using System;
using System.Collections.Generic;
using NUnit.Framework;
using Powersuit.Combat;

namespace Powersuit.Enemies.Tests
{
    public sealed class EnemyArchitectureTests
    {
        [Test]
        public void Catalog_ProvidesSixMeaningfullyDifferentRequiredRoles()
        {
            IReadOnlyList<EnemyArchetypeConfig> all = EnemyArchetypeCatalog.All;
            HashSet<EnemyRole> roles = new HashSet<EnemyRole>();

            for (int index = 0; index < all.Count; index++)
            {
                roles.Add(all[index].Role);
            }

            Assert.That(all.Count, Is.EqualTo(6));
            Assert.That(roles.Count, Is.EqualTo(6));
            Assert.That(
                EnemyArchetypeCatalog.StationarySentry.MovementMode,
                Is.EqualTo(EnemyMovementMode.Stationary)
            );
            Assert.That(
                EnemyArchetypeCatalog.PatrolRifleman.HomeState,
                Is.EqualTo(EnemyState.Patrol)
            );
            Assert.That(
                EnemyArchetypeCatalog.Pursuer.MovementSpeed,
                Is.GreaterThan(EnemyArchetypeCatalog.PatrolRifleman.MovementSpeed)
            );
            Assert.That(EnemyArchetypeCatalog.FlyingHarrier.IsFlying, Is.True);
            Assert.That(
                EnemyArchetypeCatalog.FlyingHarrier.MaximumFlightAltitude,
                Is.GreaterThan(EnemyArchetypeCatalog.FlyingHarrier.MinimumFlightAltitude)
            );
            Assert.That(
                EnemyArchetypeCatalog.Skirmisher.LateralMovementStrength,
                Is.GreaterThan(EnemyArchetypeCatalog.PatrolRifleman.LateralMovementStrength)
            );
            Assert.That(
                EnemyArchetypeCatalog.HeavyArtillery.ThreatCost,
                Is.GreaterThan(EnemyArchetypeCatalog.StationarySentry.ThreatCost)
            );
        }

        [Test]
        public void HeavyAndRapidAttacks_AreMechanicallyDistinctAndFactionAware()
        {
            EnemyAttackProfile rapid = EnemyArchetypeCatalog.SentryRapidFire;
            EnemyAttackProfile heavy = EnemyArchetypeCatalog.HeavyShell;

            Assert.That(rapid.OwnerFaction, Is.EqualTo(CombatFaction.Enemy));
            Assert.That(heavy.DamageType, Is.EqualTo(DamageType.Explosive));
            Assert.That(heavy.Style, Is.EqualTo(EnemyAttackStyle.HeavyProjectile));
            Assert.That(heavy.Damage, Is.GreaterThan(rapid.Damage * 5f));
            Assert.That(heavy.ProjectileSpeed, Is.LessThan(rapid.ProjectileSpeed * 0.5f));
            Assert.That(heavy.TelegraphSeconds, Is.GreaterThan(rapid.TelegraphSeconds));
            Assert.That(heavy.FireIntervalSeconds, Is.GreaterThan(rapid.FireIntervalSeconds));
            Assert.That(heavy.RequiresLineOfSight, Is.True);
        }

        [Test]
        public void Configuration_RejectsInvalidCombatAndMovementValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnemyAttackProfile(
                    "invalid",
                    "test-projectile",
                    EnemyAttackStyle.RapidProjectile,
                    DamageType.Kinetic,
                    CombatFaction.None,
                    damage: 1f,
                    fireIntervalSeconds: 1f,
                    burstCount: 1,
                    burstShotIntervalSeconds: 0f,
                    projectileSpeed: 1f,
                    telegraphSeconds: 0f,
                    spreadDegrees: 0f,
                    minimumRange: 0f,
                    maximumRange: 1f,
                    requiresLineOfSight: true
                )
            );

