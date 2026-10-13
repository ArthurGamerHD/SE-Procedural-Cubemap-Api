using System;
using VRage.Game;

namespace ProceduralCubemapApi.Common.PlanetModification.Templates
{
    internal static class PlanetMaterialReuse
    {
        internal static bool TryFindSimple(MyObjectBuilder_PlanetGeneratorDefinition builder,
            string subtype, float maxDepth, out byte value)
        {
            value = 0;
            if (builder.CustomMaterialTable == null) return false;
            var seen = new bool[256];
            // The engine uses the last custom definition for each byte.
            for (int i = builder.CustomMaterialTable.Length - 1; i >= 0; i--)
            {
                MyPlanetMaterialDefinition material = builder.CustomMaterialTable[i];
                if (material == null || seen[material.Value]) continue;
                seen[material.Value] = true;
                if (!material.IsRule && !material.HasLayers && material.MaxDepth == maxDepth &&
                    string.Equals(material.Material, subtype, StringComparison.Ordinal))
                {
                    value = material.Value;
                    return true;
                }
            }
            return false;
        }

        internal static bool TryFindComplex(MyObjectBuilder_PlanetGeneratorDefinition builder,
            MyPlanetMaterialGroup requested, out byte value)
        {
            value = 0;
            if (builder.ComplexMaterials == null) return false;
            var seen = new bool[256];
            // Simple entries take precedence over complex groups in the engine.
            if (builder.CustomMaterialTable != null)
                foreach (MyPlanetMaterialDefinition material in builder.CustomMaterialTable)
                    if (material != null) seen[material.Value] = true;
            for (int i = builder.ComplexMaterials.Length - 1; i >= 0; i--)
            {
                MyPlanetMaterialGroup group = builder.ComplexMaterials[i];
                if (group == null || seen[group.Value]) continue;
                seen[group.Value] = true;
                if (SameRules(group, requested))
                {
                    value = group.Value;
                    return true;
                }
            }
            return false;
        }

        private static bool SameRules(MyPlanetMaterialGroup a, MyPlanetMaterialGroup b)
        {
            // Name and map Value identify the group, not its material behavior.
            // Rule and layer order matter: the engine selects the first match.
            if (a.MaterialRules == null || b.MaterialRules == null ||
                a.MaterialRules.Length != b.MaterialRules.Length) return false;
            for (int i = 0; i < a.MaterialRules.Length; i++)
            {
                MyPlanetMaterialPlacementRule x = a.MaterialRules[i], y = b.MaterialRules[i];
                if (x == null || y == null ||
                    !string.Equals(x.Material, y.Material, StringComparison.Ordinal) ||
                    x.MaxDepth != y.MaxDepth || x.Value != y.Value ||
                    x.Height.Min != y.Height.Min || x.Height.Max != y.Height.Max ||
                    x.Latitude.Min != y.Latitude.Min || x.Latitude.Max != y.Latitude.Max ||
                    x.Latitude.Mirror != y.Latitude.Mirror ||
                    x.Longitude.Min != y.Longitude.Min || x.Longitude.Max != y.Longitude.Max ||
                    x.Slope.Min != y.Slope.Min || x.Slope.Max != y.Slope.Max)
                    return false;
                int count = x.Layers == null ? 0 : x.Layers.Length;
                if (count != (y.Layers == null ? 0 : y.Layers.Length)) return false;
                for (int layer = 0; layer < count; layer++)
                    if (x.Layers[layer].Depth != y.Layers[layer].Depth ||
                        !string.Equals(x.Layers[layer].Material, y.Layers[layer].Material,
                            StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}
