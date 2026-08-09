using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Powersuit.Tests.EditMode
{
    public sealed class PowerSuitCameraPerformanceTests
    {
        private static Type CameraMathType => FindType("PowerSuitCameraMath");
        private static Type FramePacingPolicyType =>
            FindType("PowerSuitFramePacingPolicy");

        [Test]
        public void ExponentialDamping_IsStableAcrossFrameRates()
        {
            float at30 = SimulateDamping(30, 1f, 4f);
            float at60 = SimulateDamping(60, 1f, 4f);
            float at120 = SimulateDamping(120, 1f, 4f);

            Assert.That(at30, Is.EqualTo(at60).Within(0.00001f));
            Assert.That(at60, Is.EqualTo(at120).Within(0.00001f));
            Assert.That(at60, Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void ExponentialDamping_InvalidDeltaDoesNotAdvance()
        {
            Assert.That(DampingFactor(12f, 0f), Is.EqualTo(0f));
            Assert.That(DampingFactor(12f, -1f), Is.EqualTo(0f));
            Assert.That(DampingFactor(12f, float.NaN), Is.EqualTo(0f));
            Assert.That(DampingFactor(0f, 0.016f), Is.EqualTo(1f));
        }

        [Test]
        public void CollisionDistance_PullsInImmediatelyAndReleasesSmoothly()
        {
            (float pulledIn, bool occluded) = ResolveCameraDistance(
                6f,
                6f,
                2f,
                false,
                14f,
                1f / 60f
            );
            Assert.That(pulledIn, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(occluded, Is.True);

            (float firstRelease, bool recovering) = ResolveCameraDistance(
                pulledIn,
                6f,
                6f,
                occluded,
                14f,
                1f / 60f
            );
            (float secondRelease, bool stillRecovering) = ResolveCameraDistance(
                firstRelease,
                6f,
                6f,
                recovering,
                14f,
                1f / 60f
            );

            Assert.That(firstRelease, Is.GreaterThan(2f).And.LessThan(6f));
            Assert.That(secondRelease, Is.GreaterThan(firstRelease).And.LessThan(6f));
            Assert.That(recovering, Is.True);
            Assert.That(stillRecovering, Is.True);
        }

        [Test]
        public void UnobstructedProfileChange_IsNotFilteredTwice()
        {
            (float distance, bool occluded) = ResolveCameraDistance(
                5.39f,
                5.77f,
                5.77f,
                false,
                14f,
                1f / 60f
            );

            Assert.That(distance, Is.EqualTo(5.77f).Within(0.0001f));
            Assert.That(occluded, Is.False);
        }

        [Test]
        public void FramePacing_UsesFastDisplaySyncAndFallsBackBelow60Hz()
        {
            Assert.That(ShouldUseVSync(true, 100d, 60), Is.True);
            Assert.That(ShouldUseVSync(true, 59.94d, 60), Is.True);
            Assert.That(ShouldUseVSync(true, 30d, 60), Is.False);
            Assert.That(ShouldUseVSync(false, 100d, 60), Is.False);
            Assert.That(ShouldUseVSync(true, double.NaN, 60), Is.False);
        }

        private static float SimulateDamping(
            int framesPerSecond,
            float duration,
            float sharpness
        )
        {
            float value = 0f;
            float deltaTime = 1f / framesPerSecond;
            int frameCount = (int)(duration * framesPerSecond);

            for (int index = 0; index < frameCount; index++)
            {
                value = Damp(value, 1f, sharpness, deltaTime);
            }

            return value;
        }

        private static float Damp(
            float current,
            float target,
            float sharpness,
            float deltaTime
        )
        {
            return (float)Invoke(
                CameraMathType,
                "Damp",
                current,
                target,
                sharpness,
                deltaTime
            );
        }

        private static float DampingFactor(float sharpness, float deltaTime)
        {
            return (float)Invoke(
                CameraMathType,
                "ExponentialDampingFactor",
                sharpness,
                deltaTime
            );
        }

        private static (float distance, bool occluded) ResolveCameraDistance(
            float currentDistance,
            float unobstructedDistance,
            float allowedDistance,
            bool wasOccluded,
            float releaseSharpness,
            float deltaTime
        )
        {
            MethodInfo method = CameraMathType.GetMethod(
                "ResolveCameraDistance",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null);

            object[] arguments =
            {
                currentDistance,
                unobstructedDistance,
                allowedDistance,
                wasOccluded,
                releaseSharpness,
                deltaTime,
                false
            };
            float distance = (float)method.Invoke(null, arguments);
            return (distance, (bool)arguments[6]);
        }

        private static bool ShouldUseVSync(
            bool synchronize,
            double displayRefreshRate,
            int fallbackTargetFrameRate
        )
        {
            return (bool)Invoke(
                FramePacingPolicyType,
                "ShouldUseVSync",
                synchronize,
                displayRefreshRate,
                fallbackTargetFrameRate
            );
        }

        private static object Invoke(Type type, string methodName, params object[] arguments)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null, methodName);
            return method.Invoke(null, arguments);
        }

        private static Type FindType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, name);
            return type;
        }
    }
}
