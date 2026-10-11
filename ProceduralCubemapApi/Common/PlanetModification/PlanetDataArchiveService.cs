using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adk.Compression.Zip;
using Adk.Image.Png;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ProceduralCubemapApi.Common.Noise.fBm;
using ProceduralCubemapApi.Common.PlanetModification.Features;
using ProceduralCubemapApi.Common.PlanetModification.Maps;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using ProceduralCubemapApi.Common.PlanetModification.Runtime;
using ProceduralCubemapApi.Common.PlanetModification.Templates;
using VRage.Game;
using VRage.Utils;

namespace ProceduralCubemapApi.Common.PlanetModification
{
    internal sealed class PlanetDataArchiveService
    {
        private const ushort FLAT_HEIGHT_SAMPLE = 32768;

        private readonly RuntimePackageStore _runtimePackages;
        private readonly Dictionary<int, byte[]> _emptyPlanetArchives =
            new Dictionary<int, byte[]>();


        internal PlanetDataArchiveService(
            RuntimePackageStore runtimePackages)
        {
            if (runtimePackages == null)
                throw new ArgumentNullException(nameof(runtimePackages));

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


        internal byte[] GetEmptyPlanetArchive(
            int textureSize)
        {
            byte[] archive;

            if (_emptyPlanetArchives.TryGetValue(
                    textureSize,
                    out archive))
            {
                return archive;
            }

            int pixelCount =
                checked(textureSize * textureSize);

            byte[][] heightPlanes = CreateEmptyPlanes(pixelCount);
            byte heightHigh = (byte)(FLAT_HEIGHT_SAMPLE >> 8);
            byte heightLow = (byte)(FLAT_HEIGHT_SAMPLE & 0xFF);

            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                heightPlanes[0][pixel] = heightHigh;
                heightPlanes[1][pixel] = heightLow;
            }

            byte[] heightPng =
                new PlanarPngBitmap(
                    textureSize,
                    textureSize,
                    16,
                    0,
                    heightPlanes)
                    .Encode();

            byte[][] materialPlanes = CreateEmptyPlanes(pixelCount);

            byte[] materialPng =
                new PlanarPngBitmap(
                    textureSize,
                    textureSize,
                    8,
                    2,
                    materialPlanes)
                    .Encode();

            var entries =
                new List<MinimalZip.Entry>(
                    PlanetMapFileNames.All.Length);

            foreach (string fileName in PlanetMapFileNames.All)
            {
                entries.Add(
                    new MinimalZip.Entry(
                        fileName,
                        fileName.EndsWith(
                            "_mat.png",
                            StringComparison.OrdinalIgnoreCase)
                            ? materialPng
                            : heightPng,
                        MinimalZip.CompressionMode.Deflate));
            }

            archive =
                MinimalZip.WriteBytes(entries);

            _emptyPlanetArchives.Add(
                textureSize,
                archive);

            return archive;
        }


