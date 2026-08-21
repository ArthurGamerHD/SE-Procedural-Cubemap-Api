using System;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace VoxelCubemapExampleMod
{
    /// <summary>
    /// Loads a named complex planet-material group from an XML Definitions file
    /// owned by an API client mod.
    /// </summary>
    public sealed class MaterialRulesContent
    {
        public MyPlanetMaterialGroup MaterialGroup { get; private set; }
        public string Fingerprint { get; private set; }
        public string SourceFile { get; private set; }
        public string CarrierSubtype { get; private set; }
        public string MaterialGroupName { get; private set; }


        public static MaterialRulesContent Load(
            MyModContext modContext,
            string relativeFile,
            string carrierSubtype,
            string materialGroupName)
        {
            if (modContext == null)
                throw new ArgumentNullException(nameof(modContext));

            if (string.IsNullOrWhiteSpace(relativeFile))
                throw new ArgumentException(
                    "Material-rule file path cannot be empty.",
                    nameof(relativeFile));

            if (string.IsNullOrWhiteSpace(carrierSubtype))
                throw new ArgumentException(
                    "Carrier planet-definition subtype cannot be empty.",
                    nameof(carrierSubtype));

            if (string.IsNullOrWhiteSpace(materialGroupName))
                throw new ArgumentException(
                    "Material-group name cannot be empty.",
                    nameof(materialGroupName));


            if (!MyAPIGateway.Utilities.FileExistsInModLocation(
                relativeFile,
                modContext.ModItem))
            {
                throw new Exception(
                    "Material-rule file is missing from the client mod: " +
                    relativeFile);
            }


            string xml;

            using (TextReader reader =
                MyAPIGateway.Utilities.ReadFileInModLocation(
                    relativeFile,
                    modContext.ModItem))
            {
                xml =
                    reader.ReadToEnd();
            }


            MyObjectBuilder_Definitions definitions =
                MyAPIGateway.Utilities
                    .SerializeFromXML<MyObjectBuilder_Definitions>(
                        xml);


            if (definitions == null)
            {
                throw new Exception(
                    "Material-rule file did not deserialize as Definitions: " +
                    relativeFile);
            }


            MyObjectBuilder_PlanetGeneratorDefinition carrier =
                null;


            if (definitions.Definitions != null)
            {
                carrier =
                    definitions.Definitions
                        .OfType<MyObjectBuilder_PlanetGeneratorDefinition>()
                        .FirstOrDefault(x =>
                            x != null &&
                            string.Equals(
                                x.Id.SubtypeName,
                                carrierSubtype,
                                StringComparison.OrdinalIgnoreCase));
            }


            if (carrier == null &&
                definitions.PlanetGeneratorDefinitions != null)
            {
                carrier =
                    definitions.PlanetGeneratorDefinitions
                        .FirstOrDefault(x =>
                            x != null &&
                            string.Equals(
                                x.Id.SubtypeName,
                                carrierSubtype,
                                StringComparison.OrdinalIgnoreCase));
            }


            if (carrier == null)
            {
                throw new Exception(
                    "Material-rule file '" +
                    relativeFile +
                    "' does not contain PlanetGeneratorDefinition/" +
                    carrierSubtype +
                    ".");
            }


            MyPlanetMaterialGroup materialGroup =
                carrier.ComplexMaterials == null
                    ? null
                    : carrier.ComplexMaterials
                        .FirstOrDefault(x =>
                            x != null &&
                            string.Equals(
                                x.Name,
                                materialGroupName,
                                StringComparison.OrdinalIgnoreCase));


            if (materialGroup == null)
            {
                throw new Exception(
                    "Material-rule carrier '" +
                    carrierSubtype +
                    "' does not contain ComplexMaterials/MaterialGroup named '" +
                    materialGroupName +
                    "'.");
            }


            if (materialGroup.MaterialRules == null ||
                materialGroup.MaterialRules.Length == 0)
            {
                throw new Exception(
                    "Material group '" +
                    materialGroupName +
                    "' contains no material rules.");
            }


            for (int i = 0;
                i < materialGroup.MaterialRules.Length;
                i++)
            {
                MyPlanetMaterialPlacementRule rule =
                    materialGroup.MaterialRules[i];


                if (rule == null ||
                    string.IsNullOrWhiteSpace(
                        rule.FirstOrDefault))
                {
                    throw new Exception(
                        "Material group '" +
                        materialGroupName +
                        "' rule " +
                        i +
                        " contains no material/layer data.");
                }
            }


            var result =
                new MaterialRulesContent
                {
                    MaterialGroup =
                        materialGroup,

                    Fingerprint =
                        StableTextId(
                            xml),

                    SourceFile =
                        relativeFile,

                    CarrierSubtype =
                        carrierSubtype,

                    MaterialGroupName =
                        materialGroupName
                };


            MyLog.Default.WriteLineAndConsole(
                "[VoxelCubemapApi] Loaded client material rules: " +
                "file='" +
                result.SourceFile +
                "', carrier='" +
                result.CarrierSubtype +
                "', group='" +
                result.MaterialGroupName +
                "', rules=" +
                materialGroup.MaterialRules.Length +
                ", fingerprint=" +
                result.Fingerprint +
                ".");


            return result;
        }


        private static string StableTextId(
            string text)
        {
            unchecked
            {
                uint hash =
                    2166136261;


                if (text != null)
                {
                    for (int i = 0;
                        i < text.Length;
                        i++)
                    {
                        hash ^=
                            text[i];

                        hash *=
                            16777619;
                    }
                }


                return hash.ToString("X8");
            }
        }
    }
}
