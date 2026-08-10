using System;
using System.Collections.Generic;

namespace Powersuit.Combat
{
    /// <summary>
    /// Immutable, engine-independent camera and look tuning for one weapon.
    /// Camera adapters can consume this independently from ammunition state.
    /// </summary>
    public sealed class WeaponAimProfile
    {
        public WeaponAimProfile(
            bool supportsScope,
            float shoulderFieldOfViewDegrees,
            float scopedFieldOfViewDegrees,
            float shoulderLookSensitivityMultiplier,
            float scopedLookSensitivityMultiplier,
            float transitionSharpness
        )
        {
            SupportsScope = supportsScope;
            ShoulderFieldOfViewDegrees = shoulderFieldOfViewDegrees;
            ScopedFieldOfViewDegrees = scopedFieldOfViewDegrees;
            ShoulderLookSensitivityMultiplier = shoulderLookSensitivityMultiplier;
            ScopedLookSensitivityMultiplier = scopedLookSensitivityMultiplier;
            TransitionSharpness = transitionSharpness;
        }

        public bool SupportsScope { get; }
        public float ShoulderFieldOfViewDegrees { get; }
        public float ScopedFieldOfViewDegrees { get; }
        public float ShoulderLookSensitivityMultiplier { get; }
        public float ScopedLookSensitivityMultiplier { get; }
        public float TransitionSharpness { get; }

        public IReadOnlyList<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            RequireFieldOfView(
                ShoulderFieldOfViewDegrees,
                "Shoulder field of view",
                errors
            );
            RequireFieldOfView(
                ScopedFieldOfViewDegrees,
                "Scoped field of view",
                errors
            );
            RequireSensitivityMultiplier(
                ShoulderLookSensitivityMultiplier,
                "Shoulder look sensitivity multiplier",
                errors
            );
            RequireSensitivityMultiplier(
                ScopedLookSensitivityMultiplier,
                "Scoped look sensitivity multiplier",
                errors
            );
            RequirePositiveFinite(TransitionSharpness, "Transition sharpness", errors);

            if (
                SupportsScope &&
                IsFinite(ShoulderFieldOfViewDegrees) &&
                IsFinite(ScopedFieldOfViewDegrees) &&
                ScopedFieldOfViewDegrees >= ShoulderFieldOfViewDegrees
            )
            {
                errors.Add(
                    "Scoped field of view must be narrower than shoulder field of view."
                );
            }

            if (
                SupportsScope &&
                IsFinite(ShoulderLookSensitivityMultiplier) &&
                IsFinite(ScopedLookSensitivityMultiplier) &&
                ScopedLookSensitivityMultiplier > ShoulderLookSensitivityMultiplier
            )
            {
                errors.Add(
                    "Scoped look sensitivity cannot exceed shoulder look sensitivity."
                );
            }

            return errors.ToArray();
        }

        public void ValidateOrThrow()
        {
            IReadOnlyList<string> errors = GetValidationErrors();
            if (errors.Count > 0)
            {
                throw new ArgumentException(
                    "Invalid weapon aim profile: " + string.Join(" ", errors)
                );
            }
        }

        public float GetFieldOfView(
            WeaponAimMode mode,
            float explorationFieldOfViewDegrees
        )
        {
            RequireFieldOfViewOrThrow(
                explorationFieldOfViewDegrees,
                nameof(explorationFieldOfViewDegrees)
            );

            switch (mode)
            {
                case WeaponAimMode.Exploration:
                    return explorationFieldOfViewDegrees;
                case WeaponAimMode.ShoulderAim:
                    return ShoulderFieldOfViewDegrees;
                case WeaponAimMode.ScopedAds:
                    return SupportsScope
                        ? ScopedFieldOfViewDegrees
                        : ShoulderFieldOfViewDegrees;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public float GetLookSensitivityMultiplier(WeaponAimMode mode)
        {
            switch (mode)
            {
                case WeaponAimMode.Exploration:
                    return 1f;
                case WeaponAimMode.ShoulderAim:
                    return ShoulderLookSensitivityMultiplier;
                case WeaponAimMode.ScopedAds:
                    return SupportsScope
                        ? ScopedLookSensitivityMultiplier
                        : ShoulderLookSensitivityMultiplier;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static void RequireFieldOfView(
            float value,
            string fieldName,
            ICollection<string> errors
        )
        {
            if (!IsFinite(value) || value <= 1f || value >= 179f)
            {
                errors.Add(
                    $"{fieldName} must be a finite value between 1 and 179 degrees."
                );
            }
        }

        private static void RequireSensitivityMultiplier(
            float value,
            string fieldName,
            ICollection<string> errors
        )
        {
            if (!IsFinite(value) || value <= 0f || value > 1f)
            {
                errors.Add(
                    $"{fieldName} must be a finite value greater than zero and no greater than one."
                );
            }
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

        private static void RequireFieldOfViewOrThrow(
            float value,
            string parameterName
        )
        {
            if (!IsFinite(value) || value <= 1f || value >= 179f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Field of view must be a finite value between 1 and 179 degrees."
                );
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
