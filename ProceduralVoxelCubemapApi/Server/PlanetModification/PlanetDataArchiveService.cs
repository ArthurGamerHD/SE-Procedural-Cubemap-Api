using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adk.Compression.Zip;
using Adk.Image.Png;
using VoxelCubemapApi.Server.PlanetModification.Maps;
using VoxelCubemapApi.Server.PlanetModification.Persistence;
using VoxelCubemapApi.Server.PlanetModification.Templates;
using VRage.Game;
using VRage.Utils;

namespace VoxelCubemapApi.Server.PlanetModification
{
    internal sealed class PlanetDataArchiveService
    {
        private readonly RuntimePackageStore _runtimePackages;


        internal PlanetDataArchiveService(
            RuntimePackageStore runtimePackages)
        {
            if (runtimePackages == null)
                throw new ArgumentNullException("runtimePackages");

            _runtimePackages =
                runtimePackages;
        }


        internal void CreateModifiedArchive(
            PlanetModificationSnapshot snapshot,
            string archiveFileName)
        {
            byte[] archive =
                BuildModifiedArchive(
                    snapshot,
                    true);

            _runtimePackages.SaveRuntimeArchive(
                archiveFileName,
                archive);
        }


        internal byte[] BuildModifiedArchive(
            PlanetModificationSnapshot snapshot,
            bool resolveFractalThresholds)
        {
            Dictionary<string, byte[]> files;

            return BuildModifiedArchive(
                snapshot,
                resolveFractalThresholds,
                out files);
        }


        internal byte[] BuildModifiedArchive(
            PlanetModificationSnapshot snapshot,
            bool resolveFractalThresholds,
            out Dictionary<string, byte[]> outputFiles)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");


            string[] files =
                PlanetMapFileNames.All;

            var entries =
                new List<MinimalZip.Entry>(
                    files.Length);

