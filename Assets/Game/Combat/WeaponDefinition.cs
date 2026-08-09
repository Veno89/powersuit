using System.Collections.Generic;
using UnityEngine;

namespace Powersuit.Combat
{
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

        [Header("Handling")]
        [SerializeField, Min(0f)] private float aimSpreadDegrees = 0.15f;
        [SerializeField, Min(0f)] private float hipSpreadDegrees = 1.25f;
        [SerializeField, Min(0f)] private float aimRecoilPitch = 0.9f;
        [SerializeField, Min(0f)] private float aimRecoilYaw = 0.25f;
        [SerializeField, Min(0f)] private float hipRecoilPitch = 1.6f;
        [SerializeField, Min(0f)] private float hipRecoilYaw = 0.5f;

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
        public float ReloadDurationSeconds => reloadDurationSeconds;
        public float ReloadCommitNormalizedTime => reloadCommitNormalizedTime;
        public bool RequiresManualCycle => requiresManualCycle;
        public float ManualCycleDurationSeconds => manualCycleDurationSeconds;
        public float ProjectileSpeed => projectileSpeed;
        public float ProjectileLifetimeSeconds => projectileLifetimeSeconds;
        public float ProjectileRadius => projectileRadius;
        public float AimSpreadDegrees => aimSpreadDegrees;
        public float HipSpreadDegrees => hipSpreadDegrees;
        public float AimRecoilPitch => aimRecoilPitch;
        public float AimRecoilYaw => aimRecoilYaw;
        public float HipRecoilPitch => hipRecoilPitch;
        public float HipRecoilYaw => hipRecoilYaw;

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

        public IReadOnlyList<string> GetValidationErrors()
        {
            return CreateRuntimeConfig().GetValidationErrors();
        }
    }
}
