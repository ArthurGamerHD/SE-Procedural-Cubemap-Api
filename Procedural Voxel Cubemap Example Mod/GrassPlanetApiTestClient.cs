using System;
using Generated;
using Sandbox.ModAPI;
using VoxelCubemapApi.Api;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace VoxelCubemapExampleMod
{
    /// <summary>
    /// Test consumer for the public SendModMessage API. No planet-generation
    /// implementation is called directly from this component.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    internal sealed class GrassPlanetApiTestClient : MySessionComponentBase
    {
        private const long ReplyChannel =
            0x5643584150490002L;

        private static readonly Version _clientApiVersion =
            new Version(0, 0, 6);

        private static GrassPlanetApiTestClient _instance;

        private ApiProvider _api;


        public override void LoadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            _instance =
                this;

            RequestApi();
        }


        protected override void UnloadData()
        {
            _api =
                null;

            if (_instance == this)
            {
                _instance =
                    null;
            }
        }


        private void RequestApi()
        {
            try
            {
                ApiProvider api = VoxelCubemapApiClient.TryGet(ReplyChannel);

                if (api == null)
                {
                    _api =
                        null;

                    return;
                }

                Version serverVersion =
                    api.GetApiVersion();

                if (serverVersion == null ||
                    !_clientApiVersion.Equals(
                        serverVersion))
                {
                    throw new Exception(
                        "API version mismatch. Client=" +
                        _clientApiVersion +
                        ", server=" +
                        serverVersion +
                        ".");
                }

                _api =
                    api;
            }
            catch (Exception e)
            {
                LogWarning(
                    "API response binding failed",
                    e);
            }
        }


        /// <summary>
        /// Builds the grass modification entirely through the public API:
        /// nearest planet template, complex material, randomized biome fractal
        /// bands, caller-owned procedural vegetation definition, then push.
        /// </summary>
        [ChatCommand("testgrass", "vcma")]
        public static void ApplyGrassPlanet(
            int grassCoveragePercent = 100)
        {
            GrassPlanetApiTestClient client =
                _instance;

            if (client == null)
            {
                ShowMessage(
                    "Grass API client is not initialized.");

                return;
            }

            if (client._api == null)
            {
                client.RequestApi();

                if (client._api == null)
                {
                    ShowMessage(
                        "Voxel Cubemap API is not ready.");

                    return;
                }
            }


            ModificationTemplate template =
                null;

            try
            {
                template =
                    client._api.GetModificationTemplate(
                        0);

                if (template == null)
                {
                    throw new Exception(
                        "Could not create a template for the nearest planet.");
                }

                if (grassCoveragePercent < 0 ||
                    grassCoveragePercent > 100)
                {
                    throw new ArgumentException(
                        "Grass coverage must be from 0 to 100.",
                        "grassCoveragePercent");
                }


                MaterialRulesContent grassRules =
                    MaterialRulesContent.Load(
                        (MyModContext)client.ModContext,
                        "Data/grassrules.xml",
                        "TerraformGrassRules",
                        "TerraformGrassSurface");

                byte grassMaterialMapValue =
                    template.AddComplexMaterial(
                        grassRules.MaterialGroup);

                template.ApplyFractalNoise(
                    grassMaterialMapValue,
                    grassCoveragePercent);

                byte[] forestBiomes =
                {
                    113,
                    141,
                    198
                };

                long planetSeed =
                    template.GetPlanetSeed();

                var random =
                    new Random(
                        unchecked(
                            (int)(planetSeed ^
                                (planetSeed >> 32))));

                for (int index = forestBiomes.Length - 1;
                    index > 0;
                    index--)
                {
                    int swapIndex =
                        random.Next(
                            index + 1);

                    byte swap =
                        forestBiomes[index];

                    forestBiomes[index] =
                        forestBiomes[swapIndex];

                    forestBiomes[swapIndex] =
                        swap;
                }

                int middleCoverage =
                    grassCoveragePercent > 1
                        ? random.Next(
                            1,
                            grassCoveragePercent)
                        : 0;

                int innerCoverage =
                    middleCoverage > 1
                        ? random.Next(
                            1,
                            middleCoverage)
                        : 0;

                template.ApplyBiomeFractalNoise(
                    forestBiomes[0],
                    grassCoveragePercent);

                template.ApplyBiomeFractalNoise(
                    forestBiomes[1],
                    middleCoverage);

                template.ApplyBiomeFractalNoise(
                    forestBiomes[2],
                    innerCoverage);

                template.SetEnvironmentDefinition(
                    "VoxelCubemapGrassEnvironmentCarrier");

                MyLog.Default.WriteLineAndConsole(
                    "[Grass API Test Client] Random biome fractals: " +
                    forestBiomes[0] +
                    "=" +
                    grassCoveragePercent +
                    "%, " +
                    forestBiomes[1] +
                    "=" +
                    middleCoverage +
                    "%, " +
                    forestBiomes[2] +
                    "=" +
                    innerCoverage +
                    "%, planet seed=" +
                    planetSeed +
                    ".");


                ModificationTemplate pushedTemplate =
                    template;

                template.Push(
                    delegate(
                        bool success,
                        string message)
                    {
                        pushedTemplate.Close();

                        ShowMessage(
                            string.IsNullOrWhiteSpace(message)
                                ? success
                                    ? "Grass modification committed."
                                    : "Grass modification failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning(
                    "Grass modification failed",
                    e);

                ShowMessage(
                    "Failed: " +
                    e.Message);
            }
        }


        private static void LogWarning(
            string message,
            Exception e)
        {
            MyLog.Default.WriteLineAndConsole(
                "[Grass API Test Client] " +
                message +
                ": " +
                e);
        }


        private static void ShowMessage(
            string message)
        {
            MyAPIGateway.Utilities.ShowMessage(
                "Procedural Voxel Cubemap Api",
                message);
        }
    }
}
