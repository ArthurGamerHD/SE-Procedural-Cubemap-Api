namespace VoxelCubemapApi.Common.Noise
{
    /// <summary>
    /// Smooth 3D value noise in approximately [-1,1]. Values are generated
    /// from a deterministic lattice hash and quintic interpolation.
    /// </summary>
    internal sealed class ValueNoise3D : INoise3D
    {
        private readonly long _seed;


        internal ValueNoise3D(
            long seed)
        {
            _seed = seed;
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
                _seed);
        }


        internal static double Sample(
            double x,
            double y,
            double z,
            long seed)
        {
            int x0 = NoiseMath.FastFloor(x);
            int y0 = NoiseMath.FastFloor(y);
            int z0 = NoiseMath.FastFloor(z);

            int x1 = x0 + 1;
            int y1 = y0 + 1;
            int z1 = z0 + 1;

            double tx = NoiseMath.FadeQuintic(x - x0);
            double ty = NoiseMath.FadeQuintic(y - y0);
            double tz = NoiseMath.FadeQuintic(z - z0);

            double n000 = LatticeValue(x0, y0, z0, seed);
            double n100 = LatticeValue(x1, y0, z0, seed);
            double n010 = LatticeValue(x0, y1, z0, seed);
            double n110 = LatticeValue(x1, y1, z0, seed);
            double n001 = LatticeValue(x0, y0, z1, seed);
            double n101 = LatticeValue(x1, y0, z1, seed);
            double n011 = LatticeValue(x0, y1, z1, seed);
            double n111 = LatticeValue(x1, y1, z1, seed);

            double nx00 = NoiseMath.Lerp(n000, n100, tx);
            double nx10 = NoiseMath.Lerp(n010, n110, tx);
            double nx01 = NoiseMath.Lerp(n001, n101, tx);
            double nx11 = NoiseMath.Lerp(n011, n111, tx);

            double nxy0 = NoiseMath.Lerp(nx00, nx10, ty);
            double nxy1 = NoiseMath.Lerp(nx01, nx11, ty);

            return NoiseMath.Lerp(
                nxy0,
                nxy1,
                tz);
        }


        private static double LatticeValue(
            int x,
            int y,
            int z,
            long seed)
        {
            return NoiseMath.HashToSignedUnit(
                NoiseMath.HashLattice(
                    x,
                    y,
                    z,
                    seed));
        }
    }
}
