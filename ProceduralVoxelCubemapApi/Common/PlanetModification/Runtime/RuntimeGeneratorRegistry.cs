using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace VoxelCubemapApi.Common.PlanetModification.Runtime
{
    internal sealed class RuntimeGeneratorRegistry
    {
        private readonly RuntimePackageStore _runtimePackages;
        private readonly MyModContext _modContext;


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


            MyLog.Default.WriteLineAndConsole(
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
            if (MyAPIGateway.Utilities.GamePaths == null ||
                string.IsNullOrWhiteSpace(savePath) ||
                string.IsNullOrWhiteSpace(
                    MyAPIGateway.Utilities.GamePaths.ModScopeName))
            {
                throw new Exception(
                    "Could not construct absolute world-storage file path.");
            }


            string worldSavePath =
                ResolveWorldSavePath(
                    savePath);


            return
                worldSavePath +
                "/Storage/" +
                MyAPIGateway.Utilities.GamePaths.ModScopeName +
                "/" +
                fileName;
        }


        private static string ResolveWorldSavePath(
            string fallbackSavePath)
        {
            string normalizedFallback =
                NormalizePath(
                    fallbackSavePath);

            // A joining client stores the downloaded world under its own
            // CurrentPath. That local directory is not necessarily derived
            // from the server's checkpoint Session.Name (notably, ':' may be
            // removed instead of replaced with '-'). Runtime replay files are
            // written through the client's world-storage API, so their rooted
            // generator path must use that same local directory.
            if (MyAPIGateway.Session != null &&
                !MyAPIGateway.Session.IsServer &&
                !string.IsNullOrWhiteSpace(
                    normalizedFallback))
            {
                return normalizedFallback;
            }

            string savesRoot =
                NormalizePath(
                    MyAPIGateway.Utilities.GamePaths.SavesPath);

            string sessionName =
                MyAPIGateway.Session == null
                    ? null
                    : MyAPIGateway.Session.Name;


            if (string.IsNullOrWhiteSpace(savesRoot) ||
                string.IsNullOrWhiteSpace(sessionName))
            {
                return normalizedFallback;
            }


            // World-storage APIs use MySession.WorldSavePath, which diverges
            // from CurrentPath for workshop worlds. Match the engine's path:
            // SavesPath + checkpoint SessionName with ':' replaced by '-'.
            return
                savesRoot.TrimEnd('/') +
                "/" +
                sessionName.Replace(
                    ':',
                    '-');
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

            MyLog.Default.WriteLineAndConsole(
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


            _runtimePackages.RecreateWorldStorageCache(
                false);


            for (int i = 0;
                i < _runtimePackages.Settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry entry =
                    _runtimePackages.Settings.PlanetBuilders[i];

                if (entry == null)
                    continue;


                RebindGeneratorFolder(
                    entry.Subtype,
                    BuildWorldStoragePath(
                        savePath,
                        entry.ArchiveFile));
            }


            _runtimePackages.BoundSavePath =
                savePath;


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Runtime generator save path rebound: " +
                "SavePath='" +
                _runtimePackages.BoundSavePath +
                "', Generators=" +
                _runtimePackages.Settings.PlanetBuilders.Count);
        }


        private void RebindGeneratorFolder(
            string subtype,
            string absolutePlanetDataFolder)
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

            if (string.Equals(
                NormalizePath(generator.FolderName),
                NormalizePath(absolutePlanetDataFolder),
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            generator.FolderName =
                absolutePlanetDataFolder;

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


        internal MyPlanetGeneratorDefinition RegisterDefinition(MyObjectBuilder_PlanetGeneratorDefinition sourceBuilder,
            string subtype,
            string absolutePlanetDataFolder)
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
                MyLog.Default.WriteLineAndConsole(
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
            // Init() only needs the runtime Id and rooted FolderName while it
            // consumes the builder, and it copies both values into the runtime
            // definition. Temporarily override those two fields on the already
            // valid captured/persisted builder, then restore the portable values
            // immediately afterwards. This keeps the full material rule/layer
            // graph intact and uses only ModAPI-whitelisted members.
            SerializableDefinitionId originalId =
                sourceBuilder.Id;

            string originalFolderName =
                sourceBuilder.FolderName;


            sourceBuilder.Id =
                new SerializableDefinitionId(
                    typeof(MyObjectBuilder_PlanetGeneratorDefinition),
                    subtype);


            // FolderName is the absolute VRage virtual-folder path backed by
            // one deterministic .zip file in this save's world storage.
            sourceBuilder.FolderName =
                absolutePlanetDataFolder;


            var runtimeGenerator =
                new MyPlanetGeneratorDefinition();


            try
            {
                // Use OUR mod context now. The definition itself is complete and
                // its planet maps live at an absolute FolderName, so there is no
                // remaining dependency on the source definition's map folder.
                runtimeGenerator.Init(
                    sourceBuilder,
                    _modContext);

                runtimeGenerator.Postprocess();
            }
            finally
            {
                // Keep the persisted/captured builder portable and reusable.
                // RuntimeGenerator.Init() has already copied Id and FolderName.
                sourceBuilder.Id =
                    originalId;

                sourceBuilder.FolderName =
                    originalFolderName;
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


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Registered '" +
                subtype +
                "' from persisted/captured builder with absolute FolderName='" +
                registered.FolderName +
                "'");


            return registered;
        }


    }
}
