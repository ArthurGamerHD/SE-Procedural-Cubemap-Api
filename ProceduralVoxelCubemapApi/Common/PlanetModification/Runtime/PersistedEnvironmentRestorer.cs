using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.World;
using VRage.Game;
using VRage.Utils;

namespace VoxelCubemapApi.Common.PlanetModification.Runtime
{
    internal sealed class PersistedEnvironmentRestorer
    {
        private readonly RuntimePackageStore _runtimePackages;
        private readonly PlanetStorageService _planetStorage;

        private readonly HashSet<long> _restoredEnvironmentBindings =
            new HashSet<long>();

        private int _retryTicks;


        internal PersistedEnvironmentRestorer(
            RuntimePackageStore runtimePackages,
            PlanetStorageService planetStorage)
        {
            if (runtimePackages == null)
                throw new ArgumentNullException(nameof(runtimePackages));

            if (planetStorage == null)
                throw new ArgumentNullException(nameof(planetStorage));

            _runtimePackages =
                runtimePackages;

            _planetStorage =
                planetStorage;
        }


        internal void Reset()
        {
            _restoredEnvironmentBindings.Clear();
            _retryTicks =
                0;
        }


        internal void Update()
        {
            if (_retryTicks <= 0)
            {
                try
                {
                    bool complete =
                        RestorePersistedEnvironmentBindings();

                    _retryTicks =
                        complete
                            ? int.MaxValue
                            : 100;
                }
                catch (Exception e)
                {
                    _retryTicks =
                        100;

                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Persisted environment restore failed: " +
                        e);
                }
            }
            else if (_retryTicks != int.MaxValue)
            {
                _retryTicks--;
            }
        }


        private bool RestorePersistedEnvironmentBindings()
        {
            if (_runtimePackages.Settings == null ||
                _runtimePackages.Settings.PlanetBuilders == null ||
                _runtimePackages.Settings.PlanetBuilders.Count == 0)
            {
                return true;
            }


            bool complete =
                true;

            var candidatePlanetIds =
                new HashSet<long>();


            for (int i = 0;
                i < _runtimePackages.Settings.PlanetBuilders.Count;
                i++)
            {
                RuntimePlanetBuilderEntry candidate =
                    _runtimePackages.Settings.PlanetBuilders[i];

                if (candidate == null ||
                    candidate.SourceEntityId == 0 ||
                    string.IsNullOrWhiteSpace(
                        candidate.EnvironmentCarrierSubtype))
                {
                    continue;
                }

                candidatePlanetIds.Add(
                    candidate.SourceEntityId);
            }


            foreach (long planetEntityId in candidatePlanetIds)
            {
                if (_restoredEnvironmentBindings.Contains(
                    planetEntityId))
                {
                    continue;
                }


                MyPlanet planet =
                    PlanetLocator.FindByEntityId(
                        planetEntityId);

                if (planet == null ||
                    planet.Storage == null ||
                    !planet.InScene)
                {
                    complete =
                        false;

                    continue;
                }


                long ignoredProviderSeed;
                string providerSubtype;

                _planetStorage.ReadProviderIdentity(
                    planet,
                    out ignoredProviderSeed,
                    out providerSubtype);


                RuntimePlanetBuilderEntry currentEntry =
                    _runtimePackages.Settings.PlanetBuilders
                        .LastOrDefault(x =>
                            x != null &&
                            x.SourceEntityId == planetEntityId &&
                            !string.IsNullOrWhiteSpace(x.Subtype) &&
                            string.Equals(
                                x.Subtype,
                                providerSubtype,
                                StringComparison.OrdinalIgnoreCase));

                if (currentEntry == null ||
                    string.IsNullOrWhiteSpace(
                        currentEntry.EnvironmentCarrierSubtype))
                {
                    _restoredEnvironmentBindings.Add(
                        planetEntityId);

                    continue;
                }


                try
                {
                    RestorePlanetEnvironmentFromCarrier(
                        planet,
                        currentEntry.EnvironmentCarrierSubtype,
                        providerSubtype);
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Could not restore caller environment " +
                        "for planet " +
                        planetEntityId +
                        ": " +
                        e.Message);
                }

                // A present planet is handled once per load. Missing definitions
                // are configuration errors and should not cause endless retries.
                _restoredEnvironmentBindings.Add(
                    planetEntityId);
            }


