using Powersuit.Combat;

namespace Powersuit.Abilities
{
    public enum LightningStrikePhase
    {
        Idle = 0,
        Targeting = 1
    }

    public readonly struct LightningAreaCast
    {
        public LightningAreaCast(
            CombatVector3 point,
            CombatVector3 surfaceNormal
        )
        {
            Point = point;
            SurfaceNormal = surfaceNormal;
        }

        public CombatVector3 Point { get; }
        public CombatVector3 SurfaceNormal { get; }
    }

    public readonly struct LightningReleaseResult
    {
        private LightningReleaseResult(
            bool cast,
            AbilityUseFailure failure,
            AbilityTargetValidation validation,
            LightningAreaCast areaCast
        )
        {
            Cast = cast;
            Failure = failure;
            Validation = validation;
            AreaCast = areaCast;
        }

        public bool Cast { get; }
        public AbilityUseFailure Failure { get; }
        public AbilityTargetValidation Validation { get; }
        public LightningAreaCast AreaCast { get; }

        public static LightningReleaseResult Success(
            LightningAreaCast areaCast
        )
        {
            return new LightningReleaseResult(
                true,
                AbilityUseFailure.None,
                AbilityTargetValidation.Valid,
                areaCast
            );
        }

        public static LightningReleaseResult Rejected(
            AbilityUseFailure failure,
            AbilityTargetValidation validation
        )
        {
            return new LightningReleaseResult(
                false,
                failure,
                validation,
                default
            );
        }
    }

    /// <summary>
    /// Hold begins targeting, target probes update while held, release commits
    /// one valid area cast, and cancel/invalid release never spends cooldown.
    /// </summary>
    public sealed class LightningStrikeState
    {
        private readonly AbilityCooldownState cooldown;
        private readonly float maximumRange;
        private AbilityTargetSample target;
        private AbilityTargetValidation targetValidation;
        private bool hasTarget;

        public LightningStrikeState(float cooldownSeconds, float maximumRange)
        {
            AbilityVectorMath.RequireFinitePositive(
                maximumRange,
                nameof(maximumRange)
            );

            cooldown = new AbilityCooldownState(cooldownSeconds);
            this.maximumRange = maximumRange;
            Phase = LightningStrikePhase.Idle;
            targetValidation = AbilityTargetValidation.Invalid(
                AbilityTargetInvalidReason.NotTargeting
            );
        }

        public LightningStrikePhase Phase { get; private set; }
        public bool IsTargeting => Phase == LightningStrikePhase.Targeting;
        public bool CanBeginTargeting => !IsTargeting && cooldown.IsReady;
        public float MaximumRange => maximumRange;
        public float CooldownRemaining => cooldown.RemainingSeconds;
        public float CooldownNormalized => cooldown.NormalizedRemaining;
        public bool HasTarget => hasTarget;
        public AbilityTargetSample Target => target;
        public AbilityTargetValidation TargetValidation => targetValidation;

        public AbilityUseResult TryBeginTargeting()
        {
            if (IsTargeting)
            {
                return AbilityUseResult.Rejected(
                    AbilityUseFailure.AlreadyTargeting
                );
            }

            if (!cooldown.IsReady)
            {
                return AbilityUseResult.Rejected(AbilityUseFailure.Cooldown);
            }

            Phase = LightningStrikePhase.Targeting;
            ClearTarget(AbilityTargetInvalidReason.MissingSurface);
            return AbilityUseResult.Success;
        }

        public AbilityTargetValidation UpdateTarget(
            AbilityTargetSample sample
        )
        {
            if (!IsTargeting)
            {
                return AbilityTargetValidation.Invalid(
                    AbilityTargetInvalidReason.NotTargeting
                );
            }

            target = sample;
            hasTarget = true;
            targetValidation = AbilityTargetRules.ValidateSurfaceTarget(
                sample,
                maximumRange
            );
            return targetValidation;
        }

        public AbilityTargetValidation InvalidateTarget(
            AbilityTargetInvalidReason reason
        )
        {
            if (!IsTargeting)
            {
                return AbilityTargetValidation.Invalid(
                    AbilityTargetInvalidReason.NotTargeting
                );
            }

            if (reason == AbilityTargetInvalidReason.None)
            {
                throw new System.ArgumentOutOfRangeException(nameof(reason));
            }

            ClearTarget(reason);
            return targetValidation;
        }

        public LightningReleaseResult Release()
        {
            if (!IsTargeting)
            {
                return LightningReleaseResult.Rejected(
                    AbilityUseFailure.NotTargeting,
                    AbilityTargetValidation.Invalid(
                        AbilityTargetInvalidReason.NotTargeting
                    )
                );
            }

            AbilityTargetValidation releaseValidation = targetValidation;
            if (!hasTarget || !releaseValidation.IsValid)
            {
                EndTargeting();
                return LightningReleaseResult.Rejected(
                    AbilityUseFailure.InvalidTarget,
                    releaseValidation
                );
            }

            LightningAreaCast areaCast = new LightningAreaCast(
                target.Point,
                target.SurfaceNormal
            );
            EndTargeting();

            if (!cooldown.TryConsume())
            {
                return LightningReleaseResult.Rejected(
                    AbilityUseFailure.Cooldown,
                    releaseValidation
                );
            }

            return LightningReleaseResult.Success(areaCast);
        }

        public bool Cancel()
        {
            if (!IsTargeting)
            {
                return false;
            }

            EndTargeting();
            return true;
        }

        public void Advance(float deltaSeconds)
        {
            cooldown.Advance(deltaSeconds);
        }

        public void Reset()
        {
            cooldown.Reset();
            EndTargeting();
        }

        private void EndTargeting()
        {
            Phase = LightningStrikePhase.Idle;
            ClearTarget(AbilityTargetInvalidReason.NotTargeting);
        }

        private void ClearTarget(AbilityTargetInvalidReason reason)
        {
            target = default;
            hasTarget = false;
            targetValidation = AbilityTargetValidation.Invalid(reason);
        }
    }
}
