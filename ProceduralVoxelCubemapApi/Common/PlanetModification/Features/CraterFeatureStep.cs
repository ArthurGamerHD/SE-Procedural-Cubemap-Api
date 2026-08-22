using System;
using System.Collections.Generic;
using VoxelCubemapApi.Common.Networking;
using VoxelCubemapApi.Common.Noise;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.Features
{
    internal sealed class CraterFeatureStep : IPlanetFeatureStep
    {
        internal static readonly CraterFeatureStep Instance =
            new CraterFeatureStep();

        private CraterFeatureStep()
        {
        }

        public void Expand(
            FeatureOperation operation,
            long planetSeed,
            List<GeneratedPlanetFeature> output)
        {
            if (operation == null || operation.CraterFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < operation.CraterFields.Count; fieldIndex++)
                ExpandField(output, planetSeed, operation.CraterFields[fieldIndex]);
        }

        public void WriteRuntime(
            FeatureOperation source,
            RuntimeProceduralFeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.CraterFields.Count; fieldIndex++)
            {
                CraterFieldOperation field = source.CraterFields[fieldIndex];
                target.CraterFields.Add(new RuntimeProceduralCraterField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
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
            if (source.CraterFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.CraterFields.Count; fieldIndex++)
            {
                RuntimeProceduralCraterField field = source.CraterFields[fieldIndex];
                if (field == null)
                    continue;

                target.CraterFields.Add(new CraterFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
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
            if (target.CraterFields == null)
                target.CraterFields = new List<SyncedCraterField>();

            for (int fieldIndex = 0; fieldIndex < source.CraterFields.Count; fieldIndex++)
            {
                CraterFieldOperation field = source.CraterFields[fieldIndex];
                target.CraterFields.Add(new SyncedCraterField
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
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
            if (source.CraterFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.CraterFields.Count; fieldIndex++)
            {
                SyncedCraterField field = source.CraterFields[fieldIndex];
                if (field == null)
                    continue;

                target.CraterFields.Add(new CraterFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
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
            for (int fieldIndex = 0; fieldIndex < source.CraterFields.Count; fieldIndex++)
            {
                CraterFieldOperation field = source.CraterFields[fieldIndex];
                target.CraterFields.Add(new CraterFieldOperation
                {
                    Count = field.Count,
                    SeedOffset = field.SeedOffset,
                    MinimumRadiusDegrees = field.MinimumRadiusDegrees,
                    MaximumRadiusDegrees = field.MaximumRadiusDegrees,
                    MinimumDepth = field.MinimumDepth,
                    MaximumDepth = field.MaximumDepth,
                    TargetSize = field.TargetSize
                });
            }
        }

        private static void ExpandField(
            List<GeneratedPlanetFeature> output,
            long planetSeed,
            CraterFieldOperation field)
        {
            if (field == null || field.Count <= 0)
                return;

            long fieldSeed = NoiseMath.DeriveSeed(planetSeed, field.SeedOffset);
            for (int i = 0; i < field.Count; i++)
            {
                long craterSeed = NoiseMath.DeriveSeed(fieldSeed, i + 1);
                double u0 = NoiseMath.HashToUnit(i, 0, 0, craterSeed, 0xA341316Cu);
                double u1 = NoiseMath.HashToUnit(i, 1, 0, craterSeed, 0xC8013EA4u);
                double u2 = NoiseMath.HashToUnit(i, 2, 0, craterSeed, 0xAD90777Du);
                double u3 = NoiseMath.HashToUnit(i, 3, 0, craterSeed, 0x7E95761Eu);

                double z = u0 * 2.0 - 1.0;
                double azimuth = u1 * Math.PI * 2.0;
                double xy = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                Vector3D center = new Vector3D(
                    xy * Math.Cos(azimuth),
                    z,
                    xy * Math.Sin(azimuth));

                double targetSize = field.TargetSize > 0.0f && field.TargetSize < 1.0f
                    ? field.TargetSize
                    : 1.0 / 3.0;
                double sizeExponent = (1.0 - targetSize) / targetSize;
                double size = Math.Pow(u2, sizeExponent);
                double radius = field.MinimumRadiusDegrees +
                    (field.MaximumRadiusDegrees - field.MinimumRadiusDegrees) * size;
                double depthFactor = Math.Min(1.0, Math.Max(0.0, size * 0.85 + u3 * 0.15));
                int depth = (int)(field.MinimumDepth +
                    (field.MaximumDepth - field.MinimumDepth) * depthFactor + 0.5);
                double radiusRadians = radius * (Math.PI / 180.0);

                output.Add(new GeneratedCrater
                {
                    Field = new RadialField(
                        center.X,
                        center.Y,
                        center.Z,
                        radius,
                        RadialFieldProfile.Crater),
                    Center = center,
                    RadiusRadians = radiusRadians,
                    CosRadius = Math.Cos(radiusRadians),
                    SinRadius = Math.Sin(radiusRadians),
                    Depth = depth
                });
            }
        }

        private sealed class GeneratedCrater : GeneratedPlanetFeature
        {
            internal RadialField Field;
            internal int Depth;

            internal override void Accumulate(
                Vector3D direction,
                int currentHeight,
                ref FeaturePixelAccumulator accumulator)
            {
                double score = Field.Sample(direction);
                if (score != 0.0)
                    accumulator.AdditiveDelta += score * Depth;
            }
        }
    }
}
