using System;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

namespace ProceduralCubemapApi.Common.PlanetModification.Runtime
{
    internal static class PlanetEnvironmentService
    {
        internal static void EnsureBiomeMapEnabled(
            MyObjectBuilder_PlanetGeneratorDefinition builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var planetMaps =
                builder.PlanetMaps.GetValueOrDefault();

            if (planetMaps.Biome)
                return;

            planetMaps.Biome =
                true;

            builder.PlanetMaps =
                planetMaps;
        }


        internal static MyPlanetGeneratorDefinition ResolveCarrierGenerator(
            string environmentCarrierSubtype)
        {
            if (string.IsNullOrWhiteSpace(
                environmentCarrierSubtype))
            {
                throw new ArgumentException(
                    "Environment carrier subtype cannot be empty.",
                    nameof(environmentCarrierSubtype));
            }


            MyPlanetGeneratorDefinition carrier =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x != null &&
                        string.Equals(
                            x.Id.SubtypeName,
                            environmentCarrierSubtype,
                            StringComparison.OrdinalIgnoreCase));

            if (carrier == null)
            {
                throw new Exception(
                    "Environment carrier planet generator '" +
                    environmentCarrierSubtype +
                    "' is not registered.");
            }

            if (!carrier.EnvironmentId.HasValue)
            {
                throw new Exception(
                    "Environment carrier planet generator '" +
                    environmentCarrierSubtype +
                    "' has no explicit WorldEnvironmentDefinition.");
            }

            return carrier;
        }


        internal static MyPlanetGeneratorDefinition ResolveEnvironmentGenerator(
            string generatorSubtype)
        {
            if (string.IsNullOrWhiteSpace(generatorSubtype))
            {
                throw new ArgumentException(
                    "Environment generator subtype cannot be empty.",
                    nameof(generatorSubtype));
            }

            MyPlanetGeneratorDefinition generator =
                MyDefinitionManager.Static
                    .GetPlanetsGeneratorsDefinitions()
                    .FirstOrDefault(x =>
                        x != null &&
                        string.Equals(
                            x.Id.SubtypeName,
                            generatorSubtype,
                            StringComparison.OrdinalIgnoreCase));

            if (generator == null)
            {
                throw new Exception(
                    "Environment planet generator '" +
                    generatorSubtype +
                    "' is not registered.");
            }

            if (generator.EnvironmentDefinition == null)
            {
                throw new Exception(
                    "Environment planet generator '" +
                    generatorSubtype +
                    "' has no resolved environment definition.");
            }

            return generator;
        }


        internal static bool TryGetComponentByInstanceTypeName(
            MyPlanet planet,
            string instanceTypeFullName,
            out Type componentType,
            out MyComponentBase component,
            out MyEntityComponentBase entityComponent)
        {
            componentType =
                null;

            component =
                null;

            entityComponent =
                null;


            if (planet == null ||
                string.IsNullOrWhiteSpace(instanceTypeFullName))
            {
                return false;
            }


            foreach (Type candidateType in
                planet.Components.GetComponentTypes())
            {
                if (candidateType == null)
                    continue;


                MyComponentBase candidate;

                if (!planet.Components.TryGet(
                    candidateType,
                    out candidate) ||
                    candidate == null)
                {
                    continue;
                }


                Type instanceType =
                    candidate.GetType();

                if (instanceType == null ||
                    !string.Equals(
                        instanceType.FullName,
                        instanceTypeFullName,
                        StringComparison.Ordinal))
                {
                    continue;
                }


                componentType =
                    candidateType;

                component =
                    candidate;

                entityComponent =
                    candidate as MyEntityComponentBase;

                return true;
            }


            return false;
        }


        internal static MyPlanetGeneratorDefinition BindRuntimeGenerator(
            MyPlanetGeneratorDefinition runtimeGenerator,
            string environmentCarrierSubtype)
        {
            if (runtimeGenerator == null)
                throw new ArgumentNullException(nameof(runtimeGenerator));

            if (string.IsNullOrWhiteSpace(
                environmentCarrierSubtype))
            {
                return runtimeGenerator;
            }


            MyPlanetGeneratorDefinition carrier =
                ResolveEnvironmentGenerator(
                    environmentCarrierSubtype);

            // Runtime planet definitions are registered after Keen's global
            // definition postprocessor has already run, so their EnvironmentId
            // is parsed but EnvironmentDefinition is never resolved. Reuse the
            // caller's normally-loaded carrier definition and bind its already
            // prepared environment object directly onto this runtime generator.
            runtimeGenerator.EnvironmentId =
                carrier.EnvironmentId;

            runtimeGenerator.EnvironmentDefinition =
                carrier.EnvironmentDefinition;

            runtimeGenerator.EnvironmentSectorType =
                carrier.EnvironmentSectorType;


            MyLog.Default.WriteLineAndConsole(
                "[RuntimePlanetGenerator] Bound caller environment to runtime generator. " +
                "Generator='" +
                runtimeGenerator.Id.SubtypeName +
                "', carrier='" +
                environmentCarrierSubtype +
                "'.");


            return runtimeGenerator;
        }


