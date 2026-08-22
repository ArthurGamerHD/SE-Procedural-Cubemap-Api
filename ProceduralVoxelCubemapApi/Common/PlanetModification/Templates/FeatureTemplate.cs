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


        /// <summary>
        /// Adds a compact deterministic volcano field. Volcano centers, radii and
        /// heights are regenerated from planetSeed + seedOffset + index when the
        /// feature pass executes. The profile includes a summit caldera.
        /// </summary>
        [ApiMethod]
        private void AddVolcanoField(
            int count,
            int seedOffset,
            double minimumRadiusDegrees,
            double maximumRadiusDegrees,
            int minimumHeight,
            int maximumHeight)
        {
            AddVolcanoFieldBiased(
                count, seedOffset, minimumRadiusDegrees, maximumRadiusDegrees,
                minimumHeight, maximumHeight, 0.25f);
        }

        /// <summary>
        /// Adds a deterministic volcano field with an explicit normalized target
        /// size. Values below 0.5 increasingly favor smaller volcanoes.
        /// </summary>
        [ApiMethod("AddVolcanoFieldBiased")]
        private void AddVolcanoFieldBiased(
            int count,
            int seedOffset,
            double minimumRadiusDegrees,
            double maximumRadiusDegrees,
            int minimumHeight,
            int maximumHeight,
            float targetSize)
        {
            if (count < 1 || count > ushort.MaxValue)
                throw new ArgumentException("Volcano count must be from 1 to 65535.", nameof(count));

            if (double.IsNaN(minimumRadiusDegrees) || double.IsInfinity(minimumRadiusDegrees) ||
                double.IsNaN(maximumRadiusDegrees) || double.IsInfinity(maximumRadiusDegrees) ||
                minimumRadiusDegrees <= 0.0 || maximumRadiusDegrees > 90.0 ||
                minimumRadiusDegrees > maximumRadiusDegrees)
            {
                throw new ArgumentException("Volcano radius range must be finite, greater than zero, no more than 90 degrees, and ordered.", "radius");
            }

            if (minimumHeight < 1 || maximumHeight > ushort.MaxValue || minimumHeight > maximumHeight)
                throw new ArgumentException("Volcano height range must be from 1 to 65535 and ordered.", "height");

            if (float.IsNaN(targetSize) || float.IsInfinity(targetSize) ||
                targetSize <= 0.0f || targetSize >= 1.0f)
            {
                throw new ArgumentException("Volcano target size must be finite and between 0 and 1 (exclusive).", nameof(targetSize));
            }

            _operation.VolcanoFields.Add(new VolcanoFieldOperation
            {
                Count = count,
                SeedOffset = seedOffset,
                MinimumRadiusDegrees = minimumRadiusDegrees,
                MaximumRadiusDegrees = maximumRadiusDegrees,
                MinimumHeight = minimumHeight,
                MaximumHeight = maximumHeight,
                TargetSize = targetSize
            });
        }

        /// <summary>
        /// Adds a compact deterministic ravine field. Ravine paths are regenerated
        /// from planetSeed + seedOffset + index when the feature pass executes.
        /// </summary>
        [ApiMethod]
        private void AddRavineField(
            int count,
            int seedOffset,
            double minimumLengthDegrees,
            double maximumLengthDegrees,
            double minimumWidthDegrees,
            double maximumWidthDegrees,
            int minimumDepth,
            int maximumDepth)
        {
            AddRavineFieldBiased(
                count, seedOffset, minimumLengthDegrees, maximumLengthDegrees,
                minimumWidthDegrees, maximumWidthDegrees, minimumDepth, maximumDepth,
                0.35f);
        }

        /// <summary>
        /// Adds a deterministic ravine field with an explicit normalized target
        /// size. Values below 0.5 increasingly favor shorter/narrower ravines.
        /// </summary>
        [ApiMethod("AddRavineFieldBiased")]
        private void AddRavineFieldBiased(
            int count,
            int seedOffset,
            double minimumLengthDegrees,
            double maximumLengthDegrees,
            double minimumWidthDegrees,
            double maximumWidthDegrees,
            int minimumDepth,
            int maximumDepth,
            float targetSize)
        {
            if (count < 1 || count > ushort.MaxValue)
                throw new ArgumentException("Ravine count must be from 1 to 65535.", nameof(count));

            if (double.IsNaN(minimumLengthDegrees) || double.IsInfinity(minimumLengthDegrees) ||
                double.IsNaN(maximumLengthDegrees) || double.IsInfinity(maximumLengthDegrees) ||
                minimumLengthDegrees <= 0.0 || maximumLengthDegrees > 180.0 ||
                minimumLengthDegrees > maximumLengthDegrees)
            {
                throw new ArgumentException("Ravine length range must be finite, greater than zero, no more than 180 degrees, and ordered.", "length");
            }

            if (double.IsNaN(minimumWidthDegrees) || double.IsInfinity(minimumWidthDegrees) ||
                double.IsNaN(maximumWidthDegrees) || double.IsInfinity(maximumWidthDegrees) ||
                minimumWidthDegrees <= 0.0 || maximumWidthDegrees > 30.0 ||
                minimumWidthDegrees > maximumWidthDegrees)
            {
                throw new ArgumentException("Ravine width range must be finite, greater than zero, no more than 30 degrees, and ordered.", "width");
            }

            if (minimumDepth < 1 || maximumDepth > ushort.MaxValue || minimumDepth > maximumDepth)
                throw new ArgumentException("Ravine depth range must be from 1 to 65535 and ordered.", "depth");

            if (float.IsNaN(targetSize) || float.IsInfinity(targetSize) ||
                targetSize <= 0.0f || targetSize >= 1.0f)
            {
                throw new ArgumentException("Ravine target size must be finite and between 0 and 1 (exclusive).", nameof(targetSize));
            }

            _operation.RavineFields.Add(new RavineFieldOperation
            {
                Count = count,
                SeedOffset = seedOffset,
                MinimumLengthDegrees = minimumLengthDegrees,
                MaximumLengthDegrees = maximumLengthDegrees,
                MinimumWidthDegrees = minimumWidthDegrees,
                MaximumWidthDegrees = maximumWidthDegrees,
                MinimumDepth = minimumDepth,
                MaximumDepth = maximumDepth,
                TargetSize = targetSize
            });
        }


        /// <summary>
        /// Adds deterministic sea-level river corridors. Each river chooses a seeded
        /// inland source, connects it to the nearest terrain sample at/below the fixed
        /// shoreline height, then carves a meandering spherical channel down to that
        /// water level. Planning uses a coarse six-face shoreline index and bounded
        /// source attempts;
        /// </summary>
        [ApiMethod]
        private void AddRiverField(
            int count,
            int seedOffset,
            int shorelineHeight,
            int minimumSourceHeightAboveShoreline,
            double minimumLengthDegrees,
            double maximumLengthDegrees,
            double minimumWidthDegrees,
            double maximumWidthDegrees,
            int minimumDepth,
            int maximumDepth,
            double shoulderWidthMultiplier)
        {
            if (count < 1 || count > 256)
                throw new ArgumentException("River count must be from 1 to 256.", nameof(count));

            if (shorelineHeight < 0 || shorelineHeight > ushort.MaxValue)
                throw new ArgumentException("Shoreline height must be from 0 to 65535.", nameof(shorelineHeight));

            if (minimumSourceHeightAboveShoreline < 1 ||
                minimumSourceHeightAboveShoreline > ushort.MaxValue)
            {
                throw new ArgumentException(
                    "Minimum source height above shoreline must be from 1 to 65535.",
                    nameof(minimumSourceHeightAboveShoreline));
            }

            if (double.IsNaN(minimumLengthDegrees) || double.IsInfinity(minimumLengthDegrees) ||
                double.IsNaN(maximumLengthDegrees) || double.IsInfinity(maximumLengthDegrees) ||
                minimumLengthDegrees <= 0.0 || maximumLengthDegrees > 120.0 ||
                minimumLengthDegrees > maximumLengthDegrees)
            {
                throw new ArgumentException(
                    "River length range must be finite, greater than zero, no more than 120 degrees, and ordered.",
                    "length");
            }

            if (double.IsNaN(minimumWidthDegrees) || double.IsInfinity(minimumWidthDegrees) ||
                double.IsNaN(maximumWidthDegrees) || double.IsInfinity(maximumWidthDegrees) ||
                minimumWidthDegrees <= 0.0 || maximumWidthDegrees > 10.0 ||
                minimumWidthDegrees > maximumWidthDegrees)
            {
                throw new ArgumentException(
                    "River width range must be finite, greater than zero, no more than 10 degrees, and ordered.",
                    "width");
            }

            if (minimumDepth < 1 || maximumDepth > ushort.MaxValue || minimumDepth > maximumDepth)
                throw new ArgumentException("River depth range must be from 1 to 65535 and ordered.", "depth");

            if (double.IsNaN(shoulderWidthMultiplier) || double.IsInfinity(shoulderWidthMultiplier) ||
                shoulderWidthMultiplier < 1.0 || shoulderWidthMultiplier > 16.0)
            {
                throw new ArgumentException(
                    "River shoulder width multiplier must be finite and from 1 to 16.",
                    nameof(shoulderWidthMultiplier));
            }

            _operation.RiverFields.Add(new RiverFieldOperation
            {
                Count = count,
                SeedOffset = seedOffset,
                ShorelineHeight = shorelineHeight,
                MinimumSourceHeightAboveShoreline = minimumSourceHeightAboveShoreline,
                MinimumLengthDegrees = minimumLengthDegrees,
                MaximumLengthDegrees = maximumLengthDegrees,
                MinimumWidthDegrees = minimumWidthDegrees,
                MaximumWidthDegrees = maximumWidthDegrees,
                MinimumDepth = minimumDepth,
                MaximumDepth = maximumDepth,
                ShoulderWidthMultiplier = shoulderWidthMultiplier
            });
        }

    }
}
