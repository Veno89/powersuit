using System;

namespace Powersuit.Combat
{
    /// <summary>
    /// Engine-independent cooldown state. Invalid time values are rejected and
    /// externally supplied remaining time is clamped to the configured range.
    /// </summary>
    public sealed class AbilityCooldownState
    {
        private const float TimeEpsilon = 0.00001f;
        private float remainingSeconds;

        public AbilityCooldownState(
            float durationSeconds,
            float initialRemainingSeconds = 0f
        )
        {
            RequireFiniteNonNegative(durationSeconds, nameof(durationSeconds));
            RequireFiniteNonNegative(
                initialRemainingSeconds,
                nameof(initialRemainingSeconds)
            );

            DurationSeconds = durationSeconds;
            remainingSeconds = Clamp(initialRemainingSeconds, 0f, durationSeconds);
        }

        public float DurationSeconds { get; }
        public float RemainingSeconds => remainingSeconds;
        public bool IsReady => remainingSeconds <= TimeEpsilon;
        public float NormalizedRemaining =>
            DurationSeconds <= TimeEpsilon
                ? 0f
                : Clamp(remainingSeconds / DurationSeconds, 0f, 1f);

        public bool TryConsume()
        {
            if (!IsReady)
            {
                return false;
            }

            remainingSeconds = DurationSeconds;
            return true;
        }

        public void Advance(float deltaSeconds)
        {
            RequireFiniteNonNegative(deltaSeconds, nameof(deltaSeconds));
            remainingSeconds = Math.Max(0f, remainingSeconds - deltaSeconds);
        }

        public void Reset()
        {
            remainingSeconds = 0f;
        }

        public void Reset(float remaining)
        {
            RequireFiniteNonNegative(remaining, nameof(remaining));
            remainingSeconds = Clamp(remaining, 0f, DurationSeconds);
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
                    "Cooldown time must be a finite non-negative value."
                );
            }
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
