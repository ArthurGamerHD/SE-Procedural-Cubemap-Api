using System;
using System.Text;
using ProceduralCubemapApi.Api;
using Generated;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRage.ModAPI;
using VRageMath;

namespace CubemapExampleMod
{
    /// <summary>
    /// Test consumer for the public SendModMessage API. No planet-generation
    /// implementation is called directly from this component.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    internal sealed class CubemapExampleModClient : MySessionComponentBase
    {
        private const long ReplyChannel =
            0x5643584150490002L;

        private static readonly Version ClientApiVersion =
            new Version(0, 0, 12);

        private static CubemapExampleModClient _instance;

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
                ApiProvider api = ProceduralCubemapApiClient.TryGet(ReplyChannel);

                if (api == null)
                {
                    _api =
                        null;

                    return;
                }

                Version serverVersion =
                    api.GetApiVersion();

                if (serverVersion == null ||
                    !ClientApiVersion.Equals(
                        serverVersion))
                {
                    throw new Exception(
                        "API version mismatch. Client=" +
                        ClientApiVersion +
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
        /// Prints every API-managed planet and its persistence/runtime details
        /// to local chat and the Space Engineers log.
        /// </summary>
        [ChatCommand("planets", "vcma")]
        public static void ShowApiPlanetDetails()
        {
            CubemapExampleModClient client =
                _instance;

            if (client == null)
            {
                ShowAndLog(
                    "VCM API test client is not initialized.");

                return;
            }

            try
            {
                if (client._api == null)
                    client.RequestApi();

                if (client._api == null)
                {
                    ShowAndLog(
                        "Voxel Cubemap API is not ready.");

                    return;
                }

                string[] details =
                    client._api.GetApiPlanetDetails();

                if (details == null ||
                    details.Length == 0)
                {
                    ShowAndLog(
                        "No API-managed planets are registered.");

                    return;
                }

                ShowAndLog(
                    "API-managed planets: " +
                    details.Length);

                for (int index = 0;
                    index < details.Length;
                    index++)
                {
                    ShowAndLog(
                        "[" +
                        (index + 1) +
                        "/" +
                        details.Length +
                        "]\n" +
                        details[index]);
                }
            }
            catch (Exception e)
            {
                ShowAndLog(
                    "Could not retrieve API planet details: " +
                    e.Message);

                LogWarning(
                    "API planet detail query failed",
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
            CubemapExampleModClient client =
                _instance;

            if (client == null)
            {
                ShowMessage(
                    "VCM API client is not initialized.");

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
                        nameof(grassCoveragePercent));
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
                    "CubemapGrassEnvironmentCarrier");

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] Random biome fractals: " +
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
        
        [ChatCommand("purpleGrass", "vcma")]
        public static void ApplyPurpleGrass(int materialFilter, bool setBiome255 = true)
        {
            ModificationTemplate template = null;

            try
            {
                CubemapExampleModClient client = _instance;
                
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
                
                template = _instance._api.GetModificationTemplate(0);
                if (template == null)
                    throw new Exception("Could not create a modification template for the nearest planet.");

                byte purpleGrassMapValue = template.AddMaterial("PurpleGrass", 5f);

                // Apply biome first when filtering by the original material.
                // Brush operations are queued; painting Material first could make
                // the old material filter unavailable to the following Biome brush.
                if (setBiome255)
                {
                    template.ApplyBrush(
                        "Biome",
                        255,
                        false,
                        0.0,
                        0,
                        0,
                        0.0,
                        1.0,
                        -1,
                        -1,
                        -90.0,
                        90.0,
                        -1,
                        materialFilter);
                }

                template.ApplyBrush(
                    "Material",
                    purpleGrassMapValue,
                    false,
                    0.0,
                    0,
                    0,
                    0.0,
                    1.0,
                    -1,
                    -1,
                    -90.0,
                    90.0,
                    -1,
                    materialFilter);

                ModificationTemplate pushedTemplate = template;
                template.Push(delegate(bool success, string message)
                {
                    try
                    {
                        pushedTemplate.Close();
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrWhiteSpace(message))
                    {
                        ShowMessage(success
                            ? "PurpleGrass committed. Runtime map value=" + purpleGrassMapValue + "."
                            : "PurpleGrass modification failed.");
                    }
                    else
                    {
                        ShowMessage(message);
                    }
                });

                template = null;
            }
            catch (Exception e)
            {
                if (template != null)
                {
                    try
                    {
                        template.Close();
                    }
                    catch
                    {
                    }
                }

                MyLog.Default.WriteLineAndConsole("[PurpleGrass Runtime] Apply failed: " + e);
                ShowMessage("PurpleGrass failed: " + e.Message);
            }
        }


        /// <summary>
        /// Applies environment from a loaded vanilla/modded planet definition
        /// </summary>
        [ChatCommand("applyenvironment", "vcma")]
        public static void ApplyEnvironmentPreset(string presetName = "EarthLike")
        {
            CubemapExampleModClient client =
                _instance;

            if (client == null)
            {
                ShowMessage(
                    "VCM API client is not initialized.");

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

            client.BeginEnvironmentPreset(
                presetName,
                delegate(bool success, string message)
                {
                    ShowMessage(
                        string.IsNullOrWhiteSpace(message)
                            ? success
                                ? "Environment preset applied."
                                : "Environment preset failed."
                            : message);
                },
                true);
        }

        /// <summary>
        /// Lists available environment presets.
        /// </summary>
        [ChatCommand("listenvironment", "vcma")]
        public static void ListEnvironmentPreset(string presetName = "")
        {
            CubemapExampleModClient client =
                _instance;

            if (client == null)
            {
                ShowMessage(
                    "VCM API client is not initialized.");

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
            
            var names = client._api.GetEnvironmentPresets().GetPresetNames();

            var sb = new StringBuilder("Found presets: ");
            foreach (var name in names) sb.Append(name+", ");

            ShowMessage(sb.ToString().TrimEnd(',', ' '));
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
            CubemapExampleModClient client =
                _instance;

            if (client == null)
            {
                ShowMessage(
                    "VCM API client is not initialized.");

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
                        nameof(oceanHeight));
                }

                if (fractalFillPercent < 0 ||
                    fractalFillPercent > 100)
                {
                    throw new ArgumentException(
                        "Fractal fill must be from 0 to 100.",
                        nameof(fractalFillPercent));
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

                const double grassNoiseFrequency =
                    2.15;

                const int grassNoiseOctaves =
                    4;

                const int sandBandHalfWidth =
                    1024;

                const int sandCoreHalfWidth =
                    384;

                int sandMinimumAltitude =
                    Math.Max(
                        0,
                        oceanHeight - sandBandHalfWidth);

                int sandMaximumAltitude =
                    Math.Min(
                        ushort.MaxValue,
                        oceanHeight + sandBandHalfWidth);

                int sandCoreMinimumAltitude =
                    Math.Max(
                        0,
                        oceanHeight - sandCoreHalfWidth);

                int sandCoreMaximumAltitude =
                    Math.Min(
                        ushort.MaxValue,
                        oceanHeight + sandCoreHalfWidth);


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
                        grassNoiseFrequency,
                        grassNoiseOctaves,
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
                    "CubemapGrassEnvironmentCarrier");

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] GenerateOcean: height=" +
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

        /// <summary>
        /// Repaints the nearest planet as sand and adds a seamless dune field
        /// through the public brush/noise API. The client never edits PNG or
        /// voxel data directly.
        /// </summary>
        [ChatCommand("testdune", "vcma")]
        public static void ApplyDunePlanet(
            int duneHeight = 128,
            double duneFrequency = 64)
        {
            const int domainWarpWaveNoiseType = 9;
            const int replaceHeightMode = 0;
            const int addHeightMode = 1;

            CubemapExampleModClient client = _instance;

            if (client == null)
            {
                ShowMessage("VCM API client is not initialized.");
                return;
            }

            if (client._api == null)
            {
                client.RequestApi();

                if (client._api == null)
                {
                    ShowMessage("Voxel Cubemap API is not ready.");
                    return;
                }
            }

            ModificationTemplate template = null;

            try
            {
                if (duneHeight < 1 || duneHeight > ushort.MaxValue)
                    throw new ArgumentException("Dune height must be from 1 to 65535.", nameof(duneHeight));

                if (double.IsNaN(duneFrequency) ||
                    double.IsInfinity(duneFrequency) ||
                    duneFrequency <= 0.0)
                {
                    throw new ArgumentException("Dune frequency must be finite and greater than zero.", nameof(duneFrequency));
                }

                template = client._api.GetModificationTemplate(0);

                if (template == null)
                    throw new Exception("Could not create a template for the nearest planet.");

                byte sandMaterialMapValue = template.AddMaterial(
                    "Sand_02",
                    4f);

                // Add, rather than replace, so the existing continents and
                // large terrain remain visible underneath the dune relief.
                template.ApplyNoiseBrush(
                    "Heightmap",
                    duneHeight,
                    domainWarpWaveNoiseType,
                    addHeightMode,
                    NoiseSamplingQuality.High,
                    duneFrequency,
                    3,
                    0x44554E45,
                    0.0,
                    1.0,
                    -1,
                    -1,
                    -90.0,
                    90.0,
                    -1,
                    -1);

                // Exercise the same API-side noise brush for the material
                // map as well. A full [0,1] selection paints every point sand.
                template.ApplyNoiseBrush(
                    "Material",
                    sandMaterialMapValue,
                    domainWarpWaveNoiseType,
                    replaceHeightMode,
                    NoiseSamplingQuality.High,
                    duneFrequency,
                    3,
                    0x44554E45,
                    0.0,
                    1.0,
                    -1,
                    -1,
                    -90.0,
                    90.0,
                    -1,
                    -1);

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] testdune: height=" +
                    duneHeight +
                    ", frequency=" +
                    duneFrequency +
                    ", sand map=" +
                    sandMaterialMapValue +
                    ", seed=" +
                    template.GetPlanetSeed() +
                    ".");

                ModificationTemplate pushedTemplate = template;
                template.Push(
                    delegate(bool success, string message)
                    {
                        pushedTemplate.Close();

                        ShowMessage(
                            string.IsNullOrWhiteSpace(message)
                                ? success
                                    ? "Sand dune planet committed."
                                    : "Sand dune planet failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning("Dune generation failed", e);
                ShowMessage("Failed: " + e.Message);
            }
        }


        /// <summary>
        /// Creates a raised-rim crater on the nearest planet at the player's
        /// current position. The player position is projected radially from the
        /// planet center; all cubemap editing is performed by the API.
        /// </summary>
        [ChatCommand("createcrater", "vcma")]
        public static void CreateCrater(
            int depth = 4096,
            double radiusDegrees = 3.0)
        {
            const int addHeightMode = 1;

            CubemapExampleModClient client = _instance;

            if (client == null)
            {
                ShowMessage("VCM API client is not initialized.");
                return;
            }

            if (client._api == null)
            {
                client.RequestApi();

                if (client._api == null)
                {
                    ShowMessage("Voxel Cubemap API is not ready.");
                    return;
                }
            }

            ModificationTemplate template = null;

            try
            {
                if (depth < 1 || depth > ushort.MaxValue)
                    throw new ArgumentException("Crater depth must be from 1 to 65535.", nameof(depth));

                if (double.IsNaN(radiusDegrees) ||
                    double.IsInfinity(radiusDegrees) ||
                    radiusDegrees <= 0.0 ||
                    radiusDegrees > 90.0)
                {
                    throw new ArgumentException("Crater radius must be greater than zero and no more than 90 degrees.", nameof(radiusDegrees));
                }

                if (MyAPIGateway.Session == null ||
                    MyAPIGateway.Session.Player == null)
                {
                    throw new Exception("A local player is required to choose the crater center.");
                }

                template = client._api.GetModificationTemplate(0);
                if (template == null)
                    throw new Exception("Could not create a template for the nearest planet.");

                IMyEntity planetEntity =
                    MyAPIGateway.Entities.GetEntityById(
                        template.GetPlanetEntityId());

                if (planetEntity == null)
                    throw new Exception("Could not resolve the nearest planet entity.");

                Vector3D centerDirection =
                    MyAPIGateway.Session.Player.GetPosition() -
                    planetEntity.GetPosition();

                if (centerDirection.LengthSquared() < 1.0)
                    throw new Exception("Could not determine a valid radial direction for the crater center.");

                centerDirection.Normalize();

                template.ApplyRadialBrush(
                    "Heightmap",
                    depth,
                    centerDirection.X,
                    centerDirection.Y,
                    centerDirection.Z,
                    radiusDegrees,
                    RadialFieldProfile.Crater,
                    addHeightMode,
                    -1,
                    -1,
                    -90.0,
                    90.0,
                    -1,
                    -1);

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] createcrater: depth=" +
                    depth +
                    ", radiusDegrees=" +
                    radiusDegrees +
                    ", center=" +
                    centerDirection +
                    ".");

                ModificationTemplate pushedTemplate = template;
                template.Push(
                    delegate(bool success, string message)
                    {
                        pushedTemplate.Close();

                        ShowMessage(
                            string.IsNullOrWhiteSpace(message)
                                ? success
                                    ? "Crater committed."
                                    : "Crater failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning("Crater generation failed", e);
                ShowMessage("Failed: " + e.Message);
            }
        }

        /// <summary>
        /// Covers the nearest planet with a deterministic, heavily cratered
        /// lunar-style surface. The recipe stores one compact crater-field
        /// feature instead of one radial operation per generated crater.
        /// Crater overlap is intentionally allowed by the API feature pass.
        /// </summary>
        [ChatCommand("testmoon", "vcma")]
        public static void TestMoon(
            int craterCount = 4096,
            int seedOffset = 0)
        {
            const double minimumRadiusDegrees = 0.05;
            const double maximumRadiusDegrees = 9.0;
            const int minimumDepth = 200;
            const int maximumDepth = 8000;
            const float targetSize = 0.1f;

            CubemapExampleModClient client = _instance;

            if (client == null)
            {
                ShowMessage("VCM API client is not initialized.");
                return;
            }

            if (client._api == null)
            {
                client.RequestApi();

                if (client._api == null)
                {
                    ShowMessage("Voxel Cubemap API is not ready.");
                    return;
                }
            }

            ModificationTemplate template = null;

            try
            {
                if (craterCount < 1 || craterCount > 65535)
                {
                    throw new ArgumentException(
                        "Crater count must be from 1 to 65535.",
                        nameof(craterCount));
                }

                template = client._api.GetModificationTemplate(0);
                if (template == null)
                    throw new Exception("Could not create a template for the nearest planet.");

                FeatureTemplate feature = template.AddFeature();
                feature.AddCraterFieldBiased(
                    craterCount,
                    seedOffset,
                    minimumRadiusDegrees,
                    maximumRadiusDegrees,
                    minimumDepth,
                    maximumDepth,
                    targetSize);

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] testmoon: craterField count=" +
                    craterCount +
                    ", seedOffset=" +
                    seedOffset +
                    ", targetSize=" +
                    targetSize +
                    ", planetSeed=" +
                    template.GetPlanetSeed() +
                    ". Overlap is enabled.");

                ModificationTemplate pushedTemplate = template;
                template.Push(
                    delegate(bool success, string message)
                    {
                        pushedTemplate.Close();

                        ShowMessage(
                            string.IsNullOrWhiteSpace(message)
                                ? success
                                    ? "Moon crater field committed."
                                    : "Moon crater field failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning("Moon crater generation failed", e);
                ShowMessage("Failed: " + e.Message);
            }
        }


        /// <summary>
        /// Adds a small deterministic field of very large, tall volcanoes to the
        /// nearest planet. This intentionally uses extreme values so the volcano
        /// profile is easy to inspect in-game.
        /// </summary>
        [ChatCommand("testvolcano", "vcma")]
        public static void TestVolcano(
            int volcanoCount = 4,
            int seedOffset = 0)
        {
            const double minimumRadiusDegrees = 2.5;
            const double maximumRadiusDegrees = 7.0;
            const int minimumHeight = 18000;
            const int maximumHeight = 32000;
            const float targetSize = 0.65f;

            CubemapExampleModClient client = _instance;

            if (client == null)
            {
                ShowMessage("VCM API client is not initialized.");
                return;
            }

            if (client._api == null)
            {
                client.RequestApi();

                if (client._api == null)
                {
                    ShowMessage("Voxel Cubemap API is not ready.");
                    return;
                }
            }

            ModificationTemplate template = null;

            try
            {
                if (volcanoCount < 1 || volcanoCount > 16)
                {
                    throw new ArgumentException(
                        "Volcano count must be from 1 to 16.",
                        nameof(volcanoCount));
                }

                template = client._api.GetModificationTemplate(0);
                if (template == null)
                    throw new Exception("Could not create a template for the nearest planet.");

                FeatureTemplate feature = template.AddFeature();
                feature.AddVolcanoFieldBiased(
                    volcanoCount,
                    seedOffset,
                    minimumRadiusDegrees,
                    maximumRadiusDegrees,
                    minimumHeight,
                    maximumHeight,
                    targetSize);

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] testvolcano: volcanoField count=" +
                    volcanoCount +
                    ", seedOffset=" +
                    seedOffset +
                    ", targetSize=" +
                    targetSize +
                    ", radiusDegrees=" +
                    minimumRadiusDegrees +
                    ".." +
                    maximumRadiusDegrees +
                    ", height=" +
                    minimumHeight +
                    ".." +
                    maximumHeight +
                    ", planetSeed=" +
                    template.GetPlanetSeed() +
                    ".");

                ModificationTemplate pushedTemplate = template;
                template.Push(
                    delegate(bool success, string message)
                    {
                        pushedTemplate.Close();

                        ShowMessage(
                            string.IsNullOrWhiteSpace(message)
                                ? success
                                    ? "Volcano field committed."
                                    : "Volcano field failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning("Volcano generation failed", e);
                ShowMessage("Failed: " + e.Message);
            }
        }


        /// <summary>
        /// Carves several long deterministic ravines into the nearest planet.
        /// This exercises spherical path generation, segment tiling and the
        /// parallel feature raster pass.
        /// </summary>
        [ChatCommand("testravine", "vcma")]
        public static void TestRavine(
            int ravineCount = 8,
            int seedOffset = 0)
        {
            const double minimumLengthDegrees = 10.0;
            const double maximumLengthDegrees = 35.0;
            const double minimumWidthDegrees = 0.35;
            const double maximumWidthDegrees = 1.20;
            const int minimumDepth = 3500;
            const int maximumDepth = 10000;
            const float targetSize = 0.45f;

            CubemapExampleModClient client = _instance;
            if (client == null)
            {
                ShowMessage("VCM API client is not initialized.");
                return;
            }

            if (client._api == null)
            {
                client.RequestApi();
                if (client._api == null)
                {
                    ShowMessage("Voxel Cubemap API is not ready.");
                    return;
                }
            }

            ModificationTemplate template = null;
            try
            {
                if (ravineCount < 1 || ravineCount > 32)
                    throw new ArgumentException("Ravine count must be from 1 to 32.", nameof(ravineCount));

                template = client._api.GetModificationTemplate(0);
                if (template == null)
                    throw new Exception("Could not create a template for the nearest planet.");

                FeatureTemplate feature = template.AddFeature();
                feature.AddRavineFieldBiased(
                    ravineCount,
                    seedOffset,
                    minimumLengthDegrees,
                    maximumLengthDegrees,
                    minimumWidthDegrees,
                    maximumWidthDegrees,
                    minimumDepth,
                    maximumDepth,
                    targetSize);

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] testravine: ravineField count=" +
                    ravineCount +
                    ", seedOffset=" + seedOffset +
                    ", targetSize=" + targetSize +
                    ", lengthDegrees=" + minimumLengthDegrees + ".." + maximumLengthDegrees +
                    ", widthDegrees=" + minimumWidthDegrees + ".." + maximumWidthDegrees +
                    ", depth=" + minimumDepth + ".." + maximumDepth +
                    ", planetSeed=" + template.GetPlanetSeed() + ".");

                ModificationTemplate pushedTemplate = template;
                template.Push(
                    delegate(bool success, string message)
                    {
                        pushedTemplate.Close();
                        ShowMessage(
                            string.IsNullOrWhiteSpace(message)
                                ? success
                                    ? "Ravine field committed."
                                    : "Ravine field failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning("Ravine generation failed", e);
                ShowMessage("Failed: " + e.Message);
            }
        }


        /// <summary>
        /// Generates deterministic sea-level rivers on the nearest planet. River
        /// sources and meanders come only from planetSeed + seedOffset; each source
        /// connects to the nearest terrain sample at/below shorelineHeight and grows
        /// a small distributary delta as it reaches the coast.
        /// </summary>
        [ChatCommand("testriver", "vcma")]
        public static void TestRiver(
            int riverCount = 6,
            int shorelineHeight = 32768,
            int seedOffset = 0,
            double minimumWidthDegrees = 0.28,
            double maximumWidthDegrees = 0.68)
        {
            const int minimumSourceHeightAboveShoreline = 1800;
            const double minimumLengthDegrees = 4.0;
            const double maximumLengthDegrees = 32.0;
            // Defaults are broad enough to remain visible on 4K cubemap faces. The
            // production generator widens the trunk downstream and derives delta
            // distributaries from this width, so these values describe the trunk rather
            // than the full delta fan. They remain command arguments for quick tuning.
            const int minimumDepth = 900;
            const int maximumDepth = 2200;
            const double shoulderWidthMultiplier = 5.0;

            CubemapExampleModClient client = _instance;
            if (client == null)
            {
                ShowMessage("VCM API client is not initialized.");
                return;
            }

            if (client._api == null)
            {
                client.RequestApi();
                if (client._api == null)
                {
                    ShowMessage("Voxel Cubemap API is not ready.");
                    return;
                }
            }

            ModificationTemplate template = null;
            try
            {
                if (riverCount < 1 || riverCount > 64)
                    throw new ArgumentException("River count must be from 1 to 64.", nameof(riverCount));
                if (shorelineHeight < 0 || shorelineHeight > ushort.MaxValue)
                    throw new ArgumentException("Shoreline height must be from 0 to 65535.", nameof(shorelineHeight));
                if (double.IsNaN(minimumWidthDegrees) || double.IsInfinity(minimumWidthDegrees) ||
                    double.IsNaN(maximumWidthDegrees) || double.IsInfinity(maximumWidthDegrees) ||
                    minimumWidthDegrees <= 0.0 || maximumWidthDegrees < minimumWidthDegrees ||
                    maximumWidthDegrees > 10.0)
                {
                    throw new ArgumentException(
                        "River width range must be finite, greater than zero, ordered, and at most 10 degrees.");
                }

                template = client._api.GetModificationTemplate(0);
                if (template == null)
                    throw new Exception("Could not create a template for the nearest planet.");

                FeatureTemplate feature = template.AddFeature();
                feature.AddRiverField(
                    riverCount,
                    seedOffset,
                    shorelineHeight,
                    minimumSourceHeightAboveShoreline,
                    minimumLengthDegrees,
                    maximumLengthDegrees,
                    minimumWidthDegrees,
                    maximumWidthDegrees,
                    minimumDepth,
                    maximumDepth,
                    shoulderWidthMultiplier);

                MyLog.Default.WriteLineAndConsole(
                    "[VCM API Test Client] testriver: riverField count=" +
                    riverCount +
                    ", shorelineHeight=" + shorelineHeight +
                    ", seedOffset=" + seedOffset +
                    ", sourceClearance=" + minimumSourceHeightAboveShoreline +
                    ", lengthDegrees=" + minimumLengthDegrees + ".." + maximumLengthDegrees +
                    ", widthDegrees=" + minimumWidthDegrees + ".." + maximumWidthDegrees +
                    ", depth=" + minimumDepth + ".." + maximumDepth +
                    ", shoulder=" + shoulderWidthMultiplier +
                    ", planetSeed=" + template.GetPlanetSeed() + ".");

                ModificationTemplate pushedTemplate = template;
                template.Push(
                    delegate(bool success, string message)
                    {
                        pushedTemplate.Close();
                        ShowMessage(
                            string.IsNullOrWhiteSpace(message)
                                ? success
                                    ? "River field committed."
                                    : "River field failed."
                                : message);
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                LogWarning("River generation failed", e);
                ShowMessage("Failed: " + e.Message);
            }
        }


        [ChatCommand("water", "vcma")]
        public static void WaterLevel(int level)
        {
            CubemapExampleModClient client = _instance;
            
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
            CubemapExampleModClient client = _instance;
            
            
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


        private void BeginEnvironmentPreset(
            string presetName,
            Action<bool, string> completed,
            bool logFailure)
        {
            ModificationTemplate template =
                null;

            try
            {
                EnvironmentPresetProvider presets =
                    _api.GetEnvironmentPresets();

                if (presets == null)
                {
                    throw new Exception(
                        "Environment preset provider is unavailable.");
                }

                if (!presets.HasPreset(presetName))
                {
                    throw new Exception(
                        "Environment preset '" +
                        presetName +
                        "' is unavailable. Loaded presets: " +
                        string.Join(", ", presets.GetPresetNames()));
                }

                template =
                    _api.GetModificationTemplate(
                        0);

                if (template == null)
                {
                    throw new Exception(
                        "Could not create a template for the nearest planet.");
                }

                template.SetEnvironmentPreset(
                    presetName);

                ModificationTemplate pushedTemplate =
                    template;

                template.Push(
                    delegate(bool success, string message)
                    {
                        pushedTemplate.Close();

                        if (completed != null)
                        {
                            completed(
                                success,
                                message);
                        }
                    });
            }
            catch (Exception e)
            {
                if (template != null)
                    template.Close();

                if (logFailure)
                {
                    LogWarning(
                        "Environment preset failed",
                        e);
                }

                if (completed != null)
                {
                    completed(
                        false,
                        e.Message);
                }
            }
        }


        private static void LogWarning(
            string message,
            Exception e)
        {
            MyLog.Default.WriteLineAndConsole(
                "[VCM API Test Client] " +
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


        private static void ShowAndLog(
            string message)
        {
            string text =
                message ?? string.Empty;

            MyLog.Default.WriteLineAndConsole(
                "[VCM API Test Client] " +
                text);

            ShowMessage(
                text);
        }
    }
}
