using System;
using System.Collections.Generic;
using System.Linq;
using Adk.Image.Png;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Generated;
using VoxelCubemapApi.Server.PlanetModification.Maps;
using VoxelCubemapApi.Server.PlanetModification.Runtime;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace VoxelCubemapApi.Server.PlanetModification.Templates
{
    /// <summary>
    /// Server-owned mutable template exposed to another mod only through a
    /// dictionary of BCL delegates.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "VoxelCubemapApi.Api",
        ClientName = "ModificationTemplate")]
    internal sealed partial class PlanetModificationTemplate
    {
        private readonly PlanetModificationCoordinator _coordinator;
        private readonly PlanetDataArchiveService _planetDataArchives;
        private readonly Dictionary<string, PlanarPngBitmap> _images =
            new Dictionary<string, PlanarPngBitmap>(
                StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string,
            List<Action<int, int, byte[], byte[], byte[], byte[]>>>
                _imageTransforms =
                    new Dictionary<string,
                        List<Action<int, int, byte[], byte[], byte[], byte[]>>>(
                            StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, byte[]> _sourceArchiveFiles;
        private bool[] _usedMaterialMapValues;

        private readonly List<FractalNoiseOperation>
            _fractalNoiseOperations =
                new List<FractalNoiseOperation>();

        private readonly List<BiomeReplacementOperation>
            _biomeReplacementOperations =
                new List<BiomeReplacementOperation>();

        private readonly List<byte> _allocatedComplexMaterialValues =
            new List<byte>();

        private string _environmentCarrierSubtype;

        private bool _closed;
        private bool _pushStarted;


        public readonly MyPlanet TargetPlanet;
        public readonly MyModContext SourceContext;
        public readonly string SourceSubtype;
        public readonly string SourceFolderName;
        public readonly string SourceArchiveFile;
        public readonly string CurrentProviderSubtype;
        public readonly long PlanetSeed;
        public readonly string TemplateId;
        public readonly MyObjectBuilder_PlanetGeneratorDefinition Builder;


        public PlanetModificationTemplate(
            PlanetModificationCoordinator coordinator,
            PlanetDataArchiveService planetDataArchives,
            MyPlanet targetPlanet,
            MyModContext sourceContext,
            string sourceSubtype,
            string sourceFolderName,
            string sourceArchiveFile,
            string currentProviderSubtype,
            long planetSeed,
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            string environmentCarrierSubtype)
        {
            if (coordinator == null)
                throw new ArgumentNullException("coordinator");

            if (planetDataArchives == null)
                throw new ArgumentNullException("planetDataArchives");

            _coordinator =
                coordinator;

            _planetDataArchives =
                planetDataArchives;

            TargetPlanet =
                targetPlanet;

            SourceContext =
                sourceContext;

            SourceSubtype =
                sourceSubtype;

            SourceFolderName =
                sourceFolderName;

            SourceArchiveFile =
                sourceArchiveFile;

            CurrentProviderSubtype =
                currentProviderSubtype;

            PlanetSeed =
                planetSeed;

            Builder =
                builder;

            _environmentCarrierSubtype =
                environmentCarrierSubtype;

            if (!string.IsNullOrWhiteSpace(
                _environmentCarrierSubtype))
            {
                EnsureBiomePlanetMapEnabled();
            }

            TemplateId =
                StableIdentifier.Create(
                    targetPlanet.EntityId +
                    "|" +
                    planetSeed +
                    "|" +
                    DateTime.UtcNow.Ticks);
        }


        public PlanetModificationSnapshot CreateSnapshot()
        {
            EnsureOpen();

            if (_pushStarted)
            {
                throw new Exception(
                    "This modification template has already been pushed.");
            }

            _pushStarted =
                true;


            var images =
                new Dictionary<string, PlanarPngBitmap>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, PlanarPngBitmap> pair in _images)
            {
                PlanarPngBitmap source =
                    pair.Value;
                images.Add(
                    pair.Key,
                    source.Clone());
            }


            var transforms =
                new Dictionary<string,
                    List<Action<int, int, byte[], byte[], byte[], byte[]>>>(
                        StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string,
                List<Action<int, int, byte[], byte[], byte[], byte[]>>> pair in
                _imageTransforms)
            {
                transforms.Add(
                    pair.Key,
                    new List<Action<int, int, byte[], byte[], byte[], byte[]>>(
                        pair.Value));
            }


            var fractalOperations =
                new List<FractalNoiseOperation>(
                    _fractalNoiseOperations.Count);

            for (int i = 0;
                i < _fractalNoiseOperations.Count;
                i++)
            {
                FractalNoiseOperation operation =
                    _fractalNoiseOperations[i];

                fractalOperations.Add(
                    new FractalNoiseOperation
                    {
                        PlaneIndex = operation.PlaneIndex,
                        TargetValue = operation.TargetValue,
                        CoveragePercent = operation.CoveragePercent
                    });
            }

            var biomeReplacements =
                new List<BiomeReplacementOperation>(
                    _biomeReplacementOperations.Count);

            for (int i = 0;
                i < _biomeReplacementOperations.Count;
                i++)
            {
                BiomeReplacementOperation operation =
                    _biomeReplacementOperations[i];

                biomeReplacements.Add(
                    new BiomeReplacementOperation
                    {
                        SourceBiome = operation.SourceBiome,
                        TargetBiome = operation.TargetBiome
                    });
            }

            var allocatedComplexValues =
                new List<byte>(
                    _allocatedComplexMaterialValues);


            return new PlanetModificationSnapshot
            {
                TargetPlanet = TargetPlanet,
                SourceContext = SourceContext,
                SourceSubtype = SourceSubtype,
                SourceFolderName = SourceFolderName,
                SourceArchiveFile = SourceArchiveFile,
                CurrentProviderSubtype = CurrentProviderSubtype,
                PlanetSeed = PlanetSeed,
                TemplateId = TemplateId,
                Builder = Builder,
                Images = images,
                ImageTransforms = transforms,
                FractalNoiseOperations = fractalOperations,
                BiomeReplacementOperations = biomeReplacements,
                AllocatedComplexMaterialValues = allocatedComplexValues,
                EnvironmentCarrierSubtype = _environmentCarrierSubtype
            };
        }


        /// <summary>
        /// Returns the entity ID of the planet owned by this template.
        /// </summary>
        [ApiMethod]
        private long GetPlanetEntityId()
        {
            EnsureOpen();

            return TargetPlanet.EntityId;
        }


        /// <summary>
        /// Returns the seed of the planet owned by this template.
        /// </summary>
        [ApiMethod(typeof(long))]
        private long GetPlanetSeed()
        {
            EnsureOpen();

            return PlanetSeed;
        }


        /// <summary>
        /// Loads one planet PNG as exactly four mutable byte planes. RGB images
        /// use RGBA order; 16-bit grayscale images use high/low sample bytes in
        /// planes zero and one.
        /// </summary>
        [ApiMethod]
        private byte[][] LoadPlanetPng(
            string faceFileName)
        {
            EnsureEditable();

            PlanarPngBitmap image =
                GetOrLoadImage(
                    faceFileName);

            // Return a new outer array so consumers cannot replace the
            // template's planes. The four byte planes themselves are
            // intentionally mutable and are captured when Push is called.
            return new[]
            {
                image.Planes[0],
                image.Planes[1],
                image.Planes[2],
                image.Planes[3]
            };
        }


        /// <summary>
        /// Returns the width and height of one planet PNG.
        /// </summary>
        [ApiMethod]
        private int[] GetPlanetPngSize(
            string faceFileName)
        {
            PlanarPngBitmap image =
                GetOrLoadImage(
                    faceFileName);

            return new[]
            {
                image.Width,
                image.Height
            };
        }


        /// <summary>
        /// Returns width, height, PNG bit depth, and PNG color type. For 16-bit
        /// grayscale maps, planes zero and one are the high/low sample bytes.
        /// </summary>
        [ApiMethod]
        private int[] GetPlanetPngInfo(
            string faceFileName)
        {
            PlanarPngBitmap image =
                GetOrLoadImage(
                    faceFileName);

            return new[]
            {
                image.Width,
                image.Height,
                image.BitDepth,
                image.ColorType
            };
        }


        /// <summary>
        /// Returns the sorted distinct biome IDs present in the green channel
        /// of the six material-map PNGs.
        /// </summary>
        [ApiMethod]
        private byte[] GetUsedBiomes()
        {
            EnsureEditable();

            string[] materialFaceFiles =
            {
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };

            var used =
                new bool[256];

            for (int faceIndex = 0;
                faceIndex < materialFaceFiles.Length;
                faceIndex++)
            {
                byte[] biomes =
                    GetOrLoadImage(
                        materialFaceFiles[faceIndex]).Planes[1];

                for (int pixelIndex = 0;
                    pixelIndex < biomes.Length;
                    pixelIndex++)
                {
                    used[biomes[pixelIndex]] = true;
                }
            }

            var result =
                new List<byte>();

            for (int value = 0;
                value < used.Length;
                value++)
            {
                if (used[value])
                    result.Add((byte)value);
            }

            return result.ToArray();
        }


        /// <summary>
        /// Queues a transformation over the four mutable planes of a planet PNG.
        /// The transformation executes on the background Push worker.
        /// </summary>
        [ApiMethod]
        private void ApplyPlanetImage(
            string faceFileName,
            Action<int, int, byte[], byte[], byte[], byte[]> transform)
        {
            EnsureEditable();

            if (transform == null)
                throw new ArgumentNullException("transform");


            faceFileName =
                PlanetMapFileNames.Validate(
                    faceFileName);

            List<Action<int, int, byte[], byte[], byte[], byte[]>> transforms;

            if (!_imageTransforms.TryGetValue(
                faceFileName,
                out transforms))
            {
                transforms =
                    new List<Action<int, int, byte[], byte[], byte[], byte[]>>();

                _imageTransforms.Add(
                    faceFileName,
                    transforms);
            }

            transforms.Add(
                transform);
        }


        /// <summary>
        /// Adds a simple voxel material definition at the requested map value.
        /// </summary>
        [ApiMethod]
        private bool AddMaterial(
            string materialSubtype,
            byte mapValue,
            float maxDepth)
        {
            EnsureEditable();

            if (string.IsNullOrWhiteSpace(
                materialSubtype))
            {
                throw new ArgumentException(
                    "Material subtype cannot be empty.",
                    "materialSubtype");
            }

            if (maxDepth < 0f ||
                float.IsNaN(maxDepth) ||
                float.IsInfinity(maxDepth))
            {
                throw new ArgumentException(
                    "Material max depth must be a finite non-negative value.",
                    "maxDepth");
            }

            if (PlanetMaterialMap.UsesValue(
                Builder,
                mapValue) ||
                (_usedMaterialMapValues != null &&
                    _usedMaterialMapValues[mapValue]))
            {
                return false;
            }


            MyPlanetMaterialDefinition[] existing =
                Builder.CustomMaterialTable;

            int count =
                existing == null
                    ? 0
                    : existing.Length;

            var output =
                new MyPlanetMaterialDefinition[count + 1];

            if (count > 0)
            {
                Array.Copy(
                    existing,
                    output,
                    count);
            }

            output[count] =
                new MyPlanetMaterialDefinition
                {
                    Material = materialSubtype,
                    Value = mapValue,
                    MaxDepth = maxDepth
                };

            Builder.CustomMaterialTable =
                output;

            if (_usedMaterialMapValues != null)
            {
                _usedMaterialMapValues[mapValue] =
                    true;
            }

            return true;
        }


        /// <summary>
        /// Clones a complex material group and returns a map value unused by the
        /// source maps and template definitions.
        /// </summary>
        [ApiMethod]
        private byte AddComplexMaterial(
            MyPlanetMaterialGroup materialGroup)
        {
            EnsureEditable();

            if (materialGroup == null)
                throw new ArgumentNullException("materialGroup");

            if (materialGroup.MaterialRules == null ||
                materialGroup.MaterialRules.Length == 0)
            {
                throw new ArgumentException(
                    "Complex material group contains no material rules.",
                    "materialGroup");
            }


            EnsureUsedMaterialMapValues();

            byte mapValue =
                PlanetMaterialMap.AllocateValue(
                    _usedMaterialMapValues,
                    ref _nextMaterialMapCandidate);

            var clone =
                (MyPlanetMaterialGroup)
                    materialGroup.Clone();

            clone.Value =
                mapValue;

            if (string.IsNullOrWhiteSpace(
                clone.Name))
            {
                clone.Name =
                    "ApiComplexMaterial_" +
                    mapValue;
            }


            MyPlanetMaterialGroup[] existing =
                Builder.ComplexMaterials;

            int count =
                existing == null
                    ? 0
                    : existing.Length;

            var output =
                new MyPlanetMaterialGroup[count + 1];

            if (count > 0)
            {
                Array.Copy(
                    existing,
                    output,
                    count);
            }

            output[count] =
                clone;

            Builder.ComplexMaterials =
                output;

            _allocatedComplexMaterialValues.Add(
                mapValue);

            return mapValue;
        }


        /// <summary>
        /// Appends client-authored vegetation/environment mappings and returns
        /// the number added.
        /// </summary>
        [ApiMethod]
        private int AddEnvironmentItems(
            PlanetEnvironmentItemMapping[] mappings)
        {
            EnsureEditable();

            if (!string.IsNullOrWhiteSpace(
                _environmentCarrierSubtype))
            {
                throw new Exception(
                    "This template already selected an explicit " +
                    "WorldEnvironmentDefinition carrier.");
            }

            if (mappings == null ||
                mappings.Length == 0)
            {
                throw new ArgumentException(
                    "At least one environment-item mapping is required.",
                    "mappings");
            }

            if (Builder.Environment.HasValue)
            {
                throw new Exception(
                    "This planet uses a WorldEnvironmentDefinition. " +
                    "Appending legacy EnvironmentItems is not supported.");
            }


            PlanetEnvironmentItemMapping[] existing =
                Builder.EnvironmentItems;

            int existingCount =
                existing == null
                    ? 0
                    : existing.Length;

            var output =
                new PlanetEnvironmentItemMapping[
                    existingCount + mappings.Length];

            if (existingCount > 0)
            {
                Array.Copy(
                    existing,
                    output,
                    existingCount);
            }


            for (int mappingIndex = 0;
                mappingIndex < mappings.Length;
                mappingIndex++)
            {
                PlanetEnvironmentItemMapping mapping =
                    mappings[mappingIndex];

                if (mapping.Materials == null ||
                    mapping.Materials.Length == 0)
                {
                    throw new ArgumentException(
                        "Environment mapping " +
                        mappingIndex +
                        " contains no target materials.",
                        "mappings");
                }

                if (mapping.Items == null ||
                    mapping.Items.Length == 0)
                {
                    throw new ArgumentException(
                        "Environment mapping " +
                        mappingIndex +
                        " contains no environment items.",
                        "mappings");
                }

                output[existingCount + mappingIndex] =
                    CloneEnvironmentItemMapping(
                        mapping,
                        mappingIndex);
            }


            Builder.EnvironmentItems =
                output;

            return mappings.Length;
        }


        /// <summary>
        /// Selects a caller-owned WorldEnvironmentDefinition through a normally
        /// loaded PlanetGeneratorDefinition carrier.
        /// </summary>
        [ApiMethod]
        private void SetEnvironmentDefinition(
            string carrierPlanetGeneratorSubtype)
        {
            EnsureEditable();

            MyPlanetGeneratorDefinition carrier =
                PlanetEnvironmentService.ResolveCarrierGenerator(
                    carrierPlanetGeneratorSubtype);


            EnsureBiomePlanetMapEnabled();

            // Persist the actual caller environment id into the generated
            // planet definition as well. Runtime registrations do not run
            // MyPlanetGeneratorDefinition.Postprocessor, so the carrier
            // subtype is persisted separately for live/reload rebinding.
            Builder.Environment =
                new SerializableDefinitionId(
                    carrier.EnvironmentId.Value.TypeId,
                    carrier.EnvironmentId.Value.SubtypeName);

            // Explicit procedural definitions and legacy EnvironmentItems are
            // different engine paths. Selecting an explicit environment
            // replaces inherited/legacy mappings for this terraform revision.
            Builder.EnvironmentItems =
                null;

            _environmentCarrierSubtype =
                carrier.Id.SubtypeName;
        }


        private static PlanetEnvironmentItemMapping
            CloneEnvironmentItemMapping(
                PlanetEnvironmentItemMapping source,
                int mappingIndex)
        {
            string[] materials =
                new string[source.Materials.Length];

            Array.Copy(
                source.Materials,
                materials,
                materials.Length);

            int[] biomes =
                null;

            if (source.Biomes != null)
            {
                biomes =
                    new int[source.Biomes.Length];

                Array.Copy(
                    source.Biomes,
                    biomes,
                    biomes.Length);
            }

            var items =
                new MyPlanetEnvironmentItemDef[source.Items.Length];

            for (int itemIndex = 0;
                itemIndex < source.Items.Length;
                itemIndex++)
            {
                MyPlanetEnvironmentItemDef item =
                    source.Items[itemIndex];

                if (item == null ||
                    string.IsNullOrWhiteSpace(item.TypeId))
                {
                    throw new ArgumentException(
                        "Environment mapping " +
                        mappingIndex +
                        " item " +
                        itemIndex +
                        " has no TypeId.",
                        "mappings");
                }

                if (item.Density < 0f)
                {
                    throw new ArgumentException(
                        "Environment mapping " +
                        mappingIndex +
                        " item " +
                        itemIndex +
                        " has a negative density.",
                        "mappings");
                }

                items[itemIndex] =
                    new MyPlanetEnvironmentItemDef
                    {
                        TypeId = item.TypeId,
                        SubtypeId = item.SubtypeId,
                        GroupId = item.GroupId,
                        ModifierId = item.ModifierId,
                        GroupIndex = item.GroupIndex,
                        ModifierIndex = item.ModifierIndex,
                        Density = item.Density,
                        IsDetail = item.IsDetail,
                        BaseColor = item.BaseColor,
                        ColorSpread = item.ColorSpread,
                        Offset = item.Offset,
                        MaxRoll = item.MaxRoll
                    };
            }


            return new PlanetEnvironmentItemMapping
            {
                Materials = materials,
                Biomes = biomes,
                Items = items,
                Rule = source.Rule == null
                    ? null
                    : (MyPlanetSurfaceRule)source.Rule.Clone()
            };
        }


        /// <summary>
        /// Applies seamless planet-space fractal noise to the material channel.
        /// Selected pixels receive the requested map value during Push.
        /// </summary>
        [ApiMethod]
        private void ApplyFractalNoise(
            byte mapValue,
            int coveragePercent)
        {
            EnsureEditable();

            if (coveragePercent < 0 ||
                coveragePercent > 100)
            {
                throw new ArgumentException(
                    "Coverage must be from 0 to 100.",
                    "coveragePercent");
            }

            if (!PlanetMaterialMap.UsesValue(
                Builder,
                mapValue))
            {
                throw new ArgumentException(
                    "Material-map value " +
                    mapValue +
                    " is not defined in this template.",
                    "mapValue");
            }


            _fractalNoiseOperations.Add(
                new FractalNoiseOperation
                {
                    PlaneIndex = 0,
                    TargetValue = mapValue,
                    CoveragePercent = coveragePercent
                });
        }


        private void EnsureBiomePlanetMapEnabled()
        {
            var planetMaps =
                Builder.PlanetMaps.GetValueOrDefault();

            if (planetMaps.Biome)
                return;

            planetMaps.Biome =
                true;

            Builder.PlanetMaps =
                planetMaps;

            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Enabled PlanetMaps.Biome for " +
                "runtime biome editing (source generator had Biome=false).");
        }


        /// <summary>
        /// Replaces every occurrence of one biome ID with another across all six
        /// material maps during Push.
        /// </summary>
        [ApiMethod]
        private void ReplaceBiome(
            byte sourceBiome,
            byte targetBiome)
        {
            EnsureEditable();

            if (sourceBiome == targetBiome)
                return;

            EnsureBiomePlanetMapEnabled();

            _biomeReplacementOperations.Add(
                new BiomeReplacementOperation
                {
                    SourceBiome = sourceBiome,
                    TargetBiome = targetBiome
                });
        }


        /// <summary>
        /// Applies seamless planet-space fractal noise to the biome channel.
        /// Selected pixels receive the requested biome value during Push.
        /// </summary>
        [ApiMethod]
        private void ApplyBiomeFractalNoise(
            byte biomeValue,
            int coveragePercent)
        {
            EnsureEditable();

            if (coveragePercent < 0 ||
                coveragePercent > 100)
            {
                throw new ArgumentException(
                    "Coverage must be from 0 to 100.",
                    "coveragePercent");
            }

            EnsureBiomePlanetMapEnabled();

            _fractalNoiseOperations.Add(
                new FractalNoiseOperation
                {
                    PlaneIndex = 1,
                    TargetValue = biomeValue,
                    CoveragePercent = coveragePercent
                });
        }


        /// <summary>
        /// Removes a material map value unless a default surface material uses it.
        /// </summary>
        [ApiMethod]
        private bool RemoveMaterial(
            byte mapValue)
        {
            EnsureEditable();

            if ((Builder.DefaultSurfaceMaterial != null &&
                    Builder.DefaultSurfaceMaterial.Value == mapValue) ||
                (Builder.DefaultSubSurfaceMaterial != null &&
                    Builder.DefaultSubSurfaceMaterial.Value == mapValue))
            {
                return false;
            }


            bool removed =
                false;

            if (Builder.CustomMaterialTable != null)
            {
                MyPlanetMaterialDefinition[] filtered =
                    Builder.CustomMaterialTable
                        .Where(x =>
                            x == null ||
                            x.Value != mapValue)
                        .ToArray();

                removed =
                    filtered.Length !=
                    Builder.CustomMaterialTable.Length;

                Builder.CustomMaterialTable =
                    filtered;
            }

            if (Builder.ComplexMaterials != null)
            {
                MyPlanetMaterialGroup[] filtered =
                    Builder.ComplexMaterials
                        .Where(x =>
                            x == null ||
                            x.Value != mapValue)
                        .ToArray();

                removed =
                    removed ||
                    filtered.Length !=
                    Builder.ComplexMaterials.Length;

                Builder.ComplexMaterials =
                    filtered;
            }


            return removed;
        }


        /// <summary>
        /// Commits this template asynchronously and invokes the completion callback.
        /// </summary>
        [ApiMethod]
        private void Push(
            Action<bool, string> callback)
        {
            EnsureEditable();

            _coordinator.BeginPushModification(
                this,
                callback);
        }


        /// <summary>
        /// Closes this template and releases its mutable server-side resources.
        /// </summary>
        [ApiMethod]
        private void Close()
        {
            _closed =
                true;

            _images.Clear();
            _imageTransforms.Clear();
            _fractalNoiseOperations.Clear();
            _allocatedComplexMaterialValues.Clear();

            if (_sourceArchiveFiles != null)
                _sourceArchiveFiles.Clear();
        }


        private PlanarPngBitmap GetOrLoadImage(
            string faceFileName)
        {
            EnsureEditable();

            faceFileName =
                PlanetMapFileNames.Validate(
                    faceFileName);

            PlanarPngBitmap image;

            if (_images.TryGetValue(
                faceFileName,
                out image))
            {
                return image;
            }


            byte[] png;

            if (string.IsNullOrWhiteSpace(
                SourceArchiveFile))
            {
                png =
                        _planetDataArchives.ReadSourceFile(
                            SourceContext,
                        SourceSubtype,
                        SourceFolderName,
                        faceFileName);
            }
            else
            {
                if (_sourceArchiveFiles == null)
                {
                    _sourceArchiveFiles =
                        _planetDataArchives.ReadRuntimeArchive(
                            SourceArchiveFile);
                }

                if (!_sourceArchiveFiles.TryGetValue(
                    faceFileName,
                    out png))
                {
                    throw new Exception(
                        "Planet PNG '" +
                        faceFileName +
                        "' is missing from runtime archive " +
                        SourceArchiveFile +
                        ".");
                }
            }

            image =
                PlanetMapOperations.DecodePlanetPng(
                    faceFileName,
                    png);

            _images.Add(
                faceFileName,
                image);

            return image;
        }


        private int _nextMaterialMapCandidate;


        private void EnsureUsedMaterialMapValues()
        {
            if (_usedMaterialMapValues != null)
                return;

            var used =
                new bool[256];

            string[] materialFaceFiles =
            {
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };

            for (int faceIndex = 0;
                faceIndex < materialFaceFiles.Length;
                faceIndex++)
            {
                byte[] materialValues =
                    GetOrLoadImage(
                        materialFaceFiles[faceIndex]).Planes[0];

                for (int pixelIndex = 0;
                    pixelIndex < materialValues.Length;
                    pixelIndex++)
                {
                    used[materialValues[pixelIndex]] = true;
                }
            }

            if (Builder.DefaultSurfaceMaterial != null)
                used[Builder.DefaultSurfaceMaterial.Value] = true;

            if (Builder.DefaultSubSurfaceMaterial != null)
                used[Builder.DefaultSubSurfaceMaterial.Value] = true;

            if (Builder.CustomMaterialTable != null)
            {
                for (int i = 0;
                    i < Builder.CustomMaterialTable.Length;
                    i++)
                {
                    MyPlanetMaterialDefinition material =
                        Builder.CustomMaterialTable[i];

                    if (material != null)
                        used[material.Value] = true;
                }
            }

            if (Builder.ComplexMaterials != null)
            {
                for (int i = 0;
                    i < Builder.ComplexMaterials.Length;
                    i++)
                {
                    MyPlanetMaterialGroup group =
                        Builder.ComplexMaterials[i];

                    if (group != null)
                        used[group.Value] = true;
                }
            }


            _usedMaterialMapValues =
                used;

            _nextMaterialMapCandidate =
                0;
        }


        private void EnsureEditable()
        {
            EnsureOpen();

            if (_pushStarted)
            {
                throw new Exception(
                    "Modification template is immutable after Push().");
            }
        }


        private void EnsureOpen()
        {
            if (_closed)
            {
                throw new Exception(
                    "Planet modification template is closed.");
            }
        }
    }


}
