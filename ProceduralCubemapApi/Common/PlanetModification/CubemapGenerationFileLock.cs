using System;
using Adk.Utils;
using Sandbox.ModAPI;
using VRage.Utils;

namespace ProceduralCubemapApi.Common.PlanetModification
{
    internal static class CubemapGenerationFileLock
    {
        internal const string FileName = "cubmapgenerator.lock";
        private static readonly object Sync = new object();
        private static int _active, _generation;

        internal static IDisposable Begin(string operation)
        {
            lock (Sync)
            {
                if (_active++ == 0)
                {
                    try
                    {
                        if (MyAPIGateway.Utilities != null)
                            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(
                                FileName, typeof(CubemapApiServer)))
                            {
                                writer.WriteLine("started=" + DateTime.UtcNow.ToString("O"));
                                writer.WriteLine("operation=" + operation);
                            }
                    }
                    catch (Exception error)
                    {
                        LogHelper.Log(
                            MyLogSeverity.Warning,
                            "Could not create " + FileName + ": " + error);
                    }
                }
                return new Scope(_generation);
            }
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                ++_generation;
                _active = 0;
                DeleteMarker();
            }
        }

        private static void DeleteMarker()
        {
            try
            {
                if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.FileExistsInLocalStorage(
                    FileName, typeof(CubemapApiServer)))
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(FileName, typeof(CubemapApiServer));
            }
            catch (Exception error)
            {
                LogHelper.Log(
                    MyLogSeverity.Warning,
                    "Could not remove " + FileName + ": " + error);
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly int _generation;
            private bool _disposed;

            internal Scope(int generation) { _generation = generation; }

            public void Dispose()
            {
                lock (Sync)
                {
                    if (_disposed) return;
                    _disposed = true;
                    if (_generation == CubemapGenerationFileLock._generation && --_active == 0)
                        DeleteMarker();
                }
            }
        }
    }
}
