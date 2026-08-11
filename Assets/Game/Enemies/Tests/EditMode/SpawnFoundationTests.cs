using System;
using System.Collections.Generic;
using NUnit.Framework;
using Powersuit.Combat;

namespace Powersuit.Enemies.Tests
{
    public sealed class SpawnFoundationTests
    {
        [Test]
        public void Encounter_RequiresZoneThenAdvancesExactThreePhaseBudgets()
        {
            DemoEncounterState state = new DemoEncounterState(
                new[]
                {
                    new DemoEncounterPhaseConfig("causeway", "Causeway", 2),
                    new DemoEncounterPhaseConfig("foundry", "Foundry", 1),
                    new DemoEncounterPhaseConfig("airfield", "Airfield", 3)
                },
                intermissionSeconds: 0.5f
            );

            Assert.That(state.Status, Is.EqualTo(DemoEncounterStatus.WaitingForZone));
            Assert.That(state.TryActivateCurrentPhase(false), Is.False);
            Assert.That(state.TryActivateCurrentPhase(true), Is.True);
            Assert.That(state.RegisterSpawned(5), Is.EqualTo(2));
            Assert.That(state.RegisterDefeat(), Is.True);
            Assert.That(state.RegisterDefeat(), Is.True);
            Assert.That(state.RegisterDefeat(), Is.False);
            Assert.That(state.Advance(0f, noEnemiesRemaining: true), Is.True);
            Assert.That(state.Status, Is.EqualTo(DemoEncounterStatus.Intermission));
            Assert.That(state.Advance(0.49f, true), Is.False);
            Assert.That(state.Advance(0.01f, true), Is.True);
            Assert.That(state.CurrentPhaseIndex, Is.EqualTo(1));
            Assert.That(state.Status, Is.EqualTo(DemoEncounterStatus.WaitingForZone));
        }

        [Test]
        public void Encounter_FailureRestartsCurrentPhaseWithoutRewindingCampaign()
        {
            DemoEncounterState state = new DemoEncounterState(
                new[]
                {
                    new DemoEncounterPhaseConfig("first", "First", 1),
                    new DemoEncounterPhaseConfig("second", "Second", 2)
                },
                intermissionSeconds: 0f
            );
            state.TryActivateCurrentPhase(true);
            state.RegisterSpawned(1);
            state.RegisterDefeat();
            state.Advance(0f, true);
            state.Advance(0f, true);
            Assert.That(state.CurrentPhaseIndex, Is.EqualTo(1));

            state.TryActivateCurrentPhase(true);
            state.RegisterSpawned(2);
            Assert.That(state.Fail(), Is.True);
            state.RestartCurrentPhase();

            Assert.That(state.CurrentPhaseIndex, Is.EqualTo(1));
            Assert.That(state.SpawnedThisPhase, Is.Zero);
            Assert.That(state.DefeatedThisPhase, Is.Zero);
            Assert.That(state.Status, Is.EqualTo(DemoEncounterStatus.WaitingForZone));
        }

        [Test]
        public void Eligibility_EnforcesSafeRadiusViewZoneAndSurfaceValidation()
        {
            CombatVector3 player = CombatVector3.Zero;
            EnemyArchetypeConfig ground = EnemyArchetypeCatalog.PatrolRifleman;
            EnemyArchetypeConfig flying = EnemyArchetypeCatalog.FlyingHarrier;

            Assert.That(
                Evaluate(
                    ground,
                    Point("near", 2f, SpawnZoneCompatibility.Ground),
                    player,
                    safeRadius: 5f,
                    avoidView: false
                ),
                Is.EqualTo(SpawnEligibilityFailure.InsidePlayerSafeRadius)
            );
            Assert.That(
                Evaluate(
                    ground,
                    Point(
                        "visible",
                        20f,
                        SpawnZoneCompatibility.Ground,
                        isInsideView: true
                    ),
                    player,
                    safeRadius: 5f,
                    avoidView: true
                ),
                Is.EqualTo(SpawnEligibilityFailure.InsideCameraView)
            );
            Assert.That(
                Evaluate(
                    ground,
                    Point(
                        "blocked-ground",
                        20f,
                        SpawnZoneCompatibility.Ground,
                        isObstacleFree: false
                    ),
                    player,
                    safeRadius: 5f,
                    avoidView: false
                ),
                Is.EqualTo(SpawnEligibilityFailure.GroundPositionObstructed)
            );
            Assert.That(
                Evaluate(
                    flying,
                    Point("ground", 20f, SpawnZoneCompatibility.Ground),
                    player,
                    safeRadius: 5f,
                    avoidView: false
                ),
                Is.EqualTo(SpawnEligibilityFailure.IncompatibleZone)
            );
            Assert.That(
                Evaluate(
                    ground,
                    Point(
                        "bad-ground",
                        20f,
                        SpawnZoneCompatibility.Ground,
                        isGroundValid: false
                    ),
                    player,
                    safeRadius: 5f,
                    avoidView: false
                ),
                Is.EqualTo(SpawnEligibilityFailure.InvalidGroundPosition)
            );
            Assert.That(
                Evaluate(
                    flying,
                    Point(
                        "blocked-air",
                        20f,
                        SpawnZoneCompatibility.Flight,
                        isObstacleFree: false
                    ),
                    player,
                    safeRadius: 5f,
                    avoidView: false
                ),
                Is.EqualTo(SpawnEligibilityFailure.FlightPathObstructed)
            );
        }

