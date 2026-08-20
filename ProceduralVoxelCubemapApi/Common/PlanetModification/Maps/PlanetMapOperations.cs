using System;
using System.Collections.Generic;
using Adk.Image.Png;
using VoxelCubemapApi.Common.Noise;
using VoxelCubemapApi.Common.Noise.fBm;
using VoxelCubemapApi.Common.PlanetModification.Templates;

namespace VoxelCubemapApi.Common.PlanetModification.Maps
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
                throw new ArgumentNullException(nameof(image));

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
                    throw new ArgumentNullException(nameof(operations));

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
                throw new ArgumentNullException(nameof(image));

            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

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
                throw new ArgumentNullException(nameof(heightImage));

            if (materialImage == null)
                throw new ArgumentNullException(nameof(materialImage));

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

            // Preserve operation ordering, but collapse consecutive operations
            // that target the same underlying image into one pixel traversal.
            // Material/Biome/Ore all target materialImage, so typical surface
            // repaint workloads become a single pass per cubemap face.
            int batchStart = 0;
            while (batchStart < operations.Count)
            {
                BrushOperation first = operations[batchStart];
                if (first == null)
                    throw new ArgumentNullException(nameof(operations));

                bool targetsHeight = first.LayerIndex == 3;
                int batchEnd = batchStart + 1;

                while (batchEnd < operations.Count)
                {
                    BrushOperation next = operations[batchEnd];
                    if (next == null)
                        throw new ArgumentNullException(nameof(operations));

                    if ((next.LayerIndex == 3) != targetsHeight)
                        break;

                    batchEnd++;
                }

                ApplyBrushBatch(
                    heightImage,
                    materialImage,
                    faceIndex,
                    planetSeed,
                    operations,
                    batchStart,
                    batchEnd,
                    targetsHeight ? heightImage : materialImage);

                batchStart = batchEnd;
            }
        }


        private static void ApplyBrushBatch(
            PlanarPngBitmap heightImage,
            PlanarPngBitmap materialImage,
            int faceIndex,
            long planetSeed,
            List<BrushOperation> operations,
            int batchStart,
            int batchEnd,
            PlanarPngBitmap targetImage)
        {
            int batchCount = batchEnd - batchStart;
            var noiseGrids = new double[batchCount][];
            var directNoiseFields = new INoise3D[batchCount];
            var latitudeRestricted = new bool[batchCount];

            // Build each distinct noise grid only once for this face/batch.
            // Paired Biome + Material brushes commonly share identical noise.
            for (int localIndex = 0; localIndex < batchCount; localIndex++)
            {
                BrushOperation operation = operations[batchStart + localIndex];

                latitudeRestricted[localIndex] =
                    operation.MinimumLatitude > -90.0 ||
                    operation.MaximumLatitude < 90.0;

                if (!operation.UseNoise)
                    continue;

                for (int previous = 0; previous < localIndex; previous++)
                {
                    BrushOperation previousOperation =
                        operations[batchStart + previous];

                    if (previousOperation.UseNoise &&
                        previousOperation.NoiseFrequency == operation.NoiseFrequency &&
                        previousOperation.NoiseOctaves == operation.NoiseOctaves &&
                        previousOperation.NoiseSeedOffset == operation.NoiseSeedOffset &&
                        previousOperation.NoiseType == operation.NoiseType &&
                        previousOperation.NoiseSamplingQuality == operation.NoiseSamplingQuality)
                    {
                        noiseGrids[localIndex] = noiseGrids[previous];
                        directNoiseFields[localIndex] = directNoiseFields[previous];
                        break;
                    }
                }

                if (noiseGrids[localIndex] == null &&
                    directNoiseFields[localIndex] == null)
                {
                    if (operation.NoiseSamplingQuality ==
                        (int)NoiseSamplingQuality.Direct)
                    {
                        directNoiseFields[localIndex] =
                            FractalBrownianMotion.CreateNoise(
                                planetSeed,
                                operation.NoiseType,
                                operation.NoiseFrequency,
                                operation.NoiseOctaves,
                                operation.NoiseSeedOffset);
                    }
                    else
                    {
                        noiseGrids[localIndex] =
                            FractalBrownianMotion.BuildNoiseGrid(
                                faceIndex,
                                planetSeed,
                                operation.NoiseType,
                                operation.NoiseSamplingQuality,
                                operation.NoiseFrequency,
                                operation.NoiseOctaves,
                                operation.NoiseSeedOffset);
                    }
                }
            }

            // Precompute coordinate mappings once instead of doing floating-point
            // MapCoordinate() work for every brush/pixel pair.
            int[] heightXMap = BuildCoordinateMap(
                targetImage.Width,
                heightImage.Width);
            int[] heightYMap = BuildCoordinateMap(
                targetImage.Height,
                heightImage.Height);
            int[] materialXMap = BuildCoordinateMap(
                targetImage.Width,
                materialImage.Width);
            int[] materialYMap = BuildCoordinateMap(
                targetImage.Height,
                materialImage.Height);

            byte[] biomePlane = materialImage.Planes[1];
            byte[] materialPlane = materialImage.Planes[0];

            for (int y = 0; y < targetImage.Height; y++)
            {
                int targetRow = y * targetImage.Width;
                int heightRow = heightYMap[y] * heightImage.Width;
                int materialRow = materialYMap[y] * materialImage.Width;

                for (int x = 0; x < targetImage.Width; x++)
                {
                    int targetOffset = targetRow + x;
                    int heightOffset = heightRow + heightXMap[x];
                    int materialOffset = materialRow + materialXMap[x];
                    int altitude = ReadHeightSample(heightImage, heightOffset);

                    bool latitudeCalculated = false;
                    double latitude = 0.0;

                    for (int localIndex = 0; localIndex < batchCount; localIndex++)
                    {
                        BrushOperation operation =
                            operations[batchStart + localIndex];

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
                            biomePlane[materialOffset] != operation.BiomeFilter)
                        {
                            continue;
                        }

                        if (operation.MaterialFilter >= 0 &&
                            materialPlane[materialOffset] != operation.MaterialFilter)
                        {
                            continue;
                        }

                        // Full [-90,+90] brushes skip cubemap->sphere latitude
                        // conversion entirely. When needed, calculate once/pixel.
                        if (latitudeRestricted[localIndex])
                        {
                            if (!latitudeCalculated)
                            {
                                latitude =
                                    FractalBrownianMotion.GetLatitudeDegrees(
                                        faceIndex,
                                        x,
                                        y,
                                        targetImage.Width,
                                        targetImage.Height);
                                latitudeCalculated = true;
                            }

                            if (latitude < operation.MinimumLatitude ||
                                latitude > operation.MaximumLatitude)
                            {
                                continue;
                            }
                        }

                        double noiseScore = 1.0;

                        if (operation.UseNoise)
                        {
                            if (operation.NoiseSamplingQuality ==
                                (int)NoiseSamplingQuality.Direct)
                            {
                                noiseScore =
                                    FractalBrownianMotion.SampleNoiseDirect(
                                        directNoiseFields[localIndex],
                                        faceIndex,
                                        x,
                                        y,
                                        targetImage.Width,
                                        targetImage.Height);
                            }
                            else
                            {
                                noiseScore =
                                    FractalBrownianMotion.SampleBrushNoiseGrid(
                                        noiseGrids[localIndex],
                                        operation.NoiseSamplingQuality,
                                        x,
                                        y,
                                        targetImage.Width,
                                        targetImage.Height);
                            }

                            if (noiseScore < operation.BlendNoiseMinimum ||
                                noiseScore > operation.BlendNoiseMaximum)
                            {
                                continue;
                            }
                        }

                        altitude = ApplyBrushFill(
                            heightImage,
                            materialImage,
                            operation,
                            targetOffset,
                            altitude,
                            noiseScore);
                    }
                }
            }
        }


        private static int[] BuildCoordinateMap(
            int fromSize,
            int toSize)
        {
            var map = new int[fromSize];

            if (fromSize <= 1 || toSize <= 1)
                return map;

            if (fromSize == toSize)
            {
                for (int i = 0; i < fromSize; i++)
                    map[i] = i;

                return map;
            }

            for (int i = 0; i < fromSize; i++)
            {
                double mapped =
                    (double)i *
                    (toSize - 1) /
                    (fromSize - 1);

                int result = (int)(mapped + 0.5);

                if (result < 0)
                    result = 0;
                else if (result >= toSize)
                    result = toSize - 1;

                map[i] = result;
            }

            return map;
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


        private static int ApplyBrushFill(
            PlanarPngBitmap heightImage,
            PlanarPngBitmap materialImage,
            BrushOperation operation,
            int targetOffset,
            int currentAltitude,
            double noiseScore)
        {
            if (operation.LayerIndex == 3)
            {
                int amount = operation.FillValue;

                if (operation.ScaleHeightByNoise)
                {
                    amount = (int)(
                        operation.FillValue * noiseScore + 0.5);
                }

                int value;
                switch (operation.HeightBlendMode)
                {
                    case 1: // Add
                        value = currentAltitude + amount;
                        break;

                    case 2: // Subtract
                        value = currentAltitude - amount;
                        break;

                    default: // Replace
                        value = amount;
                        break;
                }

                if (value < 0)
                    value = 0;
                else if (value > ushort.MaxValue)
                    value = ushort.MaxValue;

                WriteHeightSample(
                    heightImage,
                    targetOffset,
                    value);

                return value;
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

            return currentAltitude;
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
