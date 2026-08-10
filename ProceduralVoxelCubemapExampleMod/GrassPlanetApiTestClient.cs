using System;
using Generated;
using Sandbox.ModAPI;
using VoxelCubemapApi.Api;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

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

                // Add the complex material first. Besides allocating the grass
                // map value, this makes the server account for source-map values
                // before the two simple materials request free byte slots.
                byte grassMaterialMapValue =
                    template.AddComplexMaterial(
                        grassRules.MaterialGroup);

                byte rockMaterialMapValue =
                    AddSimpleMaterialAtFreeMapValue(
                        template,
                        "Rocks_grass",
                        5f);

                byte sandMaterialMapValue =
                    AddSimpleMaterialAtFreeMapValue(
                        template,
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

                    double grassNoiseMinimum =
                        useGrassNoise
                            ? ComputeGrassBrushCoverageThreshold(
                                planetSeed,
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


        private static byte AddSimpleMaterialAtFreeMapValue(
            ModificationTemplate template,
            string materialSubtype,
            float maxDepth)
        {
            for (int candidate = 0;
                candidate < byte.MaxValue;
                candidate++)
            {
                byte mapValue =
                    (byte)candidate;

                if (template.AddMaterial(
                    materialSubtype,
                    mapValue,
                    maxDepth))
                {
                    return mapValue;
                }
            }

            throw new Exception(
                "No free material-map byte remains for " +
                materialSubtype +
                ".");
        }


        /// <summary>
        /// Returns the normalized [0,1] brush threshold corresponding to the
        /// legacy grass coverage percentile. This intentionally mirrors the
        /// server's 129x129-per-face percentile sampling so GenerateOcean's
        /// fractalFillPercent has the same meaning as testgrass coverage.
        /// </summary>
        private static double ComputeGrassBrushCoverageThreshold(
            long planetSeed,
            int coveragePercent)
        {
            if (coveragePercent <= 0)
                return 1.0;

            if (coveragePercent >= 100)
                return 0.0;


            const int SampleResolution =
                129;

            int sampleCount =
                6 *
                SampleResolution *
                SampleResolution;

            var samples =
                new double[sampleCount];

            int sampleIndex =
                0;


            for (int face = 0;
                face < 6;
                face++)
            {
                for (int y = 0;
                    y < SampleResolution;
                    y++)
                {
                    for (int x = 0;
                        x < SampleResolution;
                        x++)
                    {
                        Vector3D direction =
                            GetCubemapSphereDirection(
                                face,
                                x,
                                y,
                                SampleResolution,
                                SampleResolution);

                        double raw =
                            PlanetGrassFbm(
                                direction,
                                planetSeed);

                        double normalized =
                            (raw + 1.0) *
                            0.5;

                        if (normalized < 0.0)
                            normalized = 0.0;
                        else if (normalized > 1.0)
                            normalized = 1.0;

                        samples[sampleIndex++] =
                            normalized;
                    }
                }
            }


            Array.Sort(
                samples);

            int selectedSampleCount =
                (sampleCount *
                    coveragePercent +
                    99) /
                100;

            int thresholdIndex =
                sampleCount -
                selectedSampleCount;

            if (thresholdIndex < 0)
                thresholdIndex = 0;

            if (thresholdIndex >= sampleCount)
                thresholdIndex = sampleCount - 1;

            return samples[
                thresholdIndex];
        }


        private static Vector3D GetCubemapSphereDirection(
            int faceIndex,
            int x,
            int y,
            int width,
            int height)
        {
            double u =
                width <= 1
                    ? 0.0
                    : (2.0 * x /
                        (width - 1.0)) -
                        1.0;

            double v =
                height <= 1
                    ? 0.0
                    : (2.0 * y /
                        (height - 1.0)) -
                        1.0;

            Vector3D direction;

            switch (faceIndex)
            {
                case 0:
                    direction =
                        new Vector3D(
                            u,
                            -v,
                            1.0);
                    break;

                case 1:
                    direction =
                        new Vector3D(
                            -u,
                            -v,
                            -1.0);
                    break;

                case 2:
                    direction =
                        new Vector3D(
                            -1.0,
                            -v,
                            u);
                    break;

                case 3:
                    direction =
                        new Vector3D(
                            1.0,
                            -v,
                            -u);
                    break;

                case 4:
                    direction =
                        new Vector3D(
                            u,
                            1.0,
                            v);
                    break;

                case 5:
                    direction =
                        new Vector3D(
                            -u,
                            -1.0,
                            v);
                    break;

                default:
                    throw new ArgumentException(
                        "faceIndex");
            }


            double lengthSquared =
                direction.X * direction.X +
                direction.Y * direction.Y +
                direction.Z * direction.Z;

            double inverseLength =
                1.0 /
                Math.Sqrt(
                    lengthSquared);

            return new Vector3D(
                direction.X * inverseLength,
                direction.Y * inverseLength,
                direction.Z * inverseLength);
        }


        private static double PlanetGrassFbm(
            Vector3D direction,
            long planetSeed)
        {
            double frequency =
                2.15;

            double amplitude =
                1.0;

            double sum =
                0.0;

            double amplitudeSum =
                0.0;


            for (int octave = 0;
                octave < 4;
                octave++)
            {
                long octaveSeed =
                    unchecked(
                        planetSeed +
                        octave * 104729L);

                sum +=
                    ValueNoise3D(
                        direction.X * frequency,
                        direction.Y * frequency,
                        direction.Z * frequency,
                        octaveSeed) *
                    amplitude;

                amplitudeSum +=
                    amplitude;

                frequency *=
                    2.07;

                amplitude *=
                    0.5;
            }

            return sum /
                amplitudeSum;
        }


        private static double ValueNoise3D(
            double x,
            double y,
            double z,
            long seed)
        {
            int x0 =
                FastFloor(
                    x);

            int y0 =
                FastFloor(
                    y);

            int z0 =
                FastFloor(
                    z);

            int x1 =
                x0 + 1;

            int y1 =
                y0 + 1;

            int z1 =
                z0 + 1;

            double tx =
                SmoothNoiseFraction(
                    x - x0);

            double ty =
                SmoothNoiseFraction(
                    y - y0);

            double tz =
                SmoothNoiseFraction(
                    z - z0);


            double n000 =
                LatticeNoiseValue(
                    x0,
                    y0,
                    z0,
                    seed);

            double n100 =
                LatticeNoiseValue(
                    x1,
                    y0,
                    z0,
                    seed);

            double n010 =
                LatticeNoiseValue(
                    x0,
                    y1,
                    z0,
                    seed);

            double n110 =
                LatticeNoiseValue(
                    x1,
                    y1,
                    z0,
                    seed);

            double n001 =
                LatticeNoiseValue(
                    x0,
                    y0,
                    z1,
                    seed);

            double n101 =
                LatticeNoiseValue(
                    x1,
                    y0,
                    z1,
                    seed);

            double n011 =
                LatticeNoiseValue(
                    x0,
                    y1,
                    z1,
                    seed);

            double n111 =
                LatticeNoiseValue(
                    x1,
                    y1,
                    z1,
                    seed);


            double nx00 =
                LerpNoise(
                    n000,
                    n100,
                    tx);

            double nx10 =
                LerpNoise(
                    n010,
                    n110,
                    tx);

            double nx01 =
                LerpNoise(
                    n001,
                    n101,
                    tx);

            double nx11 =
                LerpNoise(
                    n011,
                    n111,
                    tx);

            double nxy0 =
                LerpNoise(
                    nx00,
                    nx10,
                    ty);

            double nxy1 =
                LerpNoise(
                    nx01,
                    nx11,
                    ty);

            return LerpNoise(
                nxy0,
                nxy1,
                tz);
        }


        private static int FastFloor(
            double value)
        {
            int integer =
                (int)value;

            return value < integer
                ? integer - 1
                : integer;
        }


        private static double SmoothNoiseFraction(
            double value)
        {
            return value *
                value *
                value *
                (value *
                    (value * 6.0 - 15.0) +
                    10.0);
        }


        private static double LerpNoise(
            double a,
            double b,
            double amount)
        {
            return a +
                (b - a) *
                amount;
        }


        private static double LatticeNoiseValue(
            int x,
            int y,
            int z,
            long seed)
        {
            uint hash =
                unchecked(
                    (uint)seed ^
                    (uint)(seed >> 32));

            unchecked
            {
                hash ^=
                    (uint)x *
                    0x9E3779B9u;

                hash ^=
                    (uint)y *
                    0x85EBCA6Bu;

                hash ^=
                    (uint)z *
                    0xC2B2AE35u;

                hash ^=
                    hash >> 16;

                hash *=
                    0x7FEB352Du;

                hash ^=
                    hash >> 15;

                hash *=
                    0x846CA68Bu;

                hash ^=
                    hash >> 16;
            }

            return
                ((double)hash /
                    4294967295.0) *
                2.0 -
                1.0;
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
