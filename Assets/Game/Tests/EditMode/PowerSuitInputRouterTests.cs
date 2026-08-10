using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Powersuit.Tests.EditMode
{
    public sealed class PowerSuitInputRouterTests
    {
        private static Type ButtonsType =>
            FindRuntimeType("PowerSuitInputButtons");

        private static Type RawStateType =>
            FindRuntimeType("PowerSuitRawInputState");

        private static Type FrameBufferType =>
            FindRuntimeType("PowerSuitInputFrameBuffer");

        [Test]
        public void FrameBuffer_PreservesHeldPressedAndReleasedSemantics()
        {
            object buffer = Activator.CreateInstance(FrameBufferType);
            object fireAndLightning = CombineButtons("Fire", "Lightning");

            object pressed = Sample(
                buffer,
                10,
                Raw(
                    held: fireAndLightning,
                    pressed: fireAndLightning
                )
            );

            Assert.That(GetBool(pressed, "FireHeld"), Is.True);
            Assert.That(GetBool(pressed, "FirePressed"), Is.True);
            Assert.That(GetBool(pressed, "LightningHeld"), Is.True);
            Assert.That(GetBool(pressed, "LightningPressed"), Is.True);
            Assert.That(GetBool(pressed, "LightningReleased"), Is.False);

            object fireHeld = Sample(
                buffer,
                11,
                // Simulate a second bound device pressing Fire while the
                // first device still holds the same logical intent.
                Raw(
                    held: Button("Fire"),
                    pressed: Button("Fire")
                )
            );

            Assert.That(GetBool(fireHeld, "FireHeld"), Is.True);
            Assert.That(GetBool(fireHeld, "FirePressed"), Is.False);
            Assert.That(GetBool(fireHeld, "LightningReleased"), Is.True);

            object fireReleased = Sample(buffer, 12, Raw());
            Assert.That(GetBool(fireReleased, "FireHeld"), Is.False);
            Assert.That(
                WasReleased(fireReleased, Button("Fire")),
                Is.True
            );
        }

        [Test]
        public void FrameBuffer_FirstSampleForFrameWinsForEveryConsumer()
        {
            object buffer = Activator.CreateInstance(FrameBufferType);
            object flight = Button("FlightToggle");

            object first = Sample(
                buffer,
                25,
                Raw(
                    move: new Vector2(0.75f, -0.25f),
                    held: flight,
                    pressed: flight
                )
            );
            object second = Sample(
                buffer,
                25,
                Raw(
                    move: Vector2.left,
                    held: Button("Fire"),
                    pressed: Button("Fire")
                )
            );

            Assert.That(
                GetProperty<Vector2>(second, "Move"),
                Is.EqualTo(GetProperty<Vector2>(first, "Move"))
            );
            Assert.That(
                GetBool(second, "FlightTogglePressed"),
                Is.True
            );
            Assert.That(GetBool(second, "FirePressed"), Is.False);
            Assert.That(GetProperty<int>(second, "SampleFrame"), Is.EqualTo(25));
        }

        [Test]
        public void FrameBuffer_ResetStartsCleanFallbackLifecycle()
        {
            object buffer = Activator.CreateInstance(FrameBufferType);
            object fire = Button("Fire");
            Sample(buffer, 40, Raw(held: fire, pressed: fire));

            Invoke(buffer, "Reset");
            Assert.That(GetProperty<bool>(buffer, "HasSnapshot"), Is.False);
            Assert.That(GetProperty<int>(buffer, "SampleFrame"), Is.EqualTo(-1));

            // Re-enabling while a button is already down must preserve held
            // state without manufacturing a new semi-automatic press.
            object resumed = Sample(buffer, 41, Raw(held: fire));
            Assert.That(GetBool(resumed, "FireHeld"), Is.True);
            Assert.That(GetBool(resumed, "FirePressed"), Is.False);

            object released = Sample(buffer, 42, Raw());
            Assert.That(WasReleased(released, fire), Is.True);
        }

        [Test]
        public void DefaultGamepadMap_FlightAndAttackCannotConflict()
        {
            Type mapType = FindRuntimeType("PowerSuitDefaultInputBindings");
            Type controlType = FindRuntimeType("PowerSuitGamepadControl");
            MethodInfo getIntent = mapType.GetMethod(
                "GetGamepadIntent",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(getIntent, Is.Not.Null);

            object west = Enum.Parse(controlType, "ButtonWest");
            object rightTrigger = Enum.Parse(controlType, "RightTrigger");
            object westIntent = getIntent.Invoke(null, new[] { west });
            object triggerIntent = getIntent.Invoke(
                null,
                new[] { rightTrigger }
            );

            Assert.That(HasButton(westIntent, "FlightToggle"), Is.True);
            Assert.That(HasButton(westIntent, "Fire"), Is.False);
            Assert.That(HasButton(triggerIntent, "Fire"), Is.True);
            Assert.That(
                HasButton(triggerIntent, "FlightToggle"),
                Is.False
            );

            object stickPress = Enum.Parse(controlType, "RightStickPress");
            object scopeIntent = getIntent.Invoke(
                null,
                new[] { stickPress }
            );
            Assert.That(HasButton(scopeIntent, "Scope"), Is.True);
            Assert.That(HasButton(scopeIntent, "Aim"), Is.False);
            Assert.That(HasButton(scopeIntent, "Fire"), Is.False);
        }

        [Test]
        public void Snapshot_ExposesIndependentHeldAndPressedScopeIntent()
        {
            object buffer = Activator.CreateInstance(FrameBufferType);
            object scope = Button("Scope");
            object snapshot = Sample(
                buffer,
                60,
                Raw(held: scope, pressed: scope)
            );

            Assert.That(GetBool(snapshot, "ScopeHeld"), Is.True);
            Assert.That(GetBool(snapshot, "ScopePressed"), Is.True);
            Assert.That(GetBool(snapshot, "AimHeld"), Is.False);
        }

        private static object Raw(
            Vector2? move = null,
            Vector2? pointerLook = null,
            Vector2? gamepadLook = null,
            float vertical = 0f,
            object held = null,
            object pressed = null,
            object released = null
        )
        {
            object none = Button("None");
            return Activator.CreateInstance(
                RawStateType,
                move ?? Vector2.zero,
                pointerLook ?? Vector2.zero,
                gamepadLook ?? Vector2.zero,
                vertical,
                held ?? none,
                pressed ?? none,
                released ?? none
            );
        }

        private static object Sample(object buffer, int frame, object raw)
        {
            return Invoke(buffer, "Sample", frame, raw);
        }

        private static bool WasReleased(object snapshot, object button)
        {
            return (bool)Invoke(snapshot, "WasReleased", button);
        }

        private static bool GetBool(object target, string propertyName)
        {
            return GetProperty<bool>(target, propertyName);
        }

        private static T GetProperty<T>(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(property, Is.Not.Null, propertyName);
            return (T)property.GetValue(target);
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

        private static object Button(string name)
        {
            return Enum.Parse(ButtonsType, name);
        }

        private static object CombineButtons(params string[] names)
        {
            ulong combined = 0;
            foreach (string name in names)
            {
                combined |= Convert.ToUInt64(Button(name));
            }

            return Enum.ToObject(ButtonsType, combined);
        }

        private static bool HasButton(object value, string name)
        {
            ulong bits = Convert.ToUInt64(value);
            ulong button = Convert.ToUInt64(Button(name));
            return (bits & button) != 0;
        }

        private static Type FindRuntimeType(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .First(type => type != null);
        }
    }
}
