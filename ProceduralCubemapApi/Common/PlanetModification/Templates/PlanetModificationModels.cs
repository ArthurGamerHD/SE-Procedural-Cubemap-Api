using System;
using System.Collections.Generic;
using Adk.Image.Png;
using Generated;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using VRage.Game;

namespace ProceduralCubemapApi.Common.PlanetModification.Templates
{
    internal sealed class PlanetModificationWorkResult
    {
        public MyPlanet TargetPlanet;
        public object OriginalStorage;
        public byte[] PatchedStorage;
        public MyPlanetGeneratorDefinition ReplacementGenerator;
        public RuntimePlanetBuilderEntry NewEntry;
        public string EnvironmentCarrierSubtype;
        public string OperationName;
        public NetworkPackage RuntimeSyncPacket;
        public bool StorageCommitted;
        public bool ChangeMaterials;
        public bool ChangeEnvironment;
    }


    internal sealed class FractalNoiseOperation
    {
        public int PlaneIndex;
        public byte TargetValue;
        public int CoveragePercent;
        public double Threshold;
    }


    internal sealed class BiomeReplacementOperation
    {
        public byte SourceBiome;
        public byte TargetBiome;
    }


    internal sealed class BrushOperation
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


    internal sealed class FeatureOperation
    {
        public List<CraterFieldOperation> CraterFields =
            new List<CraterFieldOperation>();
        public List<VolcanoFieldOperation> VolcanoFields =
            new List<VolcanoFieldOperation>();
        public List<RavineFieldOperation> RavineFields =
            new List<RavineFieldOperation>();
        public List<RiverFieldOperation> RiverFields =
            new List<RiverFieldOperation>();
    }


    internal sealed class CraterFieldOperation
    {
        public int Count;
        public int SeedOffset;
        public double MinimumRadiusDegrees;
        public double MaximumRadiusDegrees;
        public int MinimumDepth;
        public int MaximumDepth;
        public float TargetSize;
    }


    internal sealed class VolcanoFieldOperation
    {
        public int Count;
        public int SeedOffset;
        public double MinimumRadiusDegrees;
        public double MaximumRadiusDegrees;
        public int MinimumHeight;
        public int MaximumHeight;
        public float TargetSize;
    }


    internal sealed class RavineFieldOperation
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


    internal sealed class RiverFieldOperation
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


    internal sealed class PlanetModificationSnapshot
    {
        public MyPlanet TargetPlanet;
        public MyModContext SourceContext;
        public string SourceSubtype;
        public string SourceFolderName;
        public string SourceArchiveFile;
        public Dictionary<string, byte[]> SourceFiles;
        public string CurrentProviderSubtype;
        public ulong BaseRuntimeRevision;
        public long PlanetSeed;
        public string TemplateId;
        public MyObjectBuilder_PlanetGeneratorDefinition Builder;
        public Dictionary<string, PlanarPngBitmap> Images;
        public Dictionary<string,
            List<Action<int, int, byte[], byte[], byte[], byte[]>>>
                ImageTransforms;
        public List<FractalNoiseOperation> FractalNoiseOperations;
        public List<BiomeReplacementOperation> BiomeReplacementOperations;
        public List<BrushOperation> BrushOperations;
        public List<FeatureOperation> FeatureOperations;
        public List<byte> AllocatedComplexMaterialValues;
        public string EnvironmentCarrierSubtype;
        public string EnvironmentPresetName;
        public bool RequiresAuthoritativeImageSync;
        public bool ChangeMaterials;
        public bool ChangeEnvironment;
        public bool ProceduralPersistenceEligible;
        public RuntimeProceduralPlanetRecipe InheritedProceduralRecipe;
    }


 }
