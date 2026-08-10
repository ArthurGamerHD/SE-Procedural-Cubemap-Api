using System;
using System.Collections.Generic;
using Generated;
using VoxelCubemapApi.Server.PlanetModification;
using VoxelCubemapApi.Server.PlanetModification.Templates;

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
        private static readonly Version _apiVersion = new Version(0, 0, 6);
        private readonly PlanetModificationCoordinator _coordinator;
        public VoxelCubemapIntermodApiServer(PlanetModificationCoordinator modificationCoordinator)
        {
            _coordinator = modificationCoordinator;
        }


        /// <summary>
        /// Creates a mutable modification template for the requested planet and
        /// returns its nested delegate API.
        /// </summary>
        [ApiMethod(typeof(PlanetModificationTemplate))]
        public Dictionary<string, Delegate> GetModificationTemplate(long entityId)
        {
            return _coordinator.CreateModificationTemplateApi(entityId);
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
