using System;
using Generated;
using Sandbox.Game.Entities;
using ProceduralCubemapApi.Common.PlanetModification.World;

namespace ProceduralCubemapApi.Common.Api
{
    /// <summary>
    /// Root-level water/height conversion helper exposed through the intermod API.
    /// SE-Water stores its configured water level as a radius multiplier over
    /// MyPlanet.MinimumRadius. This provider converts that value to and from the
    /// planet cubemap heightmap's unsigned 16-bit altitude unit.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "ProceduralCubemapApi.Api",
        ClientName = "WaterUtil")]
    internal sealed partial class WaterUtil
    {
        /// <summary>
        /// Converts an SE-Water radius multiplier to a cubemap heightmap value
        /// for the requested planet. A planet id of 0 targets the nearest planet
        /// to the local player, matching GetModificationTemplate(0).
        /// </summary>
        [ApiMethod]
        private int WaterRadiusToHeightmapUnit(
            long planetEntityId,
            double waterRadiusMultiplier)
        {
            MyPlanet planet = ResolvePlanet(
                planetEntityId);

            double waterRadiusMeters =
                planet.MinimumRadius * waterRadiusMultiplier;

            return RadiusMetersToHeightmapUnit(
                planet,
                waterRadiusMeters);
        }


        /// <summary>
        /// Converts a cubemap heightmap value to the SE-Water radius multiplier
        /// for the requested planet. A planet id of 0 targets the nearest planet
        /// to the local player, matching GetModificationTemplate(0).
        /// </summary>
        [ApiMethod]
        private double HeightmapUnitToWaterRadius(
            long planetEntityId,
            int heightmapUnit)
        {
            MyPlanet planet = ResolvePlanet(
                planetEntityId);

            double radiusMeters =
                HeightmapUnitToRadiusMetersInternal(
                    planet,
                    heightmapUnit);

            return radiusMeters / planet.MinimumRadius;
        }


        /// <summary>
        /// Converts an absolute radius in meters from planet center to a cubemap
        /// heightmap value for the requested planet.
        /// </summary>
        [ApiMethod]
        private int RadiusMetersToHeightmapUnit(
            long planetEntityId,
            double radiusMeters)
        {
            return RadiusMetersToHeightmapUnit(
                ResolvePlanet(
                    planetEntityId),
                radiusMeters);
        }


        /// <summary>
        /// Converts a cubemap heightmap value to an absolute radius in meters
        /// from planet center for the requested planet.
        /// </summary>
        [ApiMethod]
        private double HeightmapUnitToRadiusMeters(
            long planetEntityId,
            int heightmapUnit)
        {
            return HeightmapUnitToRadiusMetersInternal(
                ResolvePlanet(
                    planetEntityId),
                heightmapUnit);
        }


        /// <summary>
        /// Returns the number of cubemap heightmap units represented by one
        /// SE-Water radius multiplier unit for the requested planet.
        /// </summary>
        [ApiMethod]
        private double HeightmapUnitsPerWaterRadiusUnit(
            long planetEntityId)
        {
            MyPlanet planet = ResolvePlanet(
                planetEntityId);

            return planet.MinimumRadius * ushort.MaxValue /
                GetTerrainRadiusRange(
                    planet);
        }


        /// <summary>
        /// Returns the number of meters represented by one cubemap heightmap
        /// unit for the requested planet.
        /// </summary>
        [ApiMethod]
        private double MetersPerHeightmapUnit(
            long planetEntityId)
        {
            return GetTerrainRadiusRange(
                ResolvePlanet(
                    planetEntityId)) /
                ushort.MaxValue;
        }


        private static MyPlanet ResolvePlanet(
            long planetEntityId)
        {
            MyPlanet planet =
                planetEntityId == 0
                    ? PlanetLocator.FindNearestToPlayer()
                    : PlanetLocator.FindByEntityId(
                        planetEntityId);

            if (planet == null)
            {
                throw new Exception(
                    planetEntityId == 0
                        ? "Could not find a planet near the local player."
                        : "Could not find planet entity " +
                            planetEntityId +
                            ".");
            }

            if (planet.MinimumRadius <= 0)
            {
                throw new Exception(
                    "Planet has an invalid minimum radius.");
            }

            if (planet.MaximumRadius <= planet.MinimumRadius)
            {
                throw new Exception(
                    "Planet has an invalid terrain radius range.");
            }

            return planet;
        }


        private static int RadiusMetersToHeightmapUnit(
            MyPlanet planet,
            double radiusMeters)
        {
            double normalized =
                (radiusMeters - planet.MinimumRadius) /
                GetTerrainRadiusRange(
                    planet);

            return ClampToHeightmapUnit(
                normalized * ushort.MaxValue);
        }


        private static double HeightmapUnitToRadiusMetersInternal(
            MyPlanet planet,
            int heightmapUnit)
        {
            double normalized =
                ClampHeightmapUnit(
                    heightmapUnit) /
                (double)ushort.MaxValue;

            return planet.MinimumRadius +
                normalized *
                GetTerrainRadiusRange(
                    planet);
        }


        private static double GetTerrainRadiusRange(
            MyPlanet planet)
        {
            return planet.MaximumRadius - planet.MinimumRadius;
        }


        private static int ClampToHeightmapUnit(
            double value)
        {
            if (double.IsNaN(value) ||
                value <= 0.0)
            {
                return 0;
            }

            if (value >= ushort.MaxValue)
                return ushort.MaxValue;

            return (int)Math.Round(value);
        }


        private static int ClampHeightmapUnit(
            int value)
        {
            if (value < 0)
                return 0;

            if (value > ushort.MaxValue)
                return ushort.MaxValue;

            return value;
        }
    }
}
