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
            new Version(0, 0, 7);

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


        /// <summary>
        /// Creates an ocean-style material transition around a caller-selected
        /// 16-bit height sample. Terrain at/below the height is Rocks_grass;
        /// terrain above it receives the same seamless fBm family used by the
        /// grass example, with caller-selected coverage. Sand_02 forms a solid
        /// shoreline core plus noisy shoulders across the transition band.
        /// </summary>
        [ChatCommand("generateocean", "vcma")]
        public static void GenerateOcean(
            int oceanHeight = 32768,
            int fractalFillPercent = 100)
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
                if (oceanHeight < 0 ||
                    oceanHeight > ushort.MaxValue)
                {
                    throw new ArgumentException(
                        "Ocean height must be from 0 to 65535.",
                        "oceanHeight");
                }

                if (fractalFillPercent < 0 ||
                    fractalFillPercent > 100)
                {
                    throw new ArgumentException(
                        "Fractal fill must be from 0 to 100.",
                        "fractalFillPercent");
                }


                template =
                    client._api.GetModificationTemplate(
                        0);

                if (template == null)
                {
                    throw new Exception(
                        "Could not create a template for the nearest planet.");
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

                byte rockMaterialMapValue =
                    template.AddMaterial(
                        "Rocks_grass",
                        5f);

                byte sandMaterialMapValue =
                    template.AddMaterial(
                        "Sand_02",
                        4f);


                long planetSeed =
                    template.GetPlanetSeed();

                const double GrassNoiseFrequency =
                    2.15;

                const int GrassNoiseOctaves =
                    4;

                const int SandBandHalfWidth =
                    1024;

                const int SandCoreHalfWidth =
                    384;

                int sandMinimumAltitude =
                    Math.Max(
                        0,
                        oceanHeight - SandBandHalfWidth);

                int sandMaximumAltitude =
                    Math.Min(
                        ushort.MaxValue,
                        oceanHeight + SandBandHalfWidth);

                int sandCoreMinimumAltitude =
                    Math.Max(
                        0,
                        oceanHeight - SandCoreHalfWidth);

                int sandCoreMaximumAltitude =
                    Math.Min(
                        ushort.MaxValue,
                        oceanHeight + SandCoreHalfWidth);


                // Seabed / terrain below the requested waterline.
                template.ApplyBrush(
                    "Material",
                    rockMaterialMapValue,
                    false,
                    0.0,
                    0,
                    0,
                    0.0,
                    1.0,
                    -1,
                    oceanHeight,
                    -90.0,
                    90.0,
                    -1,
                    -1);


                // Above the waterline, use the same fBm frequency/octave family
                // as ApplyFractalNoise. For partial coverage, calculate the same
                // sampled percentile threshold and feed it to the brush blend.
                if (oceanHeight < ushort.MaxValue &&
                    fractalFillPercent > 0)
                {
                    bool useGrassNoise =
                        fractalFillPercent < 100;

                    NoiseProvider noise =
                        client._api.GetNoiseProvider();

                    if (noise == null)
                    {
                        throw new Exception(
                            "API noise provider is not available.");
                    }

                    double grassNoiseMinimum =
                        useGrassNoise
                            ? noise.FractalBrownianMotionCoverage(
                                planetSeed,
                                2.15,
                                4,
                                0,
                                fractalFillPercent)
                            : 0.0;

                    template.ApplyBrush(
                        "Material",
                        grassMaterialMapValue,
                        useGrassNoise,
                        GrassNoiseFrequency,
                        GrassNoiseOctaves,
                        0,
                        grassNoiseMinimum,
                        1.0,
                        oceanHeight + 1,
                        -1,
                        -90.0,
                        90.0,
                        -1,
                        -1);
                }


                // Always keep a continuous sand core around the waterline.
                template.ApplyBrush(
                    "Material",
                    sandMaterialMapValue,
                    false,
                    0.0,
                    0,
                    0,
                    0.0,
                    1.0,
                    sandCoreMinimumAltitude,
                    sandCoreMaximumAltitude,
                    -90.0,
                    90.0,
                    -1,
                    -1);

                // Expand that core into a wider irregular shoreline using a
                // higher-frequency seamless cubemap noise field.
                template.ApplyBrush(
                    "Material",
                    sandMaterialMapValue,
                    true,
                    8.0,
                    3,
                    104729,
                    0.48,
                    1.0,
                    sandMinimumAltitude,
                    sandMaximumAltitude,
                    -90.0,
                    90.0,
                    -1,
                    -1);


                template.SetEnvironmentDefinition(
                    "VoxelCubemapGrassEnvironmentCarrier");

                MyLog.Default.WriteLineAndConsole(
                    "[Grass API Test Client] GenerateOcean: height=" +
                    oceanHeight +
                    ", grass fractal fill=" +
                    fractalFillPercent +
                    "%, rock map=" +
                    rockMaterialMapValue +
                    ", sand map=" +
                    sandMaterialMapValue +
                    ", grass map=" +
                    grassMaterialMapValue +
                    ", planet seed=" +
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
                                    ? "Ocean material transition committed."
                                    : "Ocean material transition failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning(
                    "Ocean generation failed",
                    e);

                ShowMessage(
                    "Failed: " +
                    e.Message);
            }
        }

        [ChatCommand("water", "vcma")]
        public static void WaterLevel(int level)
        {
            GrassPlanetApiTestClient client = _instance;
            
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
            
            ShowMessage("Water double:" + _instance._api.GetWaterUtil().HeightmapUnitToWaterRadius(0, level));
        }
        
        [ChatCommand("WaterToVoxel", "vcma")]
        public static void WaterToVoxel(double level)
        {
            GrassPlanetApiTestClient client = _instance;
            
            
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
            
            ShowMessage("Voxel uint16:" + _instance._api.GetWaterUtil().WaterRadiusToHeightmapUnit(0, level));
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
