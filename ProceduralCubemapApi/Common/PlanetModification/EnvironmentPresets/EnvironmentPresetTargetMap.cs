using System;
using System.Collections.Generic;
using Adk.Image.Png;
using ProceduralCubemapApi.Common.PlanetModification.Maps;
using VRage.Game;

namespace ProceduralCubemapApi.Common.PlanetModification.EnvironmentPresets
{
    internal sealed class EnvironmentPresetTargetMap
    {
        private readonly Dictionary<string, HashSet<byte>> _materialToBiomes =
            new Dictionary<string, HashSet<byte>>(
                StringComparer.OrdinalIgnoreCase);

        private Dictionary<byte, HashSet<string>> _valueToMaterials;


        internal bool TryGetBiomes(
            string materialSubtype,
            out HashSet<byte> biomes)
        {
            return _materialToBiomes.TryGetValue(
                materialSubtype,
                out biomes);
        }


        internal bool TryGetMaterials(
            byte materialMapValue,
            out HashSet<string> materialSubtypes)
        {
            return _valueToMaterials.TryGetValue(
                materialMapValue,
                out materialSubtypes);
        }


        internal static EnvironmentPresetTargetMap Build(
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            Dictionary<string, byte[]> archiveFiles)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (archiveFiles == null)
                throw new ArgumentNullException(nameof(archiveFiles));

            Dictionary<byte, HashSet<string>> valueToMaterials =
                BuildValueToMaterials(builder);

            var output =
                new EnvironmentPresetTargetMap();

            output._valueToMaterials =
                valueToMaterials;

            string[] faces =
            {
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };

            for (int faceIndex = 0;
                faceIndex < faces.Length;
                faceIndex++)
            {
                byte[] encoded;

                if (!archiveFiles.TryGetValue(
                    faces[faceIndex],
                    out encoded))
                {
                    throw new Exception(
                        "Runtime planet archive is missing material map '" +
                        faces[faceIndex] +
                        "'.");
                }

                PlanarPngBitmap image =
                    PlanetMapOperations
                        .DecodePlanetPng(
                            faces[faceIndex],
                            encoded);

                byte[] materialValues =
                    image.Planes[0];

                byte[] biomeValues =
                    image.Planes[1];

                for (int pixelIndex = 0;
                    pixelIndex < materialValues.Length;
                    pixelIndex++)
                {
                    HashSet<string> materials;

                    if (!valueToMaterials.TryGetValue(
                        materialValues[pixelIndex],
                        out materials))
                    {
                        continue;
                    }

                    foreach (string material in materials)
                    {
                        output.Add(
                            material,
                            biomeValues[pixelIndex]);
                    }
                }
            }

            return output;
        }


        private void Add(
            string materialSubtype,
            byte biome)
        {
            if (string.IsNullOrWhiteSpace(materialSubtype))
                return;

            HashSet<byte> biomes;

            if (!_materialToBiomes.TryGetValue(
                materialSubtype,
                out biomes))
            {
                biomes =
                    new HashSet<byte>();

                _materialToBiomes.Add(
                    materialSubtype,
                    biomes);
            }

            biomes.Add(
                biome);
        }


        private static Dictionary<byte, HashSet<string>> BuildValueToMaterials(
            MyObjectBuilder_PlanetGeneratorDefinition builder)
        {
            var output =
                new Dictionary<byte, HashSet<string>>();

            AddMaterialDefinition(
                output,
                builder.DefaultSurfaceMaterial);

            AddMaterialDefinition(
                output,
                builder.DefaultSubSurfaceMaterial);

            if (builder.CustomMaterialTable != null)
            {
                for (int index = 0;
                    index < builder.CustomMaterialTable.Length;
                    index++)
                {
                    AddMaterialDefinition(
                        output,
                        builder.CustomMaterialTable[index]);
                }
            }

            if (builder.ComplexMaterials != null)
            {
                for (int groupIndex = 0;
                    groupIndex < builder.ComplexMaterials.Length;
                    groupIndex++)
                {
                    MyPlanetMaterialGroup group =
                        builder.ComplexMaterials[groupIndex];

                    if (group == null ||
                        group.MaterialRules == null)
                    {
                        continue;
                    }

                    for (int ruleIndex = 0;
                        ruleIndex < group.MaterialRules.Length;
                        ruleIndex++)
                    {
                        AddMaterialDefinition(
                            output,
                            group.MaterialRules[ruleIndex],
                            group.Value);
                    }
                }
            }

            return output;
        }


        private static void AddMaterialDefinition(
            Dictionary<byte, HashSet<string>> output,
            MyPlanetMaterialDefinition definition,
            byte? mapValue = null)
        {
            if (definition == null)
                return;

            byte value =
                mapValue.GetValueOrDefault(
                    definition.Value);

            AddMaterialName(
                output,
                value,
                definition.Material);

            if (definition.Layers == null)
                return;

            for (int index = 0;
                index < definition.Layers.Length;
                index++)
            {
                AddMaterialName(
                    output,
                    value,
                    definition.Layers[index].Material);
            }
        }


        private static void AddMaterialName(
            Dictionary<byte, HashSet<string>> output,
            byte value,
            string materialSubtype)
        {
            if (string.IsNullOrWhiteSpace(materialSubtype))
                return;

            HashSet<string> materials;

            if (!output.TryGetValue(
                value,
                out materials))
            {
                materials =
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                output.Add(
                    value,
                    materials);
            }

            materials.Add(
                materialSubtype);
        }
    }
}
