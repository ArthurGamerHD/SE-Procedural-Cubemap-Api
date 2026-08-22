using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adk.Compression.Zip;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VoxelCubemapApi.Common.Configuration;
using VoxelCubemapApi.Common.Noise;
using VoxelCubemapApi.Common.PlanetModification.Runtime;
using VoxelCubemapApi.Common.PlanetModification.World;
using VRage.Game;
using VRage.Utils;

namespace VoxelCubemapApi.Common.PlanetModification.Persistence
{
    internal sealed class RuntimePackageStore
    {
        private const string RUNTIME_SETTINGS_FILE =
            "settings.xml";

        private const string PERSISTENCE_VARIABLE_PREFIX =
            "VoxelCubemapApi.RuntimePersistence.v1.";

        private const string RECIPE_VARIABLE_PREFIX =
            "VoxelCubemapApi.RuntimePersistence.v2.Recipe.";

        private const string RUNTIME_SETTINGS_VARIABLE =
            PERSISTENCE_VARIABLE_PREFIX +
            "SettingsXml";

        private const string PERSISTENCE_MANIFEST_VARIABLE =
            PERSISTENCE_VARIABLE_PREFIX +
            "ManifestXml";

        // Utility variables must keep binary archives as ordinary Base64 strings
        // because Keen's checkpoint XML reader cannot consume typed byte arrays.
        private const int ARCHIVE_CHUNK_SIZE_BYTES =
            4 * 1024 * 1024;

        private const int MAX_ARCHIVE_CHUNK_COUNT =
            512;

        private const int MAX_RUNTIME_ARCHIVE_BYTES =
            512 * 1024 * 1024;

        private readonly VoxelCubemapApiServer _server;
        private readonly VoxelCubemapApiConfig _config;
        private readonly object _persistenceSync =
            new object();

        private readonly HashSet<string> _worldStorageCacheFiles =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        private RuntimePersistenceManifest _manifest =
            new RuntimePersistenceManifest();


        internal RuntimePackageStore(
            VoxelCubemapApiServer server,
            VoxelCubemapApiConfig config)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _server =
                server;

            _config =
                config;
        }


        internal RuntimePlanetGeneratorSettings Settings { get; private set; } =
            new RuntimePlanetGeneratorSettings();

        internal Dictionary<string, MyPlanetGeneratorDefinition> Generators { get; } =
            new Dictionary<string, MyPlanetGeneratorDefinition>(
                StringComparer.OrdinalIgnoreCase);

        internal string BoundSavePath { get; set; }

        internal Func<RuntimePlanetBuilderEntry,
            RuntimeProceduralPlanetRecipe,
            byte[]> ProceduralArchiveBuilder { get; set; }

        internal Func<RuntimePlanetBuilderEntry,
            RuntimeProceduralPlanetRecipe,
            string> ProceduralGeneratorSignatureBuilder { get; set; }


        internal void LoadPersistedRuntimeGenerators()
        {
            bool migrateLegacyWorldStorage;

            Settings =
                LoadRuntimeSettings(
                    out migrateLegacyWorldStorage);


            if (Settings.PlanetBuilders == null)
            {
                Settings.PlanetBuilders =
                    new List<RuntimePlanetBuilderEntry>();
            }


            for (int i = 0;
                i < Settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry entry =
                    Settings.PlanetBuilders[i];


                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.Subtype) ||
                    string.IsNullOrWhiteSpace(entry.GeneratorFile) ||
                    string.IsNullOrWhiteSpace(entry.ArchiveFile))
                {
                    throw new Exception(
                        "Persisted settings contain an invalid runtime planet entry.");
                }

                RuntimePlanetPersistenceType persistenceType =
                    GetPersistenceType(
                        entry);

