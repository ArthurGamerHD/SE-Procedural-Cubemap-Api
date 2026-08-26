using System;
using System.Collections.Generic;
using Adk.Compression.Zip;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using VRage;
using VRage.Game;
using VRage.Game.ObjectBuilders;

namespace ProceduralCubemapApi.Common.PlanetModification.Runtime
{
    internal static class RuntimeArchiveLayout
    {
        private const string PLANET_DATA_PREFIX =
            "Data/PlanetDataFiles/";


        internal static string GetProviderPath(
            string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                throw new ArgumentException(
                    "Runtime archive path cannot be empty.",
                    nameof(archivePath));
            }

            string normalized =
                archivePath.Replace('\\', '/').TrimEnd('/');

            int separator =
                normalized.LastIndexOf('/');

            string providerPath =
                separator < 0
                    ? normalized
                    : normalized.Substring(separator + 1);

            if (string.IsNullOrWhiteSpace(providerPath) ||
                providerPath == "." ||
                providerPath == "..")
            {
                throw new ArgumentException(
                    "Runtime archive has no usable file name: " +
                    archivePath,
                    nameof(archivePath));
            }

            return providerPath;
        }


        internal static MyObjectBuilder_PlanetTextureMapProvider
            CreateTextureMapProvider(
                string archivePath)
        {
            return new MyObjectBuilder_PlanetTextureMapProvider
            {
                Path = GetProviderPath(
                    archivePath)
            };
        }


        internal static void BindBuilder(
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            string archivePath)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            string providerPath =
                GetProviderPath(
                    archivePath);

            // Keep the builder portable. The rooted archive is supplied by the
            // synthetic MyModContext used when the runtime definition is built.
            builder.FolderName =
                providerPath;

            builder.MapProvider =
                new MyObjectBuilder_PlanetTextureMapProvider
                {
                    Path = providerPath
                };
        }


        internal static string BuildPlanetEntryPath(
            string archivePath,
            string logicalName)
        {
            if (string.IsNullOrWhiteSpace(logicalName))
            {
                throw new ArgumentException(
                    "Runtime archive entry name cannot be empty.",
                    nameof(logicalName));
            }

            return
                PLANET_DATA_PREFIX +
                GetProviderPath(archivePath) +
                "/" +
                logicalName.Replace('\\', '/').TrimStart('/');
        }


        internal static byte[] PrepareProviderArchive(
            string archivePath,
            byte[] contents)
        {
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));

            if (!archivePath.EndsWith(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                return contents;
            }

            List<MinimalZip.Entry> source;
            string comment;

            using (var stream =
                new ResetableMemoryStream(
                    contents,
                    contents.Length))
            {
                source =
                    MinimalZip.Read(
                        stream);

                MinimalZip.TryReadComment(
                    stream,
                    out comment);
            }

            var prepared =
                new List<MinimalZip.Entry>(
                    source.Count);

            for (int index = 0;
                index < source.Count;
                index++)
            {
                MinimalZip.Entry entry =
                    source[index];

                string logicalName =
                    GetLogicalEntryName(
                        entry.Name);

                string storedName =
                    string.Equals(
                        logicalName,
                        RuntimeProceduralCache.ARCHIVE_MANIFEST_FILE,
                        StringComparison.OrdinalIgnoreCase)
                        ? RuntimeProceduralCache.ARCHIVE_MANIFEST_FILE
                        : BuildPlanetEntryPath(
                            archivePath,
                            logicalName);

                prepared.Add(
                    new MinimalZip.Entry(
                        storedName,
                        entry.Data,
                        entry.CreationTime,
                        entry.Compression));
            }

            return MinimalZip.WriteBytes(
                prepared,
                comment);
        }


        internal static string GetLogicalEntryName(
            string storedName)
        {
            if (string.IsNullOrWhiteSpace(storedName))
                return storedName;

            string normalized =
                storedName.Replace('\\', '/').TrimStart('/');

            if (!normalized.StartsWith(
                    PLANET_DATA_PREFIX,
                    StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string providerAndName =
                normalized.Substring(
                    PLANET_DATA_PREFIX.Length);

            int providerSeparator =
                providerAndName.IndexOf('/');

            return providerSeparator < 0
                ? providerAndName
                : providerAndName.Substring(providerSeparator + 1);
        }
    }
}
