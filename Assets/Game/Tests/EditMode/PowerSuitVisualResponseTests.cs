using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Powersuit.Tests.EditMode
{
    public sealed class PowerSuitVisualResponseTests
    {
        private static Type MathType => FindType("PowerSuitVisualResponseMath");

        [Test]
        public void ExponentialStep_IsEquivalentAtCommonFrameRates()
        {
            float at30 = Simulate(30, 1f);
            float at60 = Simulate(60, 1f);
            float at120 = Simulate(120, 1f);

            Assert.That(at30, Is.EqualTo(at60).Within(0.0001f));
            Assert.That(at60, Is.EqualTo(at120).Within(0.0001f));
            Assert.That(at60, Is.GreaterThan(9.99f));
            Assert.That(at60, Is.LessThanOrEqualTo(10f));
        }

        [TestCase(-3f, 4f, 14f, 0.06f, 0f)]
        [TestCase(-4f, 4f, 14f, 0.06f, 0f)]
        [TestCase(-9f, 4f, 14f, 0.06f, 0.03f)]
        [TestCase(-14f, 4f, 14f, 0.06f, 0.06f)]
        [TestCase(-30f, 4f, 14f, 0.06f, 0.06f)]
        [TestCase(8f, 4f, 14f, 0.06f, 0f)]
        public void LandingCompression_UsesDownwardImpactOnly(
            float verticalSpeed,
            float minimumImpact,
            float fullImpact,
            float maximum,
            float expected
        )
        {
            float actual = (float)Invoke(
                "CalculateLandingCompression",
                verticalSpeed,
                minimumImpact,
                fullImpact,
                maximum
            );
            Assert.That(actual, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void ExponentialStep_InvalidDeltaDoesNotMutatePresentation()
        {
            Assert.That(
                Invoke("ExponentialStep", 3f, 10f, 12f, 0f),
                Is.EqualTo(3f)
            );
            Assert.That(
                Invoke("ExponentialStep", 3f, 10f, 12f, float.NaN),
                Is.EqualTo(3f)
            );
        }

        [Test]
        public void CompressionAxis_FollowsWorldUpThroughModelCorrection()
        {
            Quaternion correction =
                Quaternion.AngleAxis(90f, Vector3.right) *
                Quaternion.Euler(0f, 180f, 0f);
            Vector3 localUp = Quaternion.Inverse(correction) * Vector3.up;

            Assert.That(
                Invoke("FindDominantScaleAxis", localUp),
                Is.EqualTo(2),
                "The Generator wrapper's local Z axis represents world height."
            );
            Assert.That(
                Invoke("FindDominantScaleAxis", Vector3.up),
                Is.EqualTo(1)
            );
        }

        private static float Simulate(int frameRate, float seconds)
        {
            float value = 0f;
            float delta = 1f / frameRate;
            int steps = (int)(seconds * frameRate);
            for (int index = 0; index < steps; index++)
            {
                value = (float)Invoke(
                    "ExponentialStep",
                    value,
                    10f,
                    12f,
                    delta
                );
            }
            return value;
        }

        private static object Invoke(string methodName, params object[] values)
        {
            MethodInfo method = MathType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null, methodName);
            return method.Invoke(null, values);
        }

        private static Type FindType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name, false))
                .First(type => type != null);
        }
    }
}