        [Test]
        public void Planner_ResetReplaysTheSameSeededWeightedPlanExactly()
        {
            SpawnDirectorConfig config = Config(
                cap: 12,
                minimumGroupSize: 4,
                maximumGroupSize: 4,
                threatBudget: 7f,
                seed: 771u
            );
            SpawnPlanner planner = new SpawnPlanner(config);
            IReadOnlyList<EnemySpawnEntry> entries = AllEntries();
            IReadOnlyList<SpawnPointCandidate> points = MixedPoints();
            SpawnRequest[] first = new SpawnRequest[4];
            SpawnRequest[] second = new SpawnRequest[4];

            SpawnPlanResult firstResult = planner.FillPlan(
                entries,
                points,
                CombatVector3.Zero,
                activeEnemyCount: 0,
                first
            );
            planner.Reset();
            SpawnPlanResult secondResult = planner.FillPlan(
                entries,
                points,
                CombatVector3.Zero,
                activeEnemyCount: 0,
                second
            );

            Assert.That(firstResult.Count, Is.GreaterThan(0));
            Assert.That(secondResult.Count, Is.EqualTo(firstResult.Count));
            Assert.That(secondResult.ThreatSpent, Is.EqualTo(firstResult.ThreatSpent));

            for (int index = 0; index < firstResult.Count; index++)
            {
                Assert.That(
                    second[index].Archetype.ArchetypeId,
                    Is.EqualTo(first[index].Archetype.ArchetypeId)
                );
                Assert.That(second[index].CandidateIndex, Is.EqualTo(first[index].CandidateIndex));
                Assert.That(
                    second[index].InitialAttackDelaySeconds,
                    Is.EqualTo(first[index].InitialAttackDelaySeconds)
                );
            }
        }

        [Test]
        public void Planner_EnforcesCapBudgetGroupBoundsUniquePointsAndAttackStagger()
        {
            SpawnDirectorConfig config = Config(
                cap: 5,
                minimumGroupSize: 3,
                maximumGroupSize: 3,
                threatBudget: 2.1f,
                seed: 45u
            );
            SpawnPlanner planner = new SpawnPlanner(config);
            EnemySpawnEntry[] entries =
            {
                new EnemySpawnEntry(EnemyArchetypeCatalog.HeavyArtillery),
                new EnemySpawnEntry(EnemyArchetypeCatalog.StationarySentry)
            };
            SpawnPointCandidate[] points =
            {
                Point("g1", 20f, SpawnZoneCompatibility.Ground),
                Point("g2", 25f, SpawnZoneCompatibility.Ground),
                Point("g3", 30f, SpawnZoneCompatibility.Ground)
            };
            SpawnRequest[] output = new SpawnRequest[3];

            SpawnPlanResult result = planner.FillPlan(
                entries,
                points,
                CombatVector3.Zero,
                activeEnemyCount: 3,
                output
            );

            Assert.That(result.RequestedGroupSize, Is.EqualTo(2));
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.ThreatSpent, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(result.ThreatSpent, Is.LessThanOrEqualTo(config.GroupThreatBudget));
            Assert.That(output[0].Archetype.Role, Is.EqualTo(EnemyRole.StationarySentry));
            Assert.That(output[1].Archetype.Role, Is.EqualTo(EnemyRole.StationarySentry));
            Assert.That(output[0].CandidateIndex, Is.Not.EqualTo(output[1].CandidateIndex));
            Assert.That(
                output[0].InitialAttackDelaySeconds,
                Is.LessThan(output[1].InitialAttackDelaySeconds)
            );
            Assert.That(output[0].SpawnProtectionSeconds, Is.EqualTo(1.25f));
        }

