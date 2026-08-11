using System;

namespace Powersuit.Combat
{
    public readonly struct WeaponChargeReleaseResult
    {
        public WeaponChargeReleaseResult(
            bool shouldFire,
            float normalizedCharge,
            float damageMultiplier,
            float radiusMultiplier
        )
        {
            ShouldFire = shouldFire;
            NormalizedCharge = normalizedCharge;
            DamageMultiplier = damageMultiplier;
            RadiusMultiplier = radiusMultiplier;
        }

        public bool ShouldFire { get; }
        public float NormalizedCharge { get; }
        public float DamageMultiplier { get; }
        public float RadiusMultiplier { get; }
    }

    /// <summary>
    /// Engine-independent hold/release state for charge weapons. A short tap
    /// does not consume ammunition; a valid release returns deterministic
    /// damage and blast-radius multipliers for the accepted shot transaction.
    /// </summary>
    public sealed class WeaponChargeState
    {
        private readonly float durationSeconds;
        private readonly float minimumNormalized;
        private readonly float minimumDamageMultiplier;
        private readonly float maximumDamageMultiplier;
        private readonly float minimumRadiusMultiplier;
        private readonly float maximumRadiusMultiplier;
        private float elapsedSeconds;

        public WeaponChargeState(
            float durationSeconds,
            float minimumNormalized,
            float minimumDamageMultiplier,
            float maximumDamageMultiplier,
            float minimumRadiusMultiplier,
            float maximumRadiusMultiplier
        )
        {
            RequirePositiveFinite(durationSeconds, nameof(durationSeconds));
            RequireRange(minimumNormalized, 0f, 1f, nameof(minimumNormalized));
            RequirePositiveFinite(
                minimumDamageMultiplier,
                nameof(minimumDamageMultiplier)
            );
            RequirePositiveFinite(
                maximumDamageMultiplier,
                nameof(maximumDamageMultiplier)
            );
            RequirePositiveFinite(
                minimumRadiusMultiplier,
                nameof(minimumRadiusMultiplier)
            );
            RequirePositiveFinite(
                maximumRadiusMultiplier,
                nameof(maximumRadiusMultiplier)
            );
            if (maximumDamageMultiplier < minimumDamageMultiplier)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDamageMultiplier)
                );
            }
            if (maximumRadiusMultiplier < minimumRadiusMultiplier)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRadiusMultiplier)
                );
            }

            this.durationSeconds = durationSeconds;
            this.minimumNormalized = minimumNormalized;
            this.minimumDamageMultiplier = minimumDamageMultiplier;
            this.maximumDamageMultiplier = maximumDamageMultiplier;
            this.minimumRadiusMultiplier = minimumRadiusMultiplier;
            this.maximumRadiusMultiplier = maximumRadiusMultiplier;
        }

        public bool IsCharging { get; private set; }
        public float NormalizedCharge => Math.Min(
            1f,
            elapsedSeconds / durationSeconds
        );
        public bool CanReleaseShot =>
            IsCharging && NormalizedCharge >= minimumNormalized;

        public bool Begin()
        {
            if (IsCharging)
            {
                return false;
            }

            elapsedSeconds = 0f;
            IsCharging = true;
            return true;
        }

        public void Advance(float deltaSeconds)
        {
            RequireNonNegativeFinite(deltaSeconds, nameof(deltaSeconds));
            if (!IsCharging)
            {
                return;
            }

            elapsedSeconds = Math.Min(
                durationSeconds,
                elapsedSeconds + deltaSeconds
            );
        }

        public WeaponChargeReleaseResult Release()
        {
            float normalized = NormalizedCharge;
            bool shouldFire = IsCharging && normalized >= minimumNormalized;
            float damageMultiplier = Lerp(
                minimumDamageMultiplier,
                maximumDamageMultiplier,
                normalized
            );
            float radiusMultiplier = Lerp(
                minimumRadiusMultiplier,
                maximumRadiusMultiplier,
                normalized
            );
            Reset();
            return new WeaponChargeReleaseResult(
                shouldFire,
                normalized,
                damageMultiplier,
                radiusMultiplier
            );
        }

        public bool Cancel()
        {
            bool wasCharging = IsCharging;
            Reset();
            return wasCharging;
        }

        public void Reset()
        {
            elapsedSeconds = 0f;
            IsCharging = false;
        }

        private static float Lerp(float from, float to, float normalized)
        {
            float t = Math.Max(0f, Math.Min(1f, normalized));
            return from + (to - from) * t;
        }

        private static void RequirePositiveFinite(float value, string name)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireNonNegativeFinite(float value, string name)
        {
            if (!IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireRange(
            float value,
            float minimum,
            float maximum,
            string name
        )
        {
            if (!IsFinite(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
