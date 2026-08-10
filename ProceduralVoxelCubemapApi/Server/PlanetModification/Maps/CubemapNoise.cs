using System;

using VRageMath;

namespace VoxelCubemapApi.Server.PlanetModification.Maps
{
    internal static class CubemapNoise
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


        private static Vector3D GetCubemapSphereDirection(
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


            // Orientation recovered from the actual Space Engineers planet-map
            // edge relationships:
            //
            // front L == left R
            // front R == right L
            // front T == up B
            // front B == reversed down B
            // back  L == right R
            // back  R == left L
            //
            // Sampling one continuous 3D field with these vectors makes shared
            // face edges and all cube corners evaluate identically.
            switch (faceIndex)
            {
                case 0:
                    direction =
                        new Vector3D(
                            u,
                            -v,
                            1.0);
                    break;

                case 1:
                    direction =
                        new Vector3D(
                            -u,
                            -v,
                            -1.0);
                    break;

                case 2:
                    direction =
                        new Vector3D(
                            -1.0,
                            -v,
                            u);
                    break;

                case 3:
                    direction =
                        new Vector3D(
                            1.0,
                            -v,
                            -u);
                    break;

                case 4:
                    direction =
                        new Vector3D(
                            u,
                            1.0,
                            v);
                    break;

                case 5:
                    direction =
                        new Vector3D(
                            -u,
                            -1.0,
                            v);
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
            const int GridResolution =
                129;

            double[] grid =
                new double[
                    GridResolution *
                    GridResolution];

            int offset =
                0;


            for (int y = 0;
                y < GridResolution;
                y++)
            {
                for (int x = 0;
                    x < GridResolution;
                    x++)
                {
                    Vector3D direction =
                        GetCubemapSphereDirection(
                            faceIndex,
                            x,
                            y,
                            GridResolution,
                            GridResolution);

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
            const int GridResolution =
                129;

            double[] grid =
                new double[
                    GridResolution *
                    GridResolution];

            int offset =
                0;

            long seed =
                unchecked(
                    planetSeed +
                    seedOffset);

            for (int y = 0;
                y < GridResolution;
                y++)
            {
                for (int x = 0;
                    x < GridResolution;
                    x++)
                {
                    Vector3D direction =
                        GetCubemapSphereDirection(
                            faceIndex,
                            x,
                            y,
                            GridResolution,
                            GridResolution);

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

                    grid[offset++] =
                        normalized;
                }
            }

            return grid;
        }


        internal static double SampleGrassNoiseGrid(
            double[] grid,
            int x,
            int y,
            int width,
            int height)
        {
            const int GridResolution =
                129;

            const int GridMaximum =
                GridResolution - 1;


            double gridX =
                width <= 1
                    ? 0.0
                    : (double)x *
                        GridMaximum /
                        (width - 1.0);

            double gridY =
                height <= 1
                    ? 0.0
                    : (double)y *
                        GridMaximum /
                        (height - 1.0);


            int x0 =
                (int)gridX;

            int y0 =
                (int)gridY;

            int x1 =
                x0 < GridMaximum
                    ? x0 + 1
                    : x0;

            int y1 =
                y0 < GridMaximum
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
                        GridResolution +
                        x0],
                    grid[
                        y0 *
                        GridResolution +
                        x1],
                    tx);

            double bottom =
                LerpNoise(
                    grid[
                        y1 *
                        GridResolution +
                        x0],
                    grid[
                        y1 *
                        GridResolution +
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
            if (grassCoveragePercent <= 0)
                return double.PositiveInfinity;

            if (grassCoveragePercent >= 100)
                return double.NegativeInfinity;


            const int SampleResolution =
                129;

            int sampleCount =
                6 *
                SampleResolution *
                SampleResolution;

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
                    y < SampleResolution;
                    y++)
                {
                    for (int x = 0;
                        x < SampleResolution;
                        x++)
                    {
                        Vector3D direction =
                            GetCubemapSphereDirection(
                                face,
                                x,
                                y,
                                SampleResolution,
                                SampleResolution);

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

            double amplitude =
                1.0;

            double sum =
                0.0;

            double amplitudeSum =
                0.0;


            for (int octave = 0;
                octave < octaves;
                octave++)
            {
                long octaveSeed =
                    unchecked(
                        planetSeed +
                        octave * 104729L);

                sum +=
                    ValueNoise3D(
                        direction.X * frequency,
                        direction.Y * frequency,
                        direction.Z * frequency,
                        octaveSeed) *
                    amplitude;

                amplitudeSum +=
                    amplitude;

                frequency *=
                    2.07;

                amplitude *=
                    0.5;
            }


            return sum /
                amplitudeSum;
        }


        private static double ValueNoise3D(
            double x,
            double y,
            double z,
            long seed)
        {
            int x0 =
                FastFloor(
                    x);

            int y0 =
                FastFloor(
                    y);

            int z0 =
                FastFloor(
                    z);

            int x1 =
                x0 + 1;

            int y1 =
                y0 + 1;

            int z1 =
                z0 + 1;


            double tx =
                SmoothNoiseFraction(
                    x - x0);

            double ty =
                SmoothNoiseFraction(
                    y - y0);

            double tz =
                SmoothNoiseFraction(
                    z - z0);


            double n000 =
                LatticeNoiseValue(
                    x0,
                    y0,
                    z0,
                    seed);

            double n100 =
                LatticeNoiseValue(
                    x1,
                    y0,
                    z0,
                    seed);

            double n010 =
                LatticeNoiseValue(
                    x0,
                    y1,
                    z0,
                    seed);

            double n110 =
                LatticeNoiseValue(
                    x1,
                    y1,
                    z0,
                    seed);

            double n001 =
                LatticeNoiseValue(
                    x0,
                    y0,
                    z1,
                    seed);

            double n101 =
                LatticeNoiseValue(
                    x1,
                    y0,
                    z1,
                    seed);

            double n011 =
                LatticeNoiseValue(
                    x0,
                    y1,
                    z1,
                    seed);

            double n111 =
                LatticeNoiseValue(
                    x1,
                    y1,
                    z1,
                    seed);


            double nx00 =
                LerpNoise(
                    n000,
                    n100,
                    tx);

            double nx10 =
                LerpNoise(
                    n010,
                    n110,
                    tx);

            double nx01 =
                LerpNoise(
                    n001,
                    n101,
                    tx);

            double nx11 =
                LerpNoise(
                    n011,
                    n111,
                    tx);

            double nxy0 =
                LerpNoise(
                    nx00,
                    nx10,
                    ty);

            double nxy1 =
                LerpNoise(
                    nx01,
                    nx11,
                    ty);


            return LerpNoise(
                nxy0,
                nxy1,
                tz);
        }


        private static int FastFloor(
            double value)
        {
            int integer =
                (int)value;

            return value < integer
                ? integer - 1
                : integer;
        }


        private static double SmoothNoiseFraction(
            double value)
        {
            // Quintic fade: continuous first and second derivatives.
            return value *
                value *
                value *
                (value *
                    (value * 6.0 - 15.0) +
                    10.0);
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


        private static double LatticeNoiseValue(
            int x,
            int y,
            int z,
            long seed)
        {
            uint hash =
                unchecked(
                    (uint)seed ^
                    (uint)(seed >> 32));


            unchecked
            {
                hash ^=
                    (uint)x *
                    0x9E3779B9u;

                hash ^=
                    (uint)y *
                    0x85EBCA6Bu;

                hash ^=
                    (uint)z *
                    0xC2B2AE35u;

                hash ^=
                    hash >> 16;

                hash *=
                    0x7FEB352Du;

                hash ^=
                    hash >> 15;

                hash *=
                    0x846CA68Bu;

                hash ^=
                    hash >> 16;
            }


            return
                ((double)hash /
                    4294967295.0) *
                2.0 -
                1.0;
        }


    }
}