        private static byte[][] CreateEmptyPlanes(
            int pixelCount)
        {
            return new[]
            {
                new byte[pixelCount],
                new byte[pixelCount],
                new byte[pixelCount],
                new byte[pixelCount]
            };
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
                throw new ArgumentNullException(nameof(snapshot));


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
                snapshot.SourceFiles ??
                (string.IsNullOrWhiteSpace(
                        snapshot.SourceArchiveFile)
                    ? null
                    : ReadRuntimeArchive(
                        snapshot.SourceArchiveFile));

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
                throw new ArgumentNullException(nameof(snapshot));

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


        internal Dictionary<string, byte[]> BuildProceduralRevisionFiles(
            List<PlanetModificationSnapshot> revisions,
            Dictionary<string, byte[]> sourceFiles)
        {
            if (revisions == null || revisions.Count == 0)
            {
                throw new ArgumentException(
                    "At least one procedural revision is required.",
                    nameof(revisions));
            }

            PlanetModificationSnapshot root = revisions[0];
            string[] faces =
            {
                "front",
                "back",
                "left",
                "right",
                "up",
                "down"
            };

            if (revisions.All(revision => revision.BrushOperations.Count == 0 &&
                    revision.FeatureOperations.Count == 0 && revision.BiomeReplacementOperations.Count == 0 &&
                    revision.FractalNoiseOperations.Count == 0))
            {
                var unchanged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in PlanetMapFileNames.All)
                    unchanged.Add(name, ReadSnapshotPlanetDataFile(root, sourceFiles, name));
                var allocations = new List<byte>();
                foreach (PlanetModificationSnapshot revision in revisions)
                    allocations.AddRange(revision.AllocatedComplexMaterialValues);
                ValidateRoadMaterialAllocations(unchanged, allocations);
                return unchanged;
            }

            // Keep the six height faces resident so terrain-aware features can plan in
            // planet space. Material maps remain encoded between revisions and are
            // decoded one face at a time, avoiding six additional full material maps.
            var heightImages = new Dictionary<string, PlanarPngBitmap>(
                6,
                StringComparer.OrdinalIgnoreCase);
            var materialBytes = new Dictionary<string, byte[]>(
                6,
                StringComparer.OrdinalIgnoreCase);

            for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
            {
                string heightFileName = faces[faceIndex] + ".png";
                string materialFileName = faces[faceIndex] + "_mat.png";

                heightImages.Add(
                    heightFileName,
                    PlanetMapOperations.DecodePlanetPng(
                        heightFileName,
                        ReadSnapshotPlanetDataFile(root, sourceFiles, heightFileName)));

                materialBytes.Add(
                    materialFileName,
                    ReadSnapshotPlanetDataFile(root, sourceFiles, materialFileName));
            }

            var pendingAllocations = new List<byte>();
            for (int revisionIndex = 0; revisionIndex < revisions.Count; revisionIndex++)
            {
                PlanetModificationSnapshot revision = revisions[revisionIndex];
                pendingAllocations.AddRange(revision.AllocatedComplexMaterialValues);
                bool haveBrushes = revision.BrushOperations.Count > 0;
                bool haveMaterialEdits = revision.BiomeReplacementOperations.Count > 0 ||
                                        revision.FractalNoiseOperations.Count > 0;
                bool haveFeatures = revision.FeatureOperations.Count > 0;
                // Roads run in the final pass. A road-only revision must not
                // decode, scan and recompress all six material maps on a cold
                // replay. Allocation checks can share the next material pass.
                if (!haveBrushes && !haveMaterialEdits && !haveFeatures)
                    continue;

                // Brushes must happen before river planning because a height brush may
                // change both source eligibility and the shoreline. Material-only work
                // commutes with the feature pass, so it is completed here while each
                // material face is already decoded.
                for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
                {
                    if (!haveBrushes && !haveMaterialEdits && pendingAllocations.Count == 0)
                        break;
                    string heightFileName = faces[faceIndex] + ".png";
                    string materialFileName = faces[faceIndex] + "_mat.png";
                    PlanarPngBitmap heightImage = heightImages[heightFileName];
                    PlanarPngBitmap materialImage = PlanetMapOperations.DecodePlanetPng(
                        materialFileName,
                        materialBytes[materialFileName]);

                    if (pendingAllocations.Count > 0)
                    {
                        PlanetMapOperations.ValidateAllocatedComplexMaterialValues(
                            materialImage,
                            materialFileName,
                            pendingAllocations);
                    }

                    PlanetMapOperations.ApplyBrushToPlanetImages(
                        heightImage,
                        materialImage,
                        heightFileName,
                        revision.PlanetSeed,
                        revision.BrushOperations);

                    for (int operationIndex = 0;
                        operationIndex < revision.BiomeReplacementOperations.Count;
                        operationIndex++)
                    {
                        PlanetMapOperations.ApplyBiomeReplacementToPlanetImage(
                            materialImage,
                            revision.BiomeReplacementOperations[operationIndex]);
                    }

                    PlanetMapOperations.ApplyFractalNoiseToPlanetImage(
                        materialImage,
                        materialFileName,
                        revision.PlanetSeed,
                        revision.FractalNoiseOperations);

                    if (haveBrushes || haveMaterialEdits)
                        materialBytes[materialFileName] = materialImage.Encode();
                }
                pendingAllocations.Clear();

                List<GeneratedPlanetFeature> terrainFeatures =
                    PlanetFeatureProcessor.ResolveTerrainBlindFeatures(
                        revision.PlanetSeed,
                        revision.FeatureOperations);

                if (terrainFeatures.Count > 0)
                {
                    for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
                    {
                        string heightFileName = faces[faceIndex] + ".png";
                        PlanetFeatureProcessor.ApplyResolvedToPlanetImages(
                            heightImages[heightFileName],
                            null,
                            heightFileName,
                            terrainFeatures);
                    }
                }

                // Rivers are planned only after the other height features have changed
                // the six-face terrain. Their carve pass is then absolute and therefore
                // always wins over positive terrain deltas along the channel.
                List<GeneratedPlanetFeature> riverFeatures =
                    PlanetFeatureProcessor.ResolveRiverFeatures(
                        heightImages,
                        revision.PlanetSeed,
                        revision.FeatureOperations);

                if (riverFeatures.Count > 0)
                {
                    for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
                    {
                        string heightFileName = faces[faceIndex] + ".png";
                        PlanetFeatureProcessor.ApplyResolvedToPlanetImages(
                            heightImages[heightFileName],
                            null,
                            heightFileName,
                            riverFeatures);
                    }
                }
            }

            if (pendingAllocations.Count > 0)
                foreach (string face in faces)
                {
                    string name = face + "_mat.png";
                    PlanetMapOperations.ValidateAllocatedComplexMaterialValues(
                        PlanetMapOperations.DecodePlanetPng(name, materialBytes[name]),
                        name, pendingAllocations);
                }

            var output = new Dictionary<string, byte[]>(
                PlanetMapFileNames.All.Length,
                StringComparer.OrdinalIgnoreCase);

            for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
            {
                string heightFileName = faces[faceIndex] + ".png";
                string materialFileName = faces[faceIndex] + "_mat.png";
                output.Add(heightFileName, heightImages[heightFileName].Encode());
                output.Add(materialFileName, materialBytes[materialFileName]);
            }

            return output;
        }

