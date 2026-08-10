using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Powersuit.Enemies;
using Powersuit.Enemies.UnityAdapters;
using UnityEngine;

namespace Powersuit.Demo.Tests
{
    public sealed class PowerSuitDemoBootstrapTests
    {
        private readonly List<UnityEngine.Object> cleanup =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                if (cleanup[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(cleanup[index]);
                }
            }

            cleanup.Clear();
        }

        [Test]
        public void Configure_RequiresExplicitPrefabPlayerAndCamera()
        {
            Component bootstrap = CreateBootstrap(out GameObject player);
            GameObject world = CreateValidWorldTemplate();
            Camera camera = CreateCamera();
            MethodInfo configure = GetMethod(bootstrap, "Configure");

            AssertInvocationThrows<ArgumentNullException>(
                () => configure.Invoke(
                    bootstrap,
                    new object[] { null, player.transform, camera, null, false }
                )
            );
            AssertInvocationThrows<ArgumentNullException>(
                () => configure.Invoke(
                    bootstrap,
                    new object[] { world, null, camera, null, false }
                )
            );
            AssertInvocationThrows<ArgumentNullException>(
                () => configure.Invoke(
                    bootstrap,
                    new object[] { world, player.transform, null, null, false }
                )
            );
            Assert.That(GetProperty(bootstrap, "WorldInstance"), Is.Null);
        }

        [Test]
        public void Initialize_InstantiatesAtOriginExactlyOnceAndInitializesDirector()
        {
            Component bootstrap = CreateBootstrap(out GameObject player);
            GameObject world = CreateValidWorldTemplate();
            Camera camera = CreateCamera();
            Configure(bootstrap, world, player.transform, camera);

            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.True);
            GameObject first = (GameObject)GetProperty(bootstrap, "WorldInstance");
            EnemySpawnDirector director =
                (EnemySpawnDirector)GetProperty(bootstrap, "SpawnDirector");

            Assert.That(first, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(world));
            Assert.That(first.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(first.transform.rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(director, Is.Not.Null);
            Assert.That(director.IsInitialized, Is.True);
            Assert.That(GetProperty(bootstrap, "IsInitialized"), Is.True);

            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.True);
            Assert.That(GetProperty(bootstrap, "WorldInstance"), Is.SameAs(first));
        }

