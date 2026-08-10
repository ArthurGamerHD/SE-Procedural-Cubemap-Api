using Sandbox.ModAPI;

using System;
using System.Collections.Generic;

using VRage.Game;
using VRage.Utils;

using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;

namespace VoxelCubemapApi.Api
{
    /// <summary>
    /// Copyable client wrapper for the Voxel Cubemap inter-mod API.
    /// The wire surface uses only BCL types so delegates can cross mod
    /// assembly boundaries without sharing this assembly's DTO types.
    /// </summary>
    public sealed class VoxelCubemapApiClient
    {
        // Stable ASCII-derived channel ("VCXAPI" + protocol 1). This must not
        // be reused as a client reply channel.
        public const long RegistrationChannel =
            0x5643584150490001L;

        public static readonly Version ClientApiVersion =
            new Version(0, 6, 0);

        private Func<long, ApiData> m_getModificationTemplate;
        private bool m_listening;


        public VoxelCubemapApiClient(
            long replyChannel)
        {
            if (replyChannel == RegistrationChannel)
            {
                throw new ArgumentException(
                    "Reply channel must differ from the registration channel.",
                    "replyChannel");
            }

            ReplyChannel =
                replyChannel;
        }


        public event Action<VoxelCubemapApiClient> Initialized;

        public bool IsReady { get; private set; }
        public Version ServerApiVersion { get; private set; }
        public long ReplyChannel { get; private set; }


        public void Init()
        {
            RequestApi();
        }


        public void RequestApi()
        {
            Listen();

            try
            {
                MyAPIGateway.Utilities.SendModMessage(
                    RegistrationChannel,
                    ReplyChannel);
            }
            catch (Exception e)
            {
                LogWarning(
                    "API request failed",
                    e);
            }
        }


        public ModificationTemplate GetModificationTemplate(
            long planetEntityId)
        {
            Func<long, ApiData> getter =
                m_getModificationTemplate;

            if (getter == null)
                return null;


            try
            {
                ApiData templateApi =
                    getter(
                        planetEntityId);

                return templateApi == null
                    ? null
                    : new ModificationTemplate(
                        templateApi);
            }
            catch (Exception e)
            {
                LogWarning(
                    "GetModificationTemplate failed",
                    e);

                return null;
            }
        }


        public void Close()
        {
            if (m_listening)
            {
                try
                {
                    MyAPIGateway.Utilities.UnregisterMessageHandler(
                        ReplyChannel,
                        OnApiResponse);
                }
                catch (Exception e)
                {
                    LogWarning(
                        "Reply listener removal failed",
                        e);
                }

                m_listening =
                    false;
            }

            m_getModificationTemplate =
                null;

            ServerApiVersion =
                null;

            IsReady =
                false;

            Initialized =
                null;
        }


        private void Listen()
        {
            if (m_listening)
                return;

            MyAPIGateway.Utilities.RegisterMessageHandler(
                ReplyChannel,
                OnApiResponse);

            m_listening =
                true;
        }


        private void OnApiResponse(
            object payload)
        {
            ApiData api =
                payload as ApiData;

            if (api == null)
                return;


