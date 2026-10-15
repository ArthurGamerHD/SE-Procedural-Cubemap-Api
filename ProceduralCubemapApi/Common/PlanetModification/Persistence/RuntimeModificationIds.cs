using Sandbox.ModAPI;

namespace ProceduralCubemapApi.Common.PlanetModification.Persistence
{
    internal static class RuntimeModificationIds
    {
        /// <summary>
        /// Gives every revision without an identity a deterministic legacy id,
        /// so server and clients agree without exchanging it. The position
        /// keeps ids unique within the recipe; once persisted they no longer
        /// depend on it.
        /// </summary>
        internal static void Ensure(RuntimeProceduralPlanetRecipe recipe)
        {
            if (recipe == null || recipe.Revisions == null)
                return;
            for (int i = 0; i < recipe.Revisions.Count; i++)
            {
                RuntimeProceduralRevision revision = recipe.Revisions[i];
                if (revision == null || !string.IsNullOrWhiteSpace(revision.ModificationId))
                    continue;
                string content = MyAPIGateway.Utilities.SerializeToXML(revision);
                revision.ModificationId = "legacy-" + i + "-" + StableIdentifier.Create(content);
            }
        }
    }
}
