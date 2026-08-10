using System;
using Powersuit.Combat;

namespace Powersuit.Enemies
{
    /// <summary>
    /// Mutable per-instance state with a complete pooling reset boundary. It
    /// owns combat timers; animation and Unity AI components only present or
    /// execute the resulting state.
    /// </summary>
    public sealed class EnemyRuntimeState
    {
        private const float TimeEpsilon = 0.00001f;

        private float attackCooldownRemaining;
        private float spawnProtectionRemaining;
        private float staggerRemaining;
        private int burstShotsRemaining;

        public EnemyArchetypeConfig Config { get; private set; }
        public bool IsConfigured => Config != null;
        public CombatVector3 SpawnAnchor { get; private set; }
        public EnemyState CurrentState { get; private set; }
        public float AttackCooldownRemaining => attackCooldownRemaining;
        public float SpawnProtectionRemaining => spawnProtectionRemaining;
        public float StaggerRemaining => staggerRemaining;
        public int BurstShotsRemaining => burstShotsRemaining;
        public int AttacksStarted { get; private set; }
        public bool IsAlive => CurrentState != EnemyState.Dead;
        public bool IsSpawnProtected => spawnProtectionRemaining > TimeEpsilon;
        public bool CanBeginAttack =>
            IsAlive &&
            CurrentState != EnemyState.Staggered &&
            attackCooldownRemaining <= TimeEpsilon &&
            spawnProtectionRemaining <= TimeEpsilon;

        public void Reset(
            EnemyArchetypeConfig config,
            CombatVector3 spawnAnchor,
            float spawnProtectionSeconds = 0f,
            float initialAttackDelaySeconds = 0f
        )
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            RequireFiniteNonNegative(
                spawnProtectionSeconds,
                nameof(spawnProtectionSeconds)
            );
            RequireFiniteNonNegative(
                initialAttackDelaySeconds,
                nameof(initialAttackDelaySeconds)
            );

            Config = config;
            SpawnAnchor = spawnAnchor;
            CurrentState = config.HomeState;
            spawnProtectionRemaining = spawnProtectionSeconds;
            attackCooldownRemaining = initialAttackDelaySeconds;
            staggerRemaining = 0f;
            burstShotsRemaining = 0;
            AttacksStarted = 0;
        }

        public void Advance(float deltaSeconds)
        {
            EnsureConfigured();
            RequireFiniteNonNegative(deltaSeconds, nameof(deltaSeconds));

            attackCooldownRemaining = Decrease(attackCooldownRemaining, deltaSeconds);
            spawnProtectionRemaining = Decrease(spawnProtectionRemaining, deltaSeconds);
            staggerRemaining = Decrease(staggerRemaining, deltaSeconds);

            if (
                CurrentState == EnemyState.Staggered &&
                staggerRemaining <= TimeEpsilon
            )
            {
                CurrentState = Config.HomeState;
            }
        }

        public EnemyState Evaluate(EnemyDecisionContext context)
        {
            EnsureConfigured();
            CurrentState = EnemyDecision.SelectState(
                Config,
                CurrentState,
                context,
                CanBeginAttack
            );
            return CurrentState;
        }

        public bool TryBeginAttack()
        {
            EnsureConfigured();

            if (!CanBeginAttack || CurrentState != EnemyState.Attack)
            {
                return false;
            }

            attackCooldownRemaining = Config.AttackProfile.FireIntervalSeconds;
            burstShotsRemaining = Config.AttackProfile.BurstCount;
            AttacksStarted++;
            return true;
        }

        public bool TryConsumeBurstShot()
        {
            if (burstShotsRemaining <= 0 || !IsAlive)
            {
                return false;
            }

            burstShotsRemaining--;
            return true;
        }

        public bool ApplyStagger(float durationSeconds)
        {
            EnsureConfigured();
            RequireFiniteNonNegative(durationSeconds, nameof(durationSeconds));

            if (!IsAlive || durationSeconds <= TimeEpsilon)
            {
                return false;
            }

            staggerRemaining = Math.Max(staggerRemaining, durationSeconds);
            burstShotsRemaining = 0;
            CurrentState = EnemyState.Staggered;
            return true;
        }

        public void MarkDead()
        {
            EnsureConfigured();
            CurrentState = EnemyState.Dead;
            attackCooldownRemaining = 0f;
            spawnProtectionRemaining = 0f;
            staggerRemaining = 0f;
            burstShotsRemaining = 0;
        }

        private void EnsureConfigured()
        {
            if (Config == null)
            {
                throw new InvalidOperationException(
                    "EnemyRuntimeState must be reset with an archetype before use."
                );
            }
        }

        private static float Decrease(float value, float deltaSeconds)
        {
            return Math.Max(0f, value - deltaSeconds);
        }

        private static void RequireFiniteNonNegative(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Time must be a finite non-negative value."
                );
            }
        }
    }
}
