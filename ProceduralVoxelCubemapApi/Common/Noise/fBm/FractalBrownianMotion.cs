#if VOXEL_CUBEMAP_NOISE_CLI
using Vector3D = VoxelCubemapApi.NoiseTestCli.NoiseVector3D;
#else
using VRageMath;
using Sandbox.ModAPI;
#endif
using System;
using VoxelCubemapApi.Common.Noise;

namespace VoxelCubemapApi.Common.Noise.fBm
{
    internal static class FractalBrownianMotion
    {
        internal static int GetCubemapFaceIndex(
            string faceFileName)
        {
            if (string.IsNullOrWhiteSpace(
                faceFileName))
            {
                throw new Exception(
                    "Cubemap face name is required for partial grass coverage.");
            }


            if (faceFileName.StartsWith(
                "front",
                StringComparison.OrdinalIgnoreCase))
                return 0;

            if (faceFileName.StartsWith(
                "back",
                StringComparison.OrdinalIgnoreCase))
                return 1;

            if (faceFileName.StartsWith(
                "left",
                StringComparison.OrdinalIgnoreCase))
                return 2;

            if (faceFileName.StartsWith(
                "right",
                StringComparison.OrdinalIgnoreCase))
                return 3;

            if (faceFileName.StartsWith(
                "up",
                StringComparison.OrdinalIgnoreCase))
                return 4;

            if (faceFileName.StartsWith(
                "down",
                StringComparison.OrdinalIgnoreCase))
                return 5;


            throw new Exception(
                "Unknown cubemap face: " +
                faceFileName);
        }


        public static Vector3D GetCubemapSphereDirection(
            int faceIndex,
            int x,
            int y,
            int width,
            int height)
        {
            double u =
                width <= 1
                    ? 0.0
                    : (2.0 * x /
                        (width - 1.0)) -
                        1.0;

            double v =
                height <= 1
                    ? 0.0
                    : (2.0 * y /
                        (height - 1.0)) -
                        1.0;


            Vector3D direction;


            // Exact inverse of Space Engineers' planet heightmap mapping in
            // Sandbox.Engine.Voxels.Planet.MyCubemapHelpers.CalculateSampleTexcoord.
            //
            // This is important for localized fields: the center supplied by a
            // client is a world/planet-space direction, so it must resolve to the
            // same face and pixel that VRage samples from the planet cubemap.
            // A merely seamless (but differently oriented) cube mapping is enough
            // for global noise, but places craters/ravines on the wrong side.
            switch (faceIndex)
            {
                case 0: // front, -Z
                    direction =
                        new Vector3D(
                            -u,
                            -v,
                            -1.0);
                    break;

                case 1: // back, +Z
                    direction =
                        new Vector3D(
                            u,
                            -v,
                            1.0);
                    break;

                case 2: // left map, +X
                    direction =
                        new Vector3D(
                            1.0,
                            -v,
                            -u);
                    break;

                case 3: // right map, -X
                    direction =
                        new Vector3D(
                            -1.0,
                            -v,
                            u);
                    break;

                case 4: // up, +Y
                    direction =
                        new Vector3D(
                            -u,
                            1.0,
                            -v);
                    break;

                case 5: // down, -Y
                    direction =
                        new Vector3D(
                            u,
                            -1.0,
                            -v);
                    break;

                default:
                    throw new Exception(
                        "Invalid cubemap face index: " +
                        faceIndex);
            }


            double lengthSquared =
                direction.X * direction.X +
                direction.Y * direction.Y +
                direction.Z * direction.Z;

            double inverseLength =
                1.0 /
                Math.Sqrt(
                    lengthSquared);


            return new Vector3D(
                direction.X * inverseLength,
                direction.Y * inverseLength,
                direction.Z * inverseLength);
        }


        internal static double[] BuildGrassNoiseGrid(
            int faceIndex,
            long planetSeed)
        {
            const int gridResolution =
                129;

            double[] grid =
                new double[
                    gridResolution *
                    gridResolution];

            int offset =
                0;


            for (int y = 0;
                y < gridResolution;
                y++)
            {
                for (int x = 0;
                    x < gridResolution;
                    x++)
                {
                    Vector3D direction =
                        GetCubemapSphereDirection(
                            faceIndex,
                            x,
                            y,
                            gridResolution,
                            gridResolution);

                    grid[offset++] =
                        PlanetGrassFbm(
                            direction,
                            planetSeed);
                }
            }


            return grid;
        }


