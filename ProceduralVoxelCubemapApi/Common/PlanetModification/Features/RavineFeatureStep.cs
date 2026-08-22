using System;
using System.Collections.Generic;
using VoxelCubemapApi.Common.Networking;
using VoxelCubemapApi.Common.Noise;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.Features
{
    internal sealed class RavineFeatureStep : IPlanetFeatureStep
    {
        internal static readonly RavineFeatureStep Instance =
            new RavineFeatureStep();

        private RavineFeatureStep()
        {
        }

        public void Expand(
            FeatureOperation operation,
            long planetSeed,
            List<GeneratedPlanetFeature> output)
        {
            if (operation == null || operation.RavineFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < operation.RavineFields.Count; fieldIndex++)
                ExpandField(output, planetSeed, operation.RavineFields[fieldIndex]);
        }

        public void WriteRuntime(
            FeatureOperation source,
            RuntimeProceduralFeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.RavineFields.Count; fieldIndex++)
            {
                RavineFieldOperation field = source.RavineFields[fieldIndex];
                target.RavineFields.Add(new RuntimeProceduralRavineField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void ReadRuntime(
            RuntimeProceduralFeatureOperation source,
            FeatureOperation target)
        {
            if (source.RavineFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.RavineFields.Count; fieldIndex++)
            {
                RuntimeProceduralRavineField field = source.RavineFields[fieldIndex];
                if (field == null)
                    continue;

                target.RavineFields.Add(new RavineFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void WriteSynced(
            FeatureOperation source,
            SyncedFeatureOperation target)
        {
            if (target.RavineFields == null)
                target.RavineFields = new List<SyncedRavineField>();

            for (int fieldIndex = 0; fieldIndex < source.RavineFields.Count; fieldIndex++)
            {
                RavineFieldOperation field = source.RavineFields[fieldIndex];
                target.RavineFields.Add(new SyncedRavineField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void ReadSynced(
            SyncedFeatureOperation source,
            FeatureOperation target)
        {
            if (source.RavineFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.RavineFields.Count; fieldIndex++)
            {
                SyncedRavineField field = source.RavineFields[fieldIndex];
                if (field == null)
                    continue;

                target.RavineFields.Add(new RavineFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void Clone(
            FeatureOperation source,
            FeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.RavineFields.Count; fieldIndex++)
            {
                RavineFieldOperation field = source.RavineFields[fieldIndex];
                target.RavineFields.Add(new RavineFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    TargetSize = field.TargetSize
                });
            }
        }

        private static void ExpandField(
            List<GeneratedPlanetFeature> output,
            long planetSeed,
            RavineFieldOperation field)
        {
            if (field == null || field.Count <= 0)
                return;

            long fieldSeed = NoiseMath.DeriveSeed(planetSeed, field.SeedOffset);
            for (int i = 0; i < field.Count; i++)
            {
                long ravineSeed = NoiseMath.DeriveSeed(fieldSeed, i + 1);
                double u0 = NoiseMath.HashToUnit(i, 0, 0, ravineSeed, 0x27D4EB2Fu);
                double u1 = NoiseMath.HashToUnit(i, 1, 0, ravineSeed, 0x165667B1u);
                double u2 = NoiseMath.HashToUnit(i, 2, 0, ravineSeed, 0x9E3779B9u);
                double u3 = NoiseMath.HashToUnit(i, 3, 0, ravineSeed, 0x85EBCA77u);
                double u4 = NoiseMath.HashToUnit(i, 4, 0, ravineSeed, 0xC2B2AE3Du);

                double z = u0 * 2.0 - 1.0;
                double azimuth = u1 * Math.PI * 2.0;
                double xy = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                Vector3D start = new Vector3D(
                    xy * Math.Cos(azimuth),
                    z,
                    xy * Math.Sin(azimuth));

                double targetSize = field.TargetSize > 0.0f && field.TargetSize < 1.0f
                    ? field.TargetSize
                    : 0.35;
                double sizeExponent = (1.0 - targetSize) / targetSize;
                double size = Math.Pow(u2, sizeExponent);
                double lengthDegrees = field.MinimumLengthDegrees +
                    (field.MaximumLengthDegrees - field.MinimumLengthDegrees) * size;
                double widthFactor = Math.Min(1.0, Math.Max(0.0, size * 0.75 + u3 * 0.25));
                double widthDegrees = field.MinimumWidthDegrees +
                    (field.MaximumWidthDegrees - field.MinimumWidthDegrees) * widthFactor;
                double depthFactor = Math.Min(1.0, Math.Max(0.0, size * 0.70 + u4 * 0.30));
                int depth = (int)(field.MinimumDepth +
                    (field.MaximumDepth - field.MinimumDepth) * depthFactor + 0.5);

                BuildRavine(output, ravineSeed, start, lengthDegrees, widthDegrees, depth);
            }
        }

        private static void BuildRavine(
            List<GeneratedPlanetFeature> output,
            long seed,
            Vector3D start,
            double lengthDegrees,
            double widthDegrees,
            int depth)
        {
            double totalLength = lengthDegrees * (Math.PI / 180.0);
            double baseHalfWidth = widthDegrees * 0.5 * (Math.PI / 180.0);
            int segmentCount = Math.Max(4, (int)Math.Ceiling(lengthDegrees / 2.0));
            if (segmentCount > 96)
                segmentCount = 96;

            double segmentLength = totalLength / segmentCount;
            double heading = NoiseMath.HashToUnit(0, 7, 0, seed, 0xA24BAED5u) * Math.PI * 2.0;
            Vector3D tangentA = BuildTangentBasisA(start);
            Vector3D tangentB = Vector3D.Cross(start, tangentA);
            Vector3D tangent = tangentA * Math.Cos(heading) + tangentB * Math.Sin(heading);
            tangent.Normalize();

            Vector3D a = start;
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                double turnUnit = NoiseMath.HashToUnit(
                    segmentIndex, 8, 0, seed, 0xB5297A4Du) * 2.0 - 1.0;
                double turn = turnUnit * 0.34;
                Vector3D side = Vector3D.Cross(a, tangent);
                tangent = tangent * Math.Cos(turn) + side * Math.Sin(turn);
                tangent.Normalize();

                Vector3D b = a * Math.Cos(segmentLength) + tangent * Math.Sin(segmentLength);
                b.Normalize();

                Vector3D arcNormal = Vector3D.Cross(a, b);
                double normalLength = arcNormal.Length();
                if (normalLength > 1e-12)
                {
                    arcNormal /= normalLength;
                    double segmentAngle = SafeAcos(Vector3D.Dot(a, b));
                    Vector3D capCenter = a + b;
                    if (capCenter.LengthSquared() > 1e-12)
                    {
                        capCenter.Normalize();
                        double halfWidthA = ComputeHalfWidth(
                            seed, segmentIndex, segmentCount, baseHalfWidth);
                        double halfWidthB = ComputeHalfWidth(
                            seed, segmentIndex + 1, segmentCount, baseHalfWidth);
                        double maximumHalfWidth = Math.Max(halfWidthA, halfWidthB);
                        double capRadius = segmentAngle * 0.5 + maximumHalfWidth;
                        output.Add(new GeneratedRavineSegment
                        {
                            A = a,
                            B = b,
                            ArcNormal = arcNormal,
                            SegmentAngle = segmentAngle,
                            HalfWidthA = halfWidthA,
                            HalfWidthB = halfWidthB,
                            Depth = depth,
                            Center = capCenter,
                            RadiusRadians = capRadius,
                            CosRadius = Math.Cos(capRadius),
                            SinRadius = Math.Sin(capRadius)
                        });
                    }
                }

                Vector3D transported = tangent - b * Vector3D.Dot(tangent, b);
                if (transported.LengthSquared() < 1e-12)
                    transported = BuildTangentBasisA(b);
                transported.Normalize();
                tangent = transported;
                a = b;
            }
        }

        private static double ComputeHalfWidth(
            long seed,
            int pointIndex,
            int segmentCount,
            double baseHalfWidth)
        {
            if (pointIndex <= 0 || pointIndex >= segmentCount)
                return 0.0;

            double pathT = pointIndex / (double)segmentCount;
            double taper = Math.Sin(pathT * Math.PI);
            if (taper <= 0.0)
                return 0.0;

            // Rounded pointed ends with a deliberately wider middle. The small
            // deterministic variation keeps the walls from forming a uniform ribbon.
            taper = Math.Pow(taper, 0.70);
            double centerBulge = 1.0 + taper * 0.35;
            double widthNoise = NoiseMath.HashToUnit(
                pointIndex, 19, 0, seed, 0x7F4A7C15u);
            double variation = 0.85 + widthNoise * 0.30;
            return baseHalfWidth * taper * centerBulge * variation;
        }

        private static Vector3D BuildTangentBasisA(Vector3D normal)
        {
            Vector3D reference = Math.Abs(normal.Y) < 0.9
                ? Vector3D.Up
                : Vector3D.Right;
            Vector3D tangent = Vector3D.Cross(reference, normal);
            tangent.Normalize();
            return tangent;
        }

        private static double SafeAcos(double value)
        {
            if (value > 1.0) value = 1.0;
            else if (value < -1.0) value = -1.0;
            return Math.Acos(value);
        }

        private sealed class GeneratedRavineSegment : GeneratedPlanetFeature
        {
            internal Vector3D A;
            internal Vector3D B;
            internal Vector3D ArcNormal;
            internal double SegmentAngle;
            internal double HalfWidthA;
            internal double HalfWidthB;
            internal int Depth;

            internal override void Accumulate(
                Vector3D direction,
                int currentHeight,
                ref FeaturePixelAccumulator accumulator)
            {
                double alongSegment;
                double angularDistance = DistanceToSegment(direction, out alongSegment);
                double halfWidth = HalfWidthA + (HalfWidthB - HalfWidthA) * alongSegment;
                if (halfWidth <= 1e-12 || angularDistance >= halfWidth)
                    return;

                double t = angularDistance / halfWidth;
                double smooth = t * t * (3.0 - 2.0 * t);
                double score = -(1.0 - smooth);
                double candidateDelta = score * Depth;
                if (candidateDelta < accumulator.StrongestNegativeDelta)
                    accumulator.StrongestNegativeDelta = candidateDelta;
            }

            private double DistanceToSegment(
                Vector3D direction,
                out double alongSegment)
            {
                double planeDot = Vector3D.Dot(direction, ArcNormal);
                Vector3D projected = direction - ArcNormal * planeDot;
                double projectedLengthSquared = projected.LengthSquared();

                if (projectedLengthSquared > 1e-14)
                {
                    projected /= Math.Sqrt(projectedLengthSquared);
                    if (Vector3D.Dot(projected, direction) < 0.0)
                        projected = -projected;

                    Vector3D aq = Vector3D.Cross(A, projected);
                    Vector3D qb = Vector3D.Cross(projected, B);
                    if (Vector3D.Dot(aq, ArcNormal) >= -1e-10 &&
                        Vector3D.Dot(qb, ArcNormal) >= -1e-10)
                    {
                        double absolutePlaneDot = Math.Abs(planeDot);
                        if (absolutePlaneDot > 1.0)
                            absolutePlaneDot = 1.0;

                        if (SegmentAngle > 1e-12)
                        {
                            alongSegment = SafeAcos(Vector3D.Dot(A, projected)) / SegmentAngle;
                            if (alongSegment < 0.0) alongSegment = 0.0;
                            else if (alongSegment > 1.0) alongSegment = 1.0;
                        }
                        else
                        {
                            alongSegment = 0.0;
                        }

                        return Math.Asin(absolutePlaneDot);
                    }
                }

                double distanceA = SafeAcos(Vector3D.Dot(direction, A));
                double distanceB = SafeAcos(Vector3D.Dot(direction, B));
                if (distanceA <= distanceB)
                {
                    alongSegment = 0.0;
                    return distanceA;
                }

                alongSegment = 1.0;
                return distanceB;
            }
        }
    }
}
