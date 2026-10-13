using System;
using System.Collections.Generic;
using Adk.Image.Png;
using Sandbox.ModAPI;
using ProceduralCubemapApi.Common.Noise;
using ProceduralCubemapApi.Common.Noise.fBm;
using ProceduralCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace ProceduralCubemapApi.Common.PlanetModification.Maps
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

            // Independent material/biome/ore paints can run newest-first per
            // pixel. Filters and height edits form ordered batch boundaries.
            int batchStart = 0;
            while (batchStart < operations.Count)
            {
                BrushOperation first = operations[batchStart];
                if (first == null)
                    throw new ArgumentNullException(nameof(operations));

                bool targetsHeight = first.LayerIndex == 3;
                bool independentPaint = !targetsHeight && first.BiomeFilter < 0 && first.MaterialFilter < 0;
                int batchEnd = batchStart + 1;

                while (batchEnd < operations.Count)
                {
                    BrushOperation next = operations[batchEnd];
                    if (next == null)
                        throw new ArgumentNullException(nameof(operations));

                    if ((next.LayerIndex == 3) != targetsHeight)
                        break;

                    bool nextIndependent = next.LayerIndex != 3 && next.BiomeFilter < 0 && next.MaterialFilter < 0;
                    if (nextIndependent != independentPaint)
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
            BrushOperation first = operations[batchStart];
            bool deferredHeight = first.LayerIndex == 3;
            bool newestFirst = first.LayerIndex != 3 && first.BiomeFilter < 0 && first.MaterialFilter < 0;
            int targetLayers = 0;
            if (newestFirst)
                for (int i = batchStart; i < batchEnd; i++)
                    targetLayers |= 1 << operations[i].LayerIndex;
            var noiseGrids = new double[batchCount][];
            var directNoiseFields = new INoise3D[batchCount];
            var radialFields = new RadialField[batchCount];
            var latitudeRestricted = new bool[batchCount];

            // Build each distinct noise grid only once for this face/batch.
            // Paired Biome + Material brushes commonly share identical noise.
            for (int localIndex = 0; localIndex < batchCount; localIndex++)
            {
                BrushOperation operation = operations[batchStart + localIndex];

                latitudeRestricted[localIndex] =
                    operation.MinimumLatitude > -90.0 ||
                    operation.MaximumLatitude < 90.0;

                if (operation.UseRadial)
                {
                    radialFields[localIndex] =
                        new RadialField(
                            operation.RadialCenterX,
                            operation.RadialCenterY,
                            operation.RadialCenterZ,
                            operation.RadialRadiusDegrees,
                            (RadialFieldProfile)operation.RadialProfile);
                }

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

            bool hasNoise = false;
            for (int localIndex = 0; localIndex < batchCount; localIndex++)
            {
                if (operations[batchStart + localIndex].UseNoise)
                {
                    hasNoise = true;
                    break;
                }
            }

            int lowestScannedIndex = batchEnd;
            var scanDepthLock = new object();
            Action<int> processRow = y =>
            {
                int rowLowestScannedIndex = batchEnd;
                // Reuse a small operation stack for this row, never a pixel mask.
                // Height predicates and arithmetic need the older height, so
                // resolve them while unwinding, not while scanning backwards.
                int[] deferredIndices = deferredHeight ? new int[batchCount] : null;
                double[] deferredScores = deferredHeight ? new double[batchCount] : null;
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
                    bool radialDirectionCalculated = false;
                    Vector3D radialDirection = Vector3D.Zero;

                    // Three bits local to this pixel, not a texture-sized mask.
                    // Once a channel has a winner, no older paint can affect it.
                    int paintedLayers = 0;
                    int deferredCount = 0;
                    for (int step = 0; step < batchCount; step++)
                    {
                        int localIndex = newestFirst || deferredHeight ? batchCount - 1 - step : step;
                        if (batchStart + localIndex < rowLowestScannedIndex)
                            rowLowestScannedIndex = batchStart + localIndex;
                        BrushOperation operation =
                            operations[batchStart + localIndex];

                        int layerBit = 1 << operation.LayerIndex;
                        if (newestFirst && (paintedLayers & layerBit) != 0)
                            continue;

                        if (!deferredHeight && operation.MinimumAltitude >= 0 &&
                            altitude < operation.MinimumAltitude)
                        {
                            continue;
                        }

                        if (!deferredHeight && operation.MaximumAltitude >= 0 &&
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

                        if (operation.UseRadial)
                        {
                            if (!radialDirectionCalculated)
                            {
                                radialDirection =
                                    FractalBrownianMotion.GetCubemapSphereDirection(
                                        faceIndex,
                                        x,
                                        y,
                                        targetImage.Width,
                                        targetImage.Height);
                                radialDirectionCalculated = true;
                            }

                            double radialScore =
                                radialFields[localIndex].Sample(
                                    radialDirection);

                            // Signed radial profiles (for example Crater) use
                            // negative values for carving and positive values
                            // for raised features. Zero is the no-op boundary.
                            if (radialScore == 0.0)
                                continue;

                            noiseScore = radialScore;
                        }

                        if (deferredHeight)
                        {
                            deferredIndices[deferredCount] = localIndex;
                            deferredScores[deferredCount++] = noiseScore;

                            // A matching Replace with no altitude dependency
                            // supplies the base; anything below it is invisible.
                            // Conditional replacements still need older height.
                            if (operation.HeightBlendMode != 1 &&
                                operation.HeightBlendMode != 2 &&
                                operation.MinimumAltitude <= 0 &&
                                (operation.MaximumAltitude < 0 ||
                                 operation.MaximumAltitude >= ushort.MaxValue))
                                break;

                            continue;
                        }

                        altitude = ApplyBrushFill(
                            heightImage,
                            materialImage,
                            operation,
                            targetOffset,
                            altitude,
                            noiseScore);

                        if (newestFirst)
                        {
                            paintedLayers |= layerBit;
                            if (paintedLayers == targetLayers) break;
                        }
                    }

                    for (int pending = deferredCount - 1; pending >= 0; pending--)
                    {
                        BrushOperation operation = operations[batchStart + deferredIndices[pending]];
                        if (operation.MinimumAltitude >= 0 && altitude < operation.MinimumAltitude)
                            continue;
                        if (operation.MaximumAltitude >= 0 && altitude > operation.MaximumAltitude)
                            continue;

                        altitude = ApplyBrushFill(
                            heightImage,
                            materialImage,
                            operation,
                            targetOffset,
                            altitude,
                            deferredScores[pending]);
                    }
                }
                // Reduce once per row, not once per pixel/brush. Noise rows may
                // execute concurrently; the deepest scan is the lowest index.
                lock (scanDepthLock)
                    if (rowLowestScannedIndex < lowestScannedIndex)
                        lowestScannedIndex = rowLowestScannedIndex;
            };

            if (hasNoise)
            {
                MyAPIGateway.Parallel.For(
                    0,
                    targetImage.Height,
                    processRow);
            }
            else
            {
                for (int y = 0; y < targetImage.Height; y++)
                    processRow(y);
            }
            // Only a complete face contributes evidence. Across the six calls,
            // a brush stays marked if ANY face reaches it. Forward batches scan
            // every operation; reverse batches may leave an untouched prefix.
            for (int index = lowestScannedIndex; index < batchEnd; index++)
                operations[index].WasScanned = true;
#if DEBUG
            VRage.Utils.MyLog.Default?.WriteLine(
                "[CubemapApi] run operation " + (deferredHeight ? "height" : "paint") +
                " brushes [" + batchStart + ".." + (batchEnd - 1) + "] for planet seed " +
                planetSeed + " max depth: " + lowestScannedIndex +
                " (face " + faceIndex + "; zero-based execution-list index; " +
                (newestFirst || deferredHeight ? "newest-first" : "forward-dependent") + ").");
#endif
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

                if (operation.ScaleHeightByNoise ||
                    operation.ScaleHeightByRadial)
                {
                    double scaledAmount =
                        operation.FillValue * noiseScore;

                    amount = scaledAmount >= 0.0
                        ? (int)(scaledAmount + 0.5)
                        : (int)(scaledAmount - 0.5);
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

            var allocated = new bool[256];
            foreach (byte value in allocatedValues)
                allocated[value] = true;

            for (int pixel = 0;
                pixel < red.Length;
                pixel++)
            {
                byte sourceValue =
                    red[pixel];

                if (allocated[sourceValue])
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
