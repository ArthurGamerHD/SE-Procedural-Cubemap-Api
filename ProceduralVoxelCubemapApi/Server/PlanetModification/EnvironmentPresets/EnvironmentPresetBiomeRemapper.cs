using Adk.Image.Png;
using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCubemapApi.Server.PlanetModification.Maps;
using VoxelCubemapApi.Server.PlanetModification.Persistence;

namespace VoxelCubemapApi.Server.PlanetModification.EnvironmentPresets
{
    /// <summary>
    /// Keen does not whitelist construction of procedural environment
    /// definitions at runtime. Keep the loaded preset definition immutable and
    /// adapt the target biome channel to its semantic material/biome keys.
    /// </summary>
    internal static class EnvironmentPresetBiomeRemapper
    {
        private const double DistributionNoiseFrequency =
            2.15;

        private const int DistributionNoiseOctaves =
            4;

        private const int DistributionNoiseSeedOffset =
            48611;


        internal static int Apply(
            EnvironmentPresetSnapshot preset,
            RemappedEnvironmentPreset remapped,
            EnvironmentPresetTargetMap targetMap,
            Dictionary<string, byte[]> archiveFiles,
            long planetSeed)
        {
            if (preset == null)
                throw new ArgumentNullException("preset");

            if (remapped == null)
                throw new ArgumentNullException("remapped");

            if (targetMap == null)
                throw new ArgumentNullException("targetMap");

            if (archiveFiles == null)
                throw new ArgumentNullException("archiveFiles");

            Dictionary<string, byte[]> sourceBiomes =
                BuildSourceBiomes(
                    preset,
                    remapped.MatchedMaterials);

            string[] faces =
            {
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };

            double[][] distributionNoise =
                BuildDistributionNoise(
                    planetSeed);

            double[] sortedDistributionSamples =
                BuildSortedDistributionSamples(
                    distributionNoise);

            int changed =
                0;

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
                    PlanetMapOperations.DecodePlanetPng(
                        faces[faceIndex],
                        encoded);

                byte[] materialValues =
                    image.Planes[0];

                byte[] biomeValues =
                    image.Planes[1];

                double[] faceNoise =
                    distributionNoise[faceIndex];

                for (int y = 0;
                    y < image.Height;
                    y++)
                {
                    for (int x = 0;
                        x < image.Width;
                        x++)
                    {
                        int pixelIndex =
                            y * image.Width + x;

                        HashSet<string> candidates;

                        if (!targetMap.TryGetMaterials(
                            materialValues[pixelIndex],
                            out candidates))
                        {
                            continue;
                        }

                        string selectedMaterial =
                            candidates
                                .Where(sourceBiomes.ContainsKey)
                                .OrderBy(
                                    value => value,
                                    StringComparer.OrdinalIgnoreCase)
                                .FirstOrDefault();

                        if (selectedMaterial == null)
                            continue;

                        byte[] compatibleBiomes =
                            sourceBiomes[selectedMaterial];

                        byte currentBiome =
                            biomeValues[pixelIndex];

                        byte targetBiome =
                            Array.IndexOf(
                                compatibleBiomes,
                                currentBiome) >= 0
                                ? currentBiome
                                : SelectDistributedBiome(
                                    compatibleBiomes,
                                    FractalBrownianMotion
                                        .SampleBrushNoiseGrid(
                                            faceNoise,
                                            x,
                                            y,
                                            image.Width,
                                            image.Height),
                                    sortedDistributionSamples);

                        AddEmittedBiomePixel(
                            remapped,
                            targetBiome);

                        if (targetBiome == currentBiome)
                            continue;

                        biomeValues[pixelIndex] =
                            targetBiome;

                        changed++;
                    }
                }

                archiveFiles[faces[faceIndex]] =
                    image.Encode();
            }

            return changed;
        }


