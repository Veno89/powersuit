using System;
using Powersuit.Combat;

namespace Powersuit.Abilities
{
    public enum VoidUltimatePhase
    {
        Idle = 0,
        Active = 1
    }

    public readonly struct VoidUltimateActivation
    {
        public VoidUltimateActivation(
            CombatVector3 point,
            CombatVector3 surfaceNormal
        )
        {
            Point = point;
            SurfaceNormal = surfaceNormal;
        }

        public CombatVector3 Point { get; }
        public CombatVector3 SurfaceNormal { get; }
    }

    public readonly struct VoidActivationResult
    {
        private VoidActivationResult(
            bool activated,
            AbilityUseFailure failure,
            AbilityTargetValidation validation,
            VoidUltimateActivation activation
        )
        {
            Activated = activated;
            Failure = failure;
            Validation = validation;
            Activation = activation;
        }

        public bool Activated { get; }
        public AbilityUseFailure Failure { get; }
        public AbilityTargetValidation Validation { get; }
        public VoidUltimateActivation Activation { get; }

        public static VoidActivationResult Success(
            VoidUltimateActivation activation
        )
        {
            return new VoidActivationResult(
                true,
                AbilityUseFailure.None,
                AbilityTargetValidation.Valid,
                activation
            );
        }

        public static VoidActivationResult Rejected(
            AbilityUseFailure failure,
            AbilityTargetValidation validation
        )
        {
            return new VoidActivationResult(
                false,
                failure,
                validation,
                default
            );
        }
    }

    public readonly struct VoidAdvanceResult
    {
        public VoidAdvanceResult(
            int tickCount,
            bool finalBurstTriggered,
            CombatVector3 point,
            CombatVector3 surfaceNormal
        )
        {
            TickCount = tickCount;
            FinalBurstTriggered = finalBurstTriggered;
            Point = point;
            SurfaceNormal = surfaceNormal;
        }

        public int TickCount { get; }
        public bool FinalBurstTriggered { get; }
        public CombatVector3 Point { get; }
        public CombatVector3 SurfaceNormal { get; }
        public bool HasEvents => TickCount > 0 || FinalBurstTriggered;
    }

    /// <summary>
    /// Owns full-meter activation, one active placement, deterministic periodic
    /// ticks, and one terminal burst. Unity owns target discovery and effects.
    /// </summary>
    public sealed class VoidUltimateState
    {
        private const int MaximumTicksPerActivation = 4096;
        private const float TimeEpsilon = 0.00001f;

        private readonly UltimateMeterState meter;
        private readonly float maximumRange;
        private readonly float activeDuration;
        private readonly float tickInterval;

        private VoidUltimateActivation activation;
        private float activeElapsed;
        private float nextTickTime;

        public VoidUltimateState(
            float meterCapacity,
            float maximumRange,
            float activeDuration,
            float tickInterval
        )
        {
            AbilityVectorMath.RequireFinitePositive(
                maximumRange,
                nameof(maximumRange)
            );
            AbilityVectorMath.RequireFinitePositive(
                activeDuration,
                nameof(activeDuration)
            );
            AbilityVectorMath.RequireFinitePositive(
                tickInterval,
                nameof(tickInterval)
            );

            double possibleTicks =
                Math.Ceiling((double)activeDuration / tickInterval) - 1d;
            if (possibleTicks > MaximumTicksPerActivation)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tickInterval),
                    $"One activation may emit at most {MaximumTicksPerActivation} ticks."
                );
            }

            meter = new UltimateMeterState(meterCapacity);
            this.maximumRange = maximumRange;
            this.activeDuration = activeDuration;
            this.tickInterval = tickInterval;
            Phase = VoidUltimatePhase.Idle;
        }

        public VoidUltimatePhase Phase { get; private set; }
        public bool IsActive => Phase == VoidUltimatePhase.Active;
        public float MeterCapacity => meter.Capacity;
        public float MeterValue => meter.CurrentValue;
        public float MeterNormalized => meter.NormalizedValue;
        public bool IsMeterFull => meter.IsFull;
        public float MaximumRange => maximumRange;
        public float ActiveDuration => activeDuration;
        public float ActiveElapsed => activeElapsed;
        public float ActiveRemaining => IsActive
            ? Math.Max(0f, activeDuration - activeElapsed)
            : 0f;
        public float ActiveNormalized => IsActive
            ? Math.Max(0f, Math.Min(1f, activeElapsed / activeDuration))
            : 0f;
        public float TickInterval => tickInterval;
        public VoidUltimateActivation ActivePlacement => activation;

        public float GainMeter(float amount)
        {
            AbilityVectorMath.RequireFiniteNonNegative(amount, nameof(amount));
            return IsActive ? 0f : meter.Gain(amount);
        }

        public VoidActivationResult TryActivate(AbilityTargetSample target)
        {
            if (IsActive)
            {
                return VoidActivationResult.Rejected(
                    AbilityUseFailure.AlreadyActive,
                    AbilityTargetValidation.Valid
                );
            }

            AbilityTargetValidation validation =
                AbilityTargetRules.ValidateSurfaceTarget(target, maximumRange);
            if (!validation.IsValid)
            {
                return VoidActivationResult.Rejected(
                    AbilityUseFailure.InvalidTarget,
                    validation
                );
            }

            if (!meter.IsFull || !meter.TryConsume(meter.Capacity))
            {
                return VoidActivationResult.Rejected(
                    AbilityUseFailure.MeterNotFull,
                    validation
                );
            }

            activation = new VoidUltimateActivation(
                target.Point,
                target.SurfaceNormal
            );
            activeElapsed = 0f;
            nextTickTime = tickInterval;
            Phase = VoidUltimatePhase.Active;
            return VoidActivationResult.Success(activation);
        }

        public VoidAdvanceResult Advance(float deltaSeconds)
        {
            AbilityVectorMath.RequireFiniteNonNegative(
                deltaSeconds,
                nameof(deltaSeconds)
            );

            if (!IsActive || deltaSeconds <= 0f)
            {
                return default;
            }

            VoidUltimateActivation activePlacement = activation;
            float nextElapsed = Math.Min(
                activeDuration,
                activeElapsed + deltaSeconds
            );
            int tickCount = 0;

            while (
                nextTickTime < activeDuration - TimeEpsilon &&
                nextTickTime <= nextElapsed + TimeEpsilon
            )
            {
                tickCount++;
                nextTickTime += tickInterval;
            }

            activeElapsed = nextElapsed;
            bool finalBurst = activeElapsed + TimeEpsilon >= activeDuration;
            if (finalBurst)
            {
                Phase = VoidUltimatePhase.Idle;
                activeElapsed = 0f;
                nextTickTime = 0f;
                activation = default;
            }

            return new VoidAdvanceResult(
                tickCount,
                finalBurst,
                activePlacement.Point,
                activePlacement.SurfaceNormal
            );
        }

        public bool Cancel()
        {
            if (!IsActive)
            {
                return false;
            }

            Phase = VoidUltimatePhase.Idle;
            activeElapsed = 0f;
            nextTickTime = 0f;
            activation = default;
            return true;
        }

        public void Reset(bool clearMeter = true)
        {
            Cancel();
            if (clearMeter)
            {
                meter.Reset();
            }
        }

        public void FillMeter()
        {
            if (!IsActive)
            {
                meter.Fill();
            }
        }
    }
}
