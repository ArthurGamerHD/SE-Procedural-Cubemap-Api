using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VoxelCubemapApi.Common.Api;
using VoxelCubemapApi.Common.Networking;
using VoxelCubemapApi.Common.PlanetModification.EnvironmentPresets;
using VoxelCubemapApi.Common.PlanetModification.Features;
using VoxelCubemapApi.Common.PlanetModification.Maps;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Runtime;
using VoxelCubemapApi.Common.PlanetModification.Templates;
using VoxelCubemapApi.Common.PlanetModification.World;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;
using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;
using RuntimeImageSync = VoxelCubemapApi.Common.Networking.RuntimeImageSync;
using RuntimeOperationSync = VoxelCubemapApi.Common.Networking.RuntimeOperationSync;
using RuntimeRevisionDecision = VoxelCubemapApi.Common.Networking.RuntimeRevisionDecision;

namespace VoxelCubemapApi.Common.PlanetModification
{
    internal sealed class PlanetModificationCoordinator
    {
        private const string GENERIC_GENERATOR_FILE_SUFFIX =
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
        private readonly EnvironmentPresetCatalog _environmentPresetCatalog;
        private readonly VoxelNetworkSession _network;
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
            EnvironmentPresetCatalog environmentPresetCatalog,
            VoxelNetworkSession network,
            Func<bool> isUnloading)
        {
            if (runtimePackages == null)
                throw new ArgumentNullException(nameof(runtimePackages));

            if (planetDataArchives == null)
                throw new ArgumentNullException(nameof(planetDataArchives));

            if (runtimeGenerators == null)
                throw new ArgumentNullException(nameof(runtimeGenerators));

            if (planetStorage == null)
                throw new ArgumentNullException(nameof(planetStorage));

            if (environmentPresetCatalog == null)
                throw new ArgumentNullException(nameof(environmentPresetCatalog));

            if (network == null)
                throw new ArgumentNullException(nameof(network));

            if (isUnloading == null)
                throw new ArgumentNullException(nameof(isUnloading));

            _runtimePackages =
                runtimePackages;

            _planetDataArchives =
                planetDataArchives;

            _runtimeGenerators =
                runtimeGenerators;

            _planetStorage =
                planetStorage;

            _environmentPresetCatalog =
                environmentPresetCatalog;

            _network =
                network;

            _isUnloading =
                isUnloading;
        }


        internal bool RequestInProgress => _requestInProgress;


        internal string[] GetApiPlanetDetails()
        {
            if (_runtimePackages.Settings == null ||
                _runtimePackages.Settings.PlanetBuilders == null)
            {
                return new string[0];
            }

            List<RuntimePlanetBuilderEntry> entries =
                _runtimePackages.Settings.PlanetBuilders
                    .Where(x => x != null)
                    .OrderBy(x => x.SourceEntityId)
                    .ThenBy(x => x.RuntimeRevision)
                    .ToList();

            var details =
                new string[entries.Count];

            for (int index = 0;
                index < entries.Count;
                index++)
            {
                details[index] =
                    BuildApiPlanetDetails(
                        entries[index]);
            }

            return details;
        }


        private string BuildApiPlanetDetails(
            RuntimePlanetBuilderEntry entry)
        {
            RuntimePlanetPersistenceType persistenceType =
                RuntimePackageStore.GetPersistenceType(
                    entry);

            MyPlanet planet =
                PlanetLocator.FindByEntityId(
                    entry.SourceEntityId);

            string liveProviderSubtype =
                null;

            string liveProviderError =
                null;

            if (planet != null)
            {
                try
                {
                    liveProviderSubtype =
                        ReadCurrentProviderSubtype(
                            planet);
                }
                catch (Exception e)
                {
                    liveProviderError =
                        e.Message;
                }
            }

            bool isLive =
                !string.IsNullOrWhiteSpace(liveProviderSubtype) &&
                string.Equals(
                    liveProviderSubtype,
                    entry.Subtype,
                    StringComparison.OrdinalIgnoreCase);

            RuntimePersistencePackageEntry package =
                _runtimePackages.FindPersistenceManifestPackage(
                    entry.ArchiveFile);

            var report =
                new StringBuilder();

            report.Append("Planet entity=")
                .Append(entry.SourceEntityId)
                .Append(", storage='")
                .Append(planet == null ? "<missing>" : planet.StorageName)
                .Append("', live=")
                .Append(isLive)
                .AppendLine();

            report.Append("  runtime subtype='")
                .Append(entry.Subtype)
                .Append("', live provider='")
                .Append(string.IsNullOrWhiteSpace(liveProviderSubtype)
                    ? "<unavailable>"
                    : liveProviderSubtype)
                .Append("', revision=")
                .Append(entry.RuntimeRevision)
                .Append(", persistence=")
                .Append(persistenceType)
                .AppendLine();

            report.Append("  source subtype='")
                .Append(entry.SourceSubtype)
                .Append("', seed=")
                .Append(entry.PlanetSeed)
                .AppendLine();

            report.Append("  generator='")
                .Append(entry.GeneratorFile)
                .Append("', runtime archive='")
                .Append(entry.ArchiveFile)
                .Append("'")
                .AppendLine();

            if (persistenceType ==
                RuntimePlanetPersistenceType.Procedural)
            {
                AppendProceduralPlanetDetails(
                    report,
                    entry);
            }
            else
            {
                report.Append("  authoritative PNG chunks=")
                    .Append(package == null ? 0 : package.ChunkCount)
                    .AppendLine();
            }

            report.Append("  environment preset='")
                .Append(string.IsNullOrWhiteSpace(entry.EnvironmentPresetName)
                    ? "<none>"
                    : entry.EnvironmentPresetName)
                .Append("', carrier='")
                .Append(string.IsNullOrWhiteSpace(entry.EnvironmentCarrierSubtype)
                    ? "<none>"
                    : entry.EnvironmentCarrierSubtype)
                .Append("', preset source='")
                .Append(string.IsNullOrWhiteSpace(
                        entry.EnvironmentPresetSourceGeneratorSubtype)
                    ? "<none>"
                    : entry.EnvironmentPresetSourceGeneratorSubtype)
                .Append("', preset schema=")
                .Append(entry.EnvironmentPresetSchemaVersion);

            if (!string.IsNullOrWhiteSpace(liveProviderError))
            {
                report.AppendLine()
                    .Append("  live provider error: ")
                    .Append(liveProviderError);
            }

            return report.ToString();
        }


