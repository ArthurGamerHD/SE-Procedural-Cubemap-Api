using VRageMath;

namespace ProceduralCubemapApi.Common.PlanetModification.Features
{
    internal abstract class GeneratedPlanetFeature
    {
        internal Vector3D Center;
        internal double RadiusRadians;
        internal double CosRadius;
        internal double SinRadius;

        internal virtual bool IsAbsoluteHeightFeature => false;
        

        internal abstract void Accumulate(
            Vector3D direction,
            int currentHeight,
            ref FeaturePixelAccumulator accumulator);
    }

    internal struct FeaturePixelAccumulator
    {
        internal double AdditiveDelta;
        internal double StrongestPositiveDelta;
        internal double StrongestNegativeDelta;
        internal bool HasHeightCeiling;
        internal double HeightCeiling;

        internal double TotalDelta
        {
            get { return AdditiveDelta + StrongestPositiveDelta + StrongestNegativeDelta; }
        }
    }
}