            outputFiles =
                new Dictionary<string, byte[]>(
                    files.Length,
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<string, byte[]> runtimeSourceFiles =
                string.IsNullOrWhiteSpace(
                    snapshot.SourceArchiveFile)
                    ? null
                    : ReadRuntimeArchive(
                        snapshot.SourceArchiveFile);

            bool haveBrushOperations =
                snapshot.BrushOperations != null &&
                snapshot.BrushOperations.Count > 0;

            ApplyQueuedBrushes(
                snapshot,
                runtimeSourceFiles);

            if (resolveFractalThresholds)
            {
                ResolveFractalThresholds(
                    snapshot);
            }


            for (int i = 0;
                i < files.Length;
                i++)
            {
                string fileName =
                    files[i];

                PlanarPngBitmap modified =
                    null;

                bool haveModifiedImage =
                    snapshot.Images != null &&
                    snapshot.Images.TryGetValue(
                        fileName,
                        out modified);

                List<Action<int, int, byte[], byte[], byte[], byte[]>> transforms =
                    null;

                bool haveTransforms =
                    snapshot.ImageTransforms != null &&
                    snapshot.ImageTransforms.TryGetValue(
                        fileName,
                        out transforms) &&
                    transforms != null &&
                    transforms.Count > 0;

                bool haveFractalNoise =
                    fileName.EndsWith(
                        "_mat.png",
                        StringComparison.OrdinalIgnoreCase) &&
                    snapshot.FractalNoiseOperations != null &&
                    snapshot.FractalNoiseOperations.Count > 0;

                bool haveBiomeReplacements =
                    fileName.EndsWith(
                        "_mat.png",
                        StringComparison.OrdinalIgnoreCase) &&
                    snapshot.BiomeReplacementOperations != null &&
                    snapshot.BiomeReplacementOperations.Count > 0;

                bool validateAllocatedComplexMaterials =
                    fileName.EndsWith(
                        "_mat.png",
                        StringComparison.OrdinalIgnoreCase) &&
                    !haveBrushOperations &&
                    snapshot.AllocatedComplexMaterialValues != null &&
                    snapshot.AllocatedComplexMaterialValues.Count > 0;


                if ((haveTransforms ||
                        haveFractalNoise ||
                        haveBiomeReplacements ||
                        validateAllocatedComplexMaterials) &&
                    !haveModifiedImage)
                {
                    modified =
                        PlanetMapOperations.DecodePlanetPng(
                            fileName,
                            ReadSnapshotPlanetDataFile(
                                snapshot,
                                runtimeSourceFiles,
                                fileName));

                    haveModifiedImage =
                        true;
                }

                if (validateAllocatedComplexMaterials)
                {
                    PlanetMapOperations.ValidateAllocatedComplexMaterialValues(
                        modified,
                        fileName,
                        snapshot.AllocatedComplexMaterialValues);
                }

                if (haveBiomeReplacements)
                {
                    for (int operationIndex = 0;
                        operationIndex < snapshot.BiomeReplacementOperations.Count;
                        operationIndex++)
                    {
                        PlanetMapOperations.ApplyBiomeReplacementToPlanetImage(
                            modified,
                            snapshot.BiomeReplacementOperations[operationIndex]);
                    }
                }

                if (haveFractalNoise)
                {
                    PlanetMapOperations.ApplyFractalNoiseToPlanetImage(
                        modified,
                        fileName,
                        snapshot.PlanetSeed,
                        snapshot.FractalNoiseOperations);
                }

                if (haveTransforms)
                {
                    for (int transformIndex = 0;
                        transformIndex < transforms.Count;
                        transformIndex++)
                    {
                        transforms[transformIndex](
                            modified.Width,
                            modified.Height,
                            modified.Planes[0],
                            modified.Planes[1],
                            modified.Planes[2],
                            modified.Planes[3]);
                    }
                }

                byte[] data =
                    haveModifiedImage
                        ? modified.Encode()
                        : ReadSnapshotPlanetDataFile(
                            snapshot,
                            runtimeSourceFiles,
                            fileName);


                entries.Add(
                    new MinimalZip.Entry(
                        fileName,
                        data,
                        MinimalZip.CompressionMode.Deflate));

                outputFiles.Add(
                    fileName,
                    data);
            }


            byte[] archive =
                MinimalZip.WriteBytes(
                    entries);

            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Packed modification template " +
                snapshot.TemplateId +
                ": changed PNGs=" +
                entries.Count(x =>
                    (snapshot.Images != null &&
                        snapshot.Images.ContainsKey(x.Name)) ||
                    (snapshot.ImageTransforms != null &&
                        snapshot.ImageTransforms.ContainsKey(x.Name)) ||
                    (x.Name.EndsWith(
                            "_mat.png",
                            StringComparison.OrdinalIgnoreCase) &&
                        ((snapshot.FractalNoiseOperations != null &&
                            snapshot.FractalNoiseOperations.Count > 0) ||
                         (snapshot.BiomeReplacementOperations != null &&
                            snapshot.BiomeReplacementOperations.Count > 0)))) +
                ", archive bytes=" +
                archive.Length +
                ".");

            return archive;
        }


        internal static void ResolveFractalThresholds(
            PlanetModificationSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");

            if (snapshot.FractalNoiseOperations == null)
                return;

            for (int operationIndex = 0;
                operationIndex < snapshot.FractalNoiseOperations.Count;
                operationIndex++)
            {
                FractalNoiseOperation operation =
                    snapshot.FractalNoiseOperations[operationIndex];

                operation.Threshold =
                    FractalBrownianMotion.ComputeGrassCoverageThreshold(
                        snapshot.PlanetSeed,
                        operation.CoveragePercent);
            }
        }


