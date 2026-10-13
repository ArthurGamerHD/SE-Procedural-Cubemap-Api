using System.Collections.Generic;

namespace ProceduralCubemapApi.Common.PlanetModification.Persistence
{
    // Only discard writes whose complete footprint is replaced before anything
    // can observe it. Revision boundaries and allocation metadata stay intact.
    internal static class RuntimeBrushOptimizer
    {
        internal sealed class Removal
        {
            internal int Index, OccluderIndex, RevisionIndex, BrushIndex;
            internal int LayerIndex, Material, OccluderMaterial;
        }

        private sealed class IndexedBrush
        {
            internal RuntimeProceduralBrushOperation Brush;
            internal int Index;
        }

        internal static List<Removal> Optimize(RuntimeProceduralPlanetRecipe recipe)
        {
            var removed = new List<Removal>();
            var later = new[]
            {
                new List<IndexedBrush>(), new List<IndexedBrush>(), new List<IndexedBrush>()
            };
            int index = 0;
            foreach (RuntimeProceduralRevision revision in recipe.Revisions)
                index += revision.Brushes.Count;

            for (int r = recipe.Revisions.Count - 1; r >= 0; r--)
            {
                RuntimeProceduralRevision revision = recipe.Revisions[r];
                // These execute after this revision's brushes and can consume
                // their output, or change the height used by later masks.
                if ((revision.Features != null && revision.Features.Count != 0) ||
                    !string.IsNullOrWhiteSpace(revision.EnvironmentPresetName) ||
                    revision.EnvironmentRemap.Count != 0)
                    Clear(later);
                else if (revision.BiomeReplacements.Count != 0)
                    later[1].Clear();

                for (int b = revision.Brushes.Count - 1; b >= 0; b--)
                {
                    RuntimeProceduralBrushOperation brush = revision.Brushes[b];
                    index--;
                    if (brush.LayerIndex == 3)
                    {
                        // Height writes also change subsequent altitude masks.
                        Clear(later);
                        continue;
                    }

                    if (brush.MaterialFilter >= 0) later[0].Clear();
                    if (brush.BiomeFilter >= 0) later[1].Clear();
                    if (brush.MaterialFilter >= 0 || brush.BiomeFilter >= 0)
                        continue;

                    List<IndexedBrush> candidates = later[brush.LayerIndex];
                    IndexedBrush occluder = null;
                    foreach (IndexedBrush candidate in candidates)
                    {
                        if (Covers(candidate.Brush, brush))
                        {
                            occluder = candidate;
                            break;
                        }
                    }

                    if (occluder == null)
                        candidates.Add(new IndexedBrush { Brush = brush, Index = index });
                    else
                    {
                        removed.Add(new Removal
                        {
                            Index = index, OccluderIndex = occluder.Index,
                            RevisionIndex = r, BrushIndex = b, LayerIndex = brush.LayerIndex,
                            Material = brush.FillValue, OccluderMaterial = occluder.Brush.FillValue
                        });
                        revision.Brushes.RemoveAt(b);
                    }
                }

                // Allocation validation reads the material plane BEFORE this
                // revision. Removing an older paint could expose a source value
                // which fails this check even though the final maps are equal.
                if (revision.AllocatedComplexMaterialValues.Count != 0)
                    later[0].Clear();
            }

            removed.Reverse();
            return removed;
        }

        private static void Clear(List<IndexedBrush>[] later)
        {
            foreach (List<IndexedBrush> brushes in later) brushes.Clear();
        }

        private static bool Covers(RuntimeProceduralBrushOperation later,
            RuntimeProceduralBrushOperation earlier)
        {
            int earlierMin = earlier.MinimumAltitude < 0 ? 0 : earlier.MinimumAltitude;
            int earlierMax = earlier.MaximumAltitude < 0 ? ushort.MaxValue : earlier.MaximumAltitude;
            int laterMin = later.MinimumAltitude < 0 ? 0 : later.MinimumAltitude;
            int laterMax = later.MaximumAltitude < 0 ? ushort.MaxValue : later.MaximumAltitude;
            if (laterMin > earlierMin || laterMax < earlierMax ||
                later.MinimumLatitude > earlier.MinimumLatitude ||
                later.MaximumLatitude < earlier.MaximumLatitude)
                return false;

            // Identical radial fields have identical nonzero footprints. A
            // brush without a radial restriction also covers a radial brush.
            if (later.UseRadial && (!earlier.UseRadial ||
                later.RadialCenterX != earlier.RadialCenterX ||
                later.RadialCenterY != earlier.RadialCenterY ||
                later.RadialCenterZ != earlier.RadialCenterZ ||
                later.RadialRadiusDegrees != earlier.RadialRadiusDegrees ||
                later.RadialProfile != earlier.RadialProfile))
                return false;

            if (!later.UseNoise) return true;
            // Noise scores are normalized to [0,1]; this interval is unmasked.
            if (later.BlendNoiseMinimum <= 0 && later.BlendNoiseMaximum >= 1)
                return true;
            return earlier.UseNoise &&
                later.NoiseSeedOffset == earlier.NoiseSeedOffset &&
                later.NoiseFrequency == earlier.NoiseFrequency &&
                later.NoiseOctaves == earlier.NoiseOctaves &&
                later.NoiseType == earlier.NoiseType &&
                later.NoiseSamplingQuality == earlier.NoiseSamplingQuality &&
                later.BlendNoiseMinimum <= earlier.BlendNoiseMinimum &&
                later.BlendNoiseMaximum >= earlier.BlendNoiseMaximum;
        }
    }
}
