namespace ProceduralCubemapApi.Common.Noise
{
    /// <summary>
    /// Controls how densely procedural noise is sampled across each cubemap
    /// face before brush application.
    /// </summary>
    public enum NoiseSamplingQuality
    {
        Low = 0,     // 129 x 129
        Medium = 1,  // 257 x 257
        High = 2,    // 513 x 513
        Direct = 3
    }
}