            Assert.Throws<ArgumentOutOfRangeException>(
                () => CloneWithMovement(
                    EnemyMovementMode.Flying,
                    movementSpeed: 4f,
                    acceleration: 5f,
                    minimumFlightAltitude: 8f,
                    maximumFlightAltitude: 8f
                )
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CloneWithMovement(
                    EnemyMovementMode.GroundPatrol,
                    movementSpeed: 4f,
                    acceleration: 5f,
                    patrolRadius: 0f
                )
            );
        }

        [Test]
        public void DecisionHelper_HandlesDetectionLineOfSightRangeAndTerminalStates()
        {
            EnemyArchetypeConfig patrol = EnemyArchetypeCatalog.PatrolRifleman;
            EnemyArchetypeConfig sentry = EnemyArchetypeCatalog.StationarySentry;
            EnemyArchetypeConfig pursuer = EnemyArchetypeCatalog.Pursuer;

            Assert.That(
                EnemyDecision.SelectState(
                    patrol,
                    EnemyState.Idle,
                    Context(hasTarget: false, hasLineOfSight: false, distance: 0f),
                    attackReady: true
                ),
                Is.EqualTo(EnemyState.Patrol)
            );
            Assert.That(
                EnemyDecision.SelectState(
                    patrol,
                    EnemyState.Patrol,
                    Context(hasTarget: true, hasLineOfSight: true, distance: 45f),
                    attackReady: true
                ),
                Is.EqualTo(EnemyState.Patrol)
            );
            Assert.That(
                EnemyDecision.SelectState(
                    patrol,
                    EnemyState.Engage,
                    Context(hasTarget: true, hasLineOfSight: false, distance: 18f),
                    attackReady: true
                ),
                Is.EqualTo(EnemyState.Reposition)
            );
            Assert.That(
                EnemyDecision.SelectState(
                    sentry,
                    EnemyState.Alert,
                    Context(hasTarget: true, hasLineOfSight: false, distance: 20f),
                    attackReady: true
                ),
                Is.EqualTo(EnemyState.Alert)
            );
            Assert.That(
                EnemyDecision.SelectState(
                    pursuer,
                    EnemyState.Engage,
                    Context(hasTarget: true, hasLineOfSight: true, distance: 3f),
                    attackReady: true
                ),
                Is.EqualTo(EnemyState.Reposition)
            );
            Assert.That(
                EnemyDecision.SelectState(
                    patrol,
                    EnemyState.Engage,
                    Context(hasTarget: true, hasLineOfSight: true, distance: 18f),
                    attackReady: true
                ),
                Is.EqualTo(EnemyState.Attack)
            );
            Assert.That(
                EnemyDecision.SelectState(
                    patrol,
                    EnemyState.Attack,
                    new EnemyDecisionContext(false, false, true, true, 18f),
                    attackReady: true
                ),
                Is.EqualTo(EnemyState.Dead)
            );
        }

        [Test]
        public void RuntimeState_ResetClearsDeathBurstsProtectionCooldownAndStagger()
        {
            EnemyRuntimeState state = new EnemyRuntimeState();
            CombatVector3 firstAnchor = new CombatVector3(1f, 2f, 3f);
            state.Reset(
                EnemyArchetypeCatalog.HeavyArtillery,
                firstAnchor,
                spawnProtectionSeconds: 1f,
                initialAttackDelaySeconds: 1.2f
            );

            Assert.That(state.IsSpawnProtected, Is.True);
            Assert.That(state.CanBeginAttack, Is.False);

            state.Advance(1.2f);
            Assert.That(
                state.Evaluate(Context(true, true, 30f)),
                Is.EqualTo(EnemyState.Attack)
            );
            Assert.That(state.TryBeginAttack(), Is.True);
            Assert.That(state.TryConsumeBurstShot(), Is.True);
            Assert.That(state.BurstShotsRemaining, Is.Zero);
            Assert.That(state.ApplyStagger(0.5f), Is.True);
            state.MarkDead();

            CombatVector3 secondAnchor = new CombatVector3(9f, 0f, -4f);
            state.Reset(EnemyArchetypeCatalog.PatrolRifleman, secondAnchor);

            Assert.That(state.Config, Is.SameAs(EnemyArchetypeCatalog.PatrolRifleman));
            Assert.That(state.SpawnAnchor, Is.EqualTo(secondAnchor));
            Assert.That(state.CurrentState, Is.EqualTo(EnemyState.Patrol));
            Assert.That(state.IsAlive, Is.True);
            Assert.That(state.IsSpawnProtected, Is.False);
            Assert.That(state.AttackCooldownRemaining, Is.Zero);
            Assert.That(state.StaggerRemaining, Is.Zero);
            Assert.That(state.BurstShotsRemaining, Is.Zero);
            Assert.That(state.AttacksStarted, Is.Zero);
            Assert.That(state.CanBeginAttack, Is.True);
        }

        private static EnemyDecisionContext Context(
            bool hasTarget,
            bool hasLineOfSight,
            float distance
        )
        {
            return new EnemyDecisionContext(
                isAlive: true,
                isStaggered: false,
                hasTarget: hasTarget,
                hasLineOfSight: hasLineOfSight,
                targetDistance: distance
            );
        }

        private static EnemyArchetypeConfig CloneWithMovement(
            EnemyMovementMode movementMode,
            float movementSpeed,
            float acceleration,
            float patrolRadius = 0f,
            float minimumFlightAltitude = 0f,
            float maximumFlightAltitude = 0f
        )
        {
            return new EnemyArchetypeConfig(
                archetypeId: "validation-test",
                displayName: "Validation Test",
                role: EnemyRole.PatrolRifleman,
                movementMode: movementMode,
                maximumHealth: 10f,
                movementSpeed: movementSpeed,
                turnSpeedDegrees: 90f,
                acceleration: acceleration,
                preferredMinimumDistance: 1f,
                preferredMaximumDistance: 2f,
                aggroRange: 3f,
                loseTargetRange: 4f,
                fieldOfViewDegrees: 90f,
                attackProfile: EnemyArchetypeCatalog.SentryRapidFire,
                patrolRadius: patrolRadius,
                minimumFlightAltitude: minimumFlightAltitude,
                maximumFlightAltitude: maximumFlightAltitude,
                lateralMovementStrength: 0f,
                abilityResistance: 0f,
                spawnWeight: 1f,
                threatCost: 1f
            );
        }
    }
}
