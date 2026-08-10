using System;
using System.Collections.Generic;
using NUnit.Framework;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters.Tests
{
    public sealed class EnemyUnityAdapterTests
    {
        private readonly List<UnityEngine.Object> cleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                if (cleanup[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(cleanup[index]);
                }
            }

            cleanup.Clear();

            if (CombatFeedbackPool.Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    CombatFeedbackPool.Instance.gameObject
                );
            }
        }

        [Test]
        public void Definition_ConvertsAllSixRolePresetsToValidatedRuntimeData()
        {
            Array roles = Enum.GetValues(typeof(EnemyRole));
            HashSet<EnemyMovementMode> movementModes = new HashSet<EnemyMovementMode>();

            foreach (EnemyRole role in roles)
            {
                EnemyArchetypeDefinition definition = Track(
                    ScriptableObject.CreateInstance<EnemyArchetypeDefinition>()
                );
                definition.ApplyRolePreset(role);

                Assert.That(
                    definition.TryCreateRuntimeConfig(
                        out EnemyArchetypeConfig config,
                        out string validationError
                    ),
                    Is.True,
                    validationError
                );
                Assert.That(config.Role, Is.EqualTo(role));
                Assert.That(config.AttackProfile.OwnerFaction, Is.EqualTo(CombatFaction.Enemy));
                Assert.That(config.MaximumHealth, Is.GreaterThan(0f));
                Assert.That(config.ThreatCost, Is.GreaterThan(0f));
                movementModes.Add(config.MovementMode);
            }

            Assert.That(roles.Length, Is.EqualTo(6));
            Assert.That(movementModes.Count, Is.EqualTo(5));
        }

        [Test]
        public void Controller_PursuesWhileStationaryRoleDoesNotTranslate()
        {
            Transform target = CreateObject("Target").transform;
            target.position = new Vector3(0f, 0f, 20f);

            GameObject pursuerObject = CreateObject("Pursuer");
            EnemyArchetypeController pursuer =
                pursuerObject.AddComponent<EnemyArchetypeController>();
            pursuer.Initialize(
                EnemyArchetypeCatalog.Pursuer,
                target,
                initialAttackDelaySeconds: 10f
            );
            pursuer.Tick(0.25f);

            GameObject sentryObject = CreateObject("Sentry");
            EnemyArchetypeController sentry =
                sentryObject.AddComponent<EnemyArchetypeController>();
            sentry.Initialize(
                EnemyArchetypeCatalog.StationarySentry,
                target,
                initialAttackDelaySeconds: 10f
            );
            sentry.Tick(0.25f);

            Assert.That(pursuerObject.transform.position.z, Is.GreaterThan(0f));
            Assert.That(sentryObject.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(pursuer.CurrentState, Is.EqualTo(EnemyState.Engage));
        }

        [Test]
        public void Controller_SkirmisherAndFlyerUseDistinctMovementPlanes()
        {
            Transform target = CreateObject("Target").transform;
            target.position = new Vector3(0f, 0f, 22f);

            GameObject skirmisherObject = CreateObject("Skirmisher");
            EnemyArchetypeController skirmisher =
                skirmisherObject.AddComponent<EnemyArchetypeController>();
            skirmisher.Initialize(
                EnemyArchetypeCatalog.Skirmisher,
                target,
                initialAttackDelaySeconds: 10f
            );
            skirmisher.Tick(0.25f);

            GameObject flyerObject = CreateObject("Flyer");
            EnemyArchetypeController flyer =
                flyerObject.AddComponent<EnemyArchetypeController>();
            flyer.Initialize(
                EnemyArchetypeCatalog.FlyingHarrier,
                target,
                initialAttackDelaySeconds: 10f
            );
            flyer.Tick(0.25f);

            Assert.That(Mathf.Abs(skirmisherObject.transform.position.x), Is.GreaterThan(0f));
            Assert.That(skirmisherObject.transform.position.y, Is.EqualTo(0f));
            Assert.That(flyerObject.transform.position.y, Is.GreaterThan(0f));
        }

        [Test]
        public void Controller_TelegraphsBeforeItsProjectileBoundary()
        {
            Transform target = CreateObject("Target").transform;
            target.position = new Vector3(0f, 0f, 20f);
            EnemyArchetypeController controller = CreateObject("Sentry")
                .AddComponent<EnemyArchetypeController>();
            int telegraphs = 0;
            int attacks = 0;
            controller.AttackTelegraphStarted += signal =>
            {
                telegraphs++;
                Assert.That(signal.DurationSeconds, Is.GreaterThan(0f));
            };
            controller.AttackRequested += signal =>
            {
                attacks++;
                Assert.That(telegraphs, Is.EqualTo(1));
                Assert.That(signal.Direction.z, Is.GreaterThan(0.9f));
            };
            controller.Initialize(EnemyArchetypeCatalog.StationarySentry, target);

            controller.Tick(0f);
            Assert.That(telegraphs, Is.EqualTo(1));
            Assert.That(attacks, Is.Zero);

            controller.Tick(
                EnemyArchetypeCatalog.SentryRapidFire.TelegraphSeconds + 0.001f
            );
            Assert.That(attacks, Is.EqualTo(1));
        }

        [Test]
        public void Controller_ExternalForceDeathAndPoolResetClearTransientState()
        {
            Transform target = CreateObject("Target").transform;
            target.position = Vector3.forward * 12f;
            EnemyArchetypeController controller = CreateObject("Enemy")
                .AddComponent<EnemyArchetypeController>();
            controller.Initialize(EnemyArchetypeCatalog.Pursuer, target);

            controller.ApplyExternalForce(new CombatVector3(10f, 0f, 0f), this);
            Assert.That(controller.ExternalVelocity.x, Is.EqualTo(8.5f).Within(0.001f));

            DamageResult result = controller.ApplyDamage(
                new DamageInfo(
                    this,
                    CombatFaction.Player,
                    DamageType.Kinetic,
                    1000f,
                    CombatVector3.Zero,
                    CombatVector3.Zero
                )
            );
            Assert.That(result.WasKilled, Is.True);
            Assert.That(controller.IsDead, Is.True);

            controller.OnPoolRecycled();
            Assert.That(controller.IsInitialized, Is.False);
            Assert.That(controller.Target, Is.Null);
            Assert.That(controller.ExternalVelocity, Is.EqualTo(Vector3.zero));

            controller.OnPoolSpawned();
            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(controller.IsDead, Is.False);
            Assert.That(controller.CurrentHealth, Is.EqualTo(controller.MaximumHealth));
            Assert.That(controller.RuntimeState.AttacksStarted, Is.Zero);
        }

        [Test]
        public void SpawnZone_ProducesCompatibleBoundsAndCameraValidatedCandidates()
        {
            GameObject zoneObject = CreateObject("Spawn Zone");
            SpawnZone zone = zoneObject.AddComponent<SpawnZone>();
            Transform point = CreateObject("Point").transform;
            point.SetParent(zoneObject.transform, false);
            point.localPosition = new Vector3(1f, 0f, 1f);
            zone.Configure(
                "arena-east",
                SpawnZoneCompatibility.GroundAndFlight,
                new[] { point },
                new Bounds(Vector3.zero, new Vector3(10f, 8f, 10f)),
                requireGroundProbe: false
            );

            Camera camera = CreateObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1f, -10f);
            camera.transform.rotation = Quaternion.identity;

            Assert.That(
                zone.TryBuildCandidate(0, camera, out SpawnPointCandidate candidate),
                Is.True
            );
            Assert.That(candidate.ZoneId, Is.EqualTo("arena-east:0"));
            Assert.That(candidate.Compatibility, Is.EqualTo(SpawnZoneCompatibility.GroundAndFlight));
            Assert.That(candidate.IsEnabled, Is.True);
            Assert.That(candidate.IsGroundPositionValid, Is.True);
            Assert.That(candidate.IsWithinFlightBounds, Is.True);
            Assert.That(candidate.IsObstacleFree, Is.True);
            Assert.That(candidate.IsInsideCameraView, Is.True);

            camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Assert.That(
                zone.TryBuildCandidate(0, camera, out SpawnPointCandidate offscreen),
                Is.True
            );
            Assert.That(offscreen.IsInsideCameraView, Is.False);
        }

        [Test]
        public void SpawnZone_MarksOutOfBoundsAuthoredPointIneligible()
        {
            GameObject zoneObject = CreateObject("Spawn Zone");
            SpawnZone zone = zoneObject.AddComponent<SpawnZone>();
            Transform point = CreateObject("Outside Point").transform;
            point.position = Vector3.right * 20f;
            zone.Configure(
                "bounded-zone",
                SpawnZoneCompatibility.Flight,
                new[] { point },
                new Bounds(Vector3.zero, Vector3.one * 4f),
                requireGroundProbe: false
            );

            Assert.That(
                zone.TryBuildCandidate(0, null, out SpawnPointCandidate candidate),
                Is.True
            );
            Assert.That(candidate.IsEnabled, Is.True);
            Assert.That(candidate.IsWithinFlightBounds, Is.False);
            Assert.That(
                SpawnEligibility.Evaluate(
                    EnemyArchetypeCatalog.FlyingHarrier,
                    candidate,
                    new CombatVector3(100f, 0f, 0f),
                    playerSafeRadius: 1f,
                    avoidCameraView: false
                ),
                Is.EqualTo(SpawnEligibilityFailure.OutsideFlightBounds)
            );
        }

        [Test]
        public void SpawnDirector_SpawnsProtectedEnemyAndReusesItAfterDeath()
        {
            Transform player = CreateObject("Player").transform;
            SpawnZone zone = CreateZone(
                "ground",
                SpawnZoneCompatibility.Ground,
                new Vector3(0f, 0f, 20f)
            );
            EnemyArchetypeDefinition definition = CreateDefinition(
                EnemyRole.StationarySentry
            );
            GameObject prefab = CreateEnemyPrefab("Sentry Prefab");
            EnemySpawnDirector director = CreateObject("Director")
                .AddComponent<EnemySpawnDirector>();
            director.SetDeathRecycleDelay(0f);
            director.Initialize(
                player,
                null,
                new[] { zone },
                new[] { new EnemySpawnPrefabEntry(definition, prefab) },
                DirectorConfig(
                    cap: 1,
                    interval: 100f,
                    minimumGroup: 1,
                    maximumGroup: 1,
                    threatBudget: 2f,
                    safeRadius: 5f,
                    avoidView: false,
                    seed: 17u
                )
            );

            director.Tick(0f);

            Assert.That(director.LastPlanResult.Count, Is.EqualTo(1));
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(1));
            Assert.That(director.PendingSpawnCount, Is.Zero);
            Assert.That(director.ReservedEnemyCount, Is.EqualTo(1));
            EnemyArchetypeController first = director.GetActiveEnemy(0);
            GameObject firstInstance = first.gameObject;
            Assert.That(first.Target, Is.SameAs(player));
            Assert.That(first.transform.position.z, Is.EqualTo(20f).Within(0.001f));
            Assert.That(first.RuntimeState.IsSpawnProtected, Is.True);
            Assert.That(
                first.RuntimeState.AttackCooldownRemaining,
                Is.InRange(0f, 1f)
            );

            first.MarkDead();
            director.Tick(0f);
            Assert.That(director.ActiveInstanceCount, Is.Zero);
            Assert.That(director.ReservedEnemyCount, Is.Zero);
            Assert.That(firstInstance.activeSelf, Is.False);

            Assert.That(director.ForceSpawnCycle().Count, Is.EqualTo(1));
            EnemyArchetypeController reused = director.GetActiveEnemy(0);
            Assert.That(reused.gameObject, Is.SameAs(firstInstance));
            Assert.That(reused.IsDead, Is.False);
            Assert.That(reused.CurrentHealth, Is.EqualTo(reused.MaximumHealth));
            Assert.That(reused.RuntimeState.AttacksStarted, Is.Zero);
        }

        [Test]
        public void SpawnDirector_ReservesCapAndPausesStaggeredGroupActivation()
        {
            Transform player = CreateObject("Player").transform;
            SpawnZone zone = CreateZone(
                "group",
                SpawnZoneCompatibility.Ground,
                new Vector3(-3f, 0f, 20f),
                new Vector3(3f, 0f, 20f)
            );
            EnemyArchetypeDefinition definition = CreateDefinition(
                EnemyRole.StationarySentry
            );
            GameObject prefab = CreateEnemyPrefab("Sentry Prefab");
            EnemySpawnDirector director = CreateObject("Director")
                .AddComponent<EnemySpawnDirector>();
            director.SetGroupActivationSpacing(0.5f);
            director.Initialize(
                player,
                null,
                new[] { zone },
                new[] { new EnemySpawnPrefabEntry(definition, prefab) },
                DirectorConfig(
                    cap: 2,
                    interval: 100f,
                    minimumGroup: 2,
                    maximumGroup: 2,
                    threatBudget: 2f,
                    safeRadius: 5f,
                    avoidView: false,
                    seed: 22u
                )
            );

            director.Tick(0f);
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(1));
            Assert.That(director.PendingSpawnCount, Is.EqualTo(1));
            Assert.That(director.ReservedEnemyCount, Is.EqualTo(2));

            director.SetPaused(true);
            director.Tick(2f);
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(1));
            Assert.That(director.PendingSpawnCount, Is.EqualTo(1));

            director.SetPaused(false);
            director.Tick(0.49f);
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(1));
            director.Tick(0.01f);
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(2));
            Assert.That(director.PendingSpawnCount, Is.Zero);
            Assert.That(director.ForceSpawnCycle().HasSpawns, Is.False);

            director.SetDirectorEnabled(false);
            director.ClearActiveEnemies();
            Assert.That(director.ForceSpawnCycle().HasSpawns, Is.False);
            director.ResetDirector(
                clearExistingEnemies: true,
                shouldSpawnImmediately: false
            );
            Assert.That(director.IsDirectorEnabled, Is.True);
            Assert.That(director.IsPaused, Is.False);
            Assert.That(director.ReservedEnemyCount, Is.Zero);
            Assert.That(
                director.RuntimeState.SecondsUntilNextSpawn,
                Is.EqualTo(100f)
            );
        }

        [Test]
        public void SpawnDirector_EnforcesSafeRadiusAndCameraAvoidanceFromZoneSnapshot()
        {
            Transform player = CreateObject("Player").transform;
            Transform point;
            SpawnZone zone = CreateZone(
                "fairness",
                SpawnZoneCompatibility.Ground,
                out point,
                new Vector3(0f, 0f, 2f)
            );
            Camera camera = CreateObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1f, 0f);
            camera.transform.rotation = Quaternion.identity;
            EnemyArchetypeDefinition definition = CreateDefinition(
                EnemyRole.StationarySentry
            );
            GameObject prefab = CreateEnemyPrefab("Sentry Prefab");
            EnemySpawnDirector director = CreateObject("Director")
                .AddComponent<EnemySpawnDirector>();
            director.Initialize(
                player,
                camera,
                new[] { zone },
                new[] { new EnemySpawnPrefabEntry(definition, prefab) },
                DirectorConfig(
                    cap: 1,
                    interval: 100f,
                    minimumGroup: 1,
                    maximumGroup: 1,
                    threatBudget: 2f,
                    safeRadius: 5f,
                    avoidView: true,
                    seed: 31u
                ),
                shouldSpawnImmediately: false
            );

            Assert.That(director.ForceSpawnCycle().HasSpawns, Is.False);

            point.position = Vector3.forward * 20f;
            Assert.That(director.ForceSpawnCycle().HasSpawns, Is.False);

            camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Assert.That(director.ForceSpawnCycle().Count, Is.EqualTo(1));
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(1));
        }

        [Test]
        public void SpawnDirector_ChoosesOnlyCompatibleFlightZoneForFlyer()
        {
            Transform player = CreateObject("Player").transform;
            SpawnZone ground = CreateZone(
                "ground",
                SpawnZoneCompatibility.Ground,
                new Vector3(-20f, 0f, 20f)
            );
            SpawnZone flight = CreateZone(
                "flight",
                SpawnZoneCompatibility.Flight,
                new Vector3(20f, 8f, 20f)
            );
            EnemyArchetypeDefinition definition = CreateDefinition(
                EnemyRole.FlyingHarrier
            );
            GameObject prefab = CreateEnemyPrefab("Harrier Prefab");
            EnemySpawnDirector director = CreateObject("Director")
                .AddComponent<EnemySpawnDirector>();
            director.Initialize(
                player,
                null,
                new[] { ground, flight },
                new[] { new EnemySpawnPrefabEntry(definition, prefab) },
                DirectorConfig(
                    cap: 1,
                    interval: 100f,
                    minimumGroup: 1,
                    maximumGroup: 1,
                    threatBudget: 3f,
                    safeRadius: 5f,
                    avoidView: false,
                    seed: 41u
                ),
                shouldSpawnImmediately: false
            );

            Assert.That(director.ForceSpawnCycle().Count, Is.EqualTo(1));
            EnemyArchetypeController spawned = director.GetActiveEnemy(0);
            Assert.That(spawned.Config.IsFlying, Is.True);
            Assert.That(spawned.transform.position.x, Is.EqualTo(20f).Within(0.001f));
            Assert.That(spawned.transform.position.y, Is.EqualTo(8f).Within(0.001f));
        }

        [Test]
        public void SpawnDirector_ResettingSeedReplaysWeightedArchetypeSequence()
        {
            Transform player = CreateObject("Player").transform;
            SpawnZone zone = CreateZone(
                "seeded",
                SpawnZoneCompatibility.Ground,
                new Vector3(0f, 0f, 20f)
            );
            EnemyArchetypeDefinition sentry = CreateDefinition(
                EnemyRole.StationarySentry
            );
            EnemyArchetypeDefinition pursuer = CreateDefinition(EnemyRole.Pursuer);
            GameObject prefab = CreateEnemyPrefab("Shared Enemy Prefab");
            EnemySpawnDirector director = CreateObject("Director")
                .AddComponent<EnemySpawnDirector>();
            const uint seed = 123456u;
            director.Initialize(
                player,
                null,
                new[] { zone },
                new[]
                {
                    new EnemySpawnPrefabEntry(sentry, prefab, weightMultiplier: 1f),
                    new EnemySpawnPrefabEntry(pursuer, prefab, weightMultiplier: 1f)
                },
                DirectorConfig(
                    cap: 1,
                    interval: 100f,
                    minimumGroup: 1,
                    maximumGroup: 1,
                    threatBudget: 3f,
                    safeRadius: 5f,
                    avoidView: false,
                    seed: seed
                ),
                shouldSpawnImmediately: false
            );

            EnemyRole[] first = CaptureSpawnSequence(director, 12);
            director.ResetDirectorWithSeed(
                seed,
                clearExistingEnemies: true,
                shouldSpawnImmediately: false
            );
            EnemyRole[] second = CaptureSpawnSequence(director, 12);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Contain(EnemyRole.StationarySentry));
            Assert.That(first, Does.Contain(EnemyRole.Pursuer));
        }

        [Test]
        public void RuntimeTuning_ControllerMultipliersPreserveHealthFractionAndResetOnPool()
        {
            Transform target = CreateObject("Target").transform;
            target.position = Vector3.forward * 30f;
            EnemyArchetypeController controller = CreateObject("Enemy")
                .AddComponent<EnemyArchetypeController>();
            controller.Initialize(
                EnemyArchetypeCatalog.Pursuer,
                target,
                initialAttackDelaySeconds: 10f
            );

            float authoredHealth = controller.MaximumHealth;
            controller.SetRuntimeMultipliers(2f, 3f, 0f);
            Assert.That(controller.MaximumHealth, Is.EqualTo(authoredHealth * 2f));
            Assert.That(controller.CurrentHealth, Is.EqualTo(controller.MaximumHealth));
            Assert.That(controller.OutgoingDamageMultiplier, Is.EqualTo(3f));
            Assert.That(controller.SpeedMultiplier, Is.Zero);

            Vector3 before = controller.transform.position;
            controller.Tick(0.25f);
            Assert.That(controller.transform.position, Is.EqualTo(before));

            controller.OnPoolRecycled();
            Assert.That(controller.HealthMultiplier, Is.EqualTo(1f));
            Assert.That(controller.OutgoingDamageMultiplier, Is.EqualTo(1f));
            Assert.That(controller.SpeedMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Lifecycle_UnconfiguredHotReloadStateDoesNotTickOrThrow()
        {
            EnemyArchetypeController controller = CreateObject("Reloaded Enemy")
                .AddComponent<EnemyArchetypeController>();
            System.Reflection.FieldInfo initializedField =
                typeof(EnemyArchetypeController).GetField(
                    "initialized",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic
                );
            Assert.That(initializedField, Is.Not.Null);
            initializedField.SetValue(controller, true);

            Assert.DoesNotThrow(() => controller.Tick(0.016f));
            Assert.That(controller.IsInitialized, Is.False);
        }

        [Test]
        public void RuntimeTuning_DirectorChangesScheduleSpawnsAndClearsThroughPublicApi()
        {
            Transform player = CreateObject("Player").transform;
            SpawnZone zone = CreateZone(
                "console",
                SpawnZoneCompatibility.Ground,
                Vector3.forward * 20f
            );
            EnemyArchetypeDefinition definition = CreateDefinition(
                EnemyRole.StationarySentry
            );
            GameObject prefab = CreateEnemyPrefab("Sentry Prefab");
            EnemySpawnDirector director = CreateObject("Director")
                .AddComponent<EnemySpawnDirector>();
            director.SetDeathRecycleDelay(0f);
            director.Initialize(
                player,
                null,
                new[] { zone },
                new[] { new EnemySpawnPrefabEntry(definition, prefab) },
                DirectorConfig(
                    cap: 1,
                    interval: 100f,
                    minimumGroup: 1,
                    maximumGroup: 1,
                    threatBudget: 2f,
                    safeRadius: 5f,
                    avoidView: false,
                    seed: 77u
                ),
                shouldSpawnImmediately: false
            );

            Assert.That(director.SetActiveEnemyCap(3), Is.EqualTo(3));
            Assert.That(director.SetSpawnIntervalSeconds(0f), Is.EqualTo(0.05f));
            Assert.That(director.SetEnemyHealthMultiplier(2f), Is.EqualTo(2f));
            Assert.That(director.SetEnemyDamageMultiplier(4f), Is.EqualTo(4f));
            Assert.That(director.SetEnemySpeedMultiplier(0f), Is.Zero);
            director.SetDirectorEnabled(false);

            Assert.That(
                director.SpawnArchetype(definition.ArchetypeId, 2),
                Is.EqualTo(2)
            );
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(2));
            Assert.That(director.GetActiveEnemy(0).HealthMultiplier, Is.EqualTo(2f));
            Assert.That(
                director.GetActiveEnemy(0).OutgoingDamageMultiplier,
                Is.EqualTo(4f)
            );
            Assert.That(director.GetActiveEnemy(0).SpeedMultiplier, Is.Zero);
            Assert.That(director.SpawnEntryCount, Is.EqualTo(1));
            Assert.That(
                director.GetSpawnArchetypeId(0),
                Is.EqualTo(definition.ArchetypeId)
            );

            System.Reflection.FieldInfo activeCountField =
                typeof(EnemySpawnDirector).GetField(
                    "activeCount",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic
                );
            Assert.That(activeCountField, Is.Not.Null);
            activeCountField.SetValue(director, 999);
            Assert.DoesNotThrow(() => director.Tick(0f));
            Assert.That(director.ActiveInstanceCount, Is.EqualTo(2));

            Assert.That(director.KillAllActiveEnemies(), Is.EqualTo(2));
            director.Tick(0f);
            Assert.That(director.ActiveInstanceCount, Is.Zero);
            Assert.That(director.SpawnRandom(1), Is.EqualTo(1));
            Assert.That(director.DespawnAllEnemies(), Is.EqualTo(1));
            Assert.That(director.ActiveInstanceCount, Is.Zero);
        }

        private EnemyRole[] CaptureSpawnSequence(
            EnemySpawnDirector director,
            int count
        )
        {
            EnemyRole[] sequence = new EnemyRole[count];
            for (int index = 0; index < count; index++)
            {
                Assert.That(director.ForceSpawnCycle().Count, Is.EqualTo(1));
                sequence[index] = director.GetActiveEnemy(0).Config.Role;
                director.ClearActiveEnemies();
            }

            return sequence;
        }

        private SpawnDirectorConfig DirectorConfig(
            int cap,
            float interval,
            int minimumGroup,
            int maximumGroup,
            float threatBudget,
            float safeRadius,
            bool avoidView,
            uint seed
        )
        {
            return new SpawnDirectorConfig(
                cap,
                interval,
                minimumGroup,
                maximumGroup,
                threatBudget,
                safeRadius,
                avoidView,
                spawnProtectionSeconds: 0.5f,
                maximumInitialAttackStaggerSeconds: 1f,
                useDeterministicSeed: true,
                deterministicSeed: seed
            );
        }

        private EnemyArchetypeDefinition CreateDefinition(EnemyRole role)
        {
            EnemyArchetypeDefinition definition = Track(
                ScriptableObject.CreateInstance<EnemyArchetypeDefinition>()
            );
            definition.ApplyRolePreset(role);
            definition.name = role + " Definition";
            return definition;
        }

        private GameObject CreateEnemyPrefab(string name)
        {
            GameObject prefab = CreateObject(name);
            prefab.AddComponent<EnemyArchetypeController>();
            prefab.SetActive(false);
            return prefab;
        }

        private SpawnZone CreateZone(
            string id,
            SpawnZoneCompatibility compatibility,
            params Vector3[] positions
        )
        {
            Transform ignored;
            return CreateZone(id, compatibility, out ignored, positions);
        }

        private SpawnZone CreateZone(
            string id,
            SpawnZoneCompatibility compatibility,
            out Transform firstPoint,
            params Vector3[] positions
        )
        {
            GameObject zoneObject = CreateObject(id + " Zone");
            SpawnZone zone = zoneObject.AddComponent<SpawnZone>();
            Transform[] points = new Transform[positions.Length];
            for (int index = 0; index < positions.Length; index++)
            {
                Transform point = CreateObject(id + " Point " + index).transform;
                point.SetParent(zoneObject.transform, false);
                point.position = positions[index];
                points[index] = point;
            }

            firstPoint = points.Length > 0 ? points[0] : null;
            zone.Configure(
                id,
                compatibility,
                points,
                new Bounds(Vector3.zero, new Vector3(100f, 40f, 100f)),
                requireGroundProbe: false
            );
            return zone;
        }

        private GameObject CreateObject(string name)
        {
            return Track(new GameObject(name));
        }

        private T Track<T>(T instance) where T : UnityEngine.Object
        {
            cleanup.Add(instance);
            return instance;
        }
    }
}
