using System;

namespace Powersuit.Combat
{
    /// <summary>
    /// Engine-independent bounded resource used by the future ultimate ability.
    /// </summary>
    public sealed class UltimateMeterState
    {
        private const float ValueEpsilon = 0.00001f;

        public UltimateMeterState(float capacity, float initialValue = 0f)
        {
            RequireFinitePositive(capacity, nameof(capacity));
            RequireFiniteNonNegative(initialValue, nameof(initialValue));

            Capacity = capacity;
            CurrentValue = Clamp(initialValue, 0f, capacity);
        }

        public float Capacity { get; }
        public float CurrentValue { get; private set; }
        public bool IsEmpty => CurrentValue <= ValueEpsilon;
        public bool IsFull => CurrentValue + ValueEpsilon >= Capacity;
        public float NormalizedValue => Clamp(CurrentValue / Capacity, 0f, 1f);

        /// <summary>
        /// Adds meter and returns the amount accepted before reaching capacity.
        /// </summary>
        public float Gain(float amount)
        {
            RequireFiniteNonNegative(amount, nameof(amount));

            float previous = CurrentValue;
            CurrentValue = Clamp(CurrentValue + amount, 0f, Capacity);
            return CurrentValue - previous;
        }

        public bool CanConsume(float amount)
        {
            RequireFiniteNonNegative(amount, nameof(amount));
            return CurrentValue + ValueEpsilon >= amount;
        }

        public bool TryConsume(float amount)
        {
            if (!CanConsume(amount))
            {
                return false;
            }

            CurrentValue = Math.Max(0f, CurrentValue - amount);
            return true;
        }

        public void Fill()
        {
            CurrentValue = Capacity;
        }

        public void Reset()
        {
            CurrentValue = 0f;
        }

        public void Reset(float value)
        {
            RequireFiniteNonNegative(value, nameof(value));
            CurrentValue = Clamp(value, 0f, Capacity);
        }

        private static void RequireFinitePositive(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Ultimate-meter capacity must be a finite value greater than zero."
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
                    "Ultimate-meter values must be finite and non-negative."
                );
            }
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
