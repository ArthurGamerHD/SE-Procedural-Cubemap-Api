using System.Collections.Generic;
using Generated;
using ProtoBuf;

namespace VoxelCubemapApi.Common.Networking
{
    /// <summary>
    /// One committed modification whose affected cubemap images cannot be
    /// reproduced from deterministic operations alone.
    /// </summary>
    [NetworkPayload(2)]
    [ProtoContract]
    internal partial class RuntimeImageSync
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
        public List<SyncedCubemapImage> Images;

        [ProtoMember(7)]
        public string GeneratorFile;

        [ProtoMember(8)]
        public string ArchiveFile;

        [ProtoMember(9)]
        public string SourceSubtype;

        [ProtoMember(10)]
        public string EnvironmentCarrierSubtype;

        [ProtoMember(11)]
        public string EnvironmentPresetName;

        [ProtoMember(12)]
        public string EnvironmentPresetSourceGeneratorSubtype;

        [ProtoMember(13)]
        public int EnvironmentPresetSchemaVersion;

        [ProtoMember(14)]
        public bool ChangeMaterials;
    }

    [ProtoContract]
    internal sealed class SyncedCubemapImage
    {
        /// <summary>
        /// Canonical planet-map key, for example front.png or front_mat.png.
        /// </summary>
        [ProtoMember(1)]
        public string ImageName;

        [ProtoMember(2)]
        public byte[] PngData;
    }
}
