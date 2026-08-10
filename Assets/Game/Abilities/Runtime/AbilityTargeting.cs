using System;
using Powersuit.Combat;

namespace Powersuit.Abilities
{
    public enum AbilityUseFailure
    {
        None = 0,
        Cooldown = 1,
        AlreadyTargeting = 2,
        NotTargeting = 3,
        InvalidTarget = 4,
        MeterNotFull = 5,
        AlreadyActive = 6,
        InvalidLaunch = 7
    }

    public enum AbilityTargetInvalidReason
    {
        None = 0,
        NotTargeting = 1,
        MissingSurface = 2,
        InvalidSurfaceNormal = 3,
        Obstructed = 4,
        OutOfRange = 5,
        NonFiniteInput = 6
    }

    public readonly struct AbilityUseResult
    {
        private AbilityUseResult(bool accepted, AbilityUseFailure failure)
        {
            Accepted = accepted;
            Failure = failure;
        }

        public bool Accepted { get; }
        public AbilityUseFailure Failure { get; }

        public static AbilityUseResult Success =>
            new AbilityUseResult(true, AbilityUseFailure.None);

        public static AbilityUseResult Rejected(AbilityUseFailure failure)
        {
            if (failure == AbilityUseFailure.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(failure),
                    "A rejected ability use requires a failure reason."
                );
            }

            return new AbilityUseResult(false, failure);
        }
    }

    /// <summary>
    /// Engine-independent result of a camera/world target probe. The caller
    /// owns physics queries; the ability owns consistent validity rules.
    /// </summary>
    public readonly struct AbilityTargetSample
    {
        public AbilityTargetSample(
            CombatVector3 origin,
            CombatVector3 point,
            CombatVector3 surfaceNormal,
            bool hasSurface,
            bool isObstructed
        )
        {
            Origin = origin;
            Point = point;
            SurfaceNormal = surfaceNormal;
            HasSurface = hasSurface;
            IsObstructed = isObstructed;
        }

        public CombatVector3 Origin { get; }
        public CombatVector3 Point { get; }
        public CombatVector3 SurfaceNormal { get; }
        public bool HasSurface { get; }
        public bool IsObstructed { get; }
    }

    public readonly struct AbilityTargetValidation
    {
        private AbilityTargetValidation(
            bool isValid,
            AbilityTargetInvalidReason reason
        )
        {
            IsValid = isValid;
            Reason = reason;
        }

        public bool IsValid { get; }
        public AbilityTargetInvalidReason Reason { get; }

        public static AbilityTargetValidation Valid =>
            new AbilityTargetValidation(true, AbilityTargetInvalidReason.None);

        public static AbilityTargetValidation Invalid(
            AbilityTargetInvalidReason reason
        )
        {
            if (reason == AbilityTargetInvalidReason.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reason),
                    "An invalid target requires a reason."
                );
            }

            return new AbilityTargetValidation(false, reason);
        }
    }

    public static class AbilityTargetRules
    {
        private const float NormalEpsilonSquared = 0.000001f;
        private const float RangeEpsilon = 0.0001f;

        public static AbilityTargetValidation ValidateSurfaceTarget(
            AbilityTargetSample sample,
            float maximumRange
        )
        {
            RequireFinitePositive(maximumRange, nameof(maximumRange));

            if (!sample.HasSurface)
            {
                return AbilityTargetValidation.Invalid(
                    AbilityTargetInvalidReason.MissingSurface
                );
            }

            if (sample.SurfaceNormal.SqrMagnitude <= NormalEpsilonSquared)
            {
                return AbilityTargetValidation.Invalid(
                    AbilityTargetInvalidReason.InvalidSurfaceNormal
                );
            }

            if (sample.IsObstructed)
            {
                return AbilityTargetValidation.Invalid(
                    AbilityTargetInvalidReason.Obstructed
                );
            }

            double deltaX = (double)sample.Point.X - sample.Origin.X;
            double deltaY = (double)sample.Point.Y - sample.Origin.Y;
            double deltaZ = (double)sample.Point.Z - sample.Origin.Z;
            double squaredDistance =
                deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
            double allowedRange = (double)maximumRange + RangeEpsilon;

            return squaredDistance > allowedRange * allowedRange
                ? AbilityTargetValidation.Invalid(
                    AbilityTargetInvalidReason.OutOfRange
                )
                : AbilityTargetValidation.Valid;
        }

        private static void RequireFinitePositive(
            float value,
            string parameterName
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Ability range must be finite and greater than zero."
                );
            }
        }
    }

    internal static class AbilityVectorMath
    {
        private const float DirectionEpsilonSquared = 0.000001f;

        public static bool TryDirection(
            CombatVector3 from,
            CombatVector3 to,
            out CombatVector3 direction,
            out float distance
        )
        {
            double x = (double)to.X - from.X;
            double y = (double)to.Y - from.Y;
            double z = (double)to.Z - from.Z;
            double squaredDistance = x * x + y * y + z * z;
            if (squaredDistance <= DirectionEpsilonSquared)
            {
                direction = CombatVector3.Zero;
                distance = 0f;
                return false;
            }

            double preciseDistance = Math.Sqrt(squaredDistance);
            if (preciseDistance > float.MaxValue)
            {
                direction = CombatVector3.Zero;
                distance = 0f;
                return false;
            }

            distance = (float)preciseDistance;
            direction = new CombatVector3(
                (float)(x / preciseDistance),
                (float)(y / preciseDistance),
                (float)(z / preciseDistance)
            );
            return true;
        }

        public static void RequireFiniteNonNegative(
            float value,
            string parameterName
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Ability time/value must be finite and non-negative."
                );
            }
        }

        public static void RequireFinitePositive(
            float value,
            string parameterName
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Ability time/value must be finite and greater than zero."
                );
            }
        }
    }
}
