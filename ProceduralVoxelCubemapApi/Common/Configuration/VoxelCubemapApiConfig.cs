using System;
using System.IO;
using Sandbox.ModAPI;
using VRage.Utils;

namespace VoxelCubemapApi.Common.Configuration
{
    public sealed class VoxelCubemapApiConfig
    {
        public bool PersistentCache = true;
    }


    internal static class VoxelCubemapApiConfigStorage
    {
        private const string CONFIG_FILE =
            "VoxelCubemapApiConfig.xml";


        internal static VoxelCubemapApiConfig LoadOrCreate()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(
                    CONFIG_FILE,
                    typeof(VoxelCubemapApiServer)))
                {
                    var created =
                        new VoxelCubemapApiConfig();

                    Save(
                        created);

                    return created;
                }


                string xml;

                using (TextReader reader =
                    MyAPIGateway.Utilities.ReadFileInLocalStorage(
                        CONFIG_FILE,
                        typeof(VoxelCubemapApiServer)))
                {
                    xml =
                        reader.ReadToEnd();
                }


                VoxelCubemapApiConfig config =
                    MyAPIGateway.Utilities
                        .SerializeFromXML<VoxelCubemapApiConfig>(
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

                return new VoxelCubemapApiConfig();
            }
        }


        private static void Save(
            VoxelCubemapApiConfig config)
        {
            string xml =
                MyAPIGateway.Utilities
                    .SerializeToXML<VoxelCubemapApiConfig>(
                        config);

            using (TextWriter writer =
                MyAPIGateway.Utilities.WriteFileInLocalStorage(
                    CONFIG_FILE,
                    typeof(VoxelCubemapApiServer)))
            {
                writer.Write(
                    xml);
            }
        }
    }
}
