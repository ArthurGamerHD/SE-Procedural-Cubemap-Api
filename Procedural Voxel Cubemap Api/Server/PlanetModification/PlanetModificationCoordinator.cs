using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Linq;

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

        private readonly RuntimePackageStore _runtimePackages;
        private readonly PlanetDataArchiveService _planetDataArchives;
        private readonly RuntimeGeneratorRegistry _runtimeGenerators;
        private readonly PlanetStorageService _planetStorage;
        private readonly Func<bool> _isUnloading;

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

                _runtimePackages.PruneSupersededRuntimePackages(
                    workResult.NewEntry);

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


    }
}
