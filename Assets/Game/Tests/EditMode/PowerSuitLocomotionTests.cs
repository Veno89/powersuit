using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Powersuit.Tests.EditMode
{
    public sealed class PowerSuitLocomotionTests
    {
        private static Type LocomotionMathType =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("PowerSuitLocomotionMath"))
                .First(type => type != null);

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
    }
}
