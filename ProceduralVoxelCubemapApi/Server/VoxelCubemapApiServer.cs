using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;

using Generated;

using VoxelCubemapApi.Server.Api;
using VoxelCubemapApi.Server.PlanetModification;
using VoxelCubemapApi.Server.PlanetModification.Persistence;
using VoxelCubemapApi.Server.PlanetModification.Runtime;
using VoxelCubemapApi.Server.PlanetModification.World;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;

namespace VoxelCubemapApi.Server
{
    [APIManager(0x5643584150490001L, Provider = nameof(_intermodApi))]
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, -1000)]
    internal sealed partial class VoxelCubemapApiServer : MySessionComponentBase
    {
        private volatile bool _unloading;
        private VoxelCubemapIntermodApiServer _intermodApi;

        private RuntimePackageStore _runtimePackages;
        private PlanetDataArchiveService _planetDataArchives;
        private PersistedEnvironmentRestorer _environmentRestorer;
        private PlanetModificationCoordinator _modifications;
        private RuntimeGeneratorRegistry _runtimeGenerators;
        private PlanetStorageService _planetStorage;

        private readonly VegetationClearScheduler _vegetationClearScheduler =
            new VegetationClearScheduler();

        internal bool IsUnloading => _unloading;


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
            string absolutePlanetDataFolder,
            byte grassMaterialMapValue,
            bool verifyGrassOverlay = true)
        {
            return _runtimeGenerators.RegisterDefinition(
                sourceBuilder,
                subtype,
                absolutePlanetDataFolder,
                grassMaterialMapValue,
                verifyGrassOverlay);
        }


        public override void LoadData()
        {
            _unloading =
                false;

            _runtimePackages =
                new RuntimePackageStore(
                    this);

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

            _modifications =
                new PlanetModificationCoordinator(
                    _runtimePackages,
                    _planetDataArchives,
                    _runtimeGenerators,
                    _planetStorage,
                    delegate { return _unloading; });

            _runtimePackages.LoadPersistedRuntimeGenerators();

            _environmentRestorer.Reset();
            _vegetationClearScheduler.Clear();

            _intermodApi =
                new VoxelCubemapIntermodApiServer(
                    _modifications);

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

            _vegetationClearScheduler.Clear();

            UnregisterApiManager();


            if (_intermodApi != null)
            {
                _intermodApi =
                    null;
            }


            if (_runtimePackages != null)
            {
                _runtimePackages.ClearWorldStorageCache();

                _runtimePackages =
                    null;
            }
        }


        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session == null)
                return;

            _vegetationClearScheduler.Update();

            // Runtime generator state is owned by the background request while
            // it is active.  Rebinding resumes after its simulation-thread
            // completion callback.
            if (_modifications.RequestInProgress)
                return;

            _environmentRestorer.Update();

            string currentPath =
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
