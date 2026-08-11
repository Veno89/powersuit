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
            Type encounterType = Type.GetType(
                "PowerSuitEncounterDirector, Assembly-CSharp",
                throwOnError: true
            );
            Component encounter = sandbox.GetComponentInChildren(
                encounterType,
                true
            );
            Assert.That(encounter, Is.Not.Null);
            SerializedObject encounterSettings = new SerializedObject(encounter);
            SerializedProperty phases = encounterSettings.FindProperty("phases");
            Assert.That(phases, Is.Not.Null);
            Assert.That(phases.arraySize, Is.EqualTo(3));
            Assert.That(
                encounterSettings.FindProperty("spawnDirector")
                    .objectReferenceValue,
                Is.EqualTo(director)
            );

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

        [Test]
        public void GeneratedGroundSpawnPoints_HaveSurfaceAndGeometryClearance()
        {
            InvokeStatic("Generate");
            Type generatorType = GetGeneratorType();
            string sandboxPath = (string)generatorType
                .GetField(
                    "CombatSandboxPrefabPath",
                    BindingFlags.Public | BindingFlags.Static
                )
                .GetRawConstantValue();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                sandboxPath
            );
            Assert.That(prefab, Is.Not.Null);

            Type zoneType = Type.GetType(
                "Powersuit.Enemies.UnityAdapters.SpawnZone, Powersuit.Enemies.Unity",
                throwOnError: true
            );
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Physics.SyncTransforms();
                Component[] zones = instance.GetComponentsInChildren(
                    zoneType,
                    includeInactive: true
                );
                PropertyInfo compatibility = zoneType.GetProperty("Compatibility");
                PropertyInfo capacity = zoneType.GetProperty("CandidateCapacity");
                MethodInfo tryBuild = zoneType.GetMethod("TryBuildCandidate");
                Assert.That(compatibility, Is.Not.Null);
                Assert.That(capacity, Is.Not.Null);
                Assert.That(tryBuild, Is.Not.Null);

                int totalCandidateCount = 0;
                int groundCandidateCount = 0;
                foreach (Component zone in zones)
                {
                    int count = (int)capacity.GetValue(zone);
                    totalCandidateCount += count;
                    if (!compatibility.GetValue(zone).ToString().Contains("Ground"))
                    {
                        continue;
                    }

                    groundCandidateCount += count;
                    Assert.That(
                        count,
                        Is.GreaterThanOrEqualTo(7),
                        zone.name + " cannot host its full authored ground wave."
                    );
                    for (int index = 0; index < count; index++)
                    {
                        object[] arguments = { index, null, null };
                        Assert.That(
                            (bool)tryBuild.Invoke(zone, arguments),
                            Is.True,
                            zone.name + " point " + index
                        );
                        object candidate = arguments[2];
                        Type candidateType = candidate.GetType();
                        Assert.That(
                            candidateType.GetProperty("IsGroundPositionValid")
                                .GetValue(candidate),
                            Is.True,
                            zone.name + " point " + index + " has no ground."
                        );
                        Assert.That(
                            candidateType.GetProperty("IsObstacleFree")
                                .GetValue(candidate),
                            Is.True,
                            zone.name + " point " + index +
                                " intersects generated geometry."
                        );
                    }
                }

                Assert.That(groundCandidateCount, Is.EqualTo(21));
                Assert.That(totalCandidateCount, Is.EqualTo(28));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
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
