using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;

namespace VoxelCubemapApi.Common.PlanetModification.EnvironmentPresets
{
    internal static class EnvironmentPresetRemapper
    {
        internal static RemappedEnvironmentPreset Remap(
            EnvironmentPresetSnapshot preset,
            EnvironmentPresetTargetMap target)
        {
            if (preset == null)
                throw new ArgumentNullException(nameof(preset));

            if (target == null)
                throw new ArgumentNullException(nameof(target));

            var result =
                new RemappedEnvironmentPreset();

            for (int mappingIndex = 0;
                mappingIndex < preset.Mappings.Length;
                mappingIndex++)
            {
                EnvironmentPresetMapping mapping =
                    preset.Mappings[mappingIndex];

                for (int materialIndex = 0;
                    materialIndex < mapping.MaterialSubtypeNames.Length;
                    materialIndex++)
                {
                    string materialSubtype =
                        mapping.MaterialSubtypeNames[materialIndex];

                    if (MyDefinitionManager.Static
                        .GetVoxelMaterialDefinition(materialSubtype) == null)
                    {
                        result.MissingDefinitions.Add(
                            materialSubtype);

                        continue;
                    }

                    HashSet<byte> targetBiomes;

                    if (!target.TryGetBiomes(
                        materialSubtype,
                        out targetBiomes) ||
                        targetBiomes.Count == 0)
                    {
                        result.MissingTargetMaterials.Add(
                            materialSubtype);

                        continue;
                    }

                    byte[] biomes =
                        targetBiomes.ToArray();

                    Array.Sort(
                        biomes);

                    result.Mappings.Add(
                        new RemappedEnvironmentMapping
                        {
                            Source = mapping,
                            MaterialSubtypeName = materialSubtype,
                            TargetBiomes = biomes
                        });

                    result.MatchedMaterials.Add(
                        materialSubtype);
                }
            }

            if (result.Mappings.Count == 0)
            {
                throw new Exception(
                    "Environment preset '" +
                    preset.Name +
                    "' could not be mapped to the target planet: no " +
                    "compatible vegetation materials were found.");
            }

            return result;
        }
    }
}
