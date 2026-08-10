using System;
using NUnit.Framework;
using Powersuit.Combat;

namespace Powersuit.Abilities.Tests
{
    public sealed class AbilityStateTests
    {
        [Test]
        public void SurfaceTargetRules_RejectMissingObstructedAndOutOfRange()
        {
            AbilityTargetSample valid = Target(
                point: new CombatVector3(0f, 0f, 10f)
            );
            Assert.That(
                AbilityTargetRules.ValidateSurfaceTarget(valid, 10f).IsValid,
                Is.True
            );

            AbilityTargetValidation missing =
                AbilityTargetRules.ValidateSurfaceTarget(
                    Target(
                        point: new CombatVector3(0f, 0f, 1f),
                        hasSurface: false
                    ),
                    10f
                );
            AbilityTargetValidation obstructed =
                AbilityTargetRules.ValidateSurfaceTarget(
                    Target(
                        point: new CombatVector3(0f, 0f, 1f),
                        isObstructed: true
                    ),
                    10f
                );
            AbilityTargetValidation distant =
                AbilityTargetRules.ValidateSurfaceTarget(
                    Target(point: new CombatVector3(0f, 0f, 10.1f)),
                    10f
                );

            Assert.That(
                missing.Reason,
                Is.EqualTo(AbilityTargetInvalidReason.MissingSurface)
            );
            Assert.That(
                obstructed.Reason,
                Is.EqualTo(AbilityTargetInvalidReason.Obstructed)
            );
            Assert.That(
                distant.Reason,
                Is.EqualTo(AbilityTargetInvalidReason.OutOfRange)
            );
        }

        [Test]
        public void ShoulderRocket_ValidLaunchConsumesCooldownAndNormalizesDirection()
        {
            ShoulderRocketState state = new ShoulderRocketState(4f);

            ShoulderRocketLaunchResult invalid = state.TryLaunch(
                CombatVector3.Zero,
                CombatVector3.Zero
            );
            ShoulderRocketLaunchResult launch = state.TryLaunch(
                CombatVector3.Zero,
                new CombatVector3(0f, 0f, 20f)
            );
            ShoulderRocketLaunchResult blocked = state.TryLaunch(
                CombatVector3.Zero,
                new CombatVector3(0f, 0f, 20f)
            );

            Assert.That(invalid.Accepted, Is.False);
            Assert.That(
                invalid.Failure,
                Is.EqualTo(AbilityUseFailure.InvalidLaunch)
            );
            Assert.That(launch.Accepted, Is.True);
            Assert.That(launch.Launch.Direction.Z, Is.EqualTo(1f));
            Assert.That(launch.Launch.Distance, Is.EqualTo(20f));
            Assert.That(blocked.Failure, Is.EqualTo(AbilityUseFailure.Cooldown));
            Assert.That(state.CooldownRemaining, Is.EqualTo(4f));

            state.Advance(4f);
            Assert.That(state.CanLaunch, Is.True);
        }

        [Test]
        public void Lightning_HoldReleaseCancelAndValidityDoNotWasteCooldown()
        {
            LightningStrikeState state = new LightningStrikeState(5f, 20f);

            Assert.That(state.TryBeginTargeting().Accepted, Is.True);
            AbilityTargetValidation invalid = state.UpdateTarget(
                Target(
                    point: new CombatVector3(0f, 0f, 5f),
                    isObstructed: true
                )
            );
            LightningReleaseResult invalidRelease = state.Release();

            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalidRelease.Cast, Is.False);
            Assert.That(state.CooldownRemaining, Is.Zero);
            Assert.That(state.IsTargeting, Is.False);

            Assert.That(state.TryBeginTargeting().Accepted, Is.True);
            Assert.That(
                state.UpdateTarget(
                    Target(point: new CombatVector3(0f, 0f, 8f))
                ).IsValid,
                Is.True
            );
            LightningReleaseResult cast = state.Release();

            Assert.That(cast.Cast, Is.True);
            Assert.That(cast.AreaCast.Point.Z, Is.EqualTo(8f));
            Assert.That(state.CooldownRemaining, Is.EqualTo(5f));
            Assert.That(
                state.TryBeginTargeting().Failure,
                Is.EqualTo(AbilityUseFailure.Cooldown)
            );

            state.Advance(5f);
            Assert.That(state.TryBeginTargeting().Accepted, Is.True);
            Assert.That(state.Cancel(), Is.True);
            Assert.That(state.CooldownRemaining, Is.Zero);
        }

