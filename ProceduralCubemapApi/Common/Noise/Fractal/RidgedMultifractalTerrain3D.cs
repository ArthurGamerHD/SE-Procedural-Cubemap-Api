using System;

namespace ProceduralCubemapApi.Common.Noise
{
    /// <summary>
    /// Ridged multifractal terrain sampler. Unlike RidgedNoise3D, octave
    /// amplitudes are not independent: strong ridges in one octave increase
    /// the contribution of finer octaves, producing branching ridge systems
    /// with nested peaks and valleys.
    ///
    /// The returned value is normalized to [0,1].
    /// </summary>
    internal sealed class RidgedMultifractalTerrain3D : INoise3D
    {
        private readonly long _seed;
        private readonly double _frequency;
        private readonly int _octaves;
        private readonly double _lacunarity;
        private readonly double _gain;
        private readonly double _offset;
        private readonly double[] _spectralWeights;
        private readonly double _normalization;

        internal RidgedMultifractalTerrain3D(
            long seed,
            double frequency,
            int octaves)
            : this(seed, frequency, octaves, 2.03, 2.15, 1.0, 1.0)
        {
        }

        internal RidgedMultifractalTerrain3D(
            long seed,
            double frequency,
            int octaves,
            double lacunarity,
            double gain,
            double offset,
            double spectralExponent)
        {
            _seed = seed;
            _frequency = Math.Max(0.0001, frequency);
            _octaves = Math.Max(1, octaves);
            _lacunarity = Math.Max(1.01, lacunarity);
            _gain = Math.Max(0.01, gain);
            _offset = Math.Max(0.01, offset);

            _spectralWeights = new double[_octaves];
            double octaveFrequency = 1.0;
            double normalization = 0.0;
            double maximumSignal = _offset * _offset;

            for (int octave = 0; octave < _octaves; octave++)
            {
                double weight = Math.Pow(octaveFrequency, -spectralExponent);
                _spectralWeights[octave] = weight;
                normalization += maximumSignal * weight;
                octaveFrequency *= _lacunarity;
            }

            _normalization = Math.Max(1e-12, normalization);
        }

        public double Sample(double x, double y, double z)
        {
            double frequency = _frequency;
            double feedbackWeight = 1.0;
            double result = 0.0;

            for (int octave = 0; octave < _octaves; octave++)
            {
                long octaveSeed = unchecked(
                    _seed + octave * NoiseMath.OCTAVE_SEED_STEP);

                double value = ValueNoise3D.Sample(
                    x * frequency,
                    y * frequency,
                    z * frequency,
                    octaveSeed);

                // Classic ridged multifractal signal: fold around zero, invert,
                // square to sharpen the ridge, then use the previous octave as
                // feedback so fine structure grows preferentially on mountains.
                double signal = _offset - Math.Abs(value);
                if (signal < 0.0)
                    signal = 0.0;
                signal *= signal;
                signal *= feedbackWeight;

                feedbackWeight = signal * _gain;
                if (feedbackWeight < 0.0)
                    feedbackWeight = 0.0;
                else if (feedbackWeight > 1.0)
                    feedbackWeight = 1.0;

                result += signal * _spectralWeights[octave];
                frequency *= _lacunarity;
            }

            result /= _normalization;
            if (result < 0.0)
                return 0.0;
            if (result > 1.0)
                return 1.0;
            return result;
        }
    }
}
