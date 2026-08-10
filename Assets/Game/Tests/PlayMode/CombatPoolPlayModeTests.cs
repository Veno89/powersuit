using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;

namespace Powersuit.Tests.PlayMode
{
    public sealed class CombatPoolPlayModeTests
    {
        [UnityTest]
        public IEnumerator ProjectilePool_ReusesInstanceAndClearsTransientState()
        {
            Type poolType = FindType("CombatFeedbackPool");
            Type projectileType = FindType("PlayerProjectile");
            PropertyInfo instanceProperty = poolType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static
            );
            MethodInfo spawn = poolType.GetMethod(
                "Spawn",
                BindingFlags.Public | BindingFlags.Static
            );
            MethodInfo recycle = poolType.GetMethod(
                "Recycle",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(instanceProperty, Is.Not.Null);
            Assert.That(spawn, Is.Not.Null);
            Assert.That(recycle, Is.Not.Null);

            UnityEngine.Object existingPool =
                instanceProperty.GetValue(null) as UnityEngine.Object;
            if (existingPool != null)
            {
                UnityEngine.Object.Destroy(existingPool);
                yield return null;
            }

            GameObject prefab = new GameObject("Pool Test Projectile Prefab");
            prefab.SetActive(false);
            Component prefabProjectile = prefab.AddComponent(projectileType);
            Assert.That(prefabProjectile, Is.Not.Null);

            GameObject first = null;
            GameObject second = null;
            try
            {
                first = spawn.Invoke(
                    null,
                    new object[] { prefab, Vector3.zero, Quaternion.identity }
                ) as GameObject;
                Assert.That(first, Is.Not.Null);
                Assert.That(first.activeSelf, Is.True);

                Component firstProjectile = first.GetComponent(projectileType);
                projectileType.GetMethod("Initialize")?.Invoke(
                    firstProjectile,
                    new object[]
                    {
                        Vector3.forward,
                        100f,
                        25f,
                        4f,
                        0.1f,
                        null,
                        true
                    }
                );
                AssertPrivateBool(firstProjectile, "isInitialized", true);
                AssertPrivateBool(firstProjectile, "isCritical", true);

                recycle.Invoke(null, new object[] { first });
                Assert.That(first.activeSelf, Is.False);
                AssertPrivateBool(firstProjectile, "isInitialized", false);
                AssertPrivateBool(firstProjectile, "isCritical", false);

                second = spawn.Invoke(
                    null,
                    new object[] { prefab, Vector3.one, Quaternion.identity }
                ) as GameObject;
                Assert.That(second, Is.SameAs(first));
                Assert.That(second.activeSelf, Is.True);
                AssertPrivateBool(
                    second.GetComponent(projectileType),
                    "isInitialized",
                    false
                );

                Component pool = instanceProperty.GetValue(null) as Component;
                Assert.That(pool, Is.Not.Null);
                Assert.That(
                    poolType.GetProperty("ActiveCount")?.GetValue(pool),
                    Is.EqualTo(1)
                );
                object activeStatistics = poolType
                    .GetProperty("CurrentStatistics")
                    ?.GetValue(pool);
                Assert.That(activeStatistics, Is.Not.Null);
                Assert.That(
                    activeStatistics.GetType()
                        .GetProperty("ActiveProjectileCount")
                        ?.GetValue(activeStatistics),
                    Is.EqualTo(1)
                );

                recycle.Invoke(null, new object[] { second });
                Assert.That(
                    poolType.GetProperty("InactiveCount")?.GetValue(pool),
                    Is.GreaterThanOrEqualTo(1)
                );
                object recycledStatistics = poolType
                    .GetProperty("CurrentStatistics")
                    ?.GetValue(pool);
                Assert.That(
                    recycledStatistics.GetType()
                        .GetProperty("ActiveProjectileCount")
                        ?.GetValue(recycledStatistics),
                    Is.EqualTo(0)
                );
            }
            finally
            {
                Component pool = instanceProperty.GetValue(null) as Component;
                if (pool != null)
                {
                    UnityEngine.Object.Destroy(pool.gameObject);
                }
                UnityEngine.Object.Destroy(prefab);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PrewarmedPool_OneThousandCyclesDoNotInstantiateAtRuntime()
        {
            Type poolType = FindType("CombatFeedbackPool");
            PropertyInfo instanceProperty = poolType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static
            );
            MethodInfo prewarm = poolType.GetMethod(
                "Prewarm",
                BindingFlags.Public | BindingFlags.Static
            );
            MethodInfo spawn = poolType.GetMethod(
                "Spawn",
                BindingFlags.Public | BindingFlags.Static
            );
            MethodInfo recycle = poolType.GetMethod(
                "Recycle",
                BindingFlags.Public | BindingFlags.Static
            );

            UnityEngine.Object existingPool =
                instanceProperty.GetValue(null) as UnityEngine.Object;
            if (existingPool != null)
            {
                UnityEngine.Object.Destroy(existingPool);
                yield return null;
            }

            GameObject prefab = new GameObject("Pool Steady State Test Prefab");
            prefab.SetActive(false);
            try
            {
                prewarm.Invoke(null, new object[] { prefab, 2 });
                Component pool = instanceProperty.GetValue(null) as Component;
                Assert.That(pool, Is.Not.Null);

                object before = poolType.GetProperty("CurrentStatistics")
                    ?.GetValue(pool);
                Assert.That(before, Is.Not.Null);
                Type statisticsType = before.GetType();
                long runtimeBefore = (long)statisticsType
                    .GetProperty("RuntimeInstantiationCount")
                    .GetValue(before);

                for (int index = 0; index < 1000; index++)
                {
                    GameObject active = spawn.Invoke(
                        null,
                        new object[]
                        {
                            prefab,
                            Vector3.zero,
                            Quaternion.identity
                        }
                    ) as GameObject;
                    recycle.Invoke(null, new object[] { active });
                }

                object after = poolType.GetProperty("CurrentStatistics")
                    .GetValue(pool);
                Assert.That(
                    statisticsType.GetProperty("RuntimeInstantiationCount")
                        .GetValue(after),
                    Is.EqualTo(runtimeBefore)
                );
                Assert.That(
                    statisticsType.GetProperty("ReusedSpawnCount")
                        .GetValue(after),
                    Is.EqualTo(1000L)
                );
                Assert.That(
                    statisticsType.GetProperty("ActiveCount").GetValue(after),
                    Is.EqualTo(0)
                );
                Assert.That(
                    statisticsType.GetProperty("InactiveCount").GetValue(after),
                    Is.EqualTo(2)
                );
            }
            finally
            {
                Component pool = instanceProperty.GetValue(null) as Component;
                if (pool != null)
                {
                    UnityEngine.Object.Destroy(pool.gameObject);
                }
                UnityEngine.Object.Destroy(prefab);
            }

            yield return null;
        }

        private static Type FindType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name))
                .First(type => type != null);
        }

        private static void AssertPrivateBool(
            Component component,
            string fieldName,
            bool expected
        )
        {
            FieldInfo field = component.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(field, Is.Not.Null, fieldName);
            Assert.That(field.GetValue(component), Is.EqualTo(expected));
        }
    }
}
