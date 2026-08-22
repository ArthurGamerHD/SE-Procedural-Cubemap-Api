using System;

namespace ProceduralCubemapApi.Common.Noise
{
    /// <summary>
    /// Deterministic directional sinusoidal field. Useful as the base pattern
    /// for dunes or wind-shaped bands; irregularity can be added by wrapping
    /// it in DomainWarpNoise3D.
    /// </summary>
    internal sealed class DirectionalWaveNoise3D : INoise3D
    {
        private readonly double _directionX;
        private readonly double _directionY;
        private readonly double _directionZ;
        private readonly double _angularFrequency;
        private readonly double _phase;


        internal DirectionalWaveNoise3D(
            double directionX,
            double directionY,
            double directionZ,
            double frequency,
            double phase)
        {
            double lengthSquared =
                directionX * directionX +
                directionY * directionY +
                directionZ * directionZ;

            if (lengthSquared <= 0.0)
                throw new ArgumentException("Wave direction cannot be zero.");

            if (frequency <= 0.0)
                throw new ArgumentException("frequency");

            double inverseLength =
                1.0 /
                Math.Sqrt(lengthSquared);

            _directionX = directionX * inverseLength;
            _directionY = directionY * inverseLength;
            _directionZ = directionZ * inverseLength;
            _angularFrequency = frequency * Math.PI * 2.0;
            _phase = phase;
        }


        public double Sample(
            double x,
            double y,
            double z)
        {
            double coordinate =
                x * _directionX +
                y * _directionY +
                z * _directionZ;

            return Math.Sin(
                coordinate * _angularFrequency +
                _phase);
        }
    }
}
