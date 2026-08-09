using System;
using System.Collections.Generic;

namespace Powersuit.Combat
{
    public enum WeaponTriggerMode
    {
        SemiAutomatic = 0,
        Automatic = 1
    }

    public enum WeaponClass
    {
        LegacyPrototype = 0,
        PrecisionRifle = 1,
        AssaultRifle = 2,
        Carbine = 3,
        Shotgun = 4,
        Sidearm = 5,
        HeavyWeapon = 6
    }

    /// <summary>
    /// Immutable, engine-independent weapon values consumed by WeaponRuntimeState.
    /// ScriptableObjects and other content sources should translate into this type.
    /// </summary>
    public sealed class WeaponRuntimeConfig
    {
        public WeaponRuntimeConfig(
            string weaponId,
            string displayName,
            WeaponClass weaponClass,
            WeaponTriggerMode triggerMode,
            float baseDamage,
            float roundsPerMinute,
            bool usesInfiniteAmmo,
            int magazineCapacity,
            int startingReserveAmmo,
            int maximumReserveAmmo,
            float reloadDurationSeconds,
            float reloadCommitNormalizedTime,
            float criticalChance,
            float criticalDamageMultiplier,
            bool requiresManualCycle,
            float manualCycleDurationSeconds,
            float projectileSpeed,
            float projectileLifetimeSeconds,
            float projectileRadius,
            float aimSpreadDegrees,
            float hipSpreadDegrees,
            float aimRecoilPitch,
            float aimRecoilYaw,
            float hipRecoilPitch,
            float hipRecoilYaw
        )
        {
            WeaponId = weaponId;
            DisplayName = displayName;
            WeaponClass = weaponClass;
            TriggerMode = triggerMode;
            BaseDamage = baseDamage;
            RoundsPerMinute = roundsPerMinute;
            UsesInfiniteAmmo = usesInfiniteAmmo;
            MagazineCapacity = magazineCapacity;
            StartingReserveAmmo = startingReserveAmmo;
            MaximumReserveAmmo = maximumReserveAmmo;
            ReloadDurationSeconds = reloadDurationSeconds;
            ReloadCommitNormalizedTime = reloadCommitNormalizedTime;
            CriticalChance = criticalChance;
            CriticalDamageMultiplier = criticalDamageMultiplier;
            RequiresManualCycle = requiresManualCycle;
            ManualCycleDurationSeconds = manualCycleDurationSeconds;
            ProjectileSpeed = projectileSpeed;
            ProjectileLifetimeSeconds = projectileLifetimeSeconds;
            ProjectileRadius = projectileRadius;
            AimSpreadDegrees = aimSpreadDegrees;
            HipSpreadDegrees = hipSpreadDegrees;
            AimRecoilPitch = aimRecoilPitch;
            AimRecoilYaw = aimRecoilYaw;
            HipRecoilPitch = hipRecoilPitch;
            HipRecoilYaw = hipRecoilYaw;
        }

        public string WeaponId { get; }
        public string DisplayName { get; }
        public WeaponClass WeaponClass { get; }
        public WeaponTriggerMode TriggerMode { get; }
        public float BaseDamage { get; }
        public float RoundsPerMinute { get; }
        public bool UsesInfiniteAmmo { get; }
        public int MagazineCapacity { get; }
        public int StartingReserveAmmo { get; }
        public int MaximumReserveAmmo { get; }
        public float ReloadDurationSeconds { get; }
        public float ReloadCommitNormalizedTime { get; }
        public float CriticalChance { get; }
        public float CriticalDamageMultiplier { get; }
        public bool RequiresManualCycle { get; }
        public float ManualCycleDurationSeconds { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileLifetimeSeconds { get; }
        public float ProjectileRadius { get; }
        public float AimSpreadDegrees { get; }
        public float HipSpreadDegrees { get; }
        public float AimRecoilPitch { get; }
        public float AimRecoilYaw { get; }
        public float HipRecoilPitch { get; }
        public float HipRecoilYaw { get; }

        public float ShotsPerSecond => RoundsPerMinute / 60f;
        public float ShotIntervalSeconds => 60f / RoundsPerMinute;
        public float ReloadCommitTimeSeconds =>
            ReloadDurationSeconds * ReloadCommitNormalizedTime;

        public IReadOnlyList<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrWhiteSpace(WeaponId))
            {
                errors.Add("Weapon ID is required.");
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                errors.Add("Display name is required.");
            }

            if (!Enum.IsDefined(typeof(WeaponClass), WeaponClass))
            {
                errors.Add("Weapon class is not supported.");
            }

            if (!Enum.IsDefined(typeof(WeaponTriggerMode), TriggerMode))
            {
                errors.Add("Trigger mode is not supported.");
            }

