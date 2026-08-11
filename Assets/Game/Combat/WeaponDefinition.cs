using System.Collections.Generic;
using UnityEngine;

namespace Powersuit.Combat
{
    public enum WeaponReticleStyle
    {
        PrecisionCross = 0,
        AssaultDynamic = 1,
        HeavyCharge = 2
    }

    [CreateAssetMenu(
        fileName = "WeaponDefinition",
        menuName = "Powersuit/Combat/Weapon Definition",
        order = 10
    )]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string weaponId = "precision-rifle";
        [SerializeField] private string displayName = "Precision Rifle";
        [SerializeField] private WeaponClass weaponClass = WeaponClass.PrecisionRifle;
        [SerializeField] private WeaponTriggerMode triggerMode = WeaponTriggerMode.SemiAutomatic;

        [Header("Damage and Cadence")]
        [SerializeField, Min(0.01f)] private float baseDamage = 60f;
        [SerializeField, Min(0.01f)] private float roundsPerMinute = 45f;
        [SerializeField, Range(0f, 1f)] private float criticalChance = 0.1f;
        [SerializeField, Min(1f)] private float criticalDamageMultiplier = 2f;

        [Header("Ammunition")]
        [SerializeField, Min(1)] private int magazineCapacity = 5;
        [SerializeField, Min(0)] private int startingReserveAmmo = 25;
        [SerializeField, Min(0)] private int maximumReserveAmmo = 50;

        [Header("Reload")]
        [Tooltip("Automatically starts a reload after the magazine reaches zero and the weapon is ready to reload.")]
        [SerializeField] private bool autoReloadWhenEmpty = true;
        [SerializeField, Min(0f)] private float reloadDurationSeconds = 2.8f;
        [SerializeField, Range(0f, 1f)] private float reloadCommitNormalizedTime = 0.89f;

        [Header("Manual Action")]
        [Tooltip("When enabled, another shot is blocked until the action cycle finishes.")]
        [SerializeField] private bool requiresManualCycle;
        [SerializeField, Min(0f)] private float manualCycleDurationSeconds = 0.67f;

        [Header("Projectile")]
        [SerializeField, Min(0.01f)] private float projectileSpeed = 100f;
        [SerializeField, Min(0.01f)] private float projectileLifetimeSeconds = 4f;
        [SerializeField, Min(0.01f)] private float projectileRadius = 0.15f;
        [Tooltip(
            "Shared projectile-pool capacity requested by this weapon. " +
            "Loadouts prewarm to the largest equipped requirement."
        )]
        [SerializeField, Min(0)] private int projectilePrewarmCount = 8;
        [SerializeField] private PlayerProjectile projectilePrefabOverride;

        [Header("Projectile Impact")]
        [SerializeField] private DamageType projectileDamageType =
            DamageType.Kinetic;
        [SerializeField, Min(0f)] private float splashDamageRadius;
        [SerializeField, Range(0f, 1f)]
        private float splashMinimumDamageMultiplier = 0.35f;
        [SerializeField, Min(0f)] private float splashImpulse;
        [SerializeField, Min(0f)] private float splashStaggerSeconds;

        [Header("Handling")]
        [SerializeField, Min(0f)] private float aimSpreadDegrees = 0.15f;
        [SerializeField, Min(0f)] private float hipSpreadDegrees = 1.25f;
        [SerializeField, Min(0f)] private float aimRecoilPitch = 0.9f;
        [SerializeField, Min(0f)] private float aimRecoilYaw = 0.25f;
        [SerializeField, Min(0f)] private float hipRecoilPitch = 1.6f;
        [SerializeField, Min(0f)] private float hipRecoilYaw = 0.5f;

        [Header("Charge Shot")]
        [SerializeField] private bool usesChargeShot;
        [SerializeField, Min(0.01f)] private float chargeDurationSeconds = 0.8f;
        [SerializeField, Range(0f, 1f)]
        private float minimumChargeNormalized = 0.3f;
        [SerializeField, Min(0.01f)]
        private float minimumChargeDamageMultiplier = 0.8f;
        [SerializeField, Min(0.01f)]
        private float maximumChargeDamageMultiplier = 1.6f;
        [SerializeField, Min(0.01f)]
        private float minimumChargeRadiusMultiplier = 0.8f;
        [SerializeField, Min(0.01f)]
        private float maximumChargeRadiusMultiplier = 1.3f;

        [Header("Aim Camera")]
        [SerializeField] private bool supportsScope = true;
        [SerializeField, Range(1.01f, 178.99f)]
        private float shoulderFieldOfViewDegrees = 62f;
        [SerializeField, Range(1.01f, 178.99f)]
        private float scopedFieldOfViewDegrees = 28f;
        [SerializeField, Range(0.01f, 1f)]
        private float shoulderLookSensitivityMultiplier = 0.9f;
        [SerializeField, Range(0.01f, 1f)]
        private float scopedLookSensitivityMultiplier = 0.45f;
        [SerializeField, Min(0.01f)] private float aimTransitionSharpness = 22f;

