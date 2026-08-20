using System;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;

namespace VoxelCubemapApi.Common.Noise
{
    internal enum CellularNoiseMode
    {
        NearestDistance = 0,
        SecondDistance = 1,
        EdgeDistance = 2,
        CellValue = 3
    }


    /// <summary>
    /// 3D Worley/cellular noise. Each integer cell owns one deterministic
    /// feature point. Distance modes return values normalized to [-1,1].
    /// </summary>
    internal sealed class CellularNoise3D : INoise3D
    {
        private const double MAXIMUM_CELL_DISTANCE = 1.7320508075688772;

        private readonly long _seed;
        private readonly double _frequency;
        private readonly double _jitter;
        private readonly CellularNoiseMode _mode;


        internal CellularNoise3D(
            long seed,
            double frequency,
            CellularNoiseMode mode)
            : this(
                seed,
                frequency,
                1.0,
                mode)
        {
        }


        internal CellularNoise3D(
            long seed,
            double frequency,
            double jitter,
            CellularNoiseMode mode)
        {
            if (frequency <= 0.0)
                throw new ArgumentOutOfRangeException("frequency");

            if (jitter < 0.0 || jitter > 1.0)
                throw new ArgumentOutOfRangeException("jitter");

            _seed = seed;
            _frequency = frequency;
            _jitter = jitter;
            _mode = mode;
        }


        public double Sample(
            double x,
            double y,
            double z)
        {
            x *= _frequency;
            y *= _frequency;
            z *= _frequency;

            int cellX = NoiseMath.FastFloor(x);
            int cellY = NoiseMath.FastFloor(y);
            int cellZ = NoiseMath.FastFloor(z);

            double nearestSquared = double.MaxValue;
            double secondSquared = double.MaxValue;
            double nearestValue = 0.0;

            for (int dz = -1; dz <= 1; dz++)
            {
                int candidateZ = cellZ + dz;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int candidateY = cellY + dy;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int candidateX = cellX + dx;

                        double px =
                            candidateX +
                            FeatureOffset(
                                candidateX,
                                candidateY,
                                candidateZ,
                                0xA511E9B3u);

                        double py =
                            candidateY +
                            FeatureOffset(
                                candidateX,
                                candidateY,
                                candidateZ,
                                0x63D83595u);

                        double pz =
                            candidateZ +
                            FeatureOffset(
                                candidateX,
                                candidateY,
                                candidateZ,
                                0xB8D4E945u);

                        double differenceX = x - px;
                        double differenceY = y - py;
                        double differenceZ = z - pz;

                        double distanceSquared =
                            differenceX * differenceX +
                            differenceY * differenceY +
                            differenceZ * differenceZ;

                        if (distanceSquared < nearestSquared)
                        {
                            secondSquared = nearestSquared;
                            nearestSquared = distanceSquared;
                            nearestValue =
                                NoiseMath.HashToSignedUnit(
                                    NoiseMath.HashLattice(
                                        candidateX,
                                        candidateY,
                                        candidateZ,
                                        NoiseMath.DeriveSeed(
                                            _seed,
                                            73L)));
                        }
                        else if (distanceSquared < secondSquared)
                        {
                            secondSquared = distanceSquared;
                        }
                    }
                }
            }

            if (_mode == CellularNoiseMode.CellValue)
                return nearestValue;

            double nearest = Math.Sqrt(nearestSquared);
            double second = Math.Sqrt(secondSquared);

            if (_mode == CellularNoiseMode.SecondDistance)
                return NormalizeDistance(second);

            if (_mode == CellularNoiseMode.EdgeDistance)
            {
                // F2-F1 is near zero at cell borders and larger toward cell
                // centers. Scale by the same conservative cell-distance bound.
                return NormalizeDistance(
                    second - nearest);
            }

            return NormalizeDistance(nearest);
        }


        private double FeatureOffset(
            int x,
            int y,
            int z,
            uint salt)
        {
            double random =
                NoiseMath.HashToUnit(
                    x,
                    y,
                    z,
                    _seed,
                    salt);

            // Jitter 0 keeps every feature point at the center of its cell;
            // jitter 1 lets it span the full cell while keeping a small inset.
            double centered =
                0.5 +
                (random - 0.5) *
                _jitter;

            const double inset = 0.000001;

            if (centered < inset)
                return inset;

            if (centered > 1.0 - inset)
                return 1.0 - inset;

            return centered;
        }


        private static double NormalizeDistance(
            double distance)
        {
            double normalized =
                NoiseMath.Clamp01(
                    distance /
                    MAXIMUM_CELL_DISTANCE);

            return normalized * 2.0 - 1.0;
        }
    }
}
