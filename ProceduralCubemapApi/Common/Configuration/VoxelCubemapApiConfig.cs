using System;
using System.IO;
using Sandbox.ModAPI;
using VRage.Utils;

namespace ProceduralCubemapApi.Common.Configuration
{
    public sealed class CubemapApiConfig
    {
        public bool PersistentCache = true;
    }


    internal static class CubemapApiConfigStorage
    {
        private const string CONFIG_FILE =
            "CubemapApiConfig.xml";


        internal static CubemapApiConfig LoadOrCreate()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(
                    CONFIG_FILE,
                    typeof(CubemapApiServer)))
                {
                    var created =
                        new CubemapApiConfig();

                    Save(
                        created);

                    return created;
                }


                string xml;

                using (TextReader reader =
                    MyAPIGateway.Utilities.ReadFileInLocalStorage(
                        CONFIG_FILE,
                        typeof(CubemapApiServer)))
                {
                    xml =
                        reader.ReadToEnd();
                }


                CubemapApiConfig config =
                    MyAPIGateway.Utilities
                        .SerializeFromXML<CubemapApiConfig>(
                            xml);

                if (config == null)
                {
                    throw new Exception(
                        "Configuration deserialized to null.");
                }


                return config;
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Could not load " +
                    CONFIG_FILE +
                    "; using defaults for this session: " +
                    e);

                return new CubemapApiConfig();
            }
        }


        private static void Save(
            CubemapApiConfig config)
        {
            string xml =
                MyAPIGateway.Utilities
                    .SerializeToXML<CubemapApiConfig>(
                        config);

            using (TextWriter writer =
                MyAPIGateway.Utilities.WriteFileInLocalStorage(
                    CONFIG_FILE,
                    typeof(CubemapApiServer)))
            {
                writer.Write(
                    xml);
            }
        }
    }
}
