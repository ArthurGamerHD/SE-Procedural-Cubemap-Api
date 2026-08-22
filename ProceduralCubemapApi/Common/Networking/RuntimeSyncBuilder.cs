using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using ProceduralCubemapApi.Common.PlanetModification.Features;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using ProceduralCubemapApi.Common.PlanetModification.Templates;

namespace ProceduralCubemapApi.Common.Networking
{
    /// <summary>
    /// Converts the immutable server work snapshot into its wire representation.
    /// Packet construction stays separate from both commit and transport logic.
    /// </summary>
    internal static class RuntimeSyncBuilder
    {
        internal static RuntimeOperationSync BuildOperation(
            PlanetModificationSnapshot snapshot,
            RuntimePlanetBuilderEntry committedEntry)
        {
            ValidateCommon(
                snapshot,
                committedEntry);

            var fractalOperations =
                new List<SyncedFractalNoiseOperation>();

            if (snapshot.FractalNoiseOperations != null)
            {
                for (int index = 0;
                    index < snapshot.FractalNoiseOperations.Count;
                    index++)
                {
                    FractalNoiseOperation operation =
                        snapshot.FractalNoiseOperations[index];

                    if (operation == null)
                        throw new ArgumentException(
                            "Snapshot contains a null fractal-noise operation.",
                            nameof(snapshot));

                    fractalOperations.Add(
                        new SyncedFractalNoiseOperation
                        {
                            PlaneIndex = operation.PlaneIndex,
                            TargetValue = operation.TargetValue,
                            CoveragePercent = operation.CoveragePercent,
                            Threshold = operation.Threshold
                        });
                }
            }

            var biomeReplacements =
                new List<SyncedBiomeReplacementOperation>();

            if (snapshot.BiomeReplacementOperations != null)
            {
                for (int index = 0;
                    index < snapshot.BiomeReplacementOperations.Count;
                    index++)
                {
                    BiomeReplacementOperation operation =
                        snapshot.BiomeReplacementOperations[index];

                    if (operation == null)
                        throw new ArgumentException(
                            "Snapshot contains a null biome-replacement operation.",
                            nameof(snapshot));

                    biomeReplacements.Add(
                        new SyncedBiomeReplacementOperation
                        {
                            SourceBiome = operation.SourceBiome,
                            TargetBiome = operation.TargetBiome
                        });
                }
            }

            var brushes =
                new List<SyncedBrushOperation>();

            if (snapshot.BrushOperations != null)
            {
                for (int index = 0;
                    index < snapshot.BrushOperations.Count;
                    index++)
                {
                    BrushOperation operation =
                        snapshot.BrushOperations[index];

                    if (operation == null)
                        throw new ArgumentException(
                            "Snapshot contains a null brush operation.",
                            nameof(snapshot));

                    brushes.Add(
                        new SyncedBrushOperation
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
            }

            var features = new List<SyncedFeatureOperation>();
            if (snapshot.FeatureOperations != null)
            {
                for (int featureIndex = 0; featureIndex < snapshot.FeatureOperations.Count; featureIndex++)
                {
                    features.Add(FeatureStepRegistry.ToSynced(
                        snapshot.FeatureOperations[featureIndex]));
                }
            }

            return new RuntimeOperationSync
            {
                PlanetEntityId = snapshot.TargetPlanet.EntityId,
                Revision = committedEntry.RuntimeRevision,
                RuntimeSubtype = committedEntry.Subtype,
                GeneratorDefinitionXml = SerializeGenerator(snapshot),
                PlanetSeed = snapshot.PlanetSeed,
                FractalNoiseOperations = fractalOperations,
                BiomeReplacementOperations = biomeReplacements,
                BrushOperations = brushes,
                FeatureOperations = features,
                AllocatedComplexMaterialValues =
                    snapshot.AllocatedComplexMaterialValues == null
                        ? new List<byte>()
                        : new List<byte>(
                            snapshot.AllocatedComplexMaterialValues),
                GeneratorFile = committedEntry.GeneratorFile,
                ArchiveFile = committedEntry.ArchiveFile,
                SourceSubtype = committedEntry.SourceSubtype,
                EnvironmentCarrierSubtype =
                    committedEntry.EnvironmentCarrierSubtype,
                EnvironmentPresetName =
                    committedEntry.EnvironmentPresetName,
                EnvironmentPresetSourceGeneratorSubtype =
                    committedEntry.EnvironmentPresetSourceGeneratorSubtype,
                EnvironmentPresetSchemaVersion =
                    committedEntry.EnvironmentPresetSchemaVersion,
                RequiresCommitDecision = true,
                ChangeMaterials = snapshot.ChangeMaterials,
                ChangeEnvironment = snapshot.ChangeEnvironment
            };
        }


        internal static RuntimeImageSync BuildImages(
            PlanetModificationSnapshot snapshot,
            RuntimePlanetBuilderEntry committedEntry,
            IDictionary<string, byte[]> images)
        {
            ValidateCommon(
                snapshot,
                committedEntry);

            if (images == null ||
                images.Count == 0)
            {
                throw new ArgumentException(
                    "At least one authoritative image is required.",
                    nameof(images));
            }

            var syncedImages =
                new List<SyncedCubemapImage>(
                    images.Count);

            long imageBytes =
                0;

            foreach (KeyValuePair<string, byte[]> image in images)
            {
                if (string.IsNullOrWhiteSpace(image.Key) ||
                    image.Value == null ||
                    image.Value.Length == 0)
                {
                    throw new ArgumentException(
                        "Authoritative images require canonical names and PNG data.",
                        nameof(images));
                }

                imageBytes +=
                    image.Value.Length;

                if (imageBytes >
                    VoxelNetworkSession.MAX_RUNTIME_IMAGE_BYTES)
                {
                    throw new ArgumentException(
                        "Authoritative image bytes exceed the runtime payload policy.",
                        nameof(images));
                }

                syncedImages.Add(
                    new SyncedCubemapImage
                    {
                        ImageName = image.Key,
                        PngData = image.Value
                    });
            }

            return new RuntimeImageSync
            {
                PlanetEntityId = snapshot.TargetPlanet.EntityId,
                Revision = committedEntry.RuntimeRevision,
                RuntimeSubtype = committedEntry.Subtype,
                GeneratorDefinitionXml = SerializeGenerator(snapshot),
                PlanetSeed = snapshot.PlanetSeed,
                Images = syncedImages,
                GeneratorFile = committedEntry.GeneratorFile,
                ArchiveFile = committedEntry.ArchiveFile,
                SourceSubtype = committedEntry.SourceSubtype,
                EnvironmentCarrierSubtype =
                    committedEntry.EnvironmentCarrierSubtype,
                EnvironmentPresetName =
                    committedEntry.EnvironmentPresetName,
                EnvironmentPresetSourceGeneratorSubtype =
                    committedEntry.EnvironmentPresetSourceGeneratorSubtype,
                EnvironmentPresetSchemaVersion =
                    committedEntry.EnvironmentPresetSchemaVersion,
                ChangeMaterials = snapshot.ChangeMaterials,
                ChangeEnvironment = snapshot.ChangeEnvironment
            };
        }


        private static void ValidateCommon(
            PlanetModificationSnapshot snapshot,
            RuntimePlanetBuilderEntry committedEntry)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (snapshot.TargetPlanet == null)
                throw new ArgumentException(
                    "Snapshot has no target planet.",
                    nameof(snapshot));

            if (snapshot.Builder == null)
                throw new ArgumentException(
                    "Snapshot has no generator definition.",
                    nameof(snapshot));

            if (committedEntry == null)
                throw new ArgumentNullException(nameof(committedEntry));

            if (committedEntry.RuntimeRevision == 0)
                throw new ArgumentException(
                    "Committed runtime revision must be positive.",
                    nameof(committedEntry));

            if (string.IsNullOrWhiteSpace(
                committedEntry.Subtype))
            {
                throw new ArgumentException(
                    "Committed runtime subtype is required.",
                    nameof(committedEntry));
            }

            if (string.IsNullOrWhiteSpace(
                    committedEntry.GeneratorFile) ||
                string.IsNullOrWhiteSpace(
                    committedEntry.ArchiveFile))
            {
                throw new ArgumentException(
                    "Committed runtime package filenames are required.",
                    nameof(committedEntry));
            }
        }


        private static string SerializeGenerator(
            PlanetModificationSnapshot snapshot)
        {
            string xml =
                MyAPIGateway.Utilities.SerializeToXML(
                    snapshot.Builder);

            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new Exception(
                    "Could not serialize the resolved generator definition.");
            }

            return xml;
        }
    }
}
