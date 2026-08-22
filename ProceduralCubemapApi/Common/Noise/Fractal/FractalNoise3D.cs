using System;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;

namespace ProceduralCubemapApi.Common.Noise
{
    internal enum FractalNoiseMode
    {
        Fbm = 0,
        Ridged = 1,
        Billow = 2
    }


    /// <summary>
    /// Multi-octave wrapper over deterministic 3D value noise. The default
    /// lacunarity/persistence match the legacy FractalBrownianMotion sampler.
    /// </summary>
    internal class FractalNoise3D : INoise3D
    {
        private readonly long _seed;
        private readonly double _frequency;
        private readonly int _octaves;
        private readonly double _lacunarity;
        private readonly double _persistence;
        private readonly FractalNoiseMode _mode;
        private readonly double _ridgeSharpness;


        internal FractalNoise3D(
            long seed,
            double frequency,
            int octaves)
            : this(
                seed,
                frequency,
                octaves,
                2.07,
                0.5,
                FractalNoiseMode.Fbm,
                2.0)
        {
        }


        internal FractalNoise3D(
            long seed,
            double frequency,
            int octaves,
            double lacunarity,
            double persistence,
            FractalNoiseMode mode,
            double ridgeSharpness)
        {
            if (frequency <= 0.0)
                throw new ArgumentOutOfRangeException("frequency");

            if (octaves <= 0)
                throw new ArgumentOutOfRangeException("octaves");

            if (lacunarity <= 0.0)
                throw new ArgumentOutOfRangeException("lacunarity");

            if (persistence <= 0.0)
                throw new ArgumentOutOfRangeException("persistence");

            if (ridgeSharpness <= 0.0)
                throw new ArgumentOutOfRangeException("ridgeSharpness");

            _seed = seed;
            _frequency = frequency;
            _octaves = octaves;
            _lacunarity = lacunarity;
            _persistence = persistence;
            _mode = mode;
            _ridgeSharpness = ridgeSharpness;
        }


        public double Sample(
            double x,
            double y,
            double z)
        {
            return Sample(
                x,
                y,
                z,
                _seed,
                _frequency,
                _octaves,
                _lacunarity,
                _persistence,
                _mode,
                _ridgeSharpness);
        }


        internal static double Sample(
            double x,
            double y,
            double z,
            long seed,
            double frequency,
            int octaves,
            double lacunarity,
            double persistence,
            FractalNoiseMode mode,
            double ridgeSharpness)
        {
            double amplitude = 1.0;
            double sum = 0.0;
            double amplitudeSum = 0.0;

            for (int octave = 0;
                octave < octaves;
                octave++)
            {
                long octaveSeed =
                    unchecked(
                        seed +
                        octave * NoiseMath.OCTAVE_SEED_STEP);

                double value =
                    ValueNoise3D.Sample(
                        x * frequency,
                        y * frequency,
                        z * frequency,
                        octaveSeed);

                switch (mode)
                {
                    case FractalNoiseMode.Ridged:
                        value =
                            1.0 -
                            Math.Abs(value);

                        value = Math.Pow(
                            value,
                            ridgeSharpness);

                        // Keep the common noise contract in [-1,1].
                        value = value * 2.0 - 1.0;
                        break;

                    case FractalNoiseMode.Billow:
                        value =
                            Math.Abs(value) *
                            2.0 -
                            1.0;
                        break;
                }

                sum += value * amplitude;
                amplitudeSum += amplitude;

                frequency *= lacunarity;
                amplitude *= persistence;
            }

            if (amplitudeSum <= 0.0)
                return 0.0;

            return sum / amplitudeSum;
        }
    }


    /// <summary>
    /// Standard fractal Brownian motion over deterministic 3D value noise.
    /// This is the single FBM implementation used by the legacy cubemap wrapper
    /// and by newer procedural field consumers.
    /// </summary>
    internal sealed class FbmNoise3D : FractalNoise3D
    {
        internal FbmNoise3D(
            long seed,
            double frequency,
            int octaves)
            : this(
                seed,
                frequency,
                octaves,
                2.07,
                0.5)
        {
        }


        internal FbmNoise3D(
            long seed,
            double frequency,
            int octaves,
            double lacunarity,
            double persistence)
            : base(
                seed,
                frequency,
                octaves,
                lacunarity,
                persistence,
                FractalNoiseMode.Fbm,
                2.0)
        {
        }


        internal static double SampleFbm(
            double x,
            double y,
            double z,
            long seed,
            double frequency,
            int octaves,
            double lacunarity,
            double persistence)
        {
            return FractalNoise3D.Sample(
                x,
                y,
                z,
                seed,
                frequency,
                octaves,
                lacunarity,
                persistence,
                FractalNoiseMode.Fbm,
                2.0);
        }
    }


    internal sealed class RidgedNoise3D : FractalNoise3D
    {
        internal RidgedNoise3D(
            long seed,
            double frequency,
            int octaves)
            : this(
                seed,
                frequency,
                octaves,
                2.07,
                0.5,
                2.0)
        {
        }


        internal RidgedNoise3D(
            long seed,
            double frequency,
            int octaves,
            double lacunarity,
            double persistence,
            double sharpness)
            : base(
                seed,
                frequency,
                octaves,
                lacunarity,
                persistence,
                FractalNoiseMode.Ridged,
                sharpness)
        {
        }
    }


    internal sealed class BillowNoise3D : FractalNoise3D
    {
        internal BillowNoise3D(
            long seed,
            double frequency,
            int octaves)
            : this(
                seed,
                frequency,
                octaves,
                2.07,
                0.5)
        {
        }


        internal BillowNoise3D(
            long seed,
            double frequency,
            int octaves,
            double lacunarity,
            double persistence)
            : base(
                seed,
                frequency,
                octaves,
                lacunarity,
                persistence,
                FractalNoiseMode.Billow,
                2.0)
        {
        }
    }
}
