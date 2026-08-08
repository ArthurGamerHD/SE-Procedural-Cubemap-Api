#if DEBUG

using System;
using Generated;
using Sandbox.ModAPI;
using VoxelCubemapApi.Api;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace VoxelCubemapApi.Client
{
    /// <summary>
    /// Debug consumer for the public SendModMessage API. No planet-generation
    /// implementation is called directly from this component.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    internal sealed class DebugGrassPlanetClient : MySessionComponentBase
    {
        private const long ReplyChannel =
            0x5643584150490002L;

        private static DebugGrassPlanetClient m_instance;

        private VoxelCubemapApiClient m_api;


        public override void LoadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            m_instance =
                this;

            m_api =
                new VoxelCubemapApiClient(
                    ReplyChannel);

            m_api.Init();
        }


        protected override void UnloadData()
        {
            if (m_api != null)
            {
                m_api.Close();

                m_api =
                    null;
            }

            if (m_instance == this)
            {
                m_instance =
                    null;
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
            DebugGrassPlanetClient client =
                m_instance;

            if (client == null ||
                client.m_api == null)
            {
                ShowMessage(
                    "Grass API client is not initialized.");

                return;
            }

            if (!client.m_api.IsReady)
            {
                client.m_api.RequestApi();

                ShowMessage(
                    "Voxel Cubemap API is not ready; requested it again.");

                return;
            }


            VoxelCubemapApiClient.ModificationTemplate template =
                null;

            try
            {
                template =
                    client.m_api.GetModificationTemplate(
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
                    template.PlanetSeed;

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

                // The randomized nested bands share the grass mask's seamless
                // noise field, so every generated forest biome stays on grass.
                template.ApplyBiomeFractalNoise(
                    forestBiomes[0],
                    grassCoveragePercent);

                template.ApplyBiomeFractalNoise(
                    forestBiomes[1],
                    middleCoverage);

                template.ApplyBiomeFractalNoise(
                    forestBiomes[2],
                    innerCoverage);

                // This carrier is declared in Data/grass-environment.sbc. Keen
                // loads and postprocesses the procedural environment normally at
                // session start; the API only passes the whitelisted carrier id.
                template.SetEnvironmentDefinition(
                    "VoxelCubemapGrassEnvironmentCarrier");

                MyLog.Default.WriteLineAndConsole(
                    "[Debug Grass API Client] Random biome fractals: " +
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


                VoxelCubemapApiClient.ModificationTemplate pushedTemplate =
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

                MyLog.Default.WriteLineAndConsole(
                    "[Debug Grass API Client] " +
                    e);

                ShowMessage(
                    "Failed: " +
                    e.Message);
            }
        }


        private static void ShowMessage(
            string message)
        {
            MyAPIGateway.Utilities.ShowMessage(
                "Voxel Cubemap Api",
                message);
        }
    }
}
#endif
