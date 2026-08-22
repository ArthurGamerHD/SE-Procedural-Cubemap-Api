namespace ProceduralCubemapApi.Common.Noise
{
    /// <summary>
    /// Shape used by a radial field between its center and outer radius.
    /// Most profiles sample [0,1]. Crater is signed: negative values carve the
    /// bowl and positive values raise the rim.
    /// </summary>
    public enum RadialFieldProfile
    {
        Linear = 0,
        Smooth = 1,
        Bowl = 2,
        Crater = 3
    }
}
