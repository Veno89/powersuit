using System;
using System.Reflection;
using NUnit.Framework;
using Powersuit.Combat;
using UnityEngine;

namespace Powersuit.Tests.EditMode
{
    public sealed class CombatDamageAdapterTests
    {
        [TearDown]
        public void TearDown()
        {
            DestroyAllOfType("DamageNumberManager");
        }

        [Test]
        public void EnemyReceiver_AppliesPlayerDamageAndRejectsEnemyFriendlyFire()
        {
            GameObject targetObject = new GameObject("Enemy Damage Receiver Test");
            try
            {
                Component component = targetObject.AddComponent(
                    FindRuntimeType("DamageableTarget")
                );
                SetPrivateField(component, "currentHealth", 100f);
                IDamageReceiver receiver = (IDamageReceiver)component;

                DamageResult applied = receiver.ApplyDamage(
                    CreateDamage(CombatFaction.Player, 25f)
                );
                DamageResult friendly = receiver.ApplyDamage(
                    CreateDamage(CombatFaction.Enemy, 25f)
                );

                Assert.That(receiver.Faction, Is.EqualTo(CombatFaction.Enemy));
                Assert.That(applied.WasApplied, Is.True);
                Assert.That(applied.AppliedAmount, Is.EqualTo(25f));
                Assert.That(friendly.WasApplied, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PlayerReceiver_AppliesEnemyDamageAndRejectsPlayerFriendlyFire()
        {
            GameObject playerObject = new GameObject("Player Damage Receiver Test");
            try
            {
                Component component = playerObject.AddComponent(
                    FindRuntimeType("PlayerHealth")
                );
                SetPrivateField(component, "currentHealth", 100f);
                IDamageReceiver receiver = (IDamageReceiver)component;

                DamageResult applied = receiver.ApplyDamage(
                    CreateDamage(CombatFaction.Enemy, 20f)
                );
                DamageResult friendly = receiver.ApplyDamage(
                    CreateDamage(CombatFaction.Player, 20f)
                );

                Assert.That(receiver.Faction, Is.EqualTo(CombatFaction.Player));
                Assert.That(applied.WasApplied, Is.True);
                Assert.That(applied.AppliedAmount, Is.EqualTo(20f));
                Assert.That(friendly.WasApplied, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        private static DamageInfo CreateDamage(
            CombatFaction faction,
            float amount
        )
        {
            return new DamageInfo(
                source: new object(),
                faction: faction,
                amount: amount,
                position: CombatVector3.Zero,
                direction: new CombatVector3(0f, 0f, 1f)
            );
        }

        private static Type FindRuntimeType(string typeName)
        {
            Type type = Type.GetType($"{typeName}, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, typeName);
            return type;
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

        private static void DestroyAllOfType(string typeName)
        {
            Type type = FindRuntimeType(typeName);
            UnityEngine.Object[] instances =
                UnityEngine.Object.FindObjectsByType(
                    type,
                    FindObjectsInactive.Include
                );
            foreach (UnityEngine.Object instance in instances)
            {
                if (instance is Component component)
                {
                    UnityEngine.Object.DestroyImmediate(component.gameObject);
                }
            }
        }
    }
}
