using System;

namespace VoxelCubemapApi.Server.PlanetModification.Maps
{
    internal static class PlanetMapFileNames
    {
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


    }
}