        internal void ValidateRoadMaterialAllocations(Dictionary<string, byte[]> files, List<byte> allocations)
        {
            if (allocations.Count == 0) return;
            foreach (string name in PlanetMapFileNames.All)
                if (name.EndsWith("_mat.png", StringComparison.OrdinalIgnoreCase))
                    PlanetMapOperations.ValidateAllocatedComplexMaterialValues(
                        PlanetMapOperations.DecodePlanetPng(name, files[name]), name, allocations);
        }

        internal void ApplyFinalProceduralLines(Dictionary<string, byte[]> files,
            List<LineOperation> lines, MyPlanet targetPlanet)
        {
            if (lines == null || lines.Count == 0) return;
            if (files == null) throw new ArgumentNullException(nameof(files));

            string[] faces = { "front", "back", "left", "right", "up", "down" };
            var heights = new Dictionary<string, PlanarPngBitmap>(StringComparer.OrdinalIgnoreCase);
            var materials = new Dictionary<string, PlanarPngBitmap>(StringComparer.OrdinalIgnoreCase);
            foreach (string face in faces)
            {
                string name = face + ".png";
                heights.Add(name, PlanetMapOperations.DecodePlanetPng(name, files[name]));
            }

            bool[] changedFaces = SphericalLineRasterizer.ApplyCells(heights, new PlanetModificationSnapshot
            {
                TargetPlanet = targetPlanet,
                LineOperations = lines
            }, name =>
            {
                PlanarPngBitmap image;
                if (!materials.TryGetValue(name, out image))
                {
                    image = PlanetMapOperations.DecodePlanetPng(name, files[name]);
                    materials.Add(name, image);
                }
                return image;
            });

            // Encode independent faces concurrently, then publish in fixed face
            // order. Unaffected encoded faces are reused byte-for-byte.
            var encoded = new byte[12][];
            var failures = new Exception[6];
            Action<int> encode = face =>
            {
                if (!changedFaces[face]) return;
                try
                {
                    encoded[face * 2] = heights[faces[face] + ".png"].Encode();
                    encoded[face * 2 + 1] = materials[faces[face] + "_mat.png"].Encode();
                }
                catch (Exception error) { failures[face] = error; }
            };
            if (MyAPIGateway.Parallel == null)
                for (int face = 0; face < 6; face++) encode(face);
            else
                // PNG compression allocates large temporary buffers. Two face
                // jobs bound peak memory even on 4K/8K cubemaps.
                MyAPIGateway.Parallel.For(0, 2, worker =>
                {
                    for (int face = worker; face < 6; face += 2) encode(face);
                });
            for (int face = 0; face < 6; face++)
            {
                if (failures[face] != null) throw failures[face];
                if (!changedFaces[face]) continue;
                files[faces[face] + ".png"] = encoded[face * 2];
                files[faces[face] + "_mat.png"] = encoded[face * 2 + 1];
            }
        }


        private void ApplyQueuedBrushes(
            PlanetModificationSnapshot snapshot,
            Dictionary<string, byte[]> runtimeSourceFiles)
        {
            bool haveBrushes = snapshot.BrushOperations != null && snapshot.BrushOperations.Count > 0;
            bool haveFeatures = snapshot.FeatureOperations != null && snapshot.FeatureOperations.Count > 0;
            bool haveLines = snapshot.LineOperations != null && snapshot.LineOperations.Count > 0;
            if (!haveBrushes && !haveFeatures && !haveLines)
                return;

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
                snapshot.Images = new Dictionary<string, PlanarPngBitmap>(
                    StringComparer.OrdinalIgnoreCase);
            }

            // Load every height face first. River planning needs a coherent six-face
            // surface after the queued brushes have been applied.
            var heightImages = new Dictionary<string, PlanarPngBitmap>(
                6,
                StringComparer.OrdinalIgnoreCase);

