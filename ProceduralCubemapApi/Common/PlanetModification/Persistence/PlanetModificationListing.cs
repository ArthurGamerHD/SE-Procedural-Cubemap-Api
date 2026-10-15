using System.Collections.Generic;

namespace ProceduralCubemapApi.Common.PlanetModification.Persistence
{
    /// <summary>
    /// Public, read-only view of a planet's committed modifications, in replay
    /// order. Kept separate from the persisted recipe so its format can change.
    /// </summary>
    public class PlanetModificationListing
    {
        public List<PlanetModificationInfo> Modifications =
            new List<PlanetModificationInfo>();
    }

    public class PlanetModificationInfo
    {
        public string Id;
        // Current replay position; it shifts when earlier modifications are deleted.
        public int Index;
        // Caller-defined data set with ModificationTemplate.SetCustomData.
        public string CustomData;
        public List<RuntimeProceduralLineOperation> Lines =
            new List<RuntimeProceduralLineOperation>();
        public int BrushCount;
        public int FeatureCount;
        public int BiomeReplacementCount;
        public int FractalNoiseCount;
        public string EnvironmentPresetName;
    }
}
