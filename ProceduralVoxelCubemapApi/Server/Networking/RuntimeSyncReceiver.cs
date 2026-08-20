using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VoxelCubemapApi.Server.PlanetModification;
using VoxelCubemapApi.Server.PlanetModification.Persistence;
using VoxelCubemapApi.Server.PlanetModification.Templates;
using VRage.Utils;

namespace VoxelCubemapApi.Server.Networking
{
    /// <summary>
    /// Orders authoritative runtime deltas and performs expensive replay away
    /// from the network callback.
    /// </summary>
    internal sealed class RuntimeSyncReceiver : IDisposable
    {
        private sealed class PendingRuntimeSync
        {
            internal long PlanetEntityId;
            internal ulong Revision;
            internal RuntimeOperationSync Operation;
            internal RuntimeImageSync Images;
        }


        private readonly object _sync =
            new object();

        private readonly PlanetModificationCoordinator _coordinator;
        private readonly Func<bool> _isUnloading;

        private readonly Dictionary<long, Queue<PendingRuntimeSync>>
            _pendingByPlanet =
                new Dictionary<long, Queue<PendingRuntimeSync>>();

        private readonly Dictionary<long, ulong> _localRevisionByPlanet =
            new Dictionary<long, ulong>();

        private readonly Dictionary<long, ulong> _lastQueuedRevisionByPlanet =
            new Dictionary<long, ulong>();

        private readonly HashSet<long> _desynchronizedPlanets =
            new HashSet<long>();

        private readonly Dictionary<long, Dictionary<string, bool>>
            _revisionDecisions =
                new Dictionary<long, Dictionary<string, bool>>();

        private PendingRuntimeSync _awaitingDecision;
        private PlanetModificationWorkResult _awaitingDecisionWorkResult;
        private Exception _awaitingDecisionError;

        private bool _workerBusy;
        private bool _disposed;


        internal static RuntimeSyncReceiver Instance { get; private set; }


        internal RuntimeSyncReceiver(
            PlanetModificationCoordinator coordinator,
            RuntimePackageStore runtimePackages,
            Func<bool> isUnloading)
        {
            if (coordinator == null)
                throw new ArgumentNullException("coordinator");

            if (runtimePackages == null)
                throw new ArgumentNullException("runtimePackages");

            if (isUnloading == null)
                throw new ArgumentNullException("isUnloading");

            if (Instance != null)
            {
                throw new InvalidOperationException(
                    "A runtime sync receiver is already registered.");
            }

            _coordinator =
                coordinator;

            _isUnloading =
                isUnloading;

            SeedRevisions(
                runtimePackages.Settings);

            Instance =
                this;
        }


        internal void Enqueue(
            RuntimeOperationSync packet)
        {
            if (packet == null ||
                !ValidateEnvelope(
                    packet.PlanetEntityId,
                    packet.Revision,
                    packet.RuntimeSubtype,
                    packet.GeneratorDefinitionXml,
                    packet.GeneratorFile,
                    packet.ArchiveFile))
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Rejected malformed runtime operation packet.");

                return;
            }

            Enqueue(
                new PendingRuntimeSync
                {
                    PlanetEntityId = packet.PlanetEntityId,
                    Revision = packet.Revision,
                    Operation = packet
                });
        }


        internal void Enqueue(
            RuntimeImageSync packet)
        {
            if (packet == null ||
                !ValidateEnvelope(
                    packet.PlanetEntityId,
                    packet.Revision,
                    packet.RuntimeSubtype,
                    packet.GeneratorDefinitionXml,
                    packet.GeneratorFile,
                    packet.ArchiveFile) ||
                packet.Images == null ||
                packet.Images.Count == 0)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Rejected malformed runtime image packet.");

                return;
            }

