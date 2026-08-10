using System;

namespace Powersuit.Combat
{
    public enum WeaponAimMode
    {
        Exploration = 0,
        ShoulderAim = 1,
        ScopedAds = 2
    }

    public enum ScopeActivationPolicy
    {
        Hold = 0,
        Toggle = 1
    }

    /// <summary>
    /// One input sample consumed by WeaponAimState. ScopePressed is the
    /// edge-triggered signal used by toggle mode; ScopeHeld is used by hold mode.
    /// </summary>
    public readonly struct WeaponAimInput
    {
        public WeaponAimInput(
            bool aimHeld,
            bool scopeHeld,
            bool scopePressed,
            bool isReloading,
            bool isAlive = true
        )
        {
            AimHeld = aimHeld;
            ScopeHeld = scopeHeld;
            ScopePressed = scopePressed;
            IsReloading = isReloading;
            IsAlive = isAlive;
        }

        public bool AimHeld { get; }
        public bool ScopeHeld { get; }
        public bool ScopePressed { get; }
        public bool IsReloading { get; }
        public bool IsAlive { get; }
    }

    /// <summary>
    /// Deterministic logical aim state. Logical mode changes immediately;
    /// presentation blends are intentionally separate and never gate firing.
    /// </summary>
    public sealed class WeaponAimState
    {
        private readonly WeaponAimProfile profile;
        private readonly ScopeActivationPolicy scopePolicy;
        private bool toggledScopeRequested;

        public WeaponAimState(
            WeaponAimProfile profile,
            ScopeActivationPolicy scopePolicy = ScopeActivationPolicy.Hold
        )
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            profile.ValidateOrThrow();

            if (!Enum.IsDefined(typeof(ScopeActivationPolicy), scopePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(scopePolicy));
            }

            this.scopePolicy = scopePolicy;
            Mode = WeaponAimMode.Exploration;
        }

        public WeaponAimProfile Profile => profile;
        public ScopeActivationPolicy ScopePolicy => scopePolicy;
        public WeaponAimMode Mode { get; private set; }
        public bool IsAiming => Mode != WeaponAimMode.Exploration;
        public bool IsScoped => Mode == WeaponAimMode.ScopedAds;
        public float AimBlend { get; private set; }
        public float ScopeBlend { get; private set; }

        /// <summary>
        /// Resolves the logical mode from the current input sample. The returned
        /// mode is effective immediately and does not wait for presentation blend.
        /// </summary>
        public WeaponAimMode Evaluate(WeaponAimInput input)
        {
            if (!input.IsAlive)
            {
                toggledScopeRequested = false;
                Mode = WeaponAimMode.Exploration;
                return Mode;
            }

            if (!input.AimHeld)
            {
                toggledScopeRequested = false;
                Mode = WeaponAimMode.Exploration;
                return Mode;
            }

            if (input.IsReloading)
            {
                toggledScopeRequested = false;
                Mode = WeaponAimMode.ShoulderAim;
                return Mode;
            }

            if (!profile.SupportsScope)
            {
                toggledScopeRequested = false;
                Mode = WeaponAimMode.ShoulderAim;
                return Mode;
            }

            bool scopeRequested;
            switch (scopePolicy)
            {
                case ScopeActivationPolicy.Hold:
                    scopeRequested = input.ScopeHeld;
                    break;
                case ScopeActivationPolicy.Toggle:
                    if (input.ScopePressed)
                    {
                        toggledScopeRequested = !toggledScopeRequested;
                    }

                    scopeRequested = toggledScopeRequested;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported scope activation policy '{scopePolicy}'."
                    );
            }

            Mode = scopeRequested
                ? WeaponAimMode.ScopedAds
                : WeaponAimMode.ShoulderAim;
            return Mode;
        }

        public void AdvancePresentation(float deltaSeconds)
        {
            AimBlend = WeaponAimBlendMath.Damp(
                AimBlend,
                IsAiming ? 1f : 0f,
                profile.TransitionSharpness,
                deltaSeconds
            );
            ScopeBlend = WeaponAimBlendMath.Damp(
                ScopeBlend,
                IsScoped ? 1f : 0f,
                profile.TransitionSharpness,
                deltaSeconds
            );
        }

        public void Reset()
        {
            toggledScopeRequested = false;
            Mode = WeaponAimMode.Exploration;
            AimBlend = 0f;
            ScopeBlend = 0f;
        }
    }

    public static class WeaponAimBlendMath
    {
        /// <summary>
        /// Exponential damping produces the same result for equal elapsed time,
        /// independent of how that time is split across frames.
        /// </summary>
        public static float Damp(
            float current,
            float target,
            float sharpness,
            float deltaSeconds
        )
        {
            RequireFinite(current, nameof(current));
            RequireFinite(target, nameof(target));
            RequirePositiveFinite(sharpness, nameof(sharpness));
            RequireNonNegativeFinite(deltaSeconds, nameof(deltaSeconds));

            if (deltaSeconds == 0f || current == target)
            {
                return current;
            }

            double remaining = Math.Exp(-(double)sharpness * deltaSeconds);
            return (float)(target + ((current - target) * remaining));
        }

        private static void RequireFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Value must be finite."
                );
            }
        }

        private static void RequirePositiveFinite(float value, string parameterName)
        {
            RequireFinite(value, parameterName);
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Value must be greater than zero."
                );
            }
        }

        private static void RequireNonNegativeFinite(
            float value,
            string parameterName
        )
        {
            RequireFinite(value, parameterName);
            if (value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Value cannot be negative."
                );
            }
        }
    }
}
