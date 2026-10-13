using System.Collections.Generic;

namespace ProceduralCubemapApi.Common.PlanetModification.Persistence
{
    // Schema 2 uses the generator's material dictionary, not allocation history.
    internal static class RuntimeBrushReplayPlanner
    {
        internal sealed class Plan
        {
            internal List<RuntimeProceduralRevision> Revisions;
            internal List<RuntimeBrushOptimizer.Removal> SkippedBrushes;
        }

        internal static List<RuntimeBrushOptimizer.Removal> Compact(RuntimeProceduralPlanetRecipe history)
        {
            foreach (RuntimeProceduralRevision revision in history.Revisions)
                revision.AllocatedComplexMaterialValues.Clear();
            history.SchemaVersion = 2;
            List<RuntimeBrushOptimizer.Removal> removed = RuntimeBrushOptimizer.Optimize(history);
            RemoveEmptyRevisions(history);
            return removed;
        }

        internal static int RemoveEmptyRevisions(RuntimeProceduralPlanetRecipe history)
        {
            return history.Revisions.RemoveAll(r => r.Brushes.Count == 0 && r.Lines.Count == 0 &&
                r.Features.Count == 0 && r.BiomeReplacements.Count == 0 && r.FractalNoise.Count == 0 &&
                r.AllocatedComplexMaterialValues.Count == 0 && r.EnvironmentRemap.Count == 0 &&
                string.IsNullOrWhiteSpace(r.EnvironmentPresetName));
        }

        internal static Plan Create(RuntimeProceduralPlanetRecipe history)
        {
            var replay = new RuntimeProceduralPlanetRecipe();
            foreach (RuntimeProceduralRevision revision in history.Revisions)
                replay.Revisions.Add(new RuntimeProceduralRevision
                {
                    Brushes = new List<RuntimeProceduralBrushOperation>(revision.Brushes),
                    Lines = revision.Lines, Features = revision.Features,
                    BiomeReplacements = revision.BiomeReplacements,
                    FractalNoise = revision.FractalNoise,
                    EnvironmentPresetName = revision.EnvironmentPresetName,
                    EnvironmentRemap = revision.EnvironmentRemap
                });
            return new Plan { Revisions = replay.Revisions, SkippedBrushes = RuntimeBrushOptimizer.Optimize(replay) };
        }
    }
}
