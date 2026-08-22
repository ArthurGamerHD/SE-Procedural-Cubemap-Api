using System;
using System.Collections.Generic;
using Adk.Image.Png;
using VoxelCubemapApi.Common.Networking;
using VoxelCubemapApi.Common.Noise;
using VoxelCubemapApi.Common.Noise.fBm;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.Features
{
    /// <summary>
    /// Terrain-aware deterministic river feature. Persistence/sync is handled through
    /// IPlanetFeatureStep, while expansion is explicitly invoked with all six heightmaps
    /// because source/mouth selection depends on the current shoreline.
    /// </summary>
    internal sealed class RiverFeatureStep : IPlanetFeatureStep
    {
        internal static readonly RiverFeatureStep Instance = new RiverFeatureStep();

        private const int SourceAttempts = 24;
        private const int PlanningResolution = 96;

        private RiverFeatureStep()
        {
        }

        // Rivers cannot be expanded by the terrain-blind registry path.
        public void Expand(
            FeatureOperation operation,
            long planetSeed,
            List<GeneratedPlanetFeature> output)
        {
        }

        internal void ExpandTerrainAware(
            List<FeatureOperation> operations,
            long planetSeed,
            IDictionary<string, PlanarPngBitmap> heightImages,
            List<GeneratedPlanetFeature> output)
        {
            if (operations == null || operations.Count == 0 ||
                heightImages == null || output == null)
                return;

            var indexes = new Dictionary<int, WetSampleIndex>();

            for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                FeatureOperation operation = operations[operationIndex];
                if (operation == null || operation.RiverFields == null)
                    continue;

                for (int fieldIndex = 0; fieldIndex < operation.RiverFields.Count; fieldIndex++)
                {
                    RiverFieldOperation field = operation.RiverFields[fieldIndex];
                    if (field == null || field.Count <= 0)
                        continue;

                    WetSampleIndex index;
                    if (!indexes.TryGetValue(field.ShorelineHeight, out index))
                    {
                        index = new WetSampleIndex(
                            heightImages,
                            field.ShorelineHeight,
                            PlanningResolution);
                        indexes.Add(field.ShorelineHeight, index);
                    }

                    if (!index.HasWetSamples)
                        continue;

                    ExpandField(output, planetSeed, field, index);
                }
            }
        }

        public void WriteRuntime(
            FeatureOperation source,
            RuntimeProceduralFeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.RiverFields.Count; fieldIndex++)
            {
                RiverFieldOperation field = source.RiverFields[fieldIndex];
                target.RiverFields.Add(new RuntimeProceduralRiverField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    ShorelineHeight = field.ShorelineHeight,
                    MinimumSourceHeightAboveShoreline = field.MinimumSourceHeightAboveShoreline,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    ShoulderWidthMultiplier = field.ShoulderWidthMultiplier
                });
            }
        }

        public void ReadRuntime(
            RuntimeProceduralFeatureOperation source,
            FeatureOperation target)
        {
            if (source.RiverFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.RiverFields.Count; fieldIndex++)
            {
                RuntimeProceduralRiverField field = source.RiverFields[fieldIndex];
                if (field == null)
                    continue;

                target.RiverFields.Add(new RiverFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    ShorelineHeight = field.ShorelineHeight,
                    MinimumSourceHeightAboveShoreline = field.MinimumSourceHeightAboveShoreline,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    ShoulderWidthMultiplier = field.ShoulderWidthMultiplier
                });
            }
        }

        public void WriteSynced(
            FeatureOperation source,
            SyncedFeatureOperation target)
        {
            if (target.RiverFields == null)
                target.RiverFields = new List<SyncedRiverField>();

            for (int fieldIndex = 0; fieldIndex < source.RiverFields.Count; fieldIndex++)
            {
                RiverFieldOperation field = source.RiverFields[fieldIndex];
                target.RiverFields.Add(new SyncedRiverField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    ShorelineHeight = field.ShorelineHeight,
                    MinimumSourceHeightAboveShoreline = field.MinimumSourceHeightAboveShoreline,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    ShoulderWidthMultiplier = field.ShoulderWidthMultiplier
                });
            }
        }

        public void ReadSynced(
            SyncedFeatureOperation source,
            FeatureOperation target)
        {
            if (source.RiverFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.RiverFields.Count; fieldIndex++)
            {
                SyncedRiverField field = source.RiverFields[fieldIndex];
                if (field == null)
                    continue;

                target.RiverFields.Add(new RiverFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    ShorelineHeight = field.ShorelineHeight,
                    MinimumSourceHeightAboveShoreline = field.MinimumSourceHeightAboveShoreline,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    ShoulderWidthMultiplier = field.ShoulderWidthMultiplier
                });
            }
        }

        public void Clone(
            FeatureOperation source,
            FeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.RiverFields.Count; fieldIndex++)
            {
                RiverFieldOperation field = source.RiverFields[fieldIndex];
                target.RiverFields.Add(new RiverFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    ShorelineHeight = field.ShorelineHeight,
                    MinimumSourceHeightAboveShoreline = field.MinimumSourceHeightAboveShoreline,
                    MinimumLengthDegrees = field.MinimumLengthDegrees,
                    MaximumLengthDegrees = field.MaximumLengthDegrees,
                    MinimumWidthDegrees = field.MinimumWidthDegrees,
                    MaximumWidthDegrees = field.MaximumWidthDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    ShoulderWidthMultiplier = field.ShoulderWidthMultiplier
                });
            }
        }

        private static void ExpandField(
            List<GeneratedPlanetFeature> output,
            long planetSeed,
            RiverFieldOperation field,
            WetSampleIndex index)
        {
            if (field.ShorelineHeight < 0 || field.ShorelineHeight > ushort.MaxValue ||
                field.MinimumSourceHeightAboveShoreline < 1 ||
                field.MinimumLengthDegrees <= 0.0 ||
                field.MaximumLengthDegrees < field.MinimumLengthDegrees ||
                field.MaximumLengthDegrees > 120.0 ||
                field.MinimumWidthDegrees <= 0.0 ||
                field.MaximumWidthDegrees < field.MinimumWidthDegrees ||
                field.MinimumDepth < 1 || field.MaximumDepth < field.MinimumDepth ||
                field.ShoulderWidthMultiplier < 1.0 ||
                double.IsNaN(field.MinimumLengthDegrees) ||
                double.IsNaN(field.MaximumLengthDegrees) ||
                double.IsNaN(field.MinimumWidthDegrees) ||
                double.IsNaN(field.MaximumWidthDegrees) ||
                double.IsNaN(field.ShoulderWidthMultiplier))
            {
                return;
            }

            long fieldSeed = NoiseMath.DeriveSeed(planetSeed, field.SeedOffset);
            double minimumDot = Math.Cos(field.MaximumLengthDegrees * Math.PI / 180.0);
            double maximumDot = Math.Cos(field.MinimumLengthDegrees * Math.PI / 180.0);
            int minimumSourceHeight = Math.Min(
                ushort.MaxValue,
                field.ShorelineHeight + field.MinimumSourceHeightAboveShoreline);

            int riverCount = Math.Min(256, field.Count);
            for (int riverIndex = 0; riverIndex < riverCount; riverIndex++)
            {
                long riverSeed = NoiseMath.DeriveSeed(fieldSeed, riverIndex + 1);
                Vector3D source;
                Vector3D mouth;
                bool found = false;

                for (int attempt = 0; attempt < SourceAttempts; attempt++)
                {
                    source = RandomSpherePoint(riverSeed, attempt);
                    int sourceHeight = index.SampleHeight(source);
                    if (sourceHeight < minimumSourceHeight)
                        continue;

                    if (!index.FindNearestWet(source, out mouth))
                        continue;

                    double dot = ClampDot(Vector3D.Dot(source, mouth));
                    if (dot < minimumDot || dot > maximumDot)
                        continue;

                    BuildRiver(output, riverSeed, field, source, mouth, index);
                    found = true;
                    break;
                }

                if (!found)
                    continue;
            }
        }

        private static Vector3D RandomSpherePoint(long seed, int attempt)
        {
            double u0 = NoiseMath.HashToUnit(attempt, 0, 0, seed, 0xA511E9B3u);
            double u1 = NoiseMath.HashToUnit(attempt, 1, 0, seed, 0x63D83595u);
            double z = u0 * 2.0 - 1.0;
            double azimuth = u1 * Math.PI * 2.0;
            double xy = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
            return new Vector3D(
                xy * Math.Cos(azimuth),
                z,
                xy * Math.Sin(azimuth));
        }

        private static void BuildRiver(
            List<GeneratedPlanetFeature> output,
            long seed,
            RiverFieldOperation field,
            Vector3D source,
            Vector3D mouth,
            WetSampleIndex index)
        {
            double totalAngle = SafeAcos(Vector3D.Dot(source, mouth));
            if (totalAngle <= 1e-8)
                return;

            Vector3D pathNormal = Vector3D.Cross(source, mouth);
            double normalLength = pathNormal.Length();
            if (normalLength <= 1e-10)
                return;
            pathNormal /= normalLength;

            double uWidth = NoiseMath.HashToUnit(0, 20, 0, seed, 0x9E3779B9u);
            double uDepth = NoiseMath.HashToUnit(0, 21, 0, seed, 0x85EBCA77u);
            double widthDegrees = field.MinimumWidthDegrees +
                (field.MaximumWidthDegrees - field.MinimumWidthDegrees) * uWidth;
            int depth = (int)(field.MinimumDepth +
                (field.MaximumDepth - field.MinimumDepth) * uDepth + 0.5);
            double baseHalfWidth = widthDegrees * 0.5 * Math.PI / 180.0;

            double lengthDegrees = totalAngle * 180.0 / Math.PI;
            int segmentCount = (int)Math.Ceiling(lengthDegrees / 0.55);
            if (segmentCount < 24) segmentCount = 24;
            else if (segmentCount > 160) segmentCount = 160;

            double phaseA = NoiseMath.HashToUnit(0, 22, 0, seed, 0xC2B2AE3Du) * Math.PI * 2.0;
            double phaseB = NoiseMath.HashToUnit(0, 23, 0, seed, 0x27D4EB2Fu) * Math.PI * 2.0;
            double phaseC = NoiseMath.HashToUnit(0, 24, 0, seed, 0x165667B1u) * Math.PI * 2.0;
            double cyclesA = 1.35 + NoiseMath.HashToUnit(0, 25, 0, seed, 0xB5297A4Du) * 1.85;
            double cyclesB = 3.5 + NoiseMath.HashToUnit(0, 26, 0, seed, 0x68E31DA4u) * 3.5;
            double cyclesC = 7.0 + NoiseMath.HashToUnit(0, 27, 0, seed, 0x1B56C4E9u) * 5.0;
            double amplitude = Math.Max(baseHalfWidth * 3.5, totalAngle * 0.018);
            amplitude = Math.Min(amplitude, totalAngle * 0.065);

            var points = new Vector3D[segmentCount + 1];
            var halfWidths = new double[segmentCount + 1];
            points[0] = source;
            halfWidths[0] = ComputeHalfWidth(baseHalfWidth, 0.0, phaseC);

            for (int pointIndex = 1; pointIndex <= segmentCount; pointIndex++)
            {
                double t = pointIndex / (double)segmentCount;
                Vector3D center = Slerp(source, mouth, totalAngle, t);
                double envelope = Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * t)), 0.72);
                double wave =
                    Math.Sin(phaseA + t * Math.PI * 2.0 * cyclesA) * 0.64 +
                    Math.Sin(phaseB + t * Math.PI * 2.0 * cyclesB) * 0.25 +
                    Math.Sin(phaseC + t * Math.PI * 2.0 * cyclesC) * 0.11;
                double lateral = wave * amplitude * envelope;

                Vector3D point = center * Math.Cos(lateral) + pathNormal * Math.Sin(lateral);
                point.Normalize();
                if (pointIndex == segmentCount)
                    point = mouth;

                points[pointIndex] = point;
                halfWidths[pointIndex] = ComputeHalfWidth(baseHalfWidth, t, phaseC);
            }

            for (int pointIndex = 1; pointIndex <= segmentCount; pointIndex++)
            {
                AddSegment(
                    output,
                    points[pointIndex - 1],
                    points[pointIndex],
                    halfWidths[pointIndex - 1],
                    halfWidths[pointIndex],
                    field.ShorelineHeight,
                    depth,
                    field.ShoulderWidthMultiplier);
            }

            BuildDelta(
                output,
                seed,
                field,
                index,
                points,
                halfWidths,
                depth,
                pathNormal,
                totalAngle);
        }

        private static double ComputeHalfWidth(
            double baseHalfWidth,
            double t,
            double phase)
        {
            // The requested width is the approximate mid-river width. A production
            // river should read broader than a ravine and visibly widen downstream.
            double downstreamWidening = 0.90 + t * 0.70;
            double widthWave = 0.92 + Math.Sin(phase + t * Math.PI * 5.0) * 0.08;
            return baseHalfWidth * downstreamWidening * widthWave;
        }

        private static void BuildDelta(
            List<GeneratedPlanetFeature> output,
            long seed,
            RiverFieldOperation field,
            WetSampleIndex index,
            Vector3D[] points,
            double[] halfWidths,
            int trunkDepth,
            Vector3D coastTangent,
            double totalAngle)
        {
            if (points == null || halfWidths == null || points.Length < 8 ||
                points.Length != halfWidths.Length || index == null)
                return;

            int segmentCount = points.Length - 1;
            double deltaStartFraction = 0.76 +
                NoiseMath.HashToUnit(0, 40, 0, seed, 0xD6E8FEB8u) * 0.08;
            int deltaStartIndex = (int)Math.Round(segmentCount * deltaStartFraction);
            if (deltaStartIndex < 2) deltaStartIndex = 2;
            if (deltaStartIndex > segmentCount - 4) deltaStartIndex = segmentCount - 4;

            Vector3D mouth = points[segmentCount];
            Vector3D downstream = Vector3D.Cross(coastTangent, mouth);
            double downstreamLength = downstream.Length();
            if (downstreamLength <= 1e-10)
                return;
            downstream /= downstreamLength;

            // Ensure this points from the inland trunk toward and through the mouth.
            Vector3D beforeMouth = points[Math.Max(0, segmentCount - 2)];
            Vector3D arrival = mouth * Vector3D.Dot(beforeMouth, mouth) - beforeMouth;
            if (arrival.LengthSquared() > 1e-12 && Vector3D.Dot(downstream, arrival) < 0.0)
                downstream = -downstream;

            double mouthHalfWidth = halfWidths[segmentCount];
            double deltaLength = SafeAcos(Vector3D.Dot(points[deltaStartIndex], mouth));
            double minimumFan = 0.45 * Math.PI / 180.0;
            double maximumFan = 4.50 * Math.PI / 180.0;
            double fanHalfAngle = Math.Max(mouthHalfWidth * 5.5, deltaLength * 0.30);
            if (fanHalfAngle < minimumFan) fanHalfAngle = minimumFan;
            if (fanHalfAngle > maximumFan) fanHalfAngle = maximumFan;
            fanHalfAngle = Math.Min(fanHalfAngle, totalAngle * 0.11);

            int branchCount = 4 + (int)Math.Floor(
                NoiseMath.HashToUnit(0, 41, 0, seed, 0xA4093822u) * 3.0);
            if (branchCount < 4) branchCount = 4;
            if (branchCount > 6) branchCount = 6;

            for (int branchIndex = 0; branchIndex < branchCount; branchIndex++)
            {
                double normalized = branchCount <= 1
                    ? 0.0
                    : -1.0 + branchIndex * 2.0 / (branchCount - 1.0);
                double jitter =
                    (NoiseMath.HashToUnit(branchIndex, 42, 0, seed, 0x299F31D0u) - 0.5) *
                    (1.30 / branchCount);
                normalized += jitter;
                if (normalized < -1.0) normalized = -1.0;
                else if (normalized > 1.0) normalized = 1.0;

                double absoluteSide = Math.Abs(normalized);
                double startProgress =
                    0.02 + absoluteSide * 0.16 +
                    NoiseMath.HashToUnit(branchIndex, 43, 0, seed, 0x082EFA98u) * 0.05;
                int startIndex = deltaStartIndex + (int)Math.Round(
                    (segmentCount - deltaStartIndex) * startProgress);
                if (startIndex < deltaStartIndex) startIndex = deltaStartIndex;
                if (startIndex > segmentCount - 3) startIndex = segmentCount - 3;

                Vector3D start = points[startIndex];
                double startHalfWidth = halfWidths[startIndex] *
                    (0.58 + (1.0 - absoluteSide) * 0.18);
                double endHalfWidth = mouthHalfWidth *
                    (0.22 + (1.0 - absoluteSide) * 0.20);

                double lateralAngle = normalized * fanHalfAngle;
                double forwardAngle = fanHalfAngle *
                    (0.34 + NoiseMath.HashToUnit(branchIndex, 44, 0, seed, 0xEC4E6C89u) * 0.34);

                Vector3D outlet = FindDeltaOutlet(
                    index,
                    mouth,
                    coastTangent,
                    downstream,
                    lateralAngle,
                    forwardAngle,
                    field.ShorelineHeight);

                double branchAngle = SafeAcos(Vector3D.Dot(start, outlet));
                if (branchAngle <= 1e-7)
                    continue;

                int branchDepth = (int)Math.Round(
                    trunkDepth * (0.50 + (1.0 - absoluteSide) * 0.24));
                if (branchDepth < 1) branchDepth = 1;

                BuildDeltaBranch(
                    output,
                    seed,
                    branchIndex,
                    start,
                    outlet,
                    startHalfWidth,
                    endHalfWidth,
                    field.ShorelineHeight,
                    branchDepth,
                    field.ShoulderWidthMultiplier);
            }
        }

        private static Vector3D FindDeltaOutlet(
            WetSampleIndex index,
            Vector3D mouth,
            Vector3D coastTangent,
            Vector3D downstream,
            double lateralAngle,
            double forwardAngle,
            int shorelineHeight)
        {
            // First keep the requested lateral position and walk a few cheap samples
            // oceanward. If the local coast curves back onto land, progressively pull
            // the outlet toward the known-wet mouth and try again.
            for (int lateralPass = 0; lateralPass < 5; lateralPass++)
            {
                double lateralScale = 1.0 - lateralPass * 0.20;
                for (int forwardStep = 0; forwardStep < 9; forwardStep++)
                {
                    double forwardScale = 0.30 + forwardStep * 0.18;
                    Vector3D tangentOffset =
                        coastTangent * (lateralAngle * lateralScale) +
                        downstream * (forwardAngle * forwardScale);
                    Vector3D candidate = OffsetDirection(mouth, tangentOffset);
                    if (index.SampleHeight(candidate) <= shorelineHeight)
                        return candidate;
                }
            }

            return mouth;
        }

        private static Vector3D OffsetDirection(
            Vector3D origin,
            Vector3D tangentOffset)
        {
            double angle = tangentOffset.Length();
            if (angle <= 1e-12)
                return origin;

            Vector3D tangent = tangentOffset / angle;
            Vector3D result = origin * Math.Cos(angle) + tangent * Math.Sin(angle);
            result.Normalize();
            return result;
        }

        private static void BuildDeltaBranch(
            List<GeneratedPlanetFeature> output,
            long seed,
            int branchIndex,
            Vector3D start,
            Vector3D outlet,
            double startHalfWidth,
            double endHalfWidth,
            int shorelineHeight,
            int depth,
            double shoulderWidthMultiplier)
        {
            double angle = SafeAcos(Vector3D.Dot(start, outlet));
            if (angle <= 1e-8)
                return;

            Vector3D branchNormal = Vector3D.Cross(start, outlet);
            double branchNormalLength = branchNormal.Length();
            if (branchNormalLength <= 1e-10)
                return;
            branchNormal /= branchNormalLength;

            double angleDegrees = angle * 180.0 / Math.PI;
            int segmentCount = (int)Math.Ceiling(angleDegrees / 0.45);
            if (segmentCount < 7) segmentCount = 7;
            else if (segmentCount > 24) segmentCount = 24;

            double phase = NoiseMath.HashToUnit(
                branchIndex, 45, 0, seed, 0x452821E6u) * Math.PI * 2.0;
            double phase2 = NoiseMath.HashToUnit(
                branchIndex, 46, 0, seed, 0x38D01377u) * Math.PI * 2.0;
            double bendAmplitude = Math.Min(
                angle * 0.075,
                Math.Max(startHalfWidth * 0.85, 0.045 * Math.PI / 180.0));

            Vector3D previous = start;
            double previousHalfWidth = startHalfWidth;
            for (int pointIndex = 1; pointIndex <= segmentCount; pointIndex++)
            {
                double t = pointIndex / (double)segmentCount;
                Vector3D center = Slerp(start, outlet, angle, t);
                double envelope = Math.Sin(Math.PI * t);
                double wave =
                    Math.Sin(phase + t * Math.PI * 2.0 * 1.35) * 0.68 +
                    Math.Sin(phase2 + t * Math.PI * 2.0 * 2.70) * 0.32;
                double lateral = wave * bendAmplitude * envelope;
                Vector3D point = center * Math.Cos(lateral) + branchNormal * Math.Sin(lateral);
                point.Normalize();
                if (pointIndex == segmentCount)
                    point = outlet;

                double smooth = SmoothStep01(t);
                double halfWidth = startHalfWidth +
                    (endHalfWidth - startHalfWidth) * smooth;
                double widthNoise = 0.94 +
                    Math.Sin(phase2 + t * Math.PI * 4.0) * 0.06;
                halfWidth *= widthNoise;

                AddSegment(
                    output,
                    previous,
                    point,
                    previousHalfWidth,
                    halfWidth,
                    shorelineHeight,
                    depth,
                    shoulderWidthMultiplier);

                previous = point;
                previousHalfWidth = halfWidth;
            }
        }

        private static double SmoothStep01(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - 2.0 * t);
        }

        private static void AddSegment(
            List<GeneratedPlanetFeature> output,
            Vector3D a,
            Vector3D b,
            double halfWidthA,
            double halfWidthB,
            int shorelineHeight,
            int depth,
            double shoulderWidthMultiplier)
        {
            Vector3D arcNormal = Vector3D.Cross(a, b);
            double normalLength = arcNormal.Length();
            if (normalLength <= 1e-12)
                return;
            arcNormal /= normalLength;

            double segmentAngle = SafeAcos(Vector3D.Dot(a, b));
            Vector3D capCenter = a + b;
            if (capCenter.LengthSquared() <= 1e-12)
                return;
            capCenter.Normalize();

            double maximumHalfWidth = Math.Max(halfWidthA, halfWidthB);
            double outerHalfWidth = maximumHalfWidth * shoulderWidthMultiplier;
            double capRadius = segmentAngle * 0.5 + outerHalfWidth;

            output.Add(new GeneratedRiverSegment
            {
                A = a,
                B = b,
                ArcNormal = arcNormal,
                SegmentAngle = segmentAngle,
                HalfWidthA = halfWidthA,
                HalfWidthB = halfWidthB,
                ShorelineHeight = shorelineHeight,
                Depth = depth,
                ShoulderWidthMultiplier = shoulderWidthMultiplier,
                Center = capCenter,
                RadiusRadians = capRadius,
                CosRadius = Math.Cos(capRadius),
                SinRadius = Math.Sin(capRadius)
            });
        }

        private static Vector3D Slerp(
            Vector3D a,
            Vector3D b,
            double angle,
            double t)
        {
            double sinAngle = Math.Sin(angle);
            if (Math.Abs(sinAngle) < 1e-10)
            {
                Vector3D linear = a * (1.0 - t) + b * t;
                linear.Normalize();
                return linear;
            }

            Vector3D value =
                a * (Math.Sin((1.0 - t) * angle) / sinAngle) +
                b * (Math.Sin(t * angle) / sinAngle);
            value.Normalize();
            return value;
        }

        private static double ClampDot(double value)
        {
            if (value > 1.0) return 1.0;
            if (value < -1.0) return -1.0;
            return value;
        }

        private static double SafeAcos(double value)
        {
            return Math.Acos(ClampDot(value));
        }

        private sealed class GeneratedRiverSegment : GeneratedPlanetFeature
        {
            internal Vector3D A;
            internal Vector3D B;
            internal Vector3D ArcNormal;
            internal double SegmentAngle;
            internal double HalfWidthA;
            internal double HalfWidthB;
            internal int ShorelineHeight;
            internal int Depth;
            internal double ShoulderWidthMultiplier;

            internal override bool IsAbsoluteHeightFeature
            {
                get { return true; }
            }

            internal override void Accumulate(
                Vector3D direction,
                int currentHeight,
                ref FeaturePixelAccumulator accumulator)
            {
                double alongSegment;
                double angularDistance = DistanceToSegment(direction, out alongSegment);
                double halfWidth = HalfWidthA + (HalfWidthB - HalfWidthA) * alongSegment;
                if (halfWidth <= 1e-12)
                    return;

                double outerHalfWidth = halfWidth * ShoulderWidthMultiplier;
                if (angularDistance >= outerHalfWidth)
                    return;

                double targetHeight;
                if (angularDistance <= halfWidth)
                {
                    double q = angularDistance / halfWidth;
                    double smooth = SmoothStep(q);
                    double centerHeight = Math.Max(0, ShorelineHeight - Depth);
                    targetHeight = centerHeight + (ShorelineHeight - centerHeight) * smooth;
                }
                else
                {
                    double shoulderRange = Math.Max(1e-12, outerHalfWidth - halfWidth);
                    double q = (angularDistance - halfWidth) / shoulderRange;
                    double smooth = SmoothStep(q);
                    targetHeight = ShorelineHeight + (currentHeight - ShorelineHeight) * smooth;
                }

                if (targetHeight >= currentHeight)
                    return;

                if (!accumulator.HasHeightCeiling || targetHeight < accumulator.HeightCeiling)
                {
                    accumulator.HasHeightCeiling = true;
                    accumulator.HeightCeiling = targetHeight;
                }
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

                        alongSegment = SegmentAngle > 1e-12
                            ? SafeAcos(Vector3D.Dot(A, projected)) / SegmentAngle
                            : 0.0;
                        if (alongSegment < 0.0) alongSegment = 0.0;
                        else if (alongSegment > 1.0) alongSegment = 1.0;

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

            private static double SmoothStep(double t)
            {
                if (t <= 0.0) return 0.0;
                if (t >= 1.0) return 1.0;
                return t * t * (3.0 - 2.0 * t);
            }
        }

        private sealed class WetSampleIndex
        {
            private static readonly string[] FaceNames =
            {
                "front.png",
                "back.png",
                "left.png",
                "right.png",
                "up.png",
                "down.png"
            };

            private readonly PlanarPngBitmap[] _faces = new PlanarPngBitmap[6];
            private readonly List<WetSample> _wetSamples = new List<WetSample>();
            private readonly int _shorelineHeight;
            private readonly int _planningResolution;

            internal bool HasWetSamples
            {
                get { return _wetSamples.Count > 0; }
            }

            internal WetSampleIndex(
                IDictionary<string, PlanarPngBitmap> heightImages,
                int shorelineHeight,
                int planningResolution)
            {
                _shorelineHeight = shorelineHeight;
                _planningResolution = Math.Max(8, planningResolution);

                for (int faceIndex = 0; faceIndex < FaceNames.Length; faceIndex++)
                {
                    PlanarPngBitmap image;
                    if (!heightImages.TryGetValue(FaceNames[faceIndex], out image) || image == null)
                    {
                        throw new InvalidOperationException(
                            "River planning requires heightmap face " + FaceNames[faceIndex] + ".");
                    }

                    if (image.BitDepth != 16 || image.Planes == null || image.Planes.Length < 2)
                        throw new InvalidOperationException("River planning requires 16-bit grayscale heightmaps.");

                    _faces[faceIndex] = image;
                    BuildFaceSamples(faceIndex, image);
                }
            }

            internal int SampleHeight(Vector3D direction)
            {
                int faceIndex;
                int x;
                int y;
                ProjectDirection(direction, out faceIndex, out x, out y);
                return ReadHeight(_faces[faceIndex], x, y);
            }

            internal bool FindNearestWet(
                Vector3D source,
                out Vector3D mouth)
            {
                mouth = Vector3D.Zero;
                if (_wetSamples.Count == 0)
                    return false;

                double bestDot = -2.0;
                WetSample best = null;
                for (int i = 0; i < _wetSamples.Count; i++)
                {
                    WetSample sample = _wetSamples[i];
                    double dot = Vector3D.Dot(source, sample.Direction);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = sample;
                    }
                }

                if (best == null)
                    return false;

                PlanarPngBitmap image = _faces[best.FaceIndex];
                int spacingX = Math.Max(1, (image.Width - 1) / Math.Max(1, _planningResolution - 1));
                int spacingY = Math.Max(1, (image.Height - 1) / Math.Max(1, _planningResolution - 1));
                int radius = Math.Max(4, Math.Max(spacingX, spacingY) * 2);
                if (radius > 72)
                    radius = 72;

                int minX = Math.Max(0, best.X - radius);
                int maxX = Math.Min(image.Width - 1, best.X + radius);
                int minY = Math.Max(0, best.Y - radius);
                int maxY = Math.Min(image.Height - 1, best.Y + radius);

                Vector3D refined = best.Direction;
                double refinedDot = bestDot;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (ReadHeight(image, x, y) > _shorelineHeight)
                            continue;

                        Vector3D direction = FractalBrownianMotion.GetCubemapSphereDirection(
                            best.FaceIndex, x, y, image.Width, image.Height);
                        double dot = Vector3D.Dot(source, direction);
                        if (dot > refinedDot)
                        {
                            refinedDot = dot;
                            refined = direction;
                        }
                    }
                }

                mouth = refined;
                return true;
            }

            private void BuildFaceSamples(int faceIndex, PlanarPngBitmap image)
            {
                int resolutionX = Math.Min(_planningResolution, image.Width);
                int resolutionY = Math.Min(_planningResolution, image.Height);

                for (int gy = 0; gy < resolutionY; gy++)
                {
                    int y = resolutionY <= 1
                        ? 0
                        : (int)Math.Round(gy * (image.Height - 1.0) / (resolutionY - 1.0));

                    for (int gx = 0; gx < resolutionX; gx++)
                    {
                        int x = resolutionX <= 1
                            ? 0
                            : (int)Math.Round(gx * (image.Width - 1.0) / (resolutionX - 1.0));

                        if (ReadHeight(image, x, y) > _shorelineHeight)
                            continue;

                        _wetSamples.Add(new WetSample
                        {
                            FaceIndex = faceIndex,
                            X = x,
                            Y = y,
                            Direction = FractalBrownianMotion.GetCubemapSphereDirection(
                                faceIndex, x, y, image.Width, image.Height)
                        });
                    }
                }
            }

            private void ProjectDirection(
                Vector3D direction,
                out int faceIndex,
                out int x,
                out int y)
            {
                double ax = Math.Abs(direction.X);
                double ay = Math.Abs(direction.Y);
                double az = Math.Abs(direction.Z);
                double u;
                double v;

                if (az >= ax && az >= ay)
                {
                    if (direction.Z < 0.0)
                    {
                        faceIndex = 0;
                        u = direction.X / direction.Z;
                        v = direction.Y / direction.Z;
                    }
                    else
                    {
                        faceIndex = 1;
                        u = direction.X / direction.Z;
                        v = -direction.Y / direction.Z;
                    }
                }
                else if (ax >= ay)
                {
                    if (direction.X > 0.0)
                    {
                        faceIndex = 2;
                        u = -direction.Z / direction.X;
                        v = -direction.Y / direction.X;
                    }
                    else
                    {
                        faceIndex = 3;
                        u = -direction.Z / direction.X;
                        v = direction.Y / direction.X;
                    }
                }
                else
                {
                    if (direction.Y > 0.0)
                    {
                        faceIndex = 4;
                        u = -direction.X / direction.Y;
                        v = -direction.Z / direction.Y;
                    }
                    else
                    {
                        faceIndex = 5;
                        u = -direction.X / direction.Y;
                        v = direction.Z / direction.Y;
                    }
                }

                if (u < -1.0) u = -1.0;
                else if (u > 1.0) u = 1.0;
                if (v < -1.0) v = -1.0;
                else if (v > 1.0) v = 1.0;

                PlanarPngBitmap image = _faces[faceIndex];
                x = (int)Math.Round((u + 1.0) * 0.5 * (image.Width - 1));
                y = (int)Math.Round((v + 1.0) * 0.5 * (image.Height - 1));
                if (x < 0) x = 0;
                else if (x >= image.Width) x = image.Width - 1;
                if (y < 0) y = 0;
                else if (y >= image.Height) y = image.Height - 1;
            }

            private static int ReadHeight(PlanarPngBitmap image, int x, int y)
            {
                int offset = y * image.Width + x;
                return (image.Planes[0][offset] << 8) | image.Planes[1][offset];
            }

            private sealed class WetSample
            {
                internal int FaceIndex;
                internal int X;
                internal int Y;
                internal Vector3D Direction;
            }
        }
    }
}
