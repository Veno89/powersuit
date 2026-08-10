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

        [TestCase(false, false, false, 0f)]
        [TestCase(true, false, false, 0.82f)]
        [TestCase(false, true, false, 0.48f)]
        [TestCase(true, true, true, 1f)]
        public void ThrusterIntensity_CommunicatesPoweredMovementPriority(
            bool isRunning,
            bool isFlying,
            bool isBoosting,
            float expected
        )
        {
            Type thrusterMath = FindType("PowerSuitThrusterMath");
            MethodInfo resolve = thrusterMath.GetMethod(
                "ResolveTargetIntensity",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(resolve, Is.Not.Null);

            float actual = (float)resolve.Invoke(
                null,
                new object[]
                {
                    isRunning,
                    isFlying,
                    isBoosting,
                    0.82f,
                    0.48f,
                    1f
                }
            );
            Assert.That(actual, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void ThrusterIntensity_FailsClosedForNonFiniteTuning()
        {
            Type thrusterMath = FindType("PowerSuitThrusterMath");
            MethodInfo resolve = thrusterMath.GetMethod(
                "ResolveTargetIntensity",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(resolve, Is.Not.Null);
            Assert.That(
                resolve.Invoke(
                    null,
                    new object[]
                    {
                        true,
                        false,
                        false,
                        float.NaN,
                        0.5f,
                        1f
                    }
                ),
                Is.EqualTo(0f)
            );
        }

        [Test]
        public void ThrusterPresentation_BuildsFourCachedBlueWhiteJets()
        {
            GameObject host = new GameObject("Thruster Presentation Test");
            host.SetActive(false);
            try
            {
                GameObject visual = new GameObject("PowerSuitVisual_Generator109");
                visual.transform.SetParent(host.transform, false);
                foreach (string name in new[]
                {
                    "Thruster_Nozzle.L",
                    "Thruster_Nozzle.R",
                    "Heavy_Boot.L",
                    "Heavy_Boot.R"
                })
                {
                    GameObject anchor = new GameObject(name);
                    anchor.transform.SetParent(visual.transform, false);
                }

                Type presentationType = FindType("PowerSuitThrusterPresentation");
                Component presentation = host.AddComponent(presentationType);
                presentationType.GetProperty("VisualRoot")?.SetValue(
                    presentation,
                    visual.transform
                );
                MethodInfo awake = presentationType.GetMethod(
                    "Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.That(awake, Is.Not.Null);
                awake.Invoke(presentation, null);

                Assert.That(
                    presentationType.GetProperty("CachedJetCount")
                        ?.GetValue(presentation),
                    Is.EqualTo(4)
                );
                Assert.That(
                    host.GetComponentsInChildren<LineRenderer>(true),
                    Has.Length.EqualTo(8),
                    "Each nozzle needs an outer plume and white-hot core."
                );
                Assert.That(
                    host.GetComponentsInChildren<Light>(true),
                    Has.Length.EqualTo(2),
                    "Only backpack nozzles carry cached point-light glows."
                );
                Assert.That(
                    host.GetComponentsInChildren<LineRenderer>(true),
                    Has.All.Property("enabled").False,
                    "Jets must be hidden before sprint/flight demand."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
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