                if (persistenceType ==
                        RuntimePlanetPersistenceType.Procedural &&
                    entry.RecipeSchemaVersion != 1)
                {
                    throw new Exception(
                        "Persisted procedural runtime entry has an unsupported " +
                        "recipe schema: " +
                        entry.RecipeSchemaVersion);
                }
            }


            _manifest =
                LoadPersistenceManifest();

            CleanupAbandonedPersistencePackages();
            SeedPersistenceManifestFromSettings();


            if (Settings.PlanetBuilders.Count == 0)
                return;


            RecreateWorldStorageCache(
                migrateLegacyWorldStorage);


            string savePath =
                _server.ResolveInitialSavePath();


            for (int i = 0;
                i < Settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry entry =
                    Settings.PlanetBuilders[i];


                MyPlanetGeneratorDefinition generator =
                    LoadAndRegisterPersistedRuntimeGenerator(
                        entry,
                        savePath);


                Generators[
                    entry.Subtype] =
                    generator;
            }


            BoundSavePath =
                savePath;


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Loaded persisted runtime generators: " +
                Generators.Count);
        }


        internal RuntimePlanetGeneratorSettings LoadRuntimeSettings(
            out bool migratedLegacyWorldStorage)
        {
            migratedLegacyWorldStorage =
                false;


            string xml;

            if (!MyAPIGateway.Utilities.GetVariable<string>(
                RUNTIME_SETTINGS_VARIABLE,
                out xml))
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(
                    RUNTIME_SETTINGS_FILE,
                    typeof(VoxelCubemapApiServer)))
                {
                    return new RuntimePlanetGeneratorSettings();
                }


                using (TextReader reader =
                    MyAPIGateway.Utilities.ReadFileInWorldStorage(
                        RUNTIME_SETTINGS_FILE,
                        typeof(VoxelCubemapApiServer)))
                {
                    xml =
                        reader.ReadToEnd();
                }


                MyAPIGateway.Utilities.SetVariable(
                    RUNTIME_SETTINGS_VARIABLE,
                    xml);

                migratedLegacyWorldStorage =
                    true;

                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Migrating persistence from " +
                    "WorldStorage to session variables.");
            }


            if (string.IsNullOrWhiteSpace(xml))
                return new RuntimePlanetGeneratorSettings();


            RuntimePlanetGeneratorSettings settings =
                MyAPIGateway.Utilities
                    .SerializeFromXML<RuntimePlanetGeneratorSettings>(
                        xml);


            if (settings == null)
            {
                throw new Exception(
                    "Could not deserialize " +
                    RUNTIME_SETTINGS_FILE +
                    ".");
            }


            return settings;
        }


        internal void SaveRuntimeSettings()
        {
            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();


                string xml =
                    MyAPIGateway.Utilities
                        .SerializeToXML<RuntimePlanetGeneratorSettings>(
                            Settings);


                MyAPIGateway.Utilities.SetVariable(
                    RUNTIME_SETTINGS_VARIABLE,
                    xml);

                WriteWorldStorageTextCache(
                    RUNTIME_SETTINGS_FILE,
                    xml);
            }
        }


        internal void SaveGeneratorBuilder(
            string fileName,
            MyObjectBuilder_PlanetGeneratorDefinition builder)
        {
            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();


                string xml =
                    MyAPIGateway.Utilities
                        .SerializeToXML<MyObjectBuilder_PlanetGeneratorDefinition>(
                            builder);


                MyAPIGateway.Utilities.SetVariable(
                    BuildGeneratorVariableName(
                        fileName),
                    xml);

                WriteWorldStorageTextCache(
                    fileName,
                    xml);
            }
        }


        internal void RecreateWorldStorageCache(
            bool allowLegacyMigration)
        {
            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();

                WriteWorldStorageTextCache(
                    RUNTIME_SETTINGS_FILE,
                    MyAPIGateway.Utilities
                        .SerializeToXML(
                            Settings));


                foreach (var entry in Settings.PlanetBuilders)
                {
                    RestoreGeneratorCache(
                        entry.GeneratorFile,
                        allowLegacyMigration);

                    if (GetPersistenceType(entry) ==
                        RuntimePlanetPersistenceType.Procedural)
                    {
                        if (ProceduralArchiveBuilder == null)
                        {
                            throw new Exception(
                                "Procedural archive reconstruction is not configured.");
                        }

                        RuntimeProceduralPlanetRecipe recipe = LoadRuntimeRecipe(entry);

                        if (!_config.PersistentCache)
                        {
                            MyLog.Default.Log(MyLogSeverity.Info, "[RuntimePlanetGenerator] Persistent cache is disabled, rebuilding: " + entry.ArchiveFile + " (" + entry.Subtype + ").");
                        }
                        else if (TryUsePersistentProceduralCache(entry, recipe))
                            continue;

                        byte[] archive = ProceduralArchiveBuilder(entry, recipe);

                        WriteWorldStorageBinaryCache(entry.ArchiveFile, archive);
                    }
                    else
                    {
                        RestoreArchiveCache(
                            entry.ArchiveFile,
                            allowLegacyMigration);
                    }
                }
            }
        }


        private bool TryUsePersistentProceduralCache(
            RuntimePlanetBuilderEntry entry,
            RuntimeProceduralPlanetRecipe recipe)
        {
            if (entry == null ||
                recipe == null ||
                string.IsNullOrWhiteSpace(entry.ArchiveFile))
            {
                return false;
            }

            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(
                entry.ArchiveFile,
                typeof(VoxelCubemapApiServer)))
            {
                LogPersistentCacheMiss(
                    entry,
                    "archive is missing");

                return false;
            }

            if (ProceduralGeneratorSignatureBuilder == null)
            {
                throw new Exception(
                    "Procedural generator signature reconstruction is not configured.");
            }

            try
            {
                using (BinaryReader reader =
                    MyAPIGateway.Utilities.ReadBinaryFileInWorldStorage(
                        entry.ArchiveFile,
                        typeof(VoxelCubemapApiServer)))
                {
                    Stream stream =
                        reader.BaseStream;

                    string comment;

                    if (!MinimalZip.TryReadComment(
                        stream,
                        out comment) ||
                        !string.Equals(
                            comment,
                            RuntimeProceduralCache.ZipComment,
                            StringComparison.Ordinal))
                    {
                        LogPersistentCacheMiss(
                            entry,
                            "cache GUID comment mismatch");

                        return false;
                    }

                    byte[] manifestBytes;

                    if (!MinimalZip.TryReadEntry(
                        stream,
                        RuntimeProceduralCache.ARCHIVE_MANIFEST_FILE,
                        out manifestBytes,
                        false))
                    {
                        LogPersistentCacheMiss(
                            entry,
                            "cache manifest is missing");

                        return false;
                    }

                    string manifestXml =
                        Encoding.UTF8.GetString(
                            manifestBytes);

                    RuntimeProceduralCacheManifest manifest =
                        MyAPIGateway.Utilities
                            .SerializeFromXML<RuntimeProceduralCacheManifest>(
                                manifestXml);

                    if (manifest == null ||
                        !string.Equals(
                            manifest.CacheGuid,
                            RuntimeProceduralCache.CACHE_GUID,
                            StringComparison.Ordinal))
                    {
                        LogPersistentCacheMiss(
                            entry,
                            "cache manifest GUID mismatch");

                        return false;
                    }

                    string recipeSignature =
                        RuntimeProceduralCache.ComputeRecipeSignature(
                            recipe);

                    if (!string.Equals(
                        manifest.RecipeSignature,
                        recipeSignature,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        LogPersistentCacheMiss(
                            entry,
                            "procedural recipe changed");

                        return false;
                    }

                    string generatorSignature =
                        ProceduralGeneratorSignatureBuilder(
                            entry,
                            recipe);

                    if (!string.Equals(
                        manifest.GeneratorSignature,
                        generatorSignature,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        LogPersistentCacheMiss(
                            entry,
                            "source/runtime generator changed");

                        return false;
                    }
                }

                _worldStorageCacheFiles.Add(
                    entry.ArchiveFile);

                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Persistent cache hit: " +
                    entry.ArchiveFile +
                    " (" +
                    entry.Subtype +
                    ").");

                return true;
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Persistent cache miss for '" +
                    entry.ArchiveFile +
                    "': cache validation failed: " +
                    e.Message);

                return false;
            }
        }


        private static void LogPersistentCacheMiss(
            RuntimePlanetBuilderEntry entry,
            string reason)
        {
            MyLog.Default.WriteLineAndConsole("[RuntimePlanetGenerator] Persistent cache miss for '" + entry.ArchiveFile + "': " + reason + ".");
        }


        internal void RestoreGeneratorCache(
            string fileName,
            bool allowLegacyMigration)
        {
            string xml;

            if (!MyAPIGateway.Utilities.GetVariable<string>(
                BuildGeneratorVariableName(
                    fileName),
                out xml))
            {
                if (!allowLegacyMigration ||
                    !MyAPIGateway.Utilities.FileExistsInWorldStorage(
                        fileName,
                        typeof(VoxelCubemapApiServer)))
                {
                    throw new Exception(
                        "Missing persisted runtime generator variable: " +
                        fileName);
                }


                using (TextReader reader =
                    MyAPIGateway.Utilities.ReadFileInWorldStorage(
                        fileName,
                        typeof(VoxelCubemapApiServer)))
                {
                    xml =
                        reader.ReadToEnd();
                }


                MyAPIGateway.Utilities.SetVariable(
                    BuildGeneratorVariableName(
                        fileName),
                    xml);
            }


            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new Exception(
                    "Persisted runtime generator variable is empty: " +
                    fileName);
            }


            WriteWorldStorageTextCache(
                fileName,
                xml);
        }


        internal void RestoreArchiveCache(
            string fileName,
            bool allowLegacyMigration)
        {
            byte[] archive;

            if (!TryLoadRuntimeArchiveVariables(
                fileName,
                out archive))
            {
                if (!allowLegacyMigration ||
                    !MyAPIGateway.Utilities.FileExistsInWorldStorage(
                        fileName,
                        typeof(VoxelCubemapApiServer)))
                {
                    throw new Exception(
                        "Missing persisted runtime archive variables: " +
                        fileName);
                }


                using (BinaryReader reader =
                    MyAPIGateway.Utilities.ReadBinaryFileInWorldStorage(
                        fileName,
                        typeof(VoxelCubemapApiServer)))
                {
                    archive =
                        BinaryData.ReadAll(
                            reader);
                }


                SaveRuntimeArchiveVariables(
                    fileName,
                    archive);
            }


            WriteWorldStorageBinaryCache(
                fileName,
                archive);
        }


        internal void SaveRuntimeArchive(
            string fileName,
            byte[] archive)
        {
            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();

                SaveRuntimeArchiveVariables(
                    fileName,
                    archive);

                WriteWorldStorageBinaryCache(
                    fileName,
                    archive);
            }
        }


        internal void SaveDerivedRuntimeArchive(
            string fileName,
            byte[] archive)
        {
            if (archive == null ||
                archive.Length == 0 ||
                archive.Length > MAX_RUNTIME_ARCHIVE_BYTES)
            {
                throw new ArgumentException(
                    "Derived runtime archive has an invalid size.",
                    nameof(archive));
            }

            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();

                WriteWorldStorageBinaryCache(
                    fileName,
                    archive);
            }
        }


        internal void SaveRuntimeRecipe(
            RuntimePlanetBuilderEntry entry,
            RuntimeProceduralPlanetRecipe recipe)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();

                ValidateRuntimeRecipe(
                    recipe,
                    entry);

                string variableName =
                    ResolveRecipeVariableName(
                        entry);

                string xml =
                    MyAPIGateway.Utilities
                        .SerializeToXML<RuntimeProceduralPlanetRecipe>(
                            recipe);

                if (string.IsNullOrWhiteSpace(xml))
                    throw new Exception("Serialized procedural recipe is empty.");

                MyAPIGateway.Utilities.SetVariable(
                    variableName,
                    xml);
            }
        }


        internal RuntimeProceduralPlanetRecipe LoadRuntimeRecipe(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            string variableName =
                ResolveRecipeVariableName(
                    entry);

            string xml;

            if (!MyAPIGateway.Utilities.GetVariable<string>(
                    variableName,
                    out xml) ||
                string.IsNullOrWhiteSpace(xml))
            {
                throw new Exception(
                    "Missing persisted procedural recipe variable: " +
                    variableName);
            }

            RuntimeProceduralPlanetRecipe recipe =
                MyAPIGateway.Utilities
                    .SerializeFromXML<RuntimeProceduralPlanetRecipe>(
                        xml);

            ValidateRuntimeRecipe(
                recipe,
                entry);

            return recipe;
        }


        internal void SaveRuntimeArchiveVariables(
            string fileName,
            byte[] archive)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));

            if (archive.Length > MAX_RUNTIME_ARCHIVE_BYTES)
            {
                throw new ArgumentException(
                    "Runtime archive exceeds the persistence size limit.",
                    nameof(archive));
            }


            string chunkCountVariable =
                BuildArchiveChunkCountVariableName(
                    fileName);

            int previousChunkCount;

            bool hadPreviousChunkCount =
                MyAPIGateway.Utilities.GetVariable<int>(
                    chunkCountVariable,
                    out previousChunkCount);

            RuntimePersistencePackageEntry manifestPackage =
                FindPersistenceManifestPackage(
                    fileName);

            if (!hadPreviousChunkCount)
            {
                previousChunkCount =
                    manifestPackage == null
                        ? 0
                        : manifestPackage.ChunkCount;
            }


            ValidateArchiveChunkCount(
                previousChunkCount,
                fileName);


            int previousArchiveLength;

            bool hadPreviousArchiveLength =
                MyAPIGateway.Utilities.GetVariable<int>(
                    BuildArchiveLengthVariableName(
                        fileName),
                    out previousArchiveLength);

            var previousChunks =
                new List<string>(
                    previousChunkCount);


            for (int chunkIndex = 0;
                chunkIndex < previousChunkCount;
                chunkIndex++)
            {
                string previousChunk;

                if (!MyAPIGateway.Utilities.GetVariable<string>(
                    BuildArchiveChunkVariableName(
                        fileName,
                        chunkIndex),
                    out previousChunk) ||
                    previousChunk == null)
                {
                    throw new Exception(
                        "Cannot safely rewrite runtime archive '" +
                        fileName +
                        "' because previous chunk " +
                        chunkIndex +
                        " is missing.");
                }


                previousChunks.Add(
                    previousChunk);
            }


            int chunkCount =
                (int)(((long)archive.Length +
                    ARCHIVE_CHUNK_SIZE_BYTES -
                    1) /
                    ARCHIVE_CHUNK_SIZE_BYTES);

            ValidateArchiveChunkCount(
                chunkCount,
                fileName);


            int previousManifestChunkCount =
                manifestPackage == null
                    ? 0
                    : manifestPackage.ChunkCount;

            if (manifestPackage != null)
            {
                manifestPackage.ChunkCount =
                    chunkCount;

                SavePersistenceManifest();
            }


            RemoveRuntimeArchiveVariableRange(
                fileName,
                previousChunkCount);


            int writtenChunkCount =
                0;


            try
            {
                for (int chunkIndex = 0;
                    chunkIndex < chunkCount;
                    chunkIndex++)
                {
                    int offset =
                        chunkIndex *
                        ARCHIVE_CHUNK_SIZE_BYTES;

                    int length =
                        Math.Min(
                            ARCHIVE_CHUNK_SIZE_BYTES,
                            archive.Length - offset);

                    string chunk =
                        Convert.ToBase64String(
                            archive,
                            offset,
                            length);

                    MyAPIGateway.Utilities.SetVariable(
                        BuildArchiveChunkVariableName(
                            fileName,
                            chunkIndex),
                        chunk);

                    writtenChunkCount++;
                }


                MyAPIGateway.Utilities.SetVariable(
                    BuildArchiveLengthVariableName(
                        fileName),
                    archive.Length);

                MyAPIGateway.Utilities.SetVariable(
                    chunkCountVariable,
                    chunkCount);
            }
            catch
            {
                RemoveRuntimeArchiveVariableRange(
                    fileName,
                    writtenChunkCount);


                for (int chunkIndex = 0;
                    chunkIndex < previousChunks.Count;
                    chunkIndex++)
                {
                    MyAPIGateway.Utilities.SetVariable(
                        BuildArchiveChunkVariableName(
                            fileName,
                            chunkIndex),
                        previousChunks[chunkIndex]);
                }


                if (hadPreviousArchiveLength)
                {
                    MyAPIGateway.Utilities.SetVariable(
                        BuildArchiveLengthVariableName(
                            fileName),
                        previousArchiveLength);
                }

                if (hadPreviousChunkCount)
                {
                    MyAPIGateway.Utilities.SetVariable(
                        chunkCountVariable,
                        previousChunkCount);
                }


                if (manifestPackage != null)
                {
                    manifestPackage.ChunkCount =
                        previousManifestChunkCount;

                    SavePersistenceManifest();
                }


                throw;
            }
        }


        internal bool TryLoadRuntimeArchiveVariables(
            string fileName,
            out byte[] archive)
        {
            archive =
                null;


            int chunkCount;

            if (!MyAPIGateway.Utilities.GetVariable<int>(
                BuildArchiveChunkCountVariableName(
                    fileName),
                out chunkCount))
            {
                return false;
            }


            int archiveLength;

            if (!MyAPIGateway.Utilities.GetVariable<int>(
                    BuildArchiveLengthVariableName(
                        fileName),
                    out archiveLength) ||
                archiveLength < 0 ||
                archiveLength > MAX_RUNTIME_ARCHIVE_BYTES ||
                chunkCount < 0)
            {
                throw new Exception(
                    "Invalid runtime archive variable metadata: " +
                    fileName);
            }


            int expectedChunkCount =
                (int)(((long)archiveLength +
                    ARCHIVE_CHUNK_SIZE_BYTES -
                    1) /
                    ARCHIVE_CHUNK_SIZE_BYTES);

            if (chunkCount != expectedChunkCount)
            {
                throw new Exception(
                    "Runtime archive variable chunk count does not match " +
                    "its stored length: " +
                    fileName);
            }


            archive =
                new byte[archiveLength];


            for (int chunkIndex = 0;
                chunkIndex < chunkCount;
                chunkIndex++)
            {
                string encodedChunk;

                if (!MyAPIGateway.Utilities.GetVariable<string>(
                    BuildArchiveChunkVariableName(
                        fileName,
                        chunkIndex),
                    out encodedChunk) ||
                    string.IsNullOrEmpty(encodedChunk))
                {
                    throw new Exception(
                        "Missing runtime archive variable chunk " +
                        chunkIndex +
                        " for " +
                        fileName);
                }


                byte[] chunk;

                try
                {
                    chunk =
                        Convert.FromBase64String(
                            encodedChunk);
                }
                catch (FormatException e)
                {
                    throw new Exception(
                        "Runtime archive variable chunk " +
                        chunkIndex +
                        " is not valid Base64 for " +
                        fileName,
                        e);
                }


                int offset =
                    chunkIndex *
                    ARCHIVE_CHUNK_SIZE_BYTES;

                int expectedLength =
                    Math.Min(
                        ARCHIVE_CHUNK_SIZE_BYTES,
                        archiveLength - offset);

                if (chunk.Length != expectedLength)
                {
                    throw new Exception(
                        "Invalid runtime archive variable chunk length " +
                        chunkIndex +
                        " for " +
                        fileName);
                }


                Buffer.BlockCopy(
                    chunk,
                    0,
                    archive,
                    offset,
                    chunk.Length);
            }


            return true;
        }


        internal static string BuildGeneratorVariableName(
            string fileName)
        {
            return
                PERSISTENCE_VARIABLE_PREFIX +
                "GeneratorXml." +
                fileName;
        }


        internal static string BuildRecipeVariableName(
            string fileName)
        {
            return
                RECIPE_VARIABLE_PREFIX +
                fileName;
        }


        internal static string BuildArchiveChunkCountVariableName(
            string fileName)
        {
            return
                PERSISTENCE_VARIABLE_PREFIX +
                "Archive." +
                fileName +
                ".ChunkCount";
        }


        internal static string BuildArchiveLengthVariableName(
            string fileName)
        {
            return
                PERSISTENCE_VARIABLE_PREFIX +
                "Archive." +
                fileName +
                ".Length";
        }


        internal static string BuildArchiveChunkVariableName(
            string fileName,
            int chunkIndex)
        {
            return
                PERSISTENCE_VARIABLE_PREFIX +
                "Archive." +
                fileName +
                ".Chunk." +
                chunkIndex;
        }


        internal RuntimePersistenceManifest LoadPersistenceManifest()
        {
            string xml;

            if (!MyAPIGateway.Utilities.GetVariable<string>(
                    PERSISTENCE_MANIFEST_VARIABLE,
                    out xml) ||
                string.IsNullOrWhiteSpace(xml))
            {
                return new RuntimePersistenceManifest();
            }


            RuntimePersistenceManifest manifest =
                MyAPIGateway.Utilities
                    .SerializeFromXML<RuntimePersistenceManifest>(
                        xml);

            if (manifest == null)
            {
                throw new Exception(
                    "Could not deserialize the runtime persistence manifest.");
            }


            if (manifest.Packages == null)
            {
                manifest.Packages =
                    new List<RuntimePersistencePackageEntry>();
            }


            return manifest;
        }


        internal void SavePersistenceManifest()
        {
            string xml =
                MyAPIGateway.Utilities
                    .SerializeToXML<RuntimePersistenceManifest>(
                        _manifest);

            MyAPIGateway.Utilities.SetVariable(
                PERSISTENCE_MANIFEST_VARIABLE,
                xml);
        }


        internal RuntimePersistencePackageEntry
            FindPersistenceManifestPackage(
                string archiveFile)
        {
            if (_manifest == null ||
                _manifest.Packages == null ||
                string.IsNullOrWhiteSpace(archiveFile))
            {
                return null;
            }


            return _manifest.Packages
                .FirstOrDefault(x =>
                    x != null &&
                    string.Equals(
                        x.ArchiveFile,
                        archiveFile,
                        StringComparison.OrdinalIgnoreCase));
        }


        internal static bool PersistencePackageMatchesEntry(
            RuntimePersistencePackageEntry package,
            RuntimePlanetBuilderEntry entry)
        {
            if (package == null ||
                entry == null)
            {
                return false;
            }


            return
                string.Equals(
                    package.Subtype,
                    entry.Subtype,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    package.GeneratorFile,
                    entry.GeneratorFile,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    package.ArchiveFile,
                    entry.ArchiveFile,
                    StringComparison.OrdinalIgnoreCase);
        }


        internal RuntimePersistencePackageEntry
            CreatePersistencePackageFromEntry(
                RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));


            int chunkCount;

            if (!MyAPIGateway.Utilities.GetVariable<int>(
                BuildArchiveChunkCountVariableName(
                    entry.ArchiveFile),
                out chunkCount))
            {
                chunkCount =
                    0;
            }


            ValidateArchiveChunkCount(
                chunkCount,
                entry.ArchiveFile);


            return new RuntimePersistencePackageEntry
            {
                Subtype = entry.Subtype,
                SourceEntityId = entry.SourceEntityId,
                GeneratorFile = entry.GeneratorFile,
                ArchiveFile = entry.ArchiveFile,
                ChunkCount =
                    GetPersistenceType(entry) ==
                        RuntimePlanetPersistenceType.Procedural
                        ? 0
                        : chunkCount,
                PersistenceType = entry.PersistenceType,
                RecipeSchemaVersion = entry.RecipeSchemaVersion,
                RecipeVariable = entry.RecipeVariable,
                Pending = false
            };
        }


        internal void BeginPendingPersistencePackage(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));


            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();


                RuntimePersistencePackageEntry package =
                    FindPersistenceManifestPackage(
                        entry.ArchiveFile);

                if (package == null)
                {
                    package =
                        new RuntimePersistencePackageEntry();

                    _manifest.Packages.Add(
                        package);
                }


                int chunkCount;

                if (!MyAPIGateway.Utilities.GetVariable<int>(
                    BuildArchiveChunkCountVariableName(
                        entry.ArchiveFile),
                    out chunkCount))
                {
                    chunkCount =
                        0;
                }


                ValidateArchiveChunkCount(
                    chunkCount,
                    entry.ArchiveFile);

                package.Subtype =
                    entry.Subtype;

                package.SourceEntityId =
                    entry.SourceEntityId;

                package.GeneratorFile =
                    entry.GeneratorFile;

                package.ArchiveFile =
                    entry.ArchiveFile;

                package.ChunkCount =
                    GetPersistenceType(entry) ==
                        RuntimePlanetPersistenceType.Procedural
                        ? 0
                        : chunkCount;

                package.PersistenceType =
                    entry.PersistenceType;

                package.RecipeSchemaVersion =
                    entry.RecipeSchemaVersion;

                package.RecipeVariable =
                    entry.RecipeVariable;

                package.Pending =
                    true;

                SavePersistenceManifest();
            }
        }


        internal void CleanupAbandonedPersistencePackages()
        {
            lock (_persistenceSync)
            {
                bool settingsChanged =
                    false;

                bool manifestChanged =
                    false;


                for (int packageIndex =
                        _manifest.Packages.Count - 1;
                    packageIndex >= 0;
                    packageIndex--)
                {
                    RuntimePersistencePackageEntry package =
                        _manifest.Packages[packageIndex];

                    RuntimePlanetBuilderEntry referencedEntry =
                        package == null
                            ? null
                            : Settings.PlanetBuilders
                                .FirstOrDefault(x =>
                                    PersistencePackageMatchesEntry(
                                        package,
                                        x));

                    if (package != null &&
                        !package.Pending &&
                        referencedEntry != null)
                    {
                        continue;
                    }


                    if (package != null)
                    {
                        RemovePersistencePackageArtifacts(
                            package);
                    }


                    if (package != null &&
                        Settings.PlanetBuilders.RemoveAll(x =>
                            PersistencePackageMatchesEntry(
                                package,
                                x)) > 0)
                    {
                        settingsChanged =
                            true;
                    }


                    _manifest.Packages.RemoveAt(
                        packageIndex);

                    manifestChanged =
                        true;
                }


                if (settingsChanged)
                    SaveRuntimeSettings();

                if (manifestChanged)
                    SavePersistenceManifest();
            }
        }


        internal void SeedPersistenceManifestFromSettings()
        {
            lock (_persistenceSync)
            {
                bool changed =
                    false;


                for (int entryIndex = 0;
                    entryIndex < Settings.PlanetBuilders.Count;
                    entryIndex++)
                {
                    RuntimePlanetBuilderEntry entry =
                        Settings.PlanetBuilders[entryIndex];

                    if (entry == null ||
                        FindPersistenceManifestPackage(
                            entry.ArchiveFile) != null)
                    {
                        continue;
                    }


                    int chunkCount;

                    if (!MyAPIGateway.Utilities.GetVariable<int>(
                        BuildArchiveChunkCountVariableName(
                            entry.ArchiveFile),
                        out chunkCount))
                    {
                        chunkCount =
                            0;
                    }


                    ValidateArchiveChunkCount(
                        chunkCount,
                        entry.ArchiveFile);

                    _manifest.Packages.Add(
                        new RuntimePersistencePackageEntry
                        {
                            Subtype = entry.Subtype,
                            SourceEntityId = entry.SourceEntityId,
                            GeneratorFile = entry.GeneratorFile,
                            ArchiveFile = entry.ArchiveFile,
                            ChunkCount =
                                GetPersistenceType(entry) ==
                                    RuntimePlanetPersistenceType.Procedural
                                    ? 0
                                    : chunkCount,
                            PersistenceType = entry.PersistenceType,
                            RecipeSchemaVersion = entry.RecipeSchemaVersion,
                            RecipeVariable = entry.RecipeVariable,
                            Pending = false
                        });

                    changed =
                        true;
                }


                if (changed)
                    SavePersistenceManifest();
            }
        }


        internal static void ValidateArchiveChunkCount(
            int chunkCount,
            string fileName)
        {
            if (chunkCount < 0 ||
                chunkCount > MAX_ARCHIVE_CHUNK_COUNT)
            {
                throw new Exception(
                    "Invalid runtime archive chunk count " +
                    chunkCount +
                    " for " +
                    fileName);
            }
        }


        internal static void RemoveRuntimeArchiveVariableRange(
            string fileName,
            int chunkCount)
        {
            ValidateArchiveChunkCount(
                chunkCount,
                fileName);


            for (int chunkIndex = 0;
                chunkIndex < chunkCount;
                chunkIndex++)
            {
                MyAPIGateway.Utilities.RemoveVariable(
                    BuildArchiveChunkVariableName(
                        fileName,
                        chunkIndex));
            }


            MyAPIGateway.Utilities.RemoveVariable(
                BuildArchiveLengthVariableName(
                    fileName));

            MyAPIGateway.Utilities.RemoveVariable(
                BuildArchiveChunkCountVariableName(
                    fileName));
        }


        internal void RemovePersistencePackageArtifacts(
            RuntimePersistencePackageEntry package)
        {
            if (package == null)
                return;


            int chunkCount;

            if (!MyAPIGateway.Utilities.GetVariable<int>(
                BuildArchiveChunkCountVariableName(
                    package.ArchiveFile),
                out chunkCount))
            {
                chunkCount =
                    package.ChunkCount;
            }


            ValidateArchiveChunkCount(
                chunkCount,
                package.ArchiveFile);

            RemoveRuntimeArchiveVariableRange(
                package.ArchiveFile,
                Math.Max(
                    chunkCount,
                    package.ChunkCount));

            if (GetPersistenceType(package) ==
                RuntimePlanetPersistenceType.Procedural)
            {
                MyAPIGateway.Utilities.RemoveVariable(
                    BuildRecipeVariableName(
                        package.ArchiveFile));
            }

            MyAPIGateway.Utilities.RemoveVariable(
                BuildGeneratorVariableName(
                    package.GeneratorFile));

            TryDeleteWorldStorageCacheFile(
                package.GeneratorFile);

            TryDeleteWorldStorageCacheFile(
                package.ArchiveFile);

            _worldStorageCacheFiles.Remove(
                package.GeneratorFile);

            _worldStorageCacheFiles.Remove(
                package.ArchiveFile);


            if (!string.IsNullOrWhiteSpace(
                package.Subtype))
            {
                Generators.Remove(
                    package.Subtype);
            }
        }


        internal void StageRuntimePackageForCommit(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));


            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();


                RuntimePersistencePackageEntry package =
                    FindPersistenceManifestPackage(
                        entry.ArchiveFile);

                if (package == null)
                {
                    throw new Exception(
                        "Pending runtime package is missing from the manifest: " +
                        entry.ArchiveFile);
                }


                int chunkCount =
                    0;

                if (GetPersistenceType(entry) ==
                    RuntimePlanetPersistenceType.Procedural)
                {
                    LoadRuntimeRecipe(
                        entry);
                }
                else
                {
                    if (!MyAPIGateway.Utilities.GetVariable<int>(
                        BuildArchiveChunkCountVariableName(
                            entry.ArchiveFile),
                        out chunkCount))
                    {
                        throw new Exception(
                            "Pending runtime package has no chunk-count metadata: " +
                            entry.ArchiveFile);
                    }

                    ValidateArchiveChunkCount(
                        chunkCount,
                        entry.ArchiveFile);
                }

                package.Subtype =
                    entry.Subtype;

                package.SourceEntityId =
                    entry.SourceEntityId;

                package.GeneratorFile =
                    entry.GeneratorFile;

                package.ChunkCount =
                    chunkCount;

                package.PersistenceType =
                    entry.PersistenceType;

                package.RecipeSchemaVersion =
                    entry.RecipeSchemaVersion;

                package.RecipeVariable =
                    entry.RecipeVariable;

                package.Pending =
                    false;


                if (!Settings.PlanetBuilders.Any(x =>
                    x != null &&
                    string.Equals(
                        x.Subtype,
                        entry.Subtype,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    Settings.PlanetBuilders.Add(
                        entry);
                }


                SaveRuntimeSettings();
                SavePersistenceManifest();
            }
        }


        internal void PruneSupersededRuntimePackages(
            RuntimePlanetBuilderEntry retainedEntry)
        {
            if (retainedEntry == null)
                throw new ArgumentNullException(nameof(retainedEntry));


            lock (_persistenceSync)
            {
                var supersededEntries =
                    Settings.PlanetBuilders
                        .Where(x =>
                            x != null &&
                            x.SourceEntityId == retainedEntry.SourceEntityId &&
                            !string.Equals(
                                x.Subtype,
                                retainedEntry.Subtype,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (supersededEntries.Count == 0)
                    return;


                for (int i = 0;
                    i < supersededEntries.Count;
                    i++)
                {
                    RuntimePlanetBuilderEntry supersededEntry =
                        supersededEntries[i];

                    RuntimePersistencePackageEntry package =
                        FindPersistenceManifestPackage(
                            supersededEntry.ArchiveFile);

                    RemovePersistencePackageArtifacts(
                        package ??
                        CreatePersistencePackageFromEntry(
                            supersededEntry));

                    if (package != null)
                    {
                        _manifest.Packages.Remove(
                            package);
                    }


                    Settings.PlanetBuilders.Remove(
                        supersededEntry);

                    Generators.Remove(
                        supersededEntry.Subtype);
                }


                SaveRuntimeSettings();
                SavePersistenceManifest();
            }
        }


        internal void DiscardRuntimePackage(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                return;


            lock (_persistenceSync)
            {
                RuntimePersistencePackageEntry package =
                    FindPersistenceManifestPackage(
                        entry.ArchiveFile);

                RemovePersistencePackageArtifacts(
                    package ??
                    CreatePersistencePackageFromEntry(
                        entry));

                if (package != null)
                {
                    _manifest.Packages.Remove(
                        package);
                }


                bool settingsChanged =
                    Settings.PlanetBuilders.RemoveAll(x =>
                        x != null &&
                        string.Equals(
                            x.Subtype,
                            entry.Subtype,
                            StringComparison.OrdinalIgnoreCase)) > 0;

                Generators.Remove(
                    entry.Subtype);


                if (settingsChanged)
                    SaveRuntimeSettings();

                SavePersistenceManifest();
            }
        }


        internal void ReconcileRuntimePackagesWithLivePlanets()
        {
            List<RuntimePlanetBuilderEntry> entries;

            lock (_persistenceSync)
            {
                entries =
                    Settings.PlanetBuilders
                        .Where(x =>
                            x != null)
                        .ToList();
            }


            var staleEntries =
                new List<RuntimePlanetBuilderEntry>();

            foreach (IGrouping<long, RuntimePlanetBuilderEntry> group in
                entries.GroupBy(x =>
                    x.SourceEntityId))
            {
                MyPlanet planet =
                    PlanetLocator.FindByEntityId(
                        group.Key);

                if (planet == null ||
                    planet.Storage == null ||
                    planet.Closed ||
                    planet.MarkedForClose)
                {
                    staleEntries.AddRange(
                        group);

                    continue;
                }


                long planetSeed;
                string providerSubtype;

                try
                {
                    _server.ReadLivePlanetProviderIdentity(
                        planet,
                        out planetSeed,
                        out providerSubtype);
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Retaining packages for planet " +
                        group.Key +
                        " because its live provider could not be resolved: " +
                        e.Message);

                    continue;
                }


                staleEntries.AddRange(
                    group.Where(x =>
                        !string.Equals(
                            x.Subtype,
                            providerSubtype,
                            StringComparison.OrdinalIgnoreCase)));
            }


            if (staleEntries.Count == 0)
                return;


            lock (_persistenceSync)
            {
                int removedCount =
                    0;


                for (int entryIndex = 0;
                    entryIndex < staleEntries.Count;
                    entryIndex++)
                {
                    RuntimePlanetBuilderEntry staleEntry =
                        staleEntries[entryIndex];

                    if (!Settings.PlanetBuilders.Contains(
                        staleEntry))
                    {
                        continue;
                    }


                    RuntimePersistencePackageEntry package =
                        FindPersistenceManifestPackage(
                            staleEntry.ArchiveFile);

                    RemovePersistencePackageArtifacts(
                        package ??
                        CreatePersistencePackageFromEntry(
                            staleEntry));

                    if (package != null)
                    {
                        _manifest.Packages.Remove(
                            package);
                    }


                    Settings.PlanetBuilders.Remove(
                        staleEntry);

                    Generators.Remove(
                        staleEntry.Subtype);

                    removedCount++;
                }


                if (removedCount == 0)
                    return;


                SaveRuntimeSettings();
                SavePersistenceManifest();

                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Removed stale runtime packages: " +
                    removedCount);
            }
        }


        internal bool TryIsRuntimePackageLive(
            MyPlanet planet,
            RuntimePlanetBuilderEntry entry,
            out bool isLive)
        {
            isLive =
                false;


            if (planet == null ||
                entry == null ||
                planet.Storage == null)
            {
                return true;
            }


            try
            {
                long planetSeed;
                string providerSubtype;

                _server.ReadLivePlanetProviderIdentity(
                    planet,
                    out planetSeed,
                    out providerSubtype);

                isLive =
                    string.Equals(
                        providerSubtype,
                        entry.Subtype,
                        StringComparison.OrdinalIgnoreCase);

                return true;
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Could not resolve live provider after " +
                    "a commit error: " +
                    e.Message);

                return false;
            }
        }


        internal void WriteWorldStorageTextCache(
            string fileName,
            string contents)
        {
            using (TextWriter writer =
                MyAPIGateway.Utilities.WriteFileInWorldStorage(
                    fileName,
                    typeof(VoxelCubemapApiServer)))
            {
                writer.Write(
                    contents);
            }


            _worldStorageCacheFiles.Add(
                fileName);
        }


        internal void WriteWorldStorageBinaryCache(
            string fileName,
            byte[] contents)
        {
            using (BinaryWriter writer =
                MyAPIGateway.Utilities.WriteBinaryFileInWorldStorage(
                    fileName,
                    typeof(VoxelCubemapApiServer)))
            {
                writer.Write(
                    contents);
            }


            _worldStorageCacheFiles.Add(
                fileName);
        }


        internal void StageTransientRuntimePackage(
            RuntimePlanetBuilderEntry entry,
            string generatorXml,
            byte[] archive)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            if (string.IsNullOrWhiteSpace(generatorXml))
                throw new ArgumentException(
                    "Transient generator XML cannot be empty.",
                    nameof(generatorXml));

            if (archive == null ||
                archive.Length == 0)
            {
                throw new ArgumentException(
                    "Transient runtime archive cannot be empty.",
                    nameof(archive));
            }

            if (MyAPIGateway.Session.IsServer)
            {
                throw new InvalidOperationException(
                    "Transient runtime packages are client replay state only.");
            }

            lock (_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();

                WriteWorldStorageTextCache(
                    entry.GeneratorFile,
                    generatorXml);

                WriteWorldStorageBinaryCache(
                    entry.ArchiveFile,
                    archive);

                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(
                        entry.GeneratorFile,
                        typeof(VoxelCubemapApiServer)) ||
                    !MyAPIGateway.Utilities.FileExistsInWorldStorage(
                        entry.ArchiveFile,
                        typeof(VoxelCubemapApiServer)))
                {
                    throw new Exception(
                        "Transient runtime package was not staged in the " +
                        "client's active world storage.");
                }
            }
        }


        internal void CommitTransientRuntimePackage(
            RuntimePlanetBuilderEntry entry,
            MyPlanetGeneratorDefinition generator)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            if (generator == null)
                throw new ArgumentNullException(nameof(generator));

            if (MyAPIGateway.Session.IsServer)
            {
                throw new InvalidOperationException(
                    "Transient runtime packages are client replay state only.");
            }

            lock (_persistenceSync)
            {
                List<RuntimePlanetBuilderEntry> supersededEntries =
                    Settings.PlanetBuilders
                        .Where(x =>
                            x != null &&
                            x.SourceEntityId == entry.SourceEntityId &&
                            !string.Equals(
                                x.Subtype,
                                entry.Subtype,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                for (int index = 0;
                    index < supersededEntries.Count;
                    index++)
                {
                    RuntimePlanetBuilderEntry superseded =
                        supersededEntries[index];

                    Settings.PlanetBuilders.Remove(
                        superseded);

                    Generators.Remove(
                        superseded.Subtype);

                    TryDeleteWorldStorageCacheFile(
                        superseded.GeneratorFile);

                    TryDeleteWorldStorageCacheFile(
                        superseded.ArchiveFile);

                    _worldStorageCacheFiles.Remove(
                        superseded.GeneratorFile);

                    _worldStorageCacheFiles.Remove(
                        superseded.ArchiveFile);
                }

                Settings.PlanetBuilders.RemoveAll(x =>
                    x != null &&
                    string.Equals(
                        x.Subtype,
                        entry.Subtype,
                        StringComparison.OrdinalIgnoreCase));

                Settings.PlanetBuilders.Add(
                    entry);

                Generators[entry.Subtype] =
                    generator;
            }
        }


        internal void DiscardTransientRuntimePackage(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                return;

            lock (_persistenceSync)
            {
                Generators.Remove(
                    entry.Subtype);

                TryDeleteWorldStorageCacheFile(
                    entry.GeneratorFile);

                TryDeleteWorldStorageCacheFile(
                    entry.ArchiveFile);

                _worldStorageCacheFiles.Remove(
                    entry.GeneratorFile);

                _worldStorageCacheFiles.Remove(
                    entry.ArchiveFile);
            }
        }


        internal void ClearWorldStorageCache()
        {
            lock (_persistenceSync)
            {
                foreach (string fileName in
                    _worldStorageCacheFiles)
                {
                    if (ShouldPreservePersistentCacheFile(
                        fileName))
                    {
                        continue;
                    }

                    TryDeleteWorldStorageCacheFile(
                        fileName);
                }


                _worldStorageCacheFiles.Clear();

                TryDeleteWorldStorageCacheFile(
                    RUNTIME_SETTINGS_FILE);


                if (Settings == null ||
                    Settings.PlanetBuilders == null)
                {
                    return;
                }


                for (int i = 0;
                    i < Settings.PlanetBuilders.Count;
                    i++)
                {
                    RuntimePlanetBuilderEntry entry =
                        Settings.PlanetBuilders[i];

                    if (entry == null)
                        continue;


                    TryDeleteWorldStorageCacheFile(
                        entry.GeneratorFile);

                    if (!ShouldPreservePersistentCacheFile(
                        entry.ArchiveFile))
                    {
                        TryDeleteWorldStorageCacheFile(
                            entry.ArchiveFile);
                    }
                }
            }
        }


        private bool ShouldPreservePersistentCacheFile(
            string fileName)
        {
            if (!_config.PersistentCache ||
                string.IsNullOrWhiteSpace(fileName) ||
                Settings == null ||
                Settings.PlanetBuilders == null)
            {
                return false;
            }

            return Settings.PlanetBuilders.Any(entry =>
                entry != null &&
                GetPersistenceType(entry) ==
                    RuntimePlanetPersistenceType.Procedural &&
                string.Equals(
                    entry.ArchiveFile,
                    fileName,
                    StringComparison.OrdinalIgnoreCase));
        }


        internal void ThrowIfPersistenceUnavailable()
        {
            if (_server.IsUnloading)
            {
                throw new Exception(
                    "Runtime planet persistence is unavailable while the session is unloading.");
            }
        }


        internal static void TryDeleteWorldStorageCacheFile(
            string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;


            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(
                    fileName,
                    typeof(VoxelCubemapApiServer)))
                {
                    MyAPIGateway.Utilities.DeleteFileInWorldStorage(
                        fileName,
                        typeof(VoxelCubemapApiServer));
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Could not clear WorldStorage " +
                    "cache file '" +
                    fileName +
                    "': " +
                    e);
            }
        }


        internal MyObjectBuilder_PlanetGeneratorDefinition
            LoadGeneratorBuilderFromWorldStorage(
                string fileName)
        {
            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(
                fileName,
                typeof(VoxelCubemapApiServer)))
            {
                throw new Exception(
                    "Missing persisted runtime generator builder: " +
                    fileName);
            }


            string xml;

            using (TextReader reader =
                MyAPIGateway.Utilities.ReadFileInWorldStorage(
                    fileName,
                    typeof(VoxelCubemapApiServer)))
            {
                xml =
                    reader.ReadToEnd();
            }


            MyObjectBuilder_PlanetGeneratorDefinition builder =
                MyAPIGateway.Utilities
                    .SerializeFromXML<MyObjectBuilder_PlanetGeneratorDefinition>(
                        xml);


            if (builder == null)
            {
                throw new Exception(
                    "Could not deserialize persisted generator builder: " +
                    fileName);
            }


            return builder;
        }


        internal MyPlanetGeneratorDefinition
            LoadAndRegisterPersistedRuntimeGenerator(
                RuntimePlanetBuilderEntry entry,
                string savePath)
        {
            MyObjectBuilder_PlanetGeneratorDefinition builder =
                LoadGeneratorBuilderFromWorldStorage(
                    entry.GeneratorFile);


            string absoluteFolder =
                _server.BuildWorldStorageFilePath(
                    savePath,
                    entry.ArchiveFile);


            if (!string.IsNullOrWhiteSpace(
                entry.EnvironmentCarrierSubtype))
            {
                PlanetEnvironmentService.EnsureBiomeMapEnabled(
                    builder);
            }


            MyPlanetGeneratorDefinition runtimeGenerator =
                _server.RegisterRuntimeGeneratorDefinition(
                    builder,
                    entry.Subtype,
                    absoluteFolder,
                    entry.GrassMaterialValue,
                    entry.GrassNoiseVersion > 0);


            PlanetEnvironmentService.BindRuntimeGenerator(
                runtimeGenerator,
                entry.EnvironmentCarrierSubtype);

            return runtimeGenerator;
        }


        internal static RuntimePlanetPersistenceType GetPersistenceType(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null ||
                entry.PersistenceType == 0)
            {
                return RuntimePlanetPersistenceType.PngSnapshot;
            }

            if (entry.PersistenceType ==
                    (int)RuntimePlanetPersistenceType.Procedural ||
                entry.PersistenceType ==
                    (int)RuntimePlanetPersistenceType.PngSnapshot)
            {
                return (RuntimePlanetPersistenceType)entry.PersistenceType;
            }

            throw new Exception(
                "Unsupported runtime persistence type: " +
                entry.PersistenceType);
        }


        internal static RuntimePlanetPersistenceType GetPersistenceType(
            RuntimePersistencePackageEntry entry)
        {
            if (entry == null ||
                entry.PersistenceType == 0)
            {
                return RuntimePlanetPersistenceType.PngSnapshot;
            }

            if (entry.PersistenceType ==
                    (int)RuntimePlanetPersistenceType.Procedural ||
                entry.PersistenceType ==
                    (int)RuntimePlanetPersistenceType.PngSnapshot)
            {
                return (RuntimePlanetPersistenceType)entry.PersistenceType;
            }

            throw new Exception(
                "Unsupported runtime persistence package type: " +
                entry.PersistenceType);
        }


        private static string ResolveRecipeVariableName(
            RuntimePlanetBuilderEntry entry)
        {
            string expected =
                BuildRecipeVariableName(
                    entry.ArchiveFile);

            if (!string.IsNullOrWhiteSpace(entry.RecipeVariable) &&
                !string.Equals(
                    entry.RecipeVariable,
                    expected,
                    StringComparison.Ordinal))
            {
                throw new Exception(
                    "Procedural recipe variable does not match its runtime archive.");
            }

            return expected;
        }


        internal static void ValidateRuntimeRecipe(
            RuntimeProceduralPlanetRecipe recipe,
            RuntimePlanetBuilderEntry entry)
        {
            const int supportedSchemaVersion = 1;
            const int maximumOperationCount = 16384;

            if (recipe == null)
                throw new Exception("Procedural planet recipe is null.");

            if (recipe.SchemaVersion != supportedSchemaVersion)
            {
                throw new Exception(
                    "Unsupported procedural planet recipe schema: " +
                    recipe.SchemaVersion);
            }

            if (entry != null &&
                entry.RecipeSchemaVersion != 0 &&
                entry.RecipeSchemaVersion != recipe.SchemaVersion)
            {
                throw new Exception(
                    "Procedural recipe schema does not match its runtime entry.");
            }

            if (recipe.Source == null ||
                string.IsNullOrWhiteSpace(recipe.Source.SourceSubtype) ||
                string.IsNullOrWhiteSpace(recipe.Source.SourceFolderName))
            {
                throw new Exception(
                    "Procedural recipe has no resolvable root source.");
            }

            if (recipe.Source.SourceSubtype.Length > 512 ||
                recipe.Source.SourceFolderName.Length > 1024 ||
                (recipe.Source.ModName != null &&
                    recipe.Source.ModName.Length > 1024) ||
                (recipe.Source.PublishedServiceName != null &&
                    recipe.Source.PublishedServiceName.Length > 128))
            {
                throw new Exception(
                    "Procedural recipe source identity is too long.");
            }

            if (recipe.Source.SourceFolderName.IndexOf(':') >= 0 ||
                recipe.Source.SourceFolderName.EndsWith(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "Procedural recipe root source folder is not content-relative.");
            }

            if (entry != null &&
                entry.PlanetSeed != recipe.PlanetSeed)
            {
                throw new Exception(
                    "Procedural recipe seed does not match its runtime entry.");
            }

            if (recipe.NoiseVersion != 1)
            {
                throw new Exception(
                    "Unsupported procedural noise algorithm version: " +
                    recipe.NoiseVersion);
            }

            if (recipe.Revisions == null)
                recipe.Revisions = new List<RuntimeProceduralRevision>();

            if (recipe.Revisions.Count > 4096)
                throw new Exception("Procedural recipe revision limit exceeded.");

            int operationCount = 0;

            for (int revisionIndex = 0;
                revisionIndex < recipe.Revisions.Count;
                revisionIndex++)
            {
                RuntimeProceduralRevision revision =
                    recipe.Revisions[revisionIndex];

                if (revision == null)
                    throw new Exception("Procedural recipe contains a null revision.");

                if (revision.Brushes == null)
                    revision.Brushes = new List<RuntimeProceduralBrushOperation>();
                if (revision.BiomeReplacements == null)
                    revision.BiomeReplacements = new List<RuntimeProceduralBiomeReplacement>();
                if (revision.FractalNoise == null)
                    revision.FractalNoise = new List<RuntimeProceduralFractalNoiseOperation>();
                if (revision.AllocatedComplexMaterialValues == null)
                    revision.AllocatedComplexMaterialValues = new List<byte>();
                if (revision.EnvironmentRemap == null)
                    revision.EnvironmentRemap =
                        new List<RuntimeProceduralEnvironmentMapRule>();

                if (revision.AllocatedComplexMaterialValues.Count > 256)
                {
                    throw new Exception(
                        "Procedural recipe material-allocation limit exceeded.");
                }

                if (revision.EnvironmentRemap.Count > 256)
                {
                    throw new Exception(
                        "Procedural environment remap rule limit exceeded.");
                }

                var environmentValues =
                    new HashSet<byte>();

                for (int i = 0;
                    i < revision.EnvironmentRemap.Count;
                    i++)
                {
                    RuntimeProceduralEnvironmentMapRule rule =
                        revision.EnvironmentRemap[i];

                    if (rule == null ||
                        rule.CompatibleBiomes == null ||
                        rule.CompatibleBiomes.Length == 0 ||
                        rule.CompatibleBiomes.Length > 256 ||
                        !environmentValues.Add(rule.MaterialMapValue))
                    {
                        throw new Exception(
                            "Procedural recipe contains an invalid environment remap.");
                    }
                }

                operationCount = checked(
                    operationCount +
                    revision.Brushes.Count +
                    revision.BiomeReplacements.Count +
                    revision.FractalNoise.Count +
                    Math.Max(
                        revision.EnvironmentRemap.Count,
                        string.IsNullOrWhiteSpace(
                            revision.EnvironmentPresetName)
                            ? 0
                            : 1));

                if (revision.EnvironmentPresetName != null &&
                    revision.EnvironmentPresetName.Length > 512)
                {
                    throw new Exception("Procedural environment preset name is too long.");
                }

                for (int i = 0; i < revision.FractalNoise.Count; i++)
                {
                    RuntimeProceduralFractalNoiseOperation operation =
                        revision.FractalNoise[i];

                    if (operation == null ||
                        operation.PlaneIndex < 0 ||
                        operation.PlaneIndex > 2 ||
                        operation.CoveragePercent < 0 ||
                        operation.CoveragePercent > 100 ||
                        double.IsNaN(operation.Threshold) ||
                        double.IsInfinity(operation.Threshold))
                    {
                        throw new Exception("Procedural recipe contains invalid fractal noise.");
                    }
                }

                for (int i = 0;
                    i < revision.BiomeReplacements.Count;
                    i++)
                {
                    if (revision.BiomeReplacements[i] == null)
                    {
                        throw new Exception(
                            "Procedural recipe contains a null biome replacement.");
                    }
                }

                for (int i = 0; i < revision.Brushes.Count; i++)
                {
                    RuntimeProceduralBrushOperation operation =
                        revision.Brushes[i];

                    int maximumFill =
                        operation != null && operation.LayerIndex == 3
                            ? ushort.MaxValue
                            : byte.MaxValue;

                    if (operation == null ||
                        operation.LayerIndex < 0 ||
                        operation.LayerIndex > 3 ||
                        operation.FillValue < 0 ||
                        operation.FillValue > maximumFill ||
                        operation.MinimumAltitude < -1 ||
                        operation.MinimumAltitude > ushort.MaxValue ||
                        operation.MaximumAltitude < -1 ||
                        operation.MaximumAltitude > ushort.MaxValue ||
                        operation.MinimumLatitude < -90.0 ||
                        operation.MinimumLatitude > 90.0 ||
                        operation.MaximumLatitude < -90.0 ||
                        operation.MaximumLatitude > 90.0 ||
                        operation.MinimumLatitude > operation.MaximumLatitude ||
                        operation.BiomeFilter < -1 ||
                        operation.BiomeFilter > byte.MaxValue ||
                        operation.MaterialFilter < -1 ||
                        operation.MaterialFilter > byte.MaxValue ||
                        (operation.UseNoise &&
                            (double.IsNaN(operation.NoiseFrequency) ||
                             double.IsInfinity(operation.NoiseFrequency) ||
                             operation.NoiseFrequency <= 0.0 ||
                             operation.NoiseOctaves < 1 ||
                             operation.NoiseOctaves > 8 ||
                             operation.NoiseSamplingQuality <
                                (int)NoiseSamplingQuality.Low ||
                             operation.NoiseSamplingQuality >
                                (int)NoiseSamplingQuality.Direct ||
                             operation.BlendNoiseMinimum < 0.0 ||
                             operation.BlendNoiseMinimum > 1.0 ||
                             operation.BlendNoiseMaximum < 0.0 ||
                             operation.BlendNoiseMaximum > 1.0 ||
                             operation.BlendNoiseMinimum >
                                operation.BlendNoiseMaximum)) ||
                        (operation.UseRadial &&
                            (double.IsNaN(operation.RadialCenterX) ||
                             double.IsInfinity(operation.RadialCenterX) ||
                             double.IsNaN(operation.RadialCenterY) ||
                             double.IsInfinity(operation.RadialCenterY) ||
                             double.IsNaN(operation.RadialCenterZ) ||
                             double.IsInfinity(operation.RadialCenterZ) ||
                             operation.RadialCenterX * operation.RadialCenterX +
                                operation.RadialCenterY * operation.RadialCenterY +
                                operation.RadialCenterZ * operation.RadialCenterZ < 1e-12 ||
                             double.IsNaN(operation.RadialRadiusDegrees) ||
                             double.IsInfinity(operation.RadialRadiusDegrees) ||
                             operation.RadialRadiusDegrees <= 0.0 ||
                             operation.RadialRadiusDegrees > 180.0 ||
                             operation.RadialProfile < 0 ||
                             operation.RadialProfile > 3)))
                    {
                        throw new Exception("Procedural recipe contains an invalid brush.");
                    }
                }
            }

            if (operationCount > maximumOperationCount)
            {
                throw new Exception(
                    "Procedural recipe operation limit exceeded: " +
                    operationCount);
            }
        }


    }
}