        [Test]
        public void Planner_UsesOnlyCompatibleOffscreenValidatedFlightPoint()
        {
            SpawnDirectorConfig config = Config(
                cap: 4,
                minimumGroupSize: 1,
                maximumGroupSize: 1,
                threatBudget: 3f,
                seed: 9u
            );
            SpawnPlanner planner = new SpawnPlanner(config);
            EnemySpawnEntry[] entries =
            {
                new EnemySpawnEntry(EnemyArchetypeCatalog.FlyingHarrier)
            };
            SpawnPointCandidate[] points =
            {
                Point("ground", 20f, SpawnZoneCompatibility.Ground),
                Point(
                    "visible-air",
                    22f,
                    SpawnZoneCompatibility.Flight,
                    isInsideView: true
                ),
                Point(
                    "invalid-air",
                    24f,
                    SpawnZoneCompatibility.Flight,
                    isFlightValid: false
                ),
                Point("valid-air", 26f, SpawnZoneCompatibility.Flight)
            };
            SpawnRequest[] output = new SpawnRequest[1];

            SpawnPlanResult result = planner.FillPlan(
                entries,
                points,
                CombatVector3.Zero,
                activeEnemyCount: 0,
                output
            );

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(output[0].Point.ZoneId, Is.EqualTo("valid-air"));
            Assert.That(output[0].Archetype.IsFlying, Is.True);
        }

        [Test]
        public void Planner_AppliesConfiguredSelectionWeights()
        {
            SpawnDirectorConfig config = Config(
                cap: 4,
                minimumGroupSize: 1,
                maximumGroupSize: 1,
                threatBudget: 3f,
                seed: 1907u
            );
            SpawnPlanner planner = new SpawnPlanner(config);
            EnemySpawnEntry[] entries =
            {
                new EnemySpawnEntry(
                    EnemyArchetypeCatalog.StationarySentry,
                    weightMultiplier: 0.01f
                ),
                new EnemySpawnEntry(
                    EnemyArchetypeCatalog.PatrolRifleman,
                    weightMultiplier: 100f
                )
            };
            SpawnPointCandidate[] points =
            {
                Point("weighted", 20f, SpawnZoneCompatibility.Ground)
            };
            SpawnRequest[] output = new SpawnRequest[1];
            int riflemanSelections = 0;

            for (int iteration = 0; iteration < 200; iteration++)
            {
                SpawnPlanResult result = planner.FillPlan(
                    entries,
                    points,
                    CombatVector3.Zero,
                    activeEnemyCount: 0,
                    output
                );
                Assert.That(result.Count, Is.EqualTo(1));

                if (output[0].Archetype.Role == EnemyRole.PatrolRifleman)
                {
                    riflemanSelections++;
                }
            }

            Assert.That(riflemanSelections, Is.GreaterThanOrEqualTo(198));
        }

        [Test]
        public void Planner_ReturnsNoPlanWhenEntriesOrLocationsAreIneligible()
        {
            SpawnDirectorConfig config = Config(
                cap: 4,
                minimumGroupSize: 1,
                maximumGroupSize: 2,
                threatBudget: 3f,
                seed: 3u
            );
            SpawnPlanner planner = new SpawnPlanner(config);
            EnemySpawnEntry[] entries =
            {
                new EnemySpawnEntry(
                    EnemyArchetypeCatalog.PatrolRifleman,
                    isEnabled: false
                )
            };
            SpawnPointCandidate[] points =
            {
                Point("safe-radius", 1f, SpawnZoneCompatibility.Ground)
            };
            SpawnRequest[] output = new SpawnRequest[2];

            SpawnPlanResult result = planner.FillPlan(
                entries,
                points,
                CombatVector3.Zero,
                activeEnemyCount: 0,
                output
            );

            Assert.That(result.HasSpawns, Is.False);
            Assert.That(result.ThreatSpent, Is.Zero);
        }

        [Test]
        public void DirectorState_HandlesTimingCapPauseDisableClearAndReset()
        {
            SpawnDirectorConfig config = Config(
                cap: 3,
                minimumGroupSize: 1,
                maximumGroupSize: 2,
                threatBudget: 3f,
                seed: 1u
            );
            SpawnDirectorRuntimeState state = new SpawnDirectorRuntimeState(
                config,
                spawnImmediately: true
            );

            Assert.That(state.Advance(0f), Is.True);
            Assert.That(state.ReserveSpawnSlots(99), Is.EqualTo(3));
            Assert.That(state.ActiveEnemyCount, Is.EqualTo(3));
            Assert.That(state.CapacityRemaining, Is.Zero);
            Assert.That(state.Advance(10f), Is.False);

            state.RegisterDespawned(1);
            state.SetPaused(true);
            Assert.That(state.Advance(10f), Is.False);
            Assert.That(state.ReserveSpawnSlots(1), Is.Zero);
            state.SetPaused(false);
            state.SetEnabled(false);
            Assert.That(state.ReserveSpawnSlots(1), Is.Zero);

            state.ClearActiveEnemies();
            Assert.That(state.ActiveEnemyCount, Is.Zero);
            state.Reset(isEnabled: true, spawnImmediately: false);
            Assert.That(state.IsPaused, Is.False);
            Assert.That(state.Advance(config.SpawnIntervalSeconds - 0.1f), Is.False);
            Assert.That(state.Advance(0.1f), Is.True);
            Assert.That(state.ReserveSpawnSlots(2), Is.EqualTo(2));
        }

