using System;

namespace Powersuit.UI.HUD
{
    [Flags]
    public enum PowerSuitHudDirtyFlags
    {
        None = 0,
        Health = 1 << 0,
        Ammunition = 1 << 1,
        Reload = 1 << 2,
        ShoulderRocket = 1 << 3,
        Lightning = 1 << 4,
        Ultimate = 1 << 5,
        PropulsionHeat = 1 << 6,
        All = Health | Ammunition | Reload | ShoulderRocket | Lightning |
            Ultimate | PropulsionHeat
    }

    public readonly struct HudHealthState : IEquatable<HudHealthState>
    {
        public HudHealthState(
            bool isAvailable,
            float current,
            float maximum,
            bool isDefeated
        )
        {
            IsAvailable = isAvailable;
            if (!isAvailable)
            {
                Current = 0f;
                Maximum = 0f;
                Normalized = 0f;
                IsDefeated = false;
                return;
            }

            Maximum = HudValueMath.PositiveOrFallback(maximum, 1f);
            Current = HudValueMath.Clamp(
                HudValueMath.NonNegativeOrZero(current),
                0f,
                Maximum
            );
            Normalized = Current / Maximum;
            IsDefeated = isDefeated || Current <= 0f;
        }

        public static HudHealthState Missing => default;

        public bool IsAvailable { get; }
        public float Current { get; }
        public float Maximum { get; }
        public float Normalized { get; }
        public bool IsDefeated { get; }

        public bool Equals(HudHealthState other)
        {
            return IsAvailable == other.IsAvailable &&
                Current.Equals(other.Current) &&
                Maximum.Equals(other.Maximum) &&
                Normalized.Equals(other.Normalized) &&
                IsDefeated == other.IsDefeated;
        }

        public override bool Equals(object obj)
        {
            return obj is HudHealthState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsAvailable ? 1 : 0;
                hash = (hash * 397) ^ Current.GetHashCode();
                hash = (hash * 397) ^ Maximum.GetHashCode();
                hash = (hash * 397) ^ Normalized.GetHashCode();
                return (hash * 397) ^ (IsDefeated ? 1 : 0);
            }
        }
    }

    public readonly struct HudWeaponState : IEquatable<HudWeaponState>
    {
        public HudWeaponState(
            bool isAvailable,
            int magazine,
            int magazineCapacity,
            int reserve,
            bool usesInfiniteAmmo,
            bool isReloading,
            float reloadNormalized,
            string displayName = null
        )
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? string.Empty
                : displayName.Trim();
            IsAvailable = isAvailable;
            if (!isAvailable)
            {
                Magazine = 0;
                MagazineCapacity = 0;
                Reserve = 0;
                UsesInfiniteAmmo = false;
                IsReloading = false;
                ReloadNormalized = 0f;
                return;
            }

            MagazineCapacity = Math.Max(0, magazineCapacity);
            Magazine = Math.Max(0, Math.Min(magazine, MagazineCapacity));
            Reserve = Math.Max(0, reserve);
            UsesInfiniteAmmo = usesInfiniteAmmo;
            IsReloading = isReloading;
            ReloadNormalized = isReloading
                ? HudValueMath.Clamp01(reloadNormalized)
                : 0f;
        }

        public static HudWeaponState Missing => default;

        public string DisplayName { get; }
        public bool IsAvailable { get; }
        public int Magazine { get; }
        public int MagazineCapacity { get; }
        public int Reserve { get; }
        public bool UsesInfiniteAmmo { get; }
        public bool IsReloading { get; }
        public float ReloadNormalized { get; }

        public bool AmmunitionEquals(HudWeaponState other)
        {
            return IsAvailable == other.IsAvailable &&
                string.Equals(
                    DisplayName,
                    other.DisplayName,
                    StringComparison.Ordinal
                ) &&
                Magazine == other.Magazine &&
                MagazineCapacity == other.MagazineCapacity &&
                Reserve == other.Reserve &&
                UsesInfiniteAmmo == other.UsesInfiniteAmmo;
        }

        public bool ReloadEquals(HudWeaponState other)
        {
            return IsAvailable == other.IsAvailable &&
                IsReloading == other.IsReloading &&
                ReloadNormalized.Equals(other.ReloadNormalized);
        }

        public bool Equals(HudWeaponState other)
        {
            return AmmunitionEquals(other) && ReloadEquals(other);
        }

        public override bool Equals(object obj)
        {
            return obj is HudWeaponState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsAvailable ? 1 : 0;
                hash = (hash * 397) ^
                    (DisplayName != null ? DisplayName.GetHashCode() : 0);
                hash = (hash * 397) ^ Magazine;
                hash = (hash * 397) ^ MagazineCapacity;
                hash = (hash * 397) ^ Reserve;
                hash = (hash * 397) ^ (UsesInfiniteAmmo ? 1 : 0);
                hash = (hash * 397) ^ (IsReloading ? 1 : 0);
                return (hash * 397) ^ ReloadNormalized.GetHashCode();
            }
        }
    }

    public readonly struct HudAbilityState : IEquatable<HudAbilityState>
    {
        public HudAbilityState(
            bool isAvailable,
            float cooldownRemaining,
            float cooldownNormalized,
            bool isReady,
            bool isActive
        )
        {
            IsAvailable = isAvailable;
            if (!isAvailable)
            {
                CooldownRemaining = 0f;
                CooldownNormalized = 0f;
                IsReady = false;
                IsActive = false;
                return;
            }

            CooldownRemaining = HudValueMath.NonNegativeOrZero(cooldownRemaining);
            CooldownNormalized = HudValueMath.Clamp01(cooldownNormalized);
            IsReady = isReady;
            IsActive = isActive;
        }

        public static HudAbilityState Missing => default;

        public bool IsAvailable { get; }
        public float CooldownRemaining { get; }
        public float CooldownNormalized { get; }
        public float ReadyNormalized => IsAvailable
            ? HudValueMath.Clamp01(1f - CooldownNormalized)
            : 0f;
        public bool IsReady { get; }
        public bool IsActive { get; }

        public bool Equals(HudAbilityState other)
        {
            return IsAvailable == other.IsAvailable &&
                CooldownRemaining.Equals(other.CooldownRemaining) &&
                CooldownNormalized.Equals(other.CooldownNormalized) &&
                IsReady == other.IsReady &&
                IsActive == other.IsActive;
        }

        public override bool Equals(object obj)
        {
            return obj is HudAbilityState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsAvailable ? 1 : 0;
                hash = (hash * 397) ^ CooldownRemaining.GetHashCode();
                hash = (hash * 397) ^ CooldownNormalized.GetHashCode();
                hash = (hash * 397) ^ (IsReady ? 1 : 0);
                return (hash * 397) ^ (IsActive ? 1 : 0);
            }
        }
    }

    public readonly struct HudUltimateState : IEquatable<HudUltimateState>
    {
        public HudUltimateState(
            bool isAvailable,
            float meterNormalized,
            bool isReady,
            bool isActive
        )
        {
            IsAvailable = isAvailable;
            MeterNormalized = isAvailable
                ? HudValueMath.Clamp01(meterNormalized)
                : 0f;
            IsReady = isAvailable && isReady;
            IsActive = isAvailable && isActive;
        }

        public static HudUltimateState Missing => default;

        public bool IsAvailable { get; }
        public float MeterNormalized { get; }
        public bool IsReady { get; }
        public bool IsActive { get; }

        public bool Equals(HudUltimateState other)
        {
            return IsAvailable == other.IsAvailable &&
                MeterNormalized.Equals(other.MeterNormalized) &&
                IsReady == other.IsReady &&
                IsActive == other.IsActive;
        }

        public override bool Equals(object obj)
        {
            return obj is HudUltimateState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsAvailable ? 1 : 0;
                hash = (hash * 397) ^ MeterNormalized.GetHashCode();
                hash = (hash * 397) ^ (IsReady ? 1 : 0);
                return (hash * 397) ^ (IsActive ? 1 : 0);
            }
        }
    }

    public readonly struct HudPropulsionHeatState :
        IEquatable<HudPropulsionHeatState>
    {
        public HudPropulsionHeatState(
            bool isAvailable,
            float heat,
            float maximumHeat,
            bool isOverheated,
            bool isActive
        )
        {
            IsAvailable = isAvailable;
            if (!isAvailable)
            {
                Heat = 0f;
                MaximumHeat = 0f;
                Normalized = 0f;
                IsOverheated = false;
                IsActive = false;
                return;
            }

            MaximumHeat = HudValueMath.PositiveOrFallback(maximumHeat, 1f);
            Heat = HudValueMath.Clamp(
                HudValueMath.NonNegativeOrZero(heat),
                0f,
                MaximumHeat
            );
            Normalized = Heat / MaximumHeat;
            IsOverheated = isOverheated;
            IsActive = isActive && !isOverheated;
        }

        public static HudPropulsionHeatState Missing => default;

        public bool IsAvailable { get; }
        public float Heat { get; }
        public float MaximumHeat { get; }
        public float Normalized { get; }
        public bool IsOverheated { get; }
        public bool IsActive { get; }

        public bool Equals(HudPropulsionHeatState other)
        {
            return IsAvailable == other.IsAvailable &&
                Heat.Equals(other.Heat) &&
                MaximumHeat.Equals(other.MaximumHeat) &&
                Normalized.Equals(other.Normalized) &&
                IsOverheated == other.IsOverheated &&
                IsActive == other.IsActive;
        }

        public override bool Equals(object obj)
        {
            return obj is HudPropulsionHeatState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsAvailable ? 1 : 0;
                hash = (hash * 397) ^ Heat.GetHashCode();
                hash = (hash * 397) ^ MaximumHeat.GetHashCode();
                hash = (hash * 397) ^ Normalized.GetHashCode();
                hash = (hash * 397) ^ (IsOverheated ? 1 : 0);
                return (hash * 397) ^ (IsActive ? 1 : 0);
            }
        }
    }

    public readonly struct PowerSuitHudSnapshot : IEquatable<PowerSuitHudSnapshot>
    {
        public PowerSuitHudSnapshot(
            HudHealthState health,
            HudWeaponState weapon,
            HudPropulsionHeatState propulsionHeat,
            HudAbilityState shoulderRocket,
            HudAbilityState lightning,
            HudUltimateState ultimate
        )
        {
            Health = health;
            Weapon = weapon;
            PropulsionHeat = propulsionHeat;
            ShoulderRocket = shoulderRocket;
            Lightning = lightning;
            Ultimate = ultimate;
        }

        public static PowerSuitHudSnapshot Missing => default;

        public HudHealthState Health { get; }
        public HudWeaponState Weapon { get; }
        public HudPropulsionHeatState PropulsionHeat { get; }
        public HudAbilityState ShoulderRocket { get; }
        public HudAbilityState Lightning { get; }
        public HudUltimateState Ultimate { get; }

        public bool Equals(PowerSuitHudSnapshot other)
        {
            return Health.Equals(other.Health) &&
                Weapon.Equals(other.Weapon) &&
                PropulsionHeat.Equals(other.PropulsionHeat) &&
                ShoulderRocket.Equals(other.ShoulderRocket) &&
                Lightning.Equals(other.Lightning) &&
                Ultimate.Equals(other.Ultimate);
        }

        public override bool Equals(object obj)
        {
            return obj is PowerSuitHudSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Health.GetHashCode();
                hash = (hash * 397) ^ Weapon.GetHashCode();
                hash = (hash * 397) ^ PropulsionHeat.GetHashCode();
                hash = (hash * 397) ^ ShoulderRocket.GetHashCode();
                hash = (hash * 397) ^ Lightning.GetHashCode();
                return (hash * 397) ^ Ultimate.GetHashCode();
            }
        }
    }

    public sealed class PowerSuitHudChangeDetector
    {
        private bool hasPrevious;
        private PowerSuitHudSnapshot previous;

        public bool HasPrevious => hasPrevious;
        public PowerSuitHudSnapshot Previous => previous;

        public PowerSuitHudDirtyFlags Capture(PowerSuitHudSnapshot next)
        {
            if (!hasPrevious)
            {
                previous = next;
                hasPrevious = true;
                return PowerSuitHudDirtyFlags.All;
            }

            PowerSuitHudDirtyFlags dirty = PowerSuitHudDirtyFlags.None;
            if (!previous.Health.Equals(next.Health))
            {
                dirty |= PowerSuitHudDirtyFlags.Health;
            }

            if (!previous.Weapon.AmmunitionEquals(next.Weapon))
            {
                dirty |= PowerSuitHudDirtyFlags.Ammunition;
            }

            if (!previous.Weapon.ReloadEquals(next.Weapon))
            {
                dirty |= PowerSuitHudDirtyFlags.Reload;
            }

            if (!previous.PropulsionHeat.Equals(next.PropulsionHeat))
            {
                dirty |= PowerSuitHudDirtyFlags.PropulsionHeat;
            }

            if (!previous.ShoulderRocket.Equals(next.ShoulderRocket))
            {
                dirty |= PowerSuitHudDirtyFlags.ShoulderRocket;
            }

            if (!previous.Lightning.Equals(next.Lightning))
            {
                dirty |= PowerSuitHudDirtyFlags.Lightning;
            }

            if (!previous.Ultimate.Equals(next.Ultimate))
            {
                dirty |= PowerSuitHudDirtyFlags.Ultimate;
            }

            previous = next;
            return dirty;
        }

        public void Reset()
        {
            hasPrevious = false;
            previous = default;
        }
    }

    /// <summary>
    /// Tracks only values that materially change the text presented by the
    /// HUD. Progress fills still use <see cref="PowerSuitHudChangeDetector"/>
    /// and can update every frame, while formatted strings are rebuilt only
    /// when their visible, rounded value changes.
    /// </summary>
    public sealed class PowerSuitHudTextChangeDetector
    {
        private bool hasPrevious;
        private PowerSuitHudSnapshot previous;

        public bool HasPrevious => hasPrevious;

        public PowerSuitHudDirtyFlags Capture(PowerSuitHudSnapshot next)
        {
            if (!hasPrevious)
            {
                previous = next;
                hasPrevious = true;
                return PowerSuitHudDirtyFlags.All;
            }

            PowerSuitHudDirtyFlags dirty = PowerSuitHudDirtyFlags.None;
            if (!HealthTextEquals(previous.Health, next.Health))
            {
                dirty |= PowerSuitHudDirtyFlags.Health;
            }

            if (!AmmunitionTextEquals(previous.Weapon, next.Weapon))
            {
                dirty |= PowerSuitHudDirtyFlags.Ammunition;
            }

            if (!ReloadTextEquals(previous.Weapon, next.Weapon))
            {
                dirty |= PowerSuitHudDirtyFlags.Reload;
            }

            if (!HeatTextEquals(previous.PropulsionHeat, next.PropulsionHeat))
            {
                dirty |= PowerSuitHudDirtyFlags.PropulsionHeat;
            }

            if (!AbilityTextEquals(previous.ShoulderRocket, next.ShoulderRocket))
            {
                dirty |= PowerSuitHudDirtyFlags.ShoulderRocket;
            }

            if (!AbilityTextEquals(previous.Lightning, next.Lightning))
            {
                dirty |= PowerSuitHudDirtyFlags.Lightning;
            }

            if (!UltimateTextEquals(previous.Ultimate, next.Ultimate))
            {
                dirty |= PowerSuitHudDirtyFlags.Ultimate;
            }

            previous = next;
            return dirty;
        }

        public void Reset()
        {
            hasPrevious = false;
            previous = default;
        }

        private static bool HealthTextEquals(
            HudHealthState left,
            HudHealthState right
        )
        {
            if (left.IsAvailable != right.IsAvailable)
            {
                return false;
            }

            if (!left.IsAvailable)
            {
                return true;
            }

            if (left.IsDefeated != right.IsDefeated)
            {
                return false;
            }

            return left.IsDefeated ||
                (Math.Ceiling(left.Current) == Math.Ceiling(right.Current) &&
                 Math.Ceiling(left.Maximum) == Math.Ceiling(right.Maximum));
        }

        private static bool AmmunitionTextEquals(
            HudWeaponState left,
            HudWeaponState right
        )
        {
            if (left.IsAvailable != right.IsAvailable)
            {
                return false;
            }

            if (!left.IsAvailable)
            {
                return true;
            }

            if (left.UsesInfiniteAmmo != right.UsesInfiniteAmmo)
            {
                return false;
            }

            return left.UsesInfiniteAmmo ||
                (left.Magazine == right.Magazine &&
                 left.MagazineCapacity == right.MagazineCapacity &&
                 left.Reserve == right.Reserve);
        }

        private static bool ReloadTextEquals(
            HudWeaponState left,
            HudWeaponState right
        )
        {
            bool leftVisible = left.IsAvailable && left.IsReloading;
            bool rightVisible = right.IsAvailable && right.IsReloading;
            if (leftVisible != rightVisible)
            {
                return false;
            }

            return !leftVisible ||
                ToDisplayedInteger(left.ReloadNormalized * 100f) ==
                ToDisplayedInteger(right.ReloadNormalized * 100f);
        }

        private static bool AbilityTextEquals(
            HudAbilityState left,
            HudAbilityState right
        )
        {
            AbilityLabelState leftState = GetAbilityLabelState(left);
            AbilityLabelState rightState = GetAbilityLabelState(right);
            if (leftState != rightState)
            {
                return false;
            }

            return leftState != AbilityLabelState.Cooldown ||
                ToDisplayedInteger(left.CooldownRemaining * 10f) ==
                ToDisplayedInteger(right.CooldownRemaining * 10f);
        }

        private static bool HeatTextEquals(
            HudPropulsionHeatState left,
            HudPropulsionHeatState right
        )
        {
            if (
                left.IsAvailable != right.IsAvailable ||
                left.IsOverheated != right.IsOverheated ||
                left.IsActive != right.IsActive
            )
            {
                return false;
            }

            return !left.IsAvailable ||
                ToDisplayedInteger(left.Normalized * 100f) ==
                ToDisplayedInteger(right.Normalized * 100f);
        }

        private static bool UltimateTextEquals(
            HudUltimateState left,
            HudUltimateState right
        )
        {
            AbilityLabelState leftState = GetUltimateLabelState(left);
            AbilityLabelState rightState = GetUltimateLabelState(right);
            if (leftState != rightState)
            {
                return false;
            }

            return leftState != AbilityLabelState.Cooldown ||
                ToDisplayedInteger(left.MeterNormalized * 100f) ==
                ToDisplayedInteger(right.MeterNormalized * 100f);
        }

        private static AbilityLabelState GetAbilityLabelState(
            HudAbilityState state
        )
        {
            if (!state.IsAvailable)
            {
                return AbilityLabelState.Unavailable;
            }
            if (state.IsActive)
            {
                return AbilityLabelState.Active;
            }
            return state.IsReady
                ? AbilityLabelState.Ready
                : AbilityLabelState.Cooldown;
        }

        private static AbilityLabelState GetUltimateLabelState(
            HudUltimateState state
        )
        {
            if (!state.IsAvailable)
            {
                return AbilityLabelState.Unavailable;
            }
            if (state.IsActive)
            {
                return AbilityLabelState.Active;
            }
            return state.IsReady
                ? AbilityLabelState.Ready
                : AbilityLabelState.Cooldown;
        }

        private static int ToDisplayedInteger(float value)
        {
            return (int)Math.Floor(value + 0.5f);
        }

        private enum AbilityLabelState
        {
            Unavailable,
            Active,
            Ready,
            Cooldown
        }
    }

    internal static class HudValueMath
    {
        public static float Clamp01(float value)
        {
            return Clamp(IsFinite(value) ? value : 0f, 0f, 1f);
        }

        public static float NonNegativeOrZero(float value)
        {
            return IsFinite(value) ? Math.Max(0f, value) : 0f;
        }

        public static float PositiveOrFallback(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        public static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
