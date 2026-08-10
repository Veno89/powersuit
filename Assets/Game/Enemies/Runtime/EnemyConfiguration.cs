using System;
using Powersuit.Combat;

namespace Powersuit.Enemies
{
    public enum EnemyRole
    {
        StationarySentry = 0,
        PatrolRifleman = 1,
        Pursuer = 2,
        HeavyArtillery = 3,
        FlyingHarrier = 4,
        Skirmisher = 5
    }

    public enum EnemyMovementMode
    {
        Stationary = 0,
        GroundPatrol = 1,
        GroundPursuit = 2,
        GroundSkirmish = 3,
        Flying = 4
    }

    public enum EnemyState
    {
        Idle = 0,
        Patrol = 1,
        Alert = 2,
        Engage = 3,
        Reposition = 4,
        Attack = 5,
        Staggered = 6,
        Dead = 7
    }

    public enum EnemyAttackStyle
    {
        RapidProjectile = 0,
        ControlledBurst = 1,
        HeavyProjectile = 2
    }

    /// <summary>
    /// Immutable, engine-independent attack data. Unity authoring assets should
    /// translate their serialized values into this validated runtime shape.
    /// </summary>
    [Serializable]
    public sealed class EnemyAttackProfile
    {
        public EnemyAttackProfile(
            string attackId,
            string projectileId,
            EnemyAttackStyle style,
            DamageType damageType,
            CombatFaction ownerFaction,
            float damage,
            float fireIntervalSeconds,
            int burstCount,
            float burstShotIntervalSeconds,
            float projectileSpeed,
            float telegraphSeconds,
            float spreadDegrees,
            float minimumRange,
            float maximumRange,
            bool requiresLineOfSight
        )
        {
            RequireText(attackId, nameof(attackId));
            RequireText(projectileId, nameof(projectileId));
            RequireDefined(style, nameof(style));
            RequireDefined(damageType, nameof(damageType));

            if (!CombatFactionPolicy.IsKnown(ownerFaction) || ownerFaction == CombatFaction.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ownerFaction),
                    "An attack must have a configured combat faction."
                );
            }

            RequirePositive(damage, nameof(damage));
            RequirePositive(fireIntervalSeconds, nameof(fireIntervalSeconds));

