using System;
using System.Collections.Generic;
using Generated;
using ProceduralCubemapApi.Common.PlanetModification;
using ProceduralCubemapApi.Common.PlanetModification.EnvironmentPresets;
using ProceduralCubemapApi.Common.PlanetModification.Templates;
using VRageMath;

namespace ProceduralCubemapApi.Common.Api
{
    /// <summary>
    /// Defines the root API published by the session-level API manager.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "ProceduralCubemapApi.Api",
        ClientName = "ApiProvider")]
    internal partial class CubemapIntermodApiServer
    {
        private static readonly Version ApiVersion = new Version(0, 0, 17);
        private readonly PlanetModificationCoordinator _coordinator;
        private readonly ProceduralNoiseProvider _noiseProvider;
        private readonly WaterUtil _waterUtil;
        private readonly EnvironmentPresetProvider _environmentPresetProvider;


        public CubemapIntermodApiServer(
            PlanetModificationCoordinator modificationCoordinator,
            EnvironmentPresetCatalog environmentPresetCatalog)
        {
            if (environmentPresetCatalog == null)
                throw new ArgumentNullException(nameof(environmentPresetCatalog));

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
        /// Creates a deferred planet template centered at the requested world
        /// position. The planet is not spawned until Push commits the final
        /// runtime generator, so the live planet is initialized only once.
        /// </summary>
        [ApiMethod(typeof(PlanetModificationTemplate))]
        public Dictionary<string, Delegate> CreatePlanetAtCenter(
            string generatorSubtype,
            float diameter,
            int seed,
            Vector3D center)
        {
            return _coordinator.CreatePlanetTemplateApi(
                generatorSubtype,
                diameter,
                seed,
                center);
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

        /// <summary>Returns the committed procedural recipe XML, or null for a PNG-backed planet.</summary>
        [ApiMethod]
        public string GetPlanetRecipeXml(long entityId)
        {
            return _coordinator.GetPlanetRecipeXml(entityId);
        }

        /// <summary>
        /// Returns the planet's committed modifications in replay order, with
        /// stable ids and every road line's parameters, as XML. Null for a
        /// PNG-backed planet (no procedural history).
        /// </summary>
        [ApiMethod]
        public string GetPlanetModificationsXml(long entityId)
        {
            return _coordinator.GetPlanetModificationsXml(entityId);
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
            return ApiVersion;
        }
    }
}
