using System;
using System.Collections.Generic;
using Adk.Image.Png;
using VoxelCubemapApi.Server.PlanetModification;
using VoxelCubemapApi.Server.PlanetModification.Templates;

namespace VoxelCubemapApi.Server.PlanetModification.Maps
{
    internal static class PlanetMapOperations
    {
        internal static PlanarPngBitmap DecodePlanetPng(
            string fileName,
            byte[] png)
        {
            if (png == null)
            {
                throw new Exception(
                    "Invalid planet PNG: " +
                    fileName);
            }


            try
            {
                PlanarPngBitmap image = PlanarPngBitmap.Load(png);

                if (image.SourceInterlaceMethod != 0)
                {
                    throw new Exception(
                        "Interlaced planet PNGs are not supported.");
                }

                return image;
            }
            catch (Exception error)
            {
                throw new Exception(
                    "Could not decode planet PNG " +
                    fileName +
                    ": " +
                    error.Message,
                    error);
            }
        }


        internal static void ApplyFractalNoiseToPlanetImage(
            PlanarPngBitmap image,
            string faceFileName,
            long planetSeed,
            List<FractalNoiseOperation> operations)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            if (operations == null ||
                operations.Count == 0)
            {
                return;
            }

            bool needsNoise =
                false;

            for (int operationIndex = 0;
                operationIndex < operations.Count;
                operationIndex++)
            {
                FractalNoiseOperation operation =
                    operations[operationIndex];

                if (operation == null)
                    throw new ArgumentNullException("operations");

                if (operation.PlaneIndex < 0 ||
                    operation.PlaneIndex >= image.Planes.Length)
                {
                    throw new Exception(
                        "Invalid planet-map plane index: " +
                        operation.PlaneIndex +
                        ".");
                }

                if (operation.CoveragePercent > 0 &&
                    operation.CoveragePercent < 100)
                {
                    needsNoise =
                        true;
                }
            }


            double[] noiseGrid =
                null;

            if (needsNoise)
            {
                int faceIndex =
                    FractalBrownianMotion.GetCubemapFaceIndex(
                        faceFileName);

                noiseGrid =
                    FractalBrownianMotion.BuildGrassNoiseGrid(
                        faceIndex,
                        planetSeed);
            }

            int pixelOffset =
                0;

            for (int y = 0;
                y < image.Height;
                y++)
            {
                for (int x = 0;
                    x < image.Width;
                    x++)
                {
                    double score =
                        needsNoise
                            ? FractalBrownianMotion.SampleGrassNoiseGrid(
                                noiseGrid,
                                x,
                                y,
                                image.Width,
                                image.Height)
                            : 0.0;

                    for (int operationIndex = 0;
                        operationIndex < operations.Count;
                        operationIndex++)
                    {
                        FractalNoiseOperation operation =
                            operations[operationIndex];

                        bool selected =
                            operation.CoveragePercent >= 100 ||
                            (operation.CoveragePercent > 0 &&
                                score >= operation.Threshold);

                        if (selected)
                        {
                            image.Planes[operation.PlaneIndex][pixelOffset] =
                                operation.TargetValue;
                        }
                    }

                    pixelOffset++;
                }
            }
        }


        internal static void ApplyBiomeReplacementToPlanetImage(
            PlanarPngBitmap image,
            BiomeReplacementOperation operation)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            if (operation == null)
                throw new ArgumentNullException("operation");

            byte[] biomes =
                image.Planes[1];

