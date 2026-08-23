using System;
using System.Collections.Generic;
using ProceduralCubemapApi.Common.Networking;
using ProceduralCubemapApi.Common.Noise;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using ProceduralCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace ProceduralCubemapApi.Common.PlanetModification.Features
{
    internal sealed class MountainFeatureStep : IPlanetFeatureStep
    {
        internal static readonly MountainFeatureStep Instance =
            new MountainFeatureStep();

        private MountainFeatureStep()
        {
        }

        public void Expand(
            FeatureOperation operation,
            long planetSeed,
            List<GeneratedPlanetFeature> output)
        {
            if (operation == null || operation.MountainFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < operation.MountainFields.Count; fieldIndex++)
                ExpandField(output, planetSeed, operation.MountainFields[fieldIndex]);
        }

        public void WriteRuntime(
            FeatureOperation source,
            RuntimeProceduralFeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.MountainFields.Count; fieldIndex++)
            {
                MountainFieldOperation field = source.MountainFields[fieldIndex];
                target.MountainFields.Add(new RuntimeProceduralMountainField
                {
                    PlateCount = field.PlateCount,
                    SeedOffset = field.SeedOffset,
                    MountainWidthDegrees = field.MountainWidthDegrees,
                    MaximumHeight = field.MaximumHeight,
                    MajorFrequency = field.MajorFrequency,
                    MajorOctaves = field.MajorOctaves,
                    MajorPercent = field.MajorPercent,
                    MajorCeiling = field.MajorCeiling,
                    MinorFrequency = field.MinorFrequency,
                    MinorOctaves = field.MinorOctaves,
                    MinorPercent = field.MinorPercent,
                    MinorCeiling = field.MinorCeiling,
                    DetailFrequency = field.DetailFrequency,
                    DetailOctaves = field.DetailOctaves
                });
            }
        }

        public void ReadRuntime(
            RuntimeProceduralFeatureOperation source,
            FeatureOperation target)
        {
            if (source.MountainFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.MountainFields.Count; fieldIndex++)
            {
                RuntimeProceduralMountainField field = source.MountainFields[fieldIndex];
                if (field == null)
                    continue;

                target.MountainFields.Add(new MountainFieldOperation
                {
                    PlateCount = field.PlateCount,
                    SeedOffset = field.SeedOffset,
                    MountainWidthDegrees = field.MountainWidthDegrees,
                    MaximumHeight = field.MaximumHeight,
                    MajorFrequency = field.MajorFrequency,
                    MajorOctaves = field.MajorOctaves,
                    MajorPercent = field.MajorPercent,
                    MajorCeiling = field.MajorCeiling,
                    MinorFrequency = field.MinorFrequency,
                    MinorOctaves = field.MinorOctaves,
                    MinorPercent = field.MinorPercent,
                    MinorCeiling = field.MinorCeiling,
                    DetailFrequency = field.DetailFrequency,
                    DetailOctaves = field.DetailOctaves
                });
            }
        }

        public void WriteSynced(
            FeatureOperation source,
            SyncedFeatureOperation target)
        {
            if (target.MountainFields == null)
                target.MountainFields = new List<SyncedMountainField>();

            for (int fieldIndex = 0; fieldIndex < source.MountainFields.Count; fieldIndex++)
            {
                MountainFieldOperation field = source.MountainFields[fieldIndex];
                target.MountainFields.Add(new SyncedMountainField
                {
                    PlateCount = field.PlateCount,
                    SeedOffset = field.SeedOffset,
                    MountainWidthDegrees = field.MountainWidthDegrees,
                    MaximumHeight = field.MaximumHeight,
                    MajorFrequency = field.MajorFrequency,
                    MajorOctaves = field.MajorOctaves,
                    MajorPercent = field.MajorPercent,
                    MajorCeiling = field.MajorCeiling,
                    MinorFrequency = field.MinorFrequency,
                    MinorOctaves = field.MinorOctaves,
                    MinorPercent = field.MinorPercent,
                    MinorCeiling = field.MinorCeiling,
                    DetailFrequency = field.DetailFrequency,
                    DetailOctaves = field.DetailOctaves
                });
            }
        }

        public void ReadSynced(
            SyncedFeatureOperation source,
            FeatureOperation target)
        {
            if (source.MountainFields == null)
                return;

            for (int fieldIndex = 0; fieldIndex < source.MountainFields.Count; fieldIndex++)
            {
                SyncedMountainField field = source.MountainFields[fieldIndex];
                if (field == null)
                    continue;

                target.MountainFields.Add(new MountainFieldOperation
                {
                    PlateCount = field.PlateCount,
                    SeedOffset = field.SeedOffset,
                    MountainWidthDegrees = field.MountainWidthDegrees,
                    MaximumHeight = field.MaximumHeight,
                    MajorFrequency = field.MajorFrequency,
                    MajorOctaves = field.MajorOctaves,
                    MajorPercent = field.MajorPercent,
                    MajorCeiling = field.MajorCeiling,
                    MinorFrequency = field.MinorFrequency,
                    MinorOctaves = field.MinorOctaves,
                    MinorPercent = field.MinorPercent,
                    MinorCeiling = field.MinorCeiling,
                    DetailFrequency = field.DetailFrequency,
                    DetailOctaves = field.DetailOctaves
                });
            }
        }

        public void Clone(
            FeatureOperation source,
            FeatureOperation target)
        {
            for (int fieldIndex = 0; fieldIndex < source.MountainFields.Count; fieldIndex++)
            {
                MountainFieldOperation field = source.MountainFields[fieldIndex];
                target.MountainFields.Add(new MountainFieldOperation
                {
                    PlateCount = field.PlateCount,
                    SeedOffset = field.SeedOffset,
                    MountainWidthDegrees = field.MountainWidthDegrees,
                    MaximumHeight = field.MaximumHeight,
                    MajorFrequency = field.MajorFrequency,
                    MajorOctaves = field.MajorOctaves,
                    MajorPercent = field.MajorPercent,
                    MajorCeiling = field.MajorCeiling,
                    MinorFrequency = field.MinorFrequency,
                    MinorOctaves = field.MinorOctaves,
                    MinorPercent = field.MinorPercent,
                    MinorCeiling = field.MinorCeiling,
                    DetailFrequency = field.DetailFrequency,
                    DetailOctaves = field.DetailOctaves
                });
            }
        }

        private static void ExpandField(
            List<GeneratedPlanetFeature> output,
            long planetSeed,
            MountainFieldOperation field)
        {
            if (field == null || field.PlateCount < 2 || field.MaximumHeight <= 0)
                return;

            long fieldSeed = NoiseMath.DeriveSeed(planetSeed, field.SeedOffset);
            TectonicPlate[] plates = GeneratePlates(fieldSeed, field.PlateCount);
            Vector3D[] boundaryNormals = BuildBoundaryNormals(plates);

            double averagePlateScale = Math.Sqrt((4.0 * Math.PI) / field.PlateCount);
            double halfWidth = field.MountainWidthDegrees * 0.5 * (Math.PI / 180.0);

            Vector3D majorAxis = RandomUnitVector(
                NoiseMath.DeriveSeed(fieldSeed, 0x4D414A4FL), 0, 0, 0xA511E9B3u);
            Vector3D minorAxis = RandomUnitVector(
                NoiseMath.DeriveSeed(fieldSeed, 0x4D494E4FL), 0, 0, 0x63D83595u);

            if (Math.Abs(Vector3D.Dot(majorAxis, minorAxis)) > 0.92)
            {
                minorAxis = Vector3D.Cross(majorAxis, Math.Abs(majorAxis.Y) < 0.9
                    ? Vector3D.Up
                    : Vector3D.Right);
                if (minorAxis.LengthSquared() > 1e-12)
                    minorAxis.Normalize();
            }

            output.Add(new GeneratedMountainField
            {
                Plates = plates,
                BoundaryNormals = boundaryNormals,
                PlateCount = field.PlateCount,
                HalfWidthRadians = halfWidth,
                MaximumHeight = field.MaximumHeight,
                MajorNoise = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D414A4E4F495345L),
                    field.MajorFrequency,
                    field.MajorOctaves),
                MinorNoise = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D494E4E4F495345L),
                    field.MinorFrequency,
                    field.MinorOctaves),
                MountainFractal = new RidgedMultifractalTerrain3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D4F554E54465241L),
                    Math.Max(4.0, field.DetailFrequency * 0.45),
                    Math.Max(5, Math.Min(8, field.DetailOctaves + 2)),
                    2.03,
                    2.15,
                    1.0,
                    1.0),
                MountainWarpX = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D4F554E54575831L),
                    Math.Max(1.0, field.DetailFrequency * 0.09),
                    3),
                MountainWarpY = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D4F554E54575932L),
                    Math.Max(1.0, field.DetailFrequency * 0.09),
                    3),
                MountainWarpZ = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D4F554E54575A33L),
                    Math.Max(1.0, field.DetailFrequency * 0.09),
                    3),
                MountainMassifNoise = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D4F554E544D4153L),
                    Math.Max(1.1, field.DetailFrequency * 0.075),
                    3),
                MountainPeakNoise = new RidgedNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4D4F554E54504541L),
                    Math.Max(5.0, field.DetailFrequency * 0.85),
                    Math.Max(3, Math.Min(5, field.DetailOctaves + 1)),
                    2.08,
                    0.48,
                    2.35),
                WidthNoise = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x57494454484E4F49L),
                    Math.Max(0.75, field.MajorFrequency * 0.65),
                    2),
                JunctionWidthNoise = new FbmNoise3D(
                    NoiseMath.DeriveSeed(fieldSeed, 0x4A554E4357494454L),
                    Math.Max(0.7, field.MajorFrequency * 0.42),
                    2),
                MajorAxis = majorAxis,
                MinorAxis = minorAxis,
                MajorWarpRadians = averagePlateScale * field.MajorPercent * 0.01,
                MinorWarpRadians = averagePlateScale * field.MinorPercent * 0.01,
                MajorCeiling = field.MajorCeiling,
                MinorCeiling = field.MinorCeiling,
                Center = Vector3D.Forward,
                RadiusRadians = Math.PI,
                CosRadius = -1.0,
                SinRadius = 0.0
            });
        }

        private static TectonicPlate[] GeneratePlates(
            long seed,
            int plateCount)
        {
            var plates = new TectonicPlate[plateCount];
            var centers = new Vector3D[plateCount];

            const int candidateCount = 20;
            for (int plateIndex = 0; plateIndex < plateCount; plateIndex++)
            {
                Vector3D bestCenter = Vector3D.Forward;
                double bestNearestDot = double.MaxValue;
                int candidates = plateIndex == 0 ? 1 : candidateCount;

                for (int candidateIndex = 0; candidateIndex < candidates; candidateIndex++)
                {
                    Vector3D candidate = RandomUnitVector(
                        seed,
                        plateIndex,
                        candidateIndex,
                        0x9E3779B9u);

                    double nearestDot = -2.0;
                    for (int existingIndex = 0; existingIndex < plateIndex; existingIndex++)
                    {
                        double dot = Vector3D.Dot(candidate, centers[existingIndex]);
                        if (dot > nearestDot)
                            nearestDot = dot;
                    }

                    if (plateIndex == 0 || nearestDot < bestNearestDot)
                    {
                        bestNearestDot = nearestDot;
                        bestCenter = candidate;
                    }
                }

                centers[plateIndex] = bestCenter;
                plates[plateIndex] = new TectonicPlate
                {
                    Center = bestCenter,
                    AngularVelocity = RandomUnitVector(
                        NoiseMath.DeriveSeed(seed, 0x504C4154454D4F54L),
                        plateIndex,
                        73,
                        0x85EBCA77u)
                };
            }

            return plates;
        }

        private static Vector3D[] BuildBoundaryNormals(TectonicPlate[] plates)
        {
            int count = plates.Length;
            var result = new Vector3D[count * count];

            for (int a = 0; a < count; a++)
            {
                for (int b = 0; b < count; b++)
                {
                    if (a == b)
                        continue;

                    Vector3D normal = plates[b].Center - plates[a].Center;
                    if (normal.LengthSquared() > 1e-12)
                        normal.Normalize();
                    result[a * count + b] = normal;
                }
            }

            return result;
        }

        private static Vector3D RandomUnitVector(
            long seed,
            int a,
            int b,
            uint salt)
        {
            double u0 = NoiseMath.HashToUnit(a, b, 0, seed, salt);
            double u1 = NoiseMath.HashToUnit(a, b, 1, seed, salt ^ 0xB5297A4Du);
            double z = u0 * 2.0 - 1.0;
            double azimuth = u1 * Math.PI * 2.0;
            double xy = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
            return new Vector3D(
                xy * Math.Cos(azimuth),
                z,
                xy * Math.Sin(azimuth));
        }

        private static Vector3D RotateAroundAxis(
            Vector3D value,
            Vector3D axis,
            double angle)
        {
            if (Math.Abs(angle) < 1e-12)
                return value;

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            return value * cos +
                Vector3D.Cross(axis, value) * sin +
                axis * (Vector3D.Dot(axis, value) * (1.0 - cos));
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        private sealed class TectonicPlate
        {
            internal Vector3D Center;
            internal Vector3D AngularVelocity;
        }

        private sealed class GeneratedMountainField : GeneratedPlanetFeature
        {
            internal TectonicPlate[] Plates;
            internal Vector3D[] BoundaryNormals;
            internal int PlateCount;
            internal double HalfWidthRadians;
            internal int MaximumHeight;
            internal INoise3D MajorNoise;
            internal INoise3D MinorNoise;
            internal INoise3D MountainFractal;
            internal INoise3D MountainWarpX;
            internal INoise3D MountainWarpY;
            internal INoise3D MountainWarpZ;
            internal INoise3D MountainMassifNoise;
            internal INoise3D MountainPeakNoise;
            internal INoise3D WidthNoise;
            internal INoise3D JunctionWidthNoise;
            internal Vector3D MajorAxis;
            internal Vector3D MinorAxis;
            internal double MajorWarpRadians;
            internal double MinorWarpRadians;
            internal double MajorCeiling;
            internal double MinorCeiling;

            internal override void Accumulate(
                Vector3D direction,
                int currentHeight,
                ref FeaturePixelAccumulator accumulator)
            {
                Vector3D warped = WarpDirection(direction);

                int firstPlate;
                int secondPlate;
                int thirdPlate;
                double firstDot;
                double secondDot;
                double thirdDot;
                FindNearestPlates(
                    warped,
                    out firstPlate,
                    out secondPlate,
                    out thirdPlate,
                    out firstDot,
                    out secondDot,
                    out thirdDot);

                if (firstPlate < 0 || secondPlate < 0)
                    return;

                double strongestDelta = 0.0;

                double boundaryDelta = EvaluateBoundaryUplift(
                    direction,
                    warped,
                    firstPlate,
                    secondPlate);
                if (boundaryDelta > strongestDelta)
                    strongestDelta = boundaryDelta;

                if (thirdPlate >= 0)
                {
                    const double maximumJunctionRadiusFactor = 3.35;
                    double firstDistance = Math.Acos(Clamp(firstDot, -1.0, 1.0));
                    double thirdDistance = Math.Acos(Clamp(thirdDot, -1.0, 1.0));
                    double thirdCompetitionDistance =
                        Math.Max(0.0, (thirdDistance - firstDistance) * 0.5);

                    if (thirdCompetitionDistance <
                        HalfWidthRadians * maximumJunctionRadiusFactor)
                    {
                        double junctionDelta = EvaluateTripleJunctionMountains(
                            direction,
                            warped,
                            firstPlate,
                            secondPlate,
                            thirdPlate,
                            maximumJunctionRadiusFactor);

                        if (junctionDelta > strongestDelta)
                            strongestDelta = junctionDelta;
                    }
                }

                if (strongestDelta > accumulator.StrongestPositiveDelta)
                    accumulator.StrongestPositiveDelta = strongestDelta;
            }

            private double EvaluateBoundaryUplift(
                Vector3D direction,
                Vector3D warped,
                int firstPlate,
                int secondPlate)
            {
                Vector3D pairNormal = BoundaryNormals[firstPlate * PlateCount + secondPlate];
                if (pairNormal.LengthSquared() < 1e-12)
                    return 0.0;

                double signedPlaneDistance = Clamp(
                    Vector3D.Dot(warped, pairNormal),
                    -1.0,
                    1.0);
                double boundaryDistance = Math.Abs(Math.Asin(signedPlaneDistance));

                const double maximumWidthFactor = 1.95;
                if (boundaryDistance >= HalfWidthRadians * maximumWidthFactor)
                    return 0.0;

                Vector3D boundaryPoint = warped -
                    pairNormal * Vector3D.Dot(warped, pairNormal);
                double boundaryPointLengthSquared = boundaryPoint.LengthSquared();
                if (boundaryPointLengthSquared <= 1e-12)
                    return 0.0;
                boundaryPoint /= Math.Sqrt(boundaryPointLengthSquared);

                double widthSample = ToUnit(WidthNoise.Sample(
                    boundaryPoint.X,
                    boundaryPoint.Y,
                    boundaryPoint.Z));
                double localWidthFactor = Lerp(
                    0.55,
                    maximumWidthFactor,
                    widthSample * widthSample);
                double localHalfWidth = HalfWidthRadians * localWidthFactor;
                if (boundaryDistance >= localHalfWidth)
                    return 0.0;

                double tectonicStrength = ComputeTectonicStrength(
                    firstPlate,
                    secondPlate,
                    direction);
                if (tectonicStrength <= 1e-6)
                    return 0.0;

                double normalizedBoundaryDistance = Clamp(
                    boundaryDistance / Math.Max(localHalfWidth, 1e-12),
                    0.0,
                    1.0);
                double boundaryCore = 1.0 - Smooth01(normalizedBoundaryDistance);
                double corridorMask = Math.Pow(boundaryCore, 1.45);
                if (corridorMask <= 1e-6)
                    return 0.0;

                double thresholdBias = Lerp(
                    -0.105,
                    0.145,
                    Smooth01(normalizedBoundaryDistance));

                Vector3D fractalDirection = BuildBoundaryAlignedSampleDirection(
                    boundaryPoint,
                    pairNormal,
                    Math.Asin(signedPlaneDistance),
                    2.35);

                double fractalTerrain = SampleMountainTerrain(
                    fractalDirection,
                    localHalfWidth * 0.55,
                    thresholdBias);
                if (fractalTerrain <= 1e-6)
                    return 0.0;

                return MaximumHeight *
                    tectonicStrength *
                    corridorMask *
                    fractalTerrain;
            }

            private double EvaluateTripleJunctionMountains(
                Vector3D direction,
                Vector3D warped,
                int plateA,
                int plateB,
                int plateC,
                double maximumRadiusFactor)
            {
                Vector3D junctionPoint;
                double junctionDistance;
                if (!TryGetTripleJunction(
                    warped,
                    plateA,
                    plateB,
                    plateC,
                    out junctionPoint,
                    out junctionDistance))
                {
                    return 0.0;
                }

                double junctionWidthSample = ToUnit(JunctionWidthNoise.Sample(
                    junctionPoint.X,
                    junctionPoint.Y,
                    junctionPoint.Z));
                double localRadiusFactor = Lerp(
                    2.15,
                    maximumRadiusFactor,
                    junctionWidthSample);
                double junctionRadius = HalfWidthRadians * localRadiusFactor;
                if (junctionDistance >= junctionRadius)
                    return 0.0;

                double strengthAB = ComputeTectonicStrength(plateA, plateB, direction);
                double strengthAC = ComputeTectonicStrength(plateA, plateC, direction);
                double strengthBC = ComputeTectonicStrength(plateB, plateC, direction);

                double strongestPair = Math.Max(strengthAB, Math.Max(strengthAC, strengthBC));
                if (strongestPair <= 1e-6)
                    return 0.0;

                int activePairs = 0;
                if (strengthAB > 0.035) activePairs++;
                if (strengthAC > 0.035) activePairs++;
                if (strengthBC > 0.035) activePairs++;

                double averageStrength = (strengthAB + strengthAC + strengthBC) / 3.0;
                double interactionFactor;
                switch (activePairs)
                {
                    case 3:
                        interactionFactor = 1.12;
                        break;
                    case 2:
                        interactionFactor = 0.96;
                        break;
                    default:
                        interactionFactor = 0.72;
                        break;
                }

                double junctionTectonicStrength = Clamp(
                    strongestPair * interactionFactor + averageStrength * 0.28,
                    0.0,
                    1.0);


                double regionMask = SoftMask(
                    junctionDistance,
                    junctionRadius,
                    1.10);
                if (regionMask <= 1e-6)
                    return 0.0;

                double pairWidth = Math.Max(
                    HalfWidthRadians * 1.35,
                    junctionRadius * 0.62);

                double terrainAB = SampleJunctionPairTerrain(
                    warped,
                    plateA,
                    plateB,
                    pairWidth,
                    strengthAB);
                double terrainAC = SampleJunctionPairTerrain(
                    warped,
                    plateA,
                    plateC,
                    pairWidth,
                    strengthAC);
                double terrainBC = SampleJunctionPairTerrain(
                    warped,
                    plateB,
                    plateC,
                    pairWidth,
                    strengthBC);

                double strongestTerrain = Math.Max(
                    terrainAB,
                    Math.Max(terrainAC, terrainBC));
                if (strongestTerrain <= 1e-6)
                    return 0.0;

                double combinedTerrain = terrainAB + terrainAC + terrainBC;
                double overlapTerrain = Math.Max(
                    0.0,
                    combinedTerrain - strongestTerrain);
                double fractalTerrain = Clamp(
                    strongestTerrain + overlapTerrain * 0.36,
                    0.0,
                    1.22);

                double junctionBoost = activePairs >= 3
                    ? 1.08
                    : (activePairs == 2 ? 1.0 : 0.88);

                return MaximumHeight *
                    junctionTectonicStrength *
                    regionMask *
                    fractalTerrain *
                    junctionBoost;
            }

            private double SampleJunctionPairTerrain(
                Vector3D warped,
                int plateA,
                int plateB,
                double pairWidth,
                double tectonicStrength)
            {
                if (tectonicStrength <= 1e-6 || pairWidth <= 1e-12)
                    return 0.0;

                Vector3D pairNormal = BoundaryNormals[plateA * PlateCount + plateB];
                if (pairNormal.LengthSquared() <= 1e-12)
                    return 0.0;

                double signedPlaneDistance = Clamp(
                    Vector3D.Dot(warped, pairNormal),
                    -1.0,
                    1.0);
                double signedBoundaryDistance = Math.Asin(signedPlaneDistance);
                double boundaryDistance = Math.Abs(signedBoundaryDistance);
                if (boundaryDistance >= pairWidth)
                    return 0.0;

                Vector3D boundaryPoint = warped -
                    pairNormal * Vector3D.Dot(warped, pairNormal);
                double boundaryPointLengthSquared = boundaryPoint.LengthSquared();
                if (boundaryPointLengthSquared <= 1e-12)
                    return 0.0;
                boundaryPoint /= Math.Sqrt(boundaryPointLengthSquared);

                double normalizedDistance = Clamp(
                    boundaryDistance / pairWidth,
                    0.0,
                    1.0);
                double backbone = Math.Pow(
                    1.0 - Smooth01(normalizedDistance),
                    1.20);
                if (backbone <= 1e-6)
                    return 0.0;

                double thresholdBias = Lerp(
                    -0.135,
                    0.10,
                    Smooth01(normalizedDistance));

                Vector3D fractalDirection = BuildBoundaryAlignedSampleDirection(
                    boundaryPoint,
                    pairNormal,
                    signedBoundaryDistance,
                    2.05);

                double terrain = SampleMountainTerrain(
                    fractalDirection,
                    pairWidth * 0.42,
                    thresholdBias);

                return terrain *
                    backbone *
                    (0.62 + 0.38 * tectonicStrength);
            }

            private static Vector3D BuildBoundaryAlignedSampleDirection(
                Vector3D boundaryPoint,
                Vector3D pairNormal,
                double signedBoundaryDistance,
                double acrossStretch)
            {
                Vector3D boundaryTangent = Vector3D.Cross(
                    pairNormal,
                    boundaryPoint);
                double tangentLengthSquared = boundaryTangent.LengthSquared();
                if (tangentLengthSquared <= 1e-12)
                    return boundaryPoint;
                boundaryTangent /= Math.Sqrt(tangentLengthSquared);

                double stretchedDistance = signedBoundaryDistance *
                    Math.Max(1.0, acrossStretch);
                Vector3D result = RotateAroundAxis(
                    boundaryPoint,
                    boundaryTangent,
                    stretchedDistance);
                result.Normalize();
                return result;
            }

            private double SampleMountainTerrain(
                Vector3D direction,
                double tectonicScale,
                double thresholdBias)
            {
                Vector3D sampleDirection = WarpMountainDirection(
                    direction,
                    tectonicScale);

                double ridge = Clamp(MountainFractal.Sample(
                    sampleDirection.X,
                    sampleDirection.Y,
                    sampleDirection.Z), 0.0, 1.0);

                double massif = ToUnit(MountainMassifNoise.Sample(
                    sampleDirection.X,
                    sampleDirection.Y,
                    sampleDirection.Z));

                double threshold = Lerp(0.24, 0.43, 1.0 - massif) + thresholdBias;
                threshold = Clamp(threshold, 0.10, 0.56);

                double mountain = SmoothThreshold(ridge, threshold, 0.92);
                if (mountain <= 1e-6)
                    return 0.0;

                double peak = ToUnit(MountainPeakNoise.Sample(
                    sampleDirection.X,
                    sampleDirection.Y,
                    sampleDirection.Z));
                double peakModulation = 0.72 + 0.28 * Math.Pow(peak, 1.35);
                double massifHeight = Lerp(0.72, 1.08, massif);

                return Clamp(
                    mountain * peakModulation * massifHeight,
                    0.0,
                    1.15);
            }

            private Vector3D WarpMountainDirection(
                Vector3D direction,
                double tectonicScale)
            {
                Vector3D warp = new Vector3D(
                    MountainWarpX.Sample(direction.X, direction.Y, direction.Z),
                    MountainWarpY.Sample(direction.X, direction.Y, direction.Z),
                    MountainWarpZ.Sample(direction.X, direction.Y, direction.Z));

                warp -= direction * Vector3D.Dot(warp, direction);
                double lengthSquared = warp.LengthSquared();
                if (lengthSquared <= 1e-12)
                    return direction;

                warp /= Math.Sqrt(lengthSquared);

                double angle = Math.Min(
                    tectonicScale * 0.42,
                    4.5 * Math.PI / 180.0);
                double amount = Clamp(
                    Math.Abs(MountainWarpX.Sample(
                        direction.Z,
                        direction.X,
                        direction.Y)),
                    0.0,
                    1.0);

                Vector3D result = direction * Math.Cos(angle * amount) +
                    warp * Math.Sin(angle * amount);
                result.Normalize();
                return result;
            }

            private static double Smooth01(double value)
            {
                double t = Clamp(value, 0.0, 1.0);
                return t * t * (3.0 - 2.0 * t);
            }

            private static double SoftMask(
                double distance,
                double radius,
                double exponent)
            {
                if (radius <= 1e-12 || distance >= radius)
                    return 0.0;

                double t = Clamp(distance / radius, 0.0, 1.0);
                double smooth = t * t * (3.0 - 2.0 * t);
                return Math.Pow(1.0 - smooth, Math.Max(0.05, exponent));
            }

            private static double SmoothThreshold(
                double value,
                double threshold,
                double exponent)
            {
                if (value <= threshold)
                    return 0.0;

                double normalized = Clamp(
                    (value - threshold) / Math.Max(1e-6, 1.0 - threshold),
                    0.0,
                    1.0);
                double smooth = normalized * normalized * (3.0 - 2.0 * normalized);
                return Math.Pow(smooth, Math.Max(0.05, exponent));
            }

            private bool TryGetTripleJunction(
                Vector3D direction,
                int plateA,
                int plateB,
                int plateC,
                out Vector3D junctionPoint,
                out double junctionDistance)
            {
                junctionPoint = Vector3D.Zero;
                junctionDistance = double.MaxValue;

                Vector3D normalAB = BoundaryNormals[plateA * PlateCount + plateB];
                Vector3D normalAC = BoundaryNormals[plateA * PlateCount + plateC];

                Vector3D intersection = Vector3D.Cross(normalAB, normalAC);
                double intersectionLengthSquared = intersection.LengthSquared();
                if (intersectionLengthSquared <= 1e-12)
                    return false;

                intersection /= Math.Sqrt(intersectionLengthSquared);
                if (Vector3D.Dot(intersection, direction) < 0.0)
                    intersection = -intersection;

                double sharedDot = Vector3D.Dot(intersection, Plates[plateA].Center);
                const double visibilityTolerance = 1e-7;
                for (int plateIndex = 0; plateIndex < Plates.Length; plateIndex++)
                {
                    if (plateIndex == plateA ||
                        plateIndex == plateB ||
                        plateIndex == plateC)
                    {
                        continue;
                    }

                    if (Vector3D.Dot(intersection, Plates[plateIndex].Center) >
                        sharedDot + visibilityTolerance)
                    {
                        return false;
                    }
                }

                junctionPoint = intersection;
                junctionDistance = Math.Acos(Clamp(
                    Vector3D.Dot(direction, junctionPoint),
                    -1.0,
                    1.0));
                return true;
            }

            private double ComputeTectonicStrength(
                int firstPlate,
                int secondPlate,
                Vector3D direction)
            {
                TectonicPlate plateA = Plates[firstPlate];
                TectonicPlate plateB = Plates[secondPlate];

                Vector3D velocityA = Vector3D.Cross(plateA.AngularVelocity, direction);
                Vector3D velocityB = Vector3D.Cross(plateB.AngularVelocity, direction);
                double speedA = velocityA.Length();
                double speedB = velocityB.Length();
                if (speedA <= 1e-10 || speedB <= 1e-10)
                    return 0.0;

                double velocityDot = Vector3D.Dot(velocityA, velocityB) / (speedA * speedB);
                double angleStrength = Math.Acos(Clamp(velocityDot, -1.0, 1.0)) / Math.PI;
                if (angleStrength <= 1e-6)
                    return 0.0;

                Vector3D pairNormal = BoundaryNormals[firstPlate * PlateCount + secondPlate];
                Vector3D boundaryNormal = pairNormal -
                    direction * Vector3D.Dot(pairNormal, direction);
                double boundaryNormalLengthSquared = boundaryNormal.LengthSquared();
                if (boundaryNormalLengthSquared <= 1e-12)
                    return 0.0;
                boundaryNormal /= Math.Sqrt(boundaryNormalLengthSquared);

                double relativeConvergence = Vector3D.Dot(
                    velocityA - velocityB,
                    boundaryNormal);
                if (relativeConvergence <= 0.0)
                    return 0.0;

                double convergence = relativeConvergence / (speedA + speedB);
                convergence = Clamp(convergence, 0.0, 1.0);
                if (convergence <= 1e-6)
                    return 0.0;

                double tectonicStrength = angleStrength * Math.Sqrt(convergence);
                return Math.Pow(Clamp(tectonicStrength, 0.0, 1.0), 1.15);
            }

            private static double ToUnit(double value)
            {
                return Clamp((value + 1.0) * 0.5, 0.0, 1.0);
            }

            private static double Lerp(double from, double to, double amount)
            {
                return from + (to - from) * Clamp(amount, 0.0, 1.0);
            }

            private Vector3D WarpDirection(Vector3D direction)
            {
                double major = MajorNoise.Sample(direction.X, direction.Y, direction.Z);
                double minor = MinorNoise.Sample(direction.X, direction.Y, direction.Z);

                major = Clamp(major, -MajorCeiling, MajorCeiling);
                minor = Clamp(minor, -MinorCeiling, MinorCeiling);

                Vector3D warped = RotateAroundAxis(
                    direction,
                    MajorAxis,
                    major * MajorWarpRadians);
                warped = RotateAroundAxis(
                    warped,
                    MinorAxis,
                    minor * MinorWarpRadians);
                warped.Normalize();
                return warped;
            }

            private void FindNearestPlates(
                Vector3D direction,
                out int firstPlate,
                out int secondPlate,
                out int thirdPlate,
                out double firstDot,
                out double secondDot,
                out double thirdDot)
            {
                firstPlate = -1;
                secondPlate = -1;
                thirdPlate = -1;
                firstDot = -2.0;
                secondDot = -2.0;
                thirdDot = -2.0;

                for (int plateIndex = 0; plateIndex < Plates.Length; plateIndex++)
                {
                    double dot = Vector3D.Dot(direction, Plates[plateIndex].Center);
                    if (dot > firstDot)
                    {
                        thirdDot = secondDot;
                        thirdPlate = secondPlate;
                        secondDot = firstDot;
                        secondPlate = firstPlate;
                        firstDot = dot;
                        firstPlate = plateIndex;
                    }
                    else if (dot > secondDot)
                    {
                        thirdDot = secondDot;
                        thirdPlate = secondPlate;
                        secondDot = dot;
                        secondPlate = plateIndex;
                    }
                    else if (dot > thirdDot)
                    {
                        thirdDot = dot;
                        thirdPlate = plateIndex;
                    }
                }
            }
        }
    }
}
