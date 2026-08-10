using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Adk.Compression.Zip;
using Adk.Image.Png;
using VoxelCubemapApi.Api;
using VoxelCubemapApi.Server.Api;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRage.Voxels;
using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;

namespace VoxelCubemapApi.Server
{
    public class RuntimePlanetGeneratorSettings
    {
        public List<RuntimePlanetBuilderEntry> PlanetBuilders =
            new List<RuntimePlanetBuilderEntry>();
    }


    public class RuntimePersistenceManifest
    {
        public List<RuntimePersistencePackageEntry> Packages =
            new List<RuntimePersistencePackageEntry>();
    }


    public class RuntimePersistencePackageEntry
    {
        public string Subtype;
        public long SourceEntityId;
        public string GeneratorFile;
        public string ArchiveFile;
        public int ChunkCount;
        public bool Pending;
    }


    public class RuntimePlanetBuilderEntry
    {
        public string Subtype;
        public string SourceSubtype;
        public long SourceEntityId;
        public string EnvironmentCarrierSubtype;
        public string GeneratorFile;
        public string ArchiveFile;
        public byte GrassMaterialValue;
        public int GrassCoveragePercent;
        public long PlanetSeed;
        public int GrassNoiseVersion;
    }


    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, -1000)]
    internal sealed class VoxelCubemapApiServer : MySessionComponentBase
    {
        private sealed class PlanetModificationWorkResult
        {
            public MyPlanet TargetPlanet;
            public object OriginalStorage;
            public byte[] PatchedStorage;
            public MyPlanetGeneratorDefinition ReplacementGenerator;
            public RuntimePlanetBuilderEntry NewEntry;
            public string EnvironmentCarrierSubtype;
            public string OperationName;
        }


        private sealed class PendingVegetationClear
        {
            public long PlanetEntityId;
            public List<BoundingBoxD> Boxes;
            public int Pass;
            public int TicksUntilNextPass;
        }


        private sealed class FractalNoiseOperation
        {
            public int PlaneIndex;
            public byte TargetValue;
            public int CoveragePercent;
            public double Threshold;
        }


        private sealed class BiomeReplacementOperation
        {
            public byte SourceBiome;
            public byte TargetBiome;
        }


        private sealed class PlanetModificationSnapshot
        {
            public MyPlanet TargetPlanet;
            public MyModContext SourceContext;
            public string SourceSubtype;
            public string SourceFolderName;
            public string SourceArchiveFile;
            public string CurrentProviderSubtype;
            public long PlanetSeed;
            public string TemplateId;
            public MyObjectBuilder_PlanetGeneratorDefinition Builder;
            public Dictionary<string, PlanarPngBitmap> Images;
            public Dictionary<string,
                List<Action<int, int, byte[], byte[], byte[], byte[]>>>
                    ImageTransforms;
            public List<FractalNoiseOperation> FractalNoiseOperations;
            public List<BiomeReplacementOperation> BiomeReplacementOperations;
            public List<byte> AllocatedComplexMaterialValues;
            public string EnvironmentCarrierSubtype;
        }


        /// <summary>
        /// Server-owned mutable template exposed to another mod only through a
        /// dictionary of BCL delegates.
        /// </summary>
        private sealed class PlanetModificationTemplate
        {
            private readonly VoxelCubemapApiServer m_server;
            private readonly Dictionary<string, PlanarPngBitmap> m_images =
                new Dictionary<string, PlanarPngBitmap>(
                    StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string,
                List<Action<int, int, byte[], byte[], byte[], byte[]>>>
                    m_imageTransforms =
                        new Dictionary<string,
                            List<Action<int, int, byte[], byte[], byte[], byte[]>>>(
                                StringComparer.OrdinalIgnoreCase);

            private Dictionary<string, byte[]> m_sourceArchiveFiles;
            private bool[] m_usedMaterialMapValues;

            private readonly List<FractalNoiseOperation>
                m_fractalNoiseOperations =
                    new List<FractalNoiseOperation>();

            private readonly List<BiomeReplacementOperation>
                m_biomeReplacementOperations =
                    new List<BiomeReplacementOperation>();

            private readonly List<byte> m_allocatedComplexMaterialValues =
                new List<byte>();

            private string m_environmentCarrierSubtype;

            private bool m_closed;
            private bool m_pushStarted;


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
                VoxelCubemapApiServer server,
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
                m_server =
                    server;

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

                m_environmentCarrierSubtype =
                    environmentCarrierSubtype;

                if (!string.IsNullOrWhiteSpace(
                    m_environmentCarrierSubtype))
                {
                    EnsureBiomePlanetMapEnabled();
                }

                TemplateId =
                    StableFolderId(
                        targetPlanet.EntityId +
                        "|" +
                        planetSeed +
                        "|" +
                        DateTime.UtcNow.Ticks);
            }


            public ApiData CreateApi()
            {
                return new ApiData
                {
                    {
                        "GetPlanetEntityId",
                        new Func<long>(
                            GetPlanetEntityId)
                    },
                    {
                        "GetPlanetSeed",
                        new Func<long>(
                            GetPlanetSeed)
                    },
                    {
                        "LoadPlanetPng",
                        new Func<string, byte[][]>(
                            LoadPlanetPng)
                    },
                    {
                        "GetPlanetPngSize",
                        new Func<string, int[]>(
                            GetPlanetPngSize)
                    },
                    {
                        "GetPlanetPngInfo",
                        new Func<string, int[]>(
                            GetPlanetPngInfo)
                    },
                    {
                        "GetUsedBiomes",
                        new Func<byte[]>(
                            GetUsedBiomes)
                    },
                    {
                        "ApplyPlanetImage",
                        new Action<string,
                            Action<int, int, byte[], byte[], byte[], byte[]>>(
                                ApplyPlanetImage)
                    },
                    {
                        "AddMaterial",
                        new Func<string, byte, float, bool>(
                            AddMaterial)
                    },
                    {
                        "AddComplexMaterial",
                        new Func<MyPlanetMaterialGroup, byte>(
                            AddComplexMaterial)
                    },
                    {
                        "AddEnvironmentItems",
                        new Func<PlanetEnvironmentItemMapping[], int>(
                            AddEnvironmentItems)
                    },
                    {
                        "SetEnvironmentDefinition",
                        new Action<string>(
                            SetEnvironmentDefinition)
                    },
                    {
                        "RemoveMaterial",
                        new Func<byte, bool>(
                            RemoveMaterial)
                    },
                    {
                        "ApplyFractalNoise",
                        new Action<byte, int>(
                            ApplyFractalNoise)
                    },
                    {
                        "ReplaceBiome",
                        new Action<byte, byte>(
                            ReplaceBiome)
                    },
                    {
                        "ApplyBiomeFractalNoise",
                        new Action<byte, int>(
                            ApplyBiomeFractalNoise)
                    },
                    {
                        "Push",
                        new Action<Action<bool, string>>(
                            Push)
                    },
                    {
                        "Close",
                        new Action(
                            Close)
                    }
                };
            }


            public PlanetModificationSnapshot CreateSnapshot()
            {
                EnsureOpen();

                if (m_pushStarted)
                {
                    throw new Exception(
                        "This modification template has already been pushed.");
                }

                m_pushStarted =
                    true;


                var images =
                    new Dictionary<string, PlanarPngBitmap>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, PlanarPngBitmap> pair in m_images)
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
                    m_imageTransforms)
                {
                    transforms.Add(
                        pair.Key,
                        new List<Action<int, int, byte[], byte[], byte[], byte[]>>(
                            pair.Value));
                }


                var fractalOperations =
                    new List<FractalNoiseOperation>(
                        m_fractalNoiseOperations.Count);

                for (int i = 0;
                    i < m_fractalNoiseOperations.Count;
                    i++)
                {
                    FractalNoiseOperation operation =
                        m_fractalNoiseOperations[i];

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
                        m_biomeReplacementOperations.Count);

                for (int i = 0;
                    i < m_biomeReplacementOperations.Count;
                    i++)
                {
                    BiomeReplacementOperation operation =
                        m_biomeReplacementOperations[i];

                    biomeReplacements.Add(
                        new BiomeReplacementOperation
                        {
                            SourceBiome = operation.SourceBiome,
                            TargetBiome = operation.TargetBiome
                        });
                }

                var allocatedComplexValues =
                    new List<byte>(
                        m_allocatedComplexMaterialValues);


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
                    EnvironmentCarrierSubtype = m_environmentCarrierSubtype
                };
            }


            private long GetPlanetEntityId()
            {
                EnsureOpen();

                return TargetPlanet.EntityId;
            }


            private long GetPlanetSeed()
            {
                EnsureOpen();

                return PlanetSeed;
            }


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


            private void ApplyPlanetImage(
                string faceFileName,
                Action<int, int, byte[], byte[], byte[], byte[]> transform)
            {
                EnsureEditable();

                if (transform == null)
                    throw new ArgumentNullException("transform");


                faceFileName =
                    ValidatePlanetFaceFileName(
                        faceFileName);

                List<Action<int, int, byte[], byte[], byte[], byte[]>> transforms;

                if (!m_imageTransforms.TryGetValue(
                    faceFileName,
                    out transforms))
                {
                    transforms =
                        new List<Action<int, int, byte[], byte[], byte[], byte[]>>();

                    m_imageTransforms.Add(
                        faceFileName,
                        transforms);
                }

                transforms.Add(
                    transform);
            }


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

                if (UsesMaterialMapValue(
                    Builder,
                    mapValue) ||
                    (m_usedMaterialMapValues != null &&
                        m_usedMaterialMapValues[mapValue]))
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

                if (m_usedMaterialMapValues != null)
                {
                    m_usedMaterialMapValues[mapValue] =
                        true;
                }

                return true;
            }


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
                    AllocateGrassOverlayValue(
                        m_usedMaterialMapValues,
                        ref m_nextMaterialMapCandidate);

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

                m_allocatedComplexMaterialValues.Add(
                    mapValue);

                return mapValue;
            }


            private int AddEnvironmentItems(
                PlanetEnvironmentItemMapping[] mappings)
            {
                EnsureEditable();

                if (!string.IsNullOrWhiteSpace(
                    m_environmentCarrierSubtype))
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


            private void SetEnvironmentDefinition(
                string carrierPlanetGeneratorSubtype)
            {
                EnsureEditable();

                MyPlanetGeneratorDefinition carrier =
                    m_server.ResolveEnvironmentCarrierGenerator(
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

                m_environmentCarrierSubtype =
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

                if (!UsesMaterialMapValue(
                    Builder,
                    mapValue))
                {
                    throw new ArgumentException(
                        "Material-map value " +
                        mapValue +
                        " is not defined in this template.",
                        "mapValue");
                }


                m_fractalNoiseOperations.Add(
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


            private void ReplaceBiome(
                byte sourceBiome,
                byte targetBiome)
            {
                EnsureEditable();

                if (sourceBiome == targetBiome)
                    return;

                EnsureBiomePlanetMapEnabled();

                m_biomeReplacementOperations.Add(
                    new BiomeReplacementOperation
                    {
                        SourceBiome = sourceBiome,
                        TargetBiome = targetBiome
                    });
            }


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

                m_fractalNoiseOperations.Add(
                    new FractalNoiseOperation
                    {
                        PlaneIndex = 1,
                        TargetValue = biomeValue,
                        CoveragePercent = coveragePercent
                    });
            }


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


            private void Push(
                Action<bool, string> callback)
            {
                EnsureEditable();

                m_server.BeginPushModification(
                    this,
                    callback);
            }


            private void Close()
            {
                m_closed =
                    true;

                m_images.Clear();
                m_imageTransforms.Clear();
                m_fractalNoiseOperations.Clear();
                m_allocatedComplexMaterialValues.Clear();

                if (m_sourceArchiveFiles != null)
                    m_sourceArchiveFiles.Clear();
            }


            private PlanarPngBitmap GetOrLoadImage(
                string faceFileName)
            {
                EnsureEditable();

                faceFileName =
                    ValidatePlanetFaceFileName(
                        faceFileName);

                PlanarPngBitmap image;

                if (m_images.TryGetValue(
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
                        m_server.ReadSourcePlanetDataFile(
                            SourceContext,
                            SourceSubtype,
                            SourceFolderName,
                            faceFileName);
                }
                else
                {
                    if (m_sourceArchiveFiles == null)
                    {
                        m_sourceArchiveFiles =
                            m_server.ReadRuntimePlanetDataArchive(
                                SourceArchiveFile);
                    }

                    if (!m_sourceArchiveFiles.TryGetValue(
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
                    DecodePlanetPng(
                        faceFileName,
                        png);

                m_images.Add(
                    faceFileName,
                    image);

                return image;
            }


            private int m_nextMaterialMapCandidate;


            private void EnsureUsedMaterialMapValues()
            {
                if (m_usedMaterialMapValues != null)
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


                m_usedMaterialMapValues =
                    used;

                m_nextMaterialMapCandidate =
                    0;
            }


            private void EnsureEditable()
            {
                EnsureOpen();

                if (m_pushStarted)
                {
                    throw new Exception(
                        "Modification template is immutable after Push().");
                }
            }


            private void EnsureOpen()
            {
                if (m_closed)
                {
                    throw new Exception(
                        "Planet modification template is closed.");
                }
            }
        }


        private static bool UsesMaterialMapValue(
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            byte mapValue)
        {
            if (builder == null)
                throw new ArgumentNullException("builder");

            if ((builder.DefaultSurfaceMaterial != null &&
                    builder.DefaultSurfaceMaterial.Value == mapValue) ||
                (builder.DefaultSubSurfaceMaterial != null &&
                    builder.DefaultSubSurfaceMaterial.Value == mapValue))
            {
                return true;
            }

            if (builder.CustomMaterialTable != null &&
                builder.CustomMaterialTable.Any(x =>
                    x != null &&
                    x.Value == mapValue))
            {
                return true;
            }

            return builder.ComplexMaterials != null &&
                builder.ComplexMaterials.Any(x =>
                    x != null &&
                    x.Value == mapValue);
        }


        private static string ValidatePlanetFaceFileName(
            string faceFileName)
        {
            if (string.IsNullOrWhiteSpace(
                faceFileName))
            {
                throw new ArgumentException(
                    "Planet PNG filename cannot be empty.",
                    "faceFileName");
            }

            string[] allowed =
            {
                "front.png",
                "back.png",
                "left.png",
                "right.png",
                "up.png",
                "down.png",
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };

            for (int i = 0;
                i < allowed.Length;
                i++)
            {
                if (string.Equals(
                    allowed[i],
                    faceFileName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return allowed[i];
                }
            }

            throw new ArgumentException(
                "Unsupported planet PNG filename: " +
                faceFileName,
                "faceFileName");
        }


        private bool m_requestInProgress;
        private volatile bool m_unloading;
        private VoxelCubemapIntermodApiServer m_intermodApi;

        private readonly object m_persistenceSync =
            new object();

        private readonly HashSet<string> m_worldStorageCacheFiles =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

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

        // MyObjectBuilder_ScriptManager stores utility variables as object-valued
        // XML entries. A byte[] value is written as typed Base64, but Keen's
        // checkpoint XmlReader cannot read binary content. Keep 4 MiB raw chunk
        // boundaries while storing each chunk as an ordinary Base64 string.
        private const int ArchiveChunkSizeBytes =
            4 * 1024 * 1024;

        private const int MaxArchiveChunkCount =
            512;

        private const string GenericRuntimeSubtypePrefix =
            "GrassPlanet_";
        private const string RuntimeGeneratorDataFolderPrefix =
            "PlanetGenerator_";

        private const string GenericGeneratorFileSuffix =
            ".generator.xml";

        // Version 5 moves the terraformed surface recipe out of C# and into
        // Content/Data/grassrules.xml. Every source material-map value receives
        // an overlay backed by the same client/content-authored complex rule
        // group, so latitude/height/slope behavior is controlled entirely by XML.
        //
        // Overlay IDs are reserved away from every red value actually present
        // in the source maps so registering the runtime definition cannot alter
        // unselected terrain.
        private const int GrassOverlayVersion =
            5;

        private RuntimePlanetGeneratorSettings m_settings =
            new RuntimePlanetGeneratorSettings();

        private RuntimePersistenceManifest m_persistenceManifest =
            new RuntimePersistenceManifest();

        private readonly Dictionary<string, MyPlanetGeneratorDefinition>
            m_persistedRuntimeGenerators =
                new Dictionary<string, MyPlanetGeneratorDefinition>(
                    StringComparer.OrdinalIgnoreCase);

        // Absolute save directory currently used to build FolderName.
        // CurrentPath can settle after LoadData(), and Save As can change it
        // again while the session is running.
        private string m_boundSavePath;

        private readonly HashSet<long> m_restoredEnvironmentBindings =
            new HashSet<long>();

        private readonly Random m_bridgeRandom =
            new Random();

        private readonly List<PendingVegetationClear>
            m_pendingVegetationClears =
                new List<PendingVegetationClear>();

        private static readonly int[] VegetationClearPassDelays =
        {
            0,
            10,
            60,
            180
        };

        private int m_environmentRestoreRetryTicks;

        public override void LoadData()
        {
            m_unloading =
                false;

            LoadPersistedRuntimeGenerators();

            m_restoredEnvironmentBindings.Clear();
            m_pendingVegetationClears.Clear();
            m_environmentRestoreRetryTicks =
                0;

            m_intermodApi =
                new VoxelCubemapIntermodApiServer(
                    CreateModificationTemplateApi);

            m_intermodApi.Register();
        }


        public override void BeforeStart()
        {
            try
            {
                ReconcileRuntimePackagesWithLivePlanets();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Startup persistence cleanup failed: " +
                    e);
            }
        }

        protected override void UnloadData()
        {
            m_unloading =
                true;

            m_pendingVegetationClears.Clear();


            if (m_intermodApi != null)
            {
                m_intermodApi.Close();

                m_intermodApi =
                    null;
            }


            ClearWorldStorageCache();
        }


        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session == null)
                return;

            ProcessPendingVegetationClears();

            // Runtime generator state is owned by the background request while
            // it is active.  Rebinding resumes after its simulation-thread
            // completion callback.
            if (m_requestInProgress)
                return;

            if (m_environmentRestoreRetryTicks <= 0)
            {
                try
                {
                    bool complete =
                        RestorePersistedEnvironmentBindings();

                    m_environmentRestoreRetryTicks =
                        complete
                            ? int.MaxValue
                            : 100;
                }
                catch (Exception e)
                {
                    m_environmentRestoreRetryTicks =
                        100;

                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Persisted environment restore failed: " +
                        e);
                }
            }
            else if (m_environmentRestoreRetryTicks != int.MaxValue)
            {
                m_environmentRestoreRetryTicks--;
            }

            string currentPath =
                NormalizePath(
                    MyAPIGateway.Session.CurrentPath);

            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            if (string.Equals(
                currentPath,
                m_boundSavePath,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                RebindRuntimeGeneratorToSavePath(
                    currentPath);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Save-path rebind failed: " + e);
            }
        }


        private ApiData CreateModificationTemplateApi(
            long planetEntityId)
        {
            if (m_unloading)
            {
                throw new Exception(
                    "Voxel Cubemap API server is unloading.");
            }


            MyPlanet targetPlanet =
                planetEntityId == 0
                    ? FindNearestPlanetToPlayer()
                    : FindPlanetByEntityId(
                        planetEntityId);

            if (targetPlanet == null)
            {
                throw new Exception(
                    planetEntityId == 0
                        ? "Could not find a planet near the local player."
                        : "Could not find planet entity " +
                            planetEntityId +
                            ".");
            }

            if (targetPlanet.Generator == null)
            {
                throw new Exception(
                    "Target planet has no generator definition.");
            }


            long planetSeed;
            string currentProviderSubtype;

            ReadLivePlanetProviderIdentity(
                targetPlanet,
                out planetSeed,
                out currentProviderSubtype);


            string sourceSubtype;

            MyPlanetGeneratorDefinition sourceGenerator =
                ResolveOriginalSourceGenerator(
                    targetPlanet,
                    currentProviderSubtype,
                    out sourceSubtype);

            RuntimePlanetBuilderEntry currentRuntimeEntry =
                FindRuntimeEntry(
                    currentProviderSubtype);

            string sourceArchiveFile =
                currentRuntimeEntry == null
                    ? null
                    : currentRuntimeEntry.ArchiveFile;

            MyObjectBuilder_PlanetGeneratorDefinition builder =
                currentRuntimeEntry == null
                    ? CaptureSourceGeneratorBuilder(
                        sourceGenerator)
                    : LoadGeneratorBuilderFromWorldStorage(
                        currentRuntimeEntry.GeneratorFile);

            if (!string.IsNullOrWhiteSpace(
                builder.InheritFrom))
            {
                throw new Exception(
                    "Modification templates do not flatten inherited planet " +
                    "generator definitions yet. Source='" +
                    sourceSubtype +
                    "', InheritFrom='" +
                    builder.InheritFrom +
                    "'.");
            }


            string sourceFolderName =
                currentRuntimeEntry != null
                    ? sourceSubtype
                    :
                string.IsNullOrWhiteSpace(
                    builder.FolderName)
                    ? sourceSubtype
                    : builder.FolderName;


            var template =
                new PlanetModificationTemplate(
                    this,
                    targetPlanet,
                    sourceGenerator.Context,
                    sourceSubtype,
                    sourceFolderName,
                    sourceArchiveFile,
                    currentProviderSubtype,
                    planetSeed,
                    builder,
                    currentRuntimeEntry == null
                        ? null
                        : currentRuntimeEntry.EnvironmentCarrierSubtype);


            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Created modification template " +
                template.TemplateId +
                " for planet " +
                targetPlanet.EntityId +
                ".");


            return template.CreateApi();
        }


        private void BeginPushModification(
            PlanetModificationTemplate template,
            Action<bool, string> callback)
        {
            if (template == null)
                throw new ArgumentNullException("template");

            if (m_requestInProgress)
            {
                DispatchPushResponse(
                    callback,
                    false,
                    "Another planet modification is already running.");

                return;
            }


            PlanetModificationSnapshot snapshot;

            try
            {
                snapshot =
                    template.CreateSnapshot();
            }
            catch (Exception e)
            {
                DispatchPushResponse(
                    callback,
                    false,
                    "Could not snapshot modification template: " +
                    e.Message);

                return;
            }


            m_requestInProgress =
                true;

            PlanetModificationWorkResult workResult =
                null;

            RuntimePlanetBuilderEntry pendingEntry =
                null;

            Exception workError =
                null;


            try
            {
                MyAPIGateway.Parallel.StartBackground(
                    delegate
                    {
                        try
                        {
                            workResult =
                                PrepareModificationPush(
                                    snapshot,
                                    out pendingEntry);
                        }
                        catch (Exception e)
                        {
                            workError =
                                e;
                        }


                        MyAPIGateway.Utilities.InvokeOnGameThread(
                            delegate
                            {
                                CompleteModificationPush(
                                    workResult,
                                    workError,
                                    pendingEntry,
                                    callback);
                            });
                    });
            }
            catch (Exception e)
            {
                m_requestInProgress =
                    false;

                DispatchPushResponse(
                    callback,
                    false,
                    "Could not start modification push: " +
                    e.Message);
            }
        }


        private PlanetModificationWorkResult PrepareModificationPush(
            PlanetModificationSnapshot snapshot,
            out RuntimePlanetBuilderEntry pendingEntry)
        {
            pendingEntry =
                null;

            if (snapshot == null)
                throw new ArgumentNullException("snapshot");


            string runtimeSubtype =
                "PlanetModification_" +
                snapshot.TemplateId;

            string packageStem =
                RuntimeGeneratorDataFolderPrefix +
                runtimeSubtype;

            string archiveFile =
                packageStem +
                ".zip";

            string generatorFile =
                packageStem +
                GenericGeneratorFileSuffix;


            snapshot.Builder.Id =
                new SerializableDefinitionId(
                    typeof(MyObjectBuilder_PlanetGeneratorDefinition),
                    runtimeSubtype);

            snapshot.Builder.FolderName =
                archiveFile;


            pendingEntry =
                new RuntimePlanetBuilderEntry
                {
                    Subtype = runtimeSubtype,
                    SourceSubtype = snapshot.SourceSubtype,
                    SourceEntityId = snapshot.TargetPlanet.EntityId,
                    EnvironmentCarrierSubtype = snapshot.EnvironmentCarrierSubtype,
                    GeneratorFile = generatorFile,
                    ArchiveFile = archiveFile,
                    GrassMaterialValue = 0,
                    GrassCoveragePercent = 0,
                    PlanetSeed = snapshot.PlanetSeed,
                    GrassNoiseVersion = 0
                };

            BeginPendingPersistencePackage(
                pendingEntry);


            CreateModifiedPlanetDataArchive(
                snapshot,
                archiveFile);

            SaveGeneratorBuilder(
                generatorFile,
                snapshot.Builder);


            string absoluteFolder =
                BuildWorldStorageFilePath(
                    ResolveInitialSavePath(),
                    archiveFile);

            MyPlanetGeneratorDefinition runtimeGenerator =
                RegisterRuntimeGeneratorDefinition(
                    snapshot.Builder,
                    runtimeSubtype,
                    absoluteFolder,
                    0,
                    false);


            BindRuntimeEnvironmentCarrier(
                runtimeGenerator,
                snapshot.EnvironmentCarrierSubtype);


            m_persistedRuntimeGenerators[
                runtimeSubtype] =
                runtimeGenerator;


            PlanetModificationWorkResult result =
                PrepareStoredProviderSwap(
                    snapshot.TargetPlanet,
                    runtimeGenerator,
                    snapshot.CurrentProviderSubtype,
                    "API modification");

            result.EnvironmentCarrierSubtype =
                snapshot.EnvironmentCarrierSubtype;

            result.NewEntry =
                pendingEntry;

            return result;
        }


        private void CompleteModificationPush(
            PlanetModificationWorkResult workResult,
            Exception workError,
            RuntimePlanetBuilderEntry pendingEntry,
            Action<bool, string> callback)
        {
            bool commitAttempted =
                false;

            bool storageCommitted =
                false;

            try
            {
                if (m_unloading)
                    return;

                if (workError != null)
                    throw workError;

                if (workResult == null)
                {
                    throw new Exception(
                        "Modification worker returned no result.");
                }


                StageRuntimePackageForCommit(
                    workResult.NewEntry);

                commitAttempted =
                    true;

                CommitPlanetStorage(
                    workResult);

                storageCommitted =
                    true;

                PruneSupersededRuntimePackages(
                    workResult.NewEntry);

                DispatchPushResponse(
                    callback,
                    true,
                    "Planet modification was committed.");
            }
            catch (Exception e)
            {
                bool providerStateResolved =
                    !commitAttempted;

                if (!storageCommitted &&
                    commitAttempted &&
                    workResult != null &&
                    workResult.NewEntry != null)
                {
                    providerStateResolved =
                        TryIsRuntimePackageLive(
                            workResult.TargetPlanet,
                            workResult.NewEntry,
                            out storageCommitted);

                    if (storageCommitted)
                    {
                        try
                        {
                            PruneSupersededRuntimePackages(
                                workResult.NewEntry);
                        }
                        catch (Exception cleanupError)
                        {
                            MyLog.Default.WriteLineAndConsole(
                                "[Voxel Cubemap API] Deferred superseded-package cleanup: " +
                                cleanupError);
                        }
                    }
                }


                if (!m_unloading &&
                    !storageCommitted &&
                    providerStateResolved &&
                    (pendingEntry != null ||
                        (workResult != null &&
                            workResult.NewEntry != null)))
                {
                    try
                    {
                        DiscardRuntimePackage(
                            pendingEntry ??
                            workResult.NewEntry);
                    }
                    catch (Exception cleanupError)
                    {
                        MyLog.Default.WriteLineAndConsole(
                            "[Voxel Cubemap API] Failed package cleanup also failed: " +
                            cleanupError);
                    }
                }

                else if (!storageCommitted &&
                    commitAttempted &&
                    !providerStateResolved)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Retaining staged package because " +
                        "the live provider could not be resolved safely.");
                }


                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Push failed: " +
                    e);

                DispatchPushResponse(
                    callback,
                    false,
                    e.Message);
            }
            finally
            {
                m_requestInProgress =
                    false;
            }
        }


        private static void DispatchPushResponse(
            Action<bool, string> callback,
            bool success,
            string message)
        {
            if (callback == null)
                return;

            try
            {
                callback(
                    success,
                    message);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Push callback failed: " +
                    e);
            }
        }

        private RuntimePlanetBuilderEntry FindRuntimeEntry(
            string runtimeSubtype)
        {
            if (string.IsNullOrWhiteSpace(
                runtimeSubtype) ||
                m_settings == null ||
                m_settings.PlanetBuilders == null)
            {
                return null;
            }


            return m_settings.PlanetBuilders
                .FirstOrDefault(x =>
                    x != null &&
                    x.Subtype != null &&
                    x.Subtype.Equals(
                        runtimeSubtype,
                        StringComparison.OrdinalIgnoreCase));
        }


        private MyPlanetGeneratorDefinition ResolveOriginalSourceGenerator(
            MyPlanet sourcePlanet,
            string currentProviderSubtype,
            out string sourceSubtype)
        {
            RuntimePlanetBuilderEntry runtimeEntry =
                FindRuntimeEntry(
                    currentProviderSubtype);


            if (runtimeEntry != null &&
                !string.IsNullOrWhiteSpace(
                    runtimeEntry.SourceSubtype))
            {
                sourceSubtype =
                    runtimeEntry.SourceSubtype;
            }
            else
            {
                sourceSubtype =
                    currentProviderSubtype;
            }


            if (IsPersistedRuntimeSubtype(
                sourceSubtype))
            {
                throw new Exception(
                    "Could not resolve the original source generator behind '" +
                    currentProviderSubtype +
                    "'.");
            }

            var a = sourceSubtype;
            MyPlanetGeneratorDefinition sourceGenerator =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x.Id.SubtypeName.Equals(
                            a,
                            StringComparison.OrdinalIgnoreCase));


            if (sourceGenerator == null)
            {
                throw new Exception(
                    "Original source generator '" +
                    sourceSubtype +
                    "' is not registered.");
            }


            return sourceGenerator;
        }


        private MyPlanetGeneratorDefinition
            GetOrCreateRuntimeGeneratorForPlanet(
                MyPlanet sourcePlanet,
                MyPlanetGeneratorDefinition sourceGenerator,
                string sourceSubtype,
                long planetSeed,
                int grassCoveragePercent,
                out byte grassMaterialValue)
        {
            MaterialRulesContent grassRules =
                MaterialRulesContent.Load(
                    (MyModContext)ModContext,
                    "Data/grassrules.xml",
                    "TerraformGrassRules",
                    "TerraformGrassSurface");


            string packageId =
                StableFolderId(
                    sourceSubtype +
                    "|" +
                    sourcePlanet.EntityId +
                    "|" +
                    planetSeed +
                    "|" +
                    grassRules.Fingerprint);

            string runtimeSubtype =
                GenericRuntimeSubtypePrefix +
                packageId +
                "_O" +
                GrassOverlayVersion +
                "_P" +
                grassCoveragePercent.ToString(
                    "D3") +
                "_R" +
                grassRules.Fingerprint;

            RuntimePlanetBuilderEntry existingEntry =
                m_settings.PlanetBuilders
                    .FirstOrDefault(x =>
                        x != null &&
                        x.Subtype != null &&
                        x.Subtype.Equals(
                            runtimeSubtype,
                            StringComparison.OrdinalIgnoreCase));


            if (existingEntry != null)
            {
                grassMaterialValue =
                    existingEntry.GrassMaterialValue;

                MyPlanetGeneratorDefinition existingGenerator;

                if (m_persistedRuntimeGenerators.TryGetValue(
                    runtimeSubtype,
                    out existingGenerator))
                {
                    return existingGenerator;
                }


                existingGenerator =
                    LoadAndRegisterPersistedRuntimeGenerator(
                        existingEntry,
                        ResolveInitialSavePath());

                m_persistedRuntimeGenerators[
                    runtimeSubtype] =
                    existingGenerator;

                return existingGenerator;
            }


            MyModContext sourceContext =
                sourceGenerator.Context;

            DumpDefinitionOrigin(
                sourceGenerator);

            MyObjectBuilder_PlanetGeneratorDefinition builder =
                CaptureSourceGeneratorBuilder(
                    sourceGenerator);


            string sourceFolderName =
                string.IsNullOrWhiteSpace(
                    builder.FolderName)
                    ? sourceSubtype
                    : builder.FolderName;


            if (!string.IsNullOrWhiteSpace(
                builder.InheritFrom))
            {
                throw new Exception(
                    "Generic runtime capture does not flatten inherited " +
                    "PlanetGeneratorDefinitions yet. Source '" +
                    sourceSubtype +
                    "' inherits from '" +
                    builder.InheritFrom +
                    "'.");
            }


            bool[] sourceMaterialMapValues =
                CollectSourceMaterialMapValues(
                    sourceContext,
                    sourceSubtype,
                    sourceFolderName);

            int[] grassOverlayValuesBySource;

            grassMaterialValue =
                AppendGrassOverlayMaterials(
                    builder,
                    sourceMaterialMapValues,
                    grassRules.MaterialGroup,
                    out grassOverlayValuesBySource);


            string packageStem =
                RuntimeGeneratorDataFolderPrefix +
                runtimeSubtype;

            string archiveFile =
                packageStem +
                ".zip";

            string generatorFile =
                packageStem +
                GenericGeneratorFileSuffix;


            builder.Id =
                new SerializableDefinitionId(
                    typeof(MyObjectBuilder_PlanetGeneratorDefinition),
                    runtimeSubtype);


            // Portable representation: generator.xml never stores an absolute
            // save path. FolderName is rebound to the current save path when
            // this builder is registered.
            builder.FolderName =
                archiveFile;


            var entry =
                new RuntimePlanetBuilderEntry
                {
                    Subtype =
                        runtimeSubtype,

                    SourceSubtype =
                        sourceSubtype,

                    SourceEntityId =
                        sourcePlanet.EntityId,

                    GeneratorFile =
                        generatorFile,

                    ArchiveFile =
                        archiveFile,

                    GrassMaterialValue =
                        grassMaterialValue,

                    GrassCoveragePercent =
                        grassCoveragePercent,

                    PlanetSeed =
                        planetSeed,

                    GrassNoiseVersion =
                        GrassOverlayVersion
                };


            BeginPendingPersistencePackage(
                entry);


            MyPlanetGeneratorDefinition runtimeGenerator;

            try
            {
                string savePath =
                    ResolveInitialSavePath();


                CreatePlanetDataArchive(
                    sourceContext,
                    sourceSubtype,
                    sourceFolderName,
                    archiveFile,
                    grassMaterialValue,
                    grassOverlayValuesBySource,
                    planetSeed,
                    grassCoveragePercent);


                SaveGeneratorBuilder(
                    generatorFile,
                    builder);


                string absoluteFolder =
                    BuildWorldStorageFilePath(
                        savePath,
                        archiveFile);


                runtimeGenerator =
                    RegisterRuntimeGeneratorDefinition(
                        builder,
                        runtimeSubtype,
                        absoluteFolder,
                        grassMaterialValue);


                m_persistedRuntimeGenerators[
                    runtimeSubtype] =
                    runtimeGenerator;

                StageRuntimePackageForCommit(
                    entry);
            }
            catch
            {
                DiscardRuntimePackage(
                    entry);

                throw;
            }


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Created persistent runtime package: " +
                "Source='" +
                sourceSubtype +
                "', Runtime='" +
                runtimeSubtype +
                "', Archive='" +
                archiveFile +
                "', Generator='" +
                generatorFile +
                "', GrassRed=" +
                grassMaterialValue +
                ", Coverage=" +
                grassCoveragePercent +
                "%, PlanetSeed=" +
                planetSeed);


            return runtimeGenerator;
        }


        private static byte AppendGrassOverlayMaterials(
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            bool[] sourceMaterialMapValues,
            MyPlanetMaterialGroup surfaceRulesTemplate,
            out int[] overlayValueBySource)
        {
            if (builder == null)
                throw new ArgumentNullException("builder");

            if (surfaceRulesTemplate == null)
                throw new ArgumentNullException("surfaceRulesTemplate");

            if (surfaceRulesTemplate.MaterialRules == null ||
                surfaceRulesTemplate.MaterialRules.Length == 0)
            {
                throw new Exception(
                    "Terraform surface rule template contains no rules.");
            }


            overlayValueBySource =
                new int[256];

            for (int i = 0;
                i < overlayValueBySource.Length;
                i++)
            {
                overlayValueBySource[i] =
                    -1;
            }


            bool[] usedValues =
                new bool[256];


            MyPlanetMaterialDefinition[] originalCustom =
                builder.CustomMaterialTable;

            MyPlanetMaterialGroup[] originalGroups =
                builder.ComplexMaterials;


            int customCount =
                originalCustom == null
                    ? 0
                    : originalCustom.Length;

            int groupCount =
                originalGroups == null
                    ? 0
                    : originalGroups.Length;


            // Reserve every byte which exists in the source maps or definition.
            // Selected pixels are redirected to newly allocated bytes only; the
            // source definition remains untouched for every unselected pixel.
            if (sourceMaterialMapValues != null)
            {
                int count =
                    Math.Min(
                        usedValues.Length,
                        sourceMaterialMapValues.Length);

                for (int i = 0;
                    i < count;
                    i++)
                {
                    if (sourceMaterialMapValues[i])
                    {
                        usedValues[i] =
                            true;
                    }
                }
            }


            for (int i = 0;
                i < customCount;
                i++)
            {
                if (originalCustom[i] != null)
                {
                    usedValues[
                        originalCustom[i].Value] =
                        true;
                }
            }


            for (int i = 0;
                i < groupCount;
                i++)
            {
                if (originalGroups[i] != null)
                {
                    usedValues[
                        originalGroups[i].Value] =
                        true;
                }
            }


            if (builder.DefaultSurfaceMaterial != null)
            {
                usedValues[
                    builder.DefaultSurfaceMaterial.Value] =
                    true;
            }


            if (builder.DefaultSubSurfaceMaterial != null)
            {
                usedValues[
                    builder.DefaultSubSurfaceMaterial.Value] =
                    true;
            }


            var groupOutput =
                new List<MyPlanetMaterialGroup>(
                    groupCount + 32);


            for (int i = 0;
                i < groupCount;
                i++)
            {
                groupOutput.Add(
                    originalGroups[i]);
            }


            int nextCandidate =
                0;

            int overlayCount =
                0;

            byte firstOverlayValue =
                0;

            bool haveFirstOverlay =
                false;


            // Apply the same XML-authored complex rule group to every material
            // byte which actually occurs in the source map. This includes both
            // explicit ice/rock/etc. values and bytes which normally fall back
            // to DefaultSurfaceMaterial, so Titan's primary surface and its
            // secondary materials are terraformed through one consistent recipe.
            if (sourceMaterialMapValues != null)
            {
                int count =
                    Math.Min(
                        overlayValueBySource.Length,
                        sourceMaterialMapValues.Length);

                for (int sourceValue = 0;
                    sourceValue < count;
                    sourceValue++)
                {
                    if (!sourceMaterialMapValues[sourceValue])
                        continue;


                    byte overlayValue =
                        AllocateGrassOverlayValue(
                            usedValues,
                            ref nextCandidate);


                    MyPlanetMaterialGroup overlayGroup =
                        CloneSurfaceRuleOverlayGroup(
                            surfaceRulesTemplate,
                            overlayValue,
                            (byte)sourceValue);


                    groupOutput.Add(
                        overlayGroup);

                    overlayValueBySource[sourceValue] =
                        overlayValue;

                    overlayCount++;


                    if (!haveFirstOverlay)
                    {
                        firstOverlayValue =
                            overlayValue;

                        haveFirstOverlay =
                            true;
                    }
                }
            }


            if (!haveFirstOverlay)
            {
                throw new Exception(
                    "Source material maps contain no red-channel values to terraform.");
            }


            builder.ComplexMaterials =
                groupOutput.ToArray();


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Added XML surface-rule overlays: " +
                "source red values=" +
                overlayCount +
                ", rules per overlay=" +
                surfaceRulesTemplate.MaterialRules.Length +
                ", first overlay red=" +
                firstOverlayValue +
                ". Original source definitions were preserved for unselected pixels.");


            return firstOverlayValue;
        }


        private static MyPlanetMaterialGroup CloneSurfaceRuleOverlayGroup(
            MyPlanetMaterialGroup source,
            byte overlayValue,
            byte sourceValue)
        {
            if (source == null)
                throw new ArgumentNullException("source");


            MyPlanetMaterialGroup output =
                (MyPlanetMaterialGroup)
                    source.Clone();


            output.Value =
                overlayValue;

            output.Name =
                "TerraformSurfaceOverlay_" +
                sourceValue;


            return output;
        }


        private static byte AllocateGrassOverlayValue(
            bool[] usedValues,
            ref int nextCandidate)
        {
            // 255 is commonly useful as a sentinel in byte maps; leave it alone.
            while (nextCandidate < 255 &&
                usedValues[nextCandidate])
            {
                nextCandidate++;
            }


            if (nextCandidate >= 255)
            {
                throw new Exception(
                    "No free material-map byte remains for Grass overlay values.");
            }


            byte value =
                (byte)nextCandidate;

            usedValues[nextCandidate] =
                true;

            nextCandidate++;

            return value;
        }


        private static PlanarPngBitmap DecodePlanetPng(
            string fileName,
            byte[] png)
        {
            if (png == null)
            {
                throw new Exception(
                    "Invalid planet PNG: " +
                    fileName);
            }


            try
            {
                PlanarPngBitmap image = PlanarPngBitmap.Load(png);

                if (image.SourceInterlaceMethod != 0)
                {
                    throw new Exception(
                        "Interlaced planet PNGs are not supported.");
                }

                return image;
            }
            catch (Exception error)
            {
                throw new Exception(
                    "Could not decode planet PNG " +
                    fileName +
                    ": " +
                    error.Message,
                    error);
            }
        }


        private static byte[] RewritePngMaterialChannel(
            byte[] png,
            byte materialValue)
        {
            return RewritePngMaterialChannel(
                png,
                materialValue,
                null,
                null,
                100,
                0,
                double.NegativeInfinity,
                null);
        }


        private static byte[] RewritePngMaterialChannel(
            byte[] png,
            byte materialValue,
            int[] overlayValueBySource,
            string faceFileName,
            int grassCoveragePercent,
            long planetSeed,
            double grassThreshold,
            bool[] observedMaterialValues)
        {
            PlanarPngBitmap image =
                DecodePlanetPng(
                    faceFileName,
                    png);

            if (image.BitDepth != 8 ||
                (image.ColorType != 2 &&
                 image.ColorType != 6))
            {
                throw new Exception(
                    "Expected non-interlaced RGB8/RGBA8 source material PNG. " +
                    "Width=" +
                    image.Width +
                    ", Height=" +
                    image.Height +
                    ", BitDepth=" +
                    image.BitDepth +
                    ", ColorType=" +
                    image.ColorType +
                    ", Interlace=" +
                    image.SourceInterlaceMethod);
            }


            byte[] materials =
                image.Planes[0];


            if (observedMaterialValues != null)
            {
                for (int pixel = 0;
                    pixel < materials.Length;
                    pixel++)
                {
                    observedMaterialValues[
                        materials[pixel]] =
                        true;
                }


                if (grassCoveragePercent <= 0 &&
                    overlayValueBySource == null)
                {
                    // Scan-only call. Decoding was required to inspect the red
                    // channel, but the source PNG itself remains unchanged.
                    return png;
                }
            }


            int faceIndex =
                -1;

            double[] grassNoiseGrid =
                null;

            if (grassCoveragePercent > 0 &&
                grassCoveragePercent < 100)
            {
                faceIndex =
                    GetCubemapFaceIndex(
                        faceFileName);

                grassNoiseGrid =
                    BuildGrassNoiseGrid(
                        faceIndex,
                        planetSeed);
            }


            int pixelOffset =
                0;


            for (int y = 0;
                y < image.Height;
                y++)
            {
                for (int x = 0;
                    x < image.Width;
                    x++)
                {
                    bool makeGrass;


                    if (grassCoveragePercent <= 0)
                    {
                        makeGrass =
                            false;
                    }
                    else if (grassCoveragePercent >= 100)
                    {
                        makeGrass =
                            true;
                    }
                    else
                    {
                        double growthScore =
                            SampleGrassNoiseGrid(
                                grassNoiseGrid,
                                x,
                                y,
                                image.Width,
                                image.Height);

                        makeGrass =
                            growthScore >= grassThreshold;
                    }


                    if (makeGrass)
                    {
                        // Red = material map.
                        // Green = biome/environment map.
                        // Blue = ore map.
                        //
                        // Every source red value observed in the material maps
                        // is redirected to its XML-authored complex-rule overlay.
                        // materialValue is only a defensive fallback for an
                        // unexpected byte which was not observed during the scan.
                        if (overlayValueBySource != null)
                        {
                            int sourceValue =
                                materials[pixelOffset];

                            int overlayValue =
                                overlayValueBySource[sourceValue];


                            materials[pixelOffset] =
                                overlayValue >= 0
                                    ? (byte)overlayValue
                                    : materialValue;
                        }
                        else
                        {
                            // Legacy full-replacement path only.
                            materials[pixelOffset] =
                                materialValue;
                        }
                    }


                    pixelOffset++;
                }
            }


            return image.Encode();
        }

        private static int GetCubemapFaceIndex(
            string faceFileName)
        {
            if (string.IsNullOrWhiteSpace(
                faceFileName))
            {
                throw new Exception(
                    "Cubemap face name is required for partial grass coverage.");
            }


            if (faceFileName.StartsWith(
                "front",
                StringComparison.OrdinalIgnoreCase))
                return 0;

            if (faceFileName.StartsWith(
                "back",
                StringComparison.OrdinalIgnoreCase))
                return 1;

            if (faceFileName.StartsWith(
                "left",
                StringComparison.OrdinalIgnoreCase))
                return 2;

            if (faceFileName.StartsWith(
                "right",
                StringComparison.OrdinalIgnoreCase))
                return 3;

            if (faceFileName.StartsWith(
                "up",
                StringComparison.OrdinalIgnoreCase))
                return 4;

            if (faceFileName.StartsWith(
                "down",
                StringComparison.OrdinalIgnoreCase))
                return 5;


            throw new Exception(
                "Unknown cubemap face: " +
                faceFileName);
        }


        private static Vector3D GetCubemapSphereDirection(
            int faceIndex,
            int x,
            int y,
            int width,
            int height)
        {
            double u =
                width <= 1
                    ? 0.0
                    : (2.0 * x /
                        (width - 1.0)) -
                        1.0;

            double v =
                height <= 1
                    ? 0.0
                    : (2.0 * y /
                        (height - 1.0)) -
                        1.0;


            Vector3D direction;


            // Orientation recovered from the actual Space Engineers planet-map
            // edge relationships:
            //
            // front L == left R
            // front R == right L
            // front T == up B
            // front B == reversed down B
            // back  L == right R
            // back  R == left L
            //
            // Sampling one continuous 3D field with these vectors makes shared
            // face edges and all cube corners evaluate identically.
            switch (faceIndex)
            {
                case 0:
                    direction =
                        new Vector3D(
                            u,
                            -v,
                            1.0);
                    break;

                case 1:
                    direction =
                        new Vector3D(
                            -u,
                            -v,
                            -1.0);
                    break;

                case 2:
                    direction =
                        new Vector3D(
                            -1.0,
                            -v,
                            u);
                    break;

                case 3:
                    direction =
                        new Vector3D(
                            1.0,
                            -v,
                            -u);
                    break;

                case 4:
                    direction =
                        new Vector3D(
                            u,
                            1.0,
                            v);
                    break;

                case 5:
                    direction =
                        new Vector3D(
                            -u,
                            -1.0,
                            v);
                    break;

                default:
                    throw new Exception(
                        "Invalid cubemap face index: " +
                        faceIndex);
            }


            double lengthSquared =
                direction.X * direction.X +
                direction.Y * direction.Y +
                direction.Z * direction.Z;

            double inverseLength =
                1.0 /
                Math.Sqrt(
                    lengthSquared);


            return new Vector3D(
                direction.X * inverseLength,
                direction.Y * inverseLength,
                direction.Z * inverseLength);
        }


        private static double[] BuildGrassNoiseGrid(
            int faceIndex,
            long planetSeed)
        {
            const int GridResolution =
                129;

            double[] grid =
                new double[
                    GridResolution *
                    GridResolution];

            int offset =
                0;


            for (int y = 0;
                y < GridResolution;
                y++)
            {
                for (int x = 0;
                    x < GridResolution;
                    x++)
                {
                    Vector3D direction =
                        GetCubemapSphereDirection(
                            faceIndex,
                            x,
                            y,
                            GridResolution,
                            GridResolution);

                    grid[offset++] =
                        PlanetGrassFbm(
                            direction,
                            planetSeed);
                }
            }


            return grid;
        }


        private static double SampleGrassNoiseGrid(
            double[] grid,
            int x,
            int y,
            int width,
            int height)
        {
            const int GridResolution =
                129;

            const int GridMaximum =
                GridResolution - 1;


            double gridX =
                width <= 1
                    ? 0.0
                    : (double)x *
                        GridMaximum /
                        (width - 1.0);

            double gridY =
                height <= 1
                    ? 0.0
                    : (double)y *
                        GridMaximum /
                        (height - 1.0);


            int x0 =
                (int)gridX;

            int y0 =
                (int)gridY;

            int x1 =
                x0 < GridMaximum
                    ? x0 + 1
                    : x0;

            int y1 =
                y0 < GridMaximum
                    ? y0 + 1
                    : y0;


            double tx =
                gridX - x0;

            double ty =
                gridY - y0;


            double top =
                LerpNoise(
                    grid[
                        y0 *
                        GridResolution +
                        x0],
                    grid[
                        y0 *
                        GridResolution +
                        x1],
                    tx);

            double bottom =
                LerpNoise(
                    grid[
                        y1 *
                        GridResolution +
                        x0],
                    grid[
                        y1 *
                        GridResolution +
                        x1],
                    tx);


            return LerpNoise(
                top,
                bottom,
                ty);
        }


        private static double ComputeGrassCoverageThreshold(
            long planetSeed,
            int grassCoveragePercent)
        {
            if (grassCoveragePercent <= 0)
                return double.PositiveInfinity;

            if (grassCoveragePercent >= 100)
                return double.NegativeInfinity;


            const int SampleResolution =
                129;

            int sampleCount =
                6 *
                SampleResolution *
                SampleResolution;

            double[] samples =
                new double[
                    sampleCount];

            int sampleIndex =
                0;


            for (int face = 0;
                face < 6;
                face++)
            {
                for (int y = 0;
                    y < SampleResolution;
                    y++)
                {
                    for (int x = 0;
                        x < SampleResolution;
                        x++)
                    {
                        Vector3D direction =
                            GetCubemapSphereDirection(
                                face,
                                x,
                                y,
                                SampleResolution,
                                SampleResolution);

                        samples[sampleIndex++] =
                            PlanetGrassFbm(
                                direction,
                                planetSeed);
                    }
                }
            }


            Array.Sort(
                samples);


            int grassSampleCount =
                (sampleCount *
                    grassCoveragePercent +
                    99) /
                100;

            int thresholdIndex =
                sampleCount -
                grassSampleCount;


            if (thresholdIndex < 0)
                thresholdIndex = 0;

            if (thresholdIndex >= sampleCount)
                thresholdIndex = sampleCount - 1;


            return samples[
                thresholdIndex];
        }


        private static double PlanetGrassFbm(
            Vector3D direction,
            long planetSeed)
        {
            double frequency =
                2.15;

            double amplitude =
                1.0;

            double sum =
                0.0;

            double amplitudeSum =
                0.0;


            for (int octave = 0;
                octave < 4;
                octave++)
            {
                long octaveSeed =
                    unchecked(
                        planetSeed +
                        octave * 104729L);

                sum +=
                    ValueNoise3D(
                        direction.X * frequency,
                        direction.Y * frequency,
                        direction.Z * frequency,
                        octaveSeed) *
                    amplitude;

                amplitudeSum +=
                    amplitude;

                frequency *=
                    2.07;

                amplitude *=
                    0.5;
            }


            return sum /
                amplitudeSum;
        }


        private static double ValueNoise3D(
            double x,
            double y,
            double z,
            long seed)
        {
            int x0 =
                FastFloor(
                    x);

            int y0 =
                FastFloor(
                    y);

            int z0 =
                FastFloor(
                    z);

            int x1 =
                x0 + 1;

            int y1 =
                y0 + 1;

            int z1 =
                z0 + 1;


            double tx =
                SmoothNoiseFraction(
                    x - x0);

            double ty =
                SmoothNoiseFraction(
                    y - y0);

            double tz =
                SmoothNoiseFraction(
                    z - z0);


            double n000 =
                LatticeNoiseValue(
                    x0,
                    y0,
                    z0,
                    seed);

            double n100 =
                LatticeNoiseValue(
                    x1,
                    y0,
                    z0,
                    seed);

            double n010 =
                LatticeNoiseValue(
                    x0,
                    y1,
                    z0,
                    seed);

            double n110 =
                LatticeNoiseValue(
                    x1,
                    y1,
                    z0,
                    seed);

            double n001 =
                LatticeNoiseValue(
                    x0,
                    y0,
                    z1,
                    seed);

            double n101 =
                LatticeNoiseValue(
                    x1,
                    y0,
                    z1,
                    seed);

            double n011 =
                LatticeNoiseValue(
                    x0,
                    y1,
                    z1,
                    seed);

            double n111 =
                LatticeNoiseValue(
                    x1,
                    y1,
                    z1,
                    seed);


            double nx00 =
                LerpNoise(
                    n000,
                    n100,
                    tx);

            double nx10 =
                LerpNoise(
                    n010,
                    n110,
                    tx);

            double nx01 =
                LerpNoise(
                    n001,
                    n101,
                    tx);

            double nx11 =
                LerpNoise(
                    n011,
                    n111,
                    tx);

            double nxy0 =
                LerpNoise(
                    nx00,
                    nx10,
                    ty);

            double nxy1 =
                LerpNoise(
                    nx01,
                    nx11,
                    ty);


            return LerpNoise(
                nxy0,
                nxy1,
                tz);
        }


        private static int FastFloor(
            double value)
        {
            int integer =
                (int)value;

            return value < integer
                ? integer - 1
                : integer;
        }


        private static double SmoothNoiseFraction(
            double value)
        {
            // Quintic fade: continuous first and second derivatives.
            return value *
                value *
                value *
                (value *
                    (value * 6.0 - 15.0) +
                    10.0);
        }


        private static double LerpNoise(
            double a,
            double b,
            double amount)
        {
            return a +
                (b - a) *
                amount;
        }


        private static double LatticeNoiseValue(
            int x,
            int y,
            int z,
            long seed)
        {
            uint hash =
                unchecked(
                    (uint)seed ^
                    (uint)(seed >> 32));


            unchecked
            {
                hash ^=
                    (uint)x *
                    0x9E3779B9u;

                hash ^=
                    (uint)y *
                    0x85EBCA6Bu;

                hash ^=
                    (uint)z *
                    0xC2B2AE35u;

                hash ^=
                    hash >> 16;

                hash *=
                    0x7FEB352Du;

                hash ^=
                    hash >> 15;

                hash *=
                    0x846CA68Bu;

                hash ^=
                    hash >> 16;
            }


            return
                ((double)hash /
                    4294967295.0) *
                2.0 -
                1.0;
        }


        private static byte[] ReadAllBytes(
            BinaryReader reader)
        {
            byte[] output =
                new byte[64 * 1024];

            int length = 0;


            while (true)
            {
                if (length == output.Length)
                {
                    byte[] grown =
                        new byte[
                            output.Length * 2];

                    Buffer.BlockCopy(
                        output,
                        0,
                        grown,
                        0,
                        output.Length);

                    output =
                        grown;
                }


                int read =
                    reader.Read(
                        output,
                        length,
                        output.Length - length);


                if (read <= 0)
                    break;


                length +=
                    read;
            }


            if (length == output.Length)
                return output;


            byte[] exact =
                new byte[length];


            if (length > 0)
            {
                Buffer.BlockCopy(
                    output,
                    0,
                    exact,
                    0,
                    length);
            }


            return exact;
        }


        private static string StableFolderId(
            string value)
        {
            // FNV-1a 32-bit. Stable across process runs unlike string.GetHashCode().
            uint hash =
                2166136261u;


            for (int i = 0;
                i < value.Length;
                i++)
            {
                hash ^=
                    value[i];

                hash *=
                    16777619u;
            }


            return hash.ToString(
                "X8");
        }


        private void LoadPersistedRuntimeGenerators()
        {
            bool migrateLegacyWorldStorage;

            m_settings =
                LoadRuntimeSettings(
                    out migrateLegacyWorldStorage);


            if (m_settings.PlanetBuilders == null)
            {
                m_settings.PlanetBuilders =
                    new List<RuntimePlanetBuilderEntry>();
            }


            for (int i = 0;
                i < m_settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry entry =
                    m_settings.PlanetBuilders[i];


                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.Subtype) ||
                    string.IsNullOrWhiteSpace(entry.GeneratorFile) ||
                    string.IsNullOrWhiteSpace(entry.ArchiveFile))
                {
                    throw new Exception(
                        "Persisted settings contain an invalid runtime planet entry.");
                }
            }


            m_persistenceManifest =
                LoadPersistenceManifest();

            CleanupAbandonedPersistencePackages();
            SeedPersistenceManifestFromSettings();


            if (m_settings.PlanetBuilders.Count == 0)
                return;


            RecreateWorldStorageCache(
                migrateLegacyWorldStorage);


            string savePath =
                ResolveInitialSavePath();


            for (int i = 0;
                i < m_settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry entry =
                    m_settings.PlanetBuilders[i];


                MyPlanetGeneratorDefinition generator =
                    LoadAndRegisterPersistedRuntimeGenerator(
                        entry,
                        savePath);


                m_persistedRuntimeGenerators[
                    entry.Subtype] =
                    generator;
            }


            m_boundSavePath =
                savePath;


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Loaded persisted runtime generators: " +
                m_persistedRuntimeGenerators.Count);
        }


        private RuntimePlanetGeneratorSettings LoadRuntimeSettings(
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


        private void SaveRuntimeSettings()
        {
            lock (m_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();


                string xml =
                    MyAPIGateway.Utilities
                        .SerializeToXML<RuntimePlanetGeneratorSettings>(
                            m_settings);


                MyAPIGateway.Utilities.SetVariable(
                    RuntimeSettingsVariable,
                    xml);

                WriteWorldStorageTextCache(
                    RuntimeSettingsFile,
                    xml);
            }
        }


        private void SaveGeneratorBuilder(
            string fileName,
            MyObjectBuilder_PlanetGeneratorDefinition builder)
        {
            lock (m_persistenceSync)
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


        private void RecreateWorldStorageCache(
            bool allowLegacyMigration)
        {
            lock (m_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();


                WriteWorldStorageTextCache(
                    RuntimeSettingsFile,
                    MyAPIGateway.Utilities
                        .SerializeToXML<RuntimePlanetGeneratorSettings>(
                            m_settings));


                for (int i = 0;
                    i < m_settings.PlanetBuilders.Count;
                    i++)
                {
                    RuntimePlanetBuilderEntry entry =
                        m_settings.PlanetBuilders[i];

                    RestoreGeneratorCache(
                        entry.GeneratorFile,
                        allowLegacyMigration);

                    RestoreArchiveCache(
                        entry.ArchiveFile,
                        allowLegacyMigration);
                }
            }
        }


        private void RestoreGeneratorCache(
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


        private void RestoreArchiveCache(
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
                        ReadAllBytes(
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


        private void SaveRuntimeArchive(
            string fileName,
            byte[] archive)
        {
            lock (m_persistenceSync)
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


        private void SaveRuntimeArchiveVariables(
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


        private bool TryLoadRuntimeArchiveVariables(
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


        private static string BuildGeneratorVariableName(
            string fileName)
        {
            return
                PersistenceVariablePrefix +
                "GeneratorXml." +
                fileName;
        }


        private static string BuildArchiveChunkCountVariableName(
            string fileName)
        {
            return
                PersistenceVariablePrefix +
                "Archive." +
                fileName +
                ".ChunkCount";
        }


        private static string BuildArchiveLengthVariableName(
            string fileName)
        {
            return
                PersistenceVariablePrefix +
                "Archive." +
                fileName +
                ".Length";
        }


        private static string BuildArchiveChunkVariableName(
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


        private RuntimePersistenceManifest LoadPersistenceManifest()
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


        private void SavePersistenceManifest()
        {
            string xml =
                MyAPIGateway.Utilities
                    .SerializeToXML<RuntimePersistenceManifest>(
                        m_persistenceManifest);

            MyAPIGateway.Utilities.SetVariable(
                PersistenceManifestVariable,
                xml);
        }


        private RuntimePersistencePackageEntry
            FindPersistenceManifestPackage(
                string archiveFile)
        {
            if (m_persistenceManifest == null ||
                m_persistenceManifest.Packages == null ||
                string.IsNullOrWhiteSpace(archiveFile))
            {
                return null;
            }


            return m_persistenceManifest.Packages
                .FirstOrDefault(x =>
                    x != null &&
                    string.Equals(
                        x.ArchiveFile,
                        archiveFile,
                        StringComparison.OrdinalIgnoreCase));
        }


        private static bool PersistencePackageMatchesEntry(
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


        private RuntimePersistencePackageEntry
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


        private void BeginPendingPersistencePackage(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");


            lock (m_persistenceSync)
            {
                ThrowIfPersistenceUnavailable();


                RuntimePersistencePackageEntry package =
                    FindPersistenceManifestPackage(
                        entry.ArchiveFile);

                if (package == null)
                {
                    package =
                        new RuntimePersistencePackageEntry();

                    m_persistenceManifest.Packages.Add(
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


        private void CleanupAbandonedPersistencePackages()
        {
            lock (m_persistenceSync)
            {
                bool settingsChanged =
                    false;

                bool manifestChanged =
                    false;


                for (int packageIndex =
                        m_persistenceManifest.Packages.Count - 1;
                    packageIndex >= 0;
                    packageIndex--)
                {
                    RuntimePersistencePackageEntry package =
                        m_persistenceManifest.Packages[packageIndex];

                    RuntimePlanetBuilderEntry referencedEntry =
                        package == null
                            ? null
                            : m_settings.PlanetBuilders
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
                        m_settings.PlanetBuilders.RemoveAll(x =>
                            PersistencePackageMatchesEntry(
                                package,
                                x)) > 0)
                    {
                        settingsChanged =
                            true;
                    }


                    m_persistenceManifest.Packages.RemoveAt(
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


        private void SeedPersistenceManifestFromSettings()
        {
            lock (m_persistenceSync)
            {
                bool changed =
                    false;


                for (int entryIndex = 0;
                    entryIndex < m_settings.PlanetBuilders.Count;
                    entryIndex++)
                {
                    RuntimePlanetBuilderEntry entry =
                        m_settings.PlanetBuilders[entryIndex];

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

                    m_persistenceManifest.Packages.Add(
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


        private static void ValidateArchiveChunkCount(
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


        private static void RemoveRuntimeArchiveVariableRange(
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


        private void RemovePersistencePackageArtifacts(
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

            m_worldStorageCacheFiles.Remove(
                package.GeneratorFile);

            m_worldStorageCacheFiles.Remove(
                package.ArchiveFile);


            if (!string.IsNullOrWhiteSpace(
                package.Subtype))
            {
                m_persistedRuntimeGenerators.Remove(
                    package.Subtype);
            }
        }


        private void StageRuntimePackageForCommit(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");


            lock (m_persistenceSync)
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


                if (!m_settings.PlanetBuilders.Any(x =>
                    x != null &&
                    string.Equals(
                        x.Subtype,
                        entry.Subtype,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    m_settings.PlanetBuilders.Add(
                        entry);
                }


                SaveRuntimeSettings();
                SavePersistenceManifest();
            }
        }


        private void PruneSupersededRuntimePackages(
            RuntimePlanetBuilderEntry retainedEntry)
        {
            if (retainedEntry == null)
                throw new ArgumentNullException("retainedEntry");


            lock (m_persistenceSync)
            {
                var supersededEntries =
                    m_settings.PlanetBuilders
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
                        m_persistenceManifest.Packages.Remove(
                            package);
                    }


                    m_settings.PlanetBuilders.Remove(
                        supersededEntry);

                    m_persistedRuntimeGenerators.Remove(
                        supersededEntry.Subtype);
                }


                SaveRuntimeSettings();
                SavePersistenceManifest();
            }
        }


        private void DiscardRuntimePackage(
            RuntimePlanetBuilderEntry entry)
        {
            if (entry == null)
                return;


            lock (m_persistenceSync)
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
                    m_persistenceManifest.Packages.Remove(
                        package);
                }


                bool settingsChanged =
                    m_settings.PlanetBuilders.RemoveAll(x =>
                        x != null &&
                        string.Equals(
                            x.Subtype,
                            entry.Subtype,
                            StringComparison.OrdinalIgnoreCase)) > 0;

                m_persistedRuntimeGenerators.Remove(
                    entry.Subtype);


                if (settingsChanged)
                    SaveRuntimeSettings();

                SavePersistenceManifest();
            }
        }


        private void ReconcileRuntimePackagesWithLivePlanets()
        {
            List<RuntimePlanetBuilderEntry> entries;

            lock (m_persistenceSync)
            {
                entries =
                    m_settings.PlanetBuilders
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
                    FindPlanetByEntityId(
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
                    ReadLivePlanetProviderIdentity(
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


            lock (m_persistenceSync)
            {
                int removedCount =
                    0;


                for (int entryIndex = 0;
                    entryIndex < staleEntries.Count;
                    entryIndex++)
                {
                    RuntimePlanetBuilderEntry staleEntry =
                        staleEntries[entryIndex];

                    if (!m_settings.PlanetBuilders.Contains(
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
                        m_persistenceManifest.Packages.Remove(
                            package);
                    }


                    m_settings.PlanetBuilders.Remove(
                        staleEntry);

                    m_persistedRuntimeGenerators.Remove(
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


        private bool TryIsRuntimePackageLive(
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

                ReadLivePlanetProviderIdentity(
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


        private void WriteWorldStorageTextCache(
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


            m_worldStorageCacheFiles.Add(
                fileName);
        }


        private void WriteWorldStorageBinaryCache(
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


            m_worldStorageCacheFiles.Add(
                fileName);
        }


        private void ClearWorldStorageCache()
        {
            lock (m_persistenceSync)
            {
                foreach (string fileName in
                    m_worldStorageCacheFiles)
                {
                    TryDeleteWorldStorageCacheFile(
                        fileName);
                }


                m_worldStorageCacheFiles.Clear();

                TryDeleteWorldStorageCacheFile(
                    RuntimeSettingsFile);


                if (m_settings == null ||
                    m_settings.PlanetBuilders == null)
                {
                    return;
                }


                for (int i = 0;
                    i < m_settings.PlanetBuilders.Count;
                    i++)
                {
                    RuntimePlanetBuilderEntry entry =
                        m_settings.PlanetBuilders[i];

                    if (entry == null)
                        continue;


                    TryDeleteWorldStorageCacheFile(
                        entry.GeneratorFile);

                    TryDeleteWorldStorageCacheFile(
                        entry.ArchiveFile);
                }
            }
        }


        private void ThrowIfPersistenceUnavailable()
        {
            if (m_unloading)
            {
                throw new Exception(
                    "Runtime planet persistence is unavailable while the " +
                    "session is unloading.");
            }
        }


        private static void TryDeleteWorldStorageCacheFile(
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


        private MyObjectBuilder_PlanetGeneratorDefinition
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


        private static void EnsureBiomePlanetMapEnabled(
            MyObjectBuilder_PlanetGeneratorDefinition builder)
        {
            if (builder == null)
                throw new ArgumentNullException("builder");

            var planetMaps =
                builder.PlanetMaps.GetValueOrDefault();

            if (planetMaps.Biome)
                return;

            planetMaps.Biome =
                true;

            builder.PlanetMaps =
                planetMaps;
        }


        private MyPlanetGeneratorDefinition
            LoadAndRegisterPersistedRuntimeGenerator(
                RuntimePlanetBuilderEntry entry,
                string savePath)
        {
            MyObjectBuilder_PlanetGeneratorDefinition builder =
                LoadGeneratorBuilderFromWorldStorage(
                    entry.GeneratorFile);


            string absoluteFolder =
                BuildWorldStorageFilePath(
                    savePath,
                    entry.ArchiveFile);


            if (!string.IsNullOrWhiteSpace(
                entry.EnvironmentCarrierSubtype))
            {
                EnsureBiomePlanetMapEnabled(
                    builder);
            }


            MyPlanetGeneratorDefinition runtimeGenerator =
                RegisterRuntimeGeneratorDefinition(
                    builder,
                    entry.Subtype,
                    absoluteFolder,
                    entry.GrassMaterialValue,
                    entry.GrassNoiseVersion > 0);


            return BindRuntimeEnvironmentCarrier(
                runtimeGenerator,
                entry.EnvironmentCarrierSubtype);
        }


        private bool RestorePersistedEnvironmentBindings()
        {
            if (m_settings == null ||
                m_settings.PlanetBuilders == null ||
                m_settings.PlanetBuilders.Count == 0)
            {
                return true;
            }


            bool complete =
                true;

            var candidatePlanetIds =
                new HashSet<long>();


            for (int i = 0;
                i < m_settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry candidate =
                    m_settings.PlanetBuilders[i];

                if (candidate == null ||
                    candidate.SourceEntityId == 0 ||
                    string.IsNullOrWhiteSpace(
                        candidate.EnvironmentCarrierSubtype))
                {
                    continue;
                }

                candidatePlanetIds.Add(
                    candidate.SourceEntityId);
            }


            foreach (long planetEntityId in candidatePlanetIds)
            {
                if (m_restoredEnvironmentBindings.Contains(
                    planetEntityId))
                {
                    continue;
                }


                MyPlanet planet =
                    FindPlanetByEntityId(
                        planetEntityId);

                if (planet == null ||
                    planet.Storage == null ||
                    !planet.InScene)
                {
                    complete =
                        false;

                    continue;
                }


                long ignoredProviderSeed;
                string providerSubtype;

                ReadLivePlanetProviderIdentity(
                    planet,
                    out ignoredProviderSeed,
                    out providerSubtype);


                RuntimePlanetBuilderEntry currentEntry =
                    m_settings.PlanetBuilders
                        .LastOrDefault(x =>
                            x != null &&
                            x.SourceEntityId == planetEntityId &&
                            !string.IsNullOrWhiteSpace(x.Subtype) &&
                            string.Equals(
                                x.Subtype,
                                providerSubtype,
                                StringComparison.OrdinalIgnoreCase));

                if (currentEntry == null ||
                    string.IsNullOrWhiteSpace(
                        currentEntry.EnvironmentCarrierSubtype))
                {
                    m_restoredEnvironmentBindings.Add(
                        planetEntityId);

                    continue;
                }


                try
                {
                    RestorePlanetEnvironmentFromCarrier(
                        planet,
                        currentEntry.EnvironmentCarrierSubtype,
                        providerSubtype);
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Could not restore caller environment " +
                        "for planet " +
                        planetEntityId +
                        ": " +
                        e.Message);
                }

                // A present planet is handled once per load. Missing definitions
                // are configuration errors and should not cause endless retries.
                m_restoredEnvironmentBindings.Add(
                    planetEntityId);
            }


            return complete;
        }


        private MyPlanetGeneratorDefinition ResolveEnvironmentCarrierGenerator(
            string environmentCarrierSubtype)
        {
            if (string.IsNullOrWhiteSpace(
                environmentCarrierSubtype))
            {
                throw new ArgumentException(
                    "Environment carrier subtype cannot be empty.",
                    "environmentCarrierSubtype");
            }


            MyPlanetGeneratorDefinition carrier =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x != null &&
                        string.Equals(
                            x.Id.SubtypeName,
                            environmentCarrierSubtype,
                            StringComparison.OrdinalIgnoreCase));

            if (carrier == null)
            {
                throw new Exception(
                    "Environment carrier planet generator '" +
                    environmentCarrierSubtype +
                    "' is not registered.");
            }

            if (!carrier.EnvironmentId.HasValue)
            {
                throw new Exception(
                    "Environment carrier planet generator '" +
                    environmentCarrierSubtype +
                    "' has no explicit WorldEnvironmentDefinition.");
            }

            return carrier;
        }


        private static bool TryGetPlanetComponentByInstanceTypeName(
            MyPlanet planet,
            string instanceTypeFullName,
            out Type componentType,
            out MyComponentBase component,
            out MyEntityComponentBase entityComponent)
        {
            componentType =
                null;

            component =
                null;

            entityComponent =
                null;


            if (planet == null ||
                string.IsNullOrWhiteSpace(instanceTypeFullName))
            {
                return false;
            }


            foreach (Type candidateType in
                planet.Components.GetComponentTypes())
            {
                if (candidateType == null)
                    continue;


                MyComponentBase candidate;

                if (!planet.Components.TryGet(
                    candidateType,
                    out candidate) ||
                    candidate == null)
                {
                    continue;
                }


                Type instanceType =
                    candidate.GetType();

                if (instanceType == null ||
                    !string.Equals(
                        instanceType.FullName,
                        instanceTypeFullName,
                        StringComparison.Ordinal))
                {
                    continue;
                }


                componentType =
                    candidateType;

                component =
                    candidate;

                entityComponent =
                    candidate as MyEntityComponentBase;

                return true;
            }


            return false;
        }


        private MyPlanetGeneratorDefinition BindRuntimeEnvironmentCarrier(
            MyPlanetGeneratorDefinition runtimeGenerator,
            string environmentCarrierSubtype)
        {
            if (runtimeGenerator == null)
                throw new ArgumentNullException("runtimeGenerator");

            if (string.IsNullOrWhiteSpace(
                environmentCarrierSubtype))
            {
                return runtimeGenerator;
            }


            MyPlanetGeneratorDefinition carrier =
                ResolveEnvironmentCarrierGenerator(
                    environmentCarrierSubtype);

            // Runtime planet definitions are registered after Keen's global
            // definition postprocessor has already run, so their EnvironmentId
            // is parsed but EnvironmentDefinition is never resolved. Reuse the
            // caller's normally-loaded carrier definition and bind its already
            // prepared environment object directly onto this runtime generator.
            runtimeGenerator.EnvironmentId =
                carrier.EnvironmentId;

            runtimeGenerator.EnvironmentDefinition =
                carrier.EnvironmentDefinition;

            runtimeGenerator.EnvironmentSectorType =
                carrier.EnvironmentSectorType;


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Bound caller environment to runtime generator. " +
                "Generator='" +
                runtimeGenerator.Id.SubtypeName +
                "', carrier='" +
                environmentCarrierSubtype +
                "'.");


            return runtimeGenerator;
        }


        private static void ReinitializePlanetEnvironmentInPlace(
            MyPlanet sourcePlanet,
            MyPlanetGeneratorDefinition replacementGenerator)
        {
            if (sourcePlanet == null)
                throw new ArgumentNullException("sourcePlanet");

            if (replacementGenerator == null)
                throw new ArgumentNullException("replacementGenerator");

            if (sourcePlanet.Storage == null)
                throw new Exception(
                    "Cannot initialize planet environment: live storage is null.");


            const string EnvironmentComponentName =
                "Sandbox.Game.Entities.Planet.MyPlanetEnvironmentComponent";

            const string GravityComponentName =
                "Sandbox.Game.Entities.MySphericalNaturalGravityComponent";


            Type oldEnvironmentType;
            MyComponentBase oldEnvironmentBase;
            MyEntityComponentBase oldEnvironment;

            bool hadOldEnvironment =
                TryGetPlanetComponentByInstanceTypeName(
                    sourcePlanet,
                    EnvironmentComponentName,
                    out oldEnvironmentType,
                    out oldEnvironmentBase,
                    out oldEnvironment);


            Type gravityComponentType;
            MyComponentBase gravityComponentBase;
            MyEntityComponentBase gravityComponent;

            if (!TryGetPlanetComponentByInstanceTypeName(
                sourcePlanet,
                GravityComponentName,
                out gravityComponentType,
                out gravityComponentBase,
                out gravityComponent) ||
                gravityComponentType == null ||
                gravityComponentBase == null ||
                gravityComponent == null)
            {
                throw new Exception(
                    "Could not preserve the live planet gravity component.");
            }


            bool oldEnvironmentRemoved =
                false;

            bool gravityRemoved =
                false;

            bool newEnvironmentAddedToScene =
                false;


            try
            {
                if (hadOldEnvironment)
                {
                    if (oldEnvironment == null)
                    {
                        throw new Exception(
                            "Live planet environment component is not an entity component.");
                    }

                    if (sourcePlanet.InScene)
                    {
                        oldEnvironment.OnRemovedFromScene();
                    }

                    sourcePlanet.Components.Remove(
                        oldEnvironmentType);

                    if (oldEnvironment.Entity != null)
                    {
                        oldEnvironment.SetContainer(
                            null);
                    }

                    oldEnvironmentRemoved =
                        true;
                }


                // MyPlanet.OnAddedToScene registers the gravity component with
                // MyGravityProviderSystem. Keep that exact object alive and
                // registered while MyPlanet.Init creates its temporary replacement.
                sourcePlanet.Components.Remove(
                    gravityComponentType);

                if (gravityComponent.Entity != null)
                {
                    gravityComponent.SetContainer(
                        null);
                }

                gravityRemoved =
                    true;


                MyPlanetInitArguments initArguments =
                    sourcePlanet.GetInitArguments;

                initArguments.Storage =
                    sourcePlanet.Storage;

                initArguments.StorageName =
                    sourcePlanet.StorageName;

                initArguments.Generator =
                    replacementGenerator;

                
                //initArguments.MarkAreaEmpty =
                //  false;
                // ok this should be marked false to avoid memory leak however,
                // if I do so, no way to set it back to true without re-initing 
                // the planet, this corrupts the planet generator causing asteroids
                // to spawn inside the atmosphere next time the session is reloaded
                
                // option 1: memory leak
                // option 2: corrupted planet
                // option 3: somewhere in between by set it to false but then call
                //           "init" a single time with true to restore on next load
                
                // for now, lets keep at a small memory leak

                initArguments.InitializeComponents =
                    false;

                initArguments.FadeIn =
                    false;


                // MyVoxelBase.InitVoxelMap() applies the engine's half-voxel
                // offset by mutating PositionLeftBottomCorner. That mutation is
                // correct only for first construction; calling MyPlanet.Init() on
                // an existing planet would otherwise add another (0.5,0.5,0.5)
                // every time and persist the accumulated shift on save.
                Vector3D positionLeftBottomCornerBeforeInit =
                    sourcePlanet.PositionLeftBottomCorner;

                // The planet remains inside MyEntities and in the render scene.
                // Init() is used only when the environment definition actually
                // changes (or a barren planet needs its first environment).
                sourcePlanet.Init(
                    initArguments);

                Vector3D positionLeftBottomCornerAfterInit =
                    sourcePlanet.PositionLeftBottomCorner;

                if (positionLeftBottomCornerAfterInit !=
                    positionLeftBottomCornerBeforeInit)
                {
                    sourcePlanet.PositionLeftBottomCorner =
                        positionLeftBottomCornerBeforeInit;

                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Restored planet voxel origin after environment init. " +
                        "EntityId=" +
                        sourcePlanet.EntityId +
                        ", attemptedDelta=" +
                        (positionLeftBottomCornerAfterInit -
                            positionLeftBottomCornerBeforeInit) +
                        ".");
                }


                // Init() always adds a fresh spherical gravity component. It was
                // never registered with MyGravityProviderSystem because the planet
                // itself never left/re-entered the scene, so discard it and restore
                // the original object that is already registered there.
                MyComponentBase temporaryGravity;

                if (sourcePlanet.Components.TryGet(
                    gravityComponentType,
                    out temporaryGravity) &&
                    temporaryGravity != null &&
                    !object.ReferenceEquals(
                        temporaryGravity,
                        gravityComponentBase))
                {
                    sourcePlanet.Components.Remove(
                        gravityComponentType);

                    MyEntityComponentBase temporaryGravityEntity =
                        temporaryGravity as MyEntityComponentBase;

                    if (temporaryGravityEntity != null &&
                        temporaryGravityEntity.Entity != null)
                    {
                        temporaryGravityEntity.SetContainer(
                            null);
                    }
                }


                sourcePlanet.Components.Add(
                    gravityComponentType,
                    gravityComponentBase);

                if (!object.ReferenceEquals(
                    gravityComponent.Entity,
                    sourcePlanet))
                {
                    gravityComponent.SetContainer(
                        sourcePlanet.Components);
                }

                gravityRemoved =
                    false;


                Type newEnvironmentType;
                MyComponentBase newEnvironmentBase;
                MyEntityComponentBase newEnvironment;

                if (!TryGetPlanetComponentByInstanceTypeName(
                    sourcePlanet,
                    EnvironmentComponentName,
                    out newEnvironmentType,
                    out newEnvironmentBase,
                    out newEnvironment) ||
                    newEnvironment == null)
                {
                    throw new Exception(
                        "Runtime generator did not initialize a planet environment component.");
                }

                if (!object.ReferenceEquals(
                    newEnvironment.Entity,
                    sourcePlanet))
                {
                    throw new Exception(
                        "Engine-created environment component is not owned by the live planet.");
                }


                if (sourcePlanet.InScene)
                {
                    newEnvironment.OnAddedToScene();

                    newEnvironmentAddedToScene =
                        true;
                }


                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Reinitialized live planet environment in place. " +
                    "EntityId=" +
                    sourcePlanet.EntityId +
                    ", Generator='" +
                    replacementGenerator.Id.SubtypeName +
                    "'.");
            }
            catch
            {
                // Gravity is externally registered by MyPlanet.OnAddedToScene, so
                // restoring the original component is mandatory even on failure.
                if (gravityRemoved)
                {
                    MyComponentBase currentGravity;

                    if (sourcePlanet.Components.TryGet(
                        gravityComponentType,
                        out currentGravity) &&
                        currentGravity != null &&
                        !object.ReferenceEquals(
                            currentGravity,
                            gravityComponentBase))
                    {
                        sourcePlanet.Components.Remove(
                            gravityComponentType);

                        MyEntityComponentBase currentGravityEntity =
                            currentGravity as MyEntityComponentBase;

                        if (currentGravityEntity != null &&
                            currentGravityEntity.Entity != null)
                        {
                            currentGravityEntity.SetContainer(
                                null);
                        }
                    }

                    sourcePlanet.Components.Add(
                        gravityComponentType,
                        gravityComponentBase);

                    if (!object.ReferenceEquals(
                        gravityComponent.Entity,
                        sourcePlanet))
                    {
                        gravityComponent.SetContainer(
                            sourcePlanet.Components);
                    }
                }


                // If Init() failed before a replacement environment became usable,
                // put the previous component back. Its OnRemovedFromScene() already
                // cleared sectors, so it can safely regenerate after registration.
                Type currentEnvironmentType;
                MyComponentBase currentEnvironmentBase;
                MyEntityComponentBase currentEnvironment;

                bool hasCurrentEnvironment =
                    TryGetPlanetComponentByInstanceTypeName(
                        sourcePlanet,
                        EnvironmentComponentName,
                        out currentEnvironmentType,
                        out currentEnvironmentBase,
                        out currentEnvironment);

                if (hasCurrentEnvironment &&
                    currentEnvironment != null &&
                    !object.ReferenceEquals(
                        currentEnvironmentBase,
                        oldEnvironmentBase))
                {
                    if (newEnvironmentAddedToScene)
                    {
                        currentEnvironment.OnRemovedFromScene();
                    }

                    sourcePlanet.Components.Remove(
                        currentEnvironmentType);

                    if (currentEnvironment.Entity != null)
                    {
                        currentEnvironment.SetContainer(
                            null);
                    }
                }

                if (oldEnvironmentRemoved &&
                    oldEnvironmentType != null &&
                    oldEnvironmentBase != null &&
                    oldEnvironment != null)
                {
                    sourcePlanet.Components.Add(
                        oldEnvironmentType,
                        oldEnvironmentBase);

                    if (!object.ReferenceEquals(
                        oldEnvironment.Entity,
                        sourcePlanet))
                    {
                        oldEnvironment.SetContainer(
                            sourcePlanet.Components);
                    }

                    if (sourcePlanet.InScene)
                    {
                        oldEnvironment.OnAddedToScene();
                    }
                }

                throw;
            }
        }


        private void RefreshPersistedPlanetEnvironmentInPlace(
            MyPlanet sourcePlanet,
            MyPlanetGeneratorDefinition runtimeGenerator)
        {
            MyAPIGateway.Session.Save();
            
            if (sourcePlanet == null)
                throw new ArgumentNullException("sourcePlanet");

            if (runtimeGenerator == null)
                throw new ArgumentNullException("runtimeGenerator");

            if (sourcePlanet.Storage == null)
                throw new Exception(
                    "Cannot refresh persisted planet environment: live storage is null.");


            byte[] serializedStorage;

            sourcePlanet.Storage.Save(
                out serializedStorage);

            if (serializedStorage == null ||
                serializedStorage.Length == 0)
            {
                throw new Exception(
                    "Could not serialize live planet storage for post-init physics refresh.");
            }


            VRage.ModAPI.IMyStorage storageApi =
                MyAPIGateway.Session.VoxelMaps.CreateStorage(
                    serializedStorage);

            if (storageApi == null)
            {
                throw new Exception(
                    "CreateStorage() rejected persisted planet storage copy.");
            }


            MyVoxelMap storageBridge =
                CreateVoxelStorageBridge(
                    sourcePlanet,
                    storageApi,
                    "EnvironmentMigration");

            bool storageTransferred =
                false;

            try
            {
                // Legacy donor-based saves can still require a one-time native
                // environment initialization. ReinitializePlanetEnvironmentInPlace()
                // preserves PositionLeftBottomCorner so this migration cannot
                // accumulate the engine's first-init-only half-voxel offset.
                ReinitializePlanetEnvironmentInPlace(
                    sourcePlanet,
                    runtimeGenerator);
                
                sourcePlanet.Storage =
                    storageBridge.Storage;

                storageTransferred =
                    true;
            }
            // pray to klang for this not fail...
            // because if it does,
            // we have no way to fix it withing the mod api limitations
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    "[RuntimePlanetGenerator]",
                    10000,
                    MyFontEnum.Red);
                MyAPIGateway.Utilities.ShowNotification(
                    "Could not refresh persisted planet environment",
                    10000,
                    MyFontEnum.Red);
                MyAPIGateway.Utilities.ShowNotification(
                    "Continue to playing in this session is NOT RECOMMENDED",
                    10000,
                    MyFontEnum.Red);
                MyAPIGateway.Utilities.ShowNotification(
                    "Please reload the session",
                    10000,
                    MyFontEnum.Red);

                MyLog.Default.Log(MyLogSeverity.Error, "[RuntimePlanetGenerator] Could not refresh persisted planet environment: " + e.Message);
            }
            finally
            {
                RemoveStorageBridgeFromWorld(
                    storageBridge,
                    !storageTransferred);
            }
        }


        private void RestorePlanetEnvironmentFromCarrier(
            MyPlanet sourcePlanet,
            string environmentCarrierSubtype,
            string providerSubtype)
        {
            MyPlanetGeneratorDefinition runtimeGenerator;

            if (!m_persistedRuntimeGenerators.TryGetValue(
                providerSubtype,
                out runtimeGenerator) ||
                runtimeGenerator == null)
            {
                throw new Exception(
                    "Runtime generator '" +
                    providerSubtype +
                    "' is not registered.");
            }


            BindRuntimeEnvironmentCarrier(
                runtimeGenerator,
                environmentCarrierSubtype);


            // New saves persist Planet.Generator as PlanetModification_* and are
            // initialized natively by the engine. This branch only migrates saves
            // created by the earlier donor-based prototype, where the VX2 provider
            // was runtime-modified but MyPlanet.Generator still said Mars/Moon.
            if (sourcePlanet.Generator != null &&
                (string.Equals(
                    sourcePlanet.Generator.Id.SubtypeName,
                    providerSubtype,
                    StringComparison.OrdinalIgnoreCase) ||
                 object.ReferenceEquals(
                    sourcePlanet.Generator.EnvironmentDefinition,
                    runtimeGenerator.EnvironmentDefinition)))
            {
                // The storage provider may advance to a newer PlanetModification_*
                // revision while the caller environment stays the same. There is
                // no reason to run MyPlanet.Init() merely to make Generator.Id
                // match the provider subtype; doing so mutates initialization-only
                // voxel state. The existing environment component is already bound
                // to the same prepared environment object.
                return;
            }


            RefreshPersistedPlanetEnvironmentInPlace(
                sourcePlanet,
                runtimeGenerator);


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Migrated persisted planet to native runtime generator. " +
                "EntityId=" +
                sourcePlanet.EntityId +
                ", provider='" +
                providerSubtype +
                "', carrier='" +
                environmentCarrierSubtype +
                "'.");
        }


        private bool IsPersistedRuntimeSubtype(
            string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype))
                return false;


            return m_settings.PlanetBuilders.Any(x =>
                x != null &&
                x.Subtype != null &&
                x.Subtype.Equals(
                    subtype,
                    StringComparison.OrdinalIgnoreCase));
        }


        private static void DumpDefinitionOrigin(
            MyPlanetGeneratorDefinition definition)
        {
            if (definition == null)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Definition origin: NULL definition");

                return;
            }


            MyModContext context =
                definition.Context;


            if (context == null)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Definition origin: Subtype='" +
                    definition.Id.SubtypeName +
                    "', Context=NULL");

                return;
            }


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Definition origin: " +
                "Subtype='" +
                definition.Id.SubtypeName +
                "', CurrentFile='" +
                context.CurrentFile +
                "', ModName='" +
                context.ModName +
                "', ModId='" +
                context.ModId +
                "', ModPath='" +
                context.ModPath +
                "', ModPathData='" +
                context.ModPathData +
                "', IsBaseGame=" +
                context.IsBaseGame);
        }


        private MyObjectBuilder_PlanetGeneratorDefinition
            CaptureSourceGeneratorBuilder(
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


        private bool[] CollectSourceMaterialMapValues(
            MyModContext sourceContext,
            string sourceSubtype,
            string sourceFolderName)
        {
            string[] materialFiles =
            {
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };


            bool[] observedValues =
                new bool[256];


            for (int i = 0;
                i < materialFiles.Length;
                i++)
            {
                string fileName =
                    materialFiles[i];

                byte[] data =
                    ReadSourcePlanetDataFile(
                        sourceContext,
                        sourceSubtype,
                        sourceFolderName,
                        fileName);


                RewritePngMaterialChannel(
                    data,
                    0,
                    null,
                    fileName,
                    0,
                    0,
                    double.NegativeInfinity,
                    observedValues);
            }


            int observedCount =
                0;

            for (int i = 0;
                i < observedValues.Length;
                i++)
            {
                if (observedValues[i])
                    observedCount++;
            }


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Source material maps use " +
                observedCount +
                " distinct red-channel values.");


            return observedValues;
        }


        private void CreatePlanetDataArchive(
            MyModContext sourceContext,
            string sourceSubtype,
            string sourceFolderName,
            string archiveFileName,
            byte grassMaterialValue,
            int[] grassOverlayValuesBySource,
            long planetSeed,
            int grassCoveragePercent)
        {
            double grassThreshold =
                ComputeGrassCoverageThreshold(
                    planetSeed,
                    grassCoveragePercent);
            string[] files =
            {
                "front.png",
                "back.png",
                "left.png",
                "right.png",
                "up.png",
                "down.png",

                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };


            var entries =
                new List<MinimalZip.Entry>(
                    files.Length);


            for (int i = 0;
                i < files.Length;
                i++)
            {
                string fileName =
                    files[i];


                byte[] data =
                    ReadSourcePlanetDataFile(
                        sourceContext,
                        sourceSubtype,
                        sourceFolderName,
                        fileName);


                if (fileName.EndsWith(
                    "_mat.png",
                    StringComparison.OrdinalIgnoreCase))
                {
                    data =
                        RewritePngMaterialChannel(
                            data,
                            grassMaterialValue,
                            grassOverlayValuesBySource,
                            fileName,
                            grassCoveragePercent,
                            planetSeed,
                            grassThreshold,
                            null);
                }


                entries.Add(
                    new MinimalZip.Entry(
                        fileName,
                        data, MinimalZip.CompressionMode.Deflate));
            }


            byte[] archive =
                MinimalZip.WriteBytes(
                    entries);


            SaveRuntimeArchive(
                archiveFileName,
                archive);


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Packed generic planet data: " +
                archiveFileName +
                ", source='" +
                sourceSubtype +
                "', entries=" +
                entries.Count +
                ", bytes=" +
                archive.Length +
                ", coverage=" +
                grassCoveragePercent +
                "%, seed=" +
                planetSeed +
                ", threshold=" +
                grassThreshold);
        }


        private void CreateModifiedPlanetDataArchive(
            PlanetModificationSnapshot snapshot,
            string archiveFileName)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");


            string[] files =
            {
                "front.png",
                "back.png",
                "left.png",
                "right.png",
                "up.png",
                "down.png",
                "front_mat.png",
                "back_mat.png",
                "left_mat.png",
                "right_mat.png",
                "up_mat.png",
                "down_mat.png"
            };

            var entries =
                new List<MinimalZip.Entry>(
                    files.Length);

            Dictionary<string, byte[]> runtimeSourceFiles =
                string.IsNullOrWhiteSpace(
                    snapshot.SourceArchiveFile)
                    ? null
                    : ReadRuntimePlanetDataArchive(
                        snapshot.SourceArchiveFile);

            if (snapshot.FractalNoiseOperations != null)
            {
                for (int operationIndex = 0;
                    operationIndex < snapshot.FractalNoiseOperations.Count;
                    operationIndex++)
                {
                    FractalNoiseOperation operation =
                        snapshot.FractalNoiseOperations[operationIndex];

                    operation.Threshold =
                        ComputeGrassCoverageThreshold(
                            snapshot.PlanetSeed,
                            operation.CoveragePercent);
                }
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
                    snapshot.AllocatedComplexMaterialValues != null &&
                    snapshot.AllocatedComplexMaterialValues.Count > 0;


                if ((haveTransforms ||
                        haveFractalNoise ||
                        haveBiomeReplacements ||
                        validateAllocatedComplexMaterials) &&
                    !haveModifiedImage)
                {
                    modified =
                        DecodePlanetPng(
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
                    ValidateAllocatedComplexMaterialValues(
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
                        ApplyBiomeReplacementToPlanetImage(
                            modified,
                            snapshot.BiomeReplacementOperations[operationIndex]);
                    }
                }

                if (haveFractalNoise)
                {
                    ApplyFractalNoiseToPlanetImage(
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
            }


            byte[] archive =
                MinimalZip.WriteBytes(
                    entries);

            SaveRuntimeArchive(
                archiveFileName,
                archive);


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
        }


        private static void ApplyFractalNoiseToPlanetImage(
            PlanarPngBitmap image,
            string faceFileName,
            long planetSeed,
            List<FractalNoiseOperation> operations)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            if (operations == null ||
                operations.Count == 0)
            {
                return;
            }

            bool needsNoise =
                false;

            for (int operationIndex = 0;
                operationIndex < operations.Count;
                operationIndex++)
            {
                FractalNoiseOperation operation =
                    operations[operationIndex];

                if (operation == null)
                    throw new ArgumentNullException("operations");

                if (operation.PlaneIndex < 0 ||
                    operation.PlaneIndex >= image.Planes.Length)
                {
                    throw new Exception(
                        "Invalid planet-map plane index: " +
                        operation.PlaneIndex +
                        ".");
                }

                if (operation.CoveragePercent > 0 &&
                    operation.CoveragePercent < 100)
                {
                    needsNoise =
                        true;
                }
            }


            double[] noiseGrid =
                null;

            if (needsNoise)
            {
                int faceIndex =
                    GetCubemapFaceIndex(
                        faceFileName);

                noiseGrid =
                    BuildGrassNoiseGrid(
                        faceIndex,
                        planetSeed);
            }

            int pixelOffset =
                0;

            for (int y = 0;
                y < image.Height;
                y++)
            {
                for (int x = 0;
                    x < image.Width;
                    x++)
                {
                    double score =
                        needsNoise
                            ? SampleGrassNoiseGrid(
                                noiseGrid,
                                x,
                                y,
                                image.Width,
                                image.Height)
                            : 0.0;

                    for (int operationIndex = 0;
                        operationIndex < operations.Count;
                        operationIndex++)
                    {
                        FractalNoiseOperation operation =
                            operations[operationIndex];

                        bool selected =
                            operation.CoveragePercent >= 100 ||
                            (operation.CoveragePercent > 0 &&
                                score >= operation.Threshold);

                        if (selected)
                        {
                            image.Planes[operation.PlaneIndex][pixelOffset] =
                                operation.TargetValue;
                        }
                    }

                    pixelOffset++;
                }
            }
        }


        private static void ApplyBiomeReplacementToPlanetImage(
            PlanarPngBitmap image,
            BiomeReplacementOperation operation)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            if (operation == null)
                throw new ArgumentNullException("operation");

            byte[] biomes =
                image.Planes[1];

            for (int pixel = 0;
                pixel < biomes.Length;
                pixel++)
            {
                if (biomes[pixel] == operation.SourceBiome)
                    biomes[pixel] = operation.TargetBiome;
            }
        }


        private static void ValidateAllocatedComplexMaterialValues(
            PlanarPngBitmap image,
            string faceFileName,
            List<byte> allocatedValues)
        {
            byte[] red =
                image.Planes[0];

            for (int pixel = 0;
                pixel < red.Length;
                pixel++)
            {
                byte sourceValue =
                    red[pixel];

                for (int valueIndex = 0;
                    valueIndex < allocatedValues.Count;
                    valueIndex++)
                {
                    if (sourceValue ==
                        allocatedValues[valueIndex])
                    {
                        throw new Exception(
                            "Allocated complex material-map value " +
                            sourceValue +
                            " already exists in source PNG " +
                            faceFileName +
                            ". The modification was not pushed.");
                    }
                }
            }
        }


        private byte[] ReadSourcePlanetDataFile(
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
                if (!MyAPIGateway.Utilities.FileExistsInGameContent(
                    relativePath))
                {
                    throw new Exception(
                        "Source planet map file does not exist in game content: " +
                        relativePath);
                }


                using (BinaryReader reader =
                    MyAPIGateway.Utilities.ReadBinaryFileInGameContent(
                        relativePath))
                {
                    return ReadAllBytes(
                        reader);
                }
            }


            if (!MyAPIGateway.Utilities.FileExistsInModLocation(
                relativePath,
                sourceContext.ModItem))
            {
                throw new Exception(
                    "Source planet map file does not exist in mod content: " +
                    relativePath);
            }


            using (BinaryReader reader =
                MyAPIGateway.Utilities.ReadBinaryFileInModLocation(
                    relativePath,
                    sourceContext.ModItem))
            {
                return ReadAllBytes(
                    reader);
            }
        }


        private Dictionary<string, byte[]> ReadRuntimePlanetDataArchive(
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
                List<MinimalZip.Entry> entries =
                    MinimalZip.Read(
                        reader.BaseStream);

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
        }


        private byte[] ReadSnapshotPlanetDataFile(
            PlanetModificationSnapshot snapshot,
            Dictionary<string, byte[]> runtimeSourceFiles,
            string fileName)
        {
            if (runtimeSourceFiles == null)
            {
                return ReadSourcePlanetDataFile(
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


        private string BuildWorldStorageFilePath(
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


        private static string GetPrimarySurfaceMaterial(
            MyPlanetGeneratorDefinition generator)
        {
            if (generator.DefaultSurfaceMaterial != null)
            {
                if (!string.IsNullOrWhiteSpace(
                    generator.DefaultSurfaceMaterial.FirstOrDefault))
                {
                    return
                        generator.DefaultSurfaceMaterial.FirstOrDefault;
                }


                if (!string.IsNullOrWhiteSpace(
                    generator.DefaultSurfaceMaterial.Material))
                {
                    return
                        generator.DefaultSurfaceMaterial.Material;
                }
            }


            if (generator.SurfaceMaterialTable != null)
            {
                for (int i = 0;
                    i < generator.SurfaceMaterialTable.Length;
                    i++)
                {
                    MyPlanetMaterialDefinition material =
                        generator.SurfaceMaterialTable[i];


                    if (material != null &&
                        !string.IsNullOrWhiteSpace(
                            material.FirstOrDefault))
                    {
                        return material.FirstOrDefault;
                    }
                }
            }


            throw new Exception(
                "Could not determine source planet surface material.");
        }


        private string ResolveInitialSavePath()
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


        private void RebindRuntimeGeneratorToSavePath(
            string savePath)
        {
            savePath =
                NormalizePath(
                    savePath);

            if (string.IsNullOrWhiteSpace(savePath))
                return;


            RecreateWorldStorageCache(
                false);


            for (int i = 0;
                i < m_settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry entry =
                    m_settings.PlanetBuilders[i];

                if (entry == null)
                    continue;


                RebindGeneratorFolder(
                    entry.Subtype,
                    BuildWorldStorageFilePath(
                        savePath,
                        entry.ArchiveFile));
            }


            m_boundSavePath =
                savePath;


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Runtime generator save path rebound: " +
                "SavePath='" +
                m_boundSavePath +
                "', Generators=" +
                m_settings.PlanetBuilders.Count);
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


        private static string NormalizePath(
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


        private MyPlanetGeneratorDefinition RegisterRuntimeGeneratorDefinition(
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
                    (MyModContext)ModContext);

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


        private void ReadLivePlanetProviderIdentity(
            MyPlanet planet,
            out long planetSeed,
            out string providerSubtype)
        {
            if (planet == null ||
                planet.Storage == null)
            {
                throw new Exception(
                    "Planet/provider identity requires live planet storage.");
            }


            byte[] compressed;

            planet.Storage.Save(
                out compressed);

            if (compressed == null ||
                compressed.Length < 2 ||
                compressed[0] != 0x1F ||
                compressed[1] != 0x8B)
            {
                throw new Exception(
                    "Could not serialize live planet VX2 while reading its seed.");
            }


            byte[] raw =
                Zlib.InflateGzip(
                    compressed);


            string currentGeneratorSubtype =
                planet.Generator == null
                    ? null
                    : planet.Generator.Id.SubtypeName;


            if (TryReadSerializedPlanetProviderSeed(
                raw,
                currentGeneratorSubtype,
                out planetSeed))
            {
                providerSubtype =
                    currentGeneratorSubtype;

                return;
            }


            if (m_settings != null &&
                m_settings.PlanetBuilders != null)
            {
                for (int i =
                        m_settings.PlanetBuilders.Count - 1;
                    i >= 0;
                    i--)
                {
                    RuntimePlanetBuilderEntry entry =
                        m_settings.PlanetBuilders[i];

                    if (entry == null ||
                        entry.SourceEntityId != planet.EntityId ||
                        string.IsNullOrWhiteSpace(
                            entry.Subtype))
                    {
                        continue;
                    }


                    if (TryReadSerializedPlanetProviderSeed(
                        raw,
                        entry.Subtype,
                        out planetSeed))
                    {
                        providerSubtype =
                            entry.Subtype;

                        return;
                    }
                }
            }


            throw new Exception(
                "Could not locate the serialized live planet provider subtype " +
                "and seed in VX2.");
        }


        private static bool TryReadSerializedPlanetProviderSeed(
            byte[] raw,
            string providerSubtype,
            out long planetSeed)
        {
            planetSeed =
                0;


            if (raw == null ||
                string.IsNullOrWhiteSpace(
                    providerSubtype))
            {
                return false;
            }


            byte[] subtypeBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    providerSubtype);


            if (subtypeBytes.Length == 0 ||
                subtypeBytes.Length > 127)
            {
                return false;
            }


            int matchOffset =
                -1;

            int matches =
                0;


            for (int i = 16;
                i <= raw.Length - subtypeBytes.Length - 1;
                i++)
            {
                if (raw[i] !=
                    (byte)subtypeBytes.Length)
                {
                    continue;
                }


                bool match =
                    true;


                for (int j = 0;
                    j < subtypeBytes.Length;
                    j++)
                {
                    if (raw[
                        i + 1 + j] !=
                        subtypeBytes[j])
                    {
                        match =
                            false;

                        break;
                    }
                }


                if (!match)
                    continue;


                matchOffset =
                    i;

                matches++;
            }


            if (matches != 1 ||
                matchOffset < 16)
            {
                return false;
            }


            int seedOffset =
                matchOffset -
                16;

            ulong seedBits =
                0;


            for (int i = 0;
                i < 8;
                i++)
            {
                seedBits |=
                    (ulong)raw[
                        seedOffset + i] <<
                    (i * 8);
            }


            planetSeed =
                unchecked(
                    (long)seedBits);

            return true;
        }


        private PlanetModificationWorkResult PrepareStoredProviderSwap(
            MyPlanet targetPlanet,
            MyPlanetGeneratorDefinition replacementGenerator,
            string currentProviderSubtype,
            string operationName = "planet modification")
        {
            if (targetPlanet == null)
                throw new ArgumentNullException("targetPlanet");

            if (targetPlanet.Storage == null)
                throw new Exception(
                    "Target planet has null Storage.");

            if (replacementGenerator == null)
                throw new ArgumentNullException("replacementGenerator");


            // Capture the exact storage instance whose bytes are copied. The
            // simulation-thread commit later compares against this reference,
            // making the final assignment a real compare-and-swap operation.
            object originalStorage =
                targetPlanet.Storage;

            byte[] compressed;

            targetPlanet.Storage.Save(
                out compressed);

            if (compressed == null || compressed.Length < 2)
                throw new Exception(
                    "Storage.Save(out byte[]) returned no data.");

            if (compressed[0] != 0x1F ||
                compressed[1] != 0x8B)
            {
                throw new Exception(
                    "Serialized storage is not gzip data.");
            }


            byte[] patchedRaw =
                Zlib.InflateGzip(
                    compressed);


            // The voxel palette remains unchanged. Terraform material behavior
            // comes from the generated planet definition and map overlays; the
            // serialized storage only needs to point at the new provider subtype.
            if (!string.Equals(
                currentProviderSubtype,
                replacementGenerator.Id.SubtypeName,
                StringComparison.OrdinalIgnoreCase))
            {
                patchedRaw =
                    ReplaceSerializedShortStringExact(
                        patchedRaw,
                        currentProviderSubtype,
                        replacementGenerator.Id.SubtypeName);
            }

            byte[] patchedCompressed =
                Zlib.DeflateGzipStored(
                    patchedRaw);


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Prepared planet provider for " +
                operationName +
                ". bytes=" +
                patchedCompressed.Length +
                ". Waiting for simulation-thread commit.");


            return new PlanetModificationWorkResult
            {
                TargetPlanet =
                    targetPlanet,

                OriginalStorage =
                    originalStorage,

                PatchedStorage =
                    patchedCompressed,

                ReplacementGenerator =
                    replacementGenerator,

                OperationName =
                    operationName
            };
        }


        /// <summary>
        /// Performs the compare-and-swap commit on the simulation thread. The
        /// expensive serialized copy is already complete; this method creates
        /// the engine storage bridge and changes the live storage reference in
        /// one simulation callback.
        /// </summary>
        private void CommitPlanetStorage(
            PlanetModificationWorkResult workResult)
        {
            if (workResult == null)
                throw new ArgumentNullException("workResult");

            if (workResult.TargetPlanet == null ||
                workResult.TargetPlanet.Storage == null)
            {
                throw new Exception(
                    "Target planet disappeared before the storage commit.");
            }

            if (!object.ReferenceEquals(
                workResult.TargetPlanet.Storage,
                workResult.OriginalStorage))
            {
                throw new Exception(
                    "Target planet storage changed while terraform work was running; " +
                    "the prepared result was not committed.");
            }

            if (workResult.PatchedStorage == null ||
                workResult.PatchedStorage.Length == 0)
            {
                throw new Exception(
                    "Terraform worker produced no patched storage.");
            }


            if (!string.IsNullOrWhiteSpace(
                workResult.EnvironmentCarrierSubtype))
            {
                if (workResult.ReplacementGenerator == null)
                {
                    throw new Exception(
                        "Terraform result is missing its runtime generator.");
                }

                BindRuntimeEnvironmentCarrier(
                    workResult.ReplacementGenerator,
                    workResult.EnvironmentCarrierSubtype);
            }


            VRage.ModAPI.IMyStorage patchedStorageApi =
                MyAPIGateway.Session.VoxelMaps.CreateStorage(
                    workResult.PatchedStorage);

            if (patchedStorageApi == null)
                throw new Exception(
                    "CreateStorage() rejected the patched VX2.");


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Committing prepared provider for " +
                (workResult.OperationName ?? "planet modification") +
                ". ModAPI storage size=" +
                patchedStorageApi.Size +
                ".");


            SpawnPlanetThroughVoxelMapStorageBridge(
                workResult.TargetPlanet,
                patchedStorageApi,
                workResult.ReplacementGenerator,
                workResult.EnvironmentCarrierSubtype);
        }


        private MyVoxelMap CreateVoxelStorageBridge(
            MyPlanet sourcePlanet,
            VRage.ModAPI.IMyStorage storageApi,
            string purpose)
        {
            if (sourcePlanet == null)
                throw new ArgumentNullException("sourcePlanet");

            if (storageApi == null)
                throw new ArgumentNullException("storageApi");


            string bridgeStorageName =
                "VoxelCubemapApi_" +
                (string.IsNullOrWhiteSpace(purpose)
                    ? "StorageBridge"
                    : purpose) +
                "_" +
                DateTime.UtcNow.Ticks;

            long bridgeEntityId;
            IMyEntity existingEntity;

            do
            {
                bridgeEntityId =
                    ((long)m_bridgeRandom.Next() << 31) |
                    (uint)m_bridgeRandom.Next();

                bridgeEntityId &=
                    long.MaxValue;
            }
            while (bridgeEntityId == 0 ||
                MyAPIGateway.Entities.TryGetEntityById(
                    bridgeEntityId,
                    out existingEntity));


            const double BridgeDistance =
                299792458.0 * 3.0;

            double directionX;
            double directionY;
            double directionZ;
            double directionLengthSquared;


            do
            {
                directionX =
                    m_bridgeRandom.NextDouble() * 2.0 - 1.0;

                directionY =
                    m_bridgeRandom.NextDouble() * 2.0 - 1.0;

                directionZ =
                    m_bridgeRandom.NextDouble() * 2.0 - 1.0;

                directionLengthSquared =
                    directionX * directionX +
                    directionY * directionY +
                    directionZ * directionZ;
            }
            while (directionLengthSquared < 0.000001 ||
                directionLengthSquared > 1.0);


            double inverseDirectionLength =
                1.0 /
                Math.Sqrt(
                    directionLengthSquared);

            Vector3D bridgePosition =
                sourcePlanet.PositionComp.GetPosition() +
                new Vector3D(
                    directionX * inverseDirectionLength,
                    directionY * inverseDirectionLength,
                    directionZ * inverseDirectionLength) *
                BridgeDistance;


            VRage.Game.ModAPI.IMyVoxelMap bridgeApi =
                MyAPIGateway.Session.VoxelMaps.CreateVoxelMap(
                    bridgeStorageName,
                    storageApi,
                    bridgePosition,
                    bridgeEntityId);

            if (bridgeApi == null)
                throw new Exception(
                    "CreateVoxelMap() rejected the ModAPI storage bridge.");

            MyVoxelMap bridge =
                bridgeApi as MyVoxelMap;

            if (bridge == null)
            {
                bridgeApi.Close();

                throw new Exception(
                    "CreateVoxelMap() did not return Sandbox.Game.Entities.MyVoxelMap; " +
                    "cannot bridge the storage interface.");
            }

            if (bridge.Storage == null)
            {
                bridge.Close();

                throw new Exception(
                    "Temporary MyVoxelMap bridge has null engine storage.");
            }


            bridge.Save =
                false;

            return bridge;
        }


        private static void RemoveStorageBridgeFromWorld(
            MyVoxelMap bridge,
            bool closeStorage)
        {
            if (bridge == null)
                return;


            bridge.Save =
                false;

            // RemoveEntity also unregisters MyVoxelBase instances from the
            // session voxel-map collection. It is safe to call even when the
            // bridge was never inserted into the render scene.
            MyAPIGateway.Entities.RemoveEntity(
                bridge);

            if (closeStorage)
            {
                bridge.Close();
            }
        }


        private void ScheduleVegetationClearAroundExistingGrids(
            MyPlanet planet)
        {
            if (planet == null ||
                planet.Closed ||
                planet.MarkedForClose)
            {
                return;
            }


            BoundingBoxD planetBounds =
                planet.PositionComp.WorldAABB;

            // MyPlanetEnvironmentComponent.UpdatePhysics() considers dynamic
            // clusters up to 1024 m outside the planet AABB. Use the same
            // tolerance so near-surface vehicles are included.
            planetBounds.Inflate(
                1024.0);


            List<BoundingBoxD> boxes =
                new List<BoundingBoxD>();


            foreach (IMyEntity entity in MyEntities.GetEntities())
            {
                MyCubeGrid grid =
                    entity as MyCubeGrid;

                if (grid == null ||
                    grid.Closed ||
                    grid.MarkedForClose ||
                    grid.IsStatic ||
                    grid.Physics == null)
                {
                    continue;
                }


                BoundingBoxD gridBounds =
                    grid.PositionComp.WorldAABB;

                if (!planetBounds.Intersects(
                    gridBounds))
                {
                    continue;
                }


                // Match MyPlanetSurfacePlacement.ClearVegetation(): the
                // encounter code uses a sphere whose radius is twice the
                // prefab bounding-box half-extents length, then converts it
                // to a world-space AABB for ClearEnvironmentItemsBlocking().
                double radius =
                    gridBounds.HalfExtents.Length() *
                    2.0;

                if (radius <= 0.0)
                    continue;


                Vector3D center =
                    gridBounds.Center;

                boxes.Add(
                    new BoundingBoxD(
                        center - radius,
                        center + radius));
            }


            if (boxes.Count == 0)
                return;


            PendingVegetationClear pending =
                new PendingVegetationClear
                {
                    PlanetEntityId =
                        planet.EntityId,
                    Boxes =
                        boxes,
                    Pass =
                        0,
                    TicksUntilNextPass =
                        VegetationClearPassDelays[0]
                };

            m_pendingVegetationClears.Add(
                pending);


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Scheduled vegetation clear around " +
                boxes.Count +
                " existing grid(s). EntityId=" +
                planet.EntityId +
                ".");
        }


        private void ProcessPendingVegetationClears()
        {
            for (int i =
                    m_pendingVegetationClears.Count - 1;
                i >= 0;
                i--)
            {
                PendingVegetationClear pending =
                    m_pendingVegetationClears[i];

                if (pending == null)
                {
                    m_pendingVegetationClears.RemoveAt(
                        i);

                    continue;
                }


                if (pending.TicksUntilNextPass > 0)
                {
                    pending.TicksUntilNextPass--;

                    continue;
                }


                MyPlanet planet =
                    FindPlanetByEntityId(
                        pending.PlanetEntityId);

                if (planet == null ||
                    planet.Closed ||
                    planet.MarkedForClose)
                {
                    m_pendingVegetationClears.RemoveAt(
                        i);

                    continue;
                }


                int sectorsTouched =
                    ClearEnvironmentItemsInBoxes(
                        planet,
                        pending.Boxes);


                pending.Pass++;

                if (pending.Pass >=
                    VegetationClearPassDelays.Length)
                {
                    m_pendingVegetationClears.RemoveAt(
                        i);

                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Finished post-terraform vegetation clear. " +
                        "EntityId=" +
                        planet.EntityId +
                        ", lastPassSectors=" +
                        sectorsTouched +
                        ".");

                    continue;
                }


                pending.TicksUntilNextPass =
                    VegetationClearPassDelays[
                        pending.Pass];
            }
        }


        private static int ClearEnvironmentItemsInBoxes(
            MyPlanet planet,
            List<BoundingBoxD> boxes)
        {
            if (planet == null ||
                boxes == null ||
                boxes.Count == 0)
            {
                return 0;
            }


            int sectorsTouched =
                0;

            List<MyEntity> entities =
                new List<MyEntity>();


            for (int i = 0;
                i < boxes.Count;
                i++)
            {
                BoundingBoxD worldBox =
                    boxes[i];

                entities.Clear();

                planet.Hierarchy.QueryAABB(
                    ref worldBox,
                    entities);


                for (int j = 0;
                    j < entities.Count;
                    j++)
                {
                    MyEnvironmentSector sector =
                        entities[j] as MyEnvironmentSector;

                    if (sector == null ||
                        sector.Closed ||
                        sector.MarkedForClose)
                    {
                        continue;
                    }


                    if (sector.DataView == null)
                    {
                        sector.ForceLoadDataView();
                    }

                    if (sector.DataView == null)
                        continue;


                    BoundingBoxD clearBox =
                        boxes[i];

                    sector.DisableItemsInBox(
                        ref clearBox);

                    sectorsTouched++;
                }
            }


            return sectorsTouched;
        }


        private void SpawnPlanetThroughVoxelMapStorageBridge(
            MyPlanet sourcePlanet,
            VRage.ModAPI.IMyStorage patchedStorageApi,
            MyPlanetGeneratorDefinition replacementGenerator,
            string environmentCarrierSubtype)
        {
            if (sourcePlanet == null)
                throw new ArgumentNullException("sourcePlanet");

            if (patchedStorageApi == null)
                throw new ArgumentNullException("patchedStorageApi");


            MyVoxelMap bridge =
                CreateVoxelStorageBridge(
                    sourcePlanet,
                    patchedStorageApi,
                    "StorageBridge");

            bool storageTransferred =
                false;

            try
            {
                if (!string.IsNullOrWhiteSpace(
                    environmentCarrierSubtype))
                {
                    if (replacementGenerator == null)
                    {
                        throw new Exception(
                            "Caller environment requires a runtime generator.");
                    }

                    Type currentEnvironmentType;
                    MyComponentBase currentEnvironmentBase;
                    MyEntityComponentBase currentEnvironmentEntity;

                    bool hasEnvironmentComponent =
                        TryGetPlanetComponentByInstanceTypeName(
                            sourcePlanet,
                            "Sandbox.Game.Entities.Planet.MyPlanetEnvironmentComponent",
                            out currentEnvironmentType,
                            out currentEnvironmentBase,
                            out currentEnvironmentEntity);

                    bool environmentDefinitionChanged =
                        sourcePlanet.Generator == null ||
                        !object.ReferenceEquals(
                            sourcePlanet.Generator.EnvironmentDefinition,
                            replacementGenerator.EnvironmentDefinition);

                    // MyPlanet.Init() is initialization-only code. Use it only for
                    // the two cases where there is no alternative: adding the first
                    // environment to a barren planet, or switching to a different
                    // prepared caller environment definition. Repeated biome/height
                    // edits using the same carrier must remain pure storage swaps.
                    if (!hasEnvironmentComponent ||
                        environmentDefinitionChanged)
                    {
                        ReinitializePlanetEnvironmentInPlace(
                            sourcePlanet,
                            replacementGenerator);
                    }
                    else
                    {
                        MyLog.Default.WriteLineAndConsole(
                            "[RuntimePlanetGenerator] Reusing existing live planet environment; " +
                            "caller definition is unchanged. EntityId=" +
                            sourcePlanet.EntityId +
                            ".");
                    }
                }


                // Keep the original MyPlanet in-scene. This setter performs the
                // provider refresh plus ClearPhysicsShapes()/Clipmap.InvalidateAll().
                // It intentionally remains the final planet/voxel lifecycle mutation.
                sourcePlanet.Storage =
                    bridge.Storage;

                storageTransferred =
                    true;

                if (!string.IsNullOrWhiteSpace(
                    environmentCarrierSubtype))
                {
                    ScheduleVegetationClearAroundExistingGrids(
                        sourcePlanet);
                }


                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Patched live planet in-place. " +
                    "EntityId=" +
                    sourcePlanet.EntityId +
                    ", StorageName='" +
                    sourcePlanet.StorageName +
                    "', environment=" +
                    (string.IsNullOrWhiteSpace(environmentCarrierSubtype)
                        ? "unchanged"
                        : "'" + environmentCarrierSubtype + "'") +
                    ".");
            }
            finally
            {
                // After a successful transfer the planet owns the bridge storage,
                // so the bridge itself must be unregistered but not closed.
                RemoveStorageBridgeFromWorld(
                    bridge,
                    !storageTransferred);
            }
        }


        private static byte[] ReplaceSerializedShortStringExact(
            byte[] raw,
            string fromValue,
            string toValue)
        {
            byte[] fromBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    fromValue);

            byte[] toBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    toValue);

            if (fromBytes.Length > 127 ||
                toBytes.Length > 127)
            {
                throw new Exception(
                    "Serialized provider subtype exceeds the supported short-string encoding.");
            }


            int matchOffset = -1;
            int matches = 0;


            for (int i = 0;
                i <= raw.Length - fromBytes.Length - 1;
                i++)
            {
                if (raw[i] != (byte)fromBytes.Length)
                    continue;


                bool match =
                    true;


                for (int j = 0;
                    j < fromBytes.Length;
                    j++)
                {
                    if (raw[i + 1 + j] != fromBytes[j])
                    {
                        match =
                            false;

                        break;
                    }
                }


                if (!match)
                    continue;


                matchOffset =
                    i;

                matches++;
            }


            if (matches != 1)
            {
                throw new Exception(
                    "Expected exactly one serialized '" +
                    fromValue +
                    "' provider subtype in raw VX2, found " +
                    matches +
                    ".");
            }


            int oldEntryLength =
                1 +
                fromBytes.Length;

            int newEntryLength =
                1 +
                toBytes.Length;


            byte[] output =
                new byte[
                    raw.Length -
                    oldEntryLength +
                    newEntryLength];


            if (matchOffset > 0)
            {
                Buffer.BlockCopy(
                    raw,
                    0,
                    output,
                    0,
                    matchOffset);
            }


            int outputCursor =
                matchOffset;


            output[outputCursor++] =
                (byte)toBytes.Length;


            Buffer.BlockCopy(
                toBytes,
                0,
                output,
                outputCursor,
                toBytes.Length);


            outputCursor +=
                toBytes.Length;


            int oldTailOffset =
                matchOffset +
                oldEntryLength;

            int tailLength =
                raw.Length -
                oldTailOffset;


            if (tailLength > 0)
            {
                Buffer.BlockCopy(
                    raw,
                    oldTailOffset,
                    output,
                    outputCursor,
                    tailLength);
            }


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] VX2 provider subtype patched: '" +
                fromValue +
                "' -> '" +
                toValue +
                "'");


            return output;
        }


        private static MyPlanet FindPlanetByStorageName(
            string storageName)
        {
            foreach (IMyEntity entity in MyEntities.GetEntities())
            {
                MyPlanet planet =
                    entity as MyPlanet;

                if (planet == null)
                    continue;

                if (string.Equals(
                    planet.StorageName,
                    storageName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return planet;
                }
            }

            return null;
        }


        private static MyPlanet FindPlanetByEntityId(
            long entityId)
        {
            foreach (IMyEntity entity in MyEntities.GetEntities())
            {
                MyPlanet planet =
                    entity as MyPlanet;

                if (planet != null &&
                    planet.EntityId == entityId)
                {
                    return planet;
                }
            }

            return null;
        }


        private static MyPlanet FindNearestPlanetToPlayer()
        {
            if (MyAPIGateway.Session == null ||
                MyAPIGateway.Session.Player == null ||
                MyAPIGateway.Session.Player.Character == null)
            {
                return null;
            }


            Vector3D playerPosition =
                MyAPIGateway.Session.Player
                    .Character
                    .GetPosition();

            MyPlanet nearest =
                null;

            double nearestSurfaceDistance =
                double.MaxValue;


            foreach (IMyEntity entity in MyEntities.GetEntities())
            {
                MyPlanet planet =
                    entity as MyPlanet;


                if (planet == null ||
                    planet.Generator == null)
                {
                    continue;
                }


                MyPlanetInitArguments args =
                    planet.GetInitArguments;


                double centerDistance =
                    Vector3D.Distance(
                        playerPosition,
                        planet.PositionComp.GetPosition());


                double surfaceDistance =
                    Math.Abs(
                        centerDistance -
                        args.Radius);


                if (surfaceDistance <
                    nearestSurfaceDistance)
                {
                    nearestSurfaceDistance =
                        surfaceDistance;

                    nearest =
                        planet;
                }
            }


            return nearest;
        }


    }
}