            for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
            {
                string heightFileName = faces[faceIndex] + ".png";
                heightImages.Add(
                    heightFileName,
                    GetOrLoadSnapshotImage(
                        snapshot,
                        runtimeSourceFiles,
                        heightFileName));
            }

            for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
            {
                string heightFileName = faces[faceIndex] + ".png";
                string materialFileName = faces[faceIndex] + "_mat.png";
                PlanarPngBitmap heightImage = heightImages[heightFileName];
                PlanarPngBitmap materialImage = GetOrLoadSnapshotImage(
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

            List<GeneratedPlanetFeature> terrainFeatures =
                PlanetFeatureProcessor.ResolveTerrainBlindFeatures(
                    snapshot.PlanetSeed,
                    snapshot.FeatureOperations);

            if (terrainFeatures.Count > 0)
            {
                for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
                {
                    string heightFileName = faces[faceIndex] + ".png";
                    PlanetFeatureProcessor.ApplyResolvedToPlanetImages(
                        heightImages[heightFileName],
                        null,
                        heightFileName,
                        terrainFeatures);
                }
            }

            List<GeneratedPlanetFeature> riverFeatures =
                PlanetFeatureProcessor.ResolveRiverFeatures(
                    heightImages,
                    snapshot.PlanetSeed,
                    snapshot.FeatureOperations);

            if (riverFeatures.Count == 0)
            {
                if (haveLines)
                    SphericalLineRasterizer.Apply(heightImages, snapshot,
                        delegate(string name) { return GetOrLoadSnapshotImage(snapshot, runtimeSourceFiles, name); });
                return;
            }

            for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
            {
                string heightFileName = faces[faceIndex] + ".png";
                PlanetFeatureProcessor.ApplyResolvedToPlanetImages(
                    heightImages[heightFileName],
                    null,
                    heightFileName,
                    riverFeatures);
            }

            // The road is an absolute terrain/material override, not a feature
            // blended into terrain that later feature passes may overwrite.
            if (haveLines)
                SphericalLineRasterizer.Apply(heightImages, snapshot,
                    delegate(string name) { return GetOrLoadSnapshotImage(snapshot, runtimeSourceFiles, name); });
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
                typeof(CubemapApiServer)))
            {
                throw new Exception(
                    "Runtime planet archive is missing: " +
                    sourceArchiveFile);
            }


            using (BinaryReader reader =
                MyAPIGateway.Utilities.ReadBinaryFileInWorldStorage(
                    sourceArchiveFile,
                    typeof(CubemapApiServer)))
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

                if (entry != null &&
                    !string.Equals(
                        RuntimeArchiveLayout.GetLogicalEntryName(
                            entry.Name),
                        RuntimeProceduralCache.ARCHIVE_MANIFEST_FILE,
                        StringComparison.OrdinalIgnoreCase))
                {
                    output[
                        RuntimeArchiveLayout.GetLogicalEntryName(
                            entry.Name)] =
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
                    nameof(archiveFileName));

            if (files == null ||
                files.Count == 0)
            {
                throw new ArgumentException(
                    "Runtime archive replacement contains no files.",
                    nameof(files));
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
                throw new ArgumentNullException(nameof(files));

            if (files.Count !=
                PlanetMapFileNames.All.Length)
            {
                throw new ArgumentException(
                    "An authoritative image transaction must contain all " +
                    PlanetMapFileNames.All.Length +
                    " planet PNGs.",
                    nameof(files));
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
                        nameof(files));
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


        internal byte[] PackProceduralArchive(
            IDictionary<string, byte[]> files,
            RuntimeProceduralCacheManifest cacheManifest)
        {
            if (files == null ||
                files.Count != PlanetMapFileNames.All.Length)
            {
                throw new ArgumentException(
                    "A procedural archive must contain all planet PNGs.",
                    nameof(files));
            }

            if (cacheManifest == null)
                throw new ArgumentNullException(nameof(cacheManifest));

            string manifestXml =
                MyAPIGateway.Utilities
                    .SerializeToXML<RuntimeProceduralCacheManifest>(
                        cacheManifest);

            var entries =
                new List<MinimalZip.Entry>(
                    PlanetMapFileNames.All.Length + 1)
                {
                    new MinimalZip.Entry(
                        RuntimeProceduralCache.ARCHIVE_MANIFEST_FILE,
                        Encoding.UTF8.GetBytes(
                            manifestXml),
                        MinimalZip.CompressionMode.Stored)
                };

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
                        "Procedural archive is missing '" +
                        fileName +
                        "'.",
                        nameof(files));
                }

                entries.Add(
                    new MinimalZip.Entry(
                        fileName,
                        data,
                        MinimalZip.CompressionMode.Deflate));
            }

            return MinimalZip.WriteBytes(
                entries,
                RuntimeProceduralCache.ZipComment);
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
