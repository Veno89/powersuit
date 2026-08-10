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
        public void Reset_CancelsTransitionAtRequestedStableEndpoint()
        {
            object machine = CreateMachine(0.5f, 0.75f, true);
            Assert.That(InvokeBool(machine, "RequestDraw"), Is.True);

            machine.GetType()
                .GetMethod("Reset", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(machine, new object[] { true });

            Assert.That(StateName(machine), Is.EqualTo("Stowed"));
            Assert.That(GetBool(machine, "IsTransitioning"), Is.False);
            Assert.That(
                machine.GetType().GetProperty("RemainingTransitionTime").GetValue(machine),
                Is.EqualTo(0f)
            );

            machine.GetType()
                .GetMethod("Reset", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(machine, new object[] { false });

            Assert.That(StateName(machine), Is.EqualTo("Ready"));
            Assert.That(GetBool(machine, "CanUseWeapon"), Is.True);
        }

        [Test]
        public void AirborneStowedAim_CanDrawBeforeEffectiveAimActivates()
        {
            CursorLockMode originalLockState = Cursor.lockState;
            bool originalVisibility = Cursor.visible;
            GameObject cameraHost = new GameObject("Presentation Test Camera");
            GameObject playerHost = new GameObject("Airborne Presentation Test");

            try
            {
                Camera camera = cameraHost.AddComponent<Camera>();
                camera.tag = "MainCamera";

                Component controller = playerHost.AddComponent(
                    FindType("PowerSuitController")
                );
                Component presentation = playerHost.AddComponent(
                    FindType("PowerSuitWeaponPresentation")
                );

                SetField(presentation, "controller", controller);
                SetField(presentation, "startsStowed", true);
                presentation.GetType().GetMethod("ResetForRespawn").Invoke(
                    presentation,
                    null
                );
                SetField(controller, "isFlying", true);
                SetField(controller, "aimRequested", true);
                controller.GetType().GetMethod("RefreshAimAvailability").Invoke(
                    controller,
                    null
                );

                Assert.That(GetBool(controller, "AimRequested"), Is.True);
                Assert.That(GetBool(controller, "IsAiming"), Is.False);

                presentation.GetType().GetMethod(
                    "Update",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )?.Invoke(presentation, null);
                Assert.That(StateName(presentation), Is.EqualTo("Drawing"));

                object machine = GetField(presentation, "stateMachine");
                Tick(machine, 2f);
                presentation.GetType().GetMethod(
                    "UpdateWeaponAvailability",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )?.Invoke(presentation, null);

                Assert.That(StateName(presentation), Is.EqualTo("Ready"));
                Assert.That(GetBool(controller, "IsAiming"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerHost);
                UnityEngine.Object.DestroyImmediate(cameraHost);
                Cursor.lockState = originalLockState;
                Cursor.visible = originalVisibility;
            }
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

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static object GetField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            Assert.That(field, Is.Not.Null);
            return field.GetValue(target);
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