        internal static double[] BuildBrushNoiseGrid(
            int faceIndex,
            long planetSeed,
            double frequency,
            int octaves,
            int seedOffset)
        {
            return BuildNoiseGrid(
                faceIndex,
                planetSeed,
                (int)ProceduralNoiseKind.Fbm,
                frequency,
                octaves,
                seedOffset);
        }


        internal static double[] BuildNoiseGrid(
            int faceIndex,
            long planetSeed,
            int noiseType,
            double frequency,
            int octaves,
            int seedOffset)
        {
            return BuildNoiseGrid(
                faceIndex,
                planetSeed,
                noiseType,
                (int)NoiseSamplingQuality.Low,
                frequency,
                octaves,
                seedOffset);
        }


        internal static double[] BuildNoiseGrid(
            int faceIndex,
            long planetSeed,
            int noiseType,
            int samplingQuality,
            double frequency,
            int octaves,
            int seedOffset)
        {
            int gridResolution = GetNoiseGridResolution(samplingQuality);
            double[] grid = new double[gridResolution * gridResolution];
            INoise3D noise = CreateNoise(
                planetSeed,
                noiseType,
                frequency,
                octaves,
                seedOffset);

#if VOXEL_CUBEMAP_NOISE_CLI
            for (int y = 0; y < gridResolution; y++)
            {
                int rowOffset = y * gridResolution;
                for (int x = 0; x < gridResolution; x++)
                {
                    Vector3D direction = GetCubemapSphereDirection(
                        faceIndex,
                        x,
                        y,
                        gridResolution,
                        gridResolution);

                    grid[rowOffset + x] = NoiseMath.To01(
                        noise.Sample(direction.X, direction.Y, direction.Z));
                }
            }
#else
            // Noise modules are immutable/stateless after construction, and each
            // worker owns one independent grid row. This keeps expensive FBM /
            // ridged / billow octave sampling off the simulation thread without
            // introducing shared writes or locks.
            MyAPIGateway.Parallel.For(0, gridResolution, y =>
            {
                int rowOffset = y * gridResolution;
                for (int x = 0; x < gridResolution; x++)
                {
                    Vector3D direction = GetCubemapSphereDirection(
                        faceIndex,
                        x,
                        y,
                        gridResolution,
                        gridResolution);

                    grid[rowOffset + x] = NoiseMath.To01(
                        noise.Sample(direction.X, direction.Y, direction.Z));
                }
            });
#endif

            return grid;
        }


        internal static INoise3D CreateNoise(
            long planetSeed,
            int noiseType,
            double frequency,
            int octaves,
            int seedOffset)
        {
            long seed = unchecked(planetSeed + seedOffset);

            return ProceduralNoiseField.Create(
                (ProceduralNoiseKind)noiseType,
                seed,
                frequency,
                octaves);
        }


        internal static int GetNoiseGridResolution(
            int samplingQuality)
        {
            switch ((NoiseSamplingQuality)samplingQuality)
            {
                case NoiseSamplingQuality.Low:
                    return 129;

                case NoiseSamplingQuality.Medium:
                    return 257;

                case NoiseSamplingQuality.High:
                    return 513;

                case NoiseSamplingQuality.Direct:
                    throw new ArgumentException(
                        "Direct noise sampling does not use a grid.",
                        nameof(samplingQuality));

                default:
                    throw new ArgumentException(
                        "Unknown noise sampling quality.",
                        nameof(samplingQuality));
            }
        }


        internal static double SampleGrassNoiseGrid(
            double[] grid,
            int x,
            int y,
            int width,
            int height)
        {
            return SampleNoiseGrid(
                grid,
                129,
                x,
                y,
                width,
                height);
        }


