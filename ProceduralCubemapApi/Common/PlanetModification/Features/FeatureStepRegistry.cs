using System.Collections.Generic;
using ProceduralCubemapApi.Common.Networking;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using ProceduralCubemapApi.Common.PlanetModification.Templates;

namespace ProceduralCubemapApi.Common.PlanetModification.Features
{
    internal static class FeatureStepRegistry
    {
        private static readonly IPlanetFeatureStep[] Steps =
        {
            CraterFeatureStep.Instance,
            VolcanoFeatureStep.Instance,
            RavineFeatureStep.Instance,
            RiverFeatureStep.Instance
        };

        internal static List<GeneratedPlanetFeature> Expand(
            List<FeatureOperation> operations,
            long planetSeed)
        {
            var result = new List<GeneratedPlanetFeature>();
            if (operations == null)
                return result;

            for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                FeatureOperation operation = operations[operationIndex];
                if (operation == null)
                    continue;

                for (int stepIndex = 0; stepIndex < Steps.Length; stepIndex++)
                    Steps[stepIndex].Expand(operation, planetSeed, result);
            }

            return result;
        }

        internal static RuntimeProceduralFeatureOperation ToRuntime(
            FeatureOperation source)
        {
            var target = new RuntimeProceduralFeatureOperation();
            if (source == null)
                return target;

            for (int stepIndex = 0; stepIndex < Steps.Length; stepIndex++)
                Steps[stepIndex].WriteRuntime(source, target);

            return target;
        }

        internal static SyncedFeatureOperation ToSynced(
            FeatureOperation source)
        {
            var target = new SyncedFeatureOperation();
            if (source == null)
                return target;

            for (int stepIndex = 0; stepIndex < Steps.Length; stepIndex++)
                Steps[stepIndex].WriteSynced(source, target);

            return target;
        }

        internal static FeatureOperation Clone(
            FeatureOperation source)
        {
            var target = new FeatureOperation();
            if (source == null)
                return target;

            for (int stepIndex = 0; stepIndex < Steps.Length; stepIndex++)
                Steps[stepIndex].Clone(source, target);

            return target;
        }

        internal static FeatureOperation FromRuntime(
            RuntimeProceduralFeatureOperation source)
        {
            var target = new FeatureOperation();
            if (source == null)
                return target;

            for (int stepIndex = 0; stepIndex < Steps.Length; stepIndex++)
                Steps[stepIndex].ReadRuntime(source, target);

            return target;
        }

        internal static FeatureOperation FromSynced(
            SyncedFeatureOperation source)
        {
            var target = new FeatureOperation();
            if (source == null)
                return target;

            for (int stepIndex = 0; stepIndex < Steps.Length; stepIndex++)
                Steps[stepIndex].ReadSynced(source, target);

            return target;
        }
    }
}
