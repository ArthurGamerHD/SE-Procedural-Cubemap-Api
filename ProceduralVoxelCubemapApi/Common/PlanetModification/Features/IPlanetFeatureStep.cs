using System.Collections.Generic;
using VoxelCubemapApi.Common.Networking;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Templates;

namespace VoxelCubemapApi.Common.PlanetModification.Features
{
    internal interface IPlanetFeatureStep
    {
        void Expand(
            FeatureOperation operation,
            long planetSeed,
            List<GeneratedPlanetFeature> output);

        void WriteRuntime(
            FeatureOperation source,
            RuntimeProceduralFeatureOperation target);

        void ReadRuntime(
            RuntimeProceduralFeatureOperation source,
            FeatureOperation target);

        void WriteSynced(
            FeatureOperation source,
            SyncedFeatureOperation target);

        void ReadSynced(
            SyncedFeatureOperation source,
            FeatureOperation target);

        void Clone(
            FeatureOperation source,
            FeatureOperation target);
    }
}
