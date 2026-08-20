namespace VoxelCubemapApi.Common.Noise
{
    /// <summary>
    /// Minimal engine-owned 3D scalar-noise contract. Implementations are
    /// deterministic for a fixed configuration and do not depend on
    /// non-whitelisted VRage.Noise types.
    /// </summary>
    internal interface INoise3D
    {
        double Sample(
            double x,
            double y,
            double z);
    }
}
