using System;

namespace ProceduralCubemapApi.Common.Noise
{
    internal enum ProceduralNoiseKind
    {
        Fbm = 0,
        Value = 1,
        Ridged = 2,
        Billow = 3,
        DomainWarpFbm = 4,
        CellularNearest = 5,
        CellularEdge = 6,
        CellularValue = 7,
        DirectionalWave = 8,
        DomainWarpWave = 9
    }

    internal static class ProceduralNoiseField
    {
        internal static INoise3D Create(
            ProceduralNoiseKind type,
            long seed,
            double frequency,
            int octaves)
        {
            switch (type)
            {
                case ProceduralNoiseKind.Value:
                    return new ScaledNoise3D(
                        new ValueNoise3D(seed),
                        frequency);

                case ProceduralNoiseKind.Fbm:
                    return new FbmNoise3D(seed, frequency, octaves);

                case ProceduralNoiseKind.Ridged:
                    return new RidgedNoise3D(seed, frequency, octaves);

                case ProceduralNoiseKind.Billow:
                    return new BillowNoise3D(seed, frequency, octaves);

                case ProceduralNoiseKind.DomainWarpFbm:
                {
                    INoise3D source = new FbmNoise3D(seed, frequency, octaves);
                    INoise3D warp = new FbmNoise3D(
                        NoiseMath.DeriveSeed(seed, 0x57415250L),
                        Math.Max(0.25, frequency * 0.45),
                        Math.Max(1, Math.Min(3, octaves)));
                    return new DomainWarpNoise3D(source, warp, 0.35, seed);
                }

                case ProceduralNoiseKind.CellularNearest:
                    return new CellularNoise3D(seed, frequency, CellularNoiseMode.NearestDistance);

                case ProceduralNoiseKind.CellularEdge:
                    return new CellularNoise3D(seed, frequency, CellularNoiseMode.EdgeDistance);

                case ProceduralNoiseKind.CellularValue:
                    return new CellularNoise3D(seed, frequency, CellularNoiseMode.CellValue);

                case ProceduralNoiseKind.DirectionalWave:
                    return CreateWave(seed, frequency);

                case ProceduralNoiseKind.DomainWarpWave:
                {
                    INoise3D source = CreateWave(seed, frequency);
                    INoise3D warp = new FbmNoise3D(
                        NoiseMath.DeriveSeed(seed, 0x44554E45L),
                        Math.Max(0.25, frequency * 0.12),
                        Math.Max(1, Math.Min(3, octaves)));
                    return new DomainWarpNoise3D(source, warp, 0.18, seed);
                }

                default:
                    throw new ArgumentException("Unknown procedural noise type.", "type");
            }
        }

        private static INoise3D CreateWave(long seed, double frequency)
        {
            double x = Signed(seed, 0x11u);
            double y = Signed(seed, 0x29u);
            double z = Signed(seed, 0x47u);

            if (x * x + y * y + z * z < 0.0001)
                x = 1.0;

            double phase = NoiseMath.HashToUnit(
                NoiseMath.HashLattice(0, 0, 0, NoiseMath.DeriveSeed(seed, 0x50484153L))) *
                Math.PI * 2.0;

            return new DirectionalWaveNoise3D(x, y, z, frequency, phase);
        }

        private static double Signed(long seed, uint salt)
        {
            uint hash = NoiseMath.HashLattice(0, 0, 0, seed);
            unchecked
            {
                hash ^= salt;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
            }
            return NoiseMath.HashToSignedUnit(hash);
        }

        private sealed class ScaledNoise3D : INoise3D
        {
            private readonly INoise3D _source;
            private readonly double _frequency;

            internal ScaledNoise3D(INoise3D source, double frequency)
            {
                _source = source;
                _frequency = frequency;
            }

            public double Sample(double x, double y, double z)
            {
                return _source.Sample(
                    x * _frequency,
                    y * _frequency,
                    z * _frequency);
            }
        }
    }
}