        private static double SampleNoiseGrid(
            double[] grid,
            int gridResolution,
            int x,
            int y,
            int width,
            int height)
        {

            int gridMaximum =
                gridResolution - 1;


            double gridX =
                width <= 1
                    ? 0.0
                    : (double)x *
                        gridMaximum /
                        (width - 1.0);

            double gridY =
                height <= 1
                    ? 0.0
                    : (double)y *
                        gridMaximum /
                        (height - 1.0);


            int x0 =
                (int)gridX;

            int y0 =
                (int)gridY;

            int x1 =
                x0 < gridMaximum
                    ? x0 + 1
                    : x0;

            int y1 =
                y0 < gridMaximum
                    ? y0 + 1
                    : y0;


            double tx =
                gridX - x0;

            double ty =
                gridY - y0;


            double top =
                LerpNoise(
                    grid[
                        y0 *
                        gridResolution +
                        x0],
                    grid[
                        y0 *
                        gridResolution +
                        x1],
                    tx);

            double bottom =
                LerpNoise(
                    grid[
                        y1 *
                        gridResolution +
                        x0],
                    grid[
                        y1 *
                        gridResolution +
                        x1],
                    tx);


            return LerpNoise(
                top,
                bottom,
                ty);
        }


        internal static double SampleBrushNoiseGrid(
            double[] grid,
            int x,
            int y,
            int width,
            int height)
        {
            return SampleGrassNoiseGrid(
                grid,
                x,
                y,
                width,
                height);
        }


        internal static double SampleBrushNoiseGrid(
            double[] grid,
            int samplingQuality,
            int x,
            int y,
            int width,
            int height)
        {
            return SampleNoiseGrid(
                grid,
                GetNoiseGridResolution(samplingQuality),
                x,
                y,
                width,
                height);
        }


        internal static double SampleNoiseDirect(
            INoise3D noise,
            int faceIndex,
            int x,
            int y,
            int width,
            int height)
        {
            Vector3D direction = GetCubemapSphereDirection(
                faceIndex,
                x,
                y,
                width,
                height);

            return NoiseMath.To01(
                noise.Sample(direction.X, direction.Y, direction.Z));
        }


        internal static double GetLatitudeDegrees(
            int faceIndex,
            int x,
            int y,
            int width,
            int height)
        {
            Vector3D direction =
                GetCubemapSphereDirection(
                    faceIndex,
                    x,
                    y,
                    width,
                    height);

            return Math.Asin(
                    direction.Y) *
                (180.0 / Math.PI);
        }


        internal static double ComputeGrassCoverageThreshold(
            long planetSeed,
            int grassCoveragePercent)
        {
            // Endpoint selections are handled directly by the rasterizer and
            // never compare against Threshold. Keep their persisted/networked
            // representation finite so XML recipes can be validated and
            // replayed after a world reload.
            if (grassCoveragePercent <= 0)
                return 0.0;

            if (grassCoveragePercent >= 100)
                return 0.0;


            const int sampleResolution =
                129;

            int sampleCount =
                6 *
                sampleResolution *
                sampleResolution;

            double[] samples =
                new double[
                    sampleCount];

            int sampleIndex =
                0;


            for (int face = 0;
                face < 6;
                face++)
            {
                for (int y = 0;
                    y < sampleResolution;
                    y++)
                {
                    for (int x = 0;
                        x < sampleResolution;
                        x++)
                    {
                        Vector3D direction =
                            GetCubemapSphereDirection(
                                face,
                                x,
                                y,
                                sampleResolution,
                                sampleResolution);

                        samples[sampleIndex++] =
                            PlanetGrassFbm(
                                direction,
                                planetSeed);
                    }
                }
            }


            Array.Sort(
                samples);


            int grassSampleCount =
                (sampleCount *
                    grassCoveragePercent +
                    99) /
                100;

            int thresholdIndex =
                sampleCount -
                grassSampleCount;


            if (thresholdIndex < 0)
                thresholdIndex = 0;

            if (thresholdIndex >= sampleCount)
                thresholdIndex = sampleCount - 1;


            return samples[
                thresholdIndex];
        }