        private void ApplyQueuedBrushes(
            PlanetModificationSnapshot snapshot,
            Dictionary<string, byte[]> runtimeSourceFiles)
        {
            if (snapshot.BrushOperations == null ||
                snapshot.BrushOperations.Count == 0)
            {
                return;
            }

            string[] faces =
            {
                "front",
                "back",
                "left",
                "right",
                "up",
                "down"
            };

            if (snapshot.Images == null)
            {
                snapshot.Images =
                    new Dictionary<string, PlanarPngBitmap>(
                        StringComparer.OrdinalIgnoreCase);
            }

            for (int faceIndex = 0;
                faceIndex < faces.Length;
                faceIndex++)
            {
                string heightFileName =
                    faces[faceIndex] +
                    ".png";

                string materialFileName =
                    faces[faceIndex] +
                    "_mat.png";

                PlanarPngBitmap heightImage =
                    GetOrLoadSnapshotImage(
                        snapshot,
                        runtimeSourceFiles,
                        heightFileName);

                PlanarPngBitmap materialImage =
                    GetOrLoadSnapshotImage(
                        snapshot,
                        runtimeSourceFiles,
                        materialFileName);

                if (snapshot.AllocatedComplexMaterialValues != null &&
                    snapshot.AllocatedComplexMaterialValues.Count > 0)
                {
                    PlanetMapOperations.ValidateAllocatedComplexMaterialValues(
                        materialImage,
                        materialFileName,
                        snapshot.AllocatedComplexMaterialValues);
                }

                PlanetMapOperations.ApplyBrushToPlanetImages(
                    heightImage,
                    materialImage,
                    heightFileName,
                    snapshot.PlanetSeed,
                    snapshot.BrushOperations);
            }
        }


        private PlanarPngBitmap GetOrLoadSnapshotImage(
            PlanetModificationSnapshot snapshot,
            Dictionary<string, byte[]> runtimeSourceFiles,
            string fileName)
        {
            PlanarPngBitmap image;

            if (snapshot.Images.TryGetValue(
                fileName,
                out image))
            {
                return image;
            }

            image =
                PlanetMapOperations.DecodePlanetPng(
                    fileName,
                    ReadSnapshotPlanetDataFile(
                        snapshot,
                        runtimeSourceFiles,
                        fileName));

            snapshot.Images.Add(
                fileName,
                image);

            return image;
        }


        internal byte[] ReadSourceFile(
            MyModContext sourceContext,
            string sourceSubtype,
            string sourceFolderName,
            string fileName)
        {
            string folder =
                sourceFolderName;


            if (string.IsNullOrWhiteSpace(folder))
                folder = sourceSubtype;


            if (folder.IndexOf(':') >= 0 ||
                folder.EndsWith(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "Source definition XML uses a rooted/archive FolderName " +
                    "and is not supported as a capture source: " +
                    folder);
            }


            string relativePath =
                "Data/PlanetDataFiles/" +
                folder.Trim('/', '\\') +
                "/" +
                fileName;


            if (sourceContext == null ||
                sourceContext.IsBaseGame)
            {
                byte[] gameData;

                if (TryReadGameContentFile(
                    relativePath,
                    out gameData))
                {
                    return gameData;
                }
            }
            else
            {
                byte[] contextModData;

                if (TryReadModFile(
                    relativePath,
                    sourceContext.ModItem,
                    out contextModData))
                {
                    return contextModData;
                }
            }


            // A live definition can retain an incomplete or vanilla context
            // even though its planet maps came from a loaded mod. Probe every
            // loaded mod for the requested relative file before giving up.
            if (MyAPIGateway.Session != null &&
                MyAPIGateway.Session.Mods != null)
            {
                foreach (MyObjectBuilder_Checkpoint.ModItem mod in
                    MyAPIGateway.Session.Mods)
                {
                    byte[] modData;

                    if (!TryReadModFile(
                        relativePath,
                        mod,
                        out modData))
                    {
                        continue;
                    }


                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Resolved source planet map " +
                        "from loaded mod " +
                        mod.PublishedFileId +
                        ": " +
                        relativePath);


                    return modData;
                }
            }


            if (sourceContext != null &&
                !sourceContext.IsBaseGame)
            {
                byte[] gameData;

                if (TryReadGameContentFile(
                    relativePath,
                    out gameData))
                {
                    return gameData;
                }
            }


