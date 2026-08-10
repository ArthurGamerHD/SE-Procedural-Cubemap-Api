using Sandbox.Definitions;
using Sandbox.ModAPI;

using System;
using System.IO;
using System.Linq;

using VoxelCubemapApi.Server.PlanetModification.Persistence;

using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace VoxelCubemapApi.Server.PlanetModification.Runtime
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
                throw new ArgumentNullException("runtimePackages");

            if (modContext == null)
                throw new ArgumentNullException("modContext");

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
                throw new ArgumentNullException("sourceGenerator");


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

            ReadSourceDefinitionXml(
                context,
                out xml,
                out resolvedFile);


            MyObjectBuilder_Definitions definitions =
                MyAPIGateway.Utilities
                    .SerializeFromXML<MyObjectBuilder_Definitions>(
                        xml);


            if (definitions == null)
            {
                throw new Exception(
                    "Source definition file did not deserialize as Definitions: " +
                    resolvedFile);
            }


            string subtype =
                sourceGenerator.Id.SubtypeName;


            MyObjectBuilder_PlanetGeneratorDefinition builder =
                null;


            // Keen's PlanetGeneratorDefinitions.sbc currently mixes both XML
            // layouts in the same file:
            //
            //   <Definition xsi:type="PlanetGeneratorDefinition">
            //
            // deserializes into MyObjectBuilder_Definitions.Definitions, while:
            //
            //   <PlanetGeneratorDefinitions>
            //       <PlanetGeneratorDefinition>
            //
            // deserializes into PlanetGeneratorDefinitions.
            //
            // EarthLike is in the generic Definitions[] collection.
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


            // Definition contexts are engine objects and retain native paths,
            // while LinuxCompat deliberately exposes Windows-shaped GamePaths
            // to mods. Classify the base-game context before comparing those
            // two representations; its default ModItem has no Name and cannot
            // be passed to FileExistsInModLocation.
            if (context.IsBaseGame &&
                !string.IsNullOrWhiteSpace(contextRoot) &&
                currentFile.StartsWith(
                    contextRoot + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                string relativeFile =
                    currentFile.Substring(
                        contextRoot.Length + 1);


                ReadGameContentText(
                    relativeFile,
                    out xml);

                resolvedFile =
                    relativeFile;

                return;
            }


            // Vanilla + DLC: CurrentFile should resolve under the real game
            // Content directory. Strip only the content root and let ModAPI
            // read the SBC itself.
            if (!string.IsNullOrWhiteSpace(contentRoot) &&
                currentFile.StartsWith(
                    contentRoot + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                string relativeFile =
                    currentFile.Substring(
                        contentRoot.Length + 1);


                ReadGameContentText(
                    relativeFile,
                    out xml);

                resolvedFile =
                    relativeFile;

                return;
            }


            // Some contexts expose CurrentFile already content-relative.
            if (currentFile.StartsWith(
                "Data/",
                StringComparison.OrdinalIgnoreCase) ||
                currentFile.StartsWith(
                    "DLC/",
                    StringComparison.OrdinalIgnoreCase))
            {
                ReadGameContentText(
                    currentFile,
                    out xml);

                resolvedFile =
                    currentFile;

                return;
            }


            // Mod planets: read the actual source SBC from that mod.
            string modRoot =
                contextRoot;


            if (!string.IsNullOrWhiteSpace(modRoot) &&
                currentFile.StartsWith(
                    modRoot + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                string relativeFile =
                    currentFile.Substring(
                        modRoot.Length + 1);


                if (!MyAPIGateway.Utilities.FileExistsInModLocation(
                    relativeFile,
                    context.ModItem))
                {
                    throw new Exception(
                        "Source planet definition file does not exist in " +
                        "mod content: " +
                        relativeFile);
                }


                using (TextReader reader =
                    MyAPIGateway.Utilities.ReadFileInModLocation(
                        relativeFile,
                        context.ModItem))
                {
                    xml =
                        reader.ReadToEnd();
                }


                resolvedFile =
                    relativeFile;

                return;
            }


            throw new Exception(
                "Could not resolve source PlanetGeneratorDefinition file. " +
                "CurrentFile='" +
                currentFile +
                "', ContentPath='" +
                contentRoot +
                "', ModPath='" +
                modRoot +
                "'.");
        }


        private static void ReadGameContentText(
            string relativeFile,
            out string xml)
        {
            if (!MyAPIGateway.Utilities.FileExistsInGameContent(
                relativeFile))
            {
                throw new Exception(
                    "Source planet definition file does not exist in game " +
                    "content: " +
                    relativeFile);
            }


            using (TextReader reader =
                MyAPIGateway.Utilities.ReadFileInGameContent(
                    relativeFile))
            {
                xml =
                    reader.ReadToEnd();
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


            return
                NormalizePath(savePath) +
                "/Storage/" +
                MyAPIGateway.Utilities.GamePaths.ModScopeName +
                "/" +
                fileName;
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


        private static void VerifyBuilderGrassOverlayLookup(
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            byte overlayValue)
        {
            if (builder == null)
                throw new ArgumentNullException("builder");


            MyPlanetMaterialGroup groupOverlay =
                builder.ComplexMaterials == null
                    ? null
                    : builder.ComplexMaterials
                        .FirstOrDefault(x =>
                            x != null &&
                            x.Value == overlayValue);


            if (groupOverlay == null)
            {
                throw new Exception(
                    "Builder terraform surface overlay red=" +
                    overlayValue +
                    " is missing from ComplexMaterials.");
            }


            if (groupOverlay.MaterialRules == null ||
                groupOverlay.MaterialRules.Length == 0)
            {
                throw new Exception(
                    "Builder terraform surface overlay group red=" +
                    overlayValue +
                    " has no material rules.");
            }


            int materialRuleCount =
                0;


            for (int i = 0;
                i < groupOverlay.MaterialRules.Length;
                i++)
            {
                MyPlanetMaterialPlacementRule rule =
                    groupOverlay.MaterialRules[i];


                if (rule != null &&
                    !string.IsNullOrWhiteSpace(
                        rule.FirstOrDefault))
                {
                    materialRuleCount++;
                }
            }


            if (materialRuleCount == 0)
            {
                throw new Exception(
                    "Builder terraform surface overlay group red=" +
                    overlayValue +
                    " contains no material-bearing rules.");
            }


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Verified builder XML surface overlay: red=" +
                overlayValue +
                ", rules=" +
                groupOverlay.MaterialRules.Length +
                ", material-bearing rules=" +
                materialRuleCount +
                ".");
        }


        internal MyPlanetGeneratorDefinition RegisterDefinition(
            MyObjectBuilder_PlanetGeneratorDefinition sourceBuilder,
            string subtype,
            string absolutePlanetDataFolder,
            byte grassMaterialMapValue,
            bool verifyGrassOverlay = true)
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


            if (verifyGrassOverlay)
            {
                VerifyBuilderGrassOverlayLookup(
                    sourceBuilder,
                    grassMaterialMapValue);
            }


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


            if (verifyGrassOverlay)
            {
                MyPlanetMaterialGroup runtimeSurfaceGroup =
                    runtimeGenerator.MaterialGroups == null
                        ? null
                        : runtimeGenerator.MaterialGroups
                            .FirstOrDefault(x =>
                                x != null &&
                                x.Value == grassMaterialMapValue);


                if (runtimeSurfaceGroup != null &&
                    runtimeSurfaceGroup.MaterialRules != null &&
                    runtimeSurfaceGroup.MaterialRules.Length > 0)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Runtime XML surface overlay survived " +
                        "Init/Postprocess: red=" +
                        grassMaterialMapValue +
                        ", rules=" +
                        runtimeSurfaceGroup.MaterialRules.Length +
                        ".");
                }
                else
                {
                    // The authoritative validation is performed on the exact builder
                    // immediately before Init(). Keep this diagnostic non-fatal because
                    // runtime definition postprocessing may normalize rule storage.
                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Runtime XML surface overlay rule list " +
                        "is not exposed after Init/Postprocess for red=" +
                        grassMaterialMapValue +
                        ". Builder overlay was verified; continuing.");
                }
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
