using System;
using System.Linq;
using VRage.Game;

namespace VoxelCubemapApi.Common.PlanetModification.Runtime
{
    internal static class PlanetMaterialMap
    {
        internal static bool UsesValue(
            MyObjectBuilder_PlanetGeneratorDefinition builder,
            byte mapValue)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

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


        internal static byte AllocateValue(
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
                    "No free material-map byte remains for API material allocation.");
            }


            byte value =
                (byte)nextCandidate;

            usedValues[nextCandidate] =
                true;

            nextCandidate++;

            return value;
        }


    }
}
