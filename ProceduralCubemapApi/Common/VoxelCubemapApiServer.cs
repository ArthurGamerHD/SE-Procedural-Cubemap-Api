using System;
using System.Collections.Generic;
using Generated;
using Sandbox.Definitions;
using Sandbox.Game;
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
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

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
        private bool _worldStorageFirstFrameSelfTestPending;
        private bool _worldStorageVerificationFailed;
        private bool _playerConnectedSubscribed;
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
            CubemapGenerationFileLock.Reset();

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

            _worldStorageFirstFrameSelfTestPending =
                true;

            _worldStorageVerificationFailed =
                false;

            _playerConnectedSubscribed =
                MyAPIGateway.Session != null &&
                MyAPIGateway.Session.IsServer;

            if (_playerConnectedSubscribed)
                MyVisualScriptLogicProvider.PlayerConnected += OnPlayerConnected;

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

            // Phase 1: provisional verification during LoadData(). Persisted
            // runtime definitions created below should receive a usable physical
            // ModPath whenever the engine's world-storage destination can already
            // be resolved. The first simulation frame performs the authoritative
            // second verification because workshop/Torch startup may rebind it.
            if (MyAPIGateway.Session != null &&
                MyAPIGateway.Session.IsServer)
            {
                try
                {
                    _runtimeGenerators.RunWorldStorageSelfTest(
                        _runtimeGenerators.ResolveInitialSavePath(),
"initial setup");
                }
                catch (Exception e)
                {
                    MyLog.Default.Log(
                        MyLogSeverity.Warning,
"[RuntimePlanetGenerator] Initial world-storage self-test "+
"threw an exception; first-frame verification will retry: "+
                        e.Message);
                }
            }

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
            if (MyAPIGateway.Session == null ||
                !MyAPIGateway.Session.IsServer)
            {
                return;
            }


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
                if (!TryRunWorldStorageSelfTest(
                        currentPath,
                        "workshop save-path normalization"))
                {
                    MyLog.Default.Log(MyLogSeverity.Warning,
                        "[RuntimePlanetGenerator] World storage could not be " +
                        "verified after the initial workshop save; " +
                        "initialization will continue.");
                }

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
            CubemapGenerationFileLock.Reset();

            if (_playerConnectedSubscribed)
            {
                MyVisualScriptLogicProvider.PlayerConnected -= OnPlayerConnected;
                _playerConnectedSubscribed = false;
            }

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

            // Phase 2: authoritative verification on the first simulation frame
            // after the existing workshop path-normalization logic has run.
            if (MyAPIGateway.Session.IsServer &&
                _worldStorageFirstFrameSelfTestPending)
            {
                _worldStorageFirstFrameSelfTestPending =
                    false;

                string previousVerifiedPath =
                    _runtimeGenerators.VerifiedWorldSavePath;

                string firstFramePath =
                    RuntimeGeneratorRegistry.NormalizePath(
                        MyAPIGateway.Session.CurrentPath);

                bool selfTestPassed = TryRunWorldStorageSelfTest(
                    firstFramePath,
                    "first frame after world load");

                if (!selfTestPassed)
                {
                    ReportWorldStorageVerificationFailure(
                        _runtimeGenerators.UnixPathDetected
                            ? "Linux-Compat plugin detected; path probing " +
                              "cannot continue."
                            : "SentinelCanary.txt could not be located through " +
                              "the reported CurrentPath, enumerated saves, or " +
                              "SavesPath + Session.Name.");
                }
                else
                {
                    _worldStorageVerificationFailed = false;

                    string verifiedPath =
                        _runtimeGenerators.VerifiedWorldSavePath;

                    if (!string.Equals(
                        previousVerifiedPath,
                        verifiedPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            _runtimeGenerators.RebindToSavePath(
                                firstFramePath);

                            _modifications.EnsureEmptyPlanetRuntimeAssets(
                                savePath: firstFramePath);
                        }
                        catch (Exception e)
                        {
                            _worldStoragePathReady = false;

                            MyLog.Default.Log(MyLogSeverity.Error,
                                "[RuntimePlanetGenerator] Runtime generator " +
                                "rebinding failed after path verification: " + e);

                            return;
                        }
                    }
                }
            }

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
                if (MyAPIGateway.Session.IsServer)
                {
                    bool verified = TryRunWorldStorageSelfTest(
                        currentPath,
                        "save-path transition");

                    if (verified)
                        _worldStorageVerificationFailed = false;
                    else
                        ReportWorldStorageVerificationFailure(
                            _runtimeGenerators.UnixPathDetected
                                ? "Linux-Compat plugin detected; path probing " +
                                  "cannot continue."
                                : "World-storage path could not be verified " +
                                  "after Save As.");
                }

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


        private bool TryRunWorldStorageSelfTest(
            string savePath,
            string phase)
        {
            try
            {
                return _runtimeGenerators.RunWorldStorageSelfTest(
                    savePath,
                    phase);
            }
            catch (Exception e)
            {
                MyLog.Default.Log(MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] World-storage self-test '" +
                    phase + "' threw: " + e);
                return false;
            }
        }


        private void ReportWorldStorageVerificationFailure(
            string reason)
        {
            const string banner =
@"/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\
\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/
/\    ARTHUR'S PROCEDURAL CUBEMAP API     /\
\/                                        \/
/\    INITIALIZATION SELF TEST FAILURE    /\
\/       API MAYBE NOT READY!!!!!         \/
/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\
\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/";

            MyLog.Default.Log(
                MyLogSeverity.Warning,
                Environment.NewLine + banner + Environment.NewLine +
                "[RuntimePlanetGenerator] World-storage verification failed; " +
                "initialization will continue, but the mod cannot guarantee " +
                "that it will work. Reason: " + reason);

            if (_worldStorageVerificationFailed)
                return;

            _worldStorageVerificationFailed = true;

            if (MyAPIGateway.Players == null)
                return;

            try
            {
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                foreach (IMyPlayer player in players)
                {
                    if (player != null && !player.IsBot)
                        SendVerificationWarning(player.IdentityId);
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Log(MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] Could not notify connected " +
                    "players about failed verification: " + e);
            }
        }


        private void OnPlayerConnected(long playerId)
        {
            if (_worldStorageVerificationFailed)
                SendVerificationWarning(playerId);
        }


        private static void SendVerificationWarning(long playerId)
        {
            try
            {
                MyVisualScriptLogicProvider.SendChatMessageColored(
                    "World-storage verification failed. Procedural Cubemap API " +
                    "cannot guarantee that it will work on this server.",
                    Color.Red,
                    "Procedural Cubemap API",
                    playerId);
            }
            catch (Exception e)
            {
                MyLog.Default.Log(MyLogSeverity.Warning,
                    "[RuntimePlanetGenerator] Could not notify player " +
                    playerId + " about failed verification: " + e);
            }
        }


    }
}
