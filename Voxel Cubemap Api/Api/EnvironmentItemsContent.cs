using Sandbox.ModAPI;

using System;
using System.IO;
using System.Linq;

using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;

namespace VoxelCubemapApi.Api
{
    /// <summary>
    /// Loads the vegetation/environment mappings from a client-owned XML
    /// PlanetGeneratorDefinition used only as a data carrier.
    /// </summary>
    public sealed class EnvironmentItemsContent
    {
        public PlanetEnvironmentItemMapping[] Mappings { get; private set; }
        public string Fingerprint { get; private set; }
        public string SourceFile { get; private set; }
        public string CarrierSubtype { get; private set; }


        public static EnvironmentItemsContent Load(
            MyModContext modContext,
            string relativeFile,
            string carrierSubtype)
        {
            if (modContext == null)
                throw new ArgumentNullException("modContext");

            if (string.IsNullOrWhiteSpace(relativeFile))
                throw new ArgumentException(
                    "Environment-item file path cannot be empty.",
                    "relativeFile");

            if (string.IsNullOrWhiteSpace(carrierSubtype))
                throw new ArgumentException(
                    "Carrier planet-definition subtype cannot be empty.",
                    "carrierSubtype");


            if (!MyAPIGateway.Utilities.FileExistsInModLocation(
                relativeFile,
                modContext.ModItem))
            {
                throw new Exception(
                    "Environment-item file is missing from the client mod: " +
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
                    "Environment-item file did not deserialize as Definitions: " +
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
                    "Environment-item file '" +
                    relativeFile +
                    "' does not contain PlanetGeneratorDefinition/" +
                    carrierSubtype +
                    ".");
            }

            if (carrier.EnvironmentItems == null ||
                carrier.EnvironmentItems.Length == 0)
            {
                throw new Exception(
                    "Environment-item carrier '" +
                    carrierSubtype +
                    "' contains no EnvironmentItems mappings.");
            }


            var result =
                new EnvironmentItemsContent
                {
                    Mappings =
                        carrier.EnvironmentItems,

                    Fingerprint =
                        StableTextId(
                            xml),

                    SourceFile =
                        relativeFile,

                    CarrierSubtype =
                        carrierSubtype
                };


            MyLog.Default.WriteLineAndConsole(
                "[VoxelCubemapApi] Loaded client environment items: " +
                "file='" +
                result.SourceFile +
                "', carrier='" +
                result.CarrierSubtype +
                "', mappings=" +
                result.Mappings.Length +
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
