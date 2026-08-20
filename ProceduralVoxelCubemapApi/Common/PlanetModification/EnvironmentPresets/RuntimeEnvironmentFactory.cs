using System;
using Sandbox.Definitions;
using VoxelCubemapApi.Common.PlanetModification.Runtime;

namespace VoxelCubemapApi.Common.PlanetModification.EnvironmentPresets
{
    /// <summary>
    /// Chooses the already-loaded, immutable source environment definition.
    /// Runtime construction of Keen's procedural environment types is not
    /// available to mods, so target compatibility is established by remapping
    /// target biome-map bytes before this binding is applied.
    /// </summary>
    internal static class RuntimeEnvironmentFactory
    {
        internal static MyPlanetGeneratorDefinition ResolveCarrier(
            EnvironmentPresetSnapshot preset)
        {
            if (preset == null)
                throw new ArgumentNullException(nameof(preset));

            MyPlanetGeneratorDefinition carrier =
                PlanetEnvironmentService
                    .ResolveEnvironmentGenerator(
                        preset.SourceGeneratorSubtype);

            if (carrier.EnvironmentDefinition == null)
            {
                throw new Exception(
                    "Environment preset '" +
                    preset.Name +
                    "' no longer has a resolved environment definition.");
            }

            return carrier;
        }
    }
}
