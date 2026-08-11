using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.Tests.EditMode
{
    public sealed class EnemyContentGeneratorTests
    {
        private const string GeneratorTypeName =
            "Powersuit.Editor.PowerSuitDemoEnemyContentGenerator, "
            + "Assembly-CSharp-Editor";

        [Test]
        public void Generate_IsIdempotentAndDoesNotMutateTheActiveScene()
        {
            Scene activeBefore = SceneManager.GetActiveScene();
            int rootCountBefore = activeBefore.rootCount;
            bool dirtyBefore = activeBefore.isDirty;

            InvokeStatic("Generate");
            Dictionary<string, string> firstGuids = CaptureRequiredGuids();
            InvokeStatic("Generate");
            Dictionary<string, string> secondGuids = CaptureRequiredGuids();

            Scene activeAfter = SceneManager.GetActiveScene();
            Assert.That(activeAfter.handle, Is.EqualTo(activeBefore.handle));
            Assert.That(activeAfter.rootCount, Is.EqualTo(rootCountBefore));
            Assert.That(activeAfter.isDirty, Is.EqualTo(dirtyBefore));
            Assert.That(secondGuids, Is.EqualTo(firstGuids));
            foreach (KeyValuePair<string, string> pair in firstGuids)
            {
                Assert.That(pair.Value, Is.Not.Empty, pair.Key);
            }
        }

        [Test]
        public void Validate_ConfirmsSixEnemiesSharedProjectileAndFiveSpawnZones()
        {
            object generated = InvokeStatic("Generate");
            object report = InvokeStatic("Validate");
            Type reportType = report.GetType();
            bool isValid = (bool)reportType.GetProperty("IsValid").GetValue(report);
            string summary = (string)reportType.GetProperty("Summary").GetValue(report);
            Assert.That(isValid, Is.True, summary);

            Assert.That(CountEnumerableProperty(generated, "Definitions"), Is.EqualTo(6));
            Assert.That(CountEnumerableProperty(generated, "EnemyPrefabs"), Is.EqualTo(6));

            Type generatorType = GetGeneratorType();
            string sandboxPath = (string)generatorType
                .GetField("CombatSandboxPrefabPath", BindingFlags.Public | BindingFlags.Static)
                .GetRawConstantValue();
            string projectilePath = (string)generatorType
                .GetField("ProjectilePrefabPath", BindingFlags.Public | BindingFlags.Static)
                .GetRawConstantValue();
            GameObject sandbox = AssetDatabase.LoadAssetAtPath<GameObject>(sandboxPath);
            GameObject projectile = AssetDatabase.LoadAssetAtPath<GameObject>(projectilePath);
            Assert.That(sandbox, Is.Not.Null);
            Assert.That(projectile, Is.Not.Null);

            Type zoneType = Type.GetType(
                "Powersuit.Enemies.UnityAdapters.SpawnZone, Powersuit.Enemies.Unity",
                throwOnError: true
            );
            Type projectileType = Type.GetType(
                "Powersuit.Enemies.UnityAdapters.EnemyAttackProjectile, "
                + "Powersuit.Enemies.Unity",
                throwOnError: true
            );
            Assert.That(
                sandbox.GetComponentsInChildren(zoneType, true).Length,
                Is.GreaterThanOrEqualTo(5)
            );
            Assert.That(projectile.GetComponent(projectileType), Is.Not.Null);

            Transform environment = sandbox.transform.Find("Environment");
            Assert.That(environment, Is.Not.Null);
            Assert.That(environment.Find("Zone_FoundryYard/UpperCatwalk"), Is.Not.Null);
            Assert.That(environment.Find("Zone_FoundryYard/AoEPracticePad"), Is.Not.Null);
            Assert.That(environment.Find("Zone_CentralCauseway/ElevatedBridge"), Is.Not.Null);
            Assert.That(environment.Find("Zone_CentralCauseway/AoECourtyard"), Is.Not.Null);
            Assert.That(environment.Find("Zone_AerialBasin/HoverPlatformNorth"), Is.Not.Null);
            Assert.That(environment.Find("Zone_AerialBasin/FlightGateTop"), Is.Not.Null);

            Type directorType = Type.GetType(
                "Powersuit.Enemies.UnityAdapters.EnemySpawnDirector, Powersuit.Enemies.Unity",
                throwOnError: true
            );
            Component director = sandbox.GetComponentInChildren(directorType, true);
            Assert.That(director, Is.Not.Null);
            SerializedObject directorSettings = new SerializedObject(director);
            Assert.That(directorSettings.FindProperty("activeEnemyCap").intValue, Is.EqualTo(10));
            Assert.That(
                directorSettings.FindProperty("spawnIntervalSeconds").floatValue,
                Is.EqualTo(4.4f).Within(0.001f)
            );
            Assert.That(directorSettings.FindProperty("maximumGroupSize").intValue, Is.EqualTo(3));

            Type readabilityType = Type.GetType(
                "Powersuit.Enemies.UnityAdapters.EnemyCombatReadabilityPresenter, "
                    + "Powersuit.Enemies.Unity",
                throwOnError: true
            );
            foreach (object enemyPrefab in (IEnumerable)generated.GetType()
                .GetProperty("EnemyPrefabs").GetValue(generated))
            {
                Assert.That(
                    ((GameObject)enemyPrefab).GetComponent(readabilityType),
                    Is.Not.Null,
                    ((GameObject)enemyPrefab).name
                );
            }
        }

        private static object InvokeStatic(string methodName)
        {
            MethodInfo method = GetGeneratorType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null, methodName);
            try
            {
                return method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static Dictionary<string, string> CaptureRequiredGuids()
        {
            PropertyInfo property = GetGeneratorType().GetProperty(
                "RequiredAssetPaths",
                BindingFlags.Public | BindingFlags.Static
            );
            IEnumerable paths = (IEnumerable)property.GetValue(null);
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (object pathValue in paths)
            {
                string path = (string)pathValue;
                result[path] = AssetDatabase.AssetPathToGUID(path);
            }
            return result;
        }

        private static int CountEnumerableProperty(object source, string propertyName)
        {
            IEnumerable values = (IEnumerable)source
                .GetType()
                .GetProperty(propertyName)
                .GetValue(source);
            int count = 0;
            foreach (object ignored in values)
            {
                count++;
            }
            return count;
        }

        private static Type GetGeneratorType()
        {
            return Type.GetType(GeneratorTypeName, throwOnError: true);
        }
    }
}