        internal static void ReinitializeInPlace(
            MyPlanet sourcePlanet,
            MyPlanetGeneratorDefinition replacementGenerator)
        {
            if (sourcePlanet == null)
                throw new ArgumentNullException(nameof(sourcePlanet));

            if (replacementGenerator == null)
                throw new ArgumentNullException(nameof(replacementGenerator));

            if (sourcePlanet.Storage == null)
                throw new Exception(
                    "Cannot initialize planet environment: live storage is null.");


            const string environmentComponentName =
                "Sandbox.Game.Entities.Planet.MyPlanetEnvironmentComponent";

            const string gravityComponentName =
                "Sandbox.Game.Entities.MySphericalNaturalGravityComponent";


            Type oldEnvironmentType;
            MyComponentBase oldEnvironmentBase;
            MyEntityComponentBase oldEnvironment;

            bool hadOldEnvironment =
                TryGetComponentByInstanceTypeName(
                    sourcePlanet,
                    environmentComponentName,
                    out oldEnvironmentType,
                    out oldEnvironmentBase,
                    out oldEnvironment);


            Type gravityComponentType;
            MyComponentBase gravityComponentBase;
            MyEntityComponentBase gravityComponent;

            if (!TryGetComponentByInstanceTypeName(
                sourcePlanet,
                gravityComponentName,
                out gravityComponentType,
                out gravityComponentBase,
                out gravityComponent) ||
                gravityComponentType == null ||
                gravityComponentBase == null ||
                gravityComponent == null)
            {
                throw new Exception(
                    "Could not preserve the live planet gravity component.");
            }


            bool oldEnvironmentRemoved =
                false;

            bool gravityRemoved =
                false;

            bool newEnvironmentAddedToScene =
                false;


            try
            {
                if (hadOldEnvironment)
                {
                    if (oldEnvironment == null)
                    {
                        throw new Exception(
                            "Live planet environment component is not an entity component.");
                    }

                    if (sourcePlanet.InScene)
                    {
                        oldEnvironment.OnRemovedFromScene();
                    }

                    sourcePlanet.Components.Remove(
                        oldEnvironmentType);

                    if (oldEnvironment.Entity != null)
                    {
                        oldEnvironment.SetContainer(
                            null);
                    }

                    oldEnvironmentRemoved =
                        true;
                }


                // MyPlanet.OnAddedToScene registers the gravity component with
                // MyGravityProviderSystem. Keep that exact object alive and
                // registered while MyPlanet.Init creates its temporary replacement.
                sourcePlanet.Components.Remove(
                    gravityComponentType);

                if (gravityComponent.Entity != null)
                {
                    gravityComponent.SetContainer(
                        null);
                }

                gravityRemoved =
                    true;


                MyPlanetInitArguments initArguments =
                    sourcePlanet.GetInitArguments;

                initArguments.Storage =
                    sourcePlanet.Storage;

                initArguments.StorageName =
                    sourcePlanet.StorageName;

                initArguments.Generator =
                    replacementGenerator;

                
                //initArguments.MarkAreaEmpty =
                //  false;
                // ok this should be marked false to avoid memory leak however,
                // if I do so, no way to set it back to true without re-initing 
                // the planet, this corrupts the planet generator causing asteroids
                // to spawn inside the atmosphere next time the session is reloaded
                
                // option 1: memory leak
                // option 2: corrupted planet
                // option 3: somewhere in between by set it to false but then call
                //           "init" a single time with true to restore on next load
                
                // for now, lets keep at a small memory leak

                initArguments.InitializeComponents =
                    false;

                initArguments.FadeIn =
                    false;


                // MyVoxelBase.InitVoxelMap() applies the engine's half-voxel
                // offset by mutating PositionLeftBottomCorner. That mutation is
                // correct only for first construction; calling MyPlanet.Init() on
                // an existing planet would otherwise add another (0.5,0.5,0.5)
                // every time and persist the accumulated shift on save.
                Vector3D positionLeftBottomCornerBeforeInit =
                    sourcePlanet.PositionLeftBottomCorner;

                // The planet remains inside MyEntities and in the render scene.
                // Init() is used only when the environment definition actually
                // changes (or a barren planet needs its first environment).
                sourcePlanet.Init(
                    initArguments);

                Vector3D positionLeftBottomCornerAfterInit =
                    sourcePlanet.PositionLeftBottomCorner;

                if (positionLeftBottomCornerAfterInit !=
                    positionLeftBottomCornerBeforeInit)
                {
                    sourcePlanet.PositionLeftBottomCorner =
                        positionLeftBottomCornerBeforeInit;

                    MyLog.Default.WriteLineAndConsole(
                        "[RuntimePlanetGenerator] Restored planet voxel origin after environment init. " +
                        "EntityId=" +
                        sourcePlanet.EntityId +
                        ", attemptedDelta=" +
                        (positionLeftBottomCornerAfterInit -
                            positionLeftBottomCornerBeforeInit) +
                        ".");
                }


                // Init() always adds a fresh spherical gravity component. It was
                // never registered with MyGravityProviderSystem because the planet
                // itself never left/re-entered the scene, so discard it and restore
                // the original object that is already registered there.
                MyComponentBase temporaryGravity;

                if (sourcePlanet.Components.TryGet(
                    gravityComponentType,
                    out temporaryGravity) &&
                    temporaryGravity != null &&
                    !object.ReferenceEquals(
                        temporaryGravity,
                        gravityComponentBase))
                {
                    sourcePlanet.Components.Remove(
                        gravityComponentType);

                    MyEntityComponentBase temporaryGravityEntity =
                        temporaryGravity as MyEntityComponentBase;

                    if (temporaryGravityEntity != null &&
                        temporaryGravityEntity.Entity != null)
                    {
                        temporaryGravityEntity.SetContainer(
                            null);
                    }
                }


                sourcePlanet.Components.Add(
                    gravityComponentType,
                    gravityComponentBase);

                if (!object.ReferenceEquals(
                    gravityComponent.Entity,
                    sourcePlanet))
                {
                    gravityComponent.SetContainer(
                        sourcePlanet.Components);
                }

                gravityRemoved =
                    false;


                Type newEnvironmentType;
                MyComponentBase newEnvironmentBase;
                MyEntityComponentBase newEnvironment;

                if (!TryGetComponentByInstanceTypeName(
                    sourcePlanet,
                    environmentComponentName,
                    out newEnvironmentType,
                    out newEnvironmentBase,
                    out newEnvironment) ||
                    newEnvironment == null)
                {
                    throw new Exception(
                        "Runtime generator did not initialize a planet environment component.");
                }

                if (!object.ReferenceEquals(
                    newEnvironment.Entity,
                    sourcePlanet))
                {
                    throw new Exception(
                        "Engine-created environment component is not owned by the live planet.");
                }


                if (sourcePlanet.InScene)
                {
                    newEnvironment.OnAddedToScene();

                    newEnvironmentAddedToScene =
                        true;
                }


                MyLog.Default.WriteLineAndConsole(
                    "[RuntimePlanetGenerator] Reinitialized live planet environment in place. " +
                    "EntityId=" +
                    sourcePlanet.EntityId +
                    ", Generator='" +
                    replacementGenerator.Id.SubtypeName +
                    "'.");
            }
            catch
            {
                // Gravity is externally registered by MyPlanet.OnAddedToScene, so
                // restoring the original component is mandatory even on failure.
                if (gravityRemoved)
                {
                    MyComponentBase currentGravity;

                    if (sourcePlanet.Components.TryGet(
                        gravityComponentType,
                        out currentGravity) &&
                        currentGravity != null &&
                        !object.ReferenceEquals(
                            currentGravity,
                            gravityComponentBase))
                    {
                        sourcePlanet.Components.Remove(
                            gravityComponentType);

                        MyEntityComponentBase currentGravityEntity =
                            currentGravity as MyEntityComponentBase;

                        if (currentGravityEntity != null &&
                            currentGravityEntity.Entity != null)
                        {
                            currentGravityEntity.SetContainer(
                                null);
                        }
                    }

                    sourcePlanet.Components.Add(
                        gravityComponentType,
                        gravityComponentBase);

                    if (!object.ReferenceEquals(
                        gravityComponent.Entity,
                        sourcePlanet))
                    {
                        gravityComponent.SetContainer(
                            sourcePlanet.Components);
                    }
                }


                // If Init() failed before a replacement environment became usable,
                // put the previous component back. Its OnRemovedFromScene() already
                // cleared sectors, so it can safely regenerate after registration.
                Type currentEnvironmentType;
                MyComponentBase currentEnvironmentBase;
                MyEntityComponentBase currentEnvironment;

                bool hasCurrentEnvironment =
                    TryGetComponentByInstanceTypeName(
                        sourcePlanet,
                        environmentComponentName,
                        out currentEnvironmentType,
                        out currentEnvironmentBase,
                        out currentEnvironment);

                if (hasCurrentEnvironment &&
                    currentEnvironment != null &&
                    !object.ReferenceEquals(
                        currentEnvironmentBase,
                        oldEnvironmentBase))
                {
                    if (newEnvironmentAddedToScene)
                    {
                        currentEnvironment.OnRemovedFromScene();
                    }

                    sourcePlanet.Components.Remove(
                        currentEnvironmentType);

                    if (currentEnvironment.Entity != null)
                    {
                        currentEnvironment.SetContainer(
                            null);
                    }
                }

                if (oldEnvironmentRemoved &&
                    oldEnvironmentType != null &&
                    oldEnvironmentBase != null &&
                    oldEnvironment != null)
                {
                    sourcePlanet.Components.Add(
                        oldEnvironmentType,
                        oldEnvironmentBase);

                    if (!object.ReferenceEquals(
                        oldEnvironment.Entity,
                        sourcePlanet))
                    {
                        oldEnvironment.SetContainer(
                            sourcePlanet.Components);
                    }

                    if (sourcePlanet.InScene)
                    {
                        oldEnvironment.OnAddedToScene();
                    }
                }

                throw;
            }
        }


    }
}
