using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using VoxelCubemapApi.Common.PlanetModification.Runtime;
using VRage.Game;
using VRage.Utils;

namespace VoxelCubemapApi.Common.PlanetModification.EnvironmentPresets
{
    /// <summary>
    /// Captures semantic environment mappings from source planet XML. It only
    /// uses whitelisted planet object-builders; prepared procedural environment
    /// implementation types are deliberately never referenced.
    /// </summary>
    internal sealed class EnvironmentPresetCatalog
    {
        private readonly RuntimeGeneratorRegistry _runtimeGenerators;

        private readonly Dictionary<string, EnvironmentPresetSnapshot> _presets =
            new Dictionary<string, EnvironmentPresetSnapshot>(
                StringComparer.OrdinalIgnoreCase);


        internal EnvironmentPresetCatalog(
            RuntimeGeneratorRegistry runtimeGenerators)
        {
            if (runtimeGenerators == null)
                throw new ArgumentNullException(nameof(runtimeGenerators));

            _runtimeGenerators =
                runtimeGenerators;

            ScanLoadedDefinitions();
        }


        internal string[] GetPresetNames()
        {
            string[] names = _presets.Keys.ToArray();

            Array.Sort(
                names,
                StringComparer.OrdinalIgnoreCase);

            return names;
        }


        internal bool Contains(string presetName)
        {
            return !string.IsNullOrWhiteSpace(presetName) &&
                _presets.ContainsKey(presetName);
        }


        internal EnvironmentPresetSnapshot Resolve(string presetName)
        {
            EnvironmentPresetSnapshot preset;

            if (string.IsNullOrWhiteSpace(presetName) ||
                !_presets.TryGetValue(presetName, out preset))
            {
                throw new Exception(
                    "Environment preset '" +
                    presetName +
                    "' is not available. Query GetEnvironmentPresets() for " +
                    "the loaded preset names.");
            }

            return preset;
        }


        private void ScanLoadedDefinitions()
        {
            IEnumerable<MyPlanetGeneratorDefinition> generators =
                MyDefinitionManager.Static.GetPlanetsGeneratorsDefinitions();

            if (generators == null)
                return;

            foreach (MyPlanetGeneratorDefinition generator in generators)
            {
                EnvironmentPresetSnapshot preset;

                try
                {
                    if (!TryCreatePreset(
                        generator,
                        out preset))
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Environment preset scan skipped '" +
                        (generator == null
                            ? "<null>"
                            : generator.Id.SubtypeName) +
                        "': " +
                        e.Message);

                    continue;
                }

                if (_presets.ContainsKey(preset.Name))
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Duplicate environment preset '" +
                        preset.Name +
                        "' encountered; the final loaded definition wins.");
                }

                _presets[preset.Name] =
                    preset;
            }

            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Discovered " +
                _presets.Count +
                " XML-backed environment presets.");
        }


        private bool TryCreatePreset(
            MyPlanetGeneratorDefinition generator,
            out EnvironmentPresetSnapshot preset)
        {
            preset =
                null;

            if (generator == null ||
                generator.EnvironmentDefinition == null)
            {
                return false;
            }

            MyObjectBuilder_PlanetGeneratorDefinition builder =
                _runtimeGenerators.CaptureSourceBuilder(
                    generator);

            PlanetEnvironmentItemMapping[] sourceMappings =
                builder.EnvironmentItems;

            // Explicit procedural WorldEnvironmentDefinition object-builders
            // are not on the mod whitelist. Supporting them requires their
            // semantic XML to be embedded in or otherwise reachable from a
            // whitelisted carrier; legacy EnvironmentItems already satisfy
            // that contract and cover the vanilla vegetation planets.
            if (sourceMappings == null ||
                sourceMappings.Length == 0)
            {
                return false;
            }

            EnvironmentPresetMapping[] mappings =
                sourceMappings
                    .Where(IsUsable)
                    .Select(CreateMapping)
                    .ToArray();

            if (mappings.Length == 0)
                return false;

            preset =
                new EnvironmentPresetSnapshot
                {
                    Name = generator.Id.SubtypeName,
                    SourceGeneratorSubtype = generator.Id.SubtypeName,
                    SourceGeneratorId = generator.Id,
                    SourceContext = generator.Context,
                    Mappings = mappings
                };

            return true;
        }


        private static bool IsUsable(
            PlanetEnvironmentItemMapping mapping)
        {
            return mapping.Materials != null &&
                mapping.Materials.Length > 0 &&
                mapping.Items != null &&
                mapping.Items.Length > 0;
        }


        private static EnvironmentPresetMapping CreateMapping(
            PlanetEnvironmentItemMapping source)
        {
            int[] sourceBiomes =
                source.Biomes == null ||
                source.Biomes.Length == 0
                    ? new[] { 0 }
                    : source.Biomes;

            var biomes =
                new List<byte>(sourceBiomes.Length);

            for (int index = 0;
                index < sourceBiomes.Length;
                index++)
            {
                if (sourceBiomes[index] < byte.MinValue ||
                    sourceBiomes[index] > byte.MaxValue)
                {
                    continue;
                }

                byte biome =
                    (byte)sourceBiomes[index];

                if (!biomes.Contains(biome))
                {
                    biomes.Add(
                        biome);
                }
            }

            string[] materials =
                source.Materials
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            Array.Sort(
                materials,
                StringComparer.OrdinalIgnoreCase);

            MyPlanetSurfaceRule rule =
                source.Rule ??
                new MyPlanetSurfaceRule();

            EnvironmentPresetItem[] items =
                source.Items
                    .Where(x => x != null)
                    .Select(x =>
                        new EnvironmentPresetItem
                        {
                            Type = x.TypeId,
                            Subtype = x.SubtypeId,
                            Density = x.Density,
                            Offset = x.Offset
                        })
                    .ToArray();

            return new EnvironmentPresetMapping
            {
                MaterialSubtypeNames = materials,
                SourceBiomes = biomes.ToArray(),
                Items = items,
                HeightMin = rule.Height.Min,
                HeightMax = rule.Height.Max,
                LatitudeMin = rule.Latitude.Min,
                LatitudeMax = rule.Latitude.Max,
                LongitudeMin = rule.Longitude.Min,
                LongitudeMax = rule.Longitude.Max,
                SlopeMin = rule.Slope.Min,
                SlopeMax = rule.Slope.Max
            };
        }
    }
}
