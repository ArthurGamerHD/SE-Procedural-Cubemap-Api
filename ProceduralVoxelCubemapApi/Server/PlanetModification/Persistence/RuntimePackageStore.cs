using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using VoxelCubemapApi.Server.PlanetModification.Runtime;
using VoxelCubemapApi.Server.PlanetModification.World;

using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;

namespace VoxelCubemapApi.Server.PlanetModification.Persistence
{
    internal sealed class RuntimePackageStore
    {
        private const string RuntimeSettingsFile =
            "settings.xml";

        private const string PersistenceVariablePrefix =
            "VoxelCubemapApi.RuntimePersistence.v1.";

        private const string RuntimeSettingsVariable =
            PersistenceVariablePrefix +
            "SettingsXml";

        private const string PersistenceManifestVariable =
            PersistenceVariablePrefix +
            "ManifestXml";

        // Utility variables must keep binary archives as ordinary Base64 strings
        // because Keen's checkpoint XML reader cannot consume typed byte arrays.
        private const int ArchiveChunkSizeBytes =
            4 * 1024 * 1024;

        private const int MaxArchiveChunkCount =
            512;

        private readonly VoxelCubemapApiServer _server;
        private readonly object _persistenceSync =
            new object();

        private readonly HashSet<string> _worldStorageCacheFiles =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        private RuntimePersistenceManifest _manifest =
            new RuntimePersistenceManifest();


        internal RuntimePackageStore(
            VoxelCubemapApiServer server)
        {
            if (server == null)
                throw new ArgumentNullException("server");

            _server =
                server;
        }


        internal RuntimePlanetGeneratorSettings Settings { get; private set; } =
            new RuntimePlanetGeneratorSettings();

        internal Dictionary<string, MyPlanetGeneratorDefinition> Generators { get; } =
            new Dictionary<string, MyPlanetGeneratorDefinition>(
                StringComparer.OrdinalIgnoreCase);

        internal string BoundSavePath { get; set; }


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
                RuntimeSettingsVariable,
                out xml))
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(
                    RuntimeSettingsFile,
                    typeof(VoxelCubemapApiServer)))
                {
                    return new RuntimePlanetGeneratorSettings();
                }


                using (TextReader reader =
                    MyAPIGateway.Utilities.ReadFileInWorldStorage(
                        RuntimeSettingsFile,
                        typeof(VoxelCubemapApiServer)))
                {
                    xml =
                        reader.ReadToEnd();
                }


                MyAPIGateway.Utilities.SetVariable(
                    RuntimeSettingsVariable,
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
                    RuntimeSettingsFile +
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
                    RuntimeSettingsVariable,
                    xml);

                WriteWorldStorageTextCache(
                    RuntimeSettingsFile,
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
                    RuntimeSettingsFile,
                    MyAPIGateway.Utilities
                        .SerializeToXML<RuntimePlanetGeneratorSettings>(
                            Settings));


                for (int i = 0;
                    i < Settings.PlanetBuilders.Count;
                    i++)
                {
                    RuntimePlanetBuilderEntry entry =
                        Settings.PlanetBuilders[i];

                    RestoreGeneratorCache(
                        entry.GeneratorFile,
                        allowLegacyMigration);

                    RestoreArchiveCache(
                        entry.ArchiveFile,
                        allowLegacyMigration);
                }
            }
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


        internal void SaveRuntimeArchiveVariables(
            string fileName,
            byte[] archive)
        {
            if (archive == null)
                throw new ArgumentNullException("archive");


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
                    ArchiveChunkSizeBytes -
                    1) /
                    ArchiveChunkSizeBytes);

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
                        ArchiveChunkSizeBytes;

                    int length =
                        Math.Min(
                            ArchiveChunkSizeBytes,
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
                chunkCount < 0)
            {
                throw new Exception(
                    "Invalid runtime archive variable metadata: " +
                    fileName);
            }


            int expectedChunkCount =
                (int)(((long)archiveLength +
                    ArchiveChunkSizeBytes -
                    1) /
                    ArchiveChunkSizeBytes);

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
                    ArchiveChunkSizeBytes;

                int expectedLength =
                    Math.Min(
                        ArchiveChunkSizeBytes,
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
                PersistenceVariablePrefix +
                "GeneratorXml." +
                fileName;
        }


        internal static string BuildArchiveChunkCountVariableName(
            string fileName)
        {
            return
                PersistenceVariablePrefix +
                "Archive." +
                fileName +
                ".ChunkCount";
        }


        internal static string BuildArchiveLengthVariableName(
            string fileName)
        {
            return
                PersistenceVariablePrefix +
                "Archive." +
                fileName +
                ".Length";
        }


        internal static string BuildArchiveChunkVariableName(
            string fileName,
            int chunkIndex)
        {
            return
                PersistenceVariablePrefix +
                "Archive." +
                fileName +
                ".Chunk." +
                chunkIndex;
        }


        internal RuntimePersistenceManifest LoadPersistenceManifest()
        {
            string xml;

            if (!MyAPIGateway.Utilities.GetVariable<string>(
                    PersistenceManifestVariable,
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
                PersistenceManifestVariable,
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
                throw new ArgumentNullException("entry");


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
                ChunkCount = chunkCount,
                Pending = false
            };
        }


        internal void BeginPendingPersistencePackage(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");


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
                    chunkCount;

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
                            ChunkCount = chunkCount,
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
                chunkCount > MaxArchiveChunkCount)
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
                throw new ArgumentNullException("entry");


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


                int chunkCount;

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

                package.Subtype =
                    entry.Subtype;

                package.SourceEntityId =
                    entry.SourceEntityId;

                package.GeneratorFile =
                    entry.GeneratorFile;

                package.ChunkCount =
                    chunkCount;

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
                throw new ArgumentNullException("retainedEntry");


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


        internal void ClearWorldStorageCache()
        {
            lock (_persistenceSync)
            {
                foreach (string fileName in
                    _worldStorageCacheFiles)
                {
                    TryDeleteWorldStorageCacheFile(
                        fileName);
                }


                _worldStorageCacheFiles.Clear();

                TryDeleteWorldStorageCacheFile(
                    RuntimeSettingsFile);


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

                    TryDeleteWorldStorageCacheFile(
                        entry.ArchiveFile);
                }
            }
        }


        internal void ThrowIfPersistenceUnavailable()
        {
            if (_server.IsUnloading)
            {
                throw new Exception(
                    "Runtime planet persistence is unavailable while the " +
                    "session is unloading.");
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


    }
}