            return complete;
        }


        private void RefreshPersistedPlanetEnvironmentInPlace(
            MyPlanet sourcePlanet,
            MyPlanetGeneratorDefinition runtimeGenerator)
        {
            MyAPIGateway.Session.Save();
            
            if (sourcePlanet == null)
                throw new ArgumentNullException(nameof(sourcePlanet));

            if (runtimeGenerator == null)
                throw new ArgumentNullException(nameof(runtimeGenerator));

            if (sourcePlanet.Storage == null)
                throw new Exception(
                    "Cannot refresh persisted planet environment: live storage is null.");


            byte[] serializedStorage;

            sourcePlanet.Storage.Save(
                out serializedStorage);

            if (serializedStorage == null ||
                serializedStorage.Length == 0)
            {
                throw new Exception(
                    "Could not serialize live planet storage for post-init physics refresh.");
            }


            VRage.ModAPI.IMyStorage storageApi =
                MyAPIGateway.Session.VoxelMaps.CreateStorage(
                    serializedStorage);

            if (storageApi == null)
            {
                throw new Exception(
                    "CreateStorage() rejected persisted planet storage copy.");
            }


            MyVoxelMap storageBridge =
                _planetStorage.CreateStorageBridge(
                    sourcePlanet,
                    storageApi,
                    "EnvironmentMigration");

            bool storageTransferred =
                false;

            try
            {
                // Legacy donor-based saves can still require a one-time native
                // environment initialization. ReinitializePlanetEnvironmentInPlace()
                // preserves PositionLeftBottomCorner so this migration cannot
                // accumulate the engine's first-init-only half-voxel offset.
                PlanetEnvironmentService.ReinitializeInPlace(
                    sourcePlanet,
                    runtimeGenerator);
                
                sourcePlanet.Storage =
                    storageBridge.Storage;

                storageTransferred =
                    true;
            }
            // pray to klang for this not fail...
            // because if it does,
            // we have no way to fix it withing the mod api limitations
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    "[RuntimePlanetGenerator]",
                    10000,
                    MyFontEnum.Red);
                MyAPIGateway.Utilities.ShowNotification(
                    "Could not refresh persisted planet environment",
                    10000,
                    MyFontEnum.Red);
                MyAPIGateway.Utilities.ShowNotification(
                    "Continue to playing in this session is NOT RECOMMENDED",
                    10000,
                    MyFontEnum.Red);
                MyAPIGateway.Utilities.ShowNotification(
                    "Please reload the session",
                    10000,
                    MyFontEnum.Red);

                MyLog.Default.Log(MyLogSeverity.Error, "[RuntimePlanetGenerator] Could not refresh persisted planet environment: " + e.Message);
            }
            finally
            {
                PlanetStorageService.RemoveStorageBridge(
                    storageBridge,
                    !storageTransferred);
            }
        }


        private void RestorePlanetEnvironmentFromCarrier(
            MyPlanet sourcePlanet,
            string environmentCarrierSubtype,
            string providerSubtype)
        {
            MyPlanetGeneratorDefinition runtimeGenerator;

            if (!_runtimePackages.Generators.TryGetValue(
                providerSubtype,
                out runtimeGenerator) ||
                runtimeGenerator == null)
            {
                throw new Exception(
                    "Runtime generator '" +
                    providerSubtype +
                    "' is not registered.");
            }


            PlanetEnvironmentService.BindRuntimeGenerator(
                runtimeGenerator,
                environmentCarrierSubtype);


            // New saves persist Planet.Generator as PlanetModification_* and are
            // initialized natively by the engine. This branch only migrates saves
            // created by the earlier donor-based prototype, where the VX2 provider
            // was runtime-modified but MyPlanet.Generator still said Mars/Moon.
            if (sourcePlanet.Generator != null &&
                (string.Equals(
                    sourcePlanet.Generator.Id.SubtypeName,
                    providerSubtype,
                    StringComparison.OrdinalIgnoreCase) ||
                 object.ReferenceEquals(
                    sourcePlanet.Generator.EnvironmentDefinition,
                    runtimeGenerator.EnvironmentDefinition)))
            {
                // The storage provider may advance to a newer PlanetModification_*
                // revision while the caller environment stays the same. There is
                // no reason to run MyPlanet.Init() merely to make Generator.Id
                // match the provider subtype; doing so mutates initialization-only
                // voxel state. The existing environment component is already bound
                // to the same prepared environment object.
                return;
            }


            RefreshPersistedPlanetEnvironmentInPlace(
                sourcePlanet,
                runtimeGenerator);


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Migrated persisted planet to native runtime generator. " +
                "EntityId=" +
                sourcePlanet.EntityId +
                ", provider='" +
                providerSubtype +
                "', carrier='" +
                environmentCarrierSubtype +
                "'.");
        }
    }
}
