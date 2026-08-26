using System;
using Generated;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ProceduralCubemapApi.Common.Api;
using ProceduralCubemapApi.Common.Configuration;
using ProceduralCubemapApi.Common.Networking;
using ProceduralCubemapApi.Common.PlanetModification;
using ProceduralCubemapApi.Common.PlanetModification.EnvironmentPresets;
using ProceduralCubemapApi.Common.PlanetModification.Persistence;
using ProceduralCubemapApi.Common.PlanetModification.Runtime;
using ProceduralCubemapApi.Common.PlanetModification.World;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace ProceduralCubemapApi.Common
{
    [APIManager(0x5643584150490001L, Provider = nameof(_intermodApi))]
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, -1000)]
    internal sealed partial class CubemapApiServer : MySessionComponentBase
    {
        private volatile bool _unloading;
        private volatile bool _worldStoragePathReady;
        private bool _workshopStoragePathNormalized;
        private bool _workshopStorageWarningLogged;
        private bool _workshopStorageFailureLogged;
        private int _workshopStorageRetryFrames;
        private CubemapIntermodApiServer _intermodApi;

        private CubemapApiConfig _config;
        private RuntimePackageStore _runtimePackages;
        private PlanetDataArchiveService _planetDataArchives;
        private PersistedEnvironmentRestorer _environmentRestorer;
        private PlanetModificationCoordinator _modifications;
        private RuntimeGeneratorRegistry _runtimeGenerators;
        private PlanetStorageService _planetStorage;
        private EnvironmentPresetCatalog _environmentPresets;
        private VoxelNetworkSession _network;

        private readonly VegetationClearScheduler _vegetationClearScheduler =
            new VegetationClearScheduler();

        internal static CubemapApiServer Instance { get; private set; }

        internal bool IsUnloading => _unloading;
        internal PlanetModificationCoordinator Modifications => _modifications;
        internal RuntimePackageStore RuntimePackages => _runtimePackages;


        internal void ReadLivePlanetProviderIdentity(
            MyPlanet planet,
            out long planetSeed,
            out string providerSubtype)
        {
            _planetStorage.ReadProviderIdentity(
                planet,
                out planetSeed,
                out providerSubtype);
        }


        internal string BuildWorldStorageFilePath(
            string savePath,
            string fileName)
        {
            return _runtimeGenerators.BuildWorldStoragePath(
                savePath,
                fileName);
        }


        internal string ResolveInitialSavePath()
        {
            return _runtimeGenerators.ResolveInitialSavePath();
        }


        internal MyPlanetGeneratorDefinition RegisterRuntimeGeneratorDefinition(
            MyObjectBuilder_PlanetGeneratorDefinition sourceBuilder,
            string subtype,
            string absolutePlanetDataFolder)
        {
            return _runtimeGenerators.RegisterDefinition(
                sourceBuilder,
                subtype,
                absolutePlanetDataFolder);
        }


        public override void LoadData()
        {
            Instance =
                this;

            _unloading =
                false;

            _worldStoragePathReady =
                false;

            _workshopStoragePathNormalized =
                false;

            _workshopStorageWarningLogged =
                false;

            _workshopStorageFailureLogged =
                false;

            _workshopStorageRetryFrames =
                0;

            _config =
                CubemapApiConfigStorage.LoadOrCreate();

            _runtimePackages =
                new RuntimePackageStore(
                    this,
                    _config);

            _planetDataArchives =
                new PlanetDataArchiveService(
                    _runtimePackages);

            _runtimeGenerators =
                new RuntimeGeneratorRegistry(
                    _runtimePackages,
                    (MyModContext)ModContext);

            _planetStorage =
                new PlanetStorageService(
                    _runtimePackages,
                    _vegetationClearScheduler);

            _environmentRestorer =
                new PersistedEnvironmentRestorer(
                    _runtimePackages,
                    _planetStorage);

            _environmentPresets =
                new EnvironmentPresetCatalog(
                    _runtimeGenerators);

            _network =
                new VoxelNetworkSession();

            _modifications =
                new PlanetModificationCoordinator(
                    _runtimePackages,
                    _planetDataArchives,
                    _runtimeGenerators,
                    _planetStorage,
                    _environmentPresets,
                    _network,
                    () => _unloading,
                    () => _worldStoragePathReady);

            _runtimePackages.ProceduralArchiveBuilder =
                _modifications.RebuildProceduralArchive;

            _runtimePackages.ProceduralGeneratorSignatureBuilder =
                _modifications.BuildProceduralGeneratorSignature;

            _runtimePackages.LoadPersistedRuntimeGenerators();

            _modifications.EnsureEmptyPlanetRuntimeAssets();

            _network.Init();

            _environmentRestorer.Reset();
            _vegetationClearScheduler.Clear();

            _intermodApi =
                new CubemapIntermodApiServer(
                    _modifications,
                    _environmentPresets);

            RegisterApiManager();

            if (MyAPIGateway.Session != null &&
                (!MyAPIGateway.Session.IsServer ||
                 !HasWorkshopSavePathTransition()))
            {
                _worldStoragePathReady =
                    true;
            }
        }


        public override void BeforeStart()
        {
            try
            {
                _runtimePackages.ReconcileRuntimePackagesWithLivePlanets();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("[RuntimePlanetGenerator] Startup persistence cleanup failed: " + e);
            }
        }

        private bool EnsureWorldStoragePathReady()
        {
            if (_worldStoragePathReady)
                return true;

            if (MyAPIGateway.Session == null)
                return false;

            if (!MyAPIGateway.Session.IsServer || !HasWorkshopSavePathTransition())
            {
                _worldStoragePathReady =
                    true;

                return true;
            }

            if (!_workshopStorageWarningLogged)
            {
                _workshopStorageWarningLogged =
                    true;

                string message =
                    "Workshop worlds can cause world-storage path issues during their first load. " +
                    "Normalizing the active save file before runtime archive requests begin.";

                MyLog.Default.Log(MyLogSeverity.Warning, "[RuntimePlanetGenerator] " + message);
                MyAPIGateway.Utilities.ShowMessage(nameof(ProceduralCubemapApi), message);
                var notification = MyAPIGateway.Utilities.CreateNotification("TFM Updating Voxel... Done!");
                notification.Show();
                
                
                notification.Hide();
                notification.ResetAliveTime();
                notification.Text += " Done!";
                notification.Show();
            }

            if (_workshopStorageRetryFrames > 0)
            {
                _workshopStorageRetryFrames--;

                return false;
            }

            string currentPath =
                RuntimeGeneratorRegistry.NormalizePath(
                    MyAPIGateway.Session.CurrentPath);

            int separatorIndex =
                currentPath == null
                    ? -1
                    : currentPath.LastIndexOf('/');

            string currentSaveName =
                separatorIndex < 0
                    ? currentPath
                    : currentPath.Substring(
                        separatorIndex + 1);

            if (!_workshopStoragePathNormalized)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(currentPath) ||
                        !MyAPIGateway.Session.Save(currentPath))
                    {
                        LogWorkshopStorageFailure(
                            "the initial save was rejected");

                        return false;
                    }
                }
                catch (Exception e)
                {
                    LogWorkshopStorageFailure(
                        "the initial save failed: " +
                        e.Message);

                    return false;
                }

                MyAPIGateway.Session.Name = currentSaveName;

                _workshopStoragePathNormalized =
                    true;
            }

            try
            {
                _runtimeGenerators.RebindToSavePath(
                    currentPath);

                _modifications.EnsureEmptyPlanetRuntimeAssets(
                    savePath: currentPath);
            }
            catch (Exception e)
            {
                LogWorkshopStorageFailure(
                    "runtime generator rebinding failed: " +
                    e);

                return false;
            }

            _worldStoragePathReady =
                true;

            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Workshop world-storage path " +
                "normalized to CurrentPath: " +
                currentPath);

            return true;
        }


        private void LogWorkshopStorageFailure(
            string reason)
        {
            _workshopStorageRetryFrames =
                60;

            if (_workshopStorageFailureLogged)
                return;

            _workshopStorageFailureLogged =
                true;

            MyLog.Default.Log(
                MyLogSeverity.Warning,
                "[RuntimePlanetGenerator] Could not normalize the " +
                "workshop world-storage path because " +
                reason +
                ". Runtime archive processing remains paused and will retry.");
        }


        private static bool HasWorkshopSavePathTransition()
        {
            const string workshopPrefix =
                "(Workshop) ";

            string sessionName =
                MyAPIGateway.Session.Name;

            string currentPath =
                RuntimeGeneratorRegistry.NormalizePath(
                    MyAPIGateway.Session.CurrentPath);

            if (string.IsNullOrWhiteSpace(sessionName) ||
                string.IsNullOrWhiteSpace(currentPath) ||
                !sessionName.StartsWith(
                    workshopPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int separatorIndex =
                currentPath.LastIndexOf('/');

            string currentSaveName =
                separatorIndex < 0
                    ? currentPath
                    : currentPath.Substring(
                        separatorIndex + 1);

            string promotedSaveName =
                sessionName.Substring(
                    workshopPrefix.Length)
                    .Replace(
                        ':',
                        '-');

            return string.Equals(
                currentSaveName,
                promotedSaveName,
                StringComparison.OrdinalIgnoreCase);
        }

        protected override void UnloadData()
        {
            _unloading =
                true;

            if (_network != null)
            {
                _network.Dispose();

                _network =
                    null;
            }

            _modifications?.ClosePlanetMetadataProviders();

            _vegetationClearScheduler.Clear();

            UnregisterApiManager();


            _intermodApi =
                null;


            _runtimePackages?.ClearWorldStorageCache();

            _runtimePackages =
                null;

            _config =
                null;

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }


        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session == null)
                return;

            if (!EnsureWorldStoragePathReady())
                return;

            _modifications.UpdatePlanetMetadataProviders();

            _vegetationClearScheduler.Update();

            // Runtime generator state is owned by the background request while
            // it is active.  Rebinding resumes after its simulation-thread
            // completion callback.
            if (_modifications.RequestInProgress)
                return;

            _environmentRestorer.Update();

            var currentPath =
                RuntimeGeneratorRegistry.NormalizePath(
                    MyAPIGateway.Session.CurrentPath);

            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            if (string.Equals(
                currentPath,
                _runtimePackages.BoundSavePath,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                _runtimeGenerators.RebindToSavePath(
                    currentPath);

                _modifications.EnsureEmptyPlanetRuntimeAssets(
                    savePath: currentPath);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Save-path rebind failed: " + e);
            }
        }




    }
}
