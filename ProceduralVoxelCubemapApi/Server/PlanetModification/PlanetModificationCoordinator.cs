using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;

using VoxelCubemapApi.Server.Api;
using VoxelCubemapApi.Server.PlanetModification.Persistence;
using VoxelCubemapApi.Server.PlanetModification.Runtime;
using VoxelCubemapApi.Server.PlanetModification.Templates;
using VoxelCubemapApi.Server.PlanetModification.World;

using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;

namespace VoxelCubemapApi.Server.PlanetModification
{
    internal sealed class PlanetModificationCoordinator
    {
        private const string RuntimeGeneratorDataFolderPrefix =
            "PlanetGenerator_";

        private const string GenericGeneratorFileSuffix =
            ".generator.xml";

        private static readonly string[] MetadataMapFileNames =
        {
            "front.png",
            "back.png",
            "left.png",
            "right.png",
            "up.png",
            "down.png",
            "front_mat.png",
            "back_mat.png",
            "left_mat.png",
            "right_mat.png",
            "up_mat.png",
            "down_mat.png"
        };

        private readonly RuntimePackageStore _runtimePackages;
        private readonly PlanetDataArchiveService _planetDataArchives;
        private readonly RuntimeGeneratorRegistry _runtimeGenerators;
        private readonly PlanetStorageService _planetStorage;
        private readonly Func<bool> _isUnloading;

        private readonly Dictionary<long, List<Action<long, string>>>
            _runtimePlanetChangedCallbacks =
                new Dictionary<long, List<Action<long, string>>>();

        private readonly Dictionary<long, CachedPlanetMetadataProvider>
            _planetMetadataProviders =
                new Dictionary<long, CachedPlanetMetadataProvider>();

        private bool _requestInProgress;


        internal PlanetModificationCoordinator(
            RuntimePackageStore runtimePackages,
            PlanetDataArchiveService planetDataArchives,
            RuntimeGeneratorRegistry runtimeGenerators,
            PlanetStorageService planetStorage,
            Func<bool> isUnloading)
        {
            if (runtimePackages == null)
                throw new ArgumentNullException("runtimePackages");

            if (planetDataArchives == null)
                throw new ArgumentNullException("planetDataArchives");

            if (runtimeGenerators == null)
                throw new ArgumentNullException("runtimeGenerators");

            if (planetStorage == null)
                throw new ArgumentNullException("planetStorage");

            if (isUnloading == null)
                throw new ArgumentNullException("isUnloading");

            _runtimePackages =
                runtimePackages;

            _planetDataArchives =
                planetDataArchives;

            _runtimeGenerators =
                runtimeGenerators;

            _planetStorage =
                planetStorage;

            _isUnloading =
                isUnloading;
        }


        internal bool RequestInProgress
        {
            get { return _requestInProgress; }
        }


