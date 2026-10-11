using System;
using System.Collections.Generic;
using Adk.Image.Png;
using Sandbox.ModAPI;
using ProceduralCubemapApi.Common.Noise.fBm;
using ProceduralCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace ProceduralCubemapApi.Common.PlanetModification.Maps
{
    // Paint an absolute terrain corridor on the generated cubemap. Both network
    // replay and disk reconstruction use the compact DrawLine recipe.
    internal static class SphericalLineRasterizer
    {
        private static readonly string[] Faces = { "front", "back", "left", "right", "up", "down" };

        internal const int CellSize = 64;

        internal static void Apply(Dictionary<string, PlanarPngBitmap> heights,
            PlanetModificationSnapshot snapshot, Func<string, PlanarPngBitmap> load)
        {
            ApplyCells(heights, snapshot, load);
        }

        internal static bool[] ApplyCells(Dictionary<string, PlanarPngBitmap> heights,
            PlanetModificationSnapshot snapshot, Func<string, PlanarPngBitmap> load)
        {
            var surface = new CellSurface(heights);
            foreach (LineOperation line in snapshot.LineOperations)
                surface.Add(Prepare(heights, line, snapshot, surface.ReadHeight));

            var changedFaces = new bool[6];
            var materials = new PlanarPngBitmap[6];
            var active = new List<Cell>();
            foreach (Cell cell in surface.Cells)
            {
                if (cell.Lines.Count == 0) continue;
                changedFaces[cell.Face] = true;
                active.Add(cell);
            }
            for (int face = 0; face < 6; face++)
                if (changedFaces[face])
                {
                    materials[face] = load(Faces[face] + "_mat.png");
                    PlanarPngBitmap h = heights[Faces[face] + ".png"];
                    if (materials[face].Width != h.Width || materials[face].Height != h.Height)
                        throw new InvalidOperationException("DrawLine: height/material resolution mismatch.");
                }

            var failures = new Exception[active.Count];
            Action<int> paint = index =>
            {
                try { active[index].Paint(materials[active[index].Face]); }
                catch (Exception error) { failures[index] = error; }
            };
            if (MyAPIGateway.Parallel == null)
                for (int i = 0; i < active.Count; i++) paint(i);
            else
                MyAPIGateway.Parallel.For(0, active.Count, paint);
            foreach (Exception error in failures)
                if (error != null)
                    throw new InvalidOperationException("DrawLine: cell rasterization failed.", error);
            return changedFaces;
        }

        private sealed class CellSurface
        {
            internal readonly List<Cell> Cells = new List<Cell>();
            private readonly Cell[][] _faces = new Cell[6][];
            private readonly int[] _columns = new int[6];
            private readonly Dictionary<long, int> _samples = new Dictionary<long, int>();

            internal CellSurface(Dictionary<string, PlanarPngBitmap> heights)
            {
                for (int face = 0; face < 6; face++)
                {
                    PlanarPngBitmap image = heights[Faces[face] + ".png"];
                    int columns = (image.Width + CellSize - 1) / CellSize;
                    int rows = (image.Height + CellSize - 1) / CellSize;
                    _columns[face] = columns;
                    _faces[face] = new Cell[columns * rows];
                    for (int y = 0; y < rows; y++)
                    for (int x = 0; x < columns; x++)
                    {
                        var cell = new Cell(face, image, x * CellSize, y * CellSize);
                        _faces[face][y * columns + x] = cell;
                        Cells.Add(cell);
                    }
                }
            }

            internal void Add(PreparedLine line)
            {
                foreach (Cell cell in Cells)
                {
                    CellLine local = line.Intersect(cell);
                    if (local != null) cell.Lines.Add(local);
                }
                _samples.Clear();
            }

            internal int ReadHeight(int face, int x, int y)
            {
                Cell cell = _faces[face][y / CellSize * _columns[face] + x / CellSize];
                int offset = y * cell.Height.Width + x;
                long key = ((long)face << 32) | (uint)offset;
                int cached;
                if (_samples.TryGetValue(key, out cached)) return cached;
                int height = (cell.Height.Planes[0][offset] << 8) | cell.Height.Planes[1][offset];
                if (cell.Lines.Count == 0) return height;
                Vector3D direction = FractalBrownianMotion.GetCubemapSphereDirection(
                    face, x, y, cell.Height.Width, cell.Height.Height);
                foreach (CellLine line in cell.Lines)
                {
                    bool road;
                    height = line.Line.Evaluate(direction, height, line.Segments, out road);
                }
                _samples[key] = height;
                return height;
            }
        }

        private sealed class Cell
        {
            internal readonly int Face, X, Y, EndX, EndY;
            internal readonly PlanarPngBitmap Height;
            internal readonly Vector3D Center;
            internal readonly double AngularRadius;
            internal readonly List<CellLine> Lines = new List<CellLine>();

            internal Cell(int face, PlanarPngBitmap height, int x, int y)
            {
                Face = face; Height = height; X = x; Y = y;
                EndX = Math.Min(height.Width, x + CellSize);
                EndY = Math.Min(height.Height, y + CellSize);
                int cx = (X + EndX - 1) / 2, cy = (Y + EndY - 1) / 2;
                Center = FractalBrownianMotion.GetCubemapSphereDirection(
                    face, cx, cy, height.Width, height.Height);
                double du = height.Width <= 1 ? 0 :
                    2.0 * Math.Max(cx - X, EndX - 1 - cx) / (height.Width - 1);
                double dv = height.Height <= 1 ? 0 :
                    2.0 * Math.Max(cy - Y, EndY - 1 - cy) / (height.Height - 1);
                AngularRadius = Math.Min(Math.PI, Math.Sqrt(du * du + dv * dv) + 1e-10);
            }

            internal void Paint(PlanarPngBitmap material)
            {
                for (int y = Y; y < EndY; y++)
                for (int x = X; x < EndX; x++)
                {
                    int offset = y * Height.Width + x;
                    int value = (Height.Planes[0][offset] << 8) | Height.Planes[1][offset];
                    Vector3D n = FractalBrownianMotion.GetCubemapSphereDirection(
                        Face, x, y, Height.Width, Height.Height);
                    foreach (CellLine local in Lines)
                    {
                        PreparedLine line = local.Line;
                        bool road;
                        value = line.Evaluate(n, value, local.Segments, out road);
                        if (road) material.Planes[line.Layer][offset] = (byte)line.Operation.Material;
                    }
                    Height.Planes[0][offset] = (byte)(value >> 8);
                    Height.Planes[1][offset] = (byte)value;
                }
            }
        }

        private sealed class CellLine
        {
            internal PreparedLine Line;
            internal int[] Segments;
        }

        private sealed class PreparedLine
        {
            internal LineOperation Operation;
            internal Vector3D[] Points;
            internal double[] Distances, Profile;
            internal double MetersPerUnit, HalfWidth, Slope, CapAngle, CapDot, Reach;
            internal Vector3D CapCenter;
            internal int Layer;
            internal Vector3D[] SegmentDeltas;
            internal double[] SegmentLengthsSquared, SegmentAngles, SegmentPruneDots;

            internal CellLine Intersect(Cell cell)
            {
                double angle = Math.Min(Math.PI, CapAngle + cell.AngularRadius);
                if (Vector3D.Dot(CapCenter, cell.Center) < Math.Cos(angle) - 1e-12)
                    return null;
                var segments = new List<int>();
                for (int i = 0; i < Points.Length - 1; i++)
                {
                    double segmentAngle = SegmentAngles[i];
                    angle = Math.Min(Math.PI, Reach + segmentAngle + cell.AngularRadius + 1e-10);
                    if (Vector3D.Dot(Points[i], cell.Center) >= Math.Cos(angle) - 1e-12)
                        segments.Add(i);
                }
                return segments.Count == 0 ? null : new CellLine { Line = this, Segments = segments.ToArray() };
            }

            internal int Evaluate(Vector3D n, int old, int[] segments, out bool road)
            {
                road = false;
                if (Vector3D.Dot(n, CapCenter) < CapDot) return old;
                double best = double.MaxValue, desired = 0;
                for (int candidate = 0; candidate < segments.Length; candidate++)
                {
                    int i = segments[candidate];
                    double approx = Vector3D.Dot(n, Points[i]);
                    if (approx < SegmentPruneDots[i])
                        continue;
                    Vector3D ab = SegmentDeltas[i];
                    double t = Math.Max(0, Math.Min(1, Vector3D.Dot(n - Points[i], ab) / SegmentLengthsSquared[i]));
                    Vector3D nearest = Normalize(Points[i] + ab * t);
                    double chord = (n - nearest).Length();
                    double meters = 2 * Operation.RadiusMeters * Math.Asin(Math.Min(1, chord * 0.5));
                    if (meters < best)
                    {
                        best = meters;
                        desired = Profile[i] * (1 - t) + Profile[i + 1] * t;
                    }
                }
                if (best == double.MaxValue) return old;
                double target = old;
                if (best <= HalfWidth)
                {
                    target = desired;
                    road = true;
                }
                else if (Slope > 0)
                {
                    double deltaUnits = (best - HalfWidth) * Slope / MetersPerUnit;
                    if (desired > old) target = Math.Max(old, desired - deltaUnits);
                    else target = Math.Min(old, desired + deltaUnits);
                }
                if (target < 0 || target > 65535)
                    throw new InvalidOperationException("DrawLine: embankment exceeds height encoding.");
                return (int)Math.Round(target);
            }
        }

        private static Vector3D Normalize(Vector3D v)
        {
            if (v.LengthSquared() < 1e-16)
                throw new InvalidOperationException("DrawLine: degenerate vector.");
            v.Normalize();
            return v;
        }

        private static Vector3D Read(double[] g, int i)
        {
            return new Vector3D(g[i], g[i + 1], g[i + 2]);
        }

        // Inverse of FractalBrownianMotion.GetCubemapSphereDirection.
        private static int SampleHeight(Dictionary<string, PlanarPngBitmap> images, Vector3D n,
            Func<int, int, int, int> readHeight)
        {
            double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
            int face;
            double u, v, scale;
            if (az >= ax && az >= ay)
            {
                scale = az;
                face = n.Z < 0 ? 0 : 1;
                u = n.Z < 0 ? -n.X / scale : n.X / scale;
                v = -n.Y / scale;
            }
            else if (ax >= ay)
            {
                scale = ax;
                face = n.X > 0 ? 2 : 3;
                u = n.X > 0 ? -n.Z / scale : n.Z / scale;
                v = -n.Y / scale;
            }
            else
            {
                scale = ay;
                face = n.Y > 0 ? 4 : 5;
                u = n.Y > 0 ? -n.X / scale : n.X / scale;
                v = -n.Z / scale;
            }
            PlanarPngBitmap img = images[Faces[face] + ".png"];
            double px = Math.Max(0, Math.Min(img.Width - 1, (u + 1) * 0.5 * (img.Width - 1)));
            double py = Math.Max(0, Math.Min(img.Height - 1, (v + 1) * 0.5 * (img.Height - 1)));
            int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
            int x1 = Math.Min(img.Width - 1, x0 + 1);
            int y1 = Math.Min(img.Height - 1, y0 + 1);
            double fx = px - x0, fy = py - y0;
            double h00 = readHeight(face, x0, y0), h10 = readHeight(face, x1, y0);
            double h01 = readHeight(face, x0, y1), h11 = readHeight(face, x1, y1);
            return (int)Math.Round(
                ((h00 * (1 - fx) + h10 * fx) * (1 - fy)) +
                ((h01 * (1 - fx) + h11 * fx) * fy));
        }

        private static PreparedLine Prepare(Dictionary<string, PlanarPngBitmap> heights,
            LineOperation op, PlanetModificationSnapshot snapshot, Func<int, int, int, int> readHeight)
        {
            Vector3D a = Normalize(Read(op.Geometry, 0)), b = Normalize(Read(op.Geometry, 3));
            Vector3D da = Read(op.Geometry, 6), db = Read(op.Geometry, 9);
            da = Normalize(da - a * Vector3D.Dot(da, a));
            db = Normalize(db - b * Vector3D.Dot(db, b));
            double arc = Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(a, b))));
            if (arc < 1e-7 || arc > Math.PI - 1e-5)
                throw new InvalidOperationException("DrawLine: coincident or antipodal endpoints.");
            int resolution = heights["front.png"].Width;
            int count = Math.Max(16, Math.Min(4096,
                Math.Max((int)Math.Ceiling(arc * resolution * 2.0),
                    (int)Math.Ceiling(arc * op.RadiusMeters / 20.0))));
            Vector3D[] points = new Vector3D[count + 1];
            double[] distances = new double[count + 1];
            double[] original = new double[count + 1];
            double tangentMagnitude = arc * 0.5;
            for (int i = 0; i <= count; i++)
            {
                double t = (double)i / count, t2 = t * t, t3 = t2 * t;
                Vector3D p = a * (2*t3 - 3*t2 + 1) +
                    da * tangentMagnitude * (t3 - 2*t2 + t) +
                    b * (-2*t3 + 3*t2) +
                    db * tangentMagnitude * (t3 - t2);
                points[i] = Normalize(p);
                if (i != 0)
                {
                    distances[i] = distances[i - 1] + op.RadiusMeters *
                        Math.Acos(Math.Max(-1, Math.Min(1,
                            Vector3D.Dot(points[i - 1], points[i]))));
                }
                original[i] = SampleHeight(heights, points[i], readHeight);
            }
            double total = distances[count];
            if (total <= 0) throw new InvalidOperationException("DrawLine: zero path length.");
            double heightRange = op.HeightRangeMeters > 0.0
                ? op.HeightRangeMeters
                : snapshot.TargetPlanet == null ? 0.0 :
                    snapshot.TargetPlanet.MaximumRadius - snapshot.TargetPlanet.MinimumRadius;
            double metersPerUnit = heightRange / 65535.0;
            if (metersPerUnit <= 0)
                throw new InvalidOperationException("DrawLine: invalid planet height range.");
            double maxGradient = Math.Tan(op.MaximumSlopeDegrees * Math.PI / 180.0);
            double maxUnitsPerMeter = maxGradient / metersPerUnit;
            double[] gradeSteps = new double[count + 1];
            double permittedChange = 0.0;
            for (int i = 1; i <= count; i++)
            {
                double middle = (distances[i - 1] + distances[i]) * 0.5;
                double ramp = op.SegmentDistanceMeters > 0.0
                    ? Math.Min(1.0, Math.Min(middle, total - middle) /
                        op.SegmentDistanceMeters)
                    : 1.0;
                gradeSteps[i] = (distances[i] - distances[i - 1]) *
                    maxUnitsPerMeter * ramp;
                permittedChange += gradeSteps[i];
            }
            if (Math.Abs(original[count] - original[0]) >
                permittedChange + 0.01)
                throw new InvalidOperationException(
                    "DrawLine: endpoint elevations exceed maximumSlope/segmentDistance.");
            double sum = 0;
            for (int i = 0; i < count; i++)
                sum += (original[i] + original[i+1]) * 0.5 *
                    (distances[i+1] - distances[i]);
            double avg = sum / total;
            double[] profile = new double[count + 1];
            profile[0] = original[0];
            profile[count] = original[count];
            for (int i = 1; i < count; i++)
            {
                double lo = Math.Max(original[0] - maxUnitsPerMeter * distances[i],
                    original[count] - maxUnitsPerMeter * (total - distances[i]));
                double hi = Math.Min(original[0] + maxUnitsPerMeter * distances[i],
                    original[count] + maxUnitsPerMeter * (total - distances[i]));
                double preferred = op.AverageHeight ? avg : original[i];
                profile[i] = Math.Max(lo, Math.Min(hi, preferred));
            }
            // Project the sampled terrain profile onto the maximum-grade
            // constraint. Optional longitudinal smoothing rounds abrupt
            // climb-to-flat transitions; the second projection preserves the
            // grade limit and both fixed endpoint elevations.
            ConstrainGrade(profile, gradeSteps);
            if (op.SegmentDistanceMeters > 0.0)
            {
                SmoothGradeTransitions(profile, distances, op.SegmentDistanceMeters);
                ConstrainGrade(profile, gradeSteps);
            }
            foreach (double height in profile)
                if (height < 0 || height > 65535)
                    throw new InvalidOperationException("DrawLine: elevation cannot be encoded.");

            double halfWidth = op.WidthMeters * 0.5;
            halfWidth = Math.Max(halfWidth,
                Math.Sqrt(2.0) * op.RadiusMeters / resolution * 1.1);
            double angle = op.FillAngleDegrees * Math.PI / 180.0;
            double slope = Math.Tan(angle);
            Vector3D capCenter = points[count / 2];
            double capAngle = 0.0;
            foreach (Vector3D point in points)
                capAngle = Math.Max(capAngle, Math.Acos(Math.Max(-1.0,
                    Math.Min(1.0, Vector3D.Dot(capCenter, point)))));
            double maximumEarthworkWidth = slope > 0.0
                ? 65535.0 * metersPerUnit / slope : 0.0;
            capAngle = Math.Min(Math.PI, capAngle +
                (halfWidth + maximumEarthworkWidth) / op.RadiusMeters + 0.01);
            double capDot = Math.Cos(capAngle);

            var prepared = new PreparedLine
            {
                Operation = op, Points = points, Distances = distances, Profile = profile,
                MetersPerUnit = metersPerUnit, HalfWidth = halfWidth, Slope = slope,
                CapCenter = capCenter, CapAngle = capAngle, CapDot = capDot,
                Reach = Math.Min(Math.PI, (halfWidth + maximumEarthworkWidth) / op.RadiusMeters),
                Layer = op.Layer.Equals("Material", StringComparison.OrdinalIgnoreCase) ? 0 :
                    op.Layer.Equals("Biome", StringComparison.OrdinalIgnoreCase) ? 1 : 2
            };
            prepared.SegmentDeltas = new Vector3D[count];
            prepared.SegmentLengthsSquared = new double[count];
            prepared.SegmentAngles = new double[count];
            prepared.SegmentPruneDots = new double[count];
            for (int i = 0; i < count; i++)
            {
                prepared.SegmentDeltas[i] = points[i + 1] - points[i];
                prepared.SegmentLengthsSquared[i] = prepared.SegmentDeltas[i].LengthSquared();
                prepared.SegmentAngles[i] = (distances[i + 1] - distances[i]) / op.RadiusMeters;
                prepared.SegmentPruneDots[i] = Math.Cos(Math.Min(Math.PI,
                    halfWidth / op.RadiusMeters + prepared.SegmentAngles[i] + 0.7));
            }
            return prepared;
        }

        private static void ConstrainGrade(double[] profile, double[] gradeSteps)
        {
            int count = profile.Length - 1;
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < count; i++)
                {
                    double step = gradeSteps[i];
                    profile[i] = Math.Max(profile[i - 1] - step,
                        Math.Min(profile[i - 1] + step, profile[i]));
                }
                for (int i = count - 1; i > 0; i--)
                {
                    double step = gradeSteps[i + 1];
                    profile[i] = Math.Max(profile[i + 1] - step,
                        Math.Min(profile[i + 1] + step, profile[i]));
                }
            }
        }

        private static void SmoothGradeTransitions(double[] profile,
            double[] distances, double segmentDistanceMeters)
        {
            int count = profile.Length - 1;
            double averageStep = distances[count] / count;
            int radius = Math.Max(1, Math.Min(count,
                (int)Math.Ceiling(segmentDistanceMeters / averageStep)));
            double[] source = (double[])profile.Clone();
            double fullWeight = (radius + 1.0) * (radius + 1.0);
            for (int i = 1; i < count; i++)
            {
                double weighted = 0.0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sample = Math.Max(0, Math.Min(count, i + offset));
                    weighted += source[sample] * (radius + 1 - Math.Abs(offset));
                }
                profile[i] = weighted / fullWeight;
            }
        }
    }
}
