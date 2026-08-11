using System;

using Generated;

using VoxelCubemapApi.Server.PlanetModification.Maps;

namespace VoxelCubemapApi.Server.Api
{
    /// <summary>
    /// Root-level procedural noise service exposed through the intermod API.
    /// This keeps deterministic noise math and percentile sampling owned by
    /// the API server instead of duplicating it in client commands.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "VoxelCubemapApi.Api",
        ClientName = "NoiseProvider")]
    internal sealed partial class ProceduralNoiseProvider
    {
        /// <summary>
        /// Computes the brush blend threshold corresponding to the requested
        /// coverage for an arbitrary brush fBm profile. The result is in the
        /// same normalized [0,1] space used by ApplyBrush blend-noise limits.
        /// </summary>
        [ApiMethod]
        private double FractalBrownianMotionCoverage(
            long planetSeed,
            double frequency,
            int octaves,
            int seedOffset,
            int coveragePercent)
        {
            return FractalBrownianMotion.ComputeBrushCoverageThreshold(
                planetSeed,
                frequency,
                octaves,
                seedOffset,
                coveragePercent);
        }
    }
}
