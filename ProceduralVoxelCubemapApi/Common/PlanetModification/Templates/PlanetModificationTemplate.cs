using System;
using System.Collections.Generic;
using System.Linq;
using Adk.Image.Png;
using Generated;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VoxelCubemapApi.Common.Noise;
using VoxelCubemapApi.Common.PlanetModification.EnvironmentPresets;
using VoxelCubemapApi.Common.PlanetModification.Features;
using VoxelCubemapApi.Common.PlanetModification.Maps;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Runtime;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace VoxelCubemapApi.Common.PlanetModification.Templates
{
    /// <summary>
    /// Server-owned mutable template exposed to another mod only through a
    /// dictionary of BCL delegates.
    /// </summary>
    [ApiProvider(ClientNamespace = "VoxelCubemapApi.Api", ClientName = "ModificationTemplate")]
    internal sealed partial class PlanetModificationTemplate
    {
        private readonly PlanetModificationCoordinator _coordinator;
        private readonly PlanetDataArchiveService _planetDataArchives;

        private Dictionary<string, PlanarPngBitmap> _images =
            CreateImageDictionary();

        private Dictionary<string,
                List<Action<int, int, byte[], byte[], byte[], byte[]>>>
            _imageTransforms =
                CreateImageTransformDictionary();

        private Dictionary<string, byte[]> _sourceArchiveFiles;
        private bool[] _usedMaterialMapValues;

        private readonly List<FractalNoiseOperation>
            _fractalNoiseOperations =
                new List<FractalNoiseOperation>();

        private readonly List<BiomeReplacementOperation>
            _biomeReplacementOperations =
                new List<BiomeReplacementOperation>();

        private readonly List<BrushOperation>
            _brushOperations =
                new List<BrushOperation>();

        private readonly List<FeatureOperation>
            _featureOperations =
                new List<FeatureOperation>();

        private readonly List<byte> _allocatedComplexMaterialValues =
            new List<byte>();

        private readonly EnvironmentPresetCatalog _environmentPresetCatalog;
        private string _environmentCarrierSubtype;
        private string _environmentPresetName;
        private bool _requiresAuthoritativeImageSync;
        private bool _changeMaterials;
        private readonly bool _proceduralPersistenceEligible;

        private readonly RuntimeProceduralPlanetRecipe
            _inheritedProceduralRecipe;

        private bool _closed;
        private bool _pushStarted;


        public readonly MyPlanet TargetPlanet;
        public readonly MyModContext SourceContext;
        public readonly string SourceSubtype;
        public readonly string SourceFolderName;
        public readonly string SourceArchiveFile;
        public readonly string CurrentProviderSubtype;
        public readonly ulong BaseRuntimeRevision;
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
            ulong baseRuntimeRevision,
            bool proceduralPersistenceEligible,
            RuntimeProceduralPlanetRecipe inheritedProceduralRecipe,
            long planetSeed,
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            string environmentCarrierSubtype,
            EnvironmentPresetCatalog environmentPresetCatalog)
        {
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));

            if (planetDataArchives == null)
                throw new ArgumentNullException(nameof(planetDataArchives));

            if (environmentPresetCatalog == null)
                throw new ArgumentNullException(nameof(environmentPresetCatalog));

            _coordinator =
                coordinator;

            _planetDataArchives =
                planetDataArchives;

            _environmentPresetCatalog =
                environmentPresetCatalog;

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

            BaseRuntimeRevision =
                baseRuntimeRevision;

            _proceduralPersistenceEligible =
                proceduralPersistenceEligible;

            _inheritedProceduralRecipe =
                inheritedProceduralRecipe;

            PlanetSeed =
                planetSeed;

            Builder =
                builder;

            _environmentCarrierSubtype =
                environmentCarrierSubtype;

            if (!string.IsNullOrWhiteSpace(
                    _environmentCarrierSubtype))
            {
                EnsureLayerEnabled(1);
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


            // Push makes the template immutable, so the background snapshot can
            // take exclusive ownership of the already-loaded image state instead
            // of synchronously deep-cloning every decoded cubemap plane.
            //
            // Detach both dictionaries from the template before returning the
            // snapshot. This is important because Close() clears the template's
            // current dictionaries; after the swap it cannot invalidate data that
            // the background worker owns.
            Dictionary<string, PlanarPngBitmap> images =
                _images;
            _images =
                CreateImageDictionary();

            Dictionary<string,
                List<Action<int, int, byte[], byte[], byte[], byte[]>>> transforms =
                _imageTransforms;
            _imageTransforms =
                CreateImageTransformDictionary();


            var fractalOperations =
                new List<FractalNoiseOperation>(
                    _fractalNoiseOperations.Count);
            fractalOperations.AddRange(_fractalNoiseOperations.Select(operation => new FractalNoiseOperation
            {
                PlaneIndex = operation.PlaneIndex,
                TargetValue = operation.TargetValue,
                CoveragePercent = operation.CoveragePercent
            }));

            var biomeReplacements =
                new List<BiomeReplacementOperation>(
                    _biomeReplacementOperations.Count);
            biomeReplacements.AddRange(_biomeReplacementOperations.Select(operation => new BiomeReplacementOperation
            {
                SourceBiome = operation.SourceBiome,
                TargetBiome = operation.TargetBiome
            }));

            var brushOperations =
                new List<BrushOperation>(
                    _brushOperations.Count);
            brushOperations.AddRange(
                _brushOperations.Select(operation => new BrushOperation
                {
                    LayerIndex = operation.LayerIndex,
                    FillValue = operation.FillValue,
                    UseNoise = operation.UseNoise,
                    NoiseFrequency = operation.NoiseFrequency,
                    NoiseOctaves = operation.NoiseOctaves,
                    NoiseSeedOffset = operation.NoiseSeedOffset,
                    BlendNoiseMinimum = operation.BlendNoiseMinimum,
                    BlendNoiseMaximum = operation.BlendNoiseMaximum,
                    MinimumAltitude = operation.MinimumAltitude,
                    MaximumAltitude = operation.MaximumAltitude,
                    MinimumLatitude = operation.MinimumLatitude,
                    MaximumLatitude = operation.MaximumLatitude,
                    BiomeFilter = operation.BiomeFilter,
                    MaterialFilter = operation.MaterialFilter,
                    NoiseType = operation.NoiseType,
                    HeightBlendMode = operation.HeightBlendMode,
                    NoiseSamplingQuality = operation.NoiseSamplingQuality,
                    ScaleHeightByNoise = operation.ScaleHeightByNoise,
                    UseRadial = operation.UseRadial,
                    RadialCenterX = operation.RadialCenterX,
                    RadialCenterY = operation.RadialCenterY,
                    RadialCenterZ = operation.RadialCenterZ,
                    RadialRadiusDegrees = operation.RadialRadiusDegrees,
                    RadialProfile = operation.RadialProfile,
                    ScaleHeightByRadial = operation.ScaleHeightByRadial
                }));

            var featureOperations = new List<FeatureOperation>(_featureOperations.Count);

            featureOperations.AddRange(_featureOperations.Select(FeatureStepRegistry.Clone));

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
                BaseRuntimeRevision = BaseRuntimeRevision,
                PlanetSeed = PlanetSeed,
                TemplateId = TemplateId,
                Builder = Builder,
                Images = images,
                ImageTransforms = transforms,
                FractalNoiseOperations = fractalOperations,
                BiomeReplacementOperations = biomeReplacements,
                BrushOperations = brushOperations,
                FeatureOperations = featureOperations,
                AllocatedComplexMaterialValues = allocatedComplexValues,
                EnvironmentCarrierSubtype = _environmentCarrierSubtype,
                EnvironmentPresetName = _environmentPresetName,
                RequiresAuthoritativeImageSync =
                    _requiresAuthoritativeImageSync,
                ChangeMaterials =
                    _changeMaterials,
                ProceduralPersistenceEligible =
                    _proceduralPersistenceEligible &&
                    !_requiresAuthoritativeImageSync,
                InheritedProceduralRecipe =
                    _inheritedProceduralRecipe
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

            _requiresAuthoritativeImageSync =
                true;

            PlanarPngBitmap image =
                GetOrLoadImage(
                    faceFileName);

            if (faceFileName.EndsWith(
                    "_mat.png",
                    StringComparison.OrdinalIgnoreCase))
            {
                _changeMaterials =
                    true;
            }

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

            var used = new bool[256];
            foreach (var t in materialFaceFiles)
            {
                var biomes =
                    GetOrLoadImage(
                        t).Planes[1];

                foreach (var t1 in biomes) used[t1] = true;
            }

            var result =
                new List<byte>();

            for (var value = used.Length - 1; value >= 0; value--)
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
                throw new ArgumentNullException(nameof(transform));


            faceFileName =
                PlanetMapFileNames.Validate(
                    faceFileName);

            if (faceFileName.EndsWith(
                    "_mat.png",
                    StringComparison.OrdinalIgnoreCase))
            {
                _changeMaterials =
                    true;
            }

            _requiresAuthoritativeImageSync =
                true;

            List<Action<int, int, byte[], byte[], byte[], byte[]>> transforms;

            if (!_imageTransforms.TryGetValue(faceFileName, out transforms))
            {
                transforms = new List<Action<int, int, byte[], byte[], byte[], byte[]>>();

                _imageTransforms.Add(faceFileName, transforms);
            }

            transforms.Add(transform);
        }


        /// <summary>
        /// Adds a simple voxel material definition at the requested map value.
        /// The existing explicit-ID contract is preserved.
        /// </summary>
        [ApiMethod]
        private bool AddMaterial(
            string materialSubtype,
            byte mapValue,
            float maxDepth)
        {
            EnsureEditable();
            ValidateSimpleMaterial(
                materialSubtype,
                maxDepth);

            // Initialize this before checking the requested value so explicit
            // registration cannot accidentally collide with a value already
            // present in one of the six source material maps.
            EnsureUsedMaterialMapValues();

            if (_usedMaterialMapValues[mapValue])
                return false;

            AppendSimpleMaterial(
                materialSubtype,
                mapValue,
                maxDepth);

            _usedMaterialMapValues[mapValue] =
                true;

            return true;
        }


        /// <summary>
        /// Adds a simple voxel material definition using a server-allocated map
        /// value and returns that value. This is the preferred registration path
        /// when the caller does not require a specific byte ID.
        /// </summary>
        [ApiMethod("AddMaterialSequential")]
        private byte AddMaterial(
            string materialSubtype,
            float maxDepth)
        {
            EnsureEditable();
            ValidateSimpleMaterial(
                materialSubtype,
                maxDepth);

            EnsureUsedMaterialMapValues();

            byte mapValue =
                PlanetMaterialMap.AllocateValue(
                    _usedMaterialMapValues,
                    ref _nextMaterialMapCandidate);

            // AllocateValue already marks the chosen byte as used.
            AppendSimpleMaterial(
                materialSubtype,
                mapValue,
                maxDepth);

            return mapValue;
        }


        private static void ValidateSimpleMaterial(
            string materialSubtype,
            float maxDepth)
        {
            if (string.IsNullOrWhiteSpace(
                    materialSubtype))
            {
                throw new ArgumentException(
                    "Material subtype cannot be empty.",
                    nameof(materialSubtype));
            }

            if (maxDepth < 0f ||
                float.IsNaN(maxDepth) ||
                float.IsInfinity(maxDepth))
            {
                throw new ArgumentException(
                    "Material max depth must be a finite non-negative value.",
                    nameof(maxDepth));
            }
        }


        private void AppendSimpleMaterial(
            string materialSubtype,
            byte mapValue,
            float maxDepth)
        {
            Builder.CustomMaterialTable =
                AppendToArray(
                    Builder.CustomMaterialTable,
                    new MyPlanetMaterialDefinition
                    {
                        Material = materialSubtype,
                        Value = mapValue,
                        MaxDepth = maxDepth
                    });
        }


        /// <summary>
        /// Clones a complex material group and returns a map value unused by the
        /// source maps and template definitions.
        /// </summary>
        [ApiMethod]
        private byte AddComplexMaterial(MyPlanetMaterialGroup materialGroup)
        {
            EnsureEditable();

            if (materialGroup == null)
                throw new ArgumentNullException(nameof(materialGroup));

            if (materialGroup.MaterialRules == null ||
                materialGroup.MaterialRules.Length == 0)
            {
                throw new ArgumentException(
                    "Complex material group contains no material rules.",
                    nameof(materialGroup));
            }


            EnsureUsedMaterialMapValues();

            byte mapValue =
                PlanetMaterialMap.AllocateValue(
                    _usedMaterialMapValues,
                    ref _nextMaterialMapCandidate);

            var clone =
                (MyPlanetMaterialGroup)
                materialGroup.Clone();

            clone.Value = mapValue;

            if (string.IsNullOrWhiteSpace(clone.Name))
            {
                clone.Name =
                    "ApiComplexMaterial_" +
                    mapValue;
            }


            Builder.ComplexMaterials = AppendToArray(Builder.ComplexMaterials, clone);

            _allocatedComplexMaterialValues.Add(mapValue);

            return mapValue;
        }

        private static T[] AppendToArray<T>(
            T[] existing,
            params T[] additions)
        {
            int existingCount = existing?.Length ?? 0;

            var output = new T[existingCount + additions.Length];

            if (existingCount > 0 && existing != null)
                Array.Copy(existing, output, existingCount);

            Array.Copy(additions, 0, output, existingCount, additions.Length);

            return output;
        }


        /// <summary>
        /// Appends client-authored vegetation/environment mappings and returns
        /// the number added.
        /// </summary>
        [ApiMethod]
        private int AddEnvironmentItems(PlanetEnvironmentItemMapping[] mappings)
        {
            EnsureEditable();

            if (!string.IsNullOrWhiteSpace(_environmentCarrierSubtype) || !string.IsNullOrWhiteSpace(_environmentPresetName))
            {
                throw new Exception(
                    "This template already uses an explicit or preset " +
                    "WorldEnvironmentDefinition. Legacy EnvironmentItems " +
                    "cannot be appended to it.");
            }

            if (mappings == null ||
                mappings.Length == 0)
            {
                throw new ArgumentException(
                    "At least one environment-item mapping is required.",
                    nameof(mappings));
            }

            if (Builder.Environment.HasValue)
            {
                throw new Exception(
                    "This planet uses a WorldEnvironmentDefinition. " +
                    "Appending legacy EnvironmentItems is not supported.");
            }


            var additions =
                new PlanetEnvironmentItemMapping[mappings.Length];


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
                        nameof(mappings));
                }

                if (mapping.Items == null ||
                    mapping.Items.Length == 0)
                {
                    throw new ArgumentException(
                        "Environment mapping " +
                        mappingIndex +
                        " contains no environment items.",
                        nameof(mappings));
                }

                additions[mappingIndex] =
                    CloneEnvironmentItemMapping(
                        mapping,
                        mappingIndex);
            }


            Builder.EnvironmentItems = AppendToArray(Builder.EnvironmentItems, additions);

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


            EnsureLayerEnabled(1);

            // Persist the actual caller environment id into the generated
            // planet definition as well. Runtime registrations do not run
            // MyPlanetGeneratorDefinition.Postprocessor, so the carrier
            // subtype is persisted separately for live/reload rebinding.
            if (carrier.EnvironmentId != null)
                Builder.Environment = new SerializableDefinitionId(
                    carrier.EnvironmentId.Value.TypeId, 
                    carrier.EnvironmentId.Value.SubtypeName);

            // Explicit procedural definitions and legacy EnvironmentItems are
            // different engine paths. Selecting an explicit environment
            // replaces inherited/legacy mappings for this terraform revision.
            Builder.EnvironmentItems = null;

            _environmentCarrierSubtype = carrier.Id.SubtypeName;

            _environmentPresetName =
                null;
        }


        /// <summary>
        /// Selects and later remaps a vegetation/environment preset from the
        /// loaded planet definition library. The last explicit/preset setter
        /// wins.
        /// </summary>
        [ApiMethod]
        private void SetEnvironmentPreset(
            string presetName)
        {
            EnsureEditable();

            EnvironmentPresetSnapshot preset =
                _environmentPresetCatalog.Resolve(
                    presetName);

            EnsureLayerEnabled(1);

            _environmentPresetName =
                preset.Name;

            _environmentCarrierSubtype =
                null;

            Builder.Environment =
                null;

            Builder.EnvironmentItems =
                null;
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

                if (item == null || string.IsNullOrWhiteSpace(item.TypeId))
                {
                    throw new ArgumentException(
                        "Environment mapping " +
                        mappingIndex +
                        " item " +
                        itemIndex +
                        " has no TypeId.",
                        nameof(item.TypeId));
                }

                if (item.Density < 0f)
                {
                    throw new ArgumentException(
                        "Environment mapping " +
                        mappingIndex +
                        " item " +
                        itemIndex +
                        " has a negative density.",
                        nameof(item.Density));
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
                Rule = (MyPlanetSurfaceRule)source.Rule?.Clone()
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
                    nameof(coveragePercent));
            }

            if (!PlanetMaterialMap.UsesValue(
                    Builder,
                    mapValue))
            {
                throw new ArgumentException(
                    "Material-map value " +
                    mapValue +
                    " is not defined in this template.",
                    nameof(mapValue));
            }


            EnsureLayerEnabled(0);
            _changeMaterials =
                true;
            _fractalNoiseOperations.Add(
                new FractalNoiseOperation
                {
                    PlaneIndex = 0,
                    TargetValue = mapValue,
                    CoveragePercent = coveragePercent
                });
        }


        private void EnsureLayerEnabled(int layerIndex)
        {
            var planetMaps = Builder.PlanetMaps.GetValueOrDefault();
            switch (layerIndex)
            {
                case 0:
                    if (planetMaps.Material)
                        return;
                    planetMaps.Material = true;
                    break;
                case 1:
                    if (planetMaps.Biome)
                        return;
                    planetMaps.Biome = true;
                    break;
                case 2:
                    if (planetMaps.Ores)
                        return;
                    planetMaps.Ores = true;
                    break;
                case 3:
                    // Brush layer 3 is the separate 16-bit heightmap.
                    // It is not controlled by PlanetMaps.
                    return;
                default:
                    throw new ArgumentException(nameof(layerIndex));
            }

            Builder.PlanetMaps = planetMaps;

            string name = new[] { "Material", "Biome", "Ores" }[layerIndex];
            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Enabled Planet Map layer " +
                $"{name} for runtime map editing " +
                $"(source generator had {name}=false).");
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

            EnsureLayerEnabled(1);

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
                    nameof(coveragePercent));
            }

            EnsureLayerEnabled(1);

            _fractalNoiseOperations.Add(
                new FractalNoiseOperation
                {
                    PlaneIndex = 1,
                    TargetValue = biomeValue,
                    CoveragePercent = coveragePercent
                });
        }


        /// <summary>
        /// Queues a filtered cubemap brush. The layer is Material, Biome, Ore,
        /// or Heightmap. Material/Biome/Ore fills use byte values; Heightmap
        /// uses an unsigned 16-bit sample. Altitude filters use that same
        /// unsigned 16-bit height sample and -1 disables either bound.
        /// Latitude is signed degrees. Biome/material filters use -1 for any.
        /// When procedural noise is enabled, only pixels whose normalized
        /// seamless cubemap noise falls inside the inclusive blend range are
        /// filled. Noise-specific parameters are ignored when useNoise is false.
        /// </summary>
        [ApiMethod]
        private void ApplyBrush(
            string layer,
            int fillValue,
            bool useNoise,
            double noiseFrequency,
            int noiseOctaves,
            int noiseSeedOffset,
            double blendNoiseMinimum,
            double blendNoiseMaximum,
            int minimumAltitude,
            int maximumAltitude,
            double minimumLatitude,
            double maximumLatitude,
            int biomeFilter,
            int materialFilter)
        {
            EnsureEditable();

            int layerIndex =
                ParseBrushLayer(
                    layer);

            int maximumFillValue =
                layerIndex == 3
                    ? ushort.MaxValue
                    : byte.MaxValue;

            if (fillValue < 0 ||
                fillValue > maximumFillValue)
            {
                throw new ArgumentException(
                    "Brush fill value is outside the target layer range.",
                    nameof(fillValue));
            }

            if (layerIndex == 0 &&
                !PlanetMaterialMap.UsesValue(
                    Builder,
                    (byte)fillValue))
            {
                throw new ArgumentException(
                    "Material-map value " +
                    fillValue +
                    " is not defined in this template.",
                    nameof(fillValue));
            }

            EnsureLayerEnabled(
                layerIndex);

            if (useNoise)
            {
                if (double.IsNaN(noiseFrequency) ||
                    double.IsInfinity(noiseFrequency) ||
                    noiseFrequency <= 0.0)
                {
                    throw new ArgumentException(
                        "Noise frequency must be finite and greater than zero.",
                        nameof(noiseFrequency));
                }

                if (noiseOctaves < 1 ||
                    noiseOctaves > 8)
                {
                    throw new ArgumentException(
                        "Noise octaves must be from 1 to 8.",
                        nameof(noiseOctaves));
                }

                ValidateUnitRange(
                    blendNoiseMinimum,
                    "blendNoiseMinimum");

                ValidateUnitRange(
                    blendNoiseMaximum,
                    "blendNoiseMaximum");

                if (blendNoiseMinimum > blendNoiseMaximum)
                {
                    throw new ArgumentException(
                        "Blend noise minimum cannot exceed the maximum.",
                        nameof(blendNoiseMinimum));
                }
            }

            ValidateAltitudeBound(
                minimumAltitude,
                "minimumAltitude");

            ValidateAltitudeBound(
                maximumAltitude,
                "maximumAltitude");

            if (minimumAltitude >= 0 &&
                maximumAltitude >= 0 &&
                minimumAltitude > maximumAltitude)
            {
                throw new ArgumentException(
                    "Minimum altitude cannot exceed maximum altitude.",
                    nameof(minimumAltitude));
            }

            ValidateLatitude(
                minimumLatitude,
                "minimumLatitude");

            ValidateLatitude(
                maximumLatitude,
                "maximumLatitude");

            if (minimumLatitude > maximumLatitude)
            {
                throw new ArgumentException(
                    "Minimum latitude cannot exceed maximum latitude.",
                    nameof(minimumLatitude));
            }

            ValidateByteFilter(
                biomeFilter,
                "biomeFilter");

            ValidateByteFilter(
                materialFilter,
                "materialFilter");

            if (layerIndex == 0)
                _changeMaterials = true;

            _brushOperations.Add(
                new BrushOperation
                {
                    LayerIndex = layerIndex,
                    FillValue = fillValue,
                    UseNoise = useNoise,
                    NoiseFrequency = noiseFrequency,
                    NoiseOctaves = noiseOctaves,
                    NoiseSeedOffset = noiseSeedOffset,
                    BlendNoiseMinimum = blendNoiseMinimum,
                    BlendNoiseMaximum = blendNoiseMaximum,
                    MinimumAltitude = minimumAltitude,
                    MaximumAltitude = maximumAltitude,
                    MinimumLatitude = minimumLatitude,
                    MaximumLatitude = maximumLatitude,
                    BiomeFilter = biomeFilter,
                    MaterialFilter = materialFilter,
                    NoiseType = 0,
                    HeightBlendMode = 0,
                    NoiseSamplingQuality = (int)NoiseSamplingQuality.Low,
                    ScaleHeightByNoise = false
                });
        }


        /// <summary>
        /// Queues a brush driven by one of the API-owned procedural noise
        /// generators. For Heightmap brushes the sampled normalized noise
        /// scales fillValue, then heightBlendMode selects Replace/Add/Subtract.
        /// Non-height layers retain normal replacement semantics and use the
        /// noise only for pixel selection.
        /// </summary>
        [ApiMethod]
        private void ApplyNoiseBrush(string layer,
            int fillValue,
            int noiseType,
            int heightBlendMode,
            NoiseSamplingQuality samplingQuality,
            double noiseFrequency,
            int noiseOctaves,
            int noiseSeedOffset,
            double blendNoiseMinimum,
            double blendNoiseMaximum,
            int minimumAltitude,
            int maximumAltitude,
            double minimumLatitude,
            double maximumLatitude,
            int biomeFilter,
            int materialFilter)
        {
            EnsureEditable();

            int layerIndex = ParseBrushLayer(layer);
            int maximumFillValue = layerIndex == 3 ? ushort.MaxValue : byte.MaxValue;

            if (fillValue < 0 || fillValue > maximumFillValue)
                throw new ArgumentException("Brush fill value is outside the target layer range.", nameof(fillValue));

            if (noiseType < 0 || noiseType > 9)
                throw new ArgumentException("Unknown procedural noise type.", nameof(noiseType));

            if (heightBlendMode < 0 || heightBlendMode > 2)
                throw new ArgumentException("Height blend mode must be Replace, Add, or Subtract.",
                    nameof(heightBlendMode));

            if (samplingQuality < NoiseSamplingQuality.Low ||
                samplingQuality > NoiseSamplingQuality.Direct)
            {
                throw new ArgumentException("Unknown noise sampling quality.", nameof(samplingQuality));
            }

            if (double.IsNaN(noiseFrequency) || double.IsInfinity(noiseFrequency) || noiseFrequency <= 0.0)
                throw new ArgumentException("Noise frequency must be finite and greater than zero.",
                    nameof(noiseFrequency));

            if (noiseOctaves < 1 || noiseOctaves > 8)
                throw new ArgumentException("Noise octaves must be from 1 to 8.", nameof(noiseOctaves));

            ValidateUnitRange(blendNoiseMinimum, "blendNoiseMinimum");
            ValidateUnitRange(blendNoiseMaximum, "blendNoiseMaximum");
            if (blendNoiseMinimum > blendNoiseMaximum)
                throw new ArgumentException("Blend noise minimum cannot exceed the maximum.",
                    nameof(blendNoiseMinimum));

            ValidateAltitudeBound(minimumAltitude, "minimumAltitude");
            ValidateAltitudeBound(maximumAltitude, "maximumAltitude");
            if (minimumAltitude >= 0 && maximumAltitude >= 0 && minimumAltitude > maximumAltitude)
                throw new ArgumentException("Minimum altitude cannot exceed maximum altitude.",
                    nameof(minimumAltitude));

            ValidateLatitude(minimumLatitude, "minimumLatitude");
            ValidateLatitude(maximumLatitude, "maximumLatitude");
            if (minimumLatitude > maximumLatitude)
                throw new ArgumentException("Minimum latitude cannot exceed maximum latitude.",
                    nameof(minimumLatitude));

            ValidateByteFilter(biomeFilter, "biomeFilter");
            ValidateByteFilter(materialFilter, "materialFilter");

            if (layerIndex == 0 && !PlanetMaterialMap.UsesValue(Builder, (byte)fillValue))
                throw new ArgumentException("Material-map value " + fillValue + " is not defined in this template.",
                    nameof(fillValue));

            EnsureLayerEnabled(
                layerIndex);

            if (layerIndex == 0)
                _changeMaterials = true;

            _brushOperations.Add(new BrushOperation
            {
                LayerIndex = layerIndex,
                FillValue = fillValue,
                UseNoise = true,
                NoiseFrequency = noiseFrequency,
                NoiseOctaves = noiseOctaves,
                NoiseSeedOffset = noiseSeedOffset,
                BlendNoiseMinimum = blendNoiseMinimum,
                BlendNoiseMaximum = blendNoiseMaximum,
                MinimumAltitude = minimumAltitude,
                MaximumAltitude = maximumAltitude,
                MinimumLatitude = minimumLatitude,
                MaximumLatitude = maximumLatitude,
                BiomeFilter = biomeFilter,
                MaterialFilter = materialFilter,
                NoiseType = noiseType,
                HeightBlendMode = heightBlendMode,
                NoiseSamplingQuality = (int)samplingQuality,
                ScaleHeightByNoise = layerIndex == 3
            });
        }


        /// <summary>
        /// Queues a spherical radial field brush. Center coordinates are a planet-space
        /// direction and are normalized by the API. Radius is expressed in angular degrees.
        /// </summary>
        [ApiMethod]
        private void ApplyRadialBrush(
            string layer,
            int fillValue,
            double centerX,
            double centerY,
            double centerZ,
            double radiusDegrees,
            RadialFieldProfile radialProfile,
            int heightBlendMode,
            int minimumAltitude,
            int maximumAltitude,
            double minimumLatitude,
            double maximumLatitude,
            int biomeFilter,
            int materialFilter)
        {
            EnsureEditable();

            int layerIndex = ParseBrushLayer(layer);
            int maximumFillValue = layerIndex == 3 ? ushort.MaxValue : byte.MaxValue;

            if (fillValue < 0 || fillValue > maximumFillValue)
                throw new ArgumentException("Brush fill value is outside the target layer range.", nameof(fillValue));

            if (double.IsNaN(centerX) || double.IsInfinity(centerX) ||
                double.IsNaN(centerY) || double.IsInfinity(centerY) ||
                double.IsNaN(centerZ) || double.IsInfinity(centerZ))
            {
                throw new ArgumentException("Radial center must contain finite coordinates.");
            }

            double centerLengthSquared = centerX * centerX + centerY * centerY + centerZ * centerZ;
            if (centerLengthSquared < 1e-12)
                throw new ArgumentException("Radial center direction cannot be zero.");

            double inverseLength = 1.0 / Math.Sqrt(centerLengthSquared);
            centerX *= inverseLength;
            centerY *= inverseLength;
            centerZ *= inverseLength;

            if (double.IsNaN(radiusDegrees) || double.IsInfinity(radiusDegrees) ||
                radiusDegrees <= 0.0 || radiusDegrees > 180.0)
            {
                throw new ArgumentException("Radial radius must be greater than zero and no more than 180 degrees.",
                    nameof(radiusDegrees));
            }

            if (radialProfile < RadialFieldProfile.Linear ||
                radialProfile > RadialFieldProfile.Crater)
                throw new ArgumentException("Unknown radial field profile.", nameof(radialProfile));

            if (heightBlendMode < 0 || heightBlendMode > 2)
                throw new ArgumentException("Height blend mode must be Replace, Add, or Subtract.",
                    nameof(heightBlendMode));

            if (radialProfile == RadialFieldProfile.Crater && layerIndex != 3)
                throw new ArgumentException(
                    "The signed Crater radial profile can only be applied to the Heightmap layer.",
                    nameof(radialProfile));

            ValidateAltitudeBound(minimumAltitude, "minimumAltitude");
            ValidateAltitudeBound(maximumAltitude, "maximumAltitude");
            if (minimumAltitude >= 0 && maximumAltitude >= 0 && minimumAltitude > maximumAltitude)
                throw new ArgumentException("Minimum altitude cannot exceed maximum altitude.",
                    nameof(minimumAltitude));

            ValidateLatitude(minimumLatitude, "minimumLatitude");
            ValidateLatitude(maximumLatitude, "maximumLatitude");
            if (minimumLatitude > maximumLatitude)
                throw new ArgumentException("Minimum latitude cannot exceed maximum latitude.",
                    nameof(minimumLatitude));

            ValidateByteFilter(biomeFilter, "biomeFilter");
            ValidateByteFilter(materialFilter, "materialFilter");

            if (layerIndex == 0 && !PlanetMaterialMap.UsesValue(Builder, (byte)fillValue))
                throw new ArgumentException("Material-map value " + fillValue + " is not defined in this template.",
                    nameof(fillValue));

            EnsureLayerEnabled(
                layerIndex);

            if (layerIndex == 0)
                _changeMaterials = true;

            _brushOperations.Add(new BrushOperation
            {
                LayerIndex = layerIndex,
                FillValue = fillValue,
                UseNoise = false,
                MinimumAltitude = minimumAltitude,
                MaximumAltitude = maximumAltitude,
                MinimumLatitude = minimumLatitude,
                MaximumLatitude = maximumLatitude,
                BiomeFilter = biomeFilter,
                MaterialFilter = materialFilter,
                HeightBlendMode = heightBlendMode,
                UseRadial = true,
                RadialCenterX = centerX,
                RadialCenterY = centerY,
                RadialCenterZ = centerZ,
                RadialRadiusDegrees = radiusDegrees,
                RadialProfile = (int)radialProfile,
                ScaleHeightByRadial = layerIndex == 3
            });
        }


        /// <summary>
        /// Adds one reusable procedural feature pass and returns its nested template.
        /// Feature generators are stored compactly and expanded only while baking.
        /// </summary>
        [ApiMethod(typeof(FeatureTemplate))]
        private Dictionary<string, Delegate> AddFeature()
        {
            EnsureEditable();

            var operation = new FeatureOperation();
            _featureOperations.Add(operation);
            return new FeatureTemplate(operation).GetApi();
        }


        private static int ParseBrushLayer(
            string layer)
        {
            if (string.Equals(
                    layer,
                    "Material",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(
                    layer,
                    "Biome",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(
                    layer,
                    "Ore",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(
                    layer,
                    "Heightmap",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    layer,
                    "Height",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            throw new ArgumentException(
                "Brush layer must be Material, Biome, Ore, or Heightmap.",
                nameof(layer));
        }


        private static void ValidateUnitRange(
            double value,
            string parameterName)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0.0 ||
                value > 1.0)
            {
                throw new ArgumentException(
                    "Brush noise range values must be from 0 to 1.",
                    parameterName);
            }
        }


        private static void ValidateAltitudeBound(
            int value,
            string parameterName)
        {
            if (value < -1 ||
                value > ushort.MaxValue)
            {
                throw new ArgumentException(
                    "Altitude bounds must be -1 or from 0 to 65535.",
                    parameterName);
            }
        }


        private static void ValidateLatitude(
            double value,
            string parameterName)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < -90.0 ||
                value > 90.0)
            {
                throw new ArgumentException(
                    "Latitude must be finite and from -90 to 90 degrees.",
                    parameterName);
            }
        }


        private static void ValidateByteFilter(
            int value,
            string parameterName)
        {
            if (value < -1 ||
                value > byte.MaxValue)
            {
                throw new ArgumentException(
                    "Brush byte filters must be -1 or from 0 to 255.",
                    parameterName);
            }
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


        private static Dictionary<string, PlanarPngBitmap> CreateImageDictionary()
        {
            return new Dictionary<string, PlanarPngBitmap>(
                StringComparer.OrdinalIgnoreCase);
        }


        private static Dictionary<string,
                List<Action<int, int, byte[], byte[], byte[], byte[]>>>
            CreateImageTransformDictionary()
        {
            return new Dictionary<string,
                List<Action<int, int, byte[], byte[], byte[], byte[]>>>(
                StringComparer.OrdinalIgnoreCase);
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

            foreach (var t in materialFaceFiles)
            {
                byte[] materialValues =
                    GetOrLoadImage(
                        t).Planes[0];

                foreach (var t1 in materialValues) 
                    used[t1] = true;
            }

            if (Builder.DefaultSurfaceMaterial != null)
                used[Builder.DefaultSurfaceMaterial.Value] = true;

            if (Builder.DefaultSubSurfaceMaterial != null)
                used[Builder.DefaultSubSurfaceMaterial.Value] = true;

            if (Builder.CustomMaterialTable != null)
            {
                foreach (var material in Builder.CustomMaterialTable)
                    if (material != null)
                        used[material.Value] = true;
            }

            if (Builder.ComplexMaterials != null)
            {
                foreach (var group in Builder.ComplexMaterials)
                    if (group != null)
                        used[group.Value] = true;
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