            if (burstCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(burstCount),
                    "Burst count must be at least one."
                );
            }

            RequireNonNegative(burstShotIntervalSeconds, nameof(burstShotIntervalSeconds));
            RequirePositive(projectileSpeed, nameof(projectileSpeed));
            RequireNonNegative(telegraphSeconds, nameof(telegraphSeconds));
            RequireRange(spreadDegrees, 0f, 180f, nameof(spreadDegrees));
            RequireNonNegative(minimumRange, nameof(minimumRange));
            RequirePositive(maximumRange, nameof(maximumRange));

            if (maximumRange < minimumRange)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRange),
                    "Maximum attack range cannot be less than minimum attack range."
                );
            }

            AttackId = attackId;
            ProjectileId = projectileId;
            Style = style;
            DamageType = damageType;
            OwnerFaction = ownerFaction;
            Damage = damage;
            FireIntervalSeconds = fireIntervalSeconds;
            BurstCount = burstCount;
            BurstShotIntervalSeconds = burstShotIntervalSeconds;
            ProjectileSpeed = projectileSpeed;
            TelegraphSeconds = telegraphSeconds;
            SpreadDegrees = spreadDegrees;
            MinimumRange = minimumRange;
            MaximumRange = maximumRange;
            RequiresLineOfSight = requiresLineOfSight;
        }

        public string AttackId { get; }
        public string ProjectileId { get; }
        public EnemyAttackStyle Style { get; }
        public DamageType DamageType { get; }
        public CombatFaction OwnerFaction { get; }
        public float Damage { get; }
        public float FireIntervalSeconds { get; }
        public int BurstCount { get; }
        public float BurstShotIntervalSeconds { get; }
        public float ProjectileSpeed { get; }
        public float TelegraphSeconds { get; }
        public float SpreadDegrees { get; }
        public float MinimumRange { get; }
        public float MaximumRange { get; }
        public bool RequiresLineOfSight { get; }

        internal static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", parameterName);
            }
        }

        internal static void RequirePositive(float value, string parameterName)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "A finite value greater than zero is required."
                );
            }
        }

        internal static void RequireNonNegative(float value, string parameterName)
        {
            if (!IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "A finite non-negative value is required."
                );
            }
        }

        internal static void RequireRange(
            float value,
            float minimum,
            float maximum,
            string parameterName
        )
        {
            if (!IsFinite(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        internal static void RequireDefined<T>(T value, string parameterName)
            where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Validated runtime values shared by all enemy adapters. Role and movement
    /// are independent so future combinations do not require another AI script.
    /// </summary>
    [Serializable]
    public sealed class EnemyArchetypeConfig
    {
        public EnemyArchetypeConfig(
            string archetypeId,
            string displayName,
            EnemyRole role,
            EnemyMovementMode movementMode,
            float maximumHealth,
            float movementSpeed,
            float turnSpeedDegrees,
            float acceleration,
            float preferredMinimumDistance,
            float preferredMaximumDistance,
            float aggroRange,
            float loseTargetRange,
            float fieldOfViewDegrees,
            EnemyAttackProfile attackProfile,
            float patrolRadius,
            float minimumFlightAltitude,
            float maximumFlightAltitude,
            float lateralMovementStrength,
            float abilityResistance,
            float spawnWeight,
            float threatCost
        )
        {
            EnemyAttackProfile.RequireText(archetypeId, nameof(archetypeId));
            EnemyAttackProfile.RequireText(displayName, nameof(displayName));
            EnemyAttackProfile.RequireDefined(role, nameof(role));
            EnemyAttackProfile.RequireDefined(movementMode, nameof(movementMode));
            EnemyAttackProfile.RequirePositive(maximumHealth, nameof(maximumHealth));
            EnemyAttackProfile.RequireNonNegative(movementSpeed, nameof(movementSpeed));
            EnemyAttackProfile.RequirePositive(turnSpeedDegrees, nameof(turnSpeedDegrees));
            EnemyAttackProfile.RequireNonNegative(acceleration, nameof(acceleration));
            EnemyAttackProfile.RequireNonNegative(
                preferredMinimumDistance,
                nameof(preferredMinimumDistance)
            );
            EnemyAttackProfile.RequireNonNegative(
                preferredMaximumDistance,
                nameof(preferredMaximumDistance)
            );

            if (preferredMaximumDistance < preferredMinimumDistance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(preferredMaximumDistance),
                    "Preferred maximum distance cannot be less than the minimum."
                );
            }

            EnemyAttackProfile.RequirePositive(aggroRange, nameof(aggroRange));
            EnemyAttackProfile.RequirePositive(loseTargetRange, nameof(loseTargetRange));

            if (aggroRange < preferredMaximumDistance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(aggroRange),
                    "Aggro range must include the preferred combat band."
                );
            }

            if (loseTargetRange < aggroRange)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(loseTargetRange),
                    "Lose-target range cannot be less than aggro range."
                );
            }

            EnemyAttackProfile.RequireRange(
                fieldOfViewDegrees,
                1f,
                360f,
                nameof(fieldOfViewDegrees)
            );

            if (attackProfile == null)
            {
                throw new ArgumentNullException(nameof(attackProfile));
            }

            if (attackProfile.MaximumRange < preferredMinimumDistance)
            {
                throw new ArgumentException(
                    "The attack profile cannot reach the preferred combat band.",
                    nameof(attackProfile)
                );
            }

            EnemyAttackProfile.RequireNonNegative(patrolRadius, nameof(patrolRadius));
            EnemyAttackProfile.RequireNonNegative(
                minimumFlightAltitude,
                nameof(minimumFlightAltitude)
            );
            EnemyAttackProfile.RequireNonNegative(
                maximumFlightAltitude,
                nameof(maximumFlightAltitude)
            );
            EnemyAttackProfile.RequireRange(
                lateralMovementStrength,
                0f,
                1f,
                nameof(lateralMovementStrength)
            );
            EnemyAttackProfile.RequireRange(
                abilityResistance,
                0f,
                1f,
                nameof(abilityResistance)
            );
            EnemyAttackProfile.RequirePositive(spawnWeight, nameof(spawnWeight));
            EnemyAttackProfile.RequirePositive(threatCost, nameof(threatCost));

            if (movementMode != EnemyMovementMode.Stationary)
            {
                EnemyAttackProfile.RequirePositive(movementSpeed, nameof(movementSpeed));
                EnemyAttackProfile.RequirePositive(acceleration, nameof(acceleration));
            }

            if (movementMode == EnemyMovementMode.GroundPatrol && patrolRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(patrolRadius),
                    "A patrolling archetype requires a patrol radius."
                );
            }

            if (
                movementMode == EnemyMovementMode.Flying &&
                maximumFlightAltitude <= minimumFlightAltitude
            )
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumFlightAltitude),
                    "Flying archetypes require a non-empty altitude band."
                );
            }

            ArchetypeId = archetypeId;
            DisplayName = displayName;
            Role = role;
            MovementMode = movementMode;
            MaximumHealth = maximumHealth;
            MovementSpeed = movementSpeed;
            TurnSpeedDegrees = turnSpeedDegrees;
            Acceleration = acceleration;
            PreferredMinimumDistance = preferredMinimumDistance;
            PreferredMaximumDistance = preferredMaximumDistance;
            AggroRange = aggroRange;
            LoseTargetRange = loseTargetRange;
            FieldOfViewDegrees = fieldOfViewDegrees;
            AttackProfile = attackProfile;
            PatrolRadius = patrolRadius;
            MinimumFlightAltitude = minimumFlightAltitude;
            MaximumFlightAltitude = maximumFlightAltitude;
            LateralMovementStrength = lateralMovementStrength;
            AbilityResistance = abilityResistance;
            SpawnWeight = spawnWeight;
            ThreatCost = threatCost;
        }

        public string ArchetypeId { get; }
        public string DisplayName { get; }
        public EnemyRole Role { get; }
        public EnemyMovementMode MovementMode { get; }
        public float MaximumHealth { get; }
        public float MovementSpeed { get; }
        public float TurnSpeedDegrees { get; }
        public float Acceleration { get; }
        public float PreferredMinimumDistance { get; }
        public float PreferredMaximumDistance { get; }
        public float AggroRange { get; }
        public float LoseTargetRange { get; }
        public float FieldOfViewDegrees { get; }
        public EnemyAttackProfile AttackProfile { get; }
        public float PatrolRadius { get; }
        public float MinimumFlightAltitude { get; }
        public float MaximumFlightAltitude { get; }
        public float LateralMovementStrength { get; }
        public float AbilityResistance { get; }
        public float SpawnWeight { get; }
        public float ThreatCost { get; }

        public bool CanMove => MovementMode != EnemyMovementMode.Stationary;
        public bool IsFlying => MovementMode == EnemyMovementMode.Flying;
        public EnemyState HomeState =>
            MovementMode == EnemyMovementMode.GroundPatrol
                ? EnemyState.Patrol
                : EnemyState.Idle;
    }
}