            for (int pixel = 0;
                pixel < biomes.Length;
                pixel++)
            {
                if (biomes[pixel] == operation.SourceBiome)
                    biomes[pixel] = operation.TargetBiome;
            }
        }


        internal static void ApplyBrushToPlanetImages(
            PlanarPngBitmap heightImage,
            PlanarPngBitmap materialImage,
            string faceFileName,
            long planetSeed,
            List<BrushOperation> operations)
        {
            if (heightImage == null)
                throw new ArgumentNullException("heightImage");

            if (materialImage == null)
                throw new ArgumentNullException("materialImage");

            if (operations == null ||
                operations.Count == 0)
            {
                return;
            }

            ValidateBrushImages(
                heightImage,
                materialImage,
                faceFileName);

            int faceIndex =
                FractalBrownianMotion.GetCubemapFaceIndex(
                    faceFileName);

            for (int operationIndex = 0;
                operationIndex < operations.Count;
                operationIndex++)
            {
                BrushOperation operation =
                    operations[operationIndex];

                if (operation == null)
                    throw new ArgumentNullException("operations");

                PlanarPngBitmap targetImage =
                    operation.LayerIndex == 3
                        ? heightImage
                        : materialImage;

                double[] noiseGrid =
                    operation.UseNoise
                        ? FractalBrownianMotion.BuildBrushNoiseGrid(
                            faceIndex,
                            planetSeed,
                            operation.NoiseFrequency,
                            operation.NoiseOctaves,
                            operation.NoiseSeedOffset)
                        : null;

                for (int y = 0;
                    y < targetImage.Height;
                    y++)
                {
                    for (int x = 0;
                        x < targetImage.Width;
                        x++)
                    {
                        int targetOffset =
                            y *
                                targetImage.Width +
                            x;

                        int heightX =
                            MapCoordinate(
                                x,
                                targetImage.Width,
                                heightImage.Width);

                        int heightY =
                            MapCoordinate(
                                y,
                                targetImage.Height,
                                heightImage.Height);

                        int materialX =
                            MapCoordinate(
                                x,
                                targetImage.Width,
                                materialImage.Width);

                        int materialY =
                            MapCoordinate(
                                y,
                                targetImage.Height,
                                materialImage.Height);

                        int heightOffset =
                            heightY *
                                heightImage.Width +
                            heightX;

                        int materialOffset =
                            materialY *
                                materialImage.Width +
                            materialX;

                        int altitude =
                            ReadHeightSample(
                                heightImage,
                                heightOffset);

                        if (operation.MinimumAltitude >= 0 &&
                            altitude < operation.MinimumAltitude)
                        {
                            continue;
                        }

                        if (operation.MaximumAltitude >= 0 &&
                            altitude > operation.MaximumAltitude)
                        {
                            continue;
                        }

                        if (operation.BiomeFilter >= 0 &&
                            materialImage.Planes[1][materialOffset] !=
                                operation.BiomeFilter)
                        {
                            continue;
                        }

                        if (operation.MaterialFilter >= 0 &&
                            materialImage.Planes[0][materialOffset] !=
                                operation.MaterialFilter)
                        {
                            continue;
                        }

                        double latitude =
                            FractalBrownianMotion.GetLatitudeDegrees(
                                faceIndex,
                                x,
                                y,
                                targetImage.Width,
                                targetImage.Height);

                        if (latitude < operation.MinimumLatitude ||
                            latitude > operation.MaximumLatitude)
                        {
                            continue;
                        }

                        if (operation.UseNoise)
                        {
                            double score =
                                FractalBrownianMotion.SampleBrushNoiseGrid(
                                    noiseGrid,
                                    x,
                                    y,
                                    targetImage.Width,
                                    targetImage.Height);

                            if (score < operation.BlendNoiseMinimum ||
                                score > operation.BlendNoiseMaximum)
                            {
                                continue;
                            }
                        }

                        ApplyBrushFill(
                            heightImage,
                            materialImage,
                            operation,
                            targetOffset);
                    }
                }
            }
        }


        private static void ValidateBrushImages(
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
                    "Brush requires a 16-bit grayscale heightmap for " +
                    faceFileName +
                    ".");
            }

            if (materialImage.BitDepth != 8 ||
                materialImage.Planes == null ||
                materialImage.Planes.Length < 3)
            {
                throw new Exception(
                    "Brush requires an 8-bit material map for " +
                    faceFileName +
                    ".");
            }
        }


        private static int MapCoordinate(
            int coordinate,
            int fromSize,
            int toSize)
        {
            if (fromSize <= 1 ||
                toSize <= 1)
            {
                return 0;
            }

            double mapped =
                (double)coordinate *
                (toSize - 1) /
                (fromSize - 1);

            int result =
                (int)(mapped + 0.5);

            if (result < 0)
                return 0;

            if (result >= toSize)
                return toSize - 1;

            return result;
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
            heightImage.Planes[0][offset] =
                (byte)(value >> 8);

            heightImage.Planes[1][offset] =
                (byte)value;
        }


        private static void ApplyBrushFill(
            PlanarPngBitmap heightImage,
            PlanarPngBitmap materialImage,
            BrushOperation operation,
            int targetOffset)
        {
            if (operation.LayerIndex == 3)
            {
                WriteHeightSample(
                    heightImage,
                    targetOffset,
                    operation.FillValue);

                return;
            }

            if (operation.LayerIndex < 0 ||
                operation.LayerIndex > 2)
            {
                throw new Exception(
                    "Invalid brush layer index: " +
                    operation.LayerIndex +
                    ".");
            }

            materialImage.Planes[operation.LayerIndex][targetOffset] =
                (byte)operation.FillValue;
        }


        internal static void ValidateAllocatedComplexMaterialValues(
            PlanarPngBitmap image,
            string faceFileName,
            List<byte> allocatedValues)
        {
            byte[] red =
                image.Planes[0];

            for (int pixel = 0;
                pixel < red.Length;
                pixel++)
            {
                byte sourceValue =
                    red[pixel];

                for (int valueIndex = 0;
                    valueIndex < allocatedValues.Count;
                    valueIndex++)
                {
                    if (sourceValue ==
                        allocatedValues[valueIndex])
                    {
                        throw new Exception(
                            "Allocated complex material-map value " +
                            sourceValue +
                            " already exists in source PNG " +
                            faceFileName +
                            ". The modification was not pushed.");
                    }
                }
            }
        }



    }
}
