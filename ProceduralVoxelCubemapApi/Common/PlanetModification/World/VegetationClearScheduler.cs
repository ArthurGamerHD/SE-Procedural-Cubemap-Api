using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.World
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


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Scheduled vegetation clear around " +
                boxes.Count +
                " existing grid(s). EntityId=" +
                planet.EntityId +
                ".");
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


                int sectorsTouched =
                    ClearEnvironmentItemsInBoxes(
                        planet,
                        pending.Boxes);


                pending.Pass++;

                if (pending.Pass >=
                    PassDelays.Length)
                {
                    _pendingVegetationClears.RemoveAt(
                        i);

                    MyLog.Default.WriteLineAndConsole(
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
