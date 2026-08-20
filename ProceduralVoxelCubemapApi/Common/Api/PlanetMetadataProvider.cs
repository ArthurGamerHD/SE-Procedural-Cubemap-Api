using System;
using System.Collections.Generic;
using Adk.Image.Png;
using Generated;
using VoxelCubemapApi.Common.PlanetModification;
using VoxelCubemapApi.Common.PlanetModification.Maps;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace VoxelCubemapApi.Common.Api
{
    /// <summary>
    /// Per-caller handle to a shared, immutable cubemap snapshot.
    /// </summary>
    [ApiProvider(
        ClientNamespace = "VoxelCubemapApi.Api",
        ClientName = "PlanetMetadataProvider")]
    internal sealed partial class PlanetMetadataProvider
    {
        private readonly PlanetModificationCoordinator _coordinator;
        private readonly PlanetMetadataSnapshot _snapshot;
        private readonly object _sync =
            new object();

        private readonly List<Action<long, string>> _callbacks =
            new List<Action<long, string>>();

        private bool _closed;


        internal long PlanetEntityId => _snapshot.PlanetEntityId;

        internal string ProviderSubtype => _snapshot.ProviderSubtype;

        internal PlanetMetadataSnapshot Snapshot => _snapshot;


        internal PlanetMetadataProvider(
            PlanetModificationCoordinator coordinator,
            PlanetMetadataSnapshot snapshot)
        {
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));

            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            _coordinator =
                coordinator;

            _snapshot =
                snapshot;
        }


        /// <summary>
        /// Subscribes this handle to replacement or unavailability of its
        /// planet. The callback receives the entity ID and new runtime subtype;
        /// a null subtype means the planet became unavailable.
        /// </summary>
        [ApiMethod]
        private bool SubscribeRuntimePlanetChanged(
            Action<long, string> callback)
        {
            if (callback == null)
                return false;

            lock (_sync)
            {
                EnsureOpen();

                if (_callbacks.Contains(
                    callback))
                {
                    return false;
                }

                if (!_coordinator.SubscribeRuntimePlanetChanged(
                    PlanetEntityId,
                    callback))
                {
                    return false;
                }

                _callbacks.Add(
                    callback);

                return true;
            }
        }


        /// <summary>
        /// Removes a callback previously registered by this handle.
        /// </summary>
        [ApiMethod]
        private void UnsubscribeRuntimePlanetChanged(
            Action<long, string> callback)
        {
            if (callback == null)
                return;

            lock (_sync)
            {
                if (!_callbacks.Remove(
                    callback))
                {
                    return;
                }

                _coordinator.UnsubscribeRuntimePlanetChanged(
                    PlanetEntityId,
                    callback);
            }
        }


        /// <summary>
        /// Returns the width and height shared by the height, material, and
        /// biome arrays for one cubemap face.
        /// </summary>
        [ApiMethod]
        private int[] GetFaceSize(
            string face)
        {
            lock (_sync)
            {
                EnsureOpen();

                return _snapshot.GetFaceSize(
                    face);
            }
        }


        /// <summary>
        /// Loads one heightmap face as unsigned 16-bit samples.
        /// </summary>
        [ApiMethod]
        private ushort[] LoadHeightFace(
            string face)
        {
            lock (_sync)
            {
                EnsureOpen();

                return _snapshot.LoadHeightFace(
                    face);
            }
        }


        /// <summary>
        /// Loads the material (red) channel of one material-map face.
        /// </summary>
        [ApiMethod]
        private byte[] LoadMaterialFace(
            string face)
        {
            lock (_sync)
            {
                EnsureOpen();

                return _snapshot.LoadMaterialFace(
                    face);
            }
        }


        /// <summary>
        /// Loads the biome (green) channel of one material-map face.
        /// </summary>
        [ApiMethod]
        private byte[] LoadBiomeFace(
            string face)
        {
            lock (_sync)
            {
                EnsureOpen();

                return _snapshot.LoadBiomeFace(
                    face);
            }
        }


        /// <summary>
        /// Closes only this caller's handle and subscriptions. The shared
        /// snapshot remains alive while another handle still uses it.
        /// </summary>
        [ApiMethod]
        private void Close()
        {
            CloseCore(
                true);
        }


        internal void CloseFromCoordinator()
        {
            CloseCore(
                false);
        }


        internal bool IsClosed
        {
            get
            {
                lock (_sync)
                {
                    return _closed;
                }
            }
        }


        private void CloseCore(
            bool releaseHandle)
        {
            Action<long, string>[] callbacks;

            lock (_sync)
            {
                if (_closed)
                    return;

                _closed =
                    true;

                callbacks =
                    _callbacks.ToArray();

                _callbacks.Clear();
            }

            for (int index = 0;
                index < callbacks.Length;
                index++)
            {
                _coordinator.UnsubscribeRuntimePlanetChanged(
                    PlanetEntityId,
                    callbacks[index]);
            }

            if (releaseHandle)
            {
                _coordinator.ReleasePlanetMetadataHandler(
                    this);
            }
        }


        private void EnsureOpen()
        {
            if (_closed)
            {
                throw new InvalidOperationException(
                    "Planet metadata provider handle is closed.");
            }
        }
    }


    /// <summary>
    /// Shared immutable image data for every active handle to one planet
    /// revision.
    /// </summary>
    internal sealed class PlanetMetadataSnapshot
    {
        private readonly object _sync =
            new object();

        private readonly Dictionary<string, byte[]> _pngFiles;

        private readonly Dictionary<string, PlanarPngBitmap> _images =
            new Dictionary<string, PlanarPngBitmap>(
                StringComparer.OrdinalIgnoreCase);

        private bool _closed;


        internal long PlanetEntityId { get; private set; }
        internal string ProviderSubtype { get; private set; }


        internal PlanetMetadataSnapshot(
            long planetEntityId,
            string providerSubtype,
            Dictionary<string, byte[]> pngFiles)
        {
            if (string.IsNullOrWhiteSpace(providerSubtype))
                throw new ArgumentException(
                    "Provider subtype cannot be empty.",
                    nameof(providerSubtype));

            if (pngFiles == null)
                throw new ArgumentNullException(nameof(pngFiles));

            PlanetEntityId =
                planetEntityId;

            ProviderSubtype =
                providerSubtype;

            _pngFiles =
                pngFiles;
        }


        internal bool IsClosed
        {
            get
            {
                lock (_sync)
                {
                    return _closed;
                }
            }
        }


        internal int[] GetFaceSize(
            string face)
        {
            PlanarPngBitmap image =
                LoadImage(
                    BuildFaceFileName(
                        face,
                        false));

            PlanarPngBitmap materialImage =
                LoadImage(
                    BuildFaceFileName(
                        face,
                        true));

            if (image.Width != materialImage.Width ||
                image.Height != materialImage.Height)
            {
                throw new InvalidDataException(
                    "Planet height and material cubemap face '" +
                    face +
                    "' dimensions differ (" +
                    image.Width +
                    "x" +
                    image.Height +
                    " versus " +
                    materialImage.Width +
                    "x" +
                    materialImage.Height +
                    ").");
            }

            return new[]
            {
                image.Width,
                image.Height
            };
        }


        internal ushort[] LoadHeightFace(
            string face)
        {
            string fileName =
                BuildFaceFileName(
                    face,
                    false);

            PlanarPngBitmap image =
                LoadImage(
                    fileName);

            if (image.BitDepth != 16 ||
                image.ColorType != 0 ||
                image.Planes == null ||
                image.Planes.Length < 2)
            {
                throw new Exception(
                    "Planet heightmap face is not 16-bit grayscale: " +
                    fileName +
                    ".");
            }

            byte[] highBytes =
                image.Planes[0];

            byte[] lowBytes =
                image.Planes[1];

            if (highBytes.Length != lowBytes.Length)
            {
                throw new Exception(
                    "Planet heightmap byte planes have different lengths: " +
                    fileName +
                    ".");
            }

            var samples =
                new ushort[highBytes.Length];

            for (int index = 0;
                index < samples.Length;
                index++)
            {
                samples[index] =
                    (ushort)((highBytes[index] << 8) |
                        lowBytes[index]);
            }

            return samples;
        }


        internal byte[] LoadMaterialFace(
            string face)
        {
            return ClonePlane(
                LoadImage(
                    BuildFaceFileName(
                        face,
                        true)),
                0,
                "material");
        }


        internal byte[] LoadBiomeFace(
            string face)
        {
            return ClonePlane(
                LoadImage(
                    BuildFaceFileName(
                        face,
                        true)),
                1,
                "biome");
        }


        internal void Close()
        {
            lock (_sync)
            {
                if (_closed)
                    return;

                _closed =
                    true;

                _images.Clear();
                _pngFiles.Clear();
            }
        }


        private PlanarPngBitmap LoadImage(
            string fileName)
        {
            lock (_sync)
            {
                EnsureOpen();

                PlanarPngBitmap image;

                if (_images.TryGetValue(
                    fileName,
                    out image))
                {
                    return image;
                }

                byte[] png;

                if (!_pngFiles.TryGetValue(
                    fileName,
                    out png))
                {
                    throw new Exception(
                        "Planet metadata snapshot is missing PNG '" +
                        fileName +
                        "'.");
                }

                image =
                    PlanetMapOperations.DecodePlanetPng(
                        fileName,
                        png);

                if (image.Width != image.Height)
                {
                    throw new InvalidDataException(
                        "Planet cubemap PNG '" +
                        fileName +
                        "' is not square (" +
                        image.Width +
                        "x" +
                        image.Height +
                        ").");
                }

                _images.Add(
                    fileName,
                    image);

                return image;
            }
        }


        private void EnsureOpen()
        {
            if (_closed)
            {
                throw new InvalidOperationException(
                    "Planet metadata snapshot is closed.");
            }
        }


        private static byte[] ClonePlane(
            PlanarPngBitmap image,
            int planeIndex,
            string channelName)
        {
            if (image.Planes == null ||
                planeIndex < 0 ||
                planeIndex >= image.Planes.Length ||
                image.Planes[planeIndex] == null)
            {
                throw new Exception(
                    "Planet material map has no " +
                    channelName +
                    " channel.");
            }

            byte[] source =
                image.Planes[planeIndex];

            var result =
                new byte[source.Length];

            Buffer.BlockCopy(
                source,
                0,
                result,
                0,
                source.Length);

            return result;
        }


        private static string BuildFaceFileName(
            string face,
            bool materialMap)
        {
            if (string.IsNullOrWhiteSpace(face))
            {
                throw new ArgumentException(
                    "Cubemap face cannot be empty.",
                    nameof(face));
            }

            string normalized =
                face.Trim();

            if (normalized.EndsWith(
                "_mat.png",
                StringComparison.OrdinalIgnoreCase))
            {
                normalized =
                    normalized.Substring(
                        0,
                        normalized.Length -
                        "_mat.png".Length);
            }
            else if (normalized.EndsWith(
                ".png",
                StringComparison.OrdinalIgnoreCase))
            {
                normalized =
                    normalized.Substring(
                        0,
                        normalized.Length -
                        ".png".Length);
            }

            return PlanetMapFileNames.Validate(
                normalized +
                (materialMap
                    ? "_mat.png"
                    : ".png"));
        }
    }
}
