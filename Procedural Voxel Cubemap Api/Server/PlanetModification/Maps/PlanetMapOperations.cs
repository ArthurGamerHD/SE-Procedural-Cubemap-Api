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
                    CubemapNoise.GetCubemapFaceIndex(
                        faceFileName);

                noiseGrid =
                    CubemapNoise.BuildGrassNoiseGrid(
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
                            ? CubemapNoise.SampleGrassNoiseGrid(
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
