using System.Collections.Generic;
using ProceduralCubemapApi.Common.Networking;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using ProceduralCubemapApi.Common.PlanetModification.Templates;

namespace ProceduralCubemapApi.Common.PlanetModification.Features
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
