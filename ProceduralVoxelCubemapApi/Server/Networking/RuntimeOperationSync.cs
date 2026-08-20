using System.Collections.Generic;
using Generated;
using ProtoBuf;

namespace VoxelCubemapApi.Server.Networking
{
    /// <summary>
    /// One committed procedural modification. The final generator XML carries
    /// resolved definition changes while the operation lists preserve the exact
    /// image work and ordering executed by the authoritative server.
    /// </summary>
    [NetworkPayload(1)]
    [ProtoContract]
    internal partial class RuntimeOperationSync
    {
        [ProtoMember(1)]
        public long PlanetEntityId;

        [ProtoMember(2)]
        public ulong Revision;

        [ProtoMember(3)]
        public string RuntimeSubtype;

        [ProtoMember(4)]
        public string GeneratorDefinitionXml;

        [ProtoMember(5)]
        public long PlanetSeed;

        [ProtoMember(6)]
        public List<SyncedFractalNoiseOperation> FractalNoiseOperations;

        [ProtoMember(7)]
        public List<SyncedBiomeReplacementOperation> BiomeReplacementOperations;

        [ProtoMember(8)]
        public List<SyncedBrushOperation> BrushOperations;

        [ProtoMember(9)]
        public List<byte> AllocatedComplexMaterialValues;

        [ProtoMember(10)]
        public string GeneratorFile;

        [ProtoMember(11)]
        public string ArchiveFile;

        [ProtoMember(12)]
        public string SourceSubtype;

        [ProtoMember(13)]
        public string EnvironmentCarrierSubtype;

        [ProtoMember(14)]
        public string EnvironmentPresetName;

        [ProtoMember(15)]
        public string EnvironmentPresetSourceGeneratorSubtype;

        [ProtoMember(16)]
        public int EnvironmentPresetSchemaVersion;

        [ProtoMember(17)]
        public bool RequiresCommitDecision;
    }


    [ProtoContract]
    internal sealed class SyncedFractalNoiseOperation
    {
        [ProtoMember(1)]
        public int PlaneIndex;

        [ProtoMember(2)]
        public byte TargetValue;

        [ProtoMember(3)]
        public int CoveragePercent;

        [ProtoMember(4)]
        public double Threshold;
    }


    [ProtoContract]
    internal sealed class SyncedBiomeReplacementOperation
    {
        [ProtoMember(1)]
        public byte SourceBiome;

        [ProtoMember(2)]
        public byte TargetBiome;
    }


    [ProtoContract]
    internal sealed class SyncedBrushOperation
    {
        [ProtoMember(1)]
        public int LayerIndex;

        [ProtoMember(2)]
        public int FillValue;

        [ProtoMember(3)]
        public bool UseNoise;

        [ProtoMember(4)]
        public double NoiseFrequency;

        [ProtoMember(5)]
        public int NoiseOctaves;

        [ProtoMember(6)]
        public int NoiseSeedOffset;

        [ProtoMember(7)]
        public double BlendNoiseMinimum;

        [ProtoMember(8)]
        public double BlendNoiseMaximum;

        [ProtoMember(9)]
        public int MinimumAltitude;

        [ProtoMember(10)]
        public int MaximumAltitude;

        [ProtoMember(11)]
        public double MinimumLatitude;

        [ProtoMember(12)]
        public double MaximumLatitude;

        [ProtoMember(13)]
        public int BiomeFilter;

        [ProtoMember(14)]
        public int MaterialFilter;
    }
}
