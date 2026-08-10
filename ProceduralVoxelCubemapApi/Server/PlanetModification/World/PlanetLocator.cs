using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.ModAPI;

using System;

using VRage.ModAPI;
using VRageMath;

namespace VoxelCubemapApi.Server.PlanetModification.World
{
    internal static class PlanetLocator
    {
        internal static MyPlanet FindByEntityId(
            long entityId)
        {
            foreach (IMyEntity entity in MyEntities.GetEntities())
            {
                MyPlanet planet =
                    entity as MyPlanet;

                if (planet != null &&
                    planet.EntityId == entityId)
                {
                    return planet;
                }
            }

            return null;
        }


        internal static MyPlanet FindNearestToPlayer()
        {
            if (MyAPIGateway.Session == null ||
                MyAPIGateway.Session.Player == null ||
                MyAPIGateway.Session.Player.Character == null)
            {
                return null;
            }


            Vector3D playerPosition =
                MyAPIGateway.Session.Player
                    .Character
                    .GetPosition();

            MyPlanet nearest =
                null;

            double nearestSurfaceDistance =
                double.MaxValue;


            foreach (IMyEntity entity in MyEntities.GetEntities())
            {
                MyPlanet planet =
                    entity as MyPlanet;


                if (planet == null ||
                    planet.Generator == null)
                {
                    continue;
                }


                MyPlanetInitArguments args =
                    planet.GetInitArguments;


                double centerDistance =
                    Vector3D.Distance(
                        playerPosition,
                        planet.PositionComp.GetPosition());


                double surfaceDistance =
                    Math.Abs(
                        centerDistance -
                        args.Radius);


                if (surfaceDistance <
                    nearestSurfaceDistance)
                {
                    nearestSurfaceDistance =
                        surfaceDistance;

                    nearest =
                        planet;
                }
            }


            return nearest;
        }
    }
}
