using System;
using Powersuit.Abilities;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    public readonly struct VoidUltimateActivationCommand
    {
        public VoidUltimateActivationCommand(
            Vector3 center,
            Vector3 surfaceNormal,
            float activeDuration,
            float tickInterval,
            float radius
        )
        {
            Center = center;
            SurfaceNormal = surfaceNormal;
            ActiveDuration = activeDuration;
            TickInterval = tickInterval;
            Radius = radius;
        }

        public Vector3 Center { get; }
        public Vector3 SurfaceNormal { get; }
        public float ActiveDuration { get; }
        public float TickInterval { get; }
        public float Radius { get; }
    }

    public readonly struct VoidUltimateTickCommand
    {
        public VoidUltimateTickCommand(int sequence, AbilityAreaEffect effect)
        {
            Sequence = sequence;
            Effect = effect;
        }

        public int Sequence { get; }
        public AbilityAreaEffect Effect { get; }
    }

    public readonly struct VoidUltimateBurstCommand
    {
        public VoidUltimateBurstCommand(AbilityAreaEffect effect)
        {
            Effect = effect;
        }

        public AbilityAreaEffect Effect { get; }
    }

    [DisallowMultipleComponent]
    public sealed class VoidUltimateAbility : MonoBehaviour
    {
        public const float MaximumTunableDamage = 1000000f;
        public const float MaximumTunableRadius = 1000f;
        public const float MaximumTunableImpulse = 10000f;

        [Header("Meter and Placement")]
        [SerializeField, Min(0.01f)] private float meterCapacity = 100f;
        [SerializeField, Min(0.01f)] private float maximumRange = 40f;

        [Header("Active Field")]
        [SerializeField, Min(0.01f)] private float activeDuration = 5f;
        [SerializeField, Min(0.01f)] private float tickInterval = 0.5f;
        [SerializeField, Min(0.01f)] private float radius = 8f;
        [SerializeField, Min(0f)] private float tickDamage = 10f;
        [SerializeField, Min(0f)] private float pullImpulsePerTick = 4f;

        [Header("Final Burst")]
        [SerializeField, Min(0f)] private float finalDamage = 120f;
        [SerializeField, Range(0f, 1f)]
        private float finalMinimumDamageMultiplier = 0.3f;
        [SerializeField, Min(0f)] private float finalOutwardImpulse = 24f;
        [SerializeField] private CombatFaction sourceFaction =
            CombatFaction.Player;

        private VoidUltimateState state;
        private int emittedTickSequence;

        public event Action<float, float> MeterChanged;
        public event Action<VoidUltimateActivationCommand> Activated;
        public event Action<VoidUltimateTickCommand> TickRequested;
        public event Action<VoidUltimateBurstCommand> FinalBurstRequested;
        public event Action Completed;
        public event Action Cancelled;
        public event Action<AbilityUseFailure, AbilityTargetValidation>
            ActivationRejected;

        public bool IsActive
        {
            get
            {
                EnsureState();
                return state.IsActive;
            }
        }

        public bool IsMeterFull
        {
            get
            {
                EnsureState();
                return state.IsMeterFull;
            }
        }

        public float MeterValue
        {
            get
            {
                EnsureState();
                return state.MeterValue;
            }
        }

        public float MeterNormalized
        {
            get
            {
                EnsureState();
                return state.MeterNormalized;
            }
        }

        public float ActiveRemaining
        {
            get
            {
                EnsureState();
                return state.ActiveRemaining;
            }
        }

        public float ActiveNormalized
        {
            get
            {
                EnsureState();
                return state.ActiveNormalized;
            }
        }

        public float MaximumRange => maximumRange;

        public float Radius => radius;
        public float TickDamage => tickDamage;
        public float FinalDamage => finalDamage;
        public float PullImpulsePerTick => pullImpulsePerTick;

        /// <summary>
        /// Convenience alias for the console's void.damage command. It tunes
        /// periodic field damage; the distinct final burst remains separately
        /// tunable so its authored high-impact identity is preserved.
        /// </summary>
        public float SetDamage(float value)
        {
            return SetTickDamage(value);
        }

        public float SetTickDamage(float value)
        {
            tickDamage = ClampFinite(
                value,
                0f,
                MaximumTunableDamage,
                tickDamage
            );
            return tickDamage;
        }

        public float SetFinalDamage(float value)
        {
            finalDamage = ClampFinite(
                value,
                0f,
                MaximumTunableDamage,
                finalDamage
            );
            return finalDamage;
        }

        public float SetRadius(float value)
        {
            radius = ClampFinite(
                value,
                0.01f,
                MaximumTunableRadius,
                radius
            );
            return radius;
        }

        public float SetPullImpulsePerTick(float value)
        {
            pullImpulsePerTick = ClampFinite(
                value,
                0f,
                MaximumTunableImpulse,
                pullImpulsePerTick
            );
            return pullImpulsePerTick;
        }

        private void Awake()
        {
            Reinitialize();
        }

        private void Update()
        {
            AdvanceAbility(Time.deltaTime);
        }

        public float GainMeter(float amount)
        {
            EnsureState();
            float accepted = state.GainMeter(amount);
            if (accepted > 0f)
            {
                MeterChanged?.Invoke(
                    state.MeterValue,
                    state.MeterNormalized
                );
            }

            return accepted;
        }

        public void FillMeter()
        {
            EnsureState();
            float previous = state.MeterValue;
            state.FillMeter();
            if (!Mathf.Approximately(previous, state.MeterValue))
            {
                MeterChanged?.Invoke(
                    state.MeterValue,
                    state.MeterNormalized
                );
            }
        }

        public bool TryActivate(
            Vector3 origin,
            Vector3 point,
            Vector3 surfaceNormal,
            bool hasSurface,
            bool isObstructed
        )
        {
            EnsureState();
            if (
                !AbilityAreaEffect.IsFinite(origin) ||
                !AbilityAreaEffect.IsFinite(point) ||
                !AbilityAreaEffect.IsFinite(surfaceNormal)
            )
            {
                ActivationRejected?.Invoke(
                    AbilityUseFailure.InvalidTarget,
                    AbilityTargetValidation.Invalid(
                        AbilityTargetInvalidReason.NonFiniteInput
                    )
                );
                return false;
            }

            VoidActivationResult result = state.TryActivate(
                new AbilityTargetSample(
                    AbilityAreaEffect.ToCombatVector(origin),
                    AbilityAreaEffect.ToCombatVector(point),
                    AbilityAreaEffect.ToCombatVector(surfaceNormal),
                    hasSurface,
                    isObstructed
                )
            );
            if (!result.Activated)
            {
                ActivationRejected?.Invoke(result.Failure, result.Validation);
                return false;
            }

            emittedTickSequence = 0;
            MeterChanged?.Invoke(state.MeterValue, state.MeterNormalized);
            VoidUltimateActivation activation = result.Activation;
            Activated?.Invoke(
                new VoidUltimateActivationCommand(
                    ToUnity(activation.Point),
                    ToUnity(activation.SurfaceNormal),
                    activeDuration,
                    tickInterval,
                    radius
                )
            );
            return true;
        }

        public VoidAdvanceResult AdvanceAbility(float deltaSeconds)
        {
            EnsureState();
            VoidAdvanceResult result = state.Advance(deltaSeconds);
            if (!result.HasEvents)
            {
                return result;
            }

            Vector3 center = ToUnity(result.Point);
            Vector3 normal = ToUnity(result.SurfaceNormal);
            for (int index = 0; index < result.TickCount; index++)
            {
                emittedTickSequence++;
                TickRequested?.Invoke(
                    new VoidUltimateTickCommand(
                        emittedTickSequence,
                        CreateTickEffect(center, normal)
                    )
                );
            }

            if (result.FinalBurstTriggered)
            {
                FinalBurstRequested?.Invoke(
                    new VoidUltimateBurstCommand(
                        CreateFinalBurstEffect(center, normal)
                    )
                );
                Completed?.Invoke();
            }

            return result;
        }

        public bool CancelAbility()
        {
            EnsureState();
            if (!state.Cancel())
            {
                return false;
            }

            emittedTickSequence = 0;
            Cancelled?.Invoke();
            return true;
        }

        public void ResetAbility(bool clearMeter = true)
        {
            EnsureState();
            bool wasActive = state.IsActive;
            state.Reset(clearMeter);
            emittedTickSequence = 0;
            if (wasActive)
            {
                Cancelled?.Invoke();
            }

            MeterChanged?.Invoke(state.MeterValue, state.MeterNormalized);
        }

        public void Reinitialize()
        {
            SanitizeConfiguration();
            state = new VoidUltimateState(
                meterCapacity,
                maximumRange,
                activeDuration,
                tickInterval
            );
            emittedTickSequence = 0;
            MeterChanged?.Invoke(0f, 0f);
        }

        private AbilityAreaEffect CreateTickEffect(
            Vector3 center,
            Vector3 normal
        )
        {
            return new AbilityAreaEffect(
                gameObject,
                sourceFaction,
                DamageType.Void,
                center,
                normal,
                radius,
                tickDamage,
                1f,
                AbilityExternalForceMode.Pull,
                pullImpulsePerTick
            );
        }

        private AbilityAreaEffect CreateFinalBurstEffect(
            Vector3 center,
            Vector3 normal
        )
        {
            return new AbilityAreaEffect(
                gameObject,
                sourceFaction,
                DamageType.Void,
                center,
                normal,
                radius,
                finalDamage,
                finalMinimumDamageMultiplier,
                AbilityExternalForceMode.Push,
                finalOutwardImpulse
            );
        }

        private void EnsureState()
        {
            if (state == null)
            {
                Reinitialize();
            }
        }

        private void OnValidate()
        {
            SanitizeConfiguration();
        }

        private void SanitizeConfiguration()
        {
            meterCapacity = SanitizePositive(meterCapacity, 100f);
            maximumRange = SanitizePositive(maximumRange, 40f);
            activeDuration = SanitizePositive(activeDuration, 5f);
            tickInterval = SanitizePositive(tickInterval, 0.5f);
            float minimumSafeInterval = activeDuration / 4097f;
            tickInterval = Mathf.Max(tickInterval, minimumSafeInterval);
            radius = SanitizePositive(radius, 8f);
            tickDamage = SanitizeNonNegative(tickDamage, 10f);
            pullImpulsePerTick = SanitizeNonNegative(
                pullImpulsePerTick,
                4f
            );
            finalDamage = SanitizeNonNegative(finalDamage, 120f);
            finalMinimumDamageMultiplier = Mathf.Clamp01(
                AbilityAreaEffect.IsFinite(finalMinimumDamageMultiplier)
                    ? finalMinimumDamageMultiplier
                    : 0.3f
            );
            finalOutwardImpulse = SanitizeNonNegative(
                finalOutwardImpulse,
                24f
            );
            if (!CombatFactionPolicy.IsKnown(sourceFaction))
            {
                sourceFaction = CombatFaction.Player;
            }
        }

        private static Vector3 ToUnity(CombatVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static float SanitizePositive(float value, float fallback)
        {
            return AbilityAreaEffect.IsFinite(value) && value > 0f
                ? value
                : fallback;
        }

        private static float SanitizeNonNegative(float value, float fallback)
        {
            return AbilityAreaEffect.IsFinite(value) && value >= 0f
                ? value
                : fallback;
        }

        private static float ClampFinite(
            float value,
            float minimum,
            float maximum,
            float fallback
        )
        {
            if (float.IsNaN(value))
            {
                return fallback;
            }

            if (float.IsPositiveInfinity(value))
            {
                return maximum;
            }

            if (float.IsNegativeInfinity(value))
            {
                return minimum;
            }

            return Mathf.Clamp(value, minimum, maximum);
        }
    }
}
