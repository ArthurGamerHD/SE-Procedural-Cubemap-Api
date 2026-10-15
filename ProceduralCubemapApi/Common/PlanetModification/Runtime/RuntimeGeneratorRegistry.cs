using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adk.Utils;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using VRage.Game;
using VRage.Game.ObjectBuilders;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace ProceduralCubemapApi.Common.PlanetModification.Runtime
{
    internal sealed class RuntimeGeneratorRegistry
    {
        private const string WORLD_STORAGE_SENTINEL_PREFIX =
            "SentinelCanary_";

        private const string WORLD_STORAGE_SENTINEL_EXTENSION =
            ".txt";

        private readonly RuntimePackageStore _runtimePackages;
        private readonly MyModContext _modContext;

        private string _verifiedWorldSavePath;
        private string _verifiedReportedSavePath;


        internal RuntimeGeneratorRegistry(
            RuntimePackageStore runtimePackages,
            MyModContext modContext)
        {
            if (runtimePackages == null)
                throw new ArgumentNullException(nameof(runtimePackages));

            if (modContext == null)
                throw new ArgumentNullException(nameof(modContext));

            _runtimePackages =
                runtimePackages;

            _modContext =
                modContext;
        }


        internal string VerifiedWorldSavePath =>
            _verifiedWorldSavePath;


        internal MyObjectBuilder_PlanetGeneratorDefinition
            CaptureSourceBuilder(
                MyPlanetGeneratorDefinition sourceGenerator)
        {
            if (sourceGenerator == null)
                throw new ArgumentNullException(nameof(sourceGenerator));


            MyModContext context =
                sourceGenerator.Context;


            if (context == null)
            {
                throw new Exception(
                    "Source generator '" +
                    sourceGenerator.Id.SubtypeName +
                    "' has no definition context. Refusing lossy capture.");
            }


            string xml;
            string resolvedFile;

            string subtype =
                sourceGenerator.Id.SubtypeName;

            ReadSourceDefinitionXml(
                context,
                subtype,
                out xml,
                out resolvedFile);


            MyObjectBuilder_PlanetGeneratorDefinition builder =
                DeserializeSourceBuilder(
                    xml,
                    subtype);


            if (builder == null)
            {
                throw new Exception(
                    "PlanetGeneratorDefinition '" +
                    subtype +
                    "' was not found in source file: " +
                    resolvedFile);
            }


            LogHelper.LogInfo(
                "[RuntimePlanetGenerator] Loaded source generator directly " +
                "from content XML: Subtype='" +
                subtype +
                "', File='" +
                resolvedFile +
                "', FolderName='" +
                builder.FolderName +
                "'");


            // This builder belongs to the temporary Definitions object that
            // was just deserialized above; it is not the live engine definition.
            // Returning it directly is therefore safe. Do not call Clone() here:
            // in this build the object-builder clone path can drop nested Layers
            // from ComplexMaterials, which is exactly what breaks Titan before
            // the Grass overlay is registered.
            return builder;
        }


        private void ReadSourceDefinitionXml(
            MyModContext context,
            string subtype,
            out string xml,
            out string resolvedFile)
        {
            if (MyAPIGateway.Utilities.GamePaths == null)
            {
                throw new Exception(
                    "GamePaths is unavailable while resolving source " +
                    "PlanetGeneratorDefinition XML.");
            }


            string currentFile =
                NormalizePath(
                    context.CurrentFile);


            if (string.IsNullOrWhiteSpace(currentFile))
            {
                throw new Exception(
                    "Source generator context has no CurrentFile. " +
                    "No GetObjectBuilder fallback is allowed.");
            }


            string contentRoot =
                NormalizePath(
                    MyAPIGateway.Utilities.GamePaths.ContentPath);


            string contextRoot =
                NormalizePath(
                    context.ModPath);

            var relativeFiles =
                new List<string>();


            AddRelativeFileFromRoot(
                relativeFiles,
                currentFile,
                contextRoot);

            AddRelativeFileFromRoot(
                relativeFiles,
                currentFile,
                contentRoot);


            if (currentFile.StartsWith(
                    "Data/",
                    StringComparison.OrdinalIgnoreCase) ||
                currentFile.StartsWith(
                    "DLC/",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRelativeFileCandidate(
                    relativeFiles,
                    currentFile);
            }


            AddRelativeFileFromMarker(
                relativeFiles,
                currentFile,
                "/Data/");

            AddRelativeFileFromMarker(
                relativeFiles,
                currentFile,
                "/DLC/");


            // Definition contexts are engine objects and retain native paths,
            // while LinuxCompat deliberately exposes Windows-shaped GamePaths
            // to mods. Classify the base-game context before comparing those
            // two representations; its default ModItem has no Name and cannot
            // be passed to FileExistsInModLocation.
            bool preferGameContent =
                context.IsBaseGame ||
                (!string.IsNullOrWhiteSpace(contentRoot) &&
                 currentFile.StartsWith(
                     contentRoot + "/",
                     StringComparison.OrdinalIgnoreCase));


            if (preferGameContent)
            {
                for (int index = 0;
                     index < relativeFiles.Count;
                     index++)
                {
                    if (TryReadGameContentDefinition(
                            relativeFiles[index],
                            subtype,
                            out xml))
                    {
                        resolvedFile =
                            relativeFiles[index] +
                            " [game content]";

                        return;
                    }
                }
            }
            else
            {
                for (int index = 0;
                     index < relativeFiles.Count;
                     index++)
                {
                    if (TryReadModDefinition(
                            relativeFiles[index],
                            context.ModItem,
                            subtype,
                            out xml))
                    {
                        resolvedFile =
                            relativeFiles[index] +
                            " [definition context mod]";

                        return;
                    }
                }
            }


            // Definition contexts can be incomplete or point at the vanilla
            // file when a loaded mod supplies the live planet definition. As
            // a fallback, probe the candidate path in every loaded mod, just
            // like model-content resolution does for modded LCD blocks.
            if (MyAPIGateway.Session != null &&
                MyAPIGateway.Session.Mods != null)
            {
                for (int fileIndex = 0;
                     fileIndex < relativeFiles.Count;
                     fileIndex++)
                {
                    foreach (MyObjectBuilder_Checkpoint.ModItem mod in
                             MyAPIGateway.Session.Mods)
                    {
                        if (!TryReadModDefinition(
                                relativeFiles[fileIndex],
                                mod,
                                subtype,
                                out xml))
                        {
                            continue;
                        }

                        resolvedFile =
                            relativeFiles[fileIndex] +
                            " [loaded mod " +
                            mod.PublishedFileId +
                            "]";

                        return;
                    }
                }
            }


            if (!preferGameContent)
            {
                for (int index = 0;
                     index < relativeFiles.Count;
                     index++)
                {
                    if (TryReadGameContentDefinition(
                            relativeFiles[index],
                            subtype,
                            out xml))
                    {
                        resolvedFile =
                            relativeFiles[index] +
                            " [game-content fallback]";

                        return;
                    }
                }
            }


            throw new Exception(
                "Could not resolve source PlanetGeneratorDefinition file. " +
                "CurrentFile='" +
                currentFile +
                "', ContentPath='" +
                contentRoot +
                "', ModPath='" +
                contextRoot +
                "'.");
        }


        private static MyObjectBuilder_PlanetGeneratorDefinition
            DeserializeSourceBuilder(
                string xml,
                string subtype)
        {
            MyObjectBuilder_Definitions definitions =
                MyAPIGateway.Utilities
                    .SerializeFromXML<MyObjectBuilder_Definitions>(
                        xml);


            if (definitions == null)
                return null;


            MyObjectBuilder_PlanetGeneratorDefinition builder =
                null;


            // Keen's PlanetGeneratorDefinitions.sbc currently mixes both XML
            // layouts in the same file. Generic Definition elements deserialize
            // into Definitions[], while explicit PlanetGeneratorDefinition
            // elements deserialize into PlanetGeneratorDefinitions[].
            if (definitions.Definitions != null)
            {
                builder =
                    definitions.Definitions
                        .OfType<MyObjectBuilder_PlanetGeneratorDefinition>()
                        .FirstOrDefault(x =>
                            x.Id.SubtypeName.Equals(
                                subtype,
                                StringComparison.OrdinalIgnoreCase));
            }


            if (builder == null &&
                definitions.PlanetGeneratorDefinitions != null)
            {
                builder =
                    definitions.PlanetGeneratorDefinitions
                        .FirstOrDefault(x =>
                            x != null &&
                            x.Id.SubtypeName.Equals(
                                subtype,
                                StringComparison.OrdinalIgnoreCase));
            }


            return builder;
        }


        private static bool TryReadGameContentDefinition(
            string relativeFile,
            string subtype,
            out string xml)
        {
            xml =
                null;


            try
            {
                using (TextReader reader =
                       MyAPIGateway.Utilities.ReadFileInGameContent(
                           relativeFile))
                {
                    string candidate =
                        reader.ReadToEnd();

                    if (DeserializeSourceBuilder(
                            candidate,
                            subtype) == null)
                    {
                        return false;
                    }

                    xml =
                        candidate;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }


        private static bool TryReadModDefinition(
            string relativeFile,
            MyObjectBuilder_Checkpoint.ModItem mod,
            string subtype,
            out string xml)
        {
            xml =
                null;


            try
            {
                using (TextReader reader =
                       MyAPIGateway.Utilities.ReadFileInModLocation(
                           relativeFile,
                           mod))
                {
                    string candidate =
                        reader.ReadToEnd();

                    if (DeserializeSourceBuilder(
                            candidate,
                            subtype) == null)
                    {
                        return false;
                    }

                    xml =
                        candidate;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }


        private static void AddRelativeFileFromRoot(
            List<string> candidates,
            string currentFile,
            string root)
        {
            if (string.IsNullOrWhiteSpace(root) ||
                !currentFile.StartsWith(
                    root + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }


            AddRelativeFileCandidate(
                candidates,
                currentFile.Substring(
                    root.Length + 1));
        }


        private static void AddRelativeFileFromMarker(
            List<string> candidates,
            string currentFile,
            string marker)
        {
            int markerIndex =
                currentFile.IndexOf(
                    marker,
                    StringComparison.OrdinalIgnoreCase);

            if (markerIndex < 0)
                return;


            AddRelativeFileCandidate(
                candidates,
                currentFile.Substring(
                    markerIndex + 1));
        }


        private static void AddRelativeFileCandidate(
            List<string> candidates,
            string relativeFile)
        {
            if (string.IsNullOrWhiteSpace(relativeFile))
                return;


            string normalized =
                NormalizePath(
                        relativeFile)
                    .TrimStart('/');


            if (!candidates.Any(x =>
                    x.Equals(
                        normalized,
                        StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(
                    normalized);
            }
        }


        internal string BuildWorldStoragePath(
            string savePath,
            string fileName)
        {
            if (MyAPIGateway.Utilities == null ||
                MyAPIGateway.Utilities.GamePaths == null ||
                string.IsNullOrWhiteSpace(savePath) ||
                string.IsNullOrWhiteSpace(
                    MyAPIGateway.Utilities.GamePaths.ModScopeName))
            {
                throw new Exception(
                    "Could not construct absolute world-storage file path.");
            }


            string worldSavePath;

            string reportedSavePath =
                ResolveWorldSavePath(savePath);

            if (!string.IsNullOrWhiteSpace(_verifiedWorldSavePath) &&
                string.Equals(reportedSavePath, _verifiedReportedSavePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                worldSavePath =
                    _verifiedWorldSavePath;
            }
            else
            {
                worldSavePath =
                    ResolveAccessibleWorldSavePath(
                        reportedSavePath,
                        fileName);
            }


            return
                worldSavePath +
                "/Storage/" +
                MyAPIGateway.Utilities.GamePaths.ModScopeName +
                "/" +
                fileName;
        }


        private string ResolveAccessibleWorldSavePath(
            string reportedSavePath,
            string fileName)
        {
            if (WorldStorageFileExists(reportedSavePath, fileName))
                return reportedSavePath;

            string enumeratedSavePath = ResolveEnumeratedWorldSavePath(
                fileName, false);

            if (!string.IsNullOrWhiteSpace(enumeratedSavePath))
            {
                MyLog.Default.Log(MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] Runtime archive is not visible at " +
                    "the reported save path. Using enumerated world-storage " +
                    "path. CurrentPath='" + reportedSavePath +
                    "', EnumeratedPath='" + enumeratedSavePath +
                    "', Archive='" + fileName + "'.");

                return enumeratedSavePath;
            }

            string sessionSavePath = ResolveSessionNamedSavePath();

            if (string.IsNullOrWhiteSpace(sessionSavePath) ||
                string.Equals(reportedSavePath, sessionSavePath,
                    StringComparison.OrdinalIgnoreCase) ||
                !WorldStorageFileExists(sessionSavePath, fileName))
            {
                return reportedSavePath;
            }

            MyLog.Default.Log(MyLogSeverity.Warning,
                "[RuntimePlanetGenerator] Runtime archive is not visible at " +
                "the reported save path. Using session-name world-storage " +
                "fallback. CurrentPath='" + reportedSavePath +
                "', SessionPath='" + sessionSavePath +
                "', Archive='" + fileName + "'.");

            return sessionSavePath;
        }

        private string ResolveEnumeratedWorldSavePath(
            string fileName,
            bool uniqueFileName)
        {
            string savesRoot = null;
            try
            {
                if (MyAPIGateway.Utilities == null ||
                    MyAPIGateway.Utilities.GamePaths == null ||
                    string.IsNullOrWhiteSpace(fileName) ||
                    string.IsNullOrWhiteSpace(
                        MyAPIGateway.Utilities.GamePaths.UserDataPath) ||
                    string.IsNullOrWhiteSpace(
                        MyAPIGateway.Utilities.GamePaths.ModScopeName))
                {
                    return null;
                }

                savesRoot = NormalizePath(Path.Combine(
                    MyAPIGateway.Utilities.GamePaths.UserDataPath,
                    "Saves"));
                string leafName = Path.GetFileName(fileName);
                string storageSuffix = "/Storage/" +
                                       MyAPIGateway.Utilities.GamePaths.ModScopeName +
                                       "/" + leafName;

                string[] foundFiles = PathUtils.GetFilesRecursively(
                    savesRoot, leafName);

                TraceProbe("Enumerate", savesRoot,
                    savesRoot, leafName,
                    foundFiles == null
                        ? "enumeration returned null"
                        : foundFiles.Length + " file(s) found");

                if (foundFiles == null)
                    return null;

                string resolved = null;
                foreach (string foundFile in foundFiles)
                {
                    string normalized = NormalizePath(foundFile);

                    if (string.IsNullOrWhiteSpace(normalized) ||
                        !normalized.StartsWith(savesRoot + "/",
                            StringComparison.OrdinalIgnoreCase) ||
                        !normalized.EndsWith(storageSuffix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        TraceProbe("Enumerate", savesRoot, foundFile,
                            leafName, "skipped; outside '" + savesRoot +
                                      "' or not under '" + storageSuffix + "'");

                        continue;
                    }

                    string candidate = normalized.Substring(
                        0, normalized.Length - storageSuffix.Length);
                    if (candidate.Length <= savesRoot.Length + 1 ||
                        !(uniqueFileName ||
                          WorldStorageFileExists(candidate, leafName)))
                    {
                        TraceProbe("Enumerate", candidate, foundFile,
                            leafName, "skipped; candidate rejected");

                        continue;
                    }

                    TraceProbe("Enumerate", candidate, foundFile,
                        leafName, "candidate accepted");

                    if (resolved != null &&
                        !string.Equals(resolved, candidate,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        MyLog.Default.Log(MyLogSeverity.Warning,
                            "[RuntimePlanetGenerator] Multiple world-storage paths " +
                            "matched the enumerated file '" + leafName +
                            "'; refusing to choose one. FirstPath='" + resolved +
                            "', OtherPath='" + candidate + "'.");
                        return null;
                    }

                    resolved = candidate;
                }

                return resolved;
            }
            catch (Exception e)
            {
                MyLog.Default.Log(MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] Could not enumerate world-storage " +
                    "files under '" + (savesRoot ?? "<unresolved>") +
                    "': " + e);
                return null;
            }
        }


        private static bool WorldStorageFileExists(
            string savePath,
            string fileName)
        {
            if (MyAPIGateway.Utilities == null ||
                MyAPIGateway.Utilities.GamePaths == null ||
                string.IsNullOrWhiteSpace(savePath) ||
                string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(
                    MyAPIGateway.Utilities.GamePaths.ModScopeName))
            {
                return false;
            }

            string probeMod = null;
            try
            {
                MyObjectBuilder_Checkpoint.ModItem mod =
                    CreateWorldStorageProbeMod(savePath);
                probeMod = mod.Name;

                bool exists = MyAPIGateway.Utilities.FileExistsInModLocation(
                    Path.GetFileName(fileName), mod);

                TraceProbe("FileExistsInModLocation", savePath, probeMod,
                    fileName, exists ? "found" : "missing");

                return exists;
            }
            catch (Exception e)
            {
                TraceProbe("FileExistsInModLocation", savePath, probeMod,
                    fileName, "threw " + e.Message);

                return false;
            }
        }


        private static MyObjectBuilder_Checkpoint.ModItem CreateWorldStorageProbeMod(
            string savePath)
        {
            string storageRoot =
                NormalizePath(savePath) +
                "/Storage/" +
                MyAPIGateway.Utilities.GamePaths.ModScopeName;

            return new MyObjectBuilder_Checkpoint.ModItem(
                storageRoot.Replace('/', '\\'), 0, null);
        }


        /// <summary>
        /// Writes a canary with a GUID-suffixed name through Keen's world-storage
        /// API, then checks the reported path, enumerated save folders, and
        /// session-name path by file name only. The unique name avoids accepting
        /// a stale sentinel left in a previous save folder after a startup path
        /// transition, The canary is deleted once the self-test finishes.
        /// False means the reported path remains unverified (the mod *could still theoretically work*)
        /// </summary>
        internal bool RunWorldStorageSelfTest(
            string reportedSavePath,
            string phase)
        {
            if (MyAPIGateway.Utilities == null ||
                MyAPIGateway.Utilities.GamePaths == null)
            {
                _verifiedWorldSavePath =
                    null;
                _verifiedReportedSavePath =
                    null;

                return false;
            }

            string reported = ResolveWorldSavePath(reportedSavePath);

            string sentinelFile =
                WORLD_STORAGE_SENTINEL_PREFIX +
                Guid.NewGuid().ToString("N") +
                WORLD_STORAGE_SENTINEL_EXTENSION;

            try
            {
                using (TextWriter writer =
                       MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           sentinelFile,
                           typeof(CubemapApiServer)))
                {
                    writer.Write(
                        phase ?? "startup");
                }
            }
            catch (Exception e)
            {
                _verifiedWorldSavePath =
                    null;
                _verifiedReportedSavePath =
                    null;

                MyLog.Default.Log(
                    MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] World-storage self-test '" +
                    phase +
                    "' could not write " +
                    sentinelFile +
                    ": " +
                    e.Message);

                return false;
            }

            try
            {
                return ResolveWorldStorageSentinel(
                    reported,
                    phase,
                    sentinelFile);
            }
            finally
            {
                DeleteWorldStorageSentinel(
                    sentinelFile);
            }
        }


        private bool ResolveWorldStorageSentinel(
            string reported,
            string phase,
            string sentinelFile)
        {
            string resolved;

            string resolutionSource =
                null;

            if (WorldStorageSentinelExists(
                    reported,
                    sentinelFile))
            {
                resolved =
                    reported;
                resolutionSource =
                    "reported";
            }
            else
            {
                resolved = ResolveEnumeratedWorldSavePath(
                    sentinelFile, true);
                if (resolved != null)
                    resolutionSource = "enumerated";
            }

            string sessionNamed = resolved == null
                ? ResolveSessionNamedSavePath()
                : null;
            if (resolved == null &&
                !string.IsNullOrWhiteSpace(sessionNamed) &&
                !string.Equals(reported, sessionNamed,
                    StringComparison.OrdinalIgnoreCase) &&
                WorldStorageSentinelExists(sessionNamed, sentinelFile))
            {
                resolved = sessionNamed;
                resolutionSource = "session-name";
            }

            if (string.IsNullOrWhiteSpace(
                    resolved))
            {
                _verifiedWorldSavePath =
                    null;
                _verifiedReportedSavePath =
                    null;

                MyLog.Default.Log(
                    MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] World-storage self-test '" +
                    phase +
                    "' could not locate the freshly written canary. " +
                    "ReportedPath='" +
                    (reported ?? "<null>") +
                    "', SessionPath='" +
                    (sessionNamed ?? "<null>") +
                    "'.");

                return false;
            }

            string previous =
                _verifiedWorldSavePath;

            _verifiedWorldSavePath =
                NormalizePath(
                    resolved);
            _verifiedReportedSavePath =
                reported;

            MyLog.Default.Log(
                string.Equals(
                    reported,
                    _verifiedWorldSavePath,
                    StringComparison.OrdinalIgnoreCase)
                    ? MyLogSeverity.Info
                    : MyLogSeverity.Warning, "[RuntimePlanetGenerator] World-storage self-test '" +
                                             phase + "' resolved physical save path via " +
                                             resolutionSource +
                                             " lookup: '" +
                                             _verifiedWorldSavePath + "'." +
                                             (string.IsNullOrWhiteSpace(previous) ||
                                              string.Equals(
                                                  previous,
                                                  _verifiedWorldSavePath,
                                                  StringComparison.OrdinalIgnoreCase)
                                                 ? string.Empty
                                                 : " Previous verified path was '" +
                                                   previous + "'."));

            return true;
        }


        /// <summary>
        /// Looks for the uniquely named canary under the candidate's
        /// world-storage folder by file name only.
        /// </summary>
        private static bool WorldStorageSentinelExists(
            string savePath,
            string sentinelFile)
        {
            if (MyAPIGateway.Utilities == null ||
                MyAPIGateway.Utilities.GamePaths == null ||
                string.IsNullOrWhiteSpace(savePath) ||
                string.IsNullOrWhiteSpace(sentinelFile) ||
                string.IsNullOrWhiteSpace(
                    MyAPIGateway.Utilities.GamePaths.ModScopeName))
            {
                return false;
            }

            string storageRoot =
                NormalizePath(savePath) +
                "/Storage/" +
                MyAPIGateway.Utilities.GamePaths.ModScopeName;
            string expected =
                storageRoot + "/" + sentinelFile;

            try
            {
                string[] foundFiles = PathUtils.GetFilesRecursively(
                    storageRoot, sentinelFile);
                if (foundFiles == null)
                {
                    TraceProbe("Sentinel", savePath,
                        storageRoot, sentinelFile,
                        "enumeration returned null");

                    return false;
                }

                foreach (string foundFile in foundFiles)
                {
                    if (string.Equals(NormalizePath(foundFile), expected,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        TraceProbe("Sentinel", savePath,
                            storageRoot, sentinelFile,
                            "found at '" + foundFile + "'");

                        return true;
                    }
                }

                TraceProbe("Sentinel", savePath,
                    storageRoot, sentinelFile,
                    "missing; expected '" + expected + "', found [" +
                    string.Join(", ", foundFiles) + "]");

                return false;
            }
            catch (Exception e)
            {
                TraceProbe("Sentinel", savePath, storageRoot, sentinelFile,
                    "threw " + e.Message);

                return false;
            }
        }


        private static void DeleteWorldStorageSentinel(
            string sentinelFile)
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(
                        sentinelFile,
                        typeof(CubemapApiServer)))
                {
                    MyAPIGateway.Utilities.DeleteFileInWorldStorage(
                        sentinelFile,
                        typeof(CubemapApiServer));
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Log(MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] Could not delete world-storage " +
                    "canary '" + sentinelFile + "': " + e.Message);
            }
        }


        private static string ResolveSessionNamedSavePath()
        {
            try
            {
                if (MyAPIGateway.Session == null ||
                    MyAPIGateway.Utilities == null ||
                    MyAPIGateway.Utilities.GamePaths == null)
                {
                    return null;
                }

                string savesRoot =
                    NormalizePath(
                        MyAPIGateway.Utilities.GamePaths.SavesPath);

                string saveName =
                    MyAPIGateway.Session.Name;

                if (string.IsNullOrWhiteSpace(savesRoot) ||
                    string.IsNullOrWhiteSpace(saveName))
                {
                    return null;
                }

                const string workshopPrefix =
                    "(Workshop) ";

                saveName =
                    saveName.StartsWith(workshopPrefix,
                        StringComparison.OrdinalIgnoreCase)
                        ? saveName.Substring(workshopPrefix.Length)
                        : saveName;

                saveName =
                    saveName.Replace(':', '-');

                return savesRoot.TrimEnd('/') + "/" + saveName;
            }
            catch (Exception e)
            {
                MyLog.Default.Log(MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] Could not resolve the " +
                    "session-name world-storage fallback: " + e);
                return null;
            }
        }


        private static string ResolveWorldSavePath(
            string fallbackSavePath)
        {
            string normalizedFallback =
                NormalizePath(
                    fallbackSavePath);

            // Runtime generator paths must match the save path supplied to the
            // world-storage API. Workshop startup can temporarily make the
            // engine's private WorldSavePath diverge from CurrentPath; the
            // server normalizes that transition before archive processing.
            return normalizedFallback;
        }


        internal string ResolveInitialSavePath()
        {
            string currentPath =
                NormalizePath(
                    MyAPIGateway.Session.CurrentPath);

            if (!string.IsNullOrWhiteSpace(currentPath))
                return currentPath;


            // During the earliest LoadData() phase CurrentPath can still be
            // unresolved. Use the normal saves root + current save/session
            // name so persisted runtime generators can be registered before planet
            // entities resolve their generator definition.
            if (MyAPIGateway.Utilities.GamePaths == null)
            {
                throw new Exception(
                    "GamePaths is unavailable while resolving initial save path.");
            }

            string savesRoot =
                NormalizePath(
                    MyAPIGateway.Utilities.GamePaths.SavesPath);

            string saveName =
                MyAPIGateway.Session.Name;

            if (string.IsNullOrWhiteSpace(savesRoot) ||
                string.IsNullOrWhiteSpace(saveName))
            {
                throw new Exception(
                    "Neither CurrentPath nor SavesPath + Session.Name can " +
                    "resolve the initial save path.");
            }

            string fallback =
                savesRoot.TrimEnd('/') +
                "/" +
                saveName;

            LogHelper.Log(
                MyLogSeverity.Warning,
                "[RuntimePlanetGenerator] CurrentPath unresolved during LoadData; " +
                "using initial save path fallback: " +
                fallback);

            return fallback;
        }


        internal void RebindToSavePath(
            string savePath)
        {
            savePath =
                NormalizePath(
                    savePath);

            if (string.IsNullOrWhiteSpace(savePath))
                return;


            // With no persisted runtime generators there is no cache to move.
            // Avoid touching world storage during the workshop save transition;
            // the first committed runtime package will create its cache files.
            if (_runtimePackages.Settings.PlanetBuilders.Count > 0)
            {
                _runtimePackages.RecreateWorldStorageCache(
                    false);
            }


            foreach (var entry in _runtimePackages.Settings.PlanetBuilders.Where(entry => entry != null))
            {
                RebindGeneratorArchive(
                    entry.Subtype,
                    BuildWorldStoragePath(
                        savePath,
                        entry.ArchiveFile));
            }


            _runtimePackages.BoundSavePath =
                savePath;


            LogHelper.LogInfo(
                "[RuntimePlanetGenerator] Runtime generator save path rebound: " +
                "SavePath='" +
                _runtimePackages.BoundSavePath +
                "', Generators=" +
                _runtimePackages.Settings.PlanetBuilders.Count);
        }


        private void RebindGeneratorArchive(
            string subtype,
            string absoluteArchivePath)
        {
            MyPlanetGeneratorDefinition generator =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x.Id.SubtypeName.Equals(
                            subtype,
                            StringComparison.OrdinalIgnoreCase));

            if (generator == null)
                return;

            string providerPath =
                RuntimeArchiveLayout.GetProviderPath(
                    absoluteArchivePath);

            MyObjectBuilder_PlanetTextureMapProvider provider =
                generator.MapProvider as
                    MyObjectBuilder_PlanetTextureMapProvider;

            if (string.Equals(
                    NormalizePath(
                        generator.Context == null
                            ? null
                            : generator.Context.ModPath),
                    NormalizePath(
                        absoluteArchivePath),
                    StringComparison.OrdinalIgnoreCase) &&
                provider != null &&
                string.Equals(
                    provider.Path,
                    providerPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            BindGeneratorArchive(
                generator,
                subtype,
                absoluteArchivePath);

            generator.Postprocess();
        }


        internal static string NormalizePath(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return path
                .Replace('\\', '/')
                .TrimEnd('/');
        }


        /// <summary>
        /// Debug-only trace of one world-storage path check; LogHelper drops
        /// Debug severity outside DEBUG builds.
        /// </summary>
        private static void TraceProbe(
            string check,
            string savePath,
            string probePath,
            string fileName,
            string result)
        {
            LogHelper.Log(
                MyLogSeverity.Debug,
                "[RuntimePlanetGenerator] Path probe (" + check + "): " +
                "SavePath='" + (savePath ?? "<null>") +
                "', ProbePath='" + (probePath ?? "<null>") +
                "', File='" + (fileName ?? "<null>") +
                "' -> " + result);
        }


        internal MyPlanetGeneratorDefinition RegisterDefinition(MyObjectBuilder_PlanetGeneratorDefinition sourceBuilder,
            string subtype,
            string absoluteArchivePath)
        {
            MyPlanetGeneratorDefinition existing =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x.Id.SubtypeName.Equals(
                            subtype,
                            StringComparison.OrdinalIgnoreCase));


            if (existing != null)
            {
                BindGeneratorArchive(
                    existing,
                    subtype,
                    absoluteArchivePath);

                existing.Postprocess();

                LogHelper.Log(
                    MyLogSeverity.Debug,
                    "[RuntimePlanetGenerator] Reusing already registered generator: " +
                    subtype);

                return existing;
            }


            // Do NOT clone the complete planet generator here.
            //
            // MyObjectBuilder_Base.Clone() goes through Keen's object-builder
            // serializer. In the current game build that clone path drops the
            // Layers payload from MyPlanetMaterialPlacementRule entries nested
            // in ComplexMaterials. CustomMaterialTable entries can survive,
            // which is why some source planets appeared to work while their
            // original rock/sand/etc. rules disappeared during Init().
            //
            // Init() only needs the runtime Id and texture-map provider while it
            // consumes the builder. Temporarily override those fields on the
            // already valid captured/persisted builder, then restore the portable
            // values immediately afterwards. This keeps the full material
            // rule/layer graph intact and uses only ModAPI-whitelisted members.
            SerializableDefinitionId originalId =
                sourceBuilder.Id;

            string originalFolderName =
                sourceBuilder.FolderName;

            MyObjectBuilder_PlanetMapProvider originalMapProvider =
                sourceBuilder.MapProvider;


            sourceBuilder.Id =
                new SerializableDefinitionId(
                    typeof(MyObjectBuilder_PlanetGeneratorDefinition),
                    subtype);


            RuntimeArchiveLayout.BindBuilder(
                sourceBuilder,
                absoluteArchivePath);

            MyModContext runtimeContext =
                CreateRuntimeContext(
                    subtype,
                    absoluteArchivePath);


            var runtimeGenerator =
                new MyPlanetGeneratorDefinition();


            try
            {
                runtimeGenerator.Init(
                    sourceBuilder,
                    runtimeContext);

                runtimeGenerator.Postprocess();
            }
            finally
            {
                // Keep the persisted/captured builder portable and reusable.
                // RuntimeGenerator.Init() has already copied the runtime values.
                sourceBuilder.Id =
                    originalId;

                sourceBuilder.FolderName =
                    originalFolderName;

                sourceBuilder.MapProvider =
                    originalMapProvider;
            }


            MyDefinitionManager.Static
                .Definitions
                .AddDefinition(
                    runtimeGenerator);


            MyPlanetGeneratorDefinition registered =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x.Id.SubtypeName.Equals(
                            subtype,
                            StringComparison.OrdinalIgnoreCase));


            if (registered == null)
            {
                throw new Exception(
                    "Definition manager did not expose '" +
                    subtype +
                    "' after AddDefinition().");
            }


            LogHelper.LogInfo(
                "[RuntimePlanetGenerator] Registered '" +
                subtype +
                "' with provider Path='" +
                ((MyObjectBuilder_PlanetTextureMapProvider)
                    registered.MapProvider).Path +
                "', ModPath='" +
                registered.Context.ModPath +
                "'.");


            return registered;
        }


        internal void BindGeneratorArchive(
            MyPlanetGeneratorDefinition generator,
            string subtype,
            string absoluteArchivePath)
        {
            if (generator == null)
                throw new ArgumentNullException(nameof(generator));

            string providerPath =
                RuntimeArchiveLayout.GetProviderPath(
                    absoluteArchivePath);

            generator.FolderName =
                providerPath;

            generator.MapProvider =
                RuntimeArchiveLayout.CreateTextureMapProvider(
                    absoluteArchivePath);

            string contextName =
                BuildRuntimeContextName(
                    subtype,
                    absoluteArchivePath);

            MyModContext context =
                generator.Context;

            if (context == null ||
                !string.Equals(
                    context.ModName,
                    contextName,
                    StringComparison.Ordinal))
            {
                context =
                    new MyModContext();

                generator.Context =
                    context;
            }

            context.Init(
                contextName,
                _modContext.CurrentFile,
                absoluteArchivePath);
        }


        private MyModContext CreateRuntimeContext(
            string subtype,
            string absoluteArchivePath)
        {
            var context =
                new MyModContext();

            context.Init(
                BuildRuntimeContextName(
                    subtype,
                    absoluteArchivePath),
                _modContext.CurrentFile,
                absoluteArchivePath);

            return context;
        }


        private static string BuildRuntimeContextName(
            string subtype,
            string absoluteArchivePath)
        {
            return
                "ProceduralCubemapApi.Runtime." +
                subtype +
                "." +
                RuntimeArchiveLayout.GetProviderPath(
                    absoluteArchivePath);
        }
    }
}