        internal ApiData CreateModificationTemplateApi(long planetEntityId)
        {
            if (_isUnloading())
            {
                throw new Exception(
                    "Voxel Cubemap API server is unloading.");
            }


            MyPlanet targetPlanet =
                planetEntityId == 0
                    ? PlanetLocator.FindNearestToPlayer()
                    : PlanetLocator.FindByEntityId(
                        planetEntityId);

            if (targetPlanet == null)
            {
                throw new Exception(
                    planetEntityId == 0
                        ? "Could not find a planet near the local player."
                        : "Could not find planet entity " +
                            planetEntityId +
                            ".");
            }

            if (targetPlanet.Generator == null)
            {
                throw new Exception(
                    "Target planet has no generator definition.");
            }


            long planetSeed;
            string currentProviderSubtype;

            _planetStorage.ReadProviderIdentity(
                targetPlanet,
                out planetSeed,
                out currentProviderSubtype);


            string sourceSubtype;

            MyPlanetGeneratorDefinition sourceGenerator =
                ResolveOriginalSourceGenerator(
                    targetPlanet,
                    currentProviderSubtype,
                    out sourceSubtype);

            RuntimePlanetBuilderEntry currentRuntimeEntry =
                FindRuntimeEntry(
                    currentProviderSubtype);

            string sourceArchiveFile =
                currentRuntimeEntry == null
                    ? null
                    : currentRuntimeEntry.ArchiveFile;

            MyObjectBuilder_PlanetGeneratorDefinition builder =
                currentRuntimeEntry == null
                    ? _runtimeGenerators.CaptureSourceBuilder(
                        sourceGenerator)
                    : _runtimePackages.LoadGeneratorBuilderFromWorldStorage(
                        currentRuntimeEntry.GeneratorFile);

            if (!string.IsNullOrWhiteSpace(
                builder.InheritFrom))
            {
                throw new Exception(
                    "Modification templates do not flatten inherited planet " +
                    "generator definitions yet. Source='" +
                    sourceSubtype +
                    "', InheritFrom='" +
                    builder.InheritFrom +
                    "'.");
            }


            string sourceFolderName =
                currentRuntimeEntry != null
                    ? sourceSubtype
                    :
                string.IsNullOrWhiteSpace(
                    builder.FolderName)
                    ? sourceSubtype
                    : builder.FolderName;


            var template =
                new PlanetModificationTemplate(
                    this,
                    _planetDataArchives,
                    targetPlanet,
                    sourceGenerator.Context,
                    sourceSubtype,
                    sourceFolderName,
                    sourceArchiveFile,
                    currentProviderSubtype,
                    planetSeed,
                    builder,
                    currentRuntimeEntry == null
                        ? null
                        : currentRuntimeEntry.EnvironmentCarrierSubtype);


            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Created modification template " +
                template.TemplateId +
                " for planet " +
                targetPlanet.EntityId +
                ".");


            return new PlanetModificationTemplateApi(
                template).GetApi();
        }


        internal void BeginPushModification(
            PlanetModificationTemplate template,
            Action<bool, string> callback)
        {
            if (template == null)
                throw new ArgumentNullException("template");

            if (_requestInProgress)
            {
                DispatchPushResponse(
                    callback,
                    false,
                    "Another planet modification is already running.");

                return;
            }


            PlanetModificationSnapshot snapshot;

            try
            {
                snapshot =
                    template.CreateSnapshot();
            }
            catch (Exception e)
            {
                DispatchPushResponse(
                    callback,
                    false,
                    "Could not snapshot modification template: " +
                    e.Message);

                return;
            }


            _requestInProgress =
                true;

            PlanetModificationWorkResult workResult =
                null;

            RuntimePlanetBuilderEntry pendingEntry =
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
                                PrepareModificationPush(
                                    snapshot,
                                    out pendingEntry);
                        }
                        catch (Exception e)
                        {
                            workError =
                                e;
                        }