        [Test]
        public void Initialize_BindsOptionalHudAlreadyOnOwningPlayer()
        {
            Component bootstrap = CreateBootstrap(out GameObject player);
            Component health = player.AddComponent(FindType("PlayerHealth"));
            Component hud = player.AddComponent(FindType("PowerSuitHudPresenter"));
            GameObject world = CreateValidWorldTemplate();
            Camera camera = CreateCamera();
            Configure(bootstrap, world, player.transform, camera, hud);

            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.True);
            Assert.That(GetProperty(hud, "HealthSource"), Is.SameAs(health));
            Assert.That(GetProperty(bootstrap, "HudPresenter"), Is.SameAs(hud));
        }

        [Test]
        public void ResetPreservesWorldAndCleanupDestroysOnlyOwnedInstance()
        {
            Component bootstrap = CreateBootstrap(out GameObject player);
            GameObject world = CreateValidWorldTemplate();
            Camera camera = CreateCamera();
            Configure(bootstrap, world, player.transform, camera);
            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.True);

            GameObject instance = (GameObject)GetProperty(bootstrap, "WorldInstance");
            EnemySpawnDirector director =
                (EnemySpawnDirector)GetProperty(bootstrap, "SpawnDirector");
            director.SetDirectorEnabled(false);
            director.SetPaused(true);

            Assert.That(
                Invoke<bool>(bootstrap, "ResetDemo", true, false),
                Is.True
            );
            Assert.That(GetProperty(bootstrap, "WorldInstance"), Is.SameAs(instance));
            Assert.That(director.IsDirectorEnabled, Is.True);
            Assert.That(director.IsPaused, Is.False);

            Assert.That(Invoke<bool>(bootstrap, "CleanupOwnedWorld"), Is.True);
            Assert.That(instance == null, Is.True);
            Assert.That(GetProperty(bootstrap, "WorldInstance"), Is.Null);
            Assert.That(world, Is.Not.Null, "The configured template is not owned.");
            Assert.That(Invoke<bool>(bootstrap, "CleanupOwnedWorld"), Is.False);
        }

        [Test]
        public void DisablePreservesRuntimeWorldAndReconfigurationRequiresCleanup()
        {
            Component bootstrap = CreateBootstrap(out GameObject player);
            GameObject world = CreateValidWorldTemplate();
            GameObject replacement = CreateValidWorldTemplate();
            Camera camera = CreateCamera();
            Configure(bootstrap, world, player.transform, camera);
            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.True);
            GameObject instance = (GameObject)GetProperty(bootstrap, "WorldInstance");

            player.SetActive(false);
            Assert.That(GetProperty(bootstrap, "WorldInstance"), Is.SameAs(instance));
            Assert.That(instance, Is.Not.Null);

            MethodInfo configure = GetMethod(bootstrap, "Configure");
            AssertInvocationThrows<InvalidOperationException>(
                () => configure.Invoke(
                    bootstrap,
                    new object[]
                    {
                        replacement,
                        player.transform,
                        camera,
                        null,
                        false
                    }
                )
            );
            Assert.That(GetProperty(bootstrap, "WorldInstance"), Is.SameAs(instance));
        }

        [Test]
        public void MissingDirectorFailureRemainsIdempotentUntilExplicitCleanup()
        {
            Component bootstrap = CreateBootstrap(out GameObject player);
            GameObject invalidWorld = Track(new GameObject("World Without Director"));
            invalidWorld.SetActive(false);
            Camera camera = CreateCamera();
            Configure(bootstrap, invalidWorld, player.transform, camera);

            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.False);
            GameObject first = (GameObject)GetProperty(bootstrap, "WorldInstance");
            Assert.That(first, Is.Not.Null);
            Assert.That(
                (string)GetProperty(bootstrap, "LastInitializationError"),
                Does.Contain("EnemySpawnDirector")
            );

            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.False);
            Assert.That(GetProperty(bootstrap, "WorldInstance"), Is.SameAs(first));
        }

        [Test]
        public void Initialize_SuspendsAndCleanupRestoresLegacySceneEnemies()
        {
            Component bootstrap = CreateBootstrap(out GameObject player);
            GameObject world = CreateValidWorldTemplate();
            Camera camera = CreateCamera();
            GameObject legacyEnemy = Track(new GameObject("Legacy Enemy"));
            legacyEnemy.AddComponent(FindType("SimpleEnemy"));
            Configure(bootstrap, world, player.transform, camera);

            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.True);
            Assert.That(legacyEnemy.activeSelf, Is.False);
            Assert.That(
                (int)GetProperty(bootstrap, "SuppressedLegacyEnemyCount"),
                Is.EqualTo(1)
            );

            Assert.That(Invoke<bool>(bootstrap, "TryInitializeDemo"), Is.True);
            Assert.That(legacyEnemy.activeSelf, Is.False);
            Assert.That(
                (int)GetProperty(bootstrap, "SuppressedLegacyEnemyCount"),
                Is.EqualTo(1),
                "Idempotent initialization must retain the restoration record."
            );

            Assert.That(Invoke<bool>(bootstrap, "CleanupOwnedWorld"), Is.True);
            Assert.That(legacyEnemy.activeSelf, Is.True);
            Assert.That(
                (int)GetProperty(bootstrap, "SuppressedLegacyEnemyCount"),
                Is.Zero
            );
        }

        private Component CreateBootstrap(out GameObject player)
        {
            player = Track(new GameObject("Bootstrap Player"));
            return player.AddComponent(FindType("PowerSuitDemoBootstrap"));
        }

        private Camera CreateCamera()
        {
            return Track(new GameObject("Explicit Controller Camera"))
                .AddComponent<Camera>();
        }

        private GameObject CreateValidWorldTemplate()
        {
            GameObject world = Track(new GameObject("Authored Demo World"));
            world.transform.position = new Vector3(9f, 4f, -3f);

            GameObject directorObject = new GameObject("Spawn Director");
            directorObject.transform.SetParent(world.transform, false);
            EnemySpawnDirector director =
                directorObject.AddComponent<EnemySpawnDirector>();

            GameObject zoneObject = new GameObject("Spawn Zone");
            zoneObject.transform.SetParent(world.transform, false);
            SpawnZone zone = zoneObject.AddComponent<SpawnZone>();
            zone.Configure(
                "bootstrap-test-zone",
                SpawnZoneCompatibility.GroundAndFlight,
                Array.Empty<Transform>(),
                new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f)),
                requireGroundProbe: false
            );

            EnemyArchetypeDefinition definition = Track(
                ScriptableObject.CreateInstance<EnemyArchetypeDefinition>()
            );
            definition.ApplyRolePreset(EnemyRole.StationarySentry);
            GameObject enemyPrefab = Track(new GameObject("Bootstrap Test Enemy"));
            enemyPrefab.AddComponent<EnemyArchetypeController>();
            enemyPrefab.SetActive(false);

            SetField(director, "spawnZones", new[] { zone });
            SetField(
                director,
                "spawnEntries",
                new[]
                {
                    new EnemySpawnPrefabEntry(
                        definition,
                        enemyPrefab,
                        isEnabled: true,
                        weightMultiplier: 1f,
                        prewarmCount: 0
                    )
                }
            );
            SetField(director, "prewarmPools", false);
            SetField(director, "spawnImmediately", false);
            SetField(director, "initializeOnStart", false);
            SetField(director, "automaticTick", false);
            world.SetActive(false);
            return world;
        }

        private static void Configure(
            Component bootstrap,
            GameObject world,
            Transform player,
            Camera camera,
            Component hud = null
        )
        {
            GetMethod(bootstrap, "Configure").Invoke(
                bootstrap,
                new object[] { world, player, camera, hud, false }
            );
        }

        private static T Invoke<T>(
            Component component,
            string methodName,
            params object[] arguments
        )
        {
            return (T)GetMethod(component, methodName).Invoke(component, arguments);
        }

        private static MethodInfo GetMethod(Component component, string name)
        {
            MethodInfo method = component.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(method, Is.Not.Null, name);
            return method;
        }

        private static object GetProperty(Component component, string name)
        {
            PropertyInfo property = component.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(property, Is.Not.Null, name);
            return property.GetValue(component);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static Type FindType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, fullName);
            return type;
        }

        private static void AssertInvocationThrows<T>(TestDelegate action)
            where T : Exception
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                action
            );
            Assert.That(exception.InnerException, Is.TypeOf<T>());
        }

        private T Track<T>(T instance) where T : UnityEngine.Object
        {
            cleanup.Add(instance);
            return instance;
        }
    }
}
