using System.Collections.Generic;

namespace VoxelCubemapApi.Common.PlanetModification.Persistence
{
    public enum RuntimePlanetPersistenceType
    {
        Procedural = 1,
        PngSnapshot = 2
    }


    public class RuntimeProceduralCacheManifest
    {
        public string CacheGuid;
        public string GeneratorSignature;
        public string RecipeSignature;
    }


    public class RuntimeProceduralPlanetRecipe
    {
        public int SchemaVersion;
        public RuntimeProceduralSource Source;
        public long PlanetSeed;
        public int NoiseVersion;
        public List<RuntimeProceduralRevision> Revisions =
            new List<RuntimeProceduralRevision>();
    }


    public class RuntimeProceduralSource
    {
        public string SourceSubtype;
        public string SourceFolderName;
        public bool IsBaseGame;
        public ulong PublishedFileId;
        public string PublishedServiceName;
        public string ModName;
    }


    public class RuntimeProceduralRevision
    {
        public List<RuntimeProceduralBrushOperation> Brushes =
            new List<RuntimeProceduralBrushOperation>();

        public List<RuntimeProceduralFeatureOperation> Features =
            new List<RuntimeProceduralFeatureOperation>();

        public List<RuntimeProceduralBiomeReplacement> BiomeReplacements =
            new List<RuntimeProceduralBiomeReplacement>();

        public List<RuntimeProceduralFractalNoiseOperation> FractalNoise =
            new List<RuntimeProceduralFractalNoiseOperation>();

        public List<byte> AllocatedComplexMaterialValues =
            new List<byte>();

        public string EnvironmentPresetName;

        public List<RuntimeProceduralEnvironmentMapRule> EnvironmentRemap =
            new List<RuntimeProceduralEnvironmentMapRule>();
    }


    public class RuntimeProceduralEnvironmentMapRule
    {
        public byte MaterialMapValue;
        public byte[] CompatibleBiomes;
    }


    public class RuntimeProceduralFractalNoiseOperation
    {
        public int PlaneIndex;
        public byte TargetValue;
        public int CoveragePercent;
        public double Threshold;
    }


    public class RuntimeProceduralBiomeReplacement
    {
        public byte SourceBiome;
        public byte TargetBiome;
    }


    public class RuntimeProceduralBrushOperation
    {
        public int LayerIndex;
        public int FillValue;
        public bool UseNoise;
        public double NoiseFrequency;
        public int NoiseOctaves;
        public int NoiseSeedOffset;
        public double BlendNoiseMinimum;
        public double BlendNoiseMaximum;
        public int MinimumAltitude;
        public int MaximumAltitude;
        public double MinimumLatitude;
        public double MaximumLatitude;
        public int BiomeFilter;
        public int MaterialFilter;
        public int NoiseType;
        public int HeightBlendMode;
        public int NoiseSamplingQuality;
        public bool ScaleHeightByNoise;
        public bool UseRadial;
        public double RadialCenterX;
        public double RadialCenterY;
        public double RadialCenterZ;
        public double RadialRadiusDegrees;
        public int RadialProfile;
        public bool ScaleHeightByRadial;
    }


    public class RuntimeProceduralFeatureOperation
    {
        public List<RuntimeProceduralCraterField> CraterFields =
            new List<RuntimeProceduralCraterField>();
        public List<RuntimeProceduralVolcanoField> VolcanoFields =
            new List<RuntimeProceduralVolcanoField>();
        public List<RuntimeProceduralRavineField> RavineFields =
            new List<RuntimeProceduralRavineField>();
        public List<RuntimeProceduralRiverField> RiverFields =
            new List<RuntimeProceduralRiverField>();
    }


    public class RuntimeProceduralCraterField
    {
        public int Count;
        public int SeedOffset;
        public double MinimumRadiusDegrees;
        public double MaximumRadiusDegrees;
        public int MinimumDepth;
        public int MaximumDepth;
        public float TargetSize;
    }


    public class RuntimeProceduralVolcanoField
    {
        public int Count;
        public int SeedOffset;
        public double MinimumRadiusDegrees;
        public double MaximumRadiusDegrees;
        public int MinimumHeight;
        public int MaximumHeight;
        public float TargetSize;
    }


    public class RuntimeProceduralRavineField
    {
        public int Count;
        public int SeedOffset;
        public double MinimumLengthDegrees;
        public double MaximumLengthDegrees;
        public double MinimumWidthDegrees;
        public double MaximumWidthDegrees;
        public int MinimumDepth;
        public int MaximumDepth;
        public float TargetSize;
    }


    public class RuntimeProceduralRiverField
    {
        public int Count;
        public int SeedOffset;
        public int ShorelineHeight;
        public int MinimumSourceHeightAboveShoreline;
        public double MinimumLengthDegrees;
        public double MaximumLengthDegrees;
        public double MinimumWidthDegrees;
        public double MaximumWidthDegrees;
        public int MinimumDepth;
        public int MaximumDepth;
        public double ShoulderWidthMultiplier;
    }

    public class RuntimePlanetGeneratorSettings
    {
        public List<RuntimePlanetBuilderEntry> PlanetBuilders =
            new List<RuntimePlanetBuilderEntry>();
    }


    public class RuntimePersistenceManifest
    {
        public List<RuntimePersistencePackageEntry> Packages =
            new List<RuntimePersistencePackageEntry>();
    }


    public class RuntimePersistencePackageEntry
    {
        public string Subtype;
        public long SourceEntityId;
        public string GeneratorFile;
        public string ArchiveFile;
        public int ChunkCount;
        public int PersistenceType;
        public int RecipeSchemaVersion;
        public string RecipeVariable;
        public bool Pending;
    }


    public class RuntimePlanetBuilderEntry
    {
        public string Subtype;
        public string SourceSubtype;
        public long SourceEntityId;
        public string EnvironmentCarrierSubtype;
        public string EnvironmentPresetName;
        public string EnvironmentPresetSourceGeneratorSubtype;
        public int EnvironmentPresetSchemaVersion;
        public string GeneratorFile;
        public string ArchiveFile;
        public long PlanetSeed;
        public ulong RuntimeRevision;
        public int PersistenceType;
        public int RecipeSchemaVersion;
        public string RecipeVariable;
    }




}
