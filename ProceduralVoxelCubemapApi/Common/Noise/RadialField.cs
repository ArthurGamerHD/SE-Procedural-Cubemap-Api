using System;
using VRageMath;

namespace VoxelCubemapApi.Common.Noise
{
    /// <summary>
    /// Planet-space radial scalar field. The center is a normalized direction
    /// and the radius is an angular distance on the sphere.
    /// </summary>
    internal sealed class RadialField
    {
        private readonly Vector3D _center;
        private readonly double _radiusRadians;
        private readonly double _cosRadius;
        private readonly int _profile;

        internal RadialField(
            double centerX,
            double centerY,
            double centerZ,
            double radiusDegrees,
            int profile)
        {
            Vector3D center = new Vector3D(centerX, centerY, centerZ);
            double lengthSquared = center.LengthSquared();
            if (lengthSquared > 1e-12)
                center /= Math.Sqrt(lengthSquared);
            else
                center = Vector3D.UnitX;

            _center = center;
            _radiusRadians = radiusDegrees * (Math.PI / 180.0);
            _cosRadius = Math.Cos(_radiusRadians);
            _profile = profile;
        }

        internal double Sample(Vector3D direction)
        {
            double dot = Vector3D.Dot(direction, _center);

            // Most pixels of a small radial field are rejected without Acos.
            if (dot <= _cosRadius)
                return 0.0;

            if (dot > 1.0)
                dot = 1.0;
            else if (dot < -1.0)
                dot = -1.0;

            double t = Math.Acos(dot) / _radiusRadians;
            if (t >= 1.0)
                return 0.0;

            double inverse = 1.0 - t;

            switch (_profile)
            {
                case 0: // Linear
                    return inverse;

                case 1: // Smooth
                {
                    double smooth = t * t * (3.0 - 2.0 * t);
                    return 1.0 - smooth;
                }

                case 2: // Bowl
                {
                    double bowl = 1.0 - t * t;
                    return bowl * bowl;
                }

                case 3: // Crater: flat/deep floor, sloped wall, raised rim.
                    return SampleCrater(t);

                default:
                    return 0.0;
            }
        }

        private static double SampleCrater(double t)
        {
            // Radial half-profile (center -> outside):
            //
            //   floor            continuous wall -> rim       outside
            //  ________                                  /\
            //          \_______________________________/  \____
            //
            // There is deliberately no explicit zero-height band between the
            // bowl wall and the rim. One smooth monotonic segment goes from
            // the depressed floor directly to the positive rim peak, crossing
            // zero with a non-zero slope.
            const double floorEnd = 0.35;
            const double rimPeak = 0.84;
            const double rimHeight = 0.35;

            if (t <= floorEnd)
                return -1.0;

            if (t < rimPeak)
            {
                double u = (t - floorEnd) / (rimPeak - floorEnd);
                double shaped = SmoothStep01(u);

                // Continuous transition -1 -> +rimHeight. Unlike the previous
                // profile, zero is only crossed; it is not a control point with
                // zero derivative on both sides.
                return -1.0 + (1.0 + rimHeight) * shaped;
            }

            {
                double u = (t - rimPeak) / (1.0 - rimPeak);
                return rimHeight * (1.0 - SmoothStep01(u));
            }
        }


        private static double SmoothStep01(double value)
        {
            if (value <= 0.0)
                return 0.0;

            if (value >= 1.0)
                return 1.0;

            return value * value * (3.0 - 2.0 * value);
        }

    }
}
