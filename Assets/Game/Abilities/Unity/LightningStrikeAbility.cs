using System;
using Powersuit.Abilities;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    public readonly struct LightningAreaCastCommand
    {
        public LightningAreaCastCommand(AbilityAreaEffect effect)
        {
            Effect = effect;
        }

        public AbilityAreaEffect Effect { get; }
        public Vector3 Center => Effect.Center;
        public Vector3 SurfaceNormal => Effect.SurfaceNormal;
        public float Radius => Effect.Radius;
    }

    [DisallowMultipleComponent]
    public sealed class LightningStrikeAbility : MonoBehaviour
    {
        public const float MaximumTunableDamage = 1000000f;
        public const float MaximumTunableRadius = 1000f;

        [Header("Targeting")]
        [SerializeField, Min(0.01f)] private float maximumRange = 35f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 8f;

        [Header("Area Cast")]
        [SerializeField, Min(0.01f)] private float radius = 6f;
        [SerializeField, Min(0f)] private float damage = 55f;
        [SerializeField] private CombatFaction sourceFaction =
            CombatFaction.Player;

        [Header("Runtime Tuning")]
        [SerializeField] private bool cooldownsEnabled = true;

        private LightningStrikeState state;

        public event Action TargetingStarted;
        public event Action<AbilityTargetValidation> TargetUpdated;
        public event Action TargetingCancelled;
        public event Action<LightningAreaCastCommand> CastRequested;
        public event Action<AbilityUseFailure, AbilityTargetValidation>
            CastRejected;
        public event Action<float, float> CooldownChanged;
        public event Action BecameReady;

        public bool IsTargeting
        {
            get
            {
                EnsureState();
                return state.IsTargeting;
            }
        }

        public bool CanBeginTargeting
        {
            get
            {
                EnsureState();
                return state.CanBeginTargeting;
            }
        }

        public float CooldownRemaining
        {
            get
            {
                EnsureState();
                return state.CooldownRemaining;
            }
        }

        public float CooldownNormalized
        {
            get
            {
                EnsureState();
                return state.CooldownNormalized;
            }
        }

        public float MaximumRange => maximumRange;

        public float Radius => radius;
        public float Damage => damage;
        public bool CooldownsEnabled => cooldownsEnabled;

        public float CooldownDuration
        {
            get
            {
                return cooldownSeconds;
            }
        }

        public void SetCooldownsEnabled(bool isEnabled)
        {
            cooldownsEnabled = isEnabled;
            if (!isEnabled)
            {
                ResetAbility();
            }
        }

        public float SetDamage(float value)
        {
            damage = ClampFinite(
                value,
                0f,
                MaximumTunableDamage,
                damage
            );
            return damage;
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

        public AbilityTargetValidation CurrentTargetValidation
        {
            get
            {
                EnsureState();
                return state.TargetValidation;
            }
        }

        public bool TryGetCurrentTarget(
            out Vector3 point,
            out Vector3 surfaceNormal
        )
        {
            EnsureState();
            if (!state.HasTarget)
            {
                point = default;
                surfaceNormal = default;
                return false;
            }

            point = ToUnity(state.Target.Point);
            surfaceNormal = ToUnity(state.Target.SurfaceNormal);
            return true;
        }

        private void Awake()
        {
            Reinitialize();
        }

        private void Update()
        {
            AdvanceAbility(Time.deltaTime);
        }

        public bool TryBeginTargeting()
        {
            EnsureState();
            AbilityUseResult result = state.TryBeginTargeting();
            if (!result.Accepted)
            {
                CastRejected?.Invoke(
                    result.Failure,
                    state.TargetValidation
                );
                return false;
            }

            TargetingStarted?.Invoke();
            TargetUpdated?.Invoke(state.TargetValidation);
            return true;
        }

        public AbilityTargetValidation UpdateTarget(
            Vector3 origin,
            Vector3 point,
            Vector3 surfaceNormal,
            bool hasSurface,
            bool isObstructed
        )
        {
            EnsureState();
            AbilityTargetValidation validation;
            if (
                !AbilityAreaEffect.IsFinite(origin) ||
                !AbilityAreaEffect.IsFinite(point) ||
                !AbilityAreaEffect.IsFinite(surfaceNormal)
            )
            {
                validation = state.InvalidateTarget(
                    AbilityTargetInvalidReason.NonFiniteInput
                );
            }
            else
            {
                validation = state.UpdateTarget(
                    new AbilityTargetSample(
                        AbilityAreaEffect.ToCombatVector(origin),
                        AbilityAreaEffect.ToCombatVector(point),
                        AbilityAreaEffect.ToCombatVector(surfaceNormal),
                        hasSurface,
                        isObstructed
                    )
                );
            }

            TargetUpdated?.Invoke(validation);
            return validation;
        }

        public bool ReleaseTargeting()
        {
            EnsureState();
            bool wasTargeting = state.IsTargeting;
            LightningReleaseResult result = state.Release();
            if (!result.Cast)
            {
                if (wasTargeting)
                {
                    TargetingCancelled?.Invoke();
                }

                CastRejected?.Invoke(result.Failure, result.Validation);
                return false;
            }

            LightningAreaCast areaCast = result.AreaCast;
            AbilityAreaEffect effect = new AbilityAreaEffect(
                gameObject,
                sourceFaction,
                DamageType.Lightning,
                ToUnity(areaCast.Point),
                ToUnity(areaCast.SurfaceNormal),
                radius,
                damage,
                1f,
                AbilityExternalForceMode.None,
                0f
            );
            if (!cooldownsEnabled)
            {
                state.Reset();
            }
            CooldownChanged?.Invoke(
                state.CooldownRemaining,
                state.CooldownNormalized
            );
            CastRequested?.Invoke(new LightningAreaCastCommand(effect));
            return true;
        }

        public bool CancelTargeting()
        {
            EnsureState();
            if (!state.Cancel())
            {
                return false;
            }

            TargetingCancelled?.Invoke();
            return true;
        }

        public void AdvanceAbility(float deltaSeconds)
        {
            EnsureState();
            if (!cooldownsEnabled)
            {
                if (state.CooldownRemaining > 0f)
                {
                    state.Reset();
                    CooldownChanged?.Invoke(0f, 0f);
                }
                return;
            }

            float previous = state.CooldownRemaining;
            bool wasReady = state.CanBeginTargeting;
            state.Advance(deltaSeconds);
            if (!Mathf.Approximately(previous, state.CooldownRemaining))
            {
                CooldownChanged?.Invoke(
                    state.CooldownRemaining,
                    state.CooldownNormalized
                );
            }

            if (!wasReady && state.CanBeginTargeting)
            {
                BecameReady?.Invoke();
            }
        }

        public void ResetAbility()
        {
            EnsureState();
            bool wasTargeting = state.IsTargeting;
            state.Reset();
            if (wasTargeting)
            {
                TargetingCancelled?.Invoke();
            }

            CooldownChanged?.Invoke(0f, 0f);
        }

        public void Reinitialize()
        {
            SanitizeConfiguration();
            state = new LightningStrikeState(cooldownSeconds, maximumRange);
            CooldownChanged?.Invoke(0f, 0f);
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
            maximumRange = SanitizePositive(maximumRange, 35f);
            cooldownSeconds = SanitizeNonNegative(cooldownSeconds, 8f);
            radius = SanitizePositive(radius, 6f);
            damage = SanitizeNonNegative(damage, 55f);
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