        private void AppendProceduralPlanetDetails(
            StringBuilder report,
            RuntimePlanetBuilderEntry entry)
        {
            try
            {
                RuntimeProceduralPlanetRecipe recipe =
                    _runtimePackages.LoadRuntimeRecipe(
                        entry);

                int brushCount =
                    0;

                int biomeReplacementCount =
                    0;

                int fractalCount =
                    0;

                int environmentRuleCount =
                    0;

                for (int index = 0;
                    index < recipe.Revisions.Count;
                    index++)
                {
                    RuntimeProceduralRevision revision =
                        recipe.Revisions[index];

                    brushCount += revision.Brushes.Count;
                    biomeReplacementCount +=
                        revision.BiomeReplacements.Count;
                    fractalCount +=
                        revision.FractalNoise.Count;
                    environmentRuleCount +=
                        revision.EnvironmentRemap.Count;
                }

                report.Append("  recipe schema=")
                    .Append(recipe.SchemaVersion)
                    .Append(", noise version=")
                    .Append(recipe.NoiseVersion)
                    .Append(", revisions=")
                    .Append(recipe.Revisions.Count)
                    .Append(", brushes=")
                    .Append(brushCount)
                    .Append(", biome replacements=")
                    .Append(biomeReplacementCount)
                    .Append(", fractals=")
                    .Append(fractalCount)
                    .Append(", environment rules=")
                    .Append(environmentRuleCount)
                    .AppendLine();

                report.Append("  root folder='")
                    .Append(recipe.Source.SourceFolderName)
                    .Append("', base game=")
                    .Append(recipe.Source.IsBaseGame)
                    .Append(", workshop=")
                    .Append(recipe.Source.PublishedFileId)
                    .Append(", service='")
                    .Append(string.IsNullOrWhiteSpace(
                            recipe.Source.PublishedServiceName)
                        ? "<none>"
                        : recipe.Source.PublishedServiceName)
                    .Append("', mod='")
                    .Append(string.IsNullOrWhiteSpace(recipe.Source.ModName)
                        ? "<none>"
                        : recipe.Source.ModName)
                    .Append("'")
                    .AppendLine();

                report.Append("  recipe variable='")
                    .Append(entry.RecipeVariable)
                    .Append("', authoritative PNG chunks=0")
                    .AppendLine();
            }
            catch (Exception e)
            {
                report.Append("  recipe error: ")
                    .Append(e.Message)
                    .AppendLine();
            }
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

            bool proceduralPersistenceEligible =
                currentRuntimeEntry == null ||
                RuntimePackageStore.GetPersistenceType(
                    currentRuntimeEntry) ==
                    RuntimePlanetPersistenceType.Procedural;

            RuntimeProceduralPlanetRecipe inheritedProceduralRecipe =
                currentRuntimeEntry != null &&
                proceduralPersistenceEligible
                    ? _runtimePackages.LoadRuntimeRecipe(
                        currentRuntimeEntry)
                    : null;

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
                inheritedProceduralRecipe != null
                    ? inheritedProceduralRecipe.Source.SourceFolderName
                    : currentRuntimeEntry != null
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
                    currentRuntimeEntry == null
                        ? 0
                        : currentRuntimeEntry.RuntimeRevision,
                    proceduralPersistenceEligible,
                    inheritedProceduralRecipe,
                    planetSeed,
                    builder,
                    currentRuntimeEntry == null
                        ? null
                        : currentRuntimeEntry.EnvironmentCarrierSubtype,
                    _environmentPresetCatalog);


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
                throw new ArgumentNullException(nameof(template));

            if (_requestInProgress)
            {
                DispatchPushResponse(
                    callback,
                    false,
                    "Another planet modification is already running.");

                return;
            }


