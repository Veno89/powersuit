using System;
using Powersuit.Abilities;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    public readonly struct ShoulderRocketLaunchCommand
    {
        public ShoulderRocketLaunchCommand(
            object source,
            CombatFaction sourceFaction,
            Vector3 origin,
            Vector3 aimPoint,
            Vector3 direction,
            float projectileSpeed,
            float projectileLifetime,
            float explosionRadius,
            float explosionDamage,
            float minimumDamageMultiplier,
            float explosionImpulse
        )
        {
            Source = source;
            SourceFaction = sourceFaction;
            Origin = origin;
            AimPoint = aimPoint;
            Direction = direction;
            ProjectileSpeed = projectileSpeed;
            ProjectileLifetime = projectileLifetime;
            ExplosionRadius = explosionRadius;
            ExplosionDamage = explosionDamage;
            MinimumDamageMultiplier = minimumDamageMultiplier;
            ExplosionImpulse = explosionImpulse;
        }

        public object Source { get; }
        public CombatFaction SourceFaction { get; }
        public Vector3 Origin { get; }
        public Vector3 AimPoint { get; }
        public Vector3 Direction { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileLifetime { get; }
        public float ExplosionRadius { get; }
        public float ExplosionDamage { get; }
        public float MinimumDamageMultiplier { get; }
        public float ExplosionImpulse { get; }

        public AbilityAreaEffect CreateExplosion(
            Vector3 impactPosition,
            Vector3 impactNormal
        )
        {
            return new AbilityAreaEffect(
                Source,
                SourceFaction,
                DamageType.Explosive,
                impactPosition,
                impactNormal,
                ExplosionRadius,
                ExplosionDamage,
                MinimumDamageMultiplier,
                AbilityExternalForceMode.Push,
                ExplosionImpulse
            );
        }
    }

    [DisallowMultipleComponent]
    public sealed class ShoulderRocketAbility : MonoBehaviour
    {
        public const float MaximumTunableDamage = 1000000f;
        public const float MaximumTunableRadius = 1000f;

        [Header("Launch")]
        [SerializeField] private Transform launchPoint;
        [SerializeField, Min(0f)] private float cooldownSeconds = 6f;
        [SerializeField, Min(0.01f)] private float projectileSpeed = 45f;
        [SerializeField, Min(0.01f)] private float projectileLifetime = 5f;

        [Header("Explosion")]
        [SerializeField, Min(0.01f)] private float explosionRadius = 4f;
        [SerializeField, Min(0f)] private float explosionDamage = 80f;
        [SerializeField, Range(0f, 1f)]
        private float minimumDamageMultiplier = 0.35f;
        [SerializeField, Min(0f)] private float explosionImpulse = 18f;
        [SerializeField] private CombatFaction sourceFaction =
            CombatFaction.Player;

        [Header("Runtime Tuning")]
        [SerializeField] private bool cooldownsEnabled = true;

        private ShoulderRocketState state;

        public event Action<ShoulderRocketLaunchCommand> LaunchRequested;
        public event Action<AbilityUseFailure> LaunchRejected;
        public event Action<float, float> CooldownChanged;
        public event Action BecameReady;

        public Transform LaunchPoint
        {
            get => launchPoint;
            set => launchPoint = value;
        }

        public bool CanLaunch
        {
            get
            {
                EnsureState();
                return state.CanLaunch;
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

        public float CooldownDuration
        {
            get
            {
                EnsureState();
                return state.CooldownDuration;
            }
        }

        public bool CooldownsEnabled => cooldownsEnabled;
        public float ExplosionDamage => explosionDamage;
        public float ExplosionRadius => explosionRadius;

        public void SetCooldownsEnabled(bool isEnabled)
        {
            cooldownsEnabled = isEnabled;
            if (!isEnabled)
            {
                ResetAbility();
            }
        }

        public float SetExplosionDamage(float value)
        {
            explosionDamage = ClampFinite(
                value,
                0f,
                MaximumTunableDamage,
                explosionDamage
            );
            return explosionDamage;
        }

        public float SetExplosionRadius(float value)
        {
            explosionRadius = ClampFinite(
                value,
                0.01f,
                MaximumTunableRadius,
                explosionRadius
            );
            return explosionRadius;
        }

        private void Awake()
        {
            Reinitialize();
        }

        private void Update()
        {
            AdvanceAbility(Time.deltaTime);
        }

        public bool TryLaunch(Vector3 aimPoint)
        {
            EnsureState();
            Transform originTransform = launchPoint != null
                ? launchPoint
                : transform;
            Vector3 origin = originTransform.position;
            if (
                !AbilityAreaEffect.IsFinite(origin) ||
                !AbilityAreaEffect.IsFinite(aimPoint)
            )
            {
                LaunchRejected?.Invoke(AbilityUseFailure.InvalidLaunch);
                return false;
            }

            ShoulderRocketLaunchResult result = state.TryLaunch(
                AbilityAreaEffect.ToCombatVector(origin),
                AbilityAreaEffect.ToCombatVector(aimPoint)
            );
            if (!result.Accepted)
            {
                LaunchRejected?.Invoke(result.Failure);
                return false;
            }

            ShoulderRocketLaunch launch = result.Launch;
            Vector3 direction = new Vector3(
                launch.Direction.X,
                launch.Direction.Y,
                launch.Direction.Z
            );
            ShoulderRocketLaunchCommand command =
                new ShoulderRocketLaunchCommand(
                    gameObject,
                    sourceFaction,
                    origin,
                    aimPoint,
                    direction,
                    projectileSpeed,
                    projectileLifetime,
                    explosionRadius,
                    explosionDamage,
                    minimumDamageMultiplier,
                    explosionImpulse
                );
            if (!cooldownsEnabled)
            {
                state.ResetCooldown();
            }
            CooldownChanged?.Invoke(
                state.CooldownRemaining,
                state.CooldownNormalized
            );
            LaunchRequested?.Invoke(command);
            return true;
        }

        public void AdvanceAbility(float deltaSeconds)
        {
            EnsureState();
            if (!cooldownsEnabled)
            {
                if (state.CooldownRemaining > 0f)
                {
                    state.ResetCooldown();
                    CooldownChanged?.Invoke(0f, 0f);
                }
                return;
            }

            float previous = state.CooldownRemaining;
            bool wasReady = state.CanLaunch;
            state.Advance(deltaSeconds);
            if (!Mathf.Approximately(previous, state.CooldownRemaining))
            {
                CooldownChanged?.Invoke(
                    state.CooldownRemaining,
                    state.CooldownNormalized
                );
            }

            if (!wasReady && state.CanLaunch)
            {
                BecameReady?.Invoke();
            }
        }

        public void ResetAbility()
        {
            EnsureState();
            state.ResetCooldown();
            CooldownChanged?.Invoke(0f, 0f);
        }

        public void Reinitialize()
        {
            SanitizeConfiguration();
            state = new ShoulderRocketState(cooldownSeconds);
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
            cooldownSeconds = SanitizeNonNegative(cooldownSeconds, 6f);
            projectileSpeed = SanitizePositive(projectileSpeed, 45f);
            projectileLifetime = SanitizePositive(projectileLifetime, 5f);
            explosionRadius = SanitizePositive(explosionRadius, 4f);
            explosionDamage = SanitizeNonNegative(explosionDamage, 80f);
            minimumDamageMultiplier = Mathf.Clamp01(
                AbilityAreaEffect.IsFinite(minimumDamageMultiplier)
                    ? minimumDamageMultiplier
                    : 0.35f
            );
            explosionImpulse = SanitizeNonNegative(explosionImpulse, 18f);
            if (!CombatFactionPolicy.IsKnown(sourceFaction))
            {
                sourceFaction = CombatFaction.Player;
            }
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
