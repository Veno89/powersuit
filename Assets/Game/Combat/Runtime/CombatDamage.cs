using System;

namespace Powersuit.Combat
{
    /// <summary>
    /// Identifies combat allegiance without depending on Unity tags or layers.
    /// None represents unassigned ownership and is denied by the default
    /// faction policy until a legacy adapter opts in explicitly.
    /// </summary>
    public enum CombatFaction
    {
        None = 0,
        Player = 1,
        Enemy = 2,
        Neutral = 3
    }

    public enum DamageType
    {
        Kinetic = 0,
        Explosive = 1,
        Lightning = 2,
        Void = 3,
        Environmental = 4
    }

    /// <summary>
    /// Immutable description of one attempted damage transaction.
    /// </summary>
    public readonly struct DamageInfo
    {
        public DamageInfo(
            object source,
            CombatFaction faction,
            float amount,
            CombatVector3 position,
            CombatVector3 direction,
            bool isCritical = false
        )
            : this(
                source,
                faction,
                DamageType.Kinetic,
                amount,
                position,
                direction,
                isCritical
            )
        {
        }

        public DamageInfo(
            object source,
            CombatFaction faction,
            DamageType damageType,
            float amount,
            CombatVector3 position,
            CombatVector3 direction,
            bool isCritical = false
        )
        {
            RequireKnownFaction(faction, nameof(faction));
            RequireKnownDamageType(damageType, nameof(damageType));
            RequireFiniteNonNegative(amount, nameof(amount));

            Source = source;
            Faction = faction;
            DamageType = damageType;
            Amount = amount;
            Position = position;
            Direction = direction;
            IsCritical = isCritical;
        }

        public object Source { get; }
        public CombatFaction Faction { get; }
        public DamageType DamageType { get; }
        public float Amount { get; }
        public CombatVector3 Position { get; }
        public CombatVector3 Direction { get; }
        public bool IsCritical { get; }

        private static void RequireKnownFaction(
            CombatFaction faction,
            string parameterName
        )
        {
            if (!CombatFactionPolicy.IsKnown(faction))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Combat faction is not supported."
                );
            }
        }

        private static void RequireKnownDamageType(
            DamageType damageType,
            string parameterName
        )
        {
            int numericValue = (int)damageType;
            if (
                numericValue < (int)DamageType.Kinetic ||
                numericValue > (int)DamageType.Environmental
            )
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Damage type is not supported."
                );
            }
        }

        private static void RequireFiniteNonNegative(
            float value,
            string parameterName
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Damage amount must be a finite non-negative value."
                );
            }
        }
    }

    /// <summary>
    /// Immutable outcome returned by a damage receiver.
    /// </summary>
    public readonly struct DamageResult
    {
        private DamageResult(bool wasApplied, float appliedAmount, bool wasKilled)
        {
            WasApplied = wasApplied;
            AppliedAmount = appliedAmount;
            WasKilled = wasKilled;
        }

        public bool WasApplied { get; }
        public float AppliedAmount { get; }
        public bool WasKilled { get; }

        public static DamageResult Ignored => default;

        public static DamageResult Applied(float appliedAmount, bool wasKilled)
        {
            if (
                float.IsNaN(appliedAmount) ||
                float.IsInfinity(appliedAmount) ||
                appliedAmount < 0f
            )
            {
                throw new ArgumentOutOfRangeException(
                    nameof(appliedAmount),
                    "Applied damage must be a finite non-negative value."
                );
            }

            return new DamageResult(true, appliedAmount, wasKilled);
        }
    }

    /// <summary>
    /// Common engine-independent damage boundary used by player, enemy, and
    /// future destructible adapters. Receivers remain authoritative for faction
    /// filtering, mitigation, death, and the amount actually applied.
    /// </summary>
    public interface IDamageReceiver
    {
        CombatFaction Faction { get; }
        bool CanReceiveDamage { get; }

        DamageResult ApplyDamage(DamageInfo damage);
    }

    public static class CombatFactionPolicy
    {
        /// <summary>
        /// Returns whether one faction may damage another. Unassigned ownership
        /// is denied by default so player-safe abilities cannot accidentally hit
        /// everything; a legacy adapter must opt in explicitly while migrating.
        /// Neutral hazards or destructibles may interact with every configured
        /// faction. Friendly fire is opt-in.
        /// </summary>
        public static bool CanDamage(
            CombatFaction source,
            CombatFaction target,
            bool allowFriendlyFire = false,
            bool allowUnassigned = false
        )
        {
            if (!IsKnown(source) || !IsKnown(target))
            {
                return false;
            }

            if (
                source == CombatFaction.None ||
                target == CombatFaction.None
            )
            {
                return allowUnassigned;
            }

            if (
                allowFriendlyFire ||
                source == CombatFaction.Neutral ||
                target == CombatFaction.Neutral
            )
            {
                return true;
            }

            return source != target;
        }

        public static bool IsKnown(CombatFaction faction)
        {
            int numericValue = (int)faction;
            return
                numericValue >= (int)CombatFaction.None &&
                numericValue <= (int)CombatFaction.Neutral;
        }
    }
}
