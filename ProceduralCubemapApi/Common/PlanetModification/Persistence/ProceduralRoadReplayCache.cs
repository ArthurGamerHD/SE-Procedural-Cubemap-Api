using System;
using System.Collections.Generic;

namespace ProceduralCubemapApi.Common.PlanetModification.Persistence
{
    // Derived, session-only acceleration. The XML recipe remains authoritative;
    // a miss (including reload/rollback/terrain edits) uses the cold cell replay.
    internal sealed class ProceduralRoadReplayCache
    {
        private const long MaximumBytes = 128L * 1024 * 1024;
        private readonly object _sync = new object();
        private readonly List<Entry> _entries = new List<Entry>();

        internal sealed class Entry
        {
            internal long PlanetId, Bytes;
            internal string SourceSignature, RecipeSignature;
            internal int RevisionCount;
            internal Dictionary<string, byte[]> BaseFiles, ResultFiles;
        }

        internal Entry Find(long planetId, string sourceSignature, RuntimeProceduralPlanetRecipe recipe)
        {
            Entry entry = null;
            lock (_sync)
                foreach (Entry candidate in _entries)
                    if (candidate.PlanetId == planetId && candidate.SourceSignature == sourceSignature)
                    { entry = candidate; break; }
            if (entry == null || recipe.Revisions.Count < entry.RevisionCount) return null;
            for (int i = entry.RevisionCount; i < recipe.Revisions.Count; i++)
                if (!IsRoadOnly(recipe.Revisions[i])) return null;
            var prefix = new RuntimeProceduralPlanetRecipe
            {
                SchemaVersion = recipe.SchemaVersion, Source = recipe.Source,
                PlanetSeed = recipe.PlanetSeed, NoiseVersion = recipe.NoiseVersion,
                Revisions = recipe.Revisions.GetRange(0, entry.RevisionCount)
            };
            return RuntimeProceduralCache.ComputeRecipeSignature(prefix) == entry.RecipeSignature
                ? entry : null;
        }

        internal static bool IsRoadOnly(RuntimeProceduralRevision revision)
        {
            return revision.Brushes.Count == 0 && revision.Features.Count == 0 &&
                   revision.BiomeReplacements.Count == 0 && revision.FractalNoise.Count == 0 &&
                   string.IsNullOrWhiteSpace(revision.EnvironmentPresetName) &&
                   revision.EnvironmentRemap.Count == 0;
        }

        internal void Store(long planetId, string sourceSignature, RuntimeProceduralPlanetRecipe recipe,
            Dictionary<string, byte[]> baseFiles, Dictionary<string, byte[]> resultFiles)
        {
            long bytes = 0;
            foreach (byte[] data in baseFiles.Values) bytes += data.Length;
            foreach (var pair in resultFiles)
            {
                byte[] original;
                if (!baseFiles.TryGetValue(pair.Key, out original) || !ReferenceEquals(original, pair.Value))
                    bytes += pair.Value.Length;
            }
            if (bytes > MaximumBytes) return;
            var entry = new Entry
            {
                PlanetId = planetId, SourceSignature = sourceSignature, Bytes = bytes,
                RecipeSignature = RuntimeProceduralCache.ComputeRecipeSignature(recipe),
                RevisionCount = recipe.Revisions.Count,
                BaseFiles = new Dictionary<string, byte[]>(baseFiles, StringComparer.OrdinalIgnoreCase),
                ResultFiles = new Dictionary<string, byte[]>(resultFiles, StringComparer.OrdinalIgnoreCase)
            };
            lock (_sync)
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                    if (_entries[i].PlanetId == planetId) _entries.RemoveAt(i);
                long total = bytes;
                foreach (Entry existing in _entries) total += existing.Bytes;
                while (_entries.Count > 0 && (total > MaximumBytes || _entries.Count >= 2))
                {
                    total -= _entries[0].Bytes;
                    _entries.RemoveAt(0);
                }
                _entries.Add(entry);
            }
        }
    }
}
