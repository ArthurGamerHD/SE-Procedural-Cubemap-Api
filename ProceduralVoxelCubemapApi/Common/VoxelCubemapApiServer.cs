using System;
using Generated;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VoxelCubemapApi.Common.Api;
using VoxelCubemapApi.Common.Configuration;
using VoxelCubemapApi.Common.Networking;
using VoxelCubemapApi.Common.PlanetModification;
using VoxelCubemapApi.Common.PlanetModification.EnvironmentPresets;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Runtime;
using VoxelCubemapApi.Common.PlanetModification.World;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace VoxelCubemapApi.Common
{
    [APIManager(0x5643584150490001L, Provider = nameof(_intermodApi))]
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, -1000)]
    internal sealed partial class VoxelCubemapApiServer : MySessionComponentBase
    {
        private volatile bool _unloading;
        private VoxelCubemapIntermodApiServer _intermodApi;

        private VoxelCubemapApiConfig _config;
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

        internal static VoxelCubemapApiServer Instance { get; private set; }

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

            _config =
                VoxelCubemapApiConfigStorage.LoadOrCreate();

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
                    () => _unloading);

            _runtimePackages.ProceduralArchiveBuilder =
                _modifications.RebuildProceduralArchive;

            _runtimePackages.ProceduralGeneratorSignatureBuilder =
                _modifications.BuildProceduralGeneratorSignature;

            _runtimePackages.LoadPersistedRuntimeGenerators();

            _network.Init();

            _environmentRestorer.Reset();
            _vegetationClearScheduler.Clear();

            _intermodApi =
                new VoxelCubemapIntermodApiServer(
                    _modifications,
                    _environmentPresets);

            RegisterApiManager();
        }


        public override void BeforeStart()
        {
            try
            {
                _runtimePackages.ReconcileRuntimePackagesWithLivePlanets();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Startup persistence cleanup failed: " +
                    e);
            }
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
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Save-path rebind failed: " + e);
            }
        }




    }
}
