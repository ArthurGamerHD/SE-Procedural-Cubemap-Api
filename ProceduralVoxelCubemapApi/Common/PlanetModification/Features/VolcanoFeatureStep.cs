using System;
using System.Collections.Generic;
using VoxelCubemapApi.Common.Networking;
using VoxelCubemapApi.Common.Noise;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.Features
{
    internal sealed class VolcanoFeatureStep : IPlanetFeatureStep
    {
        internal static readonly VolcanoFeatureStep Instance =
            new VolcanoFeatureStep();

        private VolcanoFeatureStep()
        {
        }

        public void Expand(
            FeatureOperation operation,
            long planetSeed,
            List<GeneratedPlanetFeature> output)
        {
            if (operation == null || operation.VolcanoFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < operation.VolcanoFields.Count; fieldIndex++)
                ExpandField(output, planetSeed, operation.VolcanoFields[fieldIndex]);
        }

        public void WriteRuntime(
            FeatureOperation source,
            RuntimeProceduralFeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.VolcanoFields.Count; fieldIndex++)
            {
                VolcanoFieldOperation field = source.VolcanoFields[fieldIndex];
                target.VolcanoFields.Add(new RuntimeProceduralVolcanoField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
                    MinimumHeight = field.MinimumHeight,
                    MaximumHeight = field.MaximumHeight,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void ReadRuntime(
            RuntimeProceduralFeatureOperation source,
            FeatureOperation target)
        {
            if (source.VolcanoFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.VolcanoFields.Count; fieldIndex++)
            {
                RuntimeProceduralVolcanoField field = source.VolcanoFields[fieldIndex];
                if (field == null)
                    continue;

                target.VolcanoFields.Add(new VolcanoFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
                    MinimumHeight = field.MinimumHeight,
                    MaximumHeight = field.MaximumHeight,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void WriteSynced(
            FeatureOperation source,
            SyncedFeatureOperation target)
        {
            if (target.VolcanoFields == null)
                target.VolcanoFields = new List<SyncedVolcanoField>();

            for (int fieldIndex = 0; fieldIndex < source.VolcanoFields.Count; fieldIndex++)
            {
                VolcanoFieldOperation field = source.VolcanoFields[fieldIndex];
                target.VolcanoFields.Add(new SyncedVolcanoField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
                    MinimumHeight = field.MinimumHeight,
                    MaximumHeight = field.MaximumHeight,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void ReadSynced(
            SyncedFeatureOperation source,
            FeatureOperation target)
        {
            if (source.VolcanoFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.VolcanoFields.Count; fieldIndex++)
            {
                SyncedVolcanoField field = source.VolcanoFields[fieldIndex];
                if (field == null)
                    continue;

                target.VolcanoFields.Add(new VolcanoFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
                    MinimumHeight = field.MinimumHeight,
                    MaximumHeight = field.MaximumHeight,
                    TargetSize = field.TargetSize
                });
            }
        }

        public void Clone(
            FeatureOperation source,
            FeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.VolcanoFields.Count; fieldIndex++)
            {
                VolcanoFieldOperation field = source.VolcanoFields[fieldIndex];
                target.VolcanoFields.Add(new VolcanoFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
                    MinimumHeight = field.MinimumHeight,
                    MaximumHeight = field.MaximumHeight,
                    TargetSize = field.TargetSize
                });
            }
        }

        private static void ExpandField(
            List<GeneratedPlanetFeature> output,
            long planetSeed,
            VolcanoFieldOperation field)
        {
            if (field == null || field.Count <= 0)
                return;

            long fieldSeed = NoiseMath.DeriveSeed(planetSeed, field.SeedOffset);
            for (int i = 0; i < field.Count; i++)
            {
                long volcanoSeed = NoiseMath.DeriveSeed(fieldSeed, i + 1);
                double u0 = NoiseMath.HashToUnit(i, 0, 0, volcanoSeed, 0xD2511F53u);
                double u1 = NoiseMath.HashToUnit(i, 1, 0, volcanoSeed, 0xCD9E8D57u);
                double u2 = NoiseMath.HashToUnit(i, 2, 0, volcanoSeed, 0x9E3779B9u);
                double u3 = NoiseMath.HashToUnit(i, 3, 0, volcanoSeed, 0x85EBCA6Bu);

                double z = u0 * 2.0 - 1.0;
                double azimuth = u1 * Math.PI * 2.0;
                double xy = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                Vector3D center = new Vector3D(
                    xy * Math.Cos(azimuth),
                    z,
                    xy * Math.Sin(azimuth));

                double targetSize = field.TargetSize > 0.0f && field.TargetSize < 1.0f
                    ? field.TargetSize
                    : 0.25;
                double sizeExponent = (1.0 - targetSize) / targetSize;
                double size = Math.Pow(u2, sizeExponent);
                double radius = field.MinimumRadiusDegrees +
                    (field.MaximumRadiusDegrees - field.MinimumRadiusDegrees) * size;
                double heightFactor = Math.Min(1.0, Math.Max(0.0, size * 0.80 + u3 * 0.20));
                int volcanoHeight = (int)(field.MinimumHeight +
                    (field.MaximumHeight - field.MinimumHeight) * heightFactor + 0.5);
                double radiusRadians = radius * (Math.PI / 180.0);

                output.Add(new GeneratedVolcano
                {
                    Center = center,
                    RadiusRadians = radiusRadians,
                    CosRadius = Math.Cos(radiusRadians),
                    SinRadius = Math.Sin(radiusRadians),
                    Height = volcanoHeight
                });
            }
        }

        private sealed class GeneratedVolcano : GeneratedPlanetFeature
        {
            internal int Height;

            internal override void Accumulate(
                Vector3D direction,
                ref FeaturePixelAccumulator accumulator)
            {
                double score = Sample(direction);
                if (score == 0.0)
                    return;

                double candidateDelta = score * Height;
                if (candidateDelta > accumulator.StrongestPositiveDelta)
                    accumulator.StrongestPositiveDelta = candidateDelta;
            }

            private double Sample(Vector3D direction)
            {
                double dot = Vector3D.Dot(direction, Center);
                if (dot < CosRadius)
                    return 0.0;

                if (dot > 1.0) dot = 1.0;
                else if (dot < -1.0) dot = -1.0;

                double normalizedRadius = Math.Acos(dot) / RadiusRadians;
                if (normalizedRadius >= 1.0)
                    return 0.0;

                const double calderaRim = 0.18;
                if (normalizedRadius < calderaRim)
                {
                    double t = normalizedRadius / calderaRim;
                    t = t * t * (3.0 - 2.0 * t);
                    return 0.28 + 0.72 * t;
                }

                double outer = (normalizedRadius - calderaRim) / (1.0 - calderaRim);
                outer = outer * outer * (3.0 - 2.0 * outer);
                return 1.0 - outer;
            }
        }
    }
}
