using System;
using System.Globalization;

namespace Powersuit.UI.HUD
{
    public static class PowerSuitHudFormatter
    {
        public const string Unavailable = "--";

        public static string FormatHealth(HudHealthState health)
        {
            if (!health.IsAvailable)
            {
                return "HP " + Unavailable;
            }

            if (health.IsDefeated)
            {
                return "SUIT DISABLED";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "HP {0:0} / {1:0}",
                Math.Ceiling(health.Current),
                Math.Ceiling(health.Maximum)
            );
        }

        public static string FormatAmmunition(HudWeaponState weapon)
        {
            if (!weapon.IsAvailable)
            {
                return "AMMO " + Unavailable;
            }

            bool hasDisplayName = !string.IsNullOrWhiteSpace(
                weapon.DisplayName
            );
            string prefix = hasDisplayName
                ? weapon.DisplayName.ToUpperInvariant()
                : "AMMO";
            string separator = hasDisplayName ? "  " : " ";

            if (weapon.UsesInfiniteAmmo)
            {
                return prefix + separator + "INFINITE";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}{2} / {3}  RES {4}",
                prefix,
                separator,
                weapon.Magazine,
                weapon.MagazineCapacity,
                weapon.Reserve
            );
        }

        public static string FormatReload(HudWeaponState weapon)
        {
            if (!weapon.IsAvailable || (!weapon.IsReloading && !weapon.IsCharging))
            {
                return string.Empty;
            }

            if (weapon.IsCharging)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "PLASMA CHARGE {0:0}%",
                    weapon.ChargeNormalized * 100f
                );
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "RELOADING {0:0}%",
                weapon.ReloadNormalized * 100f
            );
        }

        public static string FormatPropulsionHeat(HudPropulsionHeatState heat)
        {
            if (!heat.IsAvailable)
            {
                return "HEAT " + Unavailable;
            }

            if (heat.IsOverheated)
            {
                return "PROPULSION OVERHEATED";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                heat.IsActive ? "HEAT {0:0}%" : "COOLING {0:0}%",
                heat.Normalized * 100f
            );
        }

        public static string FormatAbility(string displayName, HudAbilityState ability)
        {
            string safeName = string.IsNullOrWhiteSpace(displayName)
                ? "ABILITY"
                : displayName.Trim();
            if (!ability.IsAvailable)
            {
                return safeName + " " + Unavailable;
            }

            if (ability.IsActive)
            {
                return safeName + " ACTIVE";
            }

            if (ability.IsReady)
            {
                return safeName + " READY";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1:0.0}s",
                safeName,
                ability.CooldownRemaining
            );
        }

        public static string FormatUltimate(HudUltimateState ultimate)
        {
            if (!ultimate.IsAvailable)
            {
                return "VOID " + Unavailable;
            }

            if (ultimate.IsActive)
            {
                return "VOID ACTIVE";
            }

            if (ultimate.IsReady)
            {
                return "VOID READY";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "VOID {0:0}%",
                ultimate.MeterNormalized * 100f
            );
        }
    }
}
