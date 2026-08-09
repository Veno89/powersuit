using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Powersuit.Tests.EditMode
{
    public sealed class PowerSuitWeaponPresentationTests
    {
        private static Type StateMachineType => FindType(
            "PowerSuitWeaponPresentationStateMachine"
        );

        [Test]
        public void ReadyState_CanUseWeaponAndIsNotTransitioning()
        {
            object machine = CreateMachine(0.5f, 0.75f, false);

            Assert.That(StateName(machine), Is.EqualTo("Ready"));
            Assert.That(GetBool(machine, "CanUseWeapon"), Is.True);
            Assert.That(GetBool(machine, "IsTransitioning"), Is.False);
        }

        [Test]
        public void Draw_CompletesOnlyAfterConfiguredDuration()
        {
            object machine = CreateMachine(0.5f, 0.75f, true);

            Assert.That(InvokeBool(machine, "RequestDraw"), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Drawing"));
            Assert.That(GetBool(machine, "CanUseWeapon"), Is.False);
            Assert.That(GetBool(machine, "IsTransitioning"), Is.True);

            Assert.That(Tick(machine, 0.49f), Is.False);
            Assert.That(StateName(machine), Is.EqualTo("Drawing"));

            Assert.That(Tick(machine, 0.01f), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Ready"));
            Assert.That(GetBool(machine, "CanUseWeapon"), Is.True);
        }

        [Test]
        public void Sheathe_CompletesOnlyAfterConfiguredDuration()
        {
            object machine = CreateMachine(0.5f, 0.75f, false);

            Assert.That(InvokeBool(machine, "RequestSheathe"), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Sheathing"));

            Assert.That(Tick(machine, 0.5f), Is.False);
            Assert.That(StateName(machine), Is.EqualTo("Sheathing"));

            Assert.That(Tick(machine, 0.25f), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Stowed"));
            Assert.That(GetBool(machine, "CanUseWeapon"), Is.False);
        }

        [Test]
        public void TransitionRequests_AreRejectedUntilStableEndpoint()
        {
            object machine = CreateMachine(0.5f, 0.5f, true);

            Assert.That(InvokeBool(machine, "RequestSheathe"), Is.False);
            Assert.That(InvokeBool(machine, "RequestDraw"), Is.True);
            Assert.That(InvokeBool(machine, "RequestDraw"), Is.False);
            Assert.That(InvokeBool(machine, "RequestSheathe"), Is.False);
            Assert.That(InvokeBool(machine, "Toggle"), Is.False);
            Assert.That(StateName(machine), Is.EqualTo("Drawing"));

            Assert.That(Tick(machine, 0.5f), Is.True);
            Assert.That(InvokeBool(machine, "RequestDraw"), Is.False);
            Assert.That(InvokeBool(machine, "RequestSheathe"), Is.True);
        }

        [Test]
        public void Toggle_ChoosesOppositeTransitionFromStableStates()
        {
            object machine = CreateMachine(0.25f, 0.25f, false);

            Assert.That(InvokeBool(machine, "Toggle"), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Sheathing"));
            Assert.That(Tick(machine, 0.25f), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Stowed"));

            Assert.That(InvokeBool(machine, "Toggle"), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Drawing"));
            Assert.That(Tick(machine, 0.25f), Is.True);
            Assert.That(StateName(machine), Is.EqualTo("Ready"));
        }

        [Test]
        public void Tick_RejectsNegativeOrNonFiniteTime()
        {
            object machine = CreateMachine(0.5f, 0.5f, true);
            InvokeBool(machine, "RequestDraw");

            AssertTickThrows(machine, -0.01f);
            AssertTickThrows(machine, float.NaN);
            AssertTickThrows(machine, float.PositiveInfinity);
            Assert.That(StateName(machine), Is.EqualTo("Drawing"));
        }

        [Test]
        public void Adapter_PublicApiWorksWithoutOptionalUnityComponents()
        {
            GameObject host = new GameObject("Weapon Presentation Test");

            try
            {
                Component adapter = host.AddComponent(
                    FindType("PowerSuitWeaponPresentation")
                );

                Assert.That(StateName(adapter), Is.EqualTo("Ready"));
                Assert.That(GetBool(adapter, "CanUseWeapon"), Is.True);
                Assert.That(InvokeBool(adapter, "RequestSheathe"), Is.True);
                Assert.That(StateName(adapter), Is.EqualTo("Sheathing"));
                Assert.That(GetBool(adapter, "CanUseWeapon"), Is.False);
                Assert.That(GetBool(adapter, "IsTransitioning"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static Type FindType(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .First(type => type != null);
        }

        private static object CreateMachine(
            float drawDuration,
            float sheatheDuration,
            bool startsStowed
        )
        {
            return Activator.CreateInstance(
                StateMachineType,
                drawDuration,
                sheatheDuration,
                startsStowed
            );
        }

        private static string StateName(object machine)
        {
            return machine.GetType()
                .GetProperty("State")
                .GetValue(machine)
                .ToString();
        }

        private static bool GetBool(object machine, string propertyName)
        {
            return (bool)machine.GetType()
                .GetProperty(propertyName)
                .GetValue(machine);
        }

        private static bool InvokeBool(object machine, string methodName)
        {
            return (bool)machine.GetType()
                .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                .Invoke(machine, null);
        }

        private static bool Tick(object machine, float deltaTime)
        {
            return (bool)machine.GetType()
                .GetMethod("Tick", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(machine, new object[] { deltaTime });
        }

        private static void AssertTickThrows(object machine, float deltaTime)
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => Tick(machine, deltaTime)
            );

            Assert.That(
                exception.InnerException,
                Is.TypeOf<ArgumentOutOfRangeException>()
            );
        }
    }
}
