using System;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Enemies.UnityAdapters
{
    /// <summary>
    /// Unity authoring adapter for the engine-independent enemy configuration.
    /// One asset shape supports all required roles; behavior is selected by data,
    /// not by attaching a role-specific MonoBehaviour.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EnemyArchetype",
        menuName = "PowerSuit/Enemies/Archetype Definition"
    )]
    public sealed class EnemyArchetypeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string archetypeId = "stationary-sentry";
        [SerializeField] private string displayName = "Stationary Sentry";
        [SerializeField] private EnemyRole role = EnemyRole.StationarySentry;
        [SerializeField] private EnemyMovementMode movementMode = EnemyMovementMode.Stationary;

        [Header("Durability and locomotion")]
        [Min(0.01f)] [SerializeField] private float maximumHealth = 85f;
        [Min(0f)] [SerializeField] private float movementSpeed;
        [Min(0.01f)] [SerializeField] private float turnSpeedDegrees = 190f;
        [Min(0f)] [SerializeField] private float acceleration;
        [Min(0f)] [SerializeField] private float preferredMinimumDistance = 12f;
        [Min(0f)] [SerializeField] private float preferredMaximumDistance = 26f;
        [Min(0.01f)] [SerializeField] private float aggroRange = 36f;
        [Min(0.01f)] [SerializeField] private float loseTargetRange = 46f;
        [Range(1f, 360f)] [SerializeField] private float fieldOfViewDegrees = 170f;

        [Header("Role movement")]
        [Min(0f)] [SerializeField] private float patrolRadius;
        [Min(0f)] [SerializeField] private float minimumFlightAltitude;
        [Min(0f)] [SerializeField] private float maximumFlightAltitude;
        [Range(0f, 1f)] [SerializeField] private float lateralMovementStrength;
        [Range(0f, 1f)] [SerializeField] private float abilityResistance = 0.05f;

        [Header("Spawning")]
        [Min(0.01f)] [SerializeField] private float spawnWeight = 1.4f;
        [Min(0.01f)] [SerializeField] private float threatCost = 1f;

        [Header("Attack")]
        [SerializeField] private string attackId = "sentry-rapid-fire";
        [SerializeField] private string projectileId = "enemy-rapid-round";
        [SerializeField] private EnemyAttackStyle attackStyle = EnemyAttackStyle.RapidProjectile;
        [SerializeField] private DamageType damageType = DamageType.Kinetic;
        [SerializeField] private CombatFaction ownerFaction = CombatFaction.Enemy;
        [Min(0.01f)] [SerializeField] private float damage = 5f;
        [Min(0.01f)] [SerializeField] private float fireIntervalSeconds = 0.48f;
        [Min(1)] [SerializeField] private int burstCount = 1;
        [Min(0f)] [SerializeField] private float burstShotIntervalSeconds;
        [Min(0.01f)] [SerializeField] private float projectileSpeed = 34f;
        [Min(0f)] [SerializeField] private float telegraphSeconds = 0.08f;
        [Range(0f, 180f)] [SerializeField] private float spreadDegrees = 2.6f;
        [Min(0f)] [SerializeField] private float minimumRange = 3f;
        [Min(0.01f)] [SerializeField] private float maximumRange = 36f;
        [SerializeField] private bool requiresLineOfSight = true;

        public string ArchetypeId => archetypeId;
        public string DisplayName => displayName;
        public EnemyRole Role => role;
        public EnemyMovementMode MovementMode => movementMode;

        public EnemyAttackProfile CreateAttackProfile()
        {
            return new EnemyAttackProfile(
                attackId,
                projectileId,
                attackStyle,
                damageType,
                ownerFaction,
                damage,
                fireIntervalSeconds,
                burstCount,
                burstShotIntervalSeconds,
                projectileSpeed,
                telegraphSeconds,
                spreadDegrees,
                minimumRange,
                maximumRange,
                requiresLineOfSight
            );
        }

        public EnemyArchetypeConfig CreateRuntimeConfig()
        {
            return new EnemyArchetypeConfig(
                archetypeId,
                displayName,
                role,
                movementMode,
                maximumHealth,
                movementSpeed,
                turnSpeedDegrees,
                acceleration,
                preferredMinimumDistance,
                preferredMaximumDistance,
                aggroRange,
                loseTargetRange,
                fieldOfViewDegrees,
                CreateAttackProfile(),
                patrolRadius,
                minimumFlightAltitude,
                maximumFlightAltitude,
                lateralMovementStrength,
                abilityResistance,
                spawnWeight,
                threatCost
            );
        }

        public bool TryCreateRuntimeConfig(
            out EnemyArchetypeConfig config,
            out string validationError
        )
        {
            try
            {
                config = CreateRuntimeConfig();
                validationError = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException
            )
            {
                config = null;
                validationError = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Copies the milestone's authored baseline for one of the six required
        /// roles. Fields remain editable after the copy.
        /// </summary>
        public void ApplyRolePreset(EnemyRole requiredRole)
        {
            EnemyArchetypeConfig source = GetCatalogConfig(requiredRole);
            CopyFrom(source);
        }

        [ContextMenu("Apply Current Role Preset")]
        private void ApplyCurrentRolePreset()
        {
            ApplyRolePreset(role);
        }

        private void CopyFrom(EnemyArchetypeConfig source)
        {
            archetypeId = source.ArchetypeId;
            displayName = source.DisplayName;
            role = source.Role;
            movementMode = source.MovementMode;
            maximumHealth = source.MaximumHealth;
            movementSpeed = source.MovementSpeed;
            turnSpeedDegrees = source.TurnSpeedDegrees;
            acceleration = source.Acceleration;
            preferredMinimumDistance = source.PreferredMinimumDistance;
            preferredMaximumDistance = source.PreferredMaximumDistance;
            aggroRange = source.AggroRange;
            loseTargetRange = source.LoseTargetRange;
            fieldOfViewDegrees = source.FieldOfViewDegrees;
            patrolRadius = source.PatrolRadius;
            minimumFlightAltitude = source.MinimumFlightAltitude;
            maximumFlightAltitude = source.MaximumFlightAltitude;
            lateralMovementStrength = source.LateralMovementStrength;
            abilityResistance = source.AbilityResistance;
            spawnWeight = source.SpawnWeight;
            threatCost = source.ThreatCost;

            EnemyAttackProfile attack = source.AttackProfile;
            attackId = attack.AttackId;
            projectileId = attack.ProjectileId;
            attackStyle = attack.Style;
            damageType = attack.DamageType;
            ownerFaction = attack.OwnerFaction;
            damage = attack.Damage;
            fireIntervalSeconds = attack.FireIntervalSeconds;
            burstCount = attack.BurstCount;
            burstShotIntervalSeconds = attack.BurstShotIntervalSeconds;
            projectileSpeed = attack.ProjectileSpeed;
            telegraphSeconds = attack.TelegraphSeconds;
            spreadDegrees = attack.SpreadDegrees;
            minimumRange = attack.MinimumRange;
            maximumRange = attack.MaximumRange;
            requiresLineOfSight = attack.RequiresLineOfSight;
        }

        private static EnemyArchetypeConfig GetCatalogConfig(EnemyRole requiredRole)
        {
            switch (requiredRole)
            {
                case EnemyRole.StationarySentry:
                    return EnemyArchetypeCatalog.StationarySentry;
                case EnemyRole.PatrolRifleman:
                    return EnemyArchetypeCatalog.PatrolRifleman;
                case EnemyRole.Pursuer:
                    return EnemyArchetypeCatalog.Pursuer;
                case EnemyRole.HeavyArtillery:
                    return EnemyArchetypeCatalog.HeavyArtillery;
                case EnemyRole.FlyingHarrier:
                    return EnemyArchetypeCatalog.FlyingHarrier;
                case EnemyRole.Skirmisher:
                    return EnemyArchetypeCatalog.Skirmisher;
                default:
                    throw new ArgumentOutOfRangeException(nameof(requiredRole));
            }
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(archetypeId))
            {
                archetypeId = role.ToString();
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = role.ToString();
            }

            if (string.IsNullOrWhiteSpace(attackId))
            {
                attackId = archetypeId + "-attack";
            }

            if (string.IsNullOrWhiteSpace(projectileId))
            {
                projectileId = "enemy-projectile";
            }

            maximumHealth = Mathf.Max(0.01f, maximumHealth);
            movementSpeed = Mathf.Max(0f, movementSpeed);
            turnSpeedDegrees = Mathf.Max(0.01f, turnSpeedDegrees);
            acceleration = Mathf.Max(0f, acceleration);
            preferredMinimumDistance = Mathf.Max(0f, preferredMinimumDistance);
            preferredMaximumDistance = Mathf.Max(
                preferredMinimumDistance,
                preferredMaximumDistance
            );
            aggroRange = Mathf.Max(0.01f, preferredMaximumDistance, aggroRange);
            loseTargetRange = Mathf.Max(aggroRange, loseTargetRange);
            fieldOfViewDegrees = Mathf.Clamp(fieldOfViewDegrees, 1f, 360f);
            patrolRadius = Mathf.Max(0f, patrolRadius);
            minimumFlightAltitude = Mathf.Max(0f, minimumFlightAltitude);
            maximumFlightAltitude = Mathf.Max(minimumFlightAltitude, maximumFlightAltitude);
            lateralMovementStrength = Mathf.Clamp01(lateralMovementStrength);
            abilityResistance = Mathf.Clamp01(abilityResistance);
            spawnWeight = Mathf.Max(0.01f, spawnWeight);
            threatCost = Mathf.Max(0.01f, threatCost);

            damage = Mathf.Max(0.01f, damage);
            fireIntervalSeconds = Mathf.Max(0.01f, fireIntervalSeconds);
            burstCount = Mathf.Max(1, burstCount);
            burstShotIntervalSeconds = Mathf.Max(0f, burstShotIntervalSeconds);
            projectileSpeed = Mathf.Max(0.01f, projectileSpeed);
            telegraphSeconds = Mathf.Max(0f, telegraphSeconds);
            spreadDegrees = Mathf.Clamp(spreadDegrees, 0f, 180f);
            minimumRange = Mathf.Max(0f, minimumRange);
            maximumRange = Mathf.Max(
                0.01f,
                minimumRange,
                preferredMinimumDistance,
                maximumRange
            );

            if (movementMode != EnemyMovementMode.Stationary)
            {
                movementSpeed = Mathf.Max(0.01f, movementSpeed);
                acceleration = Mathf.Max(0.01f, acceleration);
            }

            if (movementMode == EnemyMovementMode.GroundPatrol)
            {
                patrolRadius = Mathf.Max(0.01f, patrolRadius);
            }

            if (movementMode == EnemyMovementMode.Flying)
            {
                maximumFlightAltitude = Mathf.Max(
                    minimumFlightAltitude + 0.01f,
                    maximumFlightAltitude
                );
            }

            if (ownerFaction == CombatFaction.None)
            {
                ownerFaction = CombatFaction.Enemy;
            }
        }
    }
}
