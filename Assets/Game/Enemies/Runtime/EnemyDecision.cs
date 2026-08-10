using System;

namespace Powersuit.Enemies
{
    /// <summary>
    /// Engine observations reduced to deterministic decision inputs.
    /// Navigation, physics, and vision adapters remain responsible for producing
    /// these values.
    /// </summary>
    public readonly struct EnemyDecisionContext
    {
        public EnemyDecisionContext(
            bool isAlive,
            bool isStaggered,
            bool hasTarget,
            bool hasLineOfSight,
            float targetDistance
        )
        {
            if (
                float.IsNaN(targetDistance) ||
                float.IsInfinity(targetDistance) ||
                targetDistance < 0f
            )
            {
                throw new ArgumentOutOfRangeException(nameof(targetDistance));
            }

            IsAlive = isAlive;
            IsStaggered = isStaggered;
            HasTarget = hasTarget;
            HasLineOfSight = hasLineOfSight;
            TargetDistance = targetDistance;
        }

        public bool IsAlive { get; }
        public bool IsStaggered { get; }
        public bool HasTarget { get; }
        public bool HasLineOfSight { get; }
        public float TargetDistance { get; }
    }

    public static class EnemyDecision
    {
        public static EnemyState SelectState(
            EnemyArchetypeConfig config,
            EnemyState currentState,
            EnemyDecisionContext context,
            bool attackReady
        )
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (!Enum.IsDefined(typeof(EnemyState), currentState))
            {
                throw new ArgumentOutOfRangeException(nameof(currentState));
            }

            if (!context.IsAlive || currentState == EnemyState.Dead)
            {
                return EnemyState.Dead;
            }

            if (context.IsStaggered)
            {
                return EnemyState.Staggered;
            }

            if (!context.HasTarget)
            {
                return config.HomeState;
            }

            bool wasEngaged =
                currentState != EnemyState.Idle &&
                currentState != EnemyState.Patrol &&
                currentState != EnemyState.Alert;
            float detectionLimit = wasEngaged
                ? config.LoseTargetRange
                : config.AggroRange;

            if (context.TargetDistance > detectionLimit)
            {
                return config.HomeState;
            }

            if (
                config.AttackProfile.RequiresLineOfSight &&
                !context.HasLineOfSight
            )
            {
                return config.CanMove ? EnemyState.Reposition : EnemyState.Alert;
            }

            if (
                context.TargetDistance < config.PreferredMinimumDistance &&
                config.CanMove
            )
            {
                return EnemyState.Reposition;
            }

            if (context.TargetDistance > config.PreferredMaximumDistance)
            {
                return config.CanMove ? EnemyState.Engage : EnemyState.Alert;
            }

            bool insideAttackRange =
                context.TargetDistance >= config.AttackProfile.MinimumRange &&
                context.TargetDistance <= config.AttackProfile.MaximumRange;

            if (attackReady && insideAttackRange)
            {
                return EnemyState.Attack;
            }

            if (config.CanMove && config.LateralMovementStrength > 0f)
            {
                return EnemyState.Reposition;
            }

            return EnemyState.Engage;
        }
    }
}
