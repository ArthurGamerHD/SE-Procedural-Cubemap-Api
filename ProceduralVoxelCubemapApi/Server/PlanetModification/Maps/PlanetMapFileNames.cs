using System;

namespace VoxelCubemapApi.Server.PlanetModification.Maps
{
    internal static class PlanetMapFileNames
    {
        internal static readonly string[] All =
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


        internal static string Validate(
            string faceFileName)
        {
            if (string.IsNullOrWhiteSpace(
                faceFileName))
            {
                throw new ArgumentException(
                    "Planet PNG filename cannot be empty.",
                    "faceFileName");
            }

            for (int i = 0;
                i < All.Length;
                i++)
            {
                if (string.Equals(
                    All[i],
                    faceFileName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }

            throw new ArgumentException(
                "Unsupported planet PNG filename: " +
                faceFileName,
                "faceFileName");
        }


    }
}
