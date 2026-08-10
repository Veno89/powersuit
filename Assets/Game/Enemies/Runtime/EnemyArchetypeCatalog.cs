using System;
using System.Collections.Generic;
using Powersuit.Combat;

namespace Powersuit.Enemies
{
    /// <summary>
    /// Milestone defaults for the six required roles. These are ordinary C#
    /// values rather than assets so editor adapters can copy or override them.
    /// </summary>
    public static class EnemyArchetypeCatalog
    {
        public static EnemyAttackProfile SentryRapidFire { get; } =
            CreateAttack(
                "sentry-rapid-fire",
                "enemy-rapid-round",
                EnemyAttackStyle.RapidProjectile,
                damage: 5f,
                fireInterval: 0.48f,
                burstCount: 1,
                burstInterval: 0f,
                projectileSpeed: 34f,
                telegraph: 0.08f,
                spread: 2.6f,
                minimumRange: 3f,
                maximumRange: 36f
            );

        public static EnemyAttackProfile RiflemanBurst { get; } =
            CreateAttack(
                "rifleman-controlled-burst",
                "enemy-burst-round",
                EnemyAttackStyle.ControlledBurst,
                damage: 8f,
                fireInterval: 1.65f,
                burstCount: 3,
                burstInterval: 0.12f,
                projectileSpeed: 31f,
                telegraph: 0.18f,
                spread: 1.8f,
                minimumRange: 4f,
                maximumRange: 42f
            );

        public static EnemyAttackProfile PursuerRapidBurst { get; } =
            CreateAttack(
                "pursuer-rapid-burst",
                "enemy-rapid-round",
                EnemyAttackStyle.RapidProjectile,
                damage: 6.5f,
                fireInterval: 0.9f,
                burstCount: 2,
                burstInterval: 0.1f,
                projectileSpeed: 30f,
                telegraph: 0.12f,
                spread: 3.2f,
                minimumRange: 2.5f,
                maximumRange: 27f
            );

        public static EnemyAttackProfile HeavyShell { get; } =
            new EnemyAttackProfile(
                attackId: "heavy-artillery-shell",
                projectileId: "enemy-heavy-shell",
                style: EnemyAttackStyle.HeavyProjectile,
                damageType: DamageType.Explosive,
                ownerFaction: CombatFaction.Enemy,
                damage: 42f,
                fireIntervalSeconds: 4.6f,
                burstCount: 1,
                burstShotIntervalSeconds: 0f,
                projectileSpeed: 12f,
                telegraphSeconds: 1.25f,
                spreadDegrees: 0.6f,
                minimumRange: 8f,
                maximumRange: 56f,
                requiresLineOfSight: true
            );

        public static EnemyAttackProfile HarrierBurst { get; } =
            CreateAttack(
                "harrier-air-burst",
                "enemy-air-round",
                EnemyAttackStyle.ControlledBurst,
                damage: 7f,
                fireInterval: 1.35f,
                burstCount: 2,
                burstInterval: 0.14f,
                projectileSpeed: 29f,
                telegraph: 0.18f,
                spread: 2.2f,
                minimumRange: 5f,
                maximumRange: 44f
            );

        public static EnemyAttackProfile SkirmisherBurst { get; } =
            CreateAttack(
                "skirmisher-intermittent-burst",
                "enemy-burst-round",
                EnemyAttackStyle.ControlledBurst,
                damage: 7.5f,
                fireInterval: 1.8f,
                burstCount: 3,
                burstInterval: 0.1f,
                projectileSpeed: 33f,
                telegraph: 0.16f,
                spread: 1.5f,
                minimumRange: 7f,
                maximumRange: 48f
            );

        public static EnemyArchetypeConfig StationarySentry { get; } =
            new EnemyArchetypeConfig(
                archetypeId: "stationary-sentry",
                displayName: "Stationary Sentry",
                role: EnemyRole.StationarySentry,
                movementMode: EnemyMovementMode.Stationary,
                maximumHealth: 85f,
                movementSpeed: 0f,
                turnSpeedDegrees: 190f,
                acceleration: 0f,
                preferredMinimumDistance: 12f,
                preferredMaximumDistance: 26f,
                aggroRange: 36f,
                loseTargetRange: 46f,
                fieldOfViewDegrees: 170f,
                attackProfile: SentryRapidFire,
                patrolRadius: 0f,
                minimumFlightAltitude: 0f,
                maximumFlightAltitude: 0f,
                lateralMovementStrength: 0f,
                abilityResistance: 0.05f,
                spawnWeight: 1.4f,
                threatCost: 1f
            );

        public static EnemyArchetypeConfig PatrolRifleman { get; } =
            new EnemyArchetypeConfig(
                archetypeId: "patrol-rifleman",
                displayName: "Patrol Rifleman",
                role: EnemyRole.PatrolRifleman,
                movementMode: EnemyMovementMode.GroundPatrol,
                maximumHealth: 110f,
                movementSpeed: 4.2f,
                turnSpeedDegrees: 240f,
                acceleration: 16f,
                preferredMinimumDistance: 11f,
                preferredMaximumDistance: 23f,
                aggroRange: 38f,
                loseTargetRange: 52f,
                fieldOfViewDegrees: 125f,
                attackProfile: RiflemanBurst,
                patrolRadius: 10f,
                minimumFlightAltitude: 0f,
                maximumFlightAltitude: 0f,
                lateralMovementStrength: 0.45f,
                abilityResistance: 0.1f,
                spawnWeight: 1.25f,
                threatCost: 1.45f
            );

