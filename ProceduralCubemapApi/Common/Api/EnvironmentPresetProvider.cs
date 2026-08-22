using System;
using Generated;
using ProceduralCubemapApi.Common.PlanetModification.EnvironmentPresets;

namespace ProceduralCubemapApi.Common.Api
{
    /// <summary>
    /// Enumerates vegetation/environment presets supplied by loaded planet
    /// generator definitions without exposing engine definition objects.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "ProceduralCubemapApi.Api",
        ClientName = "EnvironmentPresetProvider")]
    internal sealed partial class EnvironmentPresetProvider
    {
        private readonly EnvironmentPresetCatalog _catalog;


        internal EnvironmentPresetProvider(
            EnvironmentPresetCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

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