            throw new Exception(
                "Source planet map file was not found in game content or " +
                "any loaded mod: " +
                relativePath +
                " (source subtype '" +
                sourceSubtype +
                "').");
        }


        private static bool TryReadGameContentFile(
            string relativePath,
            out byte[] data)
        {
            data =
                null;


            try
            {
                using (BinaryReader reader =
                    MyAPIGateway.Utilities.ReadBinaryFileInGameContent(
                        relativePath))
                {
                    data =
                        BinaryData.ReadAll(
                            reader);

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }


        private static bool TryReadModFile(
            string relativePath,
            MyObjectBuilder_Checkpoint.ModItem mod,
            out byte[] data)
        {
            data =
                null;


            try
            {
                using (BinaryReader reader =
                    MyAPIGateway.Utilities.ReadBinaryFileInModLocation(
                        relativePath,
                        mod))
                {
                    data =
                        BinaryData.ReadAll(
                            reader);

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }


        internal Dictionary<string, byte[]> ReadRuntimeArchive(
            string sourceArchiveFile)
        {
            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(
                sourceArchiveFile,
                typeof(VoxelCubemapApiServer)))
            {
                throw new Exception(
                    "Runtime planet archive is missing: " +
                    sourceArchiveFile);
            }


            using (BinaryReader reader =
                MyAPIGateway.Utilities.ReadBinaryFileInWorldStorage(
                    sourceArchiveFile,
                    typeof(VoxelCubemapApiServer)))
            {
                return ReadArchive(
                    reader.BaseStream);
            }
        }


        private static Dictionary<string, byte[]> ReadArchive(
            Stream stream)
        {
            List<MinimalZip.Entry> entries =
                MinimalZip.Read(
                    stream);

            var output =
                new Dictionary<string, byte[]>(
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 0;
                i < entries.Count;
                i++)
            {
                MinimalZip.Entry entry =
                    entries[i];

                if (entry != null)
                {
                    output[entry.Name] =
                        entry.Data;
                }
            }

            return output;
        }


        internal void ReplaceRuntimeArchive(
            string archiveFileName,
            Dictionary<string, byte[]> files)
        {
            if (string.IsNullOrWhiteSpace(archiveFileName))
                throw new ArgumentException(
                    "Runtime archive file name cannot be empty.",
                    "archiveFileName");

            if (files == null ||
                files.Count == 0)
            {
                throw new ArgumentException(
                    "Runtime archive replacement contains no files.",
                    "files");
            }

            List<MinimalZip.Entry> entries =
                files
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x =>
                        new MinimalZip.Entry(
                            x.Key,
                            x.Value,
                            MinimalZip.CompressionMode.Deflate))
                    .ToList();

            _runtimePackages.SaveRuntimeArchive(
                archiveFileName,
                MinimalZip.WriteBytes(entries));
        }


        internal byte[] BuildAuthoritativeImageArchive(
            IDictionary<string, byte[]> files)
        {
            if (files == null)
                throw new ArgumentNullException("files");

            if (files.Count !=
                PlanetMapFileNames.All.Length)
            {
                throw new ArgumentException(
                    "An authoritative image transaction must contain all " +
                    PlanetMapFileNames.All.Length +
                    " planet PNGs.",
                    "files");
            }

            var entries =
                new List<MinimalZip.Entry>(
                    PlanetMapFileNames.All.Length);

            for (int index = 0;
                index < PlanetMapFileNames.All.Length;
                index++)
            {
                string fileName =
                    PlanetMapFileNames.All[index];

                byte[] data;

                if (!files.TryGetValue(
                        fileName,
                        out data) ||
                    data == null ||
                    data.Length == 0)
                {
                    throw new ArgumentException(
                        "Authoritative image transaction is missing '" +
                        fileName +
                        "'.",
                        "files");
                }

                // Decode before accepting the transaction so malformed PNGs
                // cannot reach the live generator registration path.
                PlanetMapOperations.DecodePlanetPng(
                    fileName,
                    data);

                entries.Add(
                    new MinimalZip.Entry(
                        fileName,
                        data,
                        MinimalZip.CompressionMode.Deflate));
            }

            return MinimalZip.WriteBytes(
                entries);
        }


        private byte[] ReadSnapshotPlanetDataFile(
            PlanetModificationSnapshot snapshot,
            Dictionary<string, byte[]> runtimeSourceFiles,
            string fileName)
        {
            if (runtimeSourceFiles == null)
            {
                return ReadSourceFile(
                    snapshot.SourceContext,
                    snapshot.SourceSubtype,
                    snapshot.SourceFolderName,
                    fileName);
            }

            byte[] data;

            if (!runtimeSourceFiles.TryGetValue(
                fileName,
                out data))
            {
                throw new Exception(
                    "Planet PNG '" +
                    fileName +
                    "' is missing from runtime archive " +
                    snapshot.SourceArchiveFile +
                    ".");
            }

            return data;
        }


    }
}
