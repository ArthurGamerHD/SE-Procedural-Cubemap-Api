using System;

namespace ProceduralCubemapApi.Common.Noise
{
    internal static class NoiseMath
    {
        internal const long OCTAVE_SEED_STEP = 104729L;

        internal static int FastFloor(
            double value)
        {
            int integer = (int)value;

            return value < integer
                ? integer - 1
                : integer;
        }


        internal static double FadeQuintic(
            double value)
        {
            return value *
                value *
                value *
                (value *
                    (value * 6.0 - 15.0) +
                    10.0);
        }


        internal static double Lerp(
            double a,
            double b,
            double amount)
        {
            return a +
                (b - a) *
                amount;
        }


        internal static double Clamp01(
            double value)
        {
            if (value <= 0.0)
                return 0.0;

            if (value >= 1.0)
                return 1.0;

            return value;
        }


        internal static double To01(
            double value)
        {
            return Clamp01(
                (value + 1.0) *
                0.5);
        }


        internal static long DeriveSeed(
            long seed,
            long salt)
        {
            ulong value = unchecked((ulong)seed);
            ulong addition = unchecked((ulong)salt);

            unchecked
            {
                value +=
                    0x9E3779B97F4A7C15UL +
                    addition * 0xBF58476D1CE4E5B9UL;

                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
            }

            return unchecked((long)value);
        }


        internal static uint HashLattice(
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

                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
            }

            return hash;
        }


        internal static double HashToSignedUnit(
            uint hash)
        {
            return
                ((double)hash /
                    4294967295.0) *
                2.0 -
                1.0;
        }


        internal static double HashToUnit(
            uint hash)
        {
            return
                (double)hash /
                4294967295.0;
        }


        internal static double HashToUnit(
            int x,
            int y,
            int z,
            long seed)
        {
            return HashToUnit(
                HashLattice(
                    x,
                    y,
                    z,
                    seed));
        }


        internal static double HashToUnit(
            int x,
            int y,
            int z,
            long seed,
            uint salt)
        {
            uint hash =
                HashLattice(
                    x,
                    y,
                    z,
                    seed);

            unchecked
            {
                hash ^= salt;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
            }

            return HashToUnit(
                hash);
        }
    }
}
