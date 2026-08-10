using System;
using System.Collections.Generic;
using NUnit.Framework;
using Powersuit.Combat;
using UnityEditor;

namespace Powersuit.Tests.EditMode
{
    public sealed class WeaponAimStateTests
    {
        private const string PrecisionRifleAssetPath =
            "Assets/Game/Content/Weapons/PrecisionRifle.asset";

        [Test]
        public void PrecisionRifle_ExposesValidScopeProfile()
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(
                PrecisionRifleAssetPath
            );
            Assert.That(asset, Is.Not.Null);

            object result = asset.GetType().GetMethod("CreateAimProfile")?.Invoke(
                asset,
                null
            );
            WeaponAimProfile profile = result as WeaponAimProfile;

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.GetValidationErrors(), Is.Empty);
            Assert.That(profile.SupportsScope, Is.True);
            Assert.That(profile.ShoulderFieldOfViewDegrees, Is.EqualTo(62f));
            Assert.That(profile.ScopedFieldOfViewDegrees, Is.EqualTo(28f));
            Assert.That(profile.ShoulderLookSensitivityMultiplier, Is.EqualTo(0.75f));
            Assert.That(profile.ScopedLookSensitivityMultiplier, Is.EqualTo(0.35f));
            Assert.That(profile.TransitionSharpness, Is.EqualTo(12f));
        }

        [Test]
        public void ProfileValidation_RejectsNonFiniteAndInvertedScopeTuning()
        {
            WeaponAimProfile invalid = new WeaponAimProfile(
                supportsScope: true,
                shoulderFieldOfViewDegrees: 30f,
                scopedFieldOfViewDegrees: 45f,
                shoulderLookSensitivityMultiplier: 0.5f,
                scopedLookSensitivityMultiplier: 0.75f,
                transitionSharpness: float.NaN
            );

            IReadOnlyList<string> errors = invalid.GetValidationErrors();

            Assert.That(errors.Count, Is.EqualTo(3));
            Assert.That(
                errors,
                Does.Contain(
                    "Scoped field of view must be narrower than shoulder field of view."
                )
            );
            Assert.Throws<ArgumentException>(() => invalid.ValidateOrThrow());
        }

        [Test]
        public void HoldPolicy_TransitionsImmediatelyBetweenAllLogicalModes()
        {
            WeaponAimState state = CreateState(ScopeActivationPolicy.Hold);

            Assert.That(state.Mode, Is.EqualTo(WeaponAimMode.Exploration));
            Assert.That(
                state.Evaluate(Input(aimHeld: true)),
                Is.EqualTo(WeaponAimMode.ShoulderAim)
            );
            Assert.That(state.IsAiming, Is.True);
            Assert.That(state.AimBlend, Is.Zero, "Logical aiming must not wait for blend.");

            Assert.That(
                state.Evaluate(Input(aimHeld: true, scopeHeld: true)),
                Is.EqualTo(WeaponAimMode.ScopedAds)
            );
            Assert.That(state.IsScoped, Is.True);

            Assert.That(
                state.Evaluate(Input(aimHeld: true, scopeHeld: false)),
                Is.EqualTo(WeaponAimMode.ShoulderAim)
            );
            Assert.That(
                state.Evaluate(Input(aimHeld: false, scopeHeld: true)),
                Is.EqualTo(WeaponAimMode.Exploration)
            );
        }

        [Test]
        public void TogglePolicy_UsesPressEdgesAndAimReleaseClearsLatch()
        {
            WeaponAimState state = CreateState(ScopeActivationPolicy.Toggle);

            state.Evaluate(Input(aimHeld: true, scopePressed: true));
            Assert.That(state.Mode, Is.EqualTo(WeaponAimMode.ScopedAds));

            state.Evaluate(Input(aimHeld: true));
            Assert.That(state.Mode, Is.EqualTo(WeaponAimMode.ScopedAds));

            state.Evaluate(Input(aimHeld: true, scopePressed: true));
            Assert.That(state.Mode, Is.EqualTo(WeaponAimMode.ShoulderAim));

            state.Evaluate(Input(aimHeld: true, scopePressed: true));
            state.Evaluate(Input(aimHeld: false));
            state.Evaluate(Input(aimHeld: true));
            Assert.That(state.Mode, Is.EqualTo(WeaponAimMode.ShoulderAim));
        }

        [Test]
        public void ReloadAndDeath_CancelScopeWithoutLeavingContradictoryMode()
        {
            WeaponAimState state = CreateState(ScopeActivationPolicy.Toggle);
            state.Evaluate(Input(aimHeld: true, scopePressed: true));

            Assert.That(
                state.Evaluate(Input(aimHeld: true, isReloading: true)),
                Is.EqualTo(WeaponAimMode.ShoulderAim)
            );
            Assert.That(
                state.Evaluate(Input(aimHeld: true)),
                Is.EqualTo(WeaponAimMode.ShoulderAim),
                "Reload must clear a toggled scope request."
            );

            state.Evaluate(Input(aimHeld: true, scopePressed: true));
            Assert.That(
                state.Evaluate(Input(aimHeld: true, isAlive: false)),
                Is.EqualTo(WeaponAimMode.Exploration)
            );
            Assert.That(state.IsAiming, Is.False);
        }

        [Test]
        public void UnsupportedScope_AlwaysResolvesToShoulderAim()
        {
            WeaponAimProfile profile = CreateProfile(supportsScope: false);
            WeaponAimState state = new WeaponAimState(
                profile,
                ScopeActivationPolicy.Toggle
            );

            Assert.That(
                state.Evaluate(
                    Input(aimHeld: true, scopeHeld: true, scopePressed: true)
                ),
                Is.EqualTo(WeaponAimMode.ShoulderAim)
            );
            Assert.That(state.IsScoped, Is.False);
            Assert.That(
                profile.GetFieldOfView(WeaponAimMode.ScopedAds, 72f),
                Is.EqualTo(profile.ShoulderFieldOfViewDegrees)
            );
        }

        [Test]
        public void PresentationBlend_IsFrameRateIndependentAndDoesNotGateMode()
        {
            WeaponAimState at30 = CreateState(ScopeActivationPolicy.Hold);
            WeaponAimState at120 = CreateState(ScopeActivationPolicy.Hold);
            WeaponAimInput scopeInput = Input(aimHeld: true, scopeHeld: true);

            at30.Evaluate(scopeInput);
            at120.Evaluate(scopeInput);

            Advance(at30, 30, 1f);
            Advance(at120, 120, 1f);

            Assert.That(at30.Mode, Is.EqualTo(WeaponAimMode.ScopedAds));
            Assert.That(at120.Mode, Is.EqualTo(WeaponAimMode.ScopedAds));
            Assert.That(at30.AimBlend, Is.EqualTo(at120.AimBlend).Within(0.00001f));
            Assert.That(
                at30.ScopeBlend,
                Is.EqualTo(at120.ScopeBlend).Within(0.00001f)
            );
            Assert.That(at30.ScopeBlend, Is.GreaterThan(0.99f).And.LessThan(1f));
        }

        [Test]
        public void PresentationBlend_RejectsInvalidTimeAndMapsProfileValues()
        {
            WeaponAimProfile profile = CreateProfile();
            WeaponAimState state = new WeaponAimState(profile);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.AdvancePresentation(-0.01f)
            );
            Assert.That(
                profile.GetFieldOfView(WeaponAimMode.Exploration, 72f),
                Is.EqualTo(72f)
            );
            Assert.That(
                profile.GetFieldOfView(WeaponAimMode.ShoulderAim, 72f),
                Is.EqualTo(62f)
            );
            Assert.That(
                profile.GetFieldOfView(WeaponAimMode.ScopedAds, 72f),
                Is.EqualTo(28f)
            );
            Assert.That(
                profile.GetLookSensitivityMultiplier(WeaponAimMode.Exploration),
                Is.EqualTo(1f)
            );
            Assert.That(
                profile.GetLookSensitivityMultiplier(WeaponAimMode.ScopedAds),
                Is.EqualTo(0.35f)
            );
        }

        private static WeaponAimState CreateState(ScopeActivationPolicy policy)
        {
            return new WeaponAimState(CreateProfile(), policy);
        }

        private static WeaponAimProfile CreateProfile(bool supportsScope = true)
        {
            return new WeaponAimProfile(
                supportsScope,
                shoulderFieldOfViewDegrees: 62f,
                scopedFieldOfViewDegrees: 28f,
                shoulderLookSensitivityMultiplier: 0.75f,
                scopedLookSensitivityMultiplier: 0.35f,
                transitionSharpness: 12f
            );
        }

        private static WeaponAimInput Input(
            bool aimHeld,
            bool scopeHeld = false,
            bool scopePressed = false,
            bool isReloading = false,
            bool isAlive = true
        )
        {
            return new WeaponAimInput(
                aimHeld,
                scopeHeld,
                scopePressed,
                isReloading,
                isAlive
            );
        }

        private static void Advance(
            WeaponAimState state,
            int framesPerSecond,
            float durationSeconds
        )
        {
            float deltaSeconds = 1f / framesPerSecond;
            int frameCount = (int)(framesPerSecond * durationSeconds);
            for (int index = 0; index < frameCount; index++)
            {
                state.AdvancePresentation(deltaSeconds);
            }
        }
    }
}
