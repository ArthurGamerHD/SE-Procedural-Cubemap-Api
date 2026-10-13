using System;
using System.Collections.Generic;
using Adk.Utils;
using ProceduralCubemapApi.Common.PlanetModification.Templates;
using VRage.Game;
using VRage.Utils;

namespace ProceduralCubemapApi.Common.PlanetModification.Persistence
{
    internal static class RuntimeMaterialCanonicalizer
    {
        internal sealed class Result
        {
            internal byte[] Map = Identity();
            internal int RemovedGroups;
            internal int RemovedCustomMaterials;
            internal int RemovedDefinitions { get { return RemovedGroups + RemovedCustomMaterials; } }
        }

        internal static byte[] Identity()
        {
            var map = new byte[256];
            for (int i = 0; i < map.Length; i++) map[i] = (byte)i;
            return map;
        }

        internal static Result Deduplicate(MyObjectBuilder_PlanetGeneratorDefinition builder,
            RuntimeProceduralPlanetRecipe recipe)
        {
            return DeduplicateOwnedMaterials(builder, recipe, new MyObjectBuilder_PlanetGeneratorDefinition());
        }

        internal static Result DeduplicateOwnedMaterials(MyObjectBuilder_PlanetGeneratorDefinition builder,
            RuntimeProceduralPlanetRecipe recipe, MyObjectBuilder_PlanetGeneratorDefinition original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            var result = new Result();
            var protectedIds = new bool[256];
            var originalIds = new bool[256];
            foreach (MyPlanetMaterialDefinition material in original.CustomMaterialTable ?? new MyPlanetMaterialDefinition[0])
                if (material != null) originalIds[material.Value] = true;
            foreach (MyPlanetMaterialGroup group in original.ComplexMaterials ?? new MyPlanetMaterialGroup[0])
                if (group != null) originalIds[group.Value] = true;

            if (recipe != null)
                foreach (RuntimeProceduralRevision revision in recipe.Revisions)
                {
                    if (!string.IsNullOrWhiteSpace(revision.EnvironmentPresetName) && revision.EnvironmentRemap.Count == 0)
                        for (int id = 0; id < protectedIds.Length; id++) protectedIds[id] = true;
                    // Equal definitions are not interchangeable to a byte-ID
                    // predicate: merging would broaden its selection mask.
                    foreach (RuntimeProceduralBrushOperation brush in revision.Brushes)
                        if (brush.MaterialFilter >= 0) protectedIds[brush.MaterialFilter] = true;
                    foreach (RuntimeProceduralEnvironmentMapRule rule in revision.EnvironmentRemap)
                        protectedIds[rule.MaterialMapValue] = true;
                }

            var simpleIds = new bool[256];
            var effectiveSimple = new MyPlanetMaterialDefinition[256];
            if (builder.CustomMaterialTable != null)
                foreach (MyPlanetMaterialDefinition material in builder.CustomMaterialTable)
                    if (material != null)
                    {
                        simpleIds[material.Value] = true;
                        effectiveSimple[material.Value] = material;
                    }
            var retainedSimple = new List<MyPlanetMaterialDefinition>();
            var reusableSimple = new List<MyPlanetMaterialDefinition>();
            // Prefer existing original IDs, even when their numeric ID is higher.
            for (int id = 0; id < effectiveSimple.Length; id++)
                if (originalIds[id] && !protectedIds[id] && effectiveSimple[id] != null)
                    reusableSimple.Add(effectiveSimple[id]);
            for (int id = 0; id < effectiveSimple.Length; id++)
            {
                MyPlanetMaterialDefinition material = effectiveSimple[id];
                if (material == null) continue;
                MyPlanetMaterialDefinition canonical = null;
                if (!originalIds[id] && !protectedIds[id])
                    foreach (MyPlanetMaterialDefinition candidate in reusableSimple)
                        if (SameCustomMaterial(candidate, material)) { canonical = candidate; break; }
                if (canonical != null) result.Map[id] = canonical.Value;
                else
                {
                    retainedSimple.Add(material);
                    if (!originalIds[id] && !protectedIds[id]) reusableSimple.Add(material);
                }
            }
            result.RemovedCustomMaterials = (builder.CustomMaterialTable == null ? 0 : builder.CustomMaterialTable.Length) - retainedSimple.Count;
            var effective = new MyPlanetMaterialGroup[256];
            foreach (MyPlanetMaterialGroup group in builder.ComplexMaterials ?? new MyPlanetMaterialGroup[0])
                if (group != null && !simpleIds[group.Value]) effective[group.Value] = group;

            var retained = new List<MyPlanetMaterialGroup>();
            var reusable = new List<MyPlanetMaterialGroup>();
            // Complex lookup visits candidates backwards (last-ID precedence).
            for (int id = effective.Length - 1; id >= 0; id--)
                if (originalIds[id] && !protectedIds[id] && effective[id] != null)
                    reusable.Add(effective[id]);
            var lookup = new MyObjectBuilder_PlanetGeneratorDefinition();
            // Original IDs win first, then the lowest usable API-created ID.
            for (int id = 0; id < effective.Length; id++)
            {
                MyPlanetMaterialGroup group = effective[id];
                if (group == null) continue;
                lookup.ComplexMaterials = reusable.ToArray();
                byte canonical;
                if (!originalIds[id] && !protectedIds[id] && PlanetMaterialReuse.TryFindComplex(lookup, group, out canonical))
                    result.Map[id] = canonical;
                else
                {
                    retained.Add(group);
                    if (!originalIds[id] && !protectedIds[id]) reusable.Add(group);
                }
            }
            result.RemovedGroups = (builder.ComplexMaterials == null ? 0 : builder.ComplexMaterials.Length) - retained.Count;
            if (result.RemovedDefinitions == 0) return result;
            if (result.RemovedGroups > 0) builder.ComplexMaterials = retained.ToArray();
            if (result.RemovedCustomMaterials > 0) builder.CustomMaterialTable = retainedSimple.ToArray();
            if (recipe != null) ApplyToRecipe(recipe, result.Map);
            return result;
        }

        private static bool SameCustomMaterial(MyPlanetMaterialDefinition a, MyPlanetMaterialDefinition b)
        {
            // Rules carry additional predicates; do not merge them as plain
            // entries. Ordered layered definitions must also match exactly.
            if (a.IsRule || b.IsRule || a.Material != b.Material || a.MaxDepth != b.MaxDepth) return false;
            int count = a.Layers == null ? 0 : a.Layers.Length;
            if (count != (b.Layers == null ? 0 : b.Layers.Length)) return false;
            for (int i = 0; i < count; i++)
                if (a.Layers[i].Material != b.Layers[i].Material || a.Layers[i].Depth != b.Layers[i].Depth) return false;
            return true;
        }

        internal static void ApplyToRecipe(RuntimeProceduralPlanetRecipe recipe, byte[] map)
        {
            foreach (RuntimeProceduralRevision revision in recipe.Revisions)
            {
                foreach (RuntimeProceduralBrushOperation brush in revision.Brushes)
                    if (brush.LayerIndex == 0) brush.FillValue = map[brush.FillValue];
                foreach (RuntimeProceduralLineOperation line in revision.Lines)
                    if (string.Equals(line.Layer, "Material", StringComparison.OrdinalIgnoreCase)) line.Material = map[line.Material];
                foreach (RuntimeProceduralFractalNoiseOperation noise in revision.FractalNoise)
                    if (noise.PlaneIndex == 0) noise.TargetValue = map[noise.TargetValue];
            }
        }
    }
}
