using System.Collections.Generic;
using VRage.Game;

namespace VoxelCubemapApi.Server.PlanetModification.EnvironmentPresets
{
    internal sealed class EnvironmentPresetSnapshot
    {
        public string Name;
        public string SourceGeneratorSubtype;
        public MyDefinitionId SourceGeneratorId;
        public MyModContext SourceContext;
        public EnvironmentPresetMapping[] Mappings;
    }


    internal sealed class EnvironmentPresetMapping
    {
        public string[] MaterialSubtypeNames;
        public byte[] SourceBiomes;
        public EnvironmentPresetItem[] Items;
        public float HeightMin;
        public float HeightMax;
        public float LatitudeMin;
        public float LatitudeMax;
        public float LongitudeMin;
        public float LongitudeMax;
        public float SlopeMin;
        public float SlopeMax;
    }


    internal sealed class EnvironmentPresetItem
    {
        public string Type;
        public string Subtype;
        public float Offset;
        public float Density;
    }


    internal sealed class RemappedEnvironmentPreset
    {
        public readonly List<RemappedEnvironmentMapping> Mappings =
            new List<RemappedEnvironmentMapping>();

        public readonly HashSet<string> MatchedMaterials =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public readonly HashSet<string> MissingTargetMaterials =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public readonly HashSet<string> MissingDefinitions =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public readonly Dictionary<byte, long> EmittedBiomePixels =
            new Dictionary<byte, long>();
    }


    internal sealed class RemappedEnvironmentMapping
    {
        public EnvironmentPresetMapping Source;
        public string MaterialSubtypeName;
        public byte[] TargetBiomes;
    }
}
