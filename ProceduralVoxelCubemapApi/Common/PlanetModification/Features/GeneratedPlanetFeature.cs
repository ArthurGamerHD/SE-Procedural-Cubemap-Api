using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.Features
{
    internal abstract class GeneratedPlanetFeature
    {
        internal Vector3D Center;
        internal double RadiusRadians;
        internal double CosRadius;
        internal double SinRadius;

        internal abstract void Accumulate(
            Vector3D direction,
            ref FeaturePixelAccumulator accumulator);
    }

    internal struct FeaturePixelAccumulator
    {
        internal double AdditiveDelta;
        internal double StrongestPositiveDelta;
        internal double StrongestNegativeDelta;

        internal double TotalDelta
        {
            get { return AdditiveDelta + StrongestPositiveDelta + StrongestNegativeDelta; }
        }
    }
}