            Enqueue(
                new PendingRuntimeSync
                {
                    PlanetEntityId = packet.PlanetEntityId,
                    Revision = packet.Revision,
                    Images = packet
                });
        }


        internal void Enqueue(
            RuntimeRevisionDecision packet)
        {
            if (packet == null ||
                packet.PlanetEntityId == 0 ||
                packet.Revision == 0 ||
                string.IsNullOrWhiteSpace(
                    packet.RuntimeSubtype))
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Rejected malformed runtime revision decision.");

                return;
            }

            lock (_sync)
            {
                if (_disposed)
                    return;

                Dictionary<string, bool> decisions;

                if (!_revisionDecisions.TryGetValue(
                    packet.PlanetEntityId,
                    out decisions))
                {
                    decisions =
                        new Dictionary<string, bool>(
                            StringComparer.OrdinalIgnoreCase);

                    _revisionDecisions.Add(
                        packet.PlanetEntityId,
                        decisions);
                }

                decisions[BuildDecisionKey(
                    packet.Revision,
                    packet.RuntimeSubtype)] =
                    packet.Commit;
            }

            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Received runtime revision " +
                packet.Revision +
                (packet.Commit ? " commit" : " abort") +
                " decision for planet " +
                packet.PlanetEntityId +
                ".");
        }


        private void Enqueue(
            PendingRuntimeSync pending)
        {
            lock (_sync)
            {
                if (_disposed ||
                    _desynchronizedPlanets.Contains(
                        pending.PlanetEntityId))
                {
                    return;
                }

                ulong localRevision;

                if (!_localRevisionByPlanet.TryGetValue(
                    pending.PlanetEntityId,
                    out localRevision))
                {
                    localRevision =
                        0;
                }

                ulong expectedBase;

                if (!_lastQueuedRevisionByPlanet.TryGetValue(
                    pending.PlanetEntityId,
                    out expectedBase))
                {
                    expectedBase =
                        localRevision;
                }

                if (pending.Revision <= expectedBase)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Ignored stale runtime revision " +
                        pending.Revision +
                        " for planet " +
                        pending.PlanetEntityId +
                        ".");

                    return;
                }

                if (expectedBase == ulong.MaxValue ||
                    pending.Revision != expectedBase + 1)
                {
                    MarkDesynchronizedLocked(
                        pending.PlanetEntityId,
                        "expected revision " +
                        (expectedBase == ulong.MaxValue
                            ? "after ulong.MaxValue"
                            : (expectedBase + 1).ToString()) +
                        ", received " +
                        pending.Revision);

                    return;
                }

                Queue<PendingRuntimeSync> queue;

                if (!_pendingByPlanet.TryGetValue(
                    pending.PlanetEntityId,
                    out queue))
                {
                    queue =
                        new Queue<PendingRuntimeSync>();

                    _pendingByPlanet.Add(
                        pending.PlanetEntityId,
                        queue);
                }

                queue.Enqueue(
                    pending);

                _lastQueuedRevisionByPlanet[
                    pending.PlanetEntityId] =
                    pending.Revision;

                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Enqueued runtime revision " +
                    pending.Revision +
                    " for planet " +
                    pending.PlanetEntityId +
                    ".");
            }
        }


        internal void Update()
        {
            PendingRuntimeSync pending =
                null;

            PlanetModificationWorkResult waitingWorkResult =
                null;

            Exception waitingError =
                null;

            bool resumingDecision =
                false;

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                if (_awaitingDecision != null &&
                    HasRevisionDecisionLocked(
                        _awaitingDecision.PlanetEntityId,
                        _awaitingDecision.Revision,
                        _awaitingDecision.Operation.RuntimeSubtype))
                {
                    pending =
                        _awaitingDecision;

                    waitingWorkResult =
                        _awaitingDecisionWorkResult;

                    waitingError =
                        _awaitingDecisionError;

                    resumingDecision =
                        true;

                    _awaitingDecision =
                        null;

                    _awaitingDecisionWorkResult =
                        null;

                    _awaitingDecisionError =
                        null;
                }
                else if (_workerBusy)
                {
                    return;
                }

                if (pending == null)
                {
                    foreach (KeyValuePair<long, Queue<PendingRuntimeSync>> entry in
                        _pendingByPlanet)
                    {
                        if (entry.Value.Count == 0 ||
                            _desynchronizedPlanets.Contains(
                                entry.Key))
                        {
                            continue;
                        }

                        pending =
                            entry.Value.Dequeue();

                        _workerBusy =
                            true;

                        break;
                    }
                }
            }

            if (pending == null)
                return;

            if (resumingDecision)
            {
                CompleteReplay(
                    pending,
                    waitingWorkResult,
                    waitingError);
            }
            else
            {
                StartReplay(
                    pending);
            }
        }


        private void StartReplay(
            PendingRuntimeSync pending)
        {
            PlanetModificationWorkResult workResult =
                null;

            Exception workError =
                null;

            try
            {
                MyAPIGateway.Parallel.StartBackground(
                    delegate
                    {
                        try
                        {
                            workResult =
                                pending.Operation != null
                                    ? _coordinator.PrepareRuntimeOperationReplay(
                                        pending.Operation)
                                    : _coordinator.PrepareRuntimeImageReplay(
                                        pending.Images);
                        }
                        catch (Exception e)
                        {
                            workError =
                                e;
                        }

                        MyAPIGateway.Utilities.InvokeOnGameThread(
                            delegate
                            {
                                CompleteReplay(
                                    pending,
                                    workResult,
                                    workError);
                            });
                    });
            }
            catch (Exception e)
            {
                CompleteReplay(
                    pending,
                    workResult,
                    e);
            }
        }


        private void CompleteReplay(
            PendingRuntimeSync pending,
            PlanetModificationWorkResult workResult,
            Exception workError)
        {
            if (_disposed ||
                _isUnloading())
            {
                _coordinator.DiscardRuntimeReplay(
                    workResult);

                lock (_sync)
                {
                    _workerBusy =
                        false;
                }

                return;
            }

            if (pending.Operation != null &&
                pending.Operation.RequiresCommitDecision)
            {
                bool commit;

                lock (_sync)
                {
                    if (!TryTakeRevisionDecisionLocked(
                        pending.PlanetEntityId,
                        pending.Revision,
                        pending.Operation.RuntimeSubtype,
                        out commit))
                    {
                        _awaitingDecision =
                            pending;

                        _awaitingDecisionWorkResult =
                            workResult;

                        _awaitingDecisionError =
                            workError;

                        MyLog.Default.WriteLineAndConsole(
                            "[Voxel Cubemap API] Prepared runtime revision " +
                            pending.Revision +
                            " for planet " +
                            pending.PlanetEntityId +
                            "; awaiting server decision.");

                        return;
                    }
                }

                if (!commit)
                {
                    _coordinator.DiscardRuntimeReplay(
                        workResult);

                    lock (_sync)
                    {
                        ResetAbortedRevisionLocked(
                            pending.PlanetEntityId);

                        _workerBusy =
                            false;
                    }

                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Discarded aborted runtime revision " +
                        pending.Revision +
                        " for planet " +
                        pending.PlanetEntityId +
                        ".");

                    return;
                }
            }

            bool success =
                false;

            try
            {
                if (workError != null)
                    throw workError;

                _coordinator.CommitRuntimeReplay(
                    workResult);

                success =
                    true;

                lock (_sync)
                {
                    _localRevisionByPlanet[
                        pending.PlanetEntityId] =
                        pending.Revision;
                }

                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Applied runtime revision " +
                    pending.Revision +
                    " to planet " +
                    pending.PlanetEntityId +
                    ".");
            }
            catch (Exception e)
            {
                _coordinator.DiscardRuntimeReplay(
                    workResult);

                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Runtime replay failed for planet " +
                    pending.PlanetEntityId +
                    ", revision " +
                    pending.Revision +
                    ": " +
                    e);
            }
            finally
            {
                lock (_sync)
                {
                    if (!success &&
                        !_disposed)
                    {
                        MarkDesynchronizedLocked(
                            pending.PlanetEntityId,
                            "runtime replay failed");
                    }

                    _workerBusy =
                        false;
                }
            }
        }


        private bool HasRevisionDecisionLocked(
            long planetEntityId,
            ulong revision,
            string runtimeSubtype)
        {
            Dictionary<string, bool> decisions;

            return
                _revisionDecisions.TryGetValue(
                    planetEntityId,
                    out decisions) &&
                decisions.ContainsKey(
                    BuildDecisionKey(
                        revision,
                        runtimeSubtype));
        }


        private bool TryTakeRevisionDecisionLocked(
            long planetEntityId,
            ulong revision,
            string runtimeSubtype,
            out bool commit)
        {
            commit =
                false;

            Dictionary<string, bool> decisions;

            string decisionKey =
                BuildDecisionKey(
                    revision,
                    runtimeSubtype);

            if (!_revisionDecisions.TryGetValue(
                    planetEntityId,
                    out decisions) ||
                !decisions.TryGetValue(
                    decisionKey,
                    out commit))
            {
                return false;
            }

            decisions.Remove(
                decisionKey);

            if (decisions.Count == 0)
            {
                _revisionDecisions.Remove(
                    planetEntityId);
            }

            return true;
        }


        private static string BuildDecisionKey(
            ulong revision,
            string runtimeSubtype)
        {
            return
                revision +
                "|" +
                runtimeSubtype;
        }


        private void ResetAbortedRevisionLocked(
            long planetEntityId)
        {
            ulong localRevision;

            if (!_localRevisionByPlanet.TryGetValue(
                planetEntityId,
                out localRevision))
            {
                localRevision =
                    0;
            }

            _lastQueuedRevisionByPlanet[planetEntityId] =
                localRevision;

            Queue<PendingRuntimeSync> queue;

            if (_pendingByPlanet.TryGetValue(
                planetEntityId,
                out queue))
            {
                queue.Clear();
            }
        }


        private void SeedRevisions(
            RuntimePlanetGeneratorSettings settings)
        {
            if (settings == null ||
                settings.PlanetBuilders == null)
            {
                return;
            }

            for (int index = 0;
                index < settings.PlanetBuilders.Count;
                index++)
            {
                RuntimePlanetBuilderEntry entry =
                    settings.PlanetBuilders[index];

                if (entry == null)
                    continue;

                ulong revision;

                if (!_localRevisionByPlanet.TryGetValue(
                        entry.SourceEntityId,
                        out revision) ||
                    entry.RuntimeRevision > revision)
                {
                    _localRevisionByPlanet[
                        entry.SourceEntityId] =
                        entry.RuntimeRevision;

                    _lastQueuedRevisionByPlanet[
                        entry.SourceEntityId] =
                        entry.RuntimeRevision;
                }
            }
        }


        private static bool ValidateEnvelope(
            long planetEntityId,
            ulong revision,
            string runtimeSubtype,
            string generatorXml,
            string generatorFile,
            string archiveFile)
        {
            return
                planetEntityId != 0 &&
                revision != 0 &&
                !string.IsNullOrWhiteSpace(runtimeSubtype) &&
                !string.IsNullOrWhiteSpace(generatorXml) &&
                !string.IsNullOrWhiteSpace(generatorFile) &&
                !string.IsNullOrWhiteSpace(archiveFile);
        }


        private void MarkDesynchronizedLocked(
            long planetEntityId,
            string reason)
        {
            _desynchronizedPlanets.Add(
                planetEntityId);

            Queue<PendingRuntimeSync> queue;

            if (_pendingByPlanet.TryGetValue(
                planetEntityId,
                out queue))
            {
                queue.Clear();
            }

            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Planet " +
                planetEntityId +
                " requires authoritative resync: " +
                reason +
                ". Reconnect to reload current world-variable state.");
        }


        public void Dispose()
        {
            lock (_sync)
            {
                _disposed =
                    true;

                _pendingByPlanet.Clear();
                _desynchronizedPlanets.Clear();
                _revisionDecisions.Clear();

                _awaitingDecision =
                    null;

                _awaitingDecisionWorkResult =
                    null;

                _awaitingDecisionError =
                    null;

                if (ReferenceEquals(
                    Instance,
                    this))
                {
                    Instance =
                        null;
                }
            }
        }
    }
}
