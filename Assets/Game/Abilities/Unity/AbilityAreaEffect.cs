using System;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Abilities.UnityAdapters
{
    public enum AbilityExternalForceMode
    {
        None = 0,
        Pull = 1,
        Push = 2
    }

    /// <summary>
    /// Immutable Unity-facing area-effect contract. A future non-alloc query
    /// service remains responsible for finding and deduplicating recipients.
    /// This value applies authoritative DamageInfo and IExternalForceReceiver
    /// contracts once a logical recipient has been selected.
    /// </summary>
    public readonly struct AbilityAreaEffect
    {
        private const float DirectionEpsilonSquared = 0.000001f;

        public AbilityAreaEffect(
            object source,
            CombatFaction sourceFaction,
            DamageType damageType,
            Vector3 center,
            Vector3 surfaceNormal,
            float radius,
            float damage,
            float minimumDamageMultiplier,
            AbilityExternalForceMode forceMode,
            float forceMagnitude
        )
        {
            if (!CombatFactionPolicy.IsKnown(sourceFaction))
            {
                throw new ArgumentOutOfRangeException(nameof(sourceFaction));
            }

            int damageTypeValue = (int)damageType;
            if (
                damageTypeValue < (int)DamageType.Kinetic ||
                damageTypeValue > (int)DamageType.Environmental
            )
            {
                throw new ArgumentOutOfRangeException(nameof(damageType));
            }

            RequireFinite(center, nameof(center));
            RequireFinite(surfaceNormal, nameof(surfaceNormal));
            RequireFinitePositive(radius, nameof(radius));
            RequireFiniteNonNegative(damage, nameof(damage));
            RequireFiniteRange(
                minimumDamageMultiplier,
                0f,
                1f,
                nameof(minimumDamageMultiplier)
            );
            RequireFiniteNonNegative(forceMagnitude, nameof(forceMagnitude));

            Source = source;
            SourceFaction = sourceFaction;
            DamageType = damageType;
            Center = center;
            SurfaceNormal = NormalizeOrFallback(surfaceNormal, Vector3.up);
            Radius = radius;
            Damage = damage;
            MinimumDamageMultiplier = minimumDamageMultiplier;
            ForceMode = forceMode;
            ForceMagnitude = forceMagnitude;
        }

        public object Source { get; }
        public CombatFaction SourceFaction { get; }
        public DamageType DamageType { get; }
        public Vector3 Center { get; }
        public Vector3 SurfaceNormal { get; }
        public float Radius { get; }
        public float Damage { get; }
        public float MinimumDamageMultiplier { get; }
        public AbilityExternalForceMode ForceMode { get; }
        public float ForceMagnitude { get; }

        public bool Contains(Vector3 recipientPosition)
        {
            RequireFinite(recipientPosition, nameof(recipientPosition));
            return (recipientPosition - Center).sqrMagnitude <= Radius * Radius;
        }

        public float EvaluateDamageMultiplier(Vector3 recipientPosition)
        {
            RequireFinite(recipientPosition, nameof(recipientPosition));
            float normalizedDistance = Mathf.Clamp01(
                Vector3.Distance(Center, recipientPosition) / Radius
            );
            return Mathf.Lerp(1f, MinimumDamageMultiplier, normalizedDistance);
        }

        public DamageInfo CreateDamageInfo(
            Vector3 recipientPosition,
            bool isCritical = false
        )
        {
            if (!Contains(recipientPosition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(recipientPosition),
                    "The recipient is outside this area effect."
                );
            }

            Vector3 direction = NormalizeOrFallback(
                recipientPosition - Center,
                SurfaceNormal
            );
            return new DamageInfo(
                Source,
                SourceFaction,
                DamageType,
                Damage * EvaluateDamageMultiplier(recipientPosition),
                ToCombat(recipientPosition),
                ToCombat(direction),
                isCritical
            );
        }

        public DamageResult ApplyDamage(
            IDamageReceiver receiver,
            Vector3 recipientPosition,
            bool isCritical = false
        )
        {
            if (
                receiver == null ||
                !receiver.CanReceiveDamage ||
                !Contains(recipientPosition)
            )
            {
                return DamageResult.Ignored;
            }

            return receiver.ApplyDamage(
                CreateDamageInfo(recipientPosition, isCritical)
            );
        }

        public bool ApplyExternalForce(
            IExternalForceReceiver receiver,
            Vector3 recipientPosition
        )
        {
            if (!(receiver is IDamageReceiver damageReceiver))
            {
                return false;
            }

            return ApplyExternalForce(
                receiver,
                recipientPosition,
                damageReceiver.Faction
            );
        }

        public bool ApplyExternalForce(
            IExternalForceReceiver receiver,
            Vector3 recipientPosition,
            CombatFaction recipientFaction
        )
        {
            if (
                receiver == null ||
                !receiver.CanReceiveExternalForce ||
                !CombatFactionPolicy.CanDamage(
                    SourceFaction,
                    recipientFaction
                ) ||
                ForceMode == AbilityExternalForceMode.None ||
                ForceMagnitude <= 0f ||
                !Contains(recipientPosition)
            )
            {
                return false;
            }

            Vector3 outward = NormalizeOrFallback(
                recipientPosition - Center,
                SurfaceNormal
            );
            Vector3 direction = ForceMode == AbilityExternalForceMode.Pull
                ? -outward
                : outward;
            receiver.ApplyExternalForce(
                ToCombat(direction * ForceMagnitude),
                Source
            );
            return true;
        }

        private static CombatVector3 ToCombat(Vector3 value)
        {
            return new CombatVector3(value.x, value.y, value.z);
        }

        private static Vector3 NormalizeOrFallback(
            Vector3 value,
            Vector3 fallback
        )
        {
            if (value.sqrMagnitude > DirectionEpsilonSquared)
            {
                return value.normalized;
            }

            return fallback.sqrMagnitude > DirectionEpsilonSquared
                ? fallback.normalized
                : Vector3.up;
        }

        private static void RequireFinite(Vector3 value, string parameterName)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Vector components must be finite."
                );
            }
        }

        private static void RequireFinitePositive(
            float value,
            string parameterName
        )
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void RequireFiniteNonNegative(
            float value,
            string parameterName
        )
        {
            if (!IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void RequireFiniteRange(
            float value,
            float minimum,
            float maximum,
            string parameterName
        )
        {
            if (!IsFinite(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        internal static bool IsFinite(Vector3 value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static CombatVector3 ToCombatVector(Vector3 value)
        {
            RequireFinite(value, nameof(value));
            return ToCombat(value);
        }
    }
}
