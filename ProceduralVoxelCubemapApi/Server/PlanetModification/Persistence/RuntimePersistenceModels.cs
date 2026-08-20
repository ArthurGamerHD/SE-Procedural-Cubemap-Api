using System.Collections.Generic;

// Keep the original public namespace: these DTOs are persisted and may also be
// referenced by existing consumers. Their implementation file lives with the
// feature, while their CLR identity remains backward compatible.
namespace VoxelCubemapApi.Server.PlanetModification.Persistence
{
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
        public byte GrassMaterialValue;
        public int GrassCoveragePercent;
        public long PlanetSeed;
        public int GrassNoiseVersion;
        public ulong RuntimeRevision;
    }




}