            try
            {
                Delegate value;

                Func<Version> getVersion =
                    api.TryGetValue(
                        "GetApiVersion",
                        out value)
                        ? value as Func<Version>
                        : null;

                Func<long, ApiData> getTemplate =
                    api.TryGetValue(
                        "GetModificationTemplate",
                        out value)
                        ? value as Func<long, ApiData>
                        : null;


                if (getVersion == null ||
                    getTemplate == null)
                {
                    throw new Exception(
                        "API response is missing required delegates.");
                }


                Version serverVersion =
                    getVersion();

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


                m_getModificationTemplate =
                    getTemplate;

                ServerApiVersion =
                    serverVersion;

                IsReady =
                    true;


                Action<VoxelCubemapApiClient> handlers =
                    Initialized;

                if (handlers != null)
                {
                    foreach (Action<VoxelCubemapApiClient> handler in
                        handlers.GetInvocationList())
                    {
                        try
                        {
                            handler(
                                this);
                        }
                        catch (Exception e)
                        {
                            LogWarning(
                                "Initialized subscriber failed",
                                e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogWarning(
                    "API response binding failed",
                    e);
            }
        }


        private static void LogWarning(
            string message,
            Exception e)
        {
            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API Client] " +
                message +
                ": " +
                e);
        }


        /// <summary>
        /// Wrapper around one server-owned modification template.
        /// Planet PNGs are exposed as exactly four mutable byte planes. RGB
        /// images use RGBA order; 16-bit height maps use high/low sample bytes
        /// in planes 0/1. Material maps use red for material, green for biome,
        /// and blue for ore.
        /// </summary>
        public sealed class ModificationTemplate
        {
            private readonly Func<long> m_getPlanetEntityId;
            private readonly Func<long> m_getPlanetSeed;
            private readonly Func<string, byte[][]> m_loadPlanetPng;
            private readonly Func<string, int[]> m_getPlanetPngSize;
            private readonly Func<string, int[]> m_getPlanetPngInfo;
            private readonly Func<byte[]> m_getUsedBiomes;
            private readonly Action<string,
                Action<int, int, byte[], byte[], byte[], byte[]>>
                    m_applyPlanetImage;
            private readonly Func<string, byte, float, bool> m_addMaterial;
            private readonly Func<MyPlanetMaterialGroup, byte>
                m_addComplexMaterial;
            private readonly Func<PlanetEnvironmentItemMapping[], int>
                m_addEnvironmentItems;
            private readonly Action<string> m_setEnvironmentDefinition;
            private readonly Func<byte, bool> m_removeMaterial;
            private readonly Action<byte, int> m_applyFractalNoise;
            private readonly Action<byte, byte> m_replaceBiome;
            private readonly Action<byte, int> m_applyBiomeFractalNoise;
            private readonly Action<Action<bool, string>> m_push;
            private readonly Action m_close;


            internal ModificationTemplate(
                ApiData api)
            {
                if (api == null)
                    throw new ArgumentNullException("api");


                Delegate value;

                m_getPlanetEntityId =
                    GetRequired<Func<long>>(
                        api,
                        "GetPlanetEntityId",
                        out value);

                m_getPlanetSeed =
                    GetRequired<Func<long>>(
                        api,
                        "GetPlanetSeed",
                        out value);

                m_loadPlanetPng =
                    GetRequired<Func<string, byte[][]>>(
                        api,
                        "LoadPlanetPng",
                        out value);

                m_getPlanetPngSize =
                    GetRequired<Func<string, int[]>>(
                        api,
                        "GetPlanetPngSize",
                        out value);

                m_getPlanetPngInfo =
                    GetRequired<Func<string, int[]>>(
                        api,
                        "GetPlanetPngInfo",
                        out value);

                m_getUsedBiomes =
                    GetRequired<Func<byte[]>>(
                        api,
                        "GetUsedBiomes",
                        out value);

                m_applyPlanetImage =
                    GetRequired<Action<string,
                        Action<int, int, byte[], byte[], byte[], byte[]>>>(
                            api,
                            "ApplyPlanetImage",
                            out value);

                m_addMaterial =
                    GetRequired<Func<string, byte, float, bool>>(
                        api,
                        "AddMaterial",
                        out value);

                m_addComplexMaterial =
                    GetRequired<Func<MyPlanetMaterialGroup, byte>>(
                        api,
                        "AddComplexMaterial",
                        out value);

                m_addEnvironmentItems =
                    GetRequired<Func<PlanetEnvironmentItemMapping[], int>>(
                        api,
                        "AddEnvironmentItems",
                        out value);

                m_setEnvironmentDefinition =
                    GetRequired<Action<string>>(
                        api,
                        "SetEnvironmentDefinition",
                        out value);

                m_removeMaterial =
                    GetRequired<Func<byte, bool>>(
                        api,
                        "RemoveMaterial",
                        out value);

                m_applyFractalNoise =
                    GetRequired<Action<byte, int>>(
                        api,
                        "ApplyFractalNoise",
                        out value);

                m_replaceBiome =
                    GetRequired<Action<byte, byte>>(
                        api,
                        "ReplaceBiome",
                        out value);

                m_applyBiomeFractalNoise =
                    GetRequired<Action<byte, int>>(
                        api,
                        "ApplyBiomeFractalNoise",
                        out value);

                m_push =
                    GetRequired<Action<Action<bool, string>>>(
                        api,
                        "Push",
                        out value);

                m_close =
                    GetRequired<Action>(
                        api,
                        "Close",
                        out value);
            }


            public long PlanetEntityId
            {
                get { return m_getPlanetEntityId(); }
            }

            public long PlanetSeed
            {
                get { return m_getPlanetSeed(); }
            }


            public byte[][] LoadPlanetPng(
                string faceFileName)
            {
                byte[][] planes =
                    m_loadPlanetPng(
                        faceFileName);

                if (planes == null ||
                    planes.Length != 4)
                {
                    throw new Exception(
                        "LoadPlanetPng must return exactly four byte planes."
                    );
                }

                return planes;
            }


            public int[] GetPlanetPngSize(
                string faceFileName)
            {
                return m_getPlanetPngSize(
                    faceFileName);
            }


            /// <summary>
            /// Returns width, height, PNG bit depth, and PNG color type.
            /// For 16-bit grayscale height maps, planes 0/1 are the high/low
            /// sample bytes. For 8-bit RGB/RGBA maps, planes are RGBA.
            /// </summary>
            public int[] GetPlanetPngInfo(
                string faceFileName)
            {
                return m_getPlanetPngInfo(
                    faceFileName);
            }


            /// <summary>
            /// Returns the sorted distinct biome IDs currently present in the
            /// green channel of the six material-map PNGs.
            /// </summary>
            public byte[] GetUsedBiomes()
            {
                return m_getUsedBiomes();
            }


            public void ApplyPlanetImage(
                string faceFileName,
                Action<int, int, byte[], byte[], byte[], byte[]> transform)
            {
                // Queued now and invoked by the server's background Push worker.
                // The transform must only touch these arrays; it must not call
                // simulation-thread-only game APIs.
                m_applyPlanetImage(
                    faceFileName,
                    transform);
            }


            public bool AddMaterial(
                string materialSubtype,
                byte mapValue,
                float maxDepth)
            {
                return m_addMaterial(
                    materialSubtype,
                    mapValue,
                    maxDepth);
            }


            public bool RemoveMaterial(
                byte mapValue)
            {
                return m_removeMaterial(
                    mapValue);
            }


            /// <summary>
            /// Clones a complex material group into the template and returns a
            /// map value that is unused by all six source material maps and the
            /// template definitions. Push revalidates it before committing.
            /// </summary>
            public byte AddComplexMaterial(
                MyPlanetMaterialGroup materialGroup)
            {
                return m_addComplexMaterial(
                    materialGroup);
            }


            /// <summary>
            /// Appends client-authored vegetation/environment mappings to the
            /// cloned planet definition and returns the number added. Each
            /// mapping can target biome IDs, voxel-material subtypes, placement
            /// ranges, and one or more weighted environment items.
            /// </summary>
            public int AddEnvironmentItems(
                PlanetEnvironmentItemMapping[] mappings)
            {
                return m_addEnvironmentItems(
                    mappings);
            }


            /// <summary>
            /// Selects a caller-owned, normally loaded WorldEnvironmentDefinition
            /// through a tiny PlanetGeneratorDefinition carrier. The carrier must
            /// be declared in Data/*.sbc and its Environment field must reference
            /// the caller's procedural environment definition.
            /// </summary>
            public void SetEnvironmentDefinition(
                string carrierPlanetGeneratorSubtype)
            {
                m_setEnvironmentDefinition(
                    carrierPlanetGeneratorSubtype);
            }


            /// <summary>
            /// Applies the server's seamless planet-space fractal field to all
            /// six material PNGs during Push. Selected pixels receive mapValue.
            /// </summary>
            public void ApplyFractalNoise(
                byte mapValue,
                int coveragePercent)
            {
                m_applyFractalNoise(
                    mapValue,
                    coveragePercent);
            }


            /// <summary>
            /// Replaces every occurrence of one biome ID with another across
            /// all six material maps during Push.
            /// </summary>
            public void ReplaceBiome(
                byte sourceBiome,
                byte targetBiome)
            {
                m_replaceBiome(
                    sourceBiome,
                    targetBiome);
            }


            /// <summary>
            /// Applies the server's seamless planet-space fractal field to the
            /// biome channel. Selected pixels receive biomeValue during Push.
            /// </summary>
            public void ApplyBiomeFractalNoise(
                byte biomeValue,
                int coveragePercent)
            {
                m_applyBiomeFractalNoise(
                    biomeValue,
                    coveragePercent);
            }


            public void Push(
                Action<bool, string> callback)
            {
                m_push(
                    callback);
            }


            public void Close()
            {
                m_close();
            }


            private static T GetRequired<T>(
                ApiData api,
                string name,
                out Delegate value)
                where T : class
            {
                T result =
                    api.TryGetValue(
                        name,
                        out value)
                        ? value as T
                        : null;

                if (result == null)
                {
                    throw new Exception(
                        "Modification template is missing delegate '" +
                        name +
                        "'.");
                }

                return result;
            }
        }
    }
}
