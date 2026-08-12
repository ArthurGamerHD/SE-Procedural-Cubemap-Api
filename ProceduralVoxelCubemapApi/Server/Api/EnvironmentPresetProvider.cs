using Generated;
using System;
using VoxelCubemapApi.Server.PlanetModification.EnvironmentPresets;

namespace VoxelCubemapApi.Server.Api
{
    /// <summary>
    /// Enumerates vegetation/environment presets supplied by loaded planet
    /// generator definitions without exposing engine definition objects.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "VoxelCubemapApi.Api",
        ClientName = "EnvironmentPresetProvider")]
    internal sealed partial class EnvironmentPresetProvider
    {
        private readonly EnvironmentPresetCatalog _catalog;


        internal EnvironmentPresetProvider(
            EnvironmentPresetCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException("catalog");

            _catalog =
                catalog;
        }


        [ApiMethod]
        private string[] GetPresetNames()
        {
            return _catalog.GetPresetNames();
        }


        [ApiMethod]
        private bool HasPreset(string presetName)
        {
            return _catalog.Contains(presetName);
        }
    }
}
