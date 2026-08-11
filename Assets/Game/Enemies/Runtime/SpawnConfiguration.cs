using System;
using Powersuit.Combat;

namespace Powersuit.Enemies
{
    [Flags]
    public enum SpawnZoneCompatibility
    {
        None = 0,
        Ground = 1,
        Flight = 2,
        GroundAndFlight = Ground | Flight
    }

    [Serializable]
    public sealed class SpawnDirectorConfig
    {
        public SpawnDirectorConfig(
            int activeEnemyCap,
            float spawnIntervalSeconds,
            int minimumGroupSize,
            int maximumGroupSize,
            float groupThreatBudget,
            float playerSafeRadius,
            bool avoidCameraView,
            float spawnProtectionSeconds,
            float maximumInitialAttackStaggerSeconds,
            bool useDeterministicSeed,
            uint deterministicSeed
        )
        {
            if (activeEnemyCap < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(activeEnemyCap));
            }

            EnemyAttackProfile.RequirePositive(
                spawnIntervalSeconds,
                nameof(spawnIntervalSeconds)
            );

            if (minimumGroupSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumGroupSize));
            }

            if (maximumGroupSize < minimumGroupSize || maximumGroupSize > activeEnemyCap)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumGroupSize));
            }

            EnemyAttackProfile.RequirePositive(groupThreatBudget, nameof(groupThreatBudget));
            EnemyAttackProfile.RequireNonNegative(playerSafeRadius, nameof(playerSafeRadius));
            EnemyAttackProfile.RequireNonNegative(
                spawnProtectionSeconds,
                nameof(spawnProtectionSeconds)
            );
            EnemyAttackProfile.RequireNonNegative(
                maximumInitialAttackStaggerSeconds,
                nameof(maximumInitialAttackStaggerSeconds)
            );

            ActiveEnemyCap = activeEnemyCap;
            SpawnIntervalSeconds = spawnIntervalSeconds;
            MinimumGroupSize = minimumGroupSize;
            MaximumGroupSize = maximumGroupSize;
            GroupThreatBudget = groupThreatBudget;
            PlayerSafeRadius = playerSafeRadius;
            AvoidCameraView = avoidCameraView;
            SpawnProtectionSeconds = spawnProtectionSeconds;
            MaximumInitialAttackStaggerSeconds = maximumInitialAttackStaggerSeconds;
            UseDeterministicSeed = useDeterministicSeed;
            DeterministicSeed = deterministicSeed;
        }

        public int ActiveEnemyCap { get; }
        public float SpawnIntervalSeconds { get; }
        public int MinimumGroupSize { get; }
        public int MaximumGroupSize { get; }
        public float GroupThreatBudget { get; }
        public float PlayerSafeRadius { get; }
        public bool AvoidCameraView { get; }
        public float SpawnProtectionSeconds { get; }
        public float MaximumInitialAttackStaggerSeconds { get; }
        public bool UseDeterministicSeed { get; }
        public uint DeterministicSeed { get; }
    }

    [Serializable]
    public sealed class EnemySpawnEntry
    {
        public EnemySpawnEntry(
            EnemyArchetypeConfig archetype,
            bool isEnabled = true,
            float weightMultiplier = 1f
        )
        {
            if (archetype == null)
            {
                throw new ArgumentNullException(nameof(archetype));
            }

            EnemyAttackProfile.RequirePositive(weightMultiplier, nameof(weightMultiplier));

            Archetype = archetype;
            IsEnabled = isEnabled;
            WeightMultiplier = weightMultiplier;
        }

        public EnemyArchetypeConfig Archetype { get; }
        public bool IsEnabled { get; }
        public float WeightMultiplier { get; }
        public float EffectiveWeight => Archetype.SpawnWeight * WeightMultiplier;
    }

    /// <summary>
    /// Adapter-produced validation snapshot for one possible spawn location.
    /// No physics or camera dependency leaks into the planner.
    /// </summary>
    public readonly struct SpawnPointCandidate
    {
        private const SpawnZoneCompatibility KnownCompatibility =
            SpawnZoneCompatibility.GroundAndFlight;

        public SpawnPointCandidate(
            string zoneId,
            CombatVector3 position,
            SpawnZoneCompatibility compatibility,
            bool isEnabled = true,
            bool isInsideCameraView = false,
            bool isGroundPositionValid = true,
            bool isWithinFlightBounds = true,
            bool isObstacleFree = true
        )
        {
            EnemyAttackProfile.RequireText(zoneId, nameof(zoneId));

            if (
                compatibility == SpawnZoneCompatibility.None ||
                (compatibility & ~KnownCompatibility) != 0
            )
            {
                throw new ArgumentOutOfRangeException(nameof(compatibility));
            }

            ZoneId = zoneId;
            Position = position;
            Compatibility = compatibility;
            IsEnabled = isEnabled;
            IsInsideCameraView = isInsideCameraView;
            IsGroundPositionValid = isGroundPositionValid;
            IsWithinFlightBounds = isWithinFlightBounds;
            IsObstacleFree = isObstacleFree;
        }

        public string ZoneId { get; }
        public CombatVector3 Position { get; }
        public SpawnZoneCompatibility Compatibility { get; }
        public bool IsEnabled { get; }
        public bool IsInsideCameraView { get; }
        public bool IsGroundPositionValid { get; }
        public bool IsWithinFlightBounds { get; }
        public bool IsObstacleFree { get; }
    }

    public enum SpawnEligibilityFailure
    {
        None = 0,
        Disabled = 1,
        InsidePlayerSafeRadius = 2,
        InsideCameraView = 3,
        IncompatibleZone = 4,
        InvalidGroundPosition = 5,
        OutsideFlightBounds = 6,
        FlightPathObstructed = 7,
        GroundPositionObstructed = 8
    }

    public static class SpawnEligibility
    {
        public static SpawnEligibilityFailure Evaluate(
            EnemyArchetypeConfig archetype,
            SpawnPointCandidate candidate,
            CombatVector3 playerPosition,
            float playerSafeRadius,
            bool avoidCameraView
        )
        {
            if (archetype == null)
            {
                throw new ArgumentNullException(nameof(archetype));
            }

            EnemyAttackProfile.RequireNonNegative(playerSafeRadius, nameof(playerSafeRadius));

            if (!candidate.IsEnabled)
            {
                return SpawnEligibilityFailure.Disabled;
            }

            float deltaX = candidate.Position.X - playerPosition.X;
            float deltaY = candidate.Position.Y - playerPosition.Y;
            float deltaZ = candidate.Position.Z - playerPosition.Z;
            float safeRadiusSquared = playerSafeRadius * playerSafeRadius;
            float distanceSquared = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;

            if (distanceSquared < safeRadiusSquared)
            {
                return SpawnEligibilityFailure.InsidePlayerSafeRadius;
            }

            if (avoidCameraView && candidate.IsInsideCameraView)
            {
                return SpawnEligibilityFailure.InsideCameraView;
            }

            if (archetype.IsFlying)
            {
                if ((candidate.Compatibility & SpawnZoneCompatibility.Flight) == 0)
                {
                    return SpawnEligibilityFailure.IncompatibleZone;
                }

                if (!candidate.IsWithinFlightBounds)
                {
                    return SpawnEligibilityFailure.OutsideFlightBounds;
                }

                if (!candidate.IsObstacleFree)
                {
                    return SpawnEligibilityFailure.FlightPathObstructed;
                }

                return SpawnEligibilityFailure.None;
            }

            if ((candidate.Compatibility & SpawnZoneCompatibility.Ground) == 0)
            {
                return SpawnEligibilityFailure.IncompatibleZone;
            }

            if (!candidate.IsGroundPositionValid)
            {
                return SpawnEligibilityFailure.InvalidGroundPosition;
            }

            return candidate.IsObstacleFree
                ? SpawnEligibilityFailure.None
                : SpawnEligibilityFailure.GroundPositionObstructed;
        }
    }
}