        [Test]
        public void Lightning_NonTargetingUpdatesAndInvalidationAreSafe()
        {
            LightningStrikeState state = new LightningStrikeState(1f, 10f);

            Assert.That(
                state.UpdateTarget(Target()).Reason,
                Is.EqualTo(AbilityTargetInvalidReason.NotTargeting)
            );
            Assert.That(state.TryBeginTargeting().Accepted, Is.True);
            Assert.That(
                state.InvalidateTarget(
                    AbilityTargetInvalidReason.NonFiniteInput
                ).Reason,
                Is.EqualTo(AbilityTargetInvalidReason.NonFiniteInput)
            );
            Assert.That(state.Release().Cast, Is.False);
            Assert.That(state.CooldownRemaining, Is.Zero);
        }

        [Test]
        public void VoidUltimate_RequiresFullMeterAndValidPlacement()
        {
            VoidUltimateState state = new VoidUltimateState(
                meterCapacity: 100f,
                maximumRange: 30f,
                activeDuration: 3f,
                tickInterval: 1f
            );

            Assert.That(state.GainMeter(40f), Is.EqualTo(40f));
            Assert.That(
                state.TryActivate(Target()).Failure,
                Is.EqualTo(AbilityUseFailure.MeterNotFull)
            );

            state.FillMeter();
            VoidActivationResult invalid = state.TryActivate(
                Target(
                    point: new CombatVector3(0f, 0f, 5f),
                    hasSurface: false
                )
            );
            Assert.That(invalid.Activated, Is.False);
            Assert.That(state.IsMeterFull, Is.True);

            VoidActivationResult activated = state.TryActivate(
                Target(point: new CombatVector3(0f, 0f, 5f))
            );
            Assert.That(activated.Activated, Is.True);
            Assert.That(state.IsActive, Is.True);
            Assert.That(state.MeterValue, Is.Zero);
            Assert.That(state.GainMeter(10f), Is.Zero);
        }

        [Test]
        public void VoidUltimate_EmitsPeriodicTicksThenOneFinalBurst()
        {
            VoidUltimateState state = ActiveVoidState();

            Assert.That(state.Advance(0.5f).HasEvents, Is.False);
            Assert.That(state.Advance(0.5f).TickCount, Is.EqualTo(1));
            Assert.That(state.Advance(1f).TickCount, Is.EqualTo(1));
            VoidAdvanceResult completed = state.Advance(1f);

            Assert.That(completed.TickCount, Is.Zero);
            Assert.That(completed.FinalBurstTriggered, Is.True);
            Assert.That(completed.Point.Z, Is.EqualTo(5f));
            Assert.That(state.IsActive, Is.False);
            Assert.That(state.Advance(100f).HasEvents, Is.False);
        }

        [Test]
        public void VoidUltimate_LargeAdvanceCatchesUpTicksAndCancelSuppressesBurst()
        {
            VoidUltimateState state = ActiveVoidState();
            VoidAdvanceResult completed = state.Advance(100f);

            Assert.That(completed.TickCount, Is.EqualTo(2));
            Assert.That(completed.FinalBurstTriggered, Is.True);

            state.FillMeter();
            Assert.That(state.TryActivate(Target()).Activated, Is.True);
            Assert.That(state.Cancel(), Is.True);
            Assert.That(state.Advance(100f).HasEvents, Is.False);
        }

        [Test]
        public void AbilityStates_RejectInvalidConfigurationAndTime()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new LightningStrikeState(1f, 0f)
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new VoidUltimateState(100f, 10f, 10f, 0.001f)
            );

            ShoulderRocketState rocket = new ShoulderRocketState(1f);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => rocket.Advance(float.NaN)
            );
        }

        private static VoidUltimateState ActiveVoidState()
        {
            VoidUltimateState state = new VoidUltimateState(
                meterCapacity: 100f,
                maximumRange: 30f,
                activeDuration: 3f,
                tickInterval: 1f
            );
            state.FillMeter();
            Assert.That(state.TryActivate(Target()).Activated, Is.True);
            return state;
        }

        private static AbilityTargetSample Target(
            CombatVector3? point = null,
            bool hasSurface = true,
            bool isObstructed = false
        )
        {
            return new AbilityTargetSample(
                CombatVector3.Zero,
                point ?? new CombatVector3(0f, 0f, 5f),
                new CombatVector3(0f, 1f, 0f),
                hasSurface,
                isObstructed
            );
        }
    }
}
