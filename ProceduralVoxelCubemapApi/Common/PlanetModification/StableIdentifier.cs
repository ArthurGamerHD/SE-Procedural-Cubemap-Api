namespace VoxelCubemapApi.Common.PlanetModification
{
    internal static class StableIdentifier
    {
        internal static string Create(
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


    }
}
