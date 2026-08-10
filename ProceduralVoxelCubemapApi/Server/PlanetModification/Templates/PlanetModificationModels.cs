using System;
using System.Collections.Generic;
using Adk.Image.Png;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VoxelCubemapApi.Server.PlanetModification.Persistence;
using VRage.Game;

namespace VoxelCubemapApi.Server.PlanetModification.Templates
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
    }


    internal sealed class PlanetModificationSnapshot
    {
        public MyPlanet TargetPlanet;
        public MyModContext SourceContext;
        public string SourceSubtype;
        public string SourceFolderName;
        public string SourceArchiveFile;
        public string CurrentProviderSubtype;
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
        public List<byte> AllocatedComplexMaterialValues;
        public string EnvironmentCarrierSubtype;
    }


 }
