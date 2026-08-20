using System;
using System.Collections.Generic;
using Generated;
using Sandbox.ModAPI;
using VoxelCubemapApi.Server.PlanetModification;
using VoxelCubemapApi.Server.PlanetModification.Templates;
using VoxelCubemapApi.Server.PlanetModification.EnvironmentPresets;

namespace VoxelCubemapApi.Server.Api
{
    /// <summary>
    /// Defines the root API published by the session-level API manager.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "VoxelCubemapApi.Api",
        ClientName = "ApiProvider")]
    internal partial class VoxelCubemapIntermodApiServer
    {
        private static readonly Version _apiVersion = new Version(0, 0, 9);
        private readonly PlanetModificationCoordinator _coordinator;
        private readonly ProceduralNoiseProvider _noiseProvider;
        private readonly WaterUtil _waterUtil;
        private readonly EnvironmentPresetProvider _environmentPresetProvider;


        public VoxelCubemapIntermodApiServer(
            PlanetModificationCoordinator modificationCoordinator,
            EnvironmentPresetCatalog environmentPresetCatalog)
        {
            if (environmentPresetCatalog == null)
                throw new ArgumentNullException("environmentPresetCatalog");

            _coordinator = modificationCoordinator;
            _noiseProvider = new ProceduralNoiseProvider();
            _waterUtil = new WaterUtil();
            _environmentPresetProvider =
                new EnvironmentPresetProvider(
                    environmentPresetCatalog);
        }


        /// <summary>
        /// Creates a mutable modification template for the requested planet and
        /// returns its nested delegate API.
        ///
        /// This template is thread-safe and can be used to modify the planet's procedural generation parameters.
        /// Recommended to use this method in a background <seealso cref="VRage.Game.ModAPI.IMyParallelTask"/>
        /// to avoid blocking the main game thread.
        /// </summary>
        [ApiMethod(typeof(PlanetModificationTemplate))]
        public Dictionary<string, Delegate> GetModificationTemplate(long entityId)
        {
            return _coordinator.CreateModificationTemplateApi(entityId);
        }

        /// <summary>
        /// Creates a read-only metadata for the requested planet, returns Null if no matching planet found
        /// returns its nested delegate API.
        /// </summary>
        [ApiMethod(typeof(PlanetMetadataProvider))]
        public Dictionary<string, Delegate> GetPlanetMetadata(long entityId, bool includedVanilla = false)
        {
            return _coordinator.GetOrCreatePlanetMetadataProvider(
                entityId,
                includedVanilla);
        }


        /// <summary>
        /// Returns one detailed, human-readable record for every planet
        /// currently managed by the API. An empty array means that no runtime
        /// planet packages are registered.
        /// </summary>
        [ApiMethod]
        public string[] GetApiPlanetDetails()
        {
            return _coordinator.GetApiPlanetDetails();
        }

        /// <summary>
        /// Returns the root-level procedural noise provider.
        /// </summary>
        [ApiMethod(typeof(ProceduralNoiseProvider))]
        public Dictionary<string, Delegate> GetNoiseProvider()
        {
            return _noiseProvider.GetApi();
        }




        /// <summary>
        /// Returns the root-level water/height conversion utility provider.
        /// </summary>
        [ApiMethod(typeof(WaterUtil))]
        public Dictionary<string, Delegate> GetWaterUtil()
        {
            return _waterUtil.GetApi();
        }


        /// <summary>
        /// Returns the catalog of vegetation/environment presets discovered
        /// from all loaded compatible planet definitions.
        /// </summary>
        [ApiMethod(typeof(EnvironmentPresetProvider))]
        public Dictionary<string, Delegate> GetEnvironmentPresets()
        {
            return _environmentPresetProvider.GetApi();
        }


        /// <summary>
        /// Returns the version implemented by the server's root API.
        /// </summary>
        [ApiMethod]
        private static Version GetApiVersion()
        {
            return _apiVersion;
        }
    }
}