        [Test]
        public void RuntimeTuning_ScheduleOverridesRemainBoundedAndPreserveReservations()
        {
            SpawnDirectorConfig config = Config(
                cap: 1,
                minimumGroupSize: 1,
                maximumGroupSize: 1,
                threatBudget: 2f,
                seed: 14u
            );
            SpawnDirectorRuntimeState state = new SpawnDirectorRuntimeState(
                config,
                spawnImmediately: false
            );
            SpawnPlanner planner = new SpawnPlanner(config);

            state.SetActiveEnemyCap(3);
            planner.SetActiveEnemyCap(3);
            Assert.That(state.ActiveEnemyCap, Is.EqualTo(3));
            Assert.That(planner.ActiveEnemyCap, Is.EqualTo(3));
            Assert.That(state.ReserveSpawnSlots(2), Is.EqualTo(2));
            Assert.That(state.CapacityRemaining, Is.EqualTo(1));

            state.SetSpawnIntervalSeconds(0.25f);
            state.Reset(isEnabled: true, spawnImmediately: false);
            Assert.That(state.SpawnIntervalSeconds, Is.EqualTo(0.25f));
            Assert.That(state.Advance(0.24f), Is.False);
            Assert.That(state.Advance(0.01f), Is.True);

            state.SetEnabled(false);
            Assert.That(state.ReserveSpawnSlots(1), Is.Zero);
            Assert.That(
                state.ReserveSpawnSlots(1, ignoreLifecycleState: true),
                Is.EqualTo(1)
            );
        }

        private static SpawnEligibilityFailure Evaluate(
            EnemyArchetypeConfig archetype,
            SpawnPointCandidate point,
            CombatVector3 player,
            float safeRadius,
            bool avoidView
        )
        {
            return SpawnEligibility.Evaluate(
                archetype,
                point,
                player,
                safeRadius,
                avoidView
            );
        }

        private static SpawnDirectorConfig Config(
            int cap,
            int minimumGroupSize,
            int maximumGroupSize,
            float threatBudget,
            uint seed
        )
        {
            return new SpawnDirectorConfig(
                activeEnemyCap: cap,
                spawnIntervalSeconds: 3f,
                minimumGroupSize: minimumGroupSize,
                maximumGroupSize: maximumGroupSize,
                groupThreatBudget: threatBudget,
                playerSafeRadius: 8f,
                avoidCameraView: true,
                spawnProtectionSeconds: 1.25f,
                maximumInitialAttackStaggerSeconds: 0.9f,
                useDeterministicSeed: true,
                deterministicSeed: seed
            );
        }

        private static IReadOnlyList<EnemySpawnEntry> AllEntries()
        {
            IReadOnlyList<EnemyArchetypeConfig> archetypes = EnemyArchetypeCatalog.All;
            EnemySpawnEntry[] entries = new EnemySpawnEntry[archetypes.Count];

            for (int index = 0; index < archetypes.Count; index++)
            {
                entries[index] = new EnemySpawnEntry(archetypes[index]);
            }

            return entries;
        }

        private static IReadOnlyList<SpawnPointCandidate> MixedPoints()
        {
            return new[]
            {
                Point("g1", 20f, SpawnZoneCompatibility.Ground),
                Point("g2", 24f, SpawnZoneCompatibility.Ground),
                Point("g3", 28f, SpawnZoneCompatibility.Ground),
                Point("g4", 32f, SpawnZoneCompatibility.Ground),
                Point("f1", 22f, SpawnZoneCompatibility.Flight),
                Point("f2", 26f, SpawnZoneCompatibility.Flight),
                Point("f3", 30f, SpawnZoneCompatibility.Flight),
                Point("both", 34f, SpawnZoneCompatibility.GroundAndFlight)
            };
        }

        private static SpawnPointCandidate Point(
            string id,
            float x,
            SpawnZoneCompatibility compatibility,
            bool isInsideView = false,
            bool isGroundValid = true,
            bool isFlightValid = true,
            bool isObstacleFree = true
        )
        {
            return new SpawnPointCandidate(
                id,
                new CombatVector3(x, 0f, 0f),
                compatibility,
                isEnabled: true,
                isInsideCameraView: isInsideView,
                isGroundPositionValid: isGroundValid,
                isWithinFlightBounds: isFlightValid,
                isObstacleFree: isObstacleFree
            );
        }
    }
}
