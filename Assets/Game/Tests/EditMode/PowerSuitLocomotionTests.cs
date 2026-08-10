using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Tests.EditMode
{
    public sealed class PowerSuitLocomotionTests
    {
        private static Type LocomotionMathType =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("PowerSuitLocomotionMath"))
                .First(type => type != null);

        private static Type GroundContactStateType =>
            FindRuntimeType("PowerSuitGroundContactState");

        private static Type MovementSettingsType =>
            FindRuntimeType("PowerSuitMovementSettings");

        [Test]
        public void ResolveFacingDirection_BackwardInputFacesOppositeTravel()
        {
            Vector3 result = ResolveFacingDirection(
                new Vector2(0f, -1f),
                Vector3.back,
                Vector3.right,
                Vector3.forward,
                false
            );

            Assert.That(Vector3.Angle(result, Vector3.forward), Is.LessThan(0.01f));
            Assert.That(Vector3.Dot(result, Vector3.back), Is.LessThan(-0.99f));
        }

        [Test]
        public void ResolveFacingDirection_AimingUsesCameraHeadingWhileMovingBackward()
        {
            Vector3 result = ResolveFacingDirection(
                new Vector2(0f, -1f),
                Vector3.back,
                Vector3.right,
                Vector3.forward,
                true
            );

            Assert.That(Vector3.Angle(result, Vector3.forward), Is.LessThan(0.01f));
        }

        [Test]
        public void ResolveFacingDirection_ForwardDiagonalUsesMovementHeading()
        {
            Vector3 desiredDirection = new Vector3(1f, 0f, 1f).normalized;
            Vector3 result = ResolveFacingDirection(
                new Vector2(1f, 1f).normalized,
                desiredDirection,
                Vector3.forward,
                Vector3.forward,
                false
            );

            Assert.That(Vector3.Angle(result, desiredDirection), Is.LessThan(0.01f));
        }

        [Test]
        public void ToLocalMovement_ReportsSignedForwardAndLateralValues()
        {
            Vector2 backward = ToLocalMovement(
                Quaternion.identity,
                new Vector3(0f, 12f, -2.5f),
                5f
            );

            Vector2 right = ToLocalMovement(
                Quaternion.identity,
                new Vector3(2.5f, -8f, 0f),
                5f
            );

            Assert.That(backward.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(backward.y, Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(right.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(right.y, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ToLocalMovement_AccountsForCharacterRotationAndClampsMagnitude()
        {
            Vector2 result = ToLocalMovement(
                Quaternion.Euler(0f, 90f, 0f),
                Vector3.right * 10f,
                5f
            );

            Assert.That(result.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(result.y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.magnitude, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ToLocalMovement_ZeroReferenceSpeedReturnsZero()
        {
            Vector2 result = ToLocalMovement(
                Quaternion.identity,
                Vector3.forward,
                0f
            );

            Assert.That(result, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ProjectOntoGroundPlane_RemovesFlightVerticalVelocityOnly()
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "ProjectOntoGroundPlane",
                BindingFlags.Public | BindingFlags.Static
            );

            Assert.That(method, Is.Not.Null);
            Vector3 result = (Vector3)method.Invoke(
                null,
                new object[] { new Vector3(3f, -7f, 4f) }
            );

            Assert.That(result.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(result.y, Is.Zero.Within(0.0001f));
            Assert.That(result.z, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void ApproachVelocity_UsesAccelerationDecelerationAndBrakingRates()
        {
            Vector3 accelerated = ApproachVelocity(
                Vector3.zero,
                Vector3.forward * 10f,
                2f,
                4f,
                8f,
                0.5f
            );
            Vector3 decelerated = ApproachVelocity(
                Vector3.forward * 10f,
                Vector3.zero,
                2f,
                4f,
                8f,
                0.5f
            );
            Vector3 braking = ApproachVelocity(
                Vector3.forward * 10f,
                Vector3.back * 10f,
                2f,
                4f,
                8f,
                0.5f
            );

            Assert.That(accelerated.z, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(decelerated.z, Is.EqualTo(8f).Within(0.0001f));
            Assert.That(braking.z, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void ApproachVelocity_IsEquivalentAtCommonFrameRates()
        {
            Vector3 atThirtyHertz = SimulateVelocityApproach(30);
            Vector3 atSixtyHertz = SimulateVelocityApproach(60);
            Vector3 atOneTwentyHertz = SimulateVelocityApproach(120);

            Assert.That(atThirtyHertz.z, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(
                atSixtyHertz.z,
                Is.EqualTo(atThirtyHertz.z).Within(0.0001f)
            );
            Assert.That(
                atOneTwentyHertz.z,
                Is.EqualTo(atThirtyHertz.z).Within(0.0001f)
            );
        }

        [Test]
        public void ApproachVelocity_ReversalUsesRemainingFrameTimeForAcceleration()
        {
            Vector3 singleStep = ApproachVelocity(
                Vector3.forward * 6.5f,
                Vector3.back * 6.5f,
                55f,
                65f,
                105f,
                0.1f
            );

            // Braking consumes 6.5 / 105 seconds. The rest of the frame must
            // accelerate into the new direction rather than being discarded.
            float expectedReverseSpeed = 55f * (0.1f - (6.5f / 105f));
            Assert.That(
                singleStep.z,
                Is.EqualTo(-expectedReverseSpeed).Within(0.0001f)
            );
        }

        [Test]
        public void ApproachVelocity_ReversalIsEquivalentAtCommonFrameRates()
        {
            Vector3 atThirtyHertz = SimulateVelocityReversal(30, 0.2f);
            Vector3 atSixtyHertz = SimulateVelocityReversal(60, 0.2f);
            Vector3 atOneTwentyHertz = SimulateVelocityReversal(120, 0.2f);

            Assert.That(atThirtyHertz.z, Is.EqualTo(-6.5f).Within(0.0001f));
            Assert.That(
                atSixtyHertz.z,
                Is.EqualTo(atThirtyHertz.z).Within(0.0001f)
            );
            Assert.That(
                atOneTwentyHertz.z,
                Is.EqualTo(atThirtyHertz.z).Within(0.0001f)
            );
        }

        [Test]
        public void VerticalApproach_SeparatesReleaseAndReversalBraking()
        {
            float released = ApproachVelocity(
                8f,
                0f,
                4f,
                3f,
                10f,
                1f
            );
            float reversed = ApproachVelocity(
                8f,
                -8f,
                4f,
                3f,
                10f,
                0.5f
            );

            Assert.That(released, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(reversed, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void Gravity_IsFrameRateIndependentAndClampsTerminalFallSpeed()
        {
            float atThirtyHertz = SimulateGravity(30, 1f);
            float atOneTwentyHertz = SimulateGravity(120, 1f);
            float terminal = SimulateGravity(60, 10f);

            Assert.That(atThirtyHertz, Is.EqualTo(-25f).Within(0.0001f));
            Assert.That(
                atOneTwentyHertz,
                Is.EqualTo(atThirtyHertz).Within(0.0001f)
            );
            Assert.That(terminal, Is.EqualTo(-35f).Within(0.0001f));
        }

        [Test]
        public void CalculateJumpSpeed_UsesHeightAndGravity()
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "CalculateJumpSpeed",
                BindingFlags.Public | BindingFlags.Static
            );

            Assert.That(method, Is.Not.Null);
            float speed = (float)method.Invoke(
                null,
                new object[] { 1.5f, -25f }
            );
            Assert.That(
                speed,
                Is.EqualTo(Mathf.Sqrt(75f)).Within(0.0001f)
            );
        }

        [Test]
        public void GroundContact_CoyoteWindowUsesRawSupportTime()
        {
            object insideWindow = CreateGroundContactState();
            Invoke(insideWindow, "Reset", true);
            Invoke(insideWindow, "Advance", false, 0.119f);
            Invoke(insideWindow, "BufferJump");

            object outsideWindow = CreateGroundContactState();
            Invoke(outsideWindow, "Reset", true);
            Invoke(outsideWindow, "Advance", false, 0.121f);
            Invoke(outsideWindow, "BufferJump");

            Assert.That(
                Invoke(insideWindow, "TryConsumeBufferedJump"),
                Is.EqualTo(true)
            );
            Assert.That(
                Invoke(outsideWindow, "TryConsumeBufferedJump"),
                Is.EqualTo(false)
            );
        }

        [Test]
        public void GroundContact_HysteresisDoesNotExtendCoyoteWindow()
        {
            object state = CreateGroundContactState();
            Invoke(state, "Reset", true);

            Invoke(state, "Advance", false, 0.04f);
            Assert.That(GetProperty(state, "IsGrounded"), Is.EqualTo(true));
            Assert.That(
                GetProperty(state, "CoyoteRemaining"),
                Is.EqualTo(0.08f).Within(0.0001f)
            );

            Invoke(state, "Advance", false, 0.03f);
            Assert.That(GetProperty(state, "IsGrounded"), Is.EqualTo(false));
            Assert.That(
                GetProperty(state, "CoyoteRemaining"),
                Is.EqualTo(0.05f).Within(0.0001f)
            );

            Invoke(state, "Advance", false, 0.051f);
            Invoke(state, "BufferJump");
            Assert.That(
                Invoke(state, "TryConsumeBufferedJump"),
                Is.EqualTo(false)
            );
        }

        [Test]
        public void GroundContact_BufferedJumpConsumesOnceOnLanding()
        {
            object state = CreateGroundContactState();
            Invoke(state, "Reset", false);
            Invoke(state, "BufferJump");
            Invoke(state, "Advance", false, 0.05f);
            Invoke(state, "Advance", true, 0.01f);

            Assert.That(
                Invoke(state, "TryConsumeBufferedJump"),
                Is.EqualTo(true)
            );
            Assert.That(
                Invoke(state, "TryConsumeBufferedJump"),
                Is.EqualTo(false)
            );
            Assert.That(
                GetProperty(state, "WaitingForSeparation"),
                Is.EqualTo(true)
            );
        }

        [Test]
        public void GroundContact_ExpiredBufferDoesNotJumpOnLanding()
        {
            object state = CreateGroundContactState();
            Invoke(state, "Reset", false);
            Invoke(state, "BufferJump");
            Invoke(state, "Advance", false, 0.121f);
            Invoke(state, "Advance", true, 0.001f);

            Assert.That(GetProperty(state, "HasBufferedJump"), Is.False);
            Assert.That(
                Invoke(state, "TryConsumeBufferedJump"),
                Is.EqualTo(false)
            );
        }

        [Test]
        public void GroundContact_StaleSupportCannotImmediatelyDoubleJump()
        {
            object state = CreateGroundContactState();
            Invoke(state, "Reset", true);
            Invoke(state, "BufferJump");
            Assert.That(
                Invoke(state, "TryConsumeBufferedJump"),
                Is.EqualTo(true)
            );

            Invoke(state, "BufferJump");
            Invoke(state, "Advance", true, 0.01f);
            Assert.That(
                Invoke(state, "TryConsumeBufferedJump"),
                Is.EqualTo(false)
            );
            Assert.That(
                GetProperty(state, "WaitingForSeparation"),
                Is.EqualTo(true)
            );
        }

        [Test]
        public void MovementSettings_HaveSafeInlineDefaults()
        {
            object settings = Activator.CreateInstance(MovementSettingsType);

            Assert.That(
                GetProperty(settings, "GroundDeceleration"),
                Is.EqualTo(65f)
            );
            Assert.That(
                GetProperty(settings, "GroundBrakingAcceleration"),
                Is.EqualTo(105f)
            );
            Assert.That(
                GetProperty(settings, "CoyoteTimeSeconds"),
                Is.EqualTo(0.12f)
            );
            Assert.That(
                GetProperty(settings, "FlightTakeoffSpeed"),
                Is.EqualTo(5f)
            );
            Assert.That(
                GetProperty(settings, "BoostAccelerationMultiplier"),
                Is.EqualTo(1.7f)
            );
            Assert.That(
                GetProperty(settings, "FlightLandingIntentGraceSeconds"),
                Is.EqualTo(0.25f)
            );
        }

        [Test]
        public void ControllerDefaults_PrioritizeFastGroundFlightAndAimResponse()
        {
            GameObject player = new GameObject("Responsiveness Defaults Test");
            player.SetActive(false);
            try
            {
                Component controller = player.AddComponent(
                    FindRuntimeType("PowerSuitController")
                );

                Assert.That(GetPrivateField(controller, "walkSpeed"), Is.EqualTo(6.5f));
                Assert.That(GetPrivateField(controller, "groundAcceleration"), Is.EqualTo(55f));
                Assert.That(GetPrivateField(controller, "flightSpeed"), Is.EqualTo(14f));
                Assert.That(GetPrivateField(controller, "boostSpeed"), Is.EqualTo(28f));
                Assert.That(GetPrivateField(controller, "flightAcceleration"), Is.EqualTo(38f));
                Assert.That(GetPrivateField(controller, "turningSpeed"), Is.EqualTo(20f));
                Assert.That(GetPrivateField(controller, "combatTurningSpeed"), Is.EqualTo(32f));
                Assert.That(GetPrivateField(controller, "controllerLookSpeed"), Is.EqualTo(180f));
                Assert.That(GetPrivateField(controller, "cameraLookSharpness"), Is.EqualTo(45f));
                Assert.That(GetPrivateField(controller, "aimTransitionSpeed"), Is.EqualTo(22f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void SetFlightEnabled_AirborneExitPreservesPlanarAndVerticalMomentum()
        {
            GameObject player = new GameObject("Flight Transition Test");
            player.SetActive(false);
            try
            {
                Type controllerType = FindRuntimeType("PowerSuitController");
                Component controller = player.AddComponent(controllerType);
                SetPrivateField(
                    controller,
                    "horizontalVelocity",
                    new Vector3(3f, 8f, 4f)
                );
                SetPrivateField(controller, "verticalVelocity", 7f);
                SetPrivateField(controller, "isFlying", true);

                controllerType.GetMethod("SetFlightEnabled")?.Invoke(
                    controller,
                    new object[] { false }
                );

                Vector3 planarVelocity = (Vector3)GetPrivateField(
                    controller,
                    "horizontalVelocity"
                );
                Assert.That(planarVelocity, Is.EqualTo(new Vector3(3f, 0f, 4f)));
                Assert.That(
                    GetPrivateField(controller, "verticalVelocity"),
                    Is.EqualTo(7f)
                );
                Assert.That(
                    controllerType.GetProperty("IsFlying")?.GetValue(controller),
                    Is.EqualTo(false)
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void SetFlightEnabled_GroundedEntryAddsTakeoffLiftAndSeparationLock()
        {
            GameObject player = new GameObject("Grounded Flight Entry Test");
            player.SetActive(false);
            try
            {
                Type controllerType = FindRuntimeType("PowerSuitController");
                Component controller = player.AddComponent(controllerType);
                object groundState = CreateGroundContactState();
                Invoke(groundState, "Reset", true);
                SetPrivateField(controller, "groundContactState", groundState);
                SetPrivateField(controller, "verticalVelocity", -4f);

                controllerType.GetMethod("SetFlightEnabled")?.Invoke(
                    controller,
                    new object[] { true }
                );

                Assert.That(
                    controllerType.GetProperty("IsFlying")?.GetValue(controller),
                    Is.EqualTo(true)
                );
                Assert.That(
                    controllerType.GetProperty("VerticalSpeed")?.GetValue(controller),
                    Is.EqualTo(5f)
                );
                Assert.That(
                    GetProperty(groundState, "WaitingForSeparation"),
                    Is.EqualTo(true)
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void SetFlightEnabled_RedundantTransitionDoesNotRewriteMomentum()
        {
            GameObject player = new GameObject("Flight Idempotence Test");
            player.SetActive(false);
            try
            {
                Type controllerType = FindRuntimeType("PowerSuitController");
                Component controller = player.AddComponent(controllerType);
                Vector3 injectedVelocity = new Vector3(3f, 8f, 4f);
                SetPrivateField(controller, "horizontalVelocity", injectedVelocity);
                SetPrivateField(controller, "verticalVelocity", -6f);
                SetPrivateField(controller, "isFlying", true);

                controllerType.GetMethod("SetFlightEnabled")?.Invoke(
                    controller,
                    new object[] { true }
                );

                Assert.That(
                    GetPrivateField(controller, "horizontalVelocity"),
                    Is.EqualTo(injectedVelocity)
                );
                Assert.That(
                    GetPrivateField(controller, "verticalVelocity"),
                    Is.EqualTo(-6f)
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void LandingContact_RaisesOnePreCollisionImpactForNewSupport()
        {
            GameObject player = new GameObject("Landing Contact Test");
            player.SetActive(false);
            try
            {
                Type controllerType = FindRuntimeType("PowerSuitController");
                Component controller = player.AddComponent(controllerType);
                float observedImpact = -1f;
                int eventCount = 0;
                Action<float> handler = impact =>
                {
                    observedImpact = impact;
                    eventCount++;
                };
                EventInfo landedEvent = controllerType.GetEvent("Landed");
                Assert.That(landedEvent, Is.Not.Null);
                landedEvent.AddEventHandler(controller, handler);

                MethodInfo raise = controllerType.GetMethod(
                    "RaiseLandingContactIfNeeded",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.That(raise, Is.Not.Null);
                raise.Invoke(
                    controller,
                    new object[] { CollisionFlags.Below, false, -9f }
                );
                raise.Invoke(
                    controller,
                    new object[] { CollisionFlags.Below, true, -9f }
                );
                raise.Invoke(
                    controller,
                    new object[] { CollisionFlags.Below, false, 2f }
                );

                Assert.That(eventCount, Is.EqualTo(1));
                Assert.That(observedImpact, Is.EqualTo(9f));
                landedEvent.RemoveEventHandler(controller, handler);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [TestCase(0f, 1f)]
        [TestCase(0.5f, 1.5f)]
        [TestCase(1f, 2f)]
        [TestCase(2f, 2f)]
        public void CalculateLocomotionPlaybackSpeed_MatchesMovementCadence(
            float normalizedSpeed,
            float expectedPlayback
        )
        {
            Assert.That(
                CalculateLocomotionPlaybackSpeed(normalizedSpeed, 2f),
                Is.EqualTo(expectedPlayback).Within(0.0001f)
            );
        }

        private static Vector3 ResolveFacingDirection(
            Vector2 movementInput,
            Vector3 desiredMovementDirection,
            Vector3 currentForward,
            Vector3 cameraForward,
            bool isAiming
        )
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "ResolveFacingDirection",
                BindingFlags.Public | BindingFlags.Static
            );

            Assert.That(method, Is.Not.Null);
            return (Vector3)method.Invoke(
                null,
                new object[]
                {
                    movementInput,
                    desiredMovementDirection,
                    currentForward,
                    cameraForward,
                    isAiming
                }
            );
        }

        private static Vector2 ToLocalMovement(
            Quaternion characterRotation,
            Vector3 worldVelocity,
            float referenceSpeed
        )
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "ToLocalMovement",
                BindingFlags.Public | BindingFlags.Static
            );

            Assert.That(method, Is.Not.Null);
            return (Vector2)method.Invoke(
                null,
                new object[]
                {
                    characterRotation,
                    worldVelocity,
                    referenceSpeed
                }
            );
        }

        private static float CalculateLocomotionPlaybackSpeed(
            float normalizedSpeed,
            float fullSpeedMultiplier
        )
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "CalculateLocomotionPlaybackSpeed",
                BindingFlags.Public | BindingFlags.Static
            );

            Assert.That(method, Is.Not.Null);
            return (float)method.Invoke(
                null,
                new object[] { normalizedSpeed, fullSpeedMultiplier }
            );
        }

        private static Vector3 ApproachVelocity(
            Vector3 current,
            Vector3 target,
            float acceleration,
            float deceleration,
            float brakingAcceleration,
            float deltaTime
        )
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "ApproachVelocity",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float)
                },
                null
            );

            Assert.That(method, Is.Not.Null);
            return (Vector3)method.Invoke(
                null,
                new object[]
                {
                    current,
                    target,
                    acceleration,
                    deceleration,
                    brakingAcceleration,
                    deltaTime
                }
            );
        }

        private static float ApproachVelocity(
            float current,
            float target,
            float acceleration,
            float deceleration,
            float brakingAcceleration,
            float deltaTime
        )
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "ApproachVelocity",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float)
                },
                null
            );

            Assert.That(method, Is.Not.Null);
            return (float)method.Invoke(
                null,
                new object[]
                {
                    current,
                    target,
                    acceleration,
                    deceleration,
                    brakingAcceleration,
                    deltaTime
                }
            );
        }

        private static Vector3 SimulateVelocityApproach(int frameRate)
        {
            Vector3 velocity = Vector3.zero;
            float deltaTime = 1f / frameRate;
            for (int frame = 0; frame < frameRate; frame++)
            {
                velocity = ApproachVelocity(
                    velocity,
                    Vector3.forward * 10f,
                    7f,
                    3f,
                    12f,
                    deltaTime
                );
            }

            return velocity;
        }

        private static Vector3 SimulateVelocityReversal(
            int frameRate,
            float seconds
        )
        {
            Vector3 velocity = Vector3.forward * 6.5f;
            float deltaTime = 1f / frameRate;
            int frameCount = Mathf.RoundToInt(seconds * frameRate);
            for (int frame = 0; frame < frameCount; frame++)
            {
                velocity = ApproachVelocity(
                    velocity,
                    Vector3.back * 6.5f,
                    55f,
                    65f,
                    105f,
                    deltaTime
                );
            }

            return velocity;
        }

        private static float SimulateGravity(int frameRate, float seconds)
        {
            MethodInfo method = LocomotionMathType.GetMethod(
                "ApplyGravity",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null);

            float velocity = 0f;
            int frameCount = Mathf.RoundToInt(frameRate * seconds);
            float deltaTime = 1f / frameRate;
            for (int frame = 0; frame < frameCount; frame++)
            {
                velocity = (float)method.Invoke(
                    null,
                    new object[] { velocity, -25f, 35f, deltaTime }
                );
            }

            return velocity;
        }

        private static object CreateGroundContactState(
            float groundedReleaseGraceSeconds = 0.06f,
            float coyoteTimeSeconds = 0.12f,
            float jumpBufferSeconds = 0.12f
        )
        {
            return Activator.CreateInstance(
                GroundContactStateType,
                groundedReleaseGraceSeconds,
                coyoteTimeSeconds,
                jumpBufferSeconds
            );
        }

        private static object Invoke(
            object target,
            string methodName,
            params object[] arguments
        )
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(method, Is.Not.Null, methodName);
            return method.Invoke(target, arguments);
        }

        private static object GetProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(property, Is.Not.Null, propertyName);
            return property.GetValue(target);
        }

        [Test]
        public void RuntimeTuning_PlayerHealthClampsAndDamageGatesAreIndependent()
        {
            GameObject host = new GameObject("Player Health Tuning Test");
            try
            {
                Type healthType = FindRuntimeType("PlayerHealth");
                Component health = host.AddComponent(healthType);
                Assert.That(
                    Invoke<float>(health, "SetMaximumHealth", 200f),
                    Is.EqualTo(200f)
                );
                Assert.That(
                    Invoke<float>(health, "SetCurrentHealth", 75f),
                    Is.EqualTo(75f)
                );

                Invoke(health, "SetInvulnerable", true);
                DamageResult ignored = Invoke<DamageResult>(
                    health,
                    "ApplyDamage",
                    new DamageInfo(
                        this,
                        CombatFaction.Enemy,
                        DamageType.Kinetic,
                        50f,
                        CombatVector3.Zero,
                        CombatVector3.Zero
                    )
                );
                Assert.That(ignored.WasApplied, Is.False);
                Assert.That(
                    GetProperty<float>(health, "CurrentHealth"),
                    Is.EqualTo(75f)
                );

                Invoke(health, "SetInvulnerable", false);
                Invoke(health, "SetGodMode", true);
                Assert.That(
                    GetProperty<float>(health, "CurrentHealth"),
                    Is.EqualTo(200f)
                );
                Assert.That(GetProperty<bool>(health, "IsGodMode"), Is.True);
                Assert.That(GetProperty<bool>(health, "IsInvulnerable"), Is.False);

                Assert.That(
                    Invoke<float>(
                        health,
                        "SetMaximumHealth",
                        float.PositiveInfinity
                    ),
                    Is.EqualTo(1000000f)
                );
                Assert.That(
                    Invoke<float>(
                        health,
                        "SetMaximumHealth",
                        float.NegativeInfinity
                    ),
                    Is.EqualTo(1f)
                );
                Assert.That(
                    GetProperty<float>(health, "CurrentHealth"),
                    Is.EqualTo(1f)
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RuntimeTuning_PlayerSpeedAndWeaponDamageUseFiniteBounds()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            GameObject playerObject = new GameObject("Runtime Tuning Player");
            try
            {
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                Type controllerType = FindRuntimeType("PowerSuitController");
                Type weaponType = FindRuntimeType("PowerSuitWeapon");
                Component controller = playerObject.AddComponent(controllerType);
                Component weapon = playerObject.AddComponent(weaponType);
                SetPrivateField(controller, "playerCamera", camera);

                Assert.That(
                    GetProperty<Camera>(controller, "PlayerCamera"),
                    Is.SameAs(camera)
                );
                Assert.That(
                    Invoke<float>(
                        controller,
                        "SetGroundSpeedMultiplier",
                        float.PositiveInfinity
                    ),
                    Is.EqualTo(10f)
                );
                Assert.That(
                    Invoke<float>(
                        controller,
                        "SetFlightSpeedMultiplier",
                        float.NegativeInfinity
                    ),
                    Is.EqualTo(0f)
                );
                Assert.That(
                    Invoke<float>(
                        controller,
                        "SetGroundSpeedMultiplier",
                        float.NaN
                    ),
                    Is.EqualTo(10f)
                );

                Assert.That(
                    Invoke<float>(weapon, "SetDamageMultiplier", 2.5f),
                    Is.EqualTo(2.5f)
                );
                Assert.That(
                    Invoke<float>(weapon, "CalculateOutgoingDamage", 40f),
                    Is.EqualTo(100f)
                );
                Assert.That(
                    Invoke<float>(
                        weapon,
                        "SetDamageMultiplier",
                        float.PositiveInfinity
                    ),
                    Is.EqualTo(100f)
                );
                Assert.That(
                    Invoke<float>(
                        weapon,
                        "CalculateOutgoingDamage",
                        float.PositiveInfinity
                    ),
                    Is.EqualTo(1000000f)
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static Type FindRuntimeType(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .First(type => type != null);
        }

        private static void Invoke(object target, string methodName, object argument)
        {
            target.GetType().GetMethod(methodName).Invoke(
                target,
                new[] { argument }
            );
        }

        private static T Invoke<T>(
            object target,
            string methodName,
            object argument
        )
        {
            return (T)target.GetType().GetMethod(methodName).Invoke(
                target,
                new[] { argument }
            );
        }

        private static T GetProperty<T>(object target, string propertyName)
        {
            return (T)target.GetType().GetProperty(propertyName).GetValue(target);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(field, Is.Not.Null, fieldName);
            return field.GetValue(target);
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value
        )
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}
