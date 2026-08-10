using Powersuit.Combat;

namespace Powersuit.Abilities
{
    public readonly struct ShoulderRocketLaunch
    {
        public ShoulderRocketLaunch(
            CombatVector3 origin,
            CombatVector3 aimPoint,
            CombatVector3 direction,
            float distance
        )
        {
            Origin = origin;
            AimPoint = aimPoint;
            Direction = direction;
            Distance = distance;
        }

        public CombatVector3 Origin { get; }
        public CombatVector3 AimPoint { get; }
        public CombatVector3 Direction { get; }
        public float Distance { get; }
    }

    public readonly struct ShoulderRocketLaunchResult
    {
        private ShoulderRocketLaunchResult(
            bool accepted,
            AbilityUseFailure failure,
            ShoulderRocketLaunch launch
        )
        {
            Accepted = accepted;
            Failure = failure;
            Launch = launch;
        }

        public bool Accepted { get; }
        public AbilityUseFailure Failure { get; }
        public ShoulderRocketLaunch Launch { get; }

        public static ShoulderRocketLaunchResult Success(
            ShoulderRocketLaunch launch
        )
        {
            return new ShoulderRocketLaunchResult(
                true,
                AbilityUseFailure.None,
                launch
            );
        }

        public static ShoulderRocketLaunchResult Rejected(
            AbilityUseFailure failure
        )
        {
            return new ShoulderRocketLaunchResult(false, failure, default);
        }
    }

    /// <summary>
    /// Owns launch acceptance and cooldown. Projectile spawning, travel,
    /// collision, pooling, explosion queries, and presentation stay in Unity.
    /// </summary>
    public sealed class ShoulderRocketState
    {
        private readonly AbilityCooldownState cooldown;

        public ShoulderRocketState(float cooldownSeconds)
        {
            cooldown = new AbilityCooldownState(cooldownSeconds);
        }

        public float CooldownDuration => cooldown.DurationSeconds;
        public float CooldownRemaining => cooldown.RemainingSeconds;
        public float CooldownNormalized => cooldown.NormalizedRemaining;
        public bool CanLaunch => cooldown.IsReady;

        public ShoulderRocketLaunchResult TryLaunch(
            CombatVector3 origin,
            CombatVector3 aimPoint
        )
        {
            if (
                !AbilityVectorMath.TryDirection(
                    origin,
                    aimPoint,
                    out CombatVector3 direction,
                    out float distance
                )
            )
            {
                return ShoulderRocketLaunchResult.Rejected(
                    AbilityUseFailure.InvalidLaunch
                );
            }

            if (!cooldown.TryConsume())
            {
                return ShoulderRocketLaunchResult.Rejected(
                    AbilityUseFailure.Cooldown
                );
            }

            return ShoulderRocketLaunchResult.Success(
                new ShoulderRocketLaunch(
                    origin,
                    aimPoint,
                    direction,
                    distance
                )
            );
        }

        public void Advance(float deltaSeconds)
        {
            cooldown.Advance(deltaSeconds);
        }

        public void ResetCooldown()
        {
            cooldown.Reset();
        }

        public void ResetCooldown(float remainingSeconds)
        {
            cooldown.Reset(remainingSeconds);
        }
    }
}
