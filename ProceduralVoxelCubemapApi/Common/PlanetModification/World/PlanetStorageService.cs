using System;
using Adk.Compression.Zip;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VoxelCubemapApi.Common.PlanetModification.Persistence;
using VoxelCubemapApi.Common.PlanetModification.Runtime;
using VoxelCubemapApi.Common.PlanetModification.Templates;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace VoxelCubemapApi.Common.PlanetModification.World
{
    internal sealed class PlanetStorageService
    {
        private readonly RuntimePackageStore _runtimePackages;
        private readonly VegetationClearScheduler _vegetationClearScheduler;
        private readonly Random _bridgeRandom =
            new Random();


        internal PlanetStorageService(
            RuntimePackageStore runtimePackages,
            VegetationClearScheduler vegetationClearScheduler)
        {
            if (runtimePackages == null)
                throw new ArgumentNullException(nameof(runtimePackages));

            if (vegetationClearScheduler == null)
                throw new ArgumentNullException(nameof(vegetationClearScheduler));

            _runtimePackages =
                runtimePackages;

            _vegetationClearScheduler =
                vegetationClearScheduler;
        }


        internal void ReadProviderIdentity(
            MyPlanet planet,
            out long planetSeed,
            out string providerSubtype)
        {
            if (planet == null ||
                planet.Storage == null)
            {
                throw new Exception(
                    "Planet/provider identity requires live planet storage.");
            }


            byte[] compressed;

            planet.Storage.Save(
                out compressed);

            if (compressed == null ||
                compressed.Length < 2 ||
                compressed[0] != 0x1F ||
                compressed[1] != 0x8B)
            {
                throw new Exception(
                    "Could not serialize live planet VX2 while reading its seed.");
            }


            byte[] raw =
                Zlib.InflateGzip(
                    compressed);


            string currentGeneratorSubtype =
                planet.Generator == null
                    ? null
                    : planet.Generator.Id.SubtypeName;


            if (TryReadSerializedPlanetProviderSeed(
                raw,
                currentGeneratorSubtype,
                out planetSeed))
            {
                providerSubtype =
                    currentGeneratorSubtype;

                return;
            }


            if (_runtimePackages.Settings != null &&
                _runtimePackages.Settings.PlanetBuilders != null)
            {
                for (int i =
                        _runtimePackages.Settings.PlanetBuilders.Count - 1;
                    i >= 0;
                    i--)
                {
                    RuntimePlanetBuilderEntry entry =
                        _runtimePackages.Settings.PlanetBuilders[i];

                    if (entry == null ||
                        entry.SourceEntityId != planet.EntityId ||
                        string.IsNullOrWhiteSpace(
                            entry.Subtype))
                    {
                        continue;
                    }


                    if (TryReadSerializedPlanetProviderSeed(
                        raw,
                        entry.Subtype,
                        out planetSeed))
                    {
                        providerSubtype =
                            entry.Subtype;

                        return;
                    }
                }
            }


            throw new Exception(
                "Could not locate the serialized live planet provider subtype " +
                "and seed in VX2.");
        }


        private static bool TryReadSerializedPlanetProviderSeed(
            byte[] raw,
            string providerSubtype,
            out long planetSeed)
        {
            planetSeed =
                0;


            if (raw == null ||
                string.IsNullOrWhiteSpace(
                    providerSubtype))
            {
                return false;
            }


            byte[] subtypeBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    providerSubtype);


            if (subtypeBytes.Length == 0 ||
                subtypeBytes.Length > 127)
            {
                return false;
            }


            int matchOffset =
                -1;

            int matches =
                0;


            for (int i = 16;
                i <= raw.Length - subtypeBytes.Length - 1;
                i++)
            {
                if (raw[i] !=
                    (byte)subtypeBytes.Length)
                {
                    continue;
                }


                bool match =
                    true;


                for (int j = 0;
                    j < subtypeBytes.Length;
                    j++)
                {
                    if (raw[
                        i + 1 + j] !=
                        subtypeBytes[j])
                    {
                        match =
                            false;

                        break;
                    }
                }


                if (!match)
                    continue;


                matchOffset =
                    i;

                matches++;
            }


            if (matches != 1 ||
                matchOffset < 16)
            {
                return false;
            }


            int seedOffset =
                matchOffset -
                16;

            ulong seedBits =
                0;


            for (int i = 0;
                i < 8;
                i++)
            {
                seedBits |=
                    (ulong)raw[
                        seedOffset + i] <<
                    (i * 8);
            }


            planetSeed =
                unchecked(
                    (long)seedBits);

            return true;
        }


        internal PlanetModificationWorkResult PrepareSwap(
            MyPlanet targetPlanet,
            MyPlanetGeneratorDefinition replacementGenerator,
            string currentProviderSubtype,
            string operationName = "planet modification")
        {
            if (targetPlanet == null)
                throw new ArgumentNullException(nameof(targetPlanet));

            if (targetPlanet.Storage == null)
                throw new Exception(
                    "Target planet has null Storage.");

            if (replacementGenerator == null)
                throw new ArgumentNullException(nameof(replacementGenerator));


            // Capture the exact storage instance whose bytes are copied. The
            // simulation-thread commit later compares against this reference,
            // making the final assignment a real compare-and-swap operation.
            object originalStorage =
                targetPlanet.Storage;

            byte[] compressed;

            targetPlanet.Storage.Save(
                out compressed);

            if (compressed == null || compressed.Length < 2)
                throw new Exception(
                    "Storage.Save(out byte[]) returned no data.");

            if (compressed[0] != 0x1F ||
                compressed[1] != 0x8B)
            {
                throw new Exception(
                    "Serialized storage is not gzip data.");
            }


            byte[] patchedRaw =
                Zlib.InflateGzip(
                    compressed);


            // The voxel palette remains unchanged. Terraform material behavior
            // comes from the generated planet definition and map overlays; the
            // serialized storage only needs to point at the new provider subtype.
            if (!string.Equals(
                currentProviderSubtype,
                replacementGenerator.Id.SubtypeName,
                StringComparison.OrdinalIgnoreCase))
            {
                patchedRaw =
                    ReplaceSerializedShortStringExact(
                        patchedRaw,
                        currentProviderSubtype,
                        replacementGenerator.Id.SubtypeName);
            }

            byte[] patchedCompressed =
                Zlib.DeflateGzipStored(
                    patchedRaw);


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Prepared planet provider for " +
                operationName +
                ". bytes=" +
                patchedCompressed.Length +
                ". Waiting for simulation-thread commit.");


            return new PlanetModificationWorkResult
            {
                TargetPlanet =
                    targetPlanet,

                OriginalStorage =
                    originalStorage,

                PatchedStorage =
                    patchedCompressed,

                ReplacementGenerator =
                    replacementGenerator,

                OperationName =
                    operationName
            };
        }


        /// <summary>
        /// Performs the compare-and-swap commit on the simulation thread. The
        /// expensive serialized copy is already complete; this method creates
        /// the engine storage bridge and changes the live storage reference in
        /// one simulation callback.
        /// </summary>
        internal void Commit(
            PlanetModificationWorkResult workResult)
        {
            if (workResult == null)
                throw new ArgumentNullException(nameof(workResult));

            if (workResult.TargetPlanet == null ||
                workResult.TargetPlanet.Storage == null)
            {
                throw new Exception(
                    "Target planet disappeared before the storage commit.");
            }

            if (!object.ReferenceEquals(
                workResult.TargetPlanet.Storage,
                workResult.OriginalStorage))
            {
                throw new Exception(
                    "Target planet storage changed while terraform work was running; " +
                    "the prepared result was not committed.");
            }

            if (workResult.PatchedStorage == null ||
                workResult.PatchedStorage.Length == 0)
            {
                throw new Exception(
                    "Terraform worker produced no patched storage.");
            }


            if (!string.IsNullOrWhiteSpace(
                workResult.EnvironmentCarrierSubtype))
            {
                if (workResult.ReplacementGenerator == null)
                {
                    throw new Exception(
                        "Terraform result is missing its runtime generator.");
                }

                PlanetEnvironmentService.BindRuntimeGenerator(
                    workResult.ReplacementGenerator,
                    workResult.EnvironmentCarrierSubtype);
            }


            VRage.ModAPI.IMyStorage patchedStorageApi =
                MyAPIGateway.Session.VoxelMaps.CreateStorage(
                    workResult.PatchedStorage);

            if (patchedStorageApi == null)
                throw new Exception(
                    "CreateStorage() rejected the patched VX2.");


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Committing prepared provider for " +
                (workResult.OperationName ?? "planet modification") +
                ". ModAPI storage size=" +
                patchedStorageApi.Size +
                ".");

            if (workResult.ChangeMaterials)
                patchedStorageApi.Reset(MyStorageDataTypeFlags.Material);

            SpawnPlanetThroughVoxelMapStorageBridge(
                workResult.TargetPlanet,
                patchedStorageApi,
                workResult.ReplacementGenerator,
                workResult.EnvironmentCarrierSubtype,
                workResult.ChangeEnvironment);
        }


        internal MyVoxelMap CreateStorageBridge(
            MyPlanet sourcePlanet,
            VRage.ModAPI.IMyStorage storageApi,
            string purpose)
        {
            if (sourcePlanet == null)
                throw new ArgumentNullException(nameof(sourcePlanet));

            if (storageApi == null)
                throw new ArgumentNullException(nameof(storageApi));


            string bridgeStorageName =
                "VoxelCubemapApi_" +
                (string.IsNullOrWhiteSpace(purpose)
                    ? "StorageBridge"
                    : purpose) +
                "_" +
                DateTime.UtcNow.Ticks;

            long bridgeEntityId;
            IMyEntity existingEntity;

            do
            {
                bridgeEntityId =
                    ((long)_bridgeRandom.Next() << 31) |
                    (uint)_bridgeRandom.Next();

                bridgeEntityId &=
                    long.MaxValue;
            }
            while (bridgeEntityId == 0 ||
                MyAPIGateway.Entities.TryGetEntityById(
                    bridgeEntityId,
                    out existingEntity));


            const double bridgeDistance =
                299792458.0 * 3.0;

            double directionX;
            double directionY;
            double directionZ;
            double directionLengthSquared;


            do
            {
                directionX =
                    _bridgeRandom.NextDouble() * 2.0 - 1.0;

                directionY =
                    _bridgeRandom.NextDouble() * 2.0 - 1.0;

                directionZ =
                    _bridgeRandom.NextDouble() * 2.0 - 1.0;

                directionLengthSquared =
                    directionX * directionX +
                    directionY * directionY +
                    directionZ * directionZ;
            }
            while (directionLengthSquared < 0.000001 ||
                directionLengthSquared > 1.0);


            double inverseDirectionLength =
                1.0 /
                Math.Sqrt(
                    directionLengthSquared);

            Vector3D bridgePosition =
                sourcePlanet.PositionComp.GetPosition() +
                new Vector3D(
                    directionX * inverseDirectionLength,
                    directionY * inverseDirectionLength,
                    directionZ * inverseDirectionLength) *
                bridgeDistance;


            VRage.Game.ModAPI.IMyVoxelMap bridgeApi =
                MyAPIGateway.Session.VoxelMaps.CreateVoxelMap(
                    bridgeStorageName,
                    storageApi,
                    bridgePosition,
                    bridgeEntityId);

            if (bridgeApi == null)
                throw new Exception(
                    "CreateVoxelMap() rejected the ModAPI storage bridge.");

            MyVoxelMap bridge =
                bridgeApi as MyVoxelMap;

            if (bridge == null)
            {
                bridgeApi.Close();

                throw new Exception(
                    "CreateVoxelMap() did not return Sandbox.Game.Entities.MyVoxelMap; " +
                    "cannot bridge the storage interface.");
            }

            if (bridge.Storage == null)
            {
                bridge.Close();

                throw new Exception(
                    "Temporary MyVoxelMap bridge has null engine storage.");
            }


            bridge.Save =
                false;

            return bridge;
        }


        internal static void RemoveStorageBridge(
            MyVoxelMap bridge,
            bool closeStorage)
        {
            if (bridge == null)
                return;


            bridge.Save =
                false;

            // RemoveEntity also unregisters MyVoxelBase instances from the
            // session voxel-map collection. It is safe to call even when the
            // bridge was never inserted into the render scene.
            MyAPIGateway.Entities.RemoveEntity(
                bridge);

            if (closeStorage)
            {
                bridge.Close();
            }
        }


        private void SpawnPlanetThroughVoxelMapStorageBridge(
            MyPlanet sourcePlanet,
            VRage.ModAPI.IMyStorage patchedStorageApi,
            MyPlanetGeneratorDefinition replacementGenerator,
            string environmentCarrierSubtype,
            bool changeEnvironment)
        {
            if (sourcePlanet == null)
                throw new ArgumentNullException(nameof(sourcePlanet));

            if (patchedStorageApi == null)
                throw new ArgumentNullException(nameof(patchedStorageApi));


            MyVoxelMap bridge =
                CreateStorageBridge(
                    sourcePlanet,
                    patchedStorageApi,
                    "StorageBridge");

            bool storageTransferred =
                false;

            try
            {
                if (!string.IsNullOrWhiteSpace(
                    environmentCarrierSubtype))
                {
                    if (replacementGenerator == null)
                    {
                        throw new Exception(
                            "Caller environment requires a runtime generator.");
                    }

                    Type currentEnvironmentType;
                    MyComponentBase currentEnvironmentBase;
                    MyEntityComponentBase currentEnvironmentEntity;

                    bool hasEnvironmentComponent =
                        PlanetEnvironmentService.TryGetComponentByInstanceTypeName(
                            sourcePlanet,
                            "Sandbox.Game.Entities.Planet.MyPlanetEnvironmentComponent",
                            out currentEnvironmentType,
                            out currentEnvironmentBase,
                            out currentEnvironmentEntity);

                    bool environmentDefinitionChanged =
                        sourcePlanet.Generator == null ||
                        !object.ReferenceEquals(
                            sourcePlanet.Generator.EnvironmentDefinition,
                            replacementGenerator.EnvironmentDefinition);

                    bool generatorIdentityChanged =
                        sourcePlanet.Generator == null ||
                        !string.Equals(
                            sourcePlanet.Generator.Id.SubtypeName,
                            replacementGenerator.Id.SubtypeName,
                            StringComparison.OrdinalIgnoreCase);

                    // MyPlanet.GetObjectBuilder() persists Generator.Id separately
                    // from the generator subtype stored in VX2. Even when the caller
                    // environment object is unchanged, a new runtime revision must
                    // update MyPlanet.Generator before the previous package is
                    // pruned; otherwise the next save references a definition that
                    // LoadData() no longer recreates.
                    if (!hasEnvironmentComponent ||
                        environmentDefinitionChanged ||
                        generatorIdentityChanged)
                    {
                        PlanetEnvironmentService.ReinitializeInPlace(
                            sourcePlanet,
                            replacementGenerator);
                    }
                    else
                    {
                        MyLog.Default.WriteLineAndConsole(
                            "[RuntimePlanetGenerator] Reusing existing live planet environment; " +
                            "caller definition and generator identity are unchanged. EntityId=" +
                            sourcePlanet.EntityId +
                            ".");
                    }
                }


                // Keep the original MyPlanet in-scene. This setter performs the
                // provider refresh plus ClearPhysicsShapes()/Clipmap.InvalidateAll().
                // It intentionally remains the final planet/voxel lifecycle mutation.
                sourcePlanet.Storage =
                    bridge.Storage;

                storageTransferred =
                    true;

                if (changeEnvironment &&
                    !string.IsNullOrWhiteSpace(
                        environmentCarrierSubtype))
                {
                    // Procedural environment providers cache logical sectors
                    // independently from voxel storage. Reinitialize after the
                    // replacement storage is attached so newly scanned sectors
                    // sample the current material/biome/height provider.
                    PlanetEnvironmentService.ReinitializeInPlace(
                        sourcePlanet,
                        replacementGenerator);
                }

                if (!string.IsNullOrWhiteSpace(
                    environmentCarrierSubtype))
                {
                    _vegetationClearScheduler.Schedule(
                        sourcePlanet);
                }


                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Patched live planet in-place. " +
                    "EntityId=" +
                    sourcePlanet.EntityId +
                    ", StorageName='" +
                    sourcePlanet.StorageName +
                    "', environment=" +
                    (string.IsNullOrWhiteSpace(environmentCarrierSubtype)
                        ? "unchanged"
                        : "'" + environmentCarrierSubtype + "'") +
                    ".");
            }
            finally
            {
                // After a successful transfer the planet owns the bridge storage,
                // so the bridge itself must be unregistered but not closed.
                RemoveStorageBridge(
                    bridge,
                    !storageTransferred);
            }
        }


        private static byte[] ReplaceSerializedShortStringExact(
            byte[] raw,
            string fromValue,
            string toValue)
        {
            byte[] fromBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    fromValue);

            byte[] toBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    toValue);

            if (fromBytes.Length > 127 ||
                toBytes.Length > 127)
            {
                throw new Exception(
                    "Serialized provider subtype exceeds the supported short-string encoding.");
            }


            int matchOffset = -1;
            int matches = 0;


            for (int i = 0;
                i <= raw.Length - fromBytes.Length - 1;
                i++)
            {
                if (raw[i] != (byte)fromBytes.Length)
                    continue;


                bool match =
                    true;


                for (int j = 0;
                    j < fromBytes.Length;
                    j++)
                {
                    if (raw[i + 1 + j] != fromBytes[j])
                    {
                        match =
                            false;

                        break;
                    }
                }


                if (!match)
                    continue;


                matchOffset =
                    i;

                matches++;
            }


            if (matches != 1)
            {
                throw new Exception(
                    "Expected exactly one serialized '" +
                    fromValue +
                    "' provider subtype in raw VX2, found " +
                    matches +
                    ".");
            }


            int oldEntryLength =
                1 +
                fromBytes.Length;

            int newEntryLength =
                1 +
                toBytes.Length;


            byte[] output =
                new byte[
                    raw.Length -
                    oldEntryLength +
                    newEntryLength];


            if (matchOffset > 0)
            {
                Buffer.BlockCopy(
                    raw,
                    0,
                    output,
                    0,
                    matchOffset);
            }


            int outputCursor =
                matchOffset;


            output[outputCursor++] =
                (byte)toBytes.Length;


            Buffer.BlockCopy(
                toBytes,
                0,
                output,
                outputCursor,
                toBytes.Length);


            outputCursor +=
                toBytes.Length;


            int oldTailOffset =
                matchOffset +
                oldEntryLength;

            int tailLength =
                raw.Length -
                oldTailOffset;


            if (tailLength > 0)
            {
                Buffer.BlockCopy(
                    raw,
                    oldTailOffset,
                    output,
                    outputCursor,
                    tailLength);
            }


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] VX2 provider subtype patched: '" +
                fromValue +
                "' -> '" +
                toValue +
                "'");


            return output;
        }


    }
}
