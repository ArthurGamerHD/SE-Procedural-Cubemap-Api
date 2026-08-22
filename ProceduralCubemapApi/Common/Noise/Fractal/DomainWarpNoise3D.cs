using System;

namespace ProceduralCubemapApi.Common.Noise
{
    /// <summary>
    /// Warps the coordinates of one field with another field before sampling
    /// the source. Three fixed offsets decorrelate the X/Y/Z warp channels.
    /// </summary>
    internal sealed class DomainWarpNoise3D : INoise3D
    {
        private readonly INoise3D _source;
        private readonly INoise3D _warp;
        private readonly double _strength;

        private readonly double _offsetX;
        private readonly double _offsetY;
        private readonly double _offsetZ;


        internal DomainWarpNoise3D(
            INoise3D source,
            INoise3D warp,
            double strength,
            long seed)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (warp == null)
                throw new ArgumentNullException("warp");

            _source = source;
            _warp = warp;
            _strength = strength;

            _offsetX = SeedOffset(seed, 17L);
            _offsetY = SeedOffset(seed, 31L);
            _offsetZ = SeedOffset(seed, 47L);
        }


        public double Sample(
            double x,
            double y,
            double z)
        {
            double warpX =
                _warp.Sample(
                    x + _offsetX,
                    y,
                    z);

            double warpY =
                _warp.Sample(
                    x,
                    y + _offsetY,
                    z);

            double warpZ =
                _warp.Sample(
                    x,
                    y,
                    z + _offsetZ);

            return _source.Sample(
                x + warpX * _strength,
                y + warpY * _strength,
                z + warpZ * _strength);
        }


        private static double SeedOffset(
            long seed,
            long salt)
        {
            long mixed =
                NoiseMath.DeriveSeed(
                    seed,
                    salt);

            uint hash =
                unchecked(
                    (uint)mixed ^
                    (uint)(mixed >> 32));

            // A non-integer offset avoids accidentally lining the warp
            // channels back up with lattice points.
            return 19.0 +
                NoiseMath.HashToUnit(hash) *
                173.0;
        }
    }
}
