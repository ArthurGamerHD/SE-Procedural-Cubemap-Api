using System;
using System.Collections.Generic;

using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;

namespace VoxelCubemapApi.Server.PlanetModification.Templates
{
    /// <summary>
    /// Composes the API exposed for one modification template. Additional
    /// server-owned providers can contribute delegates without adding API
    /// composition responsibilities to the template itself.
    /// </summary>
    internal sealed class PlanetModificationTemplateApi
    {
        private readonly PlanetModificationTemplate _template;


        internal PlanetModificationTemplateApi(
            PlanetModificationTemplate template)
        {
            if (template == null)
                throw new ArgumentNullException("template");

            _template =
                template;
        }


        internal ApiData GetApi(
            params Func<ApiData>[] additionalApiProviders)
        {
            var api =
                new ApiData();

            Merge(
                api,
                _template.GetApi(),
                "PlanetModificationTemplate");

            if (additionalApiProviders == null)
                return api;

            for (int i = 0;
                i < additionalApiProviders.Length;
                i++)
            {
                Func<ApiData> provider =
                    additionalApiProviders[i];

                if (provider == null)
                {
                    throw new ArgumentException(
                        "Additional API provider " +
                        i +
                        " is null.",
                        "additionalApiProviders");
                }

                Merge(
                    api,
                    provider(),
                    "additionalApiProviders[" +
                    i +
                    "]");
            }

            return api;
        }


        private static void Merge(
            ApiData destination,
            ApiData source,
            string sourceName)
        {
            if (source == null)
            {
                throw new Exception(
                    "API provider '" +
                    sourceName +
                    "' returned null.");
            }

            foreach (KeyValuePair<string, Delegate> entry in source)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new Exception(
                        "API provider '" +
                        sourceName +
                        "' exposed an empty delegate name.");
                }

                if (entry.Value == null)
                {
                    throw new Exception(
                        "API provider '" +
                        sourceName +
                        "' exposed null for '" +
                        entry.Key +
                        "'.");
                }

                if (destination.ContainsKey(
                    entry.Key))
                {
                    throw new Exception(
                        "API delegate '" +
                        entry.Key +
                        "' was exposed more than once while merging '" +
                        sourceName +
                        "'.");
                }

                destination.Add(
                    entry.Key,
                    entry.Value);
            }
        }
    }
}