            RequirePositiveFinite(BaseDamage, "Base damage", errors);
            RequirePositiveFinite(RoundsPerMinute, "Rounds per minute", errors);

            if (MagazineCapacity <= 0)
            {
                errors.Add("Magazine capacity must be greater than zero.");
            }

            if (StartingReserveAmmo < 0)
            {
                errors.Add("Starting reserve ammo cannot be negative.");
            }

            if (MaximumReserveAmmo < 0)
            {
                errors.Add("Maximum reserve ammo cannot be negative.");
            }

            if (StartingReserveAmmo > MaximumReserveAmmo)
            {
                errors.Add("Starting reserve ammo cannot exceed maximum reserve ammo.");
            }

            RequireNonNegativeFinite(ReloadDurationSeconds, "Reload duration", errors);

            if (
                !IsFinite(ReloadCommitNormalizedTime) ||
                ReloadCommitNormalizedTime < 0f ||
                ReloadCommitNormalizedTime > 1f
            )
            {
                errors.Add("Reload commit normalized time must be between zero and one.");
            }

            if (!IsFinite(CriticalChance) || CriticalChance < 0f || CriticalChance > 1f)
            {
                errors.Add("Critical chance must be between zero and one.");
            }

            if (
                !IsFinite(CriticalDamageMultiplier) ||
                CriticalDamageMultiplier < 1f
            )
            {
                errors.Add("Critical damage multiplier must be at least one.");
            }

            RequireNonNegativeFinite(
                ManualCycleDurationSeconds,
                "Manual cycle duration",
                errors
            );

            if (RequiresManualCycle && ManualCycleDurationSeconds <= 0f)
            {
                errors.Add(
                    "Manual cycle duration must be greater than zero when manual cycling is enabled."
                );
            }

            RequirePositiveFinite(ProjectileSpeed, "Projectile speed", errors);
            RequirePositiveFinite(
                ProjectileLifetimeSeconds,
                "Projectile lifetime",
                errors
            );
            RequirePositiveFinite(ProjectileRadius, "Projectile radius", errors);
            RequireNonNegativeFinite(AimSpreadDegrees, "Aim spread", errors);
            RequireNonNegativeFinite(HipSpreadDegrees, "Hip spread", errors);
            RequireNonNegativeFinite(AimRecoilPitch, "Aim recoil pitch", errors);
            RequireNonNegativeFinite(AimRecoilYaw, "Aim recoil yaw", errors);
            RequireNonNegativeFinite(HipRecoilPitch, "Hip recoil pitch", errors);
            RequireNonNegativeFinite(HipRecoilYaw, "Hip recoil yaw", errors);

            return errors.ToArray();
        }

        public void ValidateOrThrow()
        {
            IReadOnlyList<string> errors = GetValidationErrors();
            if (errors.Count > 0)
            {
                throw new ArgumentException(
                    $"Invalid weapon configuration '{WeaponId ?? "<null>"}': " +
                    string.Join(" ", errors)
                );
            }
        }

        public static WeaponRuntimeConfig CreateLegacyInfiniteAmmo(
            float baseDamage,
            float shotsPerSecond,
            float projectileSpeed,
            float projectileLifetimeSeconds,
            float projectileRadius,
            float aimSpreadDegrees,
            float hipSpreadDegrees,
            float aimRecoilPitch,
            float aimRecoilYaw,
            float hipRecoilPitch,
            float hipRecoilYaw
        )
        {
            return new WeaponRuntimeConfig(
                weaponId: "legacy-powered-suit-weapon",
                displayName: "Powered Suit Weapon",
                weaponClass: WeaponClass.LegacyPrototype,
                triggerMode: WeaponTriggerMode.Automatic,
                baseDamage: baseDamage,
                roundsPerMinute: shotsPerSecond * 60f,
                usesInfiniteAmmo: true,
                magazineCapacity: 1,
                startingReserveAmmo: 0,
                maximumReserveAmmo: 0,
                reloadDurationSeconds: 0f,
                reloadCommitNormalizedTime: 0f,
                criticalChance: 0f,
                criticalDamageMultiplier: 1f,
                requiresManualCycle: false,
                manualCycleDurationSeconds: 0f,
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

        private static void RequirePositiveFinite(
            float value,
            string fieldName,
            ICollection<string> errors
        )
        {
            if (!IsFinite(value) || value <= 0f)
            {
                errors.Add($"{fieldName} must be a finite value greater than zero.");
            }
        }

        private static void RequireNonNegativeFinite(
            float value,
            string fieldName,
            ICollection<string> errors
        )
        {
            if (!IsFinite(value) || value < 0f)
            {
                errors.Add($"{fieldName} must be a finite non-negative value.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