        public static EnemyArchetypeConfig Pursuer { get; } =
            new EnemyArchetypeConfig(
                archetypeId: "pursuer",
                displayName: "Pursuer",
                role: EnemyRole.Pursuer,
                movementMode: EnemyMovementMode.GroundPursuit,
                maximumHealth: 125f,
                movementSpeed: 6.2f,
                turnSpeedDegrees: 300f,
                acceleration: 22f,
                preferredMinimumDistance: 6f,
                preferredMaximumDistance: 12f,
                aggroRange: 42f,
                loseTargetRange: 58f,
                fieldOfViewDegrees: 145f,
                attackProfile: PursuerRapidBurst,
                patrolRadius: 0f,
                minimumFlightAltitude: 0f,
                maximumFlightAltitude: 0f,
                lateralMovementStrength: 0f,
                abilityResistance: 0.15f,
                spawnWeight: 1f,
                threatCost: 1.6f
            );

        public static EnemyArchetypeConfig HeavyArtillery { get; } =
            new EnemyArchetypeConfig(
                archetypeId: "heavy-artillery",
                displayName: "Heavy Artillery",
                role: EnemyRole.HeavyArtillery,
                movementMode: EnemyMovementMode.GroundPursuit,
                maximumHealth: 280f,
                movementSpeed: 1.65f,
                turnSpeedDegrees: 95f,
                acceleration: 5f,
                preferredMinimumDistance: 22f,
                preferredMaximumDistance: 38f,
                aggroRange: 54f,
                loseTargetRange: 66f,
                fieldOfViewDegrees: 115f,
                attackProfile: HeavyShell,
                patrolRadius: 0f,
                minimumFlightAltitude: 0f,
                maximumFlightAltitude: 0f,
                lateralMovementStrength: 0f,
                abilityResistance: 0.55f,
                spawnWeight: 0.35f,
                threatCost: 4f
            );

        public static EnemyArchetypeConfig FlyingHarrier { get; } =
            new EnemyArchetypeConfig(
                archetypeId: "flying-harrier",
                displayName: "Flying Harrier",
                role: EnemyRole.FlyingHarrier,
                movementMode: EnemyMovementMode.Flying,
                maximumHealth: 95f,
                movementSpeed: 5.6f,
                turnSpeedDegrees: 210f,
                acceleration: 13f,
                preferredMinimumDistance: 14f,
                preferredMaximumDistance: 27f,
                aggroRange: 46f,
                loseTargetRange: 62f,
                fieldOfViewDegrees: 190f,
                attackProfile: HarrierBurst,
                patrolRadius: 0f,
                minimumFlightAltitude: 6f,
                maximumFlightAltitude: 13f,
                lateralMovementStrength: 0.7f,
                abilityResistance: 0.2f,
                spawnWeight: 0.8f,
                threatCost: 2.1f
            );

        public static EnemyArchetypeConfig Skirmisher { get; } =
            new EnemyArchetypeConfig(
                archetypeId: "skirmisher",
                displayName: "Skirmisher",
                role: EnemyRole.Skirmisher,
                movementMode: EnemyMovementMode.GroundSkirmish,
                maximumHealth: 90f,
                movementSpeed: 5.8f,
                turnSpeedDegrees: 270f,
                acceleration: 20f,
                preferredMinimumDistance: 17f,
                preferredMaximumDistance: 29f,
                aggroRange: 44f,
                loseTargetRange: 60f,
                fieldOfViewDegrees: 155f,
                attackProfile: SkirmisherBurst,
                patrolRadius: 0f,
                minimumFlightAltitude: 0f,
                maximumFlightAltitude: 0f,
                lateralMovementStrength: 0.9f,
                abilityResistance: 0.08f,
                spawnWeight: 0.9f,
                threatCost: 1.85f
            );

        private static readonly IReadOnlyList<EnemyArchetypeConfig> all =
            Array.AsReadOnly(
                new[]
                {
                    StationarySentry,
                    PatrolRifleman,
                    Pursuer,
                    HeavyArtillery,
                    FlyingHarrier,
                    Skirmisher
                }
            );

        public static IReadOnlyList<EnemyArchetypeConfig> All => all;

        private static EnemyAttackProfile CreateAttack(
            string id,
            string projectileId,
            EnemyAttackStyle style,
            float damage,
            float fireInterval,
            int burstCount,
            float burstInterval,
            float projectileSpeed,
            float telegraph,
            float spread,
            float minimumRange,
            float maximumRange
        )
        {
            return new EnemyAttackProfile(
                attackId: id,
                projectileId: projectileId,
                style: style,
                damageType: DamageType.Kinetic,
                ownerFaction: CombatFaction.Enemy,
                damage: damage,
                fireIntervalSeconds: fireInterval,
                burstCount: burstCount,
                burstShotIntervalSeconds: burstInterval,
                projectileSpeed: projectileSpeed,
                telegraphSeconds: telegraph,
                spreadDegrees: spread,
                minimumRange: minimumRange,
                maximumRange: maximumRange,
                requiresLineOfSight: true
            );
        }
    }
}