        internal static List<RuntimeProceduralEnvironmentMapRule>
            BuildResolvedRules(
                EnvironmentPresetSnapshot preset,
                RemappedEnvironmentPreset remapped,
                EnvironmentPresetTargetMap targetMap)
        {
            if (preset == null)
                throw new ArgumentNullException("preset");

            if (remapped == null)
                throw new ArgumentNullException("remapped");

            if (targetMap == null)
                throw new ArgumentNullException("targetMap");

            Dictionary<string, byte[]> sourceBiomes =
                BuildSourceBiomes(
                    preset,
                    remapped.MatchedMaterials);

            var rules =
                new List<RuntimeProceduralEnvironmentMapRule>();

            for (int value = 0;
                value <= byte.MaxValue;
                value++)
            {
                HashSet<string> candidates;

                if (!targetMap.TryGetMaterials(
                    (byte)value,
                    out candidates))
                {
                    continue;
                }

                string selectedMaterial =
                    candidates
                        .Where(sourceBiomes.ContainsKey)
                        .OrderBy(
                            candidate => candidate,
                            StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                if (selectedMaterial == null)
                    continue;

                byte[] compatibleBiomes =
                    sourceBiomes[selectedMaterial];

                var persistedBiomes =
                    new byte[compatibleBiomes.Length];

                Array.Copy(
                    compatibleBiomes,
                    persistedBiomes,
                    compatibleBiomes.Length);

                rules.Add(
                    new RuntimeProceduralEnvironmentMapRule
                    {
                        MaterialMapValue = (byte)value,
                        CompatibleBiomes = persistedBiomes
                    });
            }

            if (rules.Count == 0)
            {
                throw new Exception(
                    "Environment preset produced no persistent biome-map rules.");
            }

            return rules;
        }


        internal static int ApplyResolved(
            List<RuntimeProceduralEnvironmentMapRule> rules,
            Dictionary<string, byte[]> archiveFiles,
            long planetSeed)
        {
            if (rules == null ||
                rules.Count == 0)
            {
                throw new ArgumentException(
                    "Resolved environment remap contains no rules.",
                    "rules");
            }

            if (archiveFiles == null)
                throw new ArgumentNullException("archiveFiles");

            var compatibleBiomesByMaterial =
                new Dictionary<byte, byte[]>();

            for (int index = 0;
                index < rules.Count;
                index++)
            {
                RuntimeProceduralEnvironmentMapRule rule =
                    rules[index];

                compatibleBiomesByMaterial.Add(
                    rule.MaterialMapValue,
                    rule.CompatibleBiomes);
            }

            string[] faces =
            {
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };

            double[][] distributionNoise =
                BuildDistributionNoise(
                    planetSeed);

            double[] sortedDistributionSamples =
                BuildSortedDistributionSamples(
                    distributionNoise);

            int changed =
                0;

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
                    PlanetMapOperations.DecodePlanetPng(
                        faces[faceIndex],
                        encoded);

                byte[] materialValues =
                    image.Planes[0];

                byte[] biomeValues =
                    image.Planes[1];

                double[] faceNoise =
                    distributionNoise[faceIndex];

                for (int y = 0;
                    y < image.Height;
                    y++)
                {
                    for (int x = 0;
                        x < image.Width;
                        x++)
                    {
                        int pixelIndex =
                            y * image.Width + x;

                        byte[] compatibleBiomes;

                        if (!compatibleBiomesByMaterial.TryGetValue(
                            materialValues[pixelIndex],
                            out compatibleBiomes))
                        {
                            continue;
                        }

                        byte currentBiome =
                            biomeValues[pixelIndex];

                        byte targetBiome =
                            Array.IndexOf(
                                compatibleBiomes,
                                currentBiome) >= 0
                                ? currentBiome
                                : SelectDistributedBiome(
                                    compatibleBiomes,
                                    FractalBrownianMotion
                                        .SampleBrushNoiseGrid(
                                            faceNoise,
                                            x,
                                            y,
                                            image.Width,
                                            image.Height),
                                    sortedDistributionSamples);

                        if (targetBiome == currentBiome)
                            continue;

                        biomeValues[pixelIndex] =
                            targetBiome;

                        changed++;
                    }
                }

                archiveFiles[faces[faceIndex]] =
                    image.Encode();
            }

            return changed;
        }


        private static double[][] BuildDistributionNoise(
            long planetSeed)
        {
            var grids =
                new double[6][];

            for (int faceIndex = 0;
                faceIndex < grids.Length;
                faceIndex++)
            {
                grids[faceIndex] =
                    FractalBrownianMotion.BuildBrushNoiseGrid(
                        faceIndex,
                        planetSeed,
                        DistributionNoiseFrequency,
                        DistributionNoiseOctaves,
                        DistributionNoiseSeedOffset);
            }

            return grids;
        }


        private static double[] BuildSortedDistributionSamples(
            double[][] grids)
        {
            int sampleCount =
                grids.Sum(grid => grid.Length);

            var samples =
                new double[sampleCount];

            int offset =
                0;

            for (int index = 0;
                index < grids.Length;
                index++)
            {
                Array.Copy(
                    grids[index],
                    0,
                    samples,
                    offset,
                    grids[index].Length);

                offset +=
                    grids[index].Length;
            }

            Array.Sort(
                samples);

            return samples;
        }


        private static byte SelectDistributedBiome(
            byte[] compatibleBiomes,
            double score,
            double[] sortedSamples)
        {
            int low =
                0;

            int high =
                sortedSamples.Length;

            while (low < high)
            {
                int middle =
                    low + (high - low) / 2;

                if (sortedSamples[middle] <= score)
                    low = middle + 1;
                else
                    high = middle;
            }

            int biomeIndex =
                (int)((long)low * compatibleBiomes.Length /
                    sortedSamples.Length);

            if (biomeIndex >= compatibleBiomes.Length)
                biomeIndex = compatibleBiomes.Length - 1;

            return compatibleBiomes[biomeIndex];
        }


        private static void AddEmittedBiomePixel(
            RemappedEnvironmentPreset remapped,
            byte biome)
        {
            long count;

            remapped.EmittedBiomePixels.TryGetValue(
                biome,
                out count);

            remapped.EmittedBiomePixels[biome] =
                count + 1;
        }


        private static Dictionary<string, byte[]> BuildSourceBiomes(
            EnvironmentPresetSnapshot preset,
            HashSet<string> matchedMaterials)
        {
            var accumulated =
                new Dictionary<string, HashSet<byte>>(
                    StringComparer.OrdinalIgnoreCase);

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
                    string material =
                        mapping.MaterialSubtypeNames[materialIndex];

                    if (!matchedMaterials.Contains(material))
                        continue;

                    HashSet<byte> biomes;

                    if (!accumulated.TryGetValue(
                        material,
                        out biomes))
                    {
                        biomes =
                            new HashSet<byte>();

                        accumulated.Add(
                            material,
                            biomes);
                    }

                    for (int biomeIndex = 0;
                        biomeIndex < mapping.SourceBiomes.Length;
                        biomeIndex++)
                    {
                        biomes.Add(
                            mapping.SourceBiomes[biomeIndex]);
                    }
                }
            }

            return accumulated.ToDictionary(
                x => x.Key,
                x =>
                {
                    byte[] values = x.Value.ToArray();
                    Array.Sort(values);
                    return values;
                },
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