            try
            {
                string liveProviderSubtype =
                    ReadCurrentProviderSubtype(
                        template.TargetPlanet);

                RuntimePlanetBuilderEntry liveRuntimeEntry =
                    FindRuntimeEntry(
                        liveProviderSubtype);

                ulong liveRevision =
                    liveRuntimeEntry == null
                        ? 0
                        : liveRuntimeEntry.RuntimeRevision;

                if (!string.Equals(
                        liveProviderSubtype,
                        template.CurrentProviderSubtype,
                        StringComparison.OrdinalIgnoreCase) ||
                    liveRevision != template.BaseRuntimeRevision)
                {
                    DispatchPushResponse(
                        callback,
                        false,
                        "Planet state changed after this modification template " +
                        "was created. Create a new template and retry.");

                    return;
                }
            }
            catch (Exception e)
            {
                DispatchPushResponse(
                    callback,
                    false,
                    "Could not validate the modification base revision: " +
                    e.Message);

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

            RuntimePlanetBuilderEntry preparedEntry =
                null;

            RuntimePlanetBuilderEntry pendingEntry =
                null;

            Exception workError =
                null;

            bool recipePreparedEarly =
                false;


            if (!snapshot.RequiresAuthoritativeImageSync)
            {
                try
                {
                    preparedEntry =
                        CreatePendingRuntimeEntry(
                            snapshot);

                    PlanetDataArchiveService.ResolveFractalThresholds(
                        snapshot);

                    _network.BroadcastToConnectedPlayers(
                        RuntimeSyncBuilder.BuildOperation(
                            snapshot,
                            preparedEntry));

                    recipePreparedEarly =
                        true;
                }
                catch (Exception e)
                {
                    _requestInProgress =
                        false;

                    DispatchPushResponse(
                        callback,
                        false,
                        "Could not start synchronized modification push: " +
                        e.Message);

                    return;
                }
            }


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
                                    preparedEntry,
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
                                    recipePreparedEarly,
                                    callback);
                            });
                    });
            }
            catch (Exception e)
            {
                if (recipePreparedEarly)
                {
                    BroadcastRuntimeRevisionDecision(
                        preparedEntry,
                        false);
                }

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
            RuntimePlanetBuilderEntry preparedEntry,
            out RuntimePlanetBuilderEntry pendingEntry)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            pendingEntry =
                preparedEntry ??
                CreatePendingRuntimeEntry(
                    snapshot);

            string runtimeSubtype =
                pendingEntry.Subtype;

            string archiveFile =
                pendingEntry.ArchiveFile;

            string generatorFile =
                pendingEntry.GeneratorFile;

            _runtimePackages.BeginPendingPersistencePackage(
                pendingEntry);

            if (RuntimePackageStore.GetPersistenceType(pendingEntry) ==
                RuntimePlanetPersistenceType.Procedural)
            {
                RuntimeProceduralPlanetRecipe recipe =
                    BuildAccumulatedProceduralRecipe(
                        snapshot);

                byte[] archive =
                    BuildProceduralArchive(
                        recipe,
                        snapshot.Builder,
                        pendingEntry,
                        snapshot.TargetPlanet,
                        snapshot.TemplateId);

                _runtimePackages.SaveRuntimeRecipe(
                    pendingEntry,
                    recipe);

                _runtimePackages.SaveDerivedRuntimeArchive(
                    archiveFile,
                    archive);
            }
            else
            {
                _planetDataArchives.CreateModifiedArchive(
                    snapshot,
                    archiveFile);


                if (!string.IsNullOrWhiteSpace(
                    snapshot.EnvironmentPresetName))
                {
                    Dictionary<string, byte[]> archiveFiles =
                        _planetDataArchives.ReadRuntimeArchive(
                            archiveFile);

                    ApplyEnvironmentPresetRecipe(
                        snapshot,
                        pendingEntry,
                        archiveFiles);

                    _planetDataArchives.ReplaceRuntimeArchive(
                        archiveFile,
                        archiveFiles);
                }
            }

            snapshot.EnvironmentCarrierSubtype = pendingEntry.EnvironmentCarrierSubtype;

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
                    absoluteFolder);


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

            result.ChangeMaterials =
                snapshot.ChangeMaterials;

            result.ChangeEnvironment =
                snapshot.ChangeEnvironment;

            result.RuntimeSyncPacket =
                snapshot.RequiresAuthoritativeImageSync
                    ? (Generated.NetworkPackage)
                        RuntimeSyncBuilder.BuildImages(
                            snapshot,
                            pendingEntry,
                            _planetDataArchives.ReadRuntimeArchive(
                                archiveFile))
                    : null;

            return result;
        }


        internal string BuildProceduralGeneratorSignature(
            RuntimePlanetBuilderEntry entry,
            RuntimeProceduralPlanetRecipe recipe)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            MyObjectBuilder_PlanetGeneratorDefinition builder =
                _runtimePackages.LoadGeneratorBuilderFromWorldStorage(
                    entry.GeneratorFile);

            MyPlanetGeneratorDefinition sourceGenerator =
                ResolveProceduralSourceGenerator(
                    recipe.Source);

            return BuildProceduralGeneratorSignature(
                recipe,
                builder,
                sourceGenerator);
        }


        internal byte[] RebuildProceduralArchive(
            RuntimePlanetBuilderEntry entry,
            RuntimeProceduralPlanetRecipe recipe)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            MyObjectBuilder_PlanetGeneratorDefinition builder =
                _runtimePackages.LoadGeneratorBuilderFromWorldStorage(
                    entry.GeneratorFile);

            try
            {
                return BuildProceduralArchive(
                    recipe,
                    builder,
                    entry,
                    null,
                    "persisted-" + entry.Subtype);
            }
            catch (Exception e)
            {
                throw new Exception(
                    "Could not reconstruct procedural runtime package '" +
                    entry.Subtype +
                    "' for planet " +
                    entry.SourceEntityId +
                    " (recipe schema " +
                    entry.RecipeSchemaVersion +
                    "): " +
                    e.Message,
                    e);
            }
        }


        private RuntimeProceduralPlanetRecipe
            BuildAccumulatedProceduralRecipe(
                PlanetModificationSnapshot snapshot)
        {
            RuntimeProceduralPlanetRecipe recipe =
                snapshot.InheritedProceduralRecipe;

            if (recipe == null)
            {
                recipe =
                    new RuntimeProceduralPlanetRecipe
                    {
                        SchemaVersion = 1,
                        Source = CaptureProceduralSource(snapshot),
                        PlanetSeed = snapshot.PlanetSeed,
                        NoiseVersion = 1,
                        Revisions =
                            new List<RuntimeProceduralRevision>()
                    };
            }

            if (recipe.PlanetSeed != snapshot.PlanetSeed)
            {
                throw new Exception(
                    "Procedural lineage planet seed changed from " +
                    recipe.PlanetSeed +
                    " to " +
                    snapshot.PlanetSeed +
                    ".");
            }

            var revision =
                new RuntimeProceduralRevision
                {
                    EnvironmentPresetName =
                        snapshot.EnvironmentPresetName
                };

            for (int i = 0;
                i < snapshot.BrushOperations.Count;
                i++)
            {
                BrushOperation operation =
                    snapshot.BrushOperations[i];

                revision.Brushes.Add(
                    new RuntimeProceduralBrushOperation
                    {
                        LayerIndex = operation.LayerIndex,
                        FillValue = operation.FillValue,
                        UseNoise = operation.UseNoise,
                        NoiseFrequency = operation.NoiseFrequency,
                        NoiseOctaves = operation.NoiseOctaves,
                        NoiseSeedOffset = operation.NoiseSeedOffset,
                        BlendNoiseMinimum = operation.BlendNoiseMinimum,
                        BlendNoiseMaximum = operation.BlendNoiseMaximum,
                        MinimumAltitude = operation.MinimumAltitude,
                        MaximumAltitude = operation.MaximumAltitude,
                        MinimumLatitude = operation.MinimumLatitude,
                        MaximumLatitude = operation.MaximumLatitude,
                        BiomeFilter = operation.BiomeFilter,
                        MaterialFilter = operation.MaterialFilter,
                        NoiseType = operation.NoiseType,
                        HeightBlendMode = operation.HeightBlendMode,
                        NoiseSamplingQuality = operation.NoiseSamplingQuality,
                        ScaleHeightByNoise = operation.ScaleHeightByNoise,
                            UseRadial = operation.UseRadial,
                            RadialCenterX = operation.RadialCenterX,
                            RadialCenterY = operation.RadialCenterY,
                            RadialCenterZ = operation.RadialCenterZ,
                            RadialRadiusDegrees = operation.RadialRadiusDegrees,
                            RadialProfile = operation.RadialProfile,
                            ScaleHeightByRadial = operation.ScaleHeightByRadial
                    });
            }

            for (int featureIndex = 0; featureIndex < snapshot.FeatureOperations.Count; featureIndex++)
            {
                revision.Features.Add(
                    FeatureStepRegistry.ToRuntime(
                        snapshot.FeatureOperations[featureIndex]));
            }

            for (int i = 0;
                i < snapshot.BiomeReplacementOperations.Count;
                i++)
            {
                BiomeReplacementOperation operation =
                    snapshot.BiomeReplacementOperations[i];

                revision.BiomeReplacements.Add(
                    new RuntimeProceduralBiomeReplacement
                    {
                        SourceBiome = operation.SourceBiome,
                        TargetBiome = operation.TargetBiome
                    });
            }

            for (int i = 0;
                i < snapshot.FractalNoiseOperations.Count;
                i++)
            {
                FractalNoiseOperation operation =
                    snapshot.FractalNoiseOperations[i];

                revision.FractalNoise.Add(
                    new RuntimeProceduralFractalNoiseOperation
                    {
                        PlaneIndex = operation.PlaneIndex,
                        TargetValue = operation.TargetValue,
                        CoveragePercent = operation.CoveragePercent,
                        Threshold = operation.Threshold
                    });
            }

            revision.AllocatedComplexMaterialValues.AddRange(
                snapshot.AllocatedComplexMaterialValues);

            recipe.Revisions.Add(
                revision);

            RuntimePackageStore.ValidateRuntimeRecipe(
                recipe,
                null);

            return recipe;
        }


        private byte[] BuildProceduralArchive(
            RuntimeProceduralPlanetRecipe recipe,
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            RuntimePlanetBuilderEntry entry,
            MyPlanet targetPlanet,
            string templateId)
        {
            RuntimePackageStore.ValidateRuntimeRecipe(
                recipe,
                entry);

            MyPlanetGeneratorDefinition sourceGenerator =
                ResolveProceduralSourceGenerator(
                    recipe.Source);

            Dictionary<string, byte[]> sourceFiles =
                null;

            var pendingStages =
                new List<PlanetModificationSnapshot>();

            for (int revisionIndex = 0;
                revisionIndex < recipe.Revisions.Count;
                revisionIndex++)
            {
                RuntimeProceduralRevision revision =
                    recipe.Revisions[revisionIndex];

                PlanetModificationSnapshot stage =
                    CreateProceduralRevisionSnapshot(
                        recipe,
                        revision,
                        sourceGenerator,
                        builder,
                        targetPlanet,
                        templateId + "-r" + revisionIndex,
                        sourceFiles);

                pendingStages.Add(
                    stage);

                if (!string.IsNullOrWhiteSpace(
                    revision.EnvironmentPresetName))
                {
                    sourceFiles =
                        _planetDataArchives.BuildProceduralRevisionFiles(
                            pendingStages,
                            sourceFiles);

                    pendingStages.Clear();

                    if (revision.EnvironmentRemap.Count > 0)
                    {
                        EnvironmentPresetBiomeRemapper.ApplyResolved(
                            revision.EnvironmentRemap,
                            sourceFiles,
                            recipe.PlanetSeed);
                    }
                    else
                    {
                        revision.EnvironmentRemap =
                            ApplyEnvironmentPresetRecipe(
                                stage,
                                entry,
                                sourceFiles);
                    }
                }
            }

            if (pendingStages.Count == 0 &&
                sourceFiles == null)
            {
                pendingStages.Add(
                    CreateProceduralRevisionSnapshot(
                        recipe,
                        new RuntimeProceduralRevision(),
                        sourceGenerator,
                        builder,
                        targetPlanet,
                        templateId,
                        null));
            }

            if (pendingStages.Count > 0)
            {
                sourceFiles =
                    _planetDataArchives.BuildProceduralRevisionFiles(
                        pendingStages,
                        sourceFiles);
            }

            string generatorSignature =
                BuildProceduralGeneratorSignature(
                    recipe,
                    builder,
                    sourceGenerator);

            RuntimeProceduralCacheManifest cacheManifest =
                RuntimeProceduralCache.CreateManifest(
                    generatorSignature,
                    recipe);

            return _planetDataArchives.PackProceduralArchive(
                sourceFiles,
                cacheManifest);
        }


        private string BuildProceduralGeneratorSignature(
            RuntimeProceduralPlanetRecipe recipe,
            MyObjectBuilder_PlanetGeneratorDefinition runtimeBuilder,
            MyPlanetGeneratorDefinition sourceGenerator)
        {
            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            if (runtimeBuilder == null)
                throw new ArgumentNullException(nameof(runtimeBuilder));

            if (sourceGenerator == null)
                throw new ArgumentNullException(nameof(sourceGenerator));

            MyObjectBuilder_PlanetGeneratorDefinition sourceBuilder =
                _runtimeGenerators.CaptureSourceBuilder(
                    sourceGenerator);

            string sourceBuilderXml =
                MyAPIGateway.Utilities
                    .SerializeToXML(
                        sourceBuilder);

            string runtimeBuilderXml =
                MyAPIGateway.Utilities
                    .SerializeToXML(
                        runtimeBuilder);

            using (RuntimeProceduralCache.RuntimeProceduralCacheSignatureBuilder
                signature = RuntimeProceduralCache.CreateSignatureBuilder())
            {
                signature.AppendText(
                    "source-generator-definition",
                    sourceBuilderXml);

                signature.AppendText(
                    "runtime-generator-definition",
                    runtimeBuilderXml);

                foreach (var fileName in PlanetMapFileNames.All)
                {
                    byte[] sourceData =
                        _planetDataArchives.ReadSourceFile(
                            sourceGenerator.Context,
                            recipe.Source.SourceSubtype,
                            recipe.Source.SourceFolderName,
                            fileName);

                    signature.AppendBytes(
                        fileName,
                        sourceData);
                }

                return signature.Finish();
            }
        }


        private static PlanetModificationSnapshot
            CreateProceduralRevisionSnapshot(
                RuntimeProceduralPlanetRecipe recipe,
                RuntimeProceduralRevision revision,
                MyPlanetGeneratorDefinition sourceGenerator,
                MyObjectBuilder_PlanetGeneratorDefinition builder,
                MyPlanet targetPlanet,
                string templateId,
                Dictionary<string, byte[]> sourceFiles)
        {
            var snapshot =
                new PlanetModificationSnapshot
                {
                    TargetPlanet = targetPlanet,
                    SourceContext = sourceGenerator.Context,
                    SourceSubtype = recipe.Source.SourceSubtype,
                    SourceFolderName = recipe.Source.SourceFolderName,
                    SourceFiles = sourceFiles,
                    PlanetSeed = recipe.PlanetSeed,
                    TemplateId = templateId,
                    Builder = builder,
                    Images =
                        new Dictionary<string, Adk.Image.Png.PlanarPngBitmap>(
                            StringComparer.OrdinalIgnoreCase),
                    ImageTransforms =
                        new Dictionary<string,
                            List<Action<int, int, byte[], byte[], byte[], byte[]>>>(
                                StringComparer.OrdinalIgnoreCase),
                    FractalNoiseOperations =
                        new List<FractalNoiseOperation>(),
                    BiomeReplacementOperations =
                        new List<BiomeReplacementOperation>(),
                    BrushOperations =
                        new List<BrushOperation>(),
                    FeatureOperations =
                        new List<FeatureOperation>(),
                    AllocatedComplexMaterialValues =
                        new List<byte>(
                            revision.AllocatedComplexMaterialValues),
                    EnvironmentPresetName =
                        revision.EnvironmentPresetName
                };

            for (int i = 0; i < revision.FractalNoise.Count; i++)
            {
                RuntimeProceduralFractalNoiseOperation operation =
                    revision.FractalNoise[i];

                snapshot.FractalNoiseOperations.Add(
                    new FractalNoiseOperation
                    {
                        PlaneIndex = operation.PlaneIndex,
                        TargetValue = operation.TargetValue,
                        CoveragePercent = operation.CoveragePercent,
                        Threshold = operation.Threshold
                    });
            }

            for (int i = 0; i < revision.BiomeReplacements.Count; i++)
            {
                RuntimeProceduralBiomeReplacement operation =
                    revision.BiomeReplacements[i];

                snapshot.BiomeReplacementOperations.Add(
                    new BiomeReplacementOperation
                    {
                        SourceBiome = operation.SourceBiome,
                        TargetBiome = operation.TargetBiome
                    });
            }

            for (int featureIndex = 0;
                revision.Features != null && featureIndex < revision.Features.Count;
                featureIndex++)
            {
                snapshot.FeatureOperations.Add(
                    FeatureStepRegistry.FromRuntime(
                        revision.Features[featureIndex]));
            }

            for (int i = 0; i < revision.Brushes.Count; i++)
            {
                RuntimeProceduralBrushOperation operation =
                    revision.Brushes[i];

                snapshot.BrushOperations.Add(
                    new BrushOperation
                    {
                        LayerIndex = operation.LayerIndex,
                        FillValue = operation.FillValue,
                        UseNoise = operation.UseNoise,
                        NoiseFrequency = operation.NoiseFrequency,
                        NoiseOctaves = operation.NoiseOctaves,
                        NoiseSeedOffset = operation.NoiseSeedOffset,
                        BlendNoiseMinimum = operation.BlendNoiseMinimum,
                        BlendNoiseMaximum = operation.BlendNoiseMaximum,
                        MinimumAltitude = operation.MinimumAltitude,
                        MaximumAltitude = operation.MaximumAltitude,
                        MinimumLatitude = operation.MinimumLatitude,
                        MaximumLatitude = operation.MaximumLatitude,
                        BiomeFilter = operation.BiomeFilter,
                        MaterialFilter = operation.MaterialFilter,
                        NoiseType = operation.NoiseType,
                        HeightBlendMode = operation.HeightBlendMode,
                        NoiseSamplingQuality = operation.NoiseSamplingQuality,
                        ScaleHeightByNoise = operation.ScaleHeightByNoise,
                            UseRadial = operation.UseRadial,
                            RadialCenterX = operation.RadialCenterX,
                            RadialCenterY = operation.RadialCenterY,
                            RadialCenterZ = operation.RadialCenterZ,
                            RadialRadiusDegrees = operation.RadialRadiusDegrees,
                            RadialProfile = operation.RadialProfile,
                            ScaleHeightByRadial = operation.ScaleHeightByRadial
                    });
            }

            return snapshot;
        }


        private static RuntimeProceduralSource CaptureProceduralSource(
            PlanetModificationSnapshot snapshot)
        {
            if (snapshot.SourceContext == null)
            {
                throw new Exception(
                    "Procedural persistence requires a source definition context.");
            }

            return new RuntimeProceduralSource
            {
                SourceSubtype = snapshot.SourceSubtype,
                SourceFolderName = snapshot.SourceFolderName,
                IsBaseGame = snapshot.SourceContext.IsBaseGame,
                PublishedFileId =
                    snapshot.SourceContext.IsBaseGame
                        ? 0
                        : snapshot.SourceContext.ModItem.PublishedFileId,
                PublishedServiceName =
                    snapshot.SourceContext.IsBaseGame
                        ? null
                        : snapshot.SourceContext.ModItem.PublishedServiceName,
                ModName =
                    string.IsNullOrWhiteSpace(snapshot.SourceContext.ModName)
                        ? snapshot.SourceContext.ModItem.Name
                        : snapshot.SourceContext.ModName
            };
        }


        private static MyPlanetGeneratorDefinition
            ResolveProceduralSourceGenerator(
                RuntimeProceduralSource source)
        {
            List<MyPlanetGeneratorDefinition> candidates =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .Where(x =>
                        x != null &&
                        x.Context != null &&
                        x.Id.SubtypeName.Equals(
                            source.SourceSubtype,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            MyPlanetGeneratorDefinition resolved =
                candidates.FirstOrDefault(x =>
                    ProceduralSourceContextMatches(
                        source,
                        x.Context));

            if (resolved == null)
            {
                throw new Exception(
                    "Procedural root source '" +
                    source.SourceSubtype +
                    "' could not be resolved with its persisted content identity.");
            }

            return resolved;
        }


        private static bool ProceduralSourceContextMatches(
            RuntimeProceduralSource source,
            MyModContext context)
        {
            if (source.IsBaseGame)
                return context.IsBaseGame;

            if (context.IsBaseGame)
                return false;

            if (source.PublishedFileId != 0)
            {
                return context.ModItem.PublishedFileId ==
                        source.PublishedFileId &&
                    (string.IsNullOrWhiteSpace(
                            source.PublishedServiceName) ||
                     string.Equals(
                        context.ModItem.PublishedServiceName,
                        source.PublishedServiceName,
                        StringComparison.OrdinalIgnoreCase));
            }

            return !string.IsNullOrWhiteSpace(source.ModName) &&
                (string.Equals(
                     context.ModName,
                     source.ModName,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     context.ModItem.Name,
                     source.ModName,
                     StringComparison.OrdinalIgnoreCase));
        }


        private static RuntimePlanetBuilderEntry CreatePendingRuntimeEntry(
            PlanetModificationSnapshot snapshot)
        {
            string runtimeSubtype =
                "PlanetModification_" +
                snapshot.TemplateId;

            string packageStem =
                BuildRuntimePackageStem(
                    snapshot);

            string archiveFile =
                packageStem +
                ".zip";

            string generatorFile =
                packageStem +
                GENERIC_GENERATOR_FILE_SUFFIX;

            snapshot.Builder.Id =
                new SerializableDefinitionId(
                    typeof(MyObjectBuilder_PlanetGeneratorDefinition),
                    runtimeSubtype);

            snapshot.Builder.FolderName =
                archiveFile;

            bool procedural =
                snapshot.ProceduralPersistenceEligible;

            return new RuntimePlanetBuilderEntry
            {
                Subtype = runtimeSubtype,
                SourceSubtype = snapshot.SourceSubtype,
                SourceEntityId = snapshot.TargetPlanet.EntityId,
                EnvironmentCarrierSubtype = snapshot.EnvironmentCarrierSubtype,
                EnvironmentPresetName = snapshot.EnvironmentPresetName,
                EnvironmentPresetSchemaVersion =
                    string.IsNullOrWhiteSpace(snapshot.EnvironmentPresetName)
                        ? 0
                        : 1,
                GeneratorFile = generatorFile,
                ArchiveFile = archiveFile,
                PlanetSeed = snapshot.PlanetSeed,
                PersistenceType =
                    procedural
                        ? (int)RuntimePlanetPersistenceType.Procedural
                        : (int)RuntimePlanetPersistenceType.PngSnapshot,
                RecipeSchemaVersion =
                    procedural ? 1 : 0,
                RecipeVariable =
                    procedural
                        ? RuntimePackageStore.BuildRecipeVariableName(
                            archiveFile)
                        : null,
                RuntimeRevision =
                    checked(snapshot.BaseRuntimeRevision + 1)
            };
        }


        private static string BuildRuntimePackageStem(
            PlanetModificationSnapshot snapshot)
        {
            string planetIdentity =
                snapshot.TargetPlanet.StorageName;

            if (string.IsNullOrWhiteSpace(
                planetIdentity))
            {
                planetIdentity =
                    snapshot.SourceSubtype +
                    "-" +
                    snapshot.TargetPlanet.EntityId;
            }

            char[] safeIdentity =
                planetIdentity.Trim().ToCharArray();

            for (int index = 0;
                index < safeIdentity.Length;
                index++)
            {
                char value =
                    safeIdentity[index];

                bool safe =
                    (value >= 'a' && value <= 'z') ||
                    (value >= 'A' && value <= 'Z') ||
                    (value >= '0' && value <= '9') ||
                    value == '-' ||
                    value == '_';

                if (!safe)
                    safeIdentity[index] = '-';
            }

            string safePlanetIdentity =
                new string(
                    safeIdentity)
                    .Trim('-', '_');

            if (safePlanetIdentity.Length == 0)
                safePlanetIdentity = "Planet";

            return
                safePlanetIdentity +
                "-Modification_" +
                snapshot.TemplateId;
        }


        private List<RuntimeProceduralEnvironmentMapRule>
            ApplyEnvironmentPresetRecipe(
            PlanetModificationSnapshot snapshot,
            RuntimePlanetBuilderEntry pendingEntry,
            Dictionary<string, byte[]> archiveFiles)
        {
            EnvironmentPresetSnapshot preset =
                _environmentPresetCatalog.Resolve(
                    snapshot.EnvironmentPresetName);

            EnvironmentPresetTargetMap targetMap =
                EnvironmentPresetTargetMap.Build(
                    snapshot.Builder,
                    archiveFiles);

            RemappedEnvironmentPreset remapped =
                EnvironmentPresetRemapper.Remap(
                    preset,
                    targetMap);

            List<RuntimeProceduralEnvironmentMapRule> resolvedRules =
                EnvironmentPresetBiomeRemapper.BuildResolvedRules(
                    preset,
                    remapped,
                    targetMap);

            int changedBiomePixels =
                EnvironmentPresetBiomeRemapper.Apply(
                    preset,
                    remapped,
                    targetMap,
                    archiveFiles,
                    snapshot.PlanetSeed);

            MyPlanetGeneratorDefinition presetCarrier =
                RuntimeEnvironmentFactory.ResolveCarrier(
                    preset);

            snapshot.EnvironmentCarrierSubtype =
                presetCarrier.Id.SubtypeName;

            pendingEntry.EnvironmentCarrierSubtype =
                presetCarrier.Id.SubtypeName;

            pendingEntry.EnvironmentPresetSourceGeneratorSubtype =
                preset.SourceGeneratorSubtype;

            LogEnvironmentPresetReport(
                preset,
                remapped,
                changedBiomePixels);

            PlanetEnvironmentService.EnsureBiomeMapEnabled(
                snapshot.Builder);

            snapshot.Builder.EnvironmentItems =
                null;

            return resolvedRules;
        }


        private static void LogEnvironmentPresetReport(
            EnvironmentPresetSnapshot preset,
            RemappedEnvironmentPreset remapped,
            int changedBiomePixels)
        {
            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Environment preset '" +
                preset.Name +
                "': source='" +
                preset.SourceGeneratorSubtype +
                "', source mappings=" +
                preset.Mappings.Length +
                ", emitted mappings=" +
                remapped.Mappings.Count +
                ", matched materials=[" +
                string.Join(", ", remapped.MatchedMaterials.ToArray()) +
                "], missing target materials=[" +
                string.Join(", ", remapped.MissingTargetMaterials.ToArray()) +
                "], missing definitions=[" +
                string.Join(", ", remapped.MissingDefinitions.ToArray()) +
                "], remapped biome pixels=" +
                changedBiomePixels +
                ", emitted biome distribution=[" +
                string.Join(
                    ", ",
                    remapped.EmittedBiomePixels
                        .OrderBy(x => x.Key)
                        .Select(x => x.Key + "=" + x.Value)
                        .ToArray()) +
                "]" +
                ".");
        }


        private void CompleteModificationPush(
            PlanetModificationWorkResult workResult,
            Exception workError,
            RuntimePlanetBuilderEntry pendingEntry,
            bool recipePreparedEarly,
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

                try
                {
                    if (recipePreparedEarly)
                    {
                        BroadcastRuntimeRevisionDecision(
                            workResult.NewEntry,
                            true);
                    }
                    else
                    {
                        _network.BroadcastToConnectedPlayers(
                            workResult.RuntimeSyncPacket);
                    }
                }
                catch (Exception broadcastError)
                {
                    // The authoritative commit already succeeded. A transport
                    // failure must not report the committed Push as rolled back.
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Runtime sync broadcast failed after " +
                        "commit: " +
                        broadcastError);
                }

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

                if (recipePreparedEarly)
                {
                    BroadcastRuntimeRevisionDecision(
                        pendingEntry ??
                        (workResult == null
                            ? null
                            : workResult.NewEntry),
                        storageCommitted);
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


        private void BroadcastRuntimeRevisionDecision(
            RuntimePlanetBuilderEntry entry,
            bool commit)
        {
            if (entry == null)
                return;

            try
            {
                _network.BroadcastToConnectedPlayers(
                    new RuntimeRevisionDecision
                    {
                        PlanetEntityId = entry.SourceEntityId,
                        Revision = entry.RuntimeRevision,
                        Commit = commit,
                        RuntimeSubtype = entry.Subtype
                    });
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Runtime revision " +
                    (commit ? "commit" : "abort") +
                    " decision could not be broadcast: " +
                    e);
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


        internal PlanetModificationWorkResult PrepareRuntimeOperationReplay(
            RuntimeOperationSync packet)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            PlanetModificationSnapshot snapshot =
                CreateRuntimeReplaySnapshot(
                    packet.PlanetEntityId,
                    packet.Revision,
                    packet.RuntimeSubtype,
                    packet.PlanetSeed,
                    packet.GeneratorDefinitionXml,
                    packet.EnvironmentCarrierSubtype,
                    packet.EnvironmentPresetName);

            snapshot.FractalNoiseOperations =
                ConvertFractalNoiseOperations(
                    packet.FractalNoiseOperations);

            snapshot.BiomeReplacementOperations =
                ConvertBiomeReplacementOperations(
                    packet.BiomeReplacementOperations);

            snapshot.BrushOperations =
                ConvertBrushOperations(
                    packet.BrushOperations);

            snapshot.FeatureOperations =
                ConvertFeatureOperations(packet.FeatureOperations);

            snapshot.AllocatedComplexMaterialValues =
                packet.AllocatedComplexMaterialValues == null
                    ? new List<byte>()
                    : new List<byte>(
                        packet.AllocatedComplexMaterialValues);

            snapshot.ChangeMaterials =
                packet.ChangeMaterials;

            snapshot.ChangeEnvironment =
                packet.ChangeEnvironment;

            Dictionary<string, byte[]> archiveFiles;

            byte[] archive =
                _planetDataArchives.BuildModifiedArchive(
                    snapshot,
                    false,
                    out archiveFiles);

            RuntimePlanetBuilderEntry entry =
                CreateRuntimeReplayEntry(
                    packet.PlanetEntityId,
                    packet.Revision,
                    packet.RuntimeSubtype,
                    packet.GeneratorFile,
                    packet.ArchiveFile,
                    packet.SourceSubtype,
                    packet.EnvironmentCarrierSubtype,
                    packet.EnvironmentPresetName,
                    packet.EnvironmentPresetSourceGeneratorSubtype,
                    packet.EnvironmentPresetSchemaVersion,
                    packet.PlanetSeed);

            if (!string.IsNullOrWhiteSpace(
                snapshot.EnvironmentPresetName))
            {
                ApplyEnvironmentPresetRecipe(
                    snapshot,
                    entry,
                    archiveFiles);

                archive =
                    _planetDataArchives.BuildAuthoritativeImageArchive(
                        archiveFiles);
            }

            return PrepareRuntimeReplaySwap(
                snapshot,
                entry,
                packet.GeneratorDefinitionXml,
                archive);
        }


        internal PlanetModificationWorkResult PrepareRuntimeImageReplay(
            RuntimeImageSync packet)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            PlanetModificationSnapshot snapshot =
                CreateRuntimeReplaySnapshot(
                    packet.PlanetEntityId,
                    packet.Revision,
                    packet.RuntimeSubtype,
                    packet.PlanetSeed,
                    packet.GeneratorDefinitionXml,
                    packet.EnvironmentCarrierSubtype,
                    packet.EnvironmentPresetName);

            snapshot.ChangeMaterials =
                packet.ChangeMaterials;

            snapshot.ChangeEnvironment =
                packet.ChangeEnvironment;

            var images =
                new Dictionary<string, byte[]>(
                    StringComparer.OrdinalIgnoreCase);

            if (packet.Images == null)
                throw new ArgumentException(
                    "Runtime image packet has no images.",
                    nameof(packet));

            long imageBytes =
                0;

            for (int index = 0;
                index < packet.Images.Count;
                index++)
            {
                SyncedCubemapImage image =
                    packet.Images[index];

                if (image == null ||
                    image.PngData == null ||
                    image.PngData.Length == 0)
                {
                    throw new ArgumentException(
                        "Runtime image packet contains an empty image.",
                        nameof(packet));
                }

                string imageName = PlanetMapFileNames.Validate(image.ImageName);

                if (images.ContainsKey(
                    imageName))
                {
                    throw new ArgumentException(
                        "Runtime image packet contains duplicate image '" +
                        imageName +
                        "'.",
                        nameof(packet));
                }

                imageBytes +=
                    image.PngData.Length;

                if (imageBytes >
                    VoxelNetworkSession.MAX_RUNTIME_IMAGE_BYTES)
                {
                    throw new ArgumentException(
                        "Runtime image packet exceeds the image-byte policy.",
                        nameof(packet));
                }

                images.Add(
                    imageName,
                    image.PngData);
            }

            byte[] archive =
                _planetDataArchives.BuildAuthoritativeImageArchive(
                    images);

            RuntimePlanetBuilderEntry entry =
                CreateRuntimeReplayEntry(
                    packet.PlanetEntityId,
                    packet.Revision,
                    packet.RuntimeSubtype,
                    packet.GeneratorFile,
                    packet.ArchiveFile,
                    packet.SourceSubtype,
                    packet.EnvironmentCarrierSubtype,
                    packet.EnvironmentPresetName,
                    packet.EnvironmentPresetSourceGeneratorSubtype,
                    packet.EnvironmentPresetSchemaVersion,
                    packet.PlanetSeed);

            return PrepareRuntimeReplaySwap(
                snapshot,
                entry,
                packet.GeneratorDefinitionXml,
                archive);
        }


        internal void CommitRuntimeReplay(
            PlanetModificationWorkResult workResult)
        {
            if (workResult == null)
                throw new ArgumentNullException(nameof(workResult));

            if (MyAPIGateway.Session.IsServer)
            {
                throw new InvalidOperationException(
                    "Authoritative servers cannot apply client runtime replay.");
            }

            _planetStorage.Commit(
                workResult);

            workResult.StorageCommitted =
                true;

            _runtimePackages.CommitTransientRuntimePackage(
                workResult.NewEntry,
                workResult.ReplacementGenerator);

            InvalidatePlanetMetadataProvider(
                workResult.TargetPlanet.EntityId,
                workResult.NewEntry.Subtype);
        }


        internal void DiscardRuntimeReplay(
            PlanetModificationWorkResult workResult)
        {
            if (workResult == null ||
                workResult.StorageCommitted)
            {
                return;
            }

            _runtimePackages.DiscardTransientRuntimePackage(
                workResult.NewEntry);
        }


        private PlanetModificationSnapshot CreateRuntimeReplaySnapshot(
            long planetEntityId,
            ulong revision,
            string runtimeSubtype,
            long planetSeed,
            string generatorXml,
            string environmentCarrierSubtype,
            string environmentPresetName)
        {
            if (MyAPIGateway.Session.IsServer)
            {
                throw new InvalidOperationException(
                    "Runtime replay is only valid on a remote client.");
            }

            MyPlanet targetPlanet =
                PlanetLocator.FindByEntityId(
                    planetEntityId);

            if (targetPlanet == null ||
                targetPlanet.Generator == null)
            {
                throw new Exception(
                    "Runtime sync target planet " +
                    planetEntityId +
                    " is not loaded.");
            }

            long livePlanetSeed;
            string currentProviderSubtype;

            _planetStorage.ReadProviderIdentity(
                targetPlanet,
                out livePlanetSeed,
                out currentProviderSubtype);

            RuntimePlanetBuilderEntry currentEntry =
                FindRuntimeEntry(
                    currentProviderSubtype);

            ulong currentRevision =
                currentEntry == null
                    ? 0
                    : currentEntry.RuntimeRevision;

            if (currentRevision == ulong.MaxValue ||
                revision != currentRevision + 1)
            {
                throw new Exception(
                    "Runtime replay base revision changed. Local=" +
                    currentRevision +
                    ", packet=" +
                    revision +
                    ".");
            }

            if (livePlanetSeed != planetSeed)
            {
                throw new Exception(
                    "Runtime replay planet seed mismatch. Local=" +
                    livePlanetSeed +
                    ", packet=" +
                    planetSeed +
                    ".");
            }

            MyObjectBuilder_PlanetGeneratorDefinition builder =
                MyAPIGateway.Utilities
                    .SerializeFromXML<MyObjectBuilder_PlanetGeneratorDefinition>(
                        generatorXml);

            if (builder == null ||
                !string.Equals(
                    builder.Id.SubtypeName,
                    runtimeSubtype,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "Runtime sync generator XML does not match subtype '" +
                    runtimeSubtype +
                    "'.");
            }

            string sourceSubtype;

            MyPlanetGeneratorDefinition sourceGenerator =
                ResolveOriginalSourceGenerator(
                    targetPlanet,
                    currentProviderSubtype,
                    out sourceSubtype);

            string sourceFolderName =
                string.IsNullOrWhiteSpace(
                    sourceGenerator.FolderName)
                    ? sourceSubtype
                    : sourceGenerator.FolderName;

            return new PlanetModificationSnapshot
            {
                TargetPlanet = targetPlanet,
                SourceContext = sourceGenerator.Context,
                SourceSubtype = sourceSubtype,
                SourceFolderName = sourceFolderName,
                SourceArchiveFile =
                    currentEntry == null
                        ? null
                        : currentEntry.ArchiveFile,
                CurrentProviderSubtype = currentProviderSubtype,
                BaseRuntimeRevision = currentRevision,
                PlanetSeed = planetSeed,
                TemplateId = runtimeSubtype,
                Builder = builder,
                Images =
                    new Dictionary<string, Adk.Image.Png.PlanarPngBitmap>(
                        StringComparer.OrdinalIgnoreCase),
                ImageTransforms =
                    new Dictionary<string,
                        List<Action<int, int, byte[], byte[], byte[], byte[]>>>(
                            StringComparer.OrdinalIgnoreCase),
                FractalNoiseOperations =
                    new List<FractalNoiseOperation>(),
                BiomeReplacementOperations =
                    new List<BiomeReplacementOperation>(),
                BrushOperations =
                    new List<BrushOperation>(),
                FeatureOperations =
                    new List<FeatureOperation>(),
                AllocatedComplexMaterialValues =
                    new List<byte>(),
                EnvironmentCarrierSubtype =
                    environmentCarrierSubtype,
                EnvironmentPresetName =
                    environmentPresetName
            };
        }


        private PlanetModificationWorkResult PrepareRuntimeReplaySwap(
            PlanetModificationSnapshot snapshot,
            RuntimePlanetBuilderEntry entry,
            string generatorXml,
            byte[] archive)
        {
            bool staged = false;

            try
            {
                snapshot.Builder.FolderName =
                    entry.ArchiveFile;

                string normalizedGeneratorXml =
                    MyAPIGateway.Utilities.SerializeToXML(
                        snapshot.Builder);

                staged =
                    true;

                _runtimePackages.StageTransientRuntimePackage(
                    entry,
                    normalizedGeneratorXml,
                    archive);

                string currentSavePath =
                    RuntimeGeneratorRegistry.NormalizePath(
                        MyAPIGateway.Session.CurrentPath);

                if (string.IsNullOrWhiteSpace(
                    currentSavePath))
                {
                    throw new Exception(
                        "The client's active save path is unavailable during " +
                        "runtime replay.");
                }

                string absoluteFolder =
                    _runtimeGenerators.BuildWorldStoragePath(
                        currentSavePath,
                        entry.ArchiveFile);

                string expectedStorageRoot =
                    currentSavePath.TrimEnd('/') +
                    "/Storage/";

                if (!RuntimeGeneratorRegistry.NormalizePath(
                        absoluteFolder)
                    .StartsWith(
                        expectedStorageRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        "Runtime replay archive resolved outside the client's " +
                        "active save path. CurrentPath='" +
                        currentSavePath +
                        "', archive='" +
                        absoluteFolder +
                        "'.");
                }

                MyPlanetGeneratorDefinition runtimeGenerator =
                    _runtimeGenerators.RegisterDefinition(
                        snapshot.Builder,
                        entry.Subtype,
                        absoluteFolder);

                PlanetEnvironmentService.BindRuntimeGenerator(
                    runtimeGenerator,
                    entry.EnvironmentCarrierSubtype);

                PlanetModificationWorkResult result =
                    _planetStorage.PrepareSwap(
                        snapshot.TargetPlanet,
                        runtimeGenerator,
                        snapshot.CurrentProviderSubtype,
                        "runtime multiplayer replay");

                result.EnvironmentCarrierSubtype =
                    entry.EnvironmentCarrierSubtype;

                result.NewEntry =
                    entry;

                result.ChangeMaterials =
                    snapshot.ChangeMaterials;

                result.ChangeEnvironment =
                    snapshot.ChangeEnvironment;

                return result;
            }
            catch
            {
                if (staged)
                {
                    _runtimePackages.DiscardTransientRuntimePackage(
                        entry);
                }

                throw;
            }
        }


        private static RuntimePlanetBuilderEntry CreateRuntimeReplayEntry(
            long planetEntityId,
            ulong revision,
            string runtimeSubtype,
            string generatorFile,
            string archiveFile,
            string sourceSubtype,
            string environmentCarrierSubtype,
            string environmentPresetName,
            string environmentPresetSourceGeneratorSubtype,
            int environmentPresetSchemaVersion,
            long planetSeed)
        {
            return new RuntimePlanetBuilderEntry
            {
                Subtype = runtimeSubtype,
                SourceSubtype = sourceSubtype,
                SourceEntityId = planetEntityId,
                EnvironmentCarrierSubtype = environmentCarrierSubtype,
                EnvironmentPresetName = environmentPresetName,
                EnvironmentPresetSourceGeneratorSubtype =
                    environmentPresetSourceGeneratorSubtype,
                EnvironmentPresetSchemaVersion =
                    environmentPresetSchemaVersion,
                GeneratorFile = generatorFile,
                ArchiveFile = archiveFile,
                PlanetSeed = planetSeed,
                RuntimeRevision = revision
            };
        }


        private static List<FractalNoiseOperation>
            ConvertFractalNoiseOperations(
                List<SyncedFractalNoiseOperation> operations)
        {
            var result =
                new List<FractalNoiseOperation>();

            if (operations == null)
                return result;

            for (int index = 0;
                index < operations.Count;
                index++)
            {
                SyncedFractalNoiseOperation operation =
                    operations[index];

                if (operation == null)
                    throw new ArgumentException(
                        "Runtime packet contains a null fractal operation.",
                        nameof(operations));

                result.Add(
                    new FractalNoiseOperation
                    {
                        PlaneIndex = operation.PlaneIndex,
                        TargetValue = operation.TargetValue,
                        CoveragePercent = operation.CoveragePercent,
                        Threshold = operation.Threshold
                    });
            }

            return result;
        }


        private static List<BiomeReplacementOperation>
            ConvertBiomeReplacementOperations(
                List<SyncedBiomeReplacementOperation> operations)
        {
            var result =
                new List<BiomeReplacementOperation>();

            if (operations == null)
                return result;

            for (int index = 0;
                index < operations.Count;
                index++)
            {
                SyncedBiomeReplacementOperation operation =
                    operations[index];

                if (operation == null)
                    throw new ArgumentException(
                        "Runtime packet contains a null biome operation.",
                        nameof(operations));

                result.Add(
                    new BiomeReplacementOperation
                    {
                        SourceBiome = operation.SourceBiome,
                        TargetBiome = operation.TargetBiome
                    });
            }

            return result;
        }


        private static List<BrushOperation> ConvertBrushOperations(
            List<SyncedBrushOperation> operations)
        {
            var result =
                new List<BrushOperation>();

            if (operations == null)
                return result;

            for (int index = 0;
                index < operations.Count;
                index++)
            {
                SyncedBrushOperation operation =
                    operations[index];

                if (operation == null)
                    throw new ArgumentException(
                        "Runtime packet contains a null brush operation.",
                        nameof(operations));

                result.Add(
                    new BrushOperation
                    {
                        LayerIndex = operation.LayerIndex,
                        FillValue = operation.FillValue,
                        UseNoise = operation.UseNoise,
                        NoiseFrequency = operation.NoiseFrequency,
                        NoiseOctaves = operation.NoiseOctaves,
                        NoiseSeedOffset = operation.NoiseSeedOffset,
                        BlendNoiseMinimum = operation.BlendNoiseMinimum,
                        BlendNoiseMaximum = operation.BlendNoiseMaximum,
                        MinimumAltitude = operation.MinimumAltitude,
                        MaximumAltitude = operation.MaximumAltitude,
                        MinimumLatitude = operation.MinimumLatitude,
                        MaximumLatitude = operation.MaximumLatitude,
                        BiomeFilter = operation.BiomeFilter,
                        MaterialFilter = operation.MaterialFilter,
                        NoiseType = operation.NoiseType,
                        HeightBlendMode = operation.HeightBlendMode,
                        NoiseSamplingQuality = operation.NoiseSamplingQuality,
                        ScaleHeightByNoise = operation.ScaleHeightByNoise,
                            UseRadial = operation.UseRadial,
                            RadialCenterX = operation.RadialCenterX,
                            RadialCenterY = operation.RadialCenterY,
                            RadialCenterZ = operation.RadialCenterZ,
                            RadialRadiusDegrees = operation.RadialRadiusDegrees,
                            RadialProfile = operation.RadialProfile,
                            ScaleHeightByRadial = operation.ScaleHeightByRadial
                    });
            }

            return result;
        }

        private static List<FeatureOperation> ConvertFeatureOperations(
            List<SyncedFeatureOperation> operations)
        {
            var result = new List<FeatureOperation>();
            if (operations == null)
                return result;

            for (int index = 0; index < operations.Count; index++)
            {
                SyncedFeatureOperation operation = operations[index];
                if (operation != null)
                    result.Add(FeatureStepRegistry.FromSynced(operation));
            }

            return result;
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
                throw new ArgumentNullException(nameof(planet));

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
                throw new ArgumentNullException(nameof(planet));

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


            foreach (var cachedProvider in cachedProviders)
            {
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