        [Header("Presentation Identity")]
        [SerializeField] private WeaponReticleStyle reticleStyle =
            WeaponReticleStyle.PrecisionCross;
        [SerializeField] private Color reticleColor =
            new Color(0.2f, 0.9f, 1f, 1f);
        [SerializeField, Min(0f)] private float reticleBaseGapPixels = 4f;
        [SerializeField, Min(1f)] private float reticleArmLengthPixels = 12f;
        [SerializeField, Min(0f)] private float reticleShotExpansionPixels = 7f;
        [SerializeField, Min(0.01f)] private float reticleRecoverySharpness = 18f;
        [SerializeField] private Color authoredMuzzleFlashColor =
            new Color(0.3f, 0.85f, 1f, 1f);
        [SerializeField, Min(0f)] private float authoredMuzzleFlashIntensity = 7f;
        [SerializeField, Min(0.01f)] private float authoredMuzzleFlashDuration = 0.065f;
        [SerializeField, Min(0f)] private float visualRecoilDistance = 0.018f;
        [SerializeField, Min(0f)] private float visualRecoilDegrees = 1.5f;

        public string WeaponId => weaponId;
        public string DisplayName => displayName;
        public WeaponClass WeaponClass => weaponClass;
        public WeaponTriggerMode TriggerMode => triggerMode;
        public float BaseDamage => baseDamage;
        public float RoundsPerMinute => roundsPerMinute;
        public float CriticalChance => criticalChance;
        public float CriticalDamageMultiplier => criticalDamageMultiplier;
        public int MagazineCapacity => magazineCapacity;
        public int StartingReserveAmmo => startingReserveAmmo;
        public int MaximumReserveAmmo => maximumReserveAmmo;
        public bool AutoReloadWhenEmpty => autoReloadWhenEmpty;
        public float ReloadDurationSeconds => reloadDurationSeconds;
        public float ReloadCommitNormalizedTime => reloadCommitNormalizedTime;
        public bool RequiresManualCycle => requiresManualCycle;
        public float ManualCycleDurationSeconds => manualCycleDurationSeconds;
        public float ProjectileSpeed => projectileSpeed;
        public float ProjectileLifetimeSeconds => projectileLifetimeSeconds;
        public float ProjectileRadius => projectileRadius;
        public int ProjectilePrewarmCount => Mathf.Max(0, projectilePrewarmCount);
        public PlayerProjectile ProjectilePrefabOverride => projectilePrefabOverride;
        public DamageType ProjectileDamageType => projectileDamageType;
        public float SplashDamageRadius => Mathf.Max(0f, splashDamageRadius);
        public float SplashMinimumDamageMultiplier =>
            Mathf.Clamp01(splashMinimumDamageMultiplier);
        public float SplashImpulse => Mathf.Max(0f, splashImpulse);
        public float SplashStaggerSeconds => Mathf.Max(0f, splashStaggerSeconds);
        public float AimSpreadDegrees => aimSpreadDegrees;
        public float HipSpreadDegrees => hipSpreadDegrees;
        public float AimRecoilPitch => aimRecoilPitch;
        public float AimRecoilYaw => aimRecoilYaw;
        public float HipRecoilPitch => hipRecoilPitch;
        public float HipRecoilYaw => hipRecoilYaw;
        public bool UsesChargeShot =>
            usesChargeShot && weaponClass == WeaponClass.HeavyWeapon;
        public float ChargeDurationSeconds =>
            Mathf.Max(0.01f, chargeDurationSeconds);
        public float MinimumChargeNormalized =>
            Mathf.Clamp01(minimumChargeNormalized);
        public float MinimumChargeDamageMultiplier =>
            Mathf.Max(0.01f, minimumChargeDamageMultiplier);
        public float MaximumChargeDamageMultiplier =>
            Mathf.Max(
                MinimumChargeDamageMultiplier,
                maximumChargeDamageMultiplier
            );
        public float MinimumChargeRadiusMultiplier =>
            Mathf.Max(0.01f, minimumChargeRadiusMultiplier);
        public float MaximumChargeRadiusMultiplier =>
            Mathf.Max(
                MinimumChargeRadiusMultiplier,
                maximumChargeRadiusMultiplier
            );
        public bool SupportsScope =>
            WeaponScopeEligibility.CanUseMagnifiedScope(weaponClass, supportsScope);
        public float ShoulderFieldOfViewDegrees => shoulderFieldOfViewDegrees;
        public float ScopedFieldOfViewDegrees => scopedFieldOfViewDegrees;
        public float ShoulderLookSensitivityMultiplier =>
            shoulderLookSensitivityMultiplier;
        public float ScopedLookSensitivityMultiplier =>
            scopedLookSensitivityMultiplier;
        public float AimTransitionSharpness => aimTransitionSharpness;
        public WeaponReticleStyle ReticleStyle => reticleStyle;
        public Color ReticleColor => reticleColor;
        public float ReticleBaseGapPixels => Mathf.Max(0f, reticleBaseGapPixels);
        public float ReticleArmLengthPixels => Mathf.Max(1f, reticleArmLengthPixels);
        public float ReticleShotExpansionPixels =>
            Mathf.Max(0f, reticleShotExpansionPixels);
        public float ReticleRecoverySharpness =>
            Mathf.Max(0.01f, reticleRecoverySharpness);
        public Color MuzzleFlashColor => authoredMuzzleFlashColor;
        public float MuzzleFlashIntensity =>
            Mathf.Max(0f, authoredMuzzleFlashIntensity);
        public float MuzzleFlashDuration =>
            Mathf.Max(0.01f, authoredMuzzleFlashDuration);
        public float VisualRecoilDistance => Mathf.Max(0f, visualRecoilDistance);
        public float VisualRecoilDegrees => Mathf.Max(0f, visualRecoilDegrees);

