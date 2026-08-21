using System;
using System.Collections.Generic;
using Generated;

namespace VoxelCubemapApi.Common.PlanetModification.Templates
{
    [ApiProvider(
        ClientNamespace = "VoxelCubemapApi.Api",
        ClientName = "FeatureTemplate")]
    internal sealed partial class FeatureTemplate
    {
        private readonly FeatureOperation _operation;

        internal FeatureTemplate(FeatureOperation operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            _operation = operation;
        }

        /// <summary>
        /// Adds a deterministic crater field. Individual crater centers,
        /// radii and depths are regenerated from planetSeed + seedOffset + index
        /// when the feature pass executes; they are not serialized individually.
        /// Radius distribution is biased toward medium impacts. Overlap is allowed.
        /// </summary>
        [ApiMethod]
        private void AddCraterField(
            int count,
            int seedOffset,
            double minimumRadiusDegrees,
            double maximumRadiusDegrees,
            int minimumDepth,
            int maximumDepth)
        {
            AddCraterFieldBiased(
                count, seedOffset, minimumRadiusDegrees, maximumRadiusDegrees,
                minimumDepth, maximumDepth, 1.0f / 2.0f);
        }

        /// <summary>
        /// Adds a compact deterministic crater field with an explicit normalized
        /// target crater size. 0.5 is uniform, values below 0.5 increasingly favor
        /// smaller craters, and values above 0.5 increasingly favor larger craters.
        /// </summary>
        [ApiMethod("AddCraterFieldBiased")]
        private void AddCraterFieldBiased(
            int count,
            int seedOffset,
            double minimumRadiusDegrees,
            double maximumRadiusDegrees,
            int minimumDepth,
            int maximumDepth,
            float targetSize)
        {
            // I have no real reason to put this limit, but well, infinity is a big number
            if (count < 1 || count > ushort.MaxValue)
                throw new ArgumentException("Crater count must be from 1 to 65535.", nameof(count));

            if (double.IsNaN(minimumRadiusDegrees) || double.IsInfinity(minimumRadiusDegrees) ||
                double.IsNaN(maximumRadiusDegrees) || double.IsInfinity(maximumRadiusDegrees) ||
                minimumRadiusDegrees <= 0.0 || maximumRadiusDegrees > 90.0 ||
                minimumRadiusDegrees > maximumRadiusDegrees)
            {
                throw new ArgumentException("Crater radius range must be finite, greater than zero, no more than 90 degrees, and ordered.", "radius");
            }

            if (minimumDepth < 1 || maximumDepth > ushort.MaxValue || minimumDepth > maximumDepth)
                throw new ArgumentException("Crater depth range must be from 1 to 65535 and ordered.", "depth");

            if (float.IsNaN(targetSize) || float.IsInfinity(targetSize) ||
                targetSize <= 0.0f || targetSize >= 1.0f)
            {
                throw new ArgumentException("Crater target size must be finite and between 0 and 1 (exclusive).", nameof(targetSize));
            }

            _operation.CraterFields.Add(new CraterFieldOperation
            {
                Count = count,
                SeedOffset = seedOffset,
                MinimumRadiusDegrees = minimumRadiusDegrees,
                MaximumRadiusDegrees = maximumRadiusDegrees,
                MinimumDepth = minimumDepth,
                MaximumDepth = maximumDepth,
                TargetSize = targetSize
            });
        }
    }
}