        internal static double ComputeBrushCoverageThreshold(
            long planetSeed,
            int coveragePercent)
        {
            if (coveragePercent <= 0)
                return 1.0;

            if (coveragePercent >= 100)
                return 0.0;


            const int sampleResolution =
                129;

            int sampleCount =
                6 *
                sampleResolution *
                sampleResolution;

            double[] samples =
                new double[
                    sampleCount];

            int sampleIndex =
                0;


            for (int face = 0;
                face < 6;
                face++)
            {
                for (int y = 0;
                    y < sampleResolution;
                    y++)
                {
                    for (int x = 0;
                        x < sampleResolution;
                        x++)
                    {
                        Vector3D direction =
                            GetCubemapSphereDirection(
                                face,
                                x,
                                y,
                                sampleResolution,
                                sampleResolution);

                        double raw =
                            PlanetGrassFbm(
                                direction,
                                planetSeed);

                        double normalized =
                            (raw + 1.0) *
                            0.5;

                        if (normalized < 0.0)
                            normalized = 0.0;
                        else if (normalized > 1.0)
                            normalized = 1.0;

                        samples[sampleIndex++] =
                            normalized;
                    }
                }
            }


            Array.Sort(
                samples);


            int selectedSampleCount =
                (sampleCount *
                    coveragePercent +
                    99) /
                100;

            int thresholdIndex =
                sampleCount -
                selectedSampleCount;


            if (thresholdIndex < 0)
                thresholdIndex = 0;

            if (thresholdIndex >= sampleCount)
                thresholdIndex = sampleCount - 1;


            return samples[
                thresholdIndex];
        }


        internal static double ComputeBrushCoverageThreshold(
            long planetSeed,
            double frequency,
            int octaves,
            int seedOffset,
            int coveragePercent)
        {
            if (coveragePercent <= 0)
                return double.PositiveInfinity;

            if (coveragePercent >= 100)
                return double.NegativeInfinity;

            if (octaves <= 0)
            {
                throw new ArgumentException(
                    "Noise octaves must be greater than zero.",
                    nameof(octaves));
            }


            const int sampleResolution =
                129;

            int sampleCount =
                6 *
                sampleResolution *
                sampleResolution;

            double[] samples =
                new double[
                    sampleCount];

            int sampleIndex =
                0;

            long seed =
                unchecked(
                    planetSeed +
                    seedOffset);


            for (int face = 0;
                face < 6;
                face++)
            {
                for (int y = 0;
                    y < sampleResolution;
                    y++)
                {
                    for (int x = 0;
                        x < sampleResolution;
                        x++)
                    {
                        Vector3D direction =
                            GetCubemapSphereDirection(
                                face,
                                x,
                                y,
                                sampleResolution,
                                sampleResolution);

                        double raw =
                            PlanetFbm(
                                direction,
                                seed,
                                frequency,
                                octaves);

                        double normalized =
                            (raw + 1.0) *
                            0.5;

                        if (normalized < 0.0)
                            normalized = 0.0;
                        else if (normalized > 1.0)
                            normalized = 1.0;

                        samples[sampleIndex++] =
                            normalized;
                    }
                }
            }


            Array.Sort(
                samples);


            int selectedSampleCount =
                (sampleCount *
                    coveragePercent +
                    99) /
                100;

            int thresholdIndex =
                sampleCount -
                selectedSampleCount;


            if (thresholdIndex < 0)
                thresholdIndex = 0;

            if (thresholdIndex >= sampleCount)
                thresholdIndex = sampleCount - 1;


            return samples[
                thresholdIndex];
        }


        private static double PlanetGrassFbm(
            Vector3D direction,
            long planetSeed)
        {
            return PlanetFbm(
                direction,
                planetSeed,
                2.15,
                4);
        }


        private static double PlanetFbm(
            Vector3D direction,
            long planetSeed,
            double frequency,
            int octaves)
        {
            return FbmNoise3D.SampleFbm(
                direction.X,
                direction.Y,
                direction.Z,
                planetSeed,
                frequency,
                octaves,
                2.07,
                0.5);
        }


        private static double LerpNoise(
            double a,
            double b,
            double amount)
        {
            return a +
                (b - a) *
                amount;
        }



    }
}