        public WeaponRuntimeConfig CreateRuntimeConfig()
        {
            return new WeaponRuntimeConfig(
                weaponId: weaponId,
                displayName: displayName,
                weaponClass: weaponClass,
                triggerMode: triggerMode,
                baseDamage: baseDamage,
                roundsPerMinute: roundsPerMinute,
                usesInfiniteAmmo: false,
                magazineCapacity: magazineCapacity,
                startingReserveAmmo: startingReserveAmmo,
                maximumReserveAmmo: maximumReserveAmmo,
                reloadDurationSeconds: reloadDurationSeconds,
                reloadCommitNormalizedTime: reloadCommitNormalizedTime,
                criticalChance: criticalChance,
                criticalDamageMultiplier: criticalDamageMultiplier,
                requiresManualCycle: requiresManualCycle,
                manualCycleDurationSeconds: manualCycleDurationSeconds,
                projectileSpeed: projectileSpeed,
                projectileLifetimeSeconds: projectileLifetimeSeconds,
                projectileRadius: projectileRadius,
                aimSpreadDegrees: aimSpreadDegrees,
                hipSpreadDegrees: hipSpreadDegrees,
                aimRecoilPitch: aimRecoilPitch,
                aimRecoilYaw: aimRecoilYaw,
                hipRecoilPitch: hipRecoilPitch,
                hipRecoilYaw: hipRecoilYaw
            );
        }

        public WeaponAimProfile CreateAimProfile()
        {
            return new WeaponAimProfile(
                supportsScope: SupportsScope,
                shoulderFieldOfViewDegrees: shoulderFieldOfViewDegrees,
                scopedFieldOfViewDegrees: scopedFieldOfViewDegrees,
                shoulderLookSensitivityMultiplier: shoulderLookSensitivityMultiplier,
                scopedLookSensitivityMultiplier: scopedLookSensitivityMultiplier,
                transitionSharpness: aimTransitionSharpness
            );
        }

        public WeaponChargeState CreateChargeState()
        {
            return UsesChargeShot
                ? new WeaponChargeState(
                    ChargeDurationSeconds,
                    MinimumChargeNormalized,
                    MinimumChargeDamageMultiplier,
                    MaximumChargeDamageMultiplier,
                    MinimumChargeRadiusMultiplier,
                    MaximumChargeRadiusMultiplier
                )
                : null;
        }

        public IReadOnlyList<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();
            errors.AddRange(CreateRuntimeConfig().GetValidationErrors());
            errors.AddRange(CreateAimProfile().GetValidationErrors());
            if (usesChargeShot && weaponClass != WeaponClass.HeavyWeapon)
            {
                errors.Add("Charge shots are currently restricted to HeavyWeapon definitions.");
            }
            if (
                float.IsNaN(splashDamageRadius) ||
                float.IsInfinity(splashDamageRadius) ||
                splashDamageRadius < 0f ||
                float.IsNaN(splashImpulse) ||
                float.IsInfinity(splashImpulse) ||
                splashImpulse < 0f ||
                float.IsNaN(splashStaggerSeconds) ||
                float.IsInfinity(splashStaggerSeconds) ||
                splashStaggerSeconds < 0f
            )
            {
                errors.Add("Splash radius, impulse, and stagger must be finite non-negative values.");
            }
            if (UsesChargeShot)
            {
                try
                {
                    CreateChargeState();
                }
                catch (System.ArgumentOutOfRangeException exception)
                {
                    errors.Add("Invalid charge-shot tuning: " + exception.ParamName + ".");
                }
            }
            return errors.ToArray();
        }
    }
}
