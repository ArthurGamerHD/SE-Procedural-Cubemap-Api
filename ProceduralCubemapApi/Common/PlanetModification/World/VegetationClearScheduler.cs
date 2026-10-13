using System.Collections.Generic;
using System;
using Adk.Utils;
using ProceduralCubemapApi.Common.PlanetModification.Templates;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ProceduralCubemapApi.Common.PlanetModification.World
{
    internal sealed class VegetationClearScheduler
    {
        private sealed class PendingVegetationClear
        {
            public long PlanetEntityId;
            public List<BoundingBoxD> Boxes;
            public int Pass;
            public int TicksUntilNextPass;
        }


        private static readonly int[] PassDelays =
        {
            0,
            10,
            60,
            180
        };

        private readonly List<PendingVegetationClear> _pendingVegetationClears =
            new List<PendingVegetationClear>();


        internal void Clear()
        {
            _pendingVegetationClears.Clear();
        }


        internal void Schedule(
            MyPlanet planet)
        {
            if (planet == null ||
                planet.Closed ||
                planet.MarkedForClose)
            {
                return;
            }


            BoundingBoxD planetBounds =
                planet.PositionComp.WorldAABB;

            // MyPlanetEnvironmentComponent.UpdatePhysics() considers dynamic
            // clusters up to 1024 m outside the planet AABB. Use the same
            // tolerance so near-surface vehicles are included.
            planetBounds.Inflate(
                1024.0);


            List<BoundingBoxD> boxes =
                new List<BoundingBoxD>();


            foreach (IMyEntity entity in MyEntities.GetEntities())
            {
                MyCubeGrid grid =
                    entity as MyCubeGrid;

                if (grid == null ||
                    grid.Closed ||
                    grid.MarkedForClose ||
                    grid.IsStatic ||
                    grid.Physics == null)
                {
                    continue;
                }


                BoundingBoxD gridBounds =
                    grid.PositionComp.WorldAABB;

                if (!planetBounds.Intersects(
                    gridBounds))
                {
                    continue;
                }


                // Match MyPlanetSurfacePlacement.ClearVegetation(): the
                // encounter code uses a sphere whose radius is twice the
                // prefab bounding-box half-extents length, then converts it
                // to a world-space AABB for ClearEnvironmentItemsBlocking().
                double radius =
                    gridBounds.HalfExtents.Length() *
                    2.0;

                if (radius <= 0.0)
                    continue;


                Vector3D center =
                    gridBounds.Center;

                boxes.Add(
                    new BoundingBoxD(
                        center - radius,
                        center + radius));
            }


            if (boxes.Count == 0)
                return;


            PendingVegetationClear pending =
                new PendingVegetationClear
                {
                    PlanetEntityId =
                        planet.EntityId,
                    Boxes =
                        boxes,
                    Pass =
                        0,
                    TicksUntilNextPass =
                    PassDelays[0]
                };

            _pendingVegetationClears.Add(
                pending);


            LogHelper.Log(
                MyLogSeverity.Debug,
                "[RuntimePlanetGenerator] Scheduled vegetation clear around " +
                boxes.Count +
                " existing grid(s). EntityId=" +
                planet.EntityId +
                ".");
        }


        /// <summary>
        /// Clear spawned environment items along freshly baked line corridors.
        /// Uses short per-segment boxes rather than clearing the entire planet.
        /// Called after the storage swap on the simulation thread.
        /// </summary>
        internal void ScheduleLines(MyPlanet planet, List<LineOperation> lines)
        {
            if (planet == null || planet.Closed || planet.MarkedForClose || lines == null)
                return;

            var boxes = new List<BoundingBoxD>();
            Vector3D center = planet.PositionComp.GetPosition();
            foreach (LineOperation line in lines)
            {
                if (line == null || line.Geometry == null || line.Geometry.Length != 12)
                    continue;
                double[] g = line.Geometry;
                Vector3D a = new Vector3D(g[0], g[1], g[2]);
                Vector3D b = new Vector3D(g[3], g[4], g[5]);
                if (a.LengthSquared() < 1e-12 || b.LengthSquared() < 1e-12)
                    continue;
                a.Normalize(); b.Normalize();
                Vector3D ta = new Vector3D(g[6], g[7], g[8]);
                Vector3D tb = new Vector3D(g[9], g[10], g[11]);
                ta -= a * Vector3D.Dot(ta, a);
                tb -= b * Vector3D.Dot(tb, b);
                if (ta.LengthSquared() < 1e-12 || tb.LengthSquared() < 1e-12)
                    continue;
                ta.Normalize(); tb.Normalize();
                double arc = Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(a,b))));
                double tangent = arc * 0.5;
                int segments = Math.Max(1, Math.Min(512,
                    (int)Math.Ceiling(arc * line.RadiusMeters / 125.0)));
                // The extra margin accounts for trees with canopies outside
                // their roots, and a moderate amount of cut/fill terrain.
                double radius = line.WidthMeters * 0.5 + 75.0;
                Vector3D previous = Vector3D.Zero;
                for (int i = 0; i <= segments; ++i)
                {
                    double t = (double)i / segments, t2=t*t, t3=t2*t;
                    Vector3D n = a*(2*t3-3*t2+1) +
                        ta*tangent*(t3-2*t2+t) +
                        b*(-2*t3+3*t2) +
                        tb*tangent*(t3-t2);
                    if (n.LengthSquared() < 1e-12) continue;
                    n.Normalize();
                    Vector3D world = center + n * line.RadiusMeters;
                    world = planet.GetClosestSurfacePointGlobal(world);
                    if (i > 0)
                    {
                        Vector3D min = Vector3D.Min(previous, world) - radius;
                        Vector3D max = Vector3D.Max(previous, world) + radius;
                        boxes.Add(new BoundingBoxD(min, max));
                    }
                    previous = world;
                }
            }
            if (boxes.Count == 0) return;
            _pendingVegetationClears.Add(new PendingVegetationClear
            {
                PlanetEntityId = planet.EntityId,
                Boxes = boxes,
                Pass = 0,
                TicksUntilNextPass = 0
            });
            LogHelper.Log(
                MyLogSeverity.Debug,
                "[RuntimePlanetGenerator] Scheduled line vegetation clear: " +
                boxes.Count + " regions on planet " + planet.EntityId);
        }


        internal void Update()
        {
            for (int i =
                    _pendingVegetationClears.Count - 1;
                i >= 0;
                i--)
            {
                PendingVegetationClear pending =
                    _pendingVegetationClears[i];

                if (pending == null)
                {
                    _pendingVegetationClears.RemoveAt(
                        i);

                    continue;
                }


                if (pending.TicksUntilNextPass > 0)
                {
                    pending.TicksUntilNextPass--;

                    continue;
                }


                MyPlanet planet =
                    PlanetLocator.FindByEntityId(
                        pending.PlanetEntityId);

                if (planet == null ||
                    planet.Closed ||
                    planet.MarkedForClose)
                {
                    _pendingVegetationClears.RemoveAt(
                        i);

                    continue;
                }


                int sectorsTouched = 0;
                try
                {
                    sectorsTouched = ClearEnvironmentItemsInBoxes(
                        planet,
                        pending.Boxes);
                }
                catch (Exception e)
                {
                    // Vegetation cleanup must not take down the simulation.
                    // Advance the bounded pass schedule even on failure.
                    LogHelper.Log(MyLogSeverity.Warning,
                        "[RuntimePlanetGenerator] Vegetation clear pass failed for planet " +
                        pending.PlanetEntityId + "; some environment items may remain: " + e.Message);
                }


                pending.Pass++;

                if (pending.Pass >=
                    PassDelays.Length)
                {
                    _pendingVegetationClears.RemoveAt(
                        i);

                    LogHelper.LogInfo(
                        "[RuntimePlanetGenerator] Finished post-terraform vegetation clear. " +
                        "EntityId=" +
                        planet.EntityId +
                        ", lastPassSectors=" +
                        sectorsTouched +
                        ".");

                    continue;
                }


                pending.TicksUntilNextPass =
                    PassDelays[
                        pending.Pass];
            }
        }


        private static int ClearEnvironmentItemsInBoxes(
            MyPlanet planet,
            List<BoundingBoxD> boxes)
        {
            if (planet == null ||
                boxes == null ||
                boxes.Count == 0)
            {
                return 0;
            }


            int sectorsTouched =
                0;

            List<MyEntity> entities =
                new List<MyEntity>();


            for (int i = 0;
                i < boxes.Count;
                i++)
            {
                BoundingBoxD worldBox =
                    boxes[i];

                entities.Clear();

                planet.Hierarchy.QueryAABB(
                    ref worldBox,
                    entities);


                for (int j = 0;
                    j < entities.Count;
                    j++)
                {
                    MyEnvironmentSector sector =
                        entities[j] as MyEnvironmentSector;

                    if (sector == null ||
                        sector.Closed ||
                        sector.MarkedForClose)
                    {
                        continue;
                    }


                    if (sector.DataView == null)
                    {
                        sector.ForceLoadDataView();
                    }

                    if (sector.DataView == null)
                        continue;


                    BoundingBoxD clearBox =
                        boxes[i];

                    sector.DisableItemsInBox(
                        ref clearBox);

                    sectorsTouched++;
                }
            }


            return sectorsTouched;
        }


    }
}
