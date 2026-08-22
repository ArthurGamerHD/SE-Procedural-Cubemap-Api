using System;
using System.Collections.Generic;
using Adk.Image.Png;
using Sandbox.ModAPI;
using VoxelCubemapApi.Common.Noise.fBm;
using VoxelCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.Features
{
    internal static class PlanetFeatureProcessor
    {
        internal static List<GeneratedPlanetFeature> ResolveTerrainBlindFeatures(
            long planetSeed,
            List<FeatureOperation> operations)
        {
            return FeatureStepRegistry.Expand(operations, planetSeed);
        }

        internal static List<GeneratedPlanetFeature> ResolveRiverFeatures(
            IDictionary<string, PlanarPngBitmap> heightImages,
            long planetSeed,
            List<FeatureOperation> operations)
        {
            var features = new List<GeneratedPlanetFeature>();
            RiverFeatureStep.Instance.ExpandTerrainAware(
                operations,
                planetSeed,
                heightImages,
                features);
            return features;
        }

        internal static void ApplyToPlanetImages(
            PlanarPngBitmap heightImage,
            PlanarPngBitmap materialImage,
            string faceFileName,
            long planetSeed,
            List<FeatureOperation> operations)
        {
            if (operations == null || operations.Count == 0)
                return;

            // Backward-compatible terrain-blind path. River fields deliberately do not
            // expand here because they require the six-face planning surface.
            List<GeneratedPlanetFeature> features =
                FeatureStepRegistry.Expand(operations, planetSeed);

            ApplyResolvedToPlanetImages(
                heightImage,
                materialImage,
                faceFileName,
                features);
        }

        internal static void ApplyResolvedToPlanetImages(
            PlanarPngBitmap heightImage,
            PlanarPngBitmap materialImage,
            string faceFileName,
            List<GeneratedPlanetFeature> features)
        {
            if (heightImage == null)
                throw new ArgumentNullException(nameof(heightImage));
            if (features == null || features.Count == 0)
                return;

            ValidateImages(heightImage, materialImage, faceFileName);
            int faceIndex = FractalBrownianMotion.GetCubemapFaceIndex(faceFileName);

            const int tileSize = 32;
            FeatureTile[] tiles = BuildTiles(
                faceIndex,
                heightImage.Width,
                heightImage.Height,
                tileSize,
                features);

            MyAPIGateway.Parallel.For(0, tiles.Length, tileIndex =>
            {
                FeatureTile tile = tiles[tileIndex];
                List<GeneratedPlanetFeature> candidates = tile.Features;
                if (candidates == null || candidates.Count == 0)
                    return;

                for (int y = tile.MinY; y < tile.MaxY; y++)
                {
                    int offset = y * heightImage.Width + tile.MinX;
                    for (int x = tile.MinX; x < tile.MaxX; x++, offset++)
                    {
                        Vector3D direction = FractalBrownianMotion.GetCubemapSphereDirection(
                            faceIndex, x, y, heightImage.Width, heightImage.Height);
                        int altitude = ReadHeightSample(heightImage, offset);

                        var accumulator = new FeaturePixelAccumulator();
                        for (int featureIndex = 0; featureIndex < candidates.Count; featureIndex++)
                        {
                            GeneratedPlanetFeature feature = candidates[featureIndex];
                            if (!feature.IsAbsoluteHeightFeature)
                                feature.Accumulate(direction, altitude, ref accumulator);
                        }

                        double totalDelta = accumulator.TotalDelta;
                        int delta = totalDelta >= 0.0
                            ? (int)(totalDelta + 0.5)
                            : (int)(totalDelta - 0.5);
                        int intermediateHeight = altitude + delta;
                        if (intermediateHeight < 0) intermediateHeight = 0;
                        else if (intermediateHeight > ushort.MaxValue) intermediateHeight = ushort.MaxValue;

                        var absoluteAccumulator = new FeaturePixelAccumulator();
                        for (int featureIndex = 0; featureIndex < candidates.Count; featureIndex++)
                        {
                            GeneratedPlanetFeature feature = candidates[featureIndex];
                            if (feature.IsAbsoluteHeightFeature)
                                feature.Accumulate(direction, intermediateHeight, ref absoluteAccumulator);
                        }

                        int value = intermediateHeight;
                        if (absoluteAccumulator.HasHeightCeiling)
                        {
                            int ceiling = absoluteAccumulator.HeightCeiling >= 0.0
                                ? (int)(absoluteAccumulator.HeightCeiling + 0.5)
                                : 0;
                            if (ceiling < value)
                                value = ceiling;
                        }

                        if (value == altitude)
                            continue;

                        if (value < 0) value = 0;
                        else if (value > ushort.MaxValue) value = ushort.MaxValue;
                        WriteHeightSample(heightImage, offset, value);
                    }
                }
            });
        }

        private static FeatureTile[] BuildTiles(
            int faceIndex,
            int width,
            int height,
            int tileSize,
            List<GeneratedPlanetFeature> features)
        {
            int columns = (width + tileSize - 1) / tileSize;
            int rows = (height + tileSize - 1) / tileSize;
            var tiles = new FeatureTile[columns * rows];
            double angularPadding = Math.PI / Math.Max(1, Math.Max(width, height));

            for (int tileY = 0; tileY < rows; tileY++)
            {
                int minY = tileY * tileSize;
                int maxY = Math.Min(height, minY + tileSize);

                for (int tileX = 0; tileX < columns; tileX++)
                {
                    int minX = tileX * tileSize;
                    int maxX = Math.Min(width, minX + tileSize);
                    int tileIndex = tileY * columns + tileX;
                    int centerX = (minX + maxX - 1) / 2;
                    int centerY = (minY + maxY - 1) / 2;
                    Vector3D center = FractalBrownianMotion.GetCubemapSphereDirection(
                        faceIndex, centerX, centerY, width, height);

                    double angularRadius = 0.0;
                    angularRadius = Math.Max(angularRadius, AngularDistance(
                        center, FractalBrownianMotion.GetCubemapSphereDirection(
                            faceIndex, minX, minY, width, height)));
                    angularRadius = Math.Max(angularRadius, AngularDistance(
                        center, FractalBrownianMotion.GetCubemapSphereDirection(
                            faceIndex, maxX - 1, minY, width, height)));
                    angularRadius = Math.Max(angularRadius, AngularDistance(
                        center, FractalBrownianMotion.GetCubemapSphereDirection(
                            faceIndex, minX, maxY - 1, width, height)));
                    angularRadius = Math.Max(angularRadius, AngularDistance(
                        center, FractalBrownianMotion.GetCubemapSphereDirection(
                            faceIndex, maxX - 1, maxY - 1, width, height)));
                    angularRadius += angularPadding;

                    double tileCos = Math.Cos(angularRadius);
                    double tileSin = Math.Sin(angularRadius);
                    var candidates = new List<GeneratedPlanetFeature>();

                    for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
                    {
                        GeneratedPlanetFeature feature = features[featureIndex];
                        if (SphericalCapsIntersect(
                            center,
                            tileCos,
                            tileSin,
                            angularRadius,
                            feature))
                        {
                            candidates.Add(feature);
                        }
                    }

                    tiles[tileIndex] = new FeatureTile
                    {
                        MinX = minX,
                        MinY = minY,
                        MaxX = maxX,
                        MaxY = maxY,
                        Features = candidates
                    };
                }
            }

            return tiles;
        }

        private static bool SphericalCapsIntersect(
            Vector3D tileCenter,
            double tileCos,
            double tileSin,
            double tileRadius,
            GeneratedPlanetFeature feature)
        {
            double combinedRadius = feature.RadiusRadians + tileRadius;
            if (combinedRadius >= Math.PI)
                return true;

            double threshold = feature.CosRadius * tileCos - feature.SinRadius * tileSin;
            return Vector3D.Dot(tileCenter, feature.Center) >= threshold;
        }

        private static double AngularDistance(Vector3D a, Vector3D b)
        {
            double dot = Vector3D.Dot(a, b);
            if (dot > 1.0) dot = 1.0;
            else if (dot < -1.0) dot = -1.0;
            return Math.Acos(dot);
        }

        private static void ValidateImages(
            PlanarPngBitmap heightImage,
            PlanarPngBitmap materialImage,
            string faceFileName)
        {
            if (heightImage.BitDepth != 16 ||
                heightImage.ColorType != 0 ||
                heightImage.Planes == null ||
                heightImage.Planes.Length < 2)
            {
                throw new Exception(
                    "Feature pass requires a 16-bit grayscale heightmap for " +
                    faceFileName + ".");
            }

            if (materialImage != null &&
                (materialImage.BitDepth != 8 ||
                 materialImage.Planes == null ||
                 materialImage.Planes.Length < 3))
            {
                throw new Exception(
                    "Feature pass requires an 8-bit material map for " +
                    faceFileName + ".");
            }
        }

        private static int ReadHeightSample(
            PlanarPngBitmap heightImage,
            int offset)
        {
            return
                (heightImage.Planes[0][offset] << 8) |
                heightImage.Planes[1][offset];
        }

        private static void WriteHeightSample(
            PlanarPngBitmap heightImage,
            int offset,
            int value)
        {
            heightImage.Planes[0][offset] = (byte)(value >> 8);
            heightImage.Planes[1][offset] = (byte)value;
        }

        private sealed class FeatureTile
        {
            internal int MinX;
            internal int MinY;
            internal int MaxX;
            internal int MaxY;
            internal List<GeneratedPlanetFeature> Features;
        }
    }
}
