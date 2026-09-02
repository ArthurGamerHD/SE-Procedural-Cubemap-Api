using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adk.Compression.Zip;
using Adk.Image;
using Adk.Image.Dds;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ObjectBuilders;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace ProceduralCubemapApi.Common.PlanetModification.Runtime
{
    /// <summary>
    /// Compiles caller-authored legacy planet EnvironmentItems through Keen's
    /// normal definition loader. The temporary compiler mod is supplied only to
    /// MyDefinitionManager.LoadData(); it is never added to Session.Mods and is
    /// therefore never persisted or advertised as a multiplayer dependency.
    ///
    /// A definition reload is only safe before planet entities are initialized,
    /// so this service deliberately permits one compilation batch per session.
    /// </summary>
    internal sealed class RuntimeEnvironmentDefinitionCompiler
    {
        private const string TEMP_ARCHIVE_FILE =
            "apc_api_temp_enviroment_definition.zip";

        private const string TEMP_DEFINITION_FILE =
            "Data/apc_api_temp_environment_definitions.sbc";

        private const string TEMP_VISUAL_DEFINITION_FILE =
            "Data/apc_api_temp_gas_visual_materials.sbc";

        private const string GAS_VISUAL_MATERIAL_PREFIX =
            "APC_GasVisual_";

        private const string PROCEDURAL_GAS_PREFIX =
            "ProceduralSystems_GasGiant_";

        private const string PROCEDURAL_ICE_PREFIX =
            "ProceduralSystems_IceGiant_";

        private const string CARRIER_PREFIX =
            "APC_TempEnvironment_";

        private bool _compiledThisSession;
        private bool _compileAttemptedThisSession;
        private Exception _firstCompileFailure;


        internal Dictionary<string, string> Compile(
            IEnumerable<RuntimeEnvironmentCompileRequest> requests)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));

            var uniqueRequests =
                new Dictionary<string, RuntimeEnvironmentCompileRequest>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (RuntimeEnvironmentCompileRequest request in requests)
            {
                if (request == null ||
                    request.Builder == null ||
                    string.IsNullOrWhiteSpace(request.OwnerGeneratorSubtype) ||
                    request.Builder.Environment.HasValue ||
                    request.Builder.EnvironmentItems == null ||
                    request.Builder.EnvironmentItems.Length == 0)
                {
                    continue;
                }

                uniqueRequests[request.OwnerGeneratorSubtype] =
                    request;
            }

            var output =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            if (uniqueRequests.Count == 0)
                return output;

            if (_compiledThisSession)
            {
                throw new InvalidOperationException(
                    "Runtime environment definitions have already been compiled " +
                    "during this session. A second full definition reload after " +
                    "planet initialization is intentionally not supported.");
            }

            if (_compileAttemptedThisSession)
            {
                throw new InvalidOperationException(
                    "Runtime environment compilation already failed during this " +
                    "session. Refusing to trigger another full definition reload.",
                    _firstCompileFailure);
            }

            if (MyAPIGateway.Session == null ||
                MyAPIGateway.Session.Mods == null)
            {
                throw new InvalidOperationException(
                    "Session mod metadata is unavailable while compiling runtime " +
                    "planet environments.");
            }

            var compilerBuilders =
                new List<MyObjectBuilder_PlanetGeneratorDefinition>(
                    uniqueRequests.Count);

            var visualAssets =
                new List<RuntimeGiantVisualAsset>();

            foreach (KeyValuePair<string, RuntimeEnvironmentCompileRequest> pair in
                uniqueRequests)
            {
                string carrierSubtype =
                    BuildCarrierSubtype(pair.Key);

                output[pair.Key] =
                    carrierSubtype;

                compilerBuilders.Add(
                    CreateCompilerBuilder(
                        pair.Value.Builder,
                        carrierSubtype));

                if (IsProceduralGiantSubtype(pair.Key))
                {
                    visualAssets.Add(
                        CreateGiantVisualAsset(
                            pair.Key));
                }
            }

            var realMods =
                new List<MyObjectBuilder_Checkpoint.ModItem>(
                    MyAPIGateway.Session.Mods);

            var reloadMods =
                new List<MyObjectBuilder_Checkpoint.ModItem>(
                    realMods);

            string serviceName =
                realMods
                    .Where(x => !string.IsNullOrWhiteSpace(x.PublishedServiceName))
                    .Select(x => x.PublishedServiceName)
                    .FirstOrDefault();

            // Local mod paths are resolved below UserData/Mods. The temporary
            // compiler archive lives in UserData/Storage, so one parent traversal
            // reaches the sibling Storage directory. This ModItem exists only in
            // the local LoadData() argument list; Session.Mods is never mutated.
            var compilerMod =
                new MyObjectBuilder_Checkpoint.ModItem(
                    "../Storage/" + TEMP_ARCHIVE_FILE,
                    0UL,
                    serviceName);

            reloadMods.Add(
                compilerMod);

            _compileAttemptedThisSession =
                true;

            try
            {
                WriteCompilerArchive(
                    compilerBuilders,
                    visualAssets);

                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Compiling " +
                    compilerBuilders.Count +
                    " runtime environment definition(s) and " +
                    visualAssets.Count +
                    " procedural giant visual material(s) through temporary mod '" +
                    TEMP_ARCHIVE_FILE +
                    "'.");

                MyDefinitionManager.Static.UnloadData(true);
                MyDefinitionManager.Static.LoadData(reloadMods);

                ValidateCompiledCarriers(
                    output.Values);

                _compiledThisSession =
                    true;

                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Runtime environment compilation " +
                    "finished successfully. Temporary compiler mod was not added " +
                    "to Session.Mods.");
            }
            catch (Exception compileError)
            {
                _firstCompileFailure =
                    compileError;

                Exception restoreError =
                    null;

                try
                {
                    // Do not leave the session with a partially loaded definition
                    // set if the generated compiler mod or any environment mapping
                    // is rejected.
                    MyDefinitionManager.Static.UnloadData(true);
                    MyDefinitionManager.Static.LoadData(realMods);
                }
                catch (Exception exception)
                {
                    restoreError =
                        exception;
                }

                string message =
                    "Runtime environment definition compilation failed: " +
                    compileError;

                if (restoreError != null)
                {
                    message +=
                        " | Restoring the normal definition set also failed: " +
                        restoreError;
                }

                throw new Exception(
                    message,
                    compileError);
            }

            return output;
        }


        private static MyObjectBuilder_PlanetGeneratorDefinition
            CreateCompilerBuilder(
                MyObjectBuilder_PlanetGeneratorDefinition source,
                string carrierSubtype)
        {
            return new MyObjectBuilder_PlanetGeneratorDefinition
            {
                Id = new SerializableDefinitionId(
                    typeof(MyObjectBuilder_PlanetGeneratorDefinition),
                    carrierSubtype),

                // The environment compiler only needs the legacy mappings. Keen's
                // MyPlanetGeneratorDefinition.Postprocessor converts these into a
                // MyProceduralEnvironmentDefinition and then Prepare() compiles the
                // runtime mapping tables/samplers for us.
                EnvironmentItems = source.EnvironmentItems,

                // Keep the compiler carrier itself structurally planet-valid. The
                // real runtime generator receives its own final atmosphere values.
                HasAtmosphere = true
            };
        }


        private static void WriteCompilerArchive(
            List<MyObjectBuilder_PlanetGeneratorDefinition> builders,
            List<RuntimeGiantVisualAsset> visualAssets)
        {
            var definitions =
                new MyObjectBuilder_Definitions
                {
                    PlanetGeneratorDefinitions =
                        builders.ToArray()
                };

            string xml =
                MyAPIGateway.Utilities.SerializeToXML(
                    definitions);

            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new Exception(
                    "Could not serialize generated environment compiler definitions.");
            }

            // SerializeToXML() serializes through a StringWriter and therefore
            // emits encoding="utf-16" in the XML declaration. The generated mod
            // is stored as UTF-8 bytes, so leaving that declaration intact makes
            // Keen's XML pre-scan demand a UTF-16 BOM and reject the SBC. Rewrite
            // only the declaration so the declared encoding matches the bytes.
            xml = NormalizeXmlDeclarationForUtf8(
                xml);

            var entries =
                new List<MinimalZip.Entry>(
                    2 +
                    (visualAssets == null
                        ? 0
                        : visualAssets.Count));

            entries.Add(
                new MinimalZip.Entry(
                    TEMP_DEFINITION_FILE,
                    new UTF8Encoding(false).GetBytes(xml),
                    MinimalZip.CompressionMode.Deflate));

            if (visualAssets != null &&
                visualAssets.Count > 0)
            {
                string visualDefinitions =
                    BuildVisualMaterialDefinitionsXml(
                        visualAssets);

                entries.Add(
                    new MinimalZip.Entry(
                        TEMP_VISUAL_DEFINITION_FILE,
                        new UTF8Encoding(false).GetBytes(
                            visualDefinitions),
                        MinimalZip.CompressionMode.Deflate));

                for (int index = 0;
                    index < visualAssets.Count;
                    index++)
                {
                    RuntimeGiantVisualAsset asset =
                        visualAssets[index];

                    entries.Add(
                        new MinimalZip.Entry(
                            asset.TextureArchivePath,
                            asset.TextureBytes,
                            MinimalZip.CompressionMode.Stored));
                }
            }

            byte[] archive =
                MinimalZip.WriteBytes(
                    entries.ToArray());

            using (BinaryWriter writer =
                MyAPIGateway.Utilities.WriteBinaryFileInGlobalStorage(
                    TEMP_ARCHIVE_FILE))
            {
                writer.Write(
                    archive);
            }
        }


        private static string BuildVisualMaterialDefinitionsXml(
            List<RuntimeGiantVisualAsset> visualAssets)
        {
            var xml =
                new StringBuilder();

            xml.Append(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.Append(
                "<Definitions xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
            xml.Append(
                "<TransparentMaterials>");

            for (int index = 0;
                index < visualAssets.Count;
                index++)
            {
                RuntimeGiantVisualAsset asset =
                    visualAssets[index];

                xml.Append(
                    "<TransparentMaterial>");
                xml.Append(
                    "<Id><TypeId>TransparentMaterialDefinition</TypeId><SubtypeId>");
                xml.Append(
                    asset.MaterialSubtype);
                xml.Append(
                    "</SubtypeId></Id>");
                xml.Append(
                    "<AlphaMistingEnable>false</AlphaMistingEnable>");
                xml.Append(
                    "<AlphaSaturation>1</AlphaSaturation>");
                xml.Append(
                    "<CanBeAffectedByOtherLights>false</CanBeAffectedByOtherLights>");
                xml.Append(
                    "<IgnoreDepth>false</IgnoreDepth>");
                xml.Append(
                    "<SoftParticleDistanceScale>0</SoftParticleDistanceScale>");
                xml.Append(
                    "<Texture>");
                xml.Append(
                    asset.TextureArchivePath);
                xml.Append(
                    "</Texture>");
                xml.Append(
                    "<UseAtlas>false</UseAtlas>");
                xml.Append(
                    "<Color><X>1</X><Y>1</Y><Z>1</Z><W>1</W></Color>");
                xml.Append(
                    "<ColorAdd><X>0</X><Y>0</Y><Z>0</Z><W>0</W></ColorAdd>");
                xml.Append(
                    "<AlphaCutout>false</AlphaCutout>");
                xml.Append(
                    "<IsFlareOccluder>false</IsFlareOccluder>");
                xml.Append(
                    "<TriangleFaceCulling>false</TriangleFaceCulling>");
                xml.Append(
                    "</TransparentMaterial>");
            }

            xml.Append(
                "</TransparentMaterials></Definitions>");

            return xml.ToString();
        }


        private static RuntimeGiantVisualAsset CreateGiantVisualAsset(
            string ownerGeneratorSubtype)
        {
            ulong hash =
                HashSubtype(
                    ownerGeneratorSubtype);

            string hashText =
                hash.ToString(
                    "X16");

            bool iceGiant =
                ownerGeneratorSubtype.StartsWith(
                    PROCEDURAL_ICE_PREFIX,
                    StringComparison.OrdinalIgnoreCase);

            string textureArchivePath =
                "Textures/APC/GasVisual_" +
                hashText +
                ".dds";

            return new RuntimeGiantVisualAsset
            {
                MaterialSubtype =
                    GAS_VISUAL_MATERIAL_PREFIX +
                    hashText,
                TextureArchivePath =
                    textureArchivePath,
                TextureBytes =
                    GenerateGiantTextureDds(
                        hash,
                        iceGiant)
            };
        }


        private static byte[] GenerateGiantTextureDds(
            ulong hash,
            bool iceGiant)
        {
            const int width = 512;
            const int height = 256;

            int randomSeed =
                unchecked(
                    (int)(hash ^ (hash >> 32)));

            var random =
                new Random(
                    randomSeed);

            var bitmap =
                new RawRgbaBitmap(
                    width,
                    height);

            float baseHue =
                iceGiant
                    ? NextRange(random, 0.48f, 0.62f)
                    : NextRange(random, 0.015f, 0.16f);

            float baseSaturation =
                iceGiant
                    ? NextRange(random, 0.34f, 0.62f)
                    : NextRange(random, 0.38f, 0.76f);

            float baseValue =
                NextRange(
                    random,
                    0.58f,
                    0.86f);

            int bandCount =
                random.Next(10, 23);

            double bandPhase =
                random.NextDouble() *
                Math.PI *
                2.0;

            double warpPhase =
                random.NextDouble() *
                Math.PI *
                2.0;

            double stormLongitude =
                random.NextDouble() *
                Math.PI *
                2.0;

            double stormLatitude =
                NextRange(random, -0.55f, 0.55f) *
                Math.PI *
                0.5;

            double stormLongitudeRadius =
                NextRange(random, 0.18f, 0.38f);

            double stormLatitudeRadius =
                NextRange(random, 0.08f, 0.18f);

            for (int y = 0;
                y < height;
                y++)
            {
                double latitude01 =
                    (y + 0.5) /
                    height;

                double latitude =
                    (latitude01 - 0.5) *
                    Math.PI;

                double latitudeBand =
                    (latitude01 *
                     bandCount *
                     Math.PI *
                     2.0) +
                    bandPhase;

                for (int x = 0;
                    x < width;
                    x++)
                {
                    double longitude =
                        (x / (double)width) *
                        Math.PI *
                        2.0;

                    double warp =
                        Math.Sin(
                            longitude * 2.0 +
                            warpPhase) *
                        0.55 +
                        Math.Sin(
                            longitude * 5.0 -
                            warpPhase * 0.7) *
                        0.20;

                    double mainBand =
                        Math.Sin(
                            latitudeBand +
                            warp *
                            Math.Cos(latitude));

                    double fineBand =
                        Math.Sin(
                            latitudeBand * 2.7 -
                            longitude * 0.7 +
                            warpPhase);

                    double longitudeDistance =
                        Math.Abs(
                            longitude -
                            stormLongitude);

                    if (longitudeDistance > Math.PI)
                    {
                        longitudeDistance =
                            Math.PI * 2.0 -
                            longitudeDistance;
                    }

                    double stormDistance =
                        (longitudeDistance / stormLongitudeRadius) *
                        (longitudeDistance / stormLongitudeRadius) +
                        ((latitude - stormLatitude) / stormLatitudeRadius) *
                        ((latitude - stormLatitude) / stormLatitudeRadius);

                    double storm =
                        Math.Exp(
                            -stormDistance * 2.8);

                    float hue =
                        WrapHue(
                            baseHue +
                            (float)(mainBand * 0.022) +
                            (float)(fineBand * 0.008) +
                            (float)(storm *
                                (iceGiant
                                    ? -0.025
                                    : 0.035)));

                    float saturation =
                        MathHelper.Clamp(
                            baseSaturation +
                            (float)(mainBand * 0.10) -
                            (float)(storm * 0.10),
                            0.08f,
                            0.95f);

                    float value =
                        MathHelper.Clamp(
                            baseValue +
                            (float)(mainBand * 0.13) +
                            (float)(fineBand * 0.045) +
                            (float)(storm * 0.16),
                            0.18f,
                            1f);

                    Color color =
                        new Vector3(
                            hue,
                            saturation,
                            value)
                        .HSVtoColor();

                    bitmap.SetPixel(
                        x,
                        y,
                        color.R,
                        color.G,
                        color.B,
                        255);
                }
            }

            DdsEncodeOptions options =
                SpaceEngineersTextureProfiles.CreateTexture(
                    SpaceEngineersTextureKind.TransparentColor);

            return DdsEncoder.Encode(
                bitmap,
                options);
        }


        private static float WrapHue(
            float hue)
        {
            hue -=
                (float)Math.Floor(hue);

            return hue;
        }


        private static float NextRange(
            Random random,
            float minimum,
            float maximum)
        {
            return
                minimum +
                (float)random.NextDouble() *
                (maximum - minimum);
        }


        private static bool IsProceduralGiantSubtype(
            string subtype)
        {
            return
                subtype.StartsWith(
                    PROCEDURAL_GAS_PREFIX,
                    StringComparison.OrdinalIgnoreCase) ||
                subtype.StartsWith(
                    PROCEDURAL_ICE_PREFIX,
                    StringComparison.OrdinalIgnoreCase);
        }


        private static string NormalizeXmlDeclarationForUtf8(
            string xml)
        {
            if (string.IsNullOrEmpty(xml) ||
                !xml.StartsWith(
                    "<?xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                return xml;
            }

            int declarationEnd =
                xml.IndexOf(
                    "?>",
                    StringComparison.Ordinal);

            if (declarationEnd < 0)
                return xml;

            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                xml.Substring(declarationEnd + 2);
        }


        private static void ValidateCompiledCarriers(
            IEnumerable<string> carrierSubtypes)
        {
            var generators =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .Where(x => x != null)
                    .ToDictionary(
                        x => x.Id.SubtypeName,
                        StringComparer.OrdinalIgnoreCase);

            foreach (string carrierSubtype in carrierSubtypes)
            {
                MyPlanetGeneratorDefinition carrier;

                if (!generators.TryGetValue(
                        carrierSubtype,
                        out carrier))
                {
                    throw new Exception(
                        "Keen's definition reload did not expose generated " +
                        "environment carrier '" +
                        carrierSubtype +
                        "'.");
                }

                if (carrier.EnvironmentDefinition == null)
                {
                    throw new Exception(
                        "Generated environment carrier '" +
                        carrierSubtype +
                        "' loaded but its EnvironmentDefinition was not compiled.");
                }
            }
        }


        private static string BuildCarrierSubtype(
            string ownerGeneratorSubtype)
        {
            return
                CARRIER_PREFIX +
                HashSubtype(
                    ownerGeneratorSubtype)
                .ToString("X16");
        }


        private static ulong HashSubtype(
            string subtype)
        {
            // FNV-1a over invariant uppercase UTF-16 code units. Runtime generator
            // subtypes are already unique; the deterministic hash keeps compiler
            // and visual material identities compact and stable across reloads.
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash =
                offset;

            string normalized =
                subtype.ToUpperInvariant();

            for (int index = 0;
                index < normalized.Length;
                index++)
            {
                char value =
                    normalized[index];

                hash ^= (byte)(value & 0xFF);
                hash *= prime;
                hash ^= (byte)(value >> 8);
                hash *= prime;
            }

            return hash;
        }
    }


    internal sealed class RuntimeGiantVisualAsset
    {
        internal string MaterialSubtype;
        internal string TextureArchivePath;
        internal byte[] TextureBytes;
    }


    internal sealed class RuntimeEnvironmentCompileRequest
    {
        internal string OwnerGeneratorSubtype;
        internal MyObjectBuilder_PlanetGeneratorDefinition Builder;
    }
}