                        MyAPIGateway.Utilities.InvokeOnGameThread(
                            delegate
                            {
                                CompleteModificationPush(
                                    workResult,
                                    workError,
                                    pendingEntry,
                                    callback);
                            });
                    });
            }
            catch (Exception e)
            {
                _requestInProgress =
                    false;

                DispatchPushResponse(
                    callback,
                    false,
                    "Could not start modification push: " +
                    e.Message);
            }
        }


        private PlanetModificationWorkResult PrepareModificationPush(
            PlanetModificationSnapshot snapshot,
            out RuntimePlanetBuilderEntry pendingEntry)
        {
            pendingEntry =
                null;

            if (snapshot == null)
                throw new ArgumentNullException("snapshot");


            string runtimeSubtype =
                "PlanetModification_" +
                snapshot.TemplateId;

            string packageStem =
                RuntimeGeneratorDataFolderPrefix +
                runtimeSubtype;

            string archiveFile =
                packageStem +
                ".zip";

            string generatorFile =
                packageStem +
                GenericGeneratorFileSuffix;


            snapshot.Builder.Id =
                new SerializableDefinitionId(
                    typeof(MyObjectBuilder_PlanetGeneratorDefinition),
                    runtimeSubtype);

            snapshot.Builder.FolderName =
                archiveFile;


            pendingEntry =
                new RuntimePlanetBuilderEntry
                {
                    Subtype = runtimeSubtype,
                    SourceSubtype = snapshot.SourceSubtype,
                    SourceEntityId = snapshot.TargetPlanet.EntityId,
                    EnvironmentCarrierSubtype = snapshot.EnvironmentCarrierSubtype,
                    GeneratorFile = generatorFile,
                    ArchiveFile = archiveFile,
                    GrassMaterialValue = 0,
                    GrassCoveragePercent = 0,
                    PlanetSeed = snapshot.PlanetSeed,
                    GrassNoiseVersion = 0
                };

            _runtimePackages.BeginPendingPersistencePackage(
                pendingEntry);


            _planetDataArchives.CreateModifiedArchive(
                snapshot,
                archiveFile);

            _runtimePackages.SaveGeneratorBuilder(
                generatorFile,
                snapshot.Builder);


            string absoluteFolder =
                _runtimeGenerators.BuildWorldStoragePath(
                    _runtimeGenerators.ResolveInitialSavePath(),
                    archiveFile);

            MyPlanetGeneratorDefinition runtimeGenerator =
                _runtimeGenerators.RegisterDefinition(
                    snapshot.Builder,
                    runtimeSubtype,
                    absoluteFolder,
                    0,
                    false);


            PlanetEnvironmentService.BindRuntimeGenerator(
                runtimeGenerator,
                snapshot.EnvironmentCarrierSubtype);


            _runtimePackages.Generators[
                runtimeSubtype] =
                runtimeGenerator;


            PlanetModificationWorkResult result =
                _planetStorage.PrepareSwap(
                    snapshot.TargetPlanet,
                    runtimeGenerator,
                    snapshot.CurrentProviderSubtype,
                    "API modification");

            result.EnvironmentCarrierSubtype =
                snapshot.EnvironmentCarrierSubtype;

            result.NewEntry =
                pendingEntry;

            return result;
        }


        private void CompleteModificationPush(
            PlanetModificationWorkResult workResult,
            Exception workError,
            RuntimePlanetBuilderEntry pendingEntry,
            Action<bool, string> callback)
        {
            bool commitAttempted =
                false;

            bool storageCommitted =
                false;

            try
            {
                if (_isUnloading())
                    return;

                if (workError != null)
                    throw workError;

                if (workResult == null)
                {
                    throw new Exception(
                        "Modification worker returned no result.");
                }


                _runtimePackages.StageRuntimePackageForCommit(
                    workResult.NewEntry);

                commitAttempted =
                    true;

                _planetStorage.Commit(
                    workResult);

                storageCommitted =
                    true;


                if (!string.IsNullOrWhiteSpace(
                    workResult.EnvironmentCarrierSubtype))
                {
                    MyPlanetGeneratorDefinition committedGenerator =
                        workResult.TargetPlanet == null
                            ? null
                            : workResult.TargetPlanet.Generator;

                    string expectedGeneratorSubtype =
                        workResult.ReplacementGenerator == null
                            ? null
                            : workResult.ReplacementGenerator.Id.SubtypeName;

                    if (committedGenerator == null ||
                        string.IsNullOrWhiteSpace(
                            expectedGeneratorSubtype) ||
                        !string.Equals(
                            committedGenerator.Id.SubtypeName,
                            expectedGeneratorSubtype,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception(
                            "Planet storage was committed, but its live generator " +
                            "identity was not updated to '" +
                            expectedGeneratorSubtype +
                            "'. The superseded runtime package was retained.");
                    }
                }

                _runtimePackages.PruneSupersededRuntimePackages(
                    workResult.NewEntry);

                InvalidatePlanetMetadataProvider(
                    workResult.TargetPlanet.EntityId,
                    workResult.NewEntry.Subtype);

                DispatchPushResponse(
                    callback,
                    true,
                    "Planet modification was committed.");
            }
            catch (Exception e)
            {
                bool providerStateResolved =
                    !commitAttempted;

                if (!storageCommitted &&
                    commitAttempted &&
                    workResult != null &&
                    workResult.NewEntry != null)
                {
                    providerStateResolved =
                        _runtimePackages.TryIsRuntimePackageLive(
                            workResult.TargetPlanet,
                            workResult.NewEntry,
                            out storageCommitted);

                    if (storageCommitted)
                    {
                        try
                        {
                            _runtimePackages.PruneSupersededRuntimePackages(
                                workResult.NewEntry);
                        }
                        catch (Exception cleanupError)
                        {
                            MyLog.Default.WriteLineAndConsole(
                                "[Voxel Cubemap API] Deferred superseded-package cleanup: " +
                                cleanupError);
                        }
                    }
                }


                if (!_isUnloading() &&
                    !storageCommitted &&
                    providerStateResolved &&
                    (pendingEntry != null ||
                        (workResult != null &&
                            workResult.NewEntry != null)))
                {
                    try
                    {
                        _runtimePackages.DiscardRuntimePackage(
                            pendingEntry ??
                            workResult.NewEntry);
                    }
                    catch (Exception cleanupError)
                    {
                        MyLog.Default.WriteLineAndConsole(
                            "[Voxel Cubemap API] Failed package cleanup also failed: " +
                            cleanupError);
                    }
                }

                else if (!storageCommitted &&
                    commitAttempted &&
                    !providerStateResolved)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Retaining staged package because " +
                        "the live provider could not be resolved safely.");
                }


                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Push failed: " +
                    e);

                DispatchPushResponse(
                    callback,
                    false,
                    e.Message);
            }
            finally
            {
                _requestInProgress =
                    false;
            }
        }


        private static void DispatchPushResponse(
            Action<bool, string> callback,
            bool success,
            string message)
        {
            if (callback == null)
                return;

            try
            {
                callback(
                    success,
                    message);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Push callback failed: " +
                    e);
            }
        }

        private RuntimePlanetBuilderEntry FindRuntimeEntry(
            string runtimeSubtype)
        {
            if (string.IsNullOrWhiteSpace(
                runtimeSubtype) ||
                _runtimePackages.Settings == null ||
                _runtimePackages.Settings.PlanetBuilders == null)
            {
                return null;
            }


            return _runtimePackages.Settings.PlanetBuilders
                .FirstOrDefault(x =>
                    x != null &&
                    x.Subtype != null &&
                    x.Subtype.Equals(
                        runtimeSubtype,
                        StringComparison.OrdinalIgnoreCase));
        }


        private bool IsPersistedRuntimeSubtype(
            string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype))
                return false;

            return _runtimePackages.Settings.PlanetBuilders.Any(x =>
                x != null &&
                x.Subtype != null &&
                x.Subtype.Equals(
                    subtype,
                    StringComparison.OrdinalIgnoreCase));
        }


        private MyPlanetGeneratorDefinition ResolveOriginalSourceGenerator(
            MyPlanet sourcePlanet,
            string currentProviderSubtype,
            out string sourceSubtype)
        {
            RuntimePlanetBuilderEntry runtimeEntry =
                FindRuntimeEntry(
                    currentProviderSubtype);


            if (runtimeEntry != null &&
                !string.IsNullOrWhiteSpace(
                    runtimeEntry.SourceSubtype))
            {
                sourceSubtype =
                    runtimeEntry.SourceSubtype;
            }
            else
            {
                sourceSubtype =
                    currentProviderSubtype;
            }


            if (IsPersistedRuntimeSubtype(
                sourceSubtype))
            {
                throw new Exception(
                    "Could not resolve the original source generator behind '" +
                    currentProviderSubtype +
                    "'.");
            }

            var a = sourceSubtype;
            MyPlanetGeneratorDefinition sourceGenerator =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x.Id.SubtypeName.Equals(
                            a,
                            StringComparison.OrdinalIgnoreCase));


            if (sourceGenerator == null)
            {
                throw new Exception(
                    "Original source generator '" +
                    sourceSubtype +
                    "' is not registered.");
            }


            return sourceGenerator;
        }


        internal ApiData GetOrCreatePlanetMetadataProvider(
            long planetEntityId,
            bool includeVanilla)
        {
            if (_isUnloading())
            {
                throw new Exception(
                    "Voxel Cubemap API server is unloading.");
            }


            MyPlanet targetPlanet =
                planetEntityId == 0
                    ? PlanetLocator.FindNearestToPlayer()
                    : PlanetLocator.FindByEntityId(
                        planetEntityId);

            if (targetPlanet == null)
            {
                throw new Exception(
                    planetEntityId == 0
                        ? "Could not find a planet near the local player."
                        : "Could not find planet entity " +
                            planetEntityId +
                            ".");
            }

            if (targetPlanet.Generator == null)
            {
                throw new Exception(
                    "Target planet has no generator definition.");
            }


            string currentProviderSubtype =
                ReadCurrentProviderSubtype(
                    targetPlanet);

            if (!includeVanilla &&
                FindRuntimeEntry(
                    currentProviderSubtype) == null)
            {
                return null;
            }


            CachedPlanetMetadataProvider staleProvider =
                null;

            lock (_planetMetadataProviders)
            {
                CachedPlanetMetadataProvider cachedProvider;

                if (_planetMetadataProviders.TryGetValue(
                        targetPlanet.EntityId,
                        out cachedProvider) &&
                    ReferenceEquals(
                        cachedProvider.Planet,
                        targetPlanet) &&
                    !cachedProvider.Snapshot.IsClosed &&
                    string.Equals(
                        cachedProvider.Snapshot.ProviderSubtype,
                        currentProviderSubtype,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return AddPlanetMetadataHandler(
                        cachedProvider);
                }

                if (cachedProvider != null)
                {
                    _planetMetadataProviders.Remove(
                        targetPlanet.EntityId);

                    staleProvider =
                        cachedProvider;
                }
            }


            if (staleProvider != null)
            {
                NotifyRuntimePlanetChanged(
                    targetPlanet.EntityId,
                    currentProviderSubtype);

                CloseCachedPlanetMetadataProvider(
                    staleProvider);
            }


            Dictionary<string, byte[]> snapshotFiles =
                CapturePlanetMetadataFiles(
                    targetPlanet,
                    currentProviderSubtype);


            var snapshot =
                new PlanetMetadataSnapshot(
                    targetPlanet.EntityId,
                    currentProviderSubtype,
                    snapshotFiles);

            var newCachedProvider =
                new CachedPlanetMetadataProvider
                {
                    Planet = targetPlanet,
                    Snapshot = snapshot
                };

            CachedPlanetMetadataProvider displacedProvider =
                null;

            ApiData handlerApi;

            lock (_planetMetadataProviders)
            {
                string verifiedProviderSubtype =
                    ReadCurrentProviderSubtype(
                        targetPlanet);

                if (!string.Equals(
                    currentProviderSubtype,
                    verifiedProviderSubtype,
                    StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.Close();

                    throw new InvalidOperationException(
                        "Planet provider changed from '" +
                        currentProviderSubtype +
                        "' to '" +
                        verifiedProviderSubtype +
                        "' while its metadata snapshot was being captured. Retry the request.");
                }


                CachedPlanetMetadataProvider cachedProvider;

                if (_planetMetadataProviders.TryGetValue(
                        targetPlanet.EntityId,
                        out cachedProvider) &&
                    ReferenceEquals(
                        cachedProvider.Planet,
                        targetPlanet) &&
                    !cachedProvider.Snapshot.IsClosed &&
                    string.Equals(
                        cachedProvider.Snapshot.ProviderSubtype,
                        currentProviderSubtype,
                        StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.Close();

                    return AddPlanetMetadataHandler(
                        cachedProvider);
                }

                if (cachedProvider != null)
                {
                    displacedProvider =
                        cachedProvider;
                }

                _planetMetadataProviders[
                    targetPlanet.EntityId] =
                    newCachedProvider;

                handlerApi =
                    AddPlanetMetadataHandler(
                        newCachedProvider);
            }


            if (displacedProvider != null)
            {
                CloseCachedPlanetMetadataProvider(
                    displacedProvider);
            }


            return handlerApi;
        }


        internal string ReadCurrentProviderSubtype(
            MyPlanet planet)
        {
            if (planet == null)
                throw new ArgumentNullException("planet");

            MyPlanet livePlanet =
                PlanetLocator.FindByEntityId(
                    planet.EntityId);

            if (livePlanet == null ||
                !ReferenceEquals(
                    livePlanet,
                    planet))
            {
                throw new Exception(
                    "Planet entity " +
                    planet.EntityId +
                    " is no longer loaded.");
            }


            long planetSeed;
            string currentProviderSubtype;

            _planetStorage.ReadProviderIdentity(
                planet,
                out planetSeed,
                out currentProviderSubtype);


            return currentProviderSubtype;
        }


        private Dictionary<string, byte[]> CapturePlanetMetadataFiles(
            MyPlanet planet,
            string currentProviderSubtype)
        {
            if (planet == null)
                throw new ArgumentNullException("planet");

            var snapshotFiles =
                new Dictionary<string, byte[]>(
                    StringComparer.OrdinalIgnoreCase);

            RuntimePlanetBuilderEntry runtimeEntry =
                FindRuntimeEntry(
                    currentProviderSubtype);

            if (runtimeEntry != null)
            {
                Dictionary<string, byte[]> runtimeFiles =
                    _planetDataArchives.ReadRuntimeArchive(
                        runtimeEntry.ArchiveFile);

                for (int index = 0;
                    index < MetadataMapFileNames.Length;
                    index++)
                {
                    string fileName =
                        MetadataMapFileNames[index];

                    byte[] runtimeFile;

                    if (!runtimeFiles.TryGetValue(
                        fileName,
                        out runtimeFile))
                    {
                        throw new Exception(
                            "Planet PNG '" +
                            fileName +
                            "' is missing from runtime archive " +
                            runtimeEntry.ArchiveFile +
                            ".");
                    }

                    snapshotFiles.Add(
                        fileName,
                        runtimeFile);
                }

                return snapshotFiles;
            }


            string sourceSubtype;

            MyPlanetGeneratorDefinition sourceGenerator =
                ResolveOriginalSourceGenerator(
                    planet,
                    currentProviderSubtype,
                    out sourceSubtype);

            string sourceFolderName =
                string.IsNullOrWhiteSpace(
                    sourceGenerator.FolderName)
                    ? sourceSubtype
                    : sourceGenerator.FolderName;


            for (int index = 0;
                index < MetadataMapFileNames.Length;
                index++)
            {
                string fileName =
                    MetadataMapFileNames[index];

                snapshotFiles.Add(
                    fileName,
                    _planetDataArchives.ReadSourceFile(
                        sourceGenerator.Context,
                        sourceSubtype,
                        sourceFolderName,
                        fileName));
            }


            return snapshotFiles;
        }


        internal void ReleasePlanetMetadataHandler(
            PlanetMetadataProvider provider)
        {
            if (provider == null)
                return;

            PlanetMetadataSnapshot snapshotToClose =
                null;

            lock (_planetMetadataProviders)
            {
                CachedPlanetMetadataProvider cachedProvider;

                if (_planetMetadataProviders.TryGetValue(
                        provider.PlanetEntityId,
                        out cachedProvider) &&
                    ReferenceEquals(
                        cachedProvider.Snapshot,
                        provider.Snapshot))
                {
                    cachedProvider.Handlers.Remove(
                        provider);

                    if (cachedProvider.Handlers.Count == 0)
                    {
                        _planetMetadataProviders.Remove(
                            provider.PlanetEntityId);

                        snapshotToClose =
                            cachedProvider.Snapshot;
                    }
                }
            }


            if (snapshotToClose != null)
            {
                snapshotToClose.Close();
            }
        }


        private ApiData AddPlanetMetadataHandler(
            CachedPlanetMetadataProvider cachedProvider)
        {
            var handler =
                new PlanetMetadataProvider(
                    this,
                    cachedProvider.Snapshot);

            cachedProvider.Handlers.Add(
                handler);

            return handler.GetApi();
        }


        private static void CloseCachedPlanetMetadataProvider(
            CachedPlanetMetadataProvider cachedProvider)
        {
            PlanetMetadataProvider[] handlers =
                cachedProvider.Handlers.ToArray();

            cachedProvider.Handlers.Clear();

            for (int index = 0;
                index < handlers.Length;
                index++)
            {
                handlers[index].CloseFromCoordinator();
            }

            cachedProvider.Snapshot.Close();
        }


        internal void UpdatePlanetMetadataProviders()
        {
            CachedPlanetMetadataProvider[] cachedProviders;

            lock (_planetMetadataProviders)
            {
                cachedProviders =
                    _planetMetadataProviders.Values.ToArray();
            }


            for (int index = 0;
                index < cachedProviders.Length;
                index++)
            {
                CachedPlanetMetadataProvider cachedProvider =
                    cachedProviders[index];

                long planetEntityId =
                    cachedProvider.Snapshot.PlanetEntityId;

                MyPlanet livePlanet =
                    PlanetLocator.FindByEntityId(
                        planetEntityId);

                if (livePlanet == null ||
                    !ReferenceEquals(
                        livePlanet,
                        cachedProvider.Planet) ||
                    livePlanet.Closed ||
                    livePlanet.MarkedForClose ||
                    !livePlanet.InScene)
                {
                    InvalidatePlanetMetadataProvider(
                        planetEntityId,
                        null,
                        cachedProvider.Snapshot);

                    continue;
                }
            }
        }


        internal void ClosePlanetMetadataProviders()
        {
            CachedPlanetMetadataProvider[] cachedProviders;

            lock (_planetMetadataProviders)
            {
                cachedProviders =
                    _planetMetadataProviders.Values.ToArray();

                _planetMetadataProviders.Clear();
            }


            for (int index = 0;
                index < cachedProviders.Length;
                index++)
            {
                CachedPlanetMetadataProvider cachedProvider =
                    cachedProviders[index];

                NotifyRuntimePlanetChanged(
                    cachedProvider.Snapshot.PlanetEntityId,
                    null);

                CloseCachedPlanetMetadataProvider(
                    cachedProvider);
            }
        }


        internal bool SubscribeRuntimePlanetChanged(
            long planetEntityId,
            Action<long, string> callback)
        {
            if (callback == null)
                return false;


            lock (_runtimePlanetChangedCallbacks)
            {
                List<Action<long, string>> callbacks;

                if (!_runtimePlanetChangedCallbacks.TryGetValue(
                    planetEntityId,
                    out callbacks))
                {
                    callbacks =
                        new List<Action<long, string>>();

                    _runtimePlanetChangedCallbacks.Add(
                        planetEntityId,
                        callbacks);
                }

                if (callbacks.Contains(
                    callback))
                {
                    return false;
                }

                callbacks.Add(
                    callback);

                return true;
            }
        }


        internal void UnsubscribeRuntimePlanetChanged(
            long planetEntityId,
            Action<long, string> callback)
        {
            if (callback == null)
                return;


            lock (_runtimePlanetChangedCallbacks)
            {
                List<Action<long, string>> callbacks;

                if (!_runtimePlanetChangedCallbacks.TryGetValue(
                    planetEntityId,
                    out callbacks))
                {
                    return;
                }

                callbacks.Remove(
                    callback);

                if (callbacks.Count == 0)
                {
                    _runtimePlanetChangedCallbacks.Remove(
                        planetEntityId);
                }
            }
        }


        private void NotifyRuntimePlanetChanged(
            long planetEntityId,
            string runtimeSubtype)
        {
            Action<long, string>[] callbacks;

            lock (_runtimePlanetChangedCallbacks)
            {
                List<Action<long, string>> registeredCallbacks;

                if (!_runtimePlanetChangedCallbacks.TryGetValue(
                        planetEntityId,
                        out registeredCallbacks) ||
                    registeredCallbacks.Count == 0)
                {
                    return;
                }

                callbacks =
                    registeredCallbacks.ToArray();
            }


            for (int index = 0;
                index < callbacks.Length;
                index++)
            {
                try
                {
                    callbacks[index](
                        planetEntityId,
                        runtimeSubtype);
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Runtime planet change callback " +
                        "failed: " +
                        e);
                }
            }
        }


        private bool InvalidatePlanetMetadataProvider(
            long planetEntityId,
            string runtimeSubtype,
            PlanetMetadataSnapshot expectedSnapshot = null)
        {
            CachedPlanetMetadataProvider cachedProvider;

            lock (_planetMetadataProviders)
            {
                if (!_planetMetadataProviders.TryGetValue(
                    planetEntityId,
                    out cachedProvider))
                {
                    return false;
                }

                if (expectedSnapshot != null &&
                    !ReferenceEquals(
                        cachedProvider.Snapshot,
                        expectedSnapshot))
                {
                    return false;
                }

                _planetMetadataProviders.Remove(
                    planetEntityId);
            }


            NotifyRuntimePlanetChanged(
                planetEntityId,
                runtimeSubtype);

            CloseCachedPlanetMetadataProvider(
                cachedProvider);

            return true;
        }


        private sealed class CachedPlanetMetadataProvider
        {
            internal MyPlanet Planet;
            internal PlanetMetadataSnapshot Snapshot;

            internal readonly List<PlanetMetadataProvider> Handlers =
                new List<PlanetMetadataProvider>();
        }
    }
}
