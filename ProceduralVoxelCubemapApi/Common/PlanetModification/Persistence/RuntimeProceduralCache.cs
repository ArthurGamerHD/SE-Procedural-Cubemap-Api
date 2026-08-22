using System;
using System.Text;
using Sandbox.ModAPI;

namespace VoxelCubemapApi.Common.PlanetModification.Persistence
{
    internal static class RuntimeProceduralCache
    {
        internal const string CACHE_GUID = "21d3c440-3644-4c32-869e-755541b10120";

        internal const string ARCHIVE_MANIFEST_FILE = "__VoxelCubemapApiCache.xml";

        private const string ZIP_COMMENT_PREFIX = "PVCAPI:";


        internal static string ZipComment => ZIP_COMMENT_PREFIX + CACHE_GUID;


        internal static RuntimeProceduralCacheManifest CreateManifest(
            string generatorSignature,
            RuntimeProceduralPlanetRecipe recipe)
        {
            if (string.IsNullOrWhiteSpace(generatorSignature))
            {
                throw new ArgumentException(
                    "Generator signature cannot be empty.",
                    nameof(generatorSignature));
            }

            return new RuntimeProceduralCacheManifest
            {
                CacheGuid = CACHE_GUID,
                GeneratorSignature = generatorSignature,
                RecipeSignature = ComputeRecipeSignature(recipe)
            };
        }


        internal static string ComputeRecipeSignature(
            RuntimeProceduralPlanetRecipe recipe)
        {
            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            string xml =
                MyAPIGateway.Utilities
                    .SerializeToXML<RuntimeProceduralPlanetRecipe>(
                        recipe);

            using (RuntimeProceduralCacheSignatureBuilder signature =
                CreateSignatureBuilder())
            {
                signature.AppendText(
                    "procedural-recipe",
                    xml);

                return signature.Finish();
            }
        }

        internal static RuntimeProceduralCacheSignatureBuilder
            CreateSignatureBuilder()
        {
            return new RuntimeProceduralCacheSignatureBuilder();
        }

        internal sealed class RuntimeProceduralCacheSignatureBuilder :
            IDisposable
        {
            private const ulong FNV_OFFSET_BASIS =
                14695981039346656037UL;

            private const ulong FNV_PRIME =
                1099511628211UL;

            private ulong _hash =
                FNV_OFFSET_BASIS;

            private bool _finished;


            internal void AppendText(
                string name,
                string value)
            {
                AppendBytes(
                    name,
                    Encoding.UTF8.GetBytes(
                        value ?? string.Empty));
            }


            internal void AppendBytes(
                string name,
                byte[] data)
            {
                if (_finished)
                {
                    throw new InvalidOperationException(
                        "Signature builder is already finalized.");
                }

                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                if (data == null)
                    throw new ArgumentNullException(nameof(data));

                byte[] nameBytes =
                    Encoding.UTF8.GetBytes(
                        name);

                AppendInt32(
                    nameBytes.Length);

                Transform(
                    nameBytes);

                AppendInt32(
                    data.Length);

                Transform(
                    data);
            }


            internal string Finish()
            {
                if (_finished)
                {
                    throw new InvalidOperationException(
                        "Signature builder is already finalized.");
                }

                _finished =
                    true;

                return _hash.ToString("x16");
            }


            public void Dispose()
            {
            }


            private void AppendInt32(
                int value)
            {
                byte[] bytes =
                new byte[4]
                {
                    (byte)value,
                    (byte)(value >> 8),
                    (byte)(value >> 16),
                    (byte)(value >> 24)
                };

                Transform(
                    bytes);
            }


            private void Transform(
                byte[] data)
            {
                if (data.Length == 0)
                    return;

                unchecked
                {
                    for (int i = 0;
                        i < data.Length;
                        i++)
                    {
                        _hash ^=
                            data[i];

                        _hash *=
                            FNV_PRIME;
                    }
                }
            }
        }
    }
}
