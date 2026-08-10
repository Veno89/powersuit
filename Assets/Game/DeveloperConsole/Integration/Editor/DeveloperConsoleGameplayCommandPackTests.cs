using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Powersuit.Abilities.UnityAdapters;
using Powersuit.Enemies.UnityAdapters;
using UnityEngine;

namespace Powersuit.DeveloperConsole.Integration.Tests
{
    public sealed class DeveloperConsoleGameplayCommandPackTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                if (createdObjects[index] != null)
                {
                    Object.DestroyImmediate(createdObjects[index]);
                }
            }

            createdObjects.Clear();
        }

        [Test]
        public void Register_ExposesAvailableHealthAndGeneralCapabilitiesIdempotently()
        {
            GameObject player = CreateObject("ConsoleHealthTest");
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            DeveloperConsoleGameplayCommandPack pack =
                player.AddComponent<DeveloperConsoleGameplayCommandPack>();
            pack.Configure(
                null,
                health,
                null,
                null,
                null,
                null,
                null,
                null
            );
            var registry = new ConsoleCommandRegistry();

            int firstRegistration = pack.Register(registry);
            int secondRegistration = pack.Register(registry);

            Assert.That(firstRegistration, Is.EqualTo(11));
            Assert.That(secondRegistration, Is.Zero);
            Assert.That(registry.TryGetCommand("reloadscene", out _), Is.True);
            Assert.That(registry.TryGetCommand("fps", out _), Is.True);
            Assert.That(registry.TryGetCommand("pools", out _), Is.True);
            Assert.That(registry.TryGetCommand("projectiles", out _), Is.True);
            Assert.That(registry.TryGetCommand("killplayer", out _), Is.True);
            Assert.That(registry.TryGetCommand("playerstate", out _), Is.True);

            Assert.That(registry.TryGetCommand("god", out _), Is.True);
            Assert.That(registry.TryGetCommand("invulnerable", out _), Is.True);
            Assert.That(registry.TryGetCommand("heal", out _), Is.True);
            Assert.That(registry.TryGetCommand("player.hp", out _), Is.True);
            Assert.That(registry.TryGetCommand("player.maxhp", out _), Is.True);
            Assert.That(
                registry.TryGetCommand("player.damage_multiplier", out _),
                Is.False
            );
            Assert.That(
                registry.TryGetCommand("player.speed_multiplier", out _),
                Is.False
            );
        }

        [Test]
        public void PlayerCommands_ApplySafeRuntimeTuningAndClampMalformedValues()
        {
            GameObject player = CreateObject("ConsolePlayerTuningTest");
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            PowerSuitController controller =
                player.AddComponent<PowerSuitController>();
            PowerSuitWeapon weapon = player.AddComponent<PowerSuitWeapon>();
            InvokePrivate(health, "Awake");
            InvokePrivate(weapon, "EnsureRuntimeState");

            DeveloperConsoleGameplayCommandPack pack =
                player.AddComponent<DeveloperConsoleGameplayCommandPack>();
            pack.Configure(
                null,
                health,
                controller,
                weapon,
                null,
                null,
                null,
                null
            );
            var registry = new ConsoleCommandRegistry();
            pack.Register(registry);

            Assert.That(registry.Execute("god on").Succeeded, Is.True);
            Assert.That(health.IsGodMode, Is.True);
            Assert.That(registry.Execute("god off").Succeeded, Is.True);
            Assert.That(health.IsGodMode, Is.False);
            Assert.That(registry.Execute("invulnerable on").Succeeded, Is.True);
            Assert.That(health.IsInvulnerable, Is.True);

            Assert.That(registry.Execute("player.hp 25").Succeeded, Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(25f));
            Assert.That(registry.Execute("player.maxhp 50").Succeeded, Is.True);
            Assert.That(health.MaximumHealth, Is.EqualTo(50f));
            Assert.That(registry.Execute("heal").Succeeded, Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(50f));

            Assert.That(
                registry.Execute("player.damage_multiplier 999").Succeeded,
                Is.True
            );
            Assert.That(
                weapon.DamageMultiplier,
                Is.EqualTo(PowerSuitWeapon.MaximumDamageMultiplier)
            );
            Assert.That(
                registry.Execute("player.speed_multiplier -5").Succeeded,
                Is.True
            );
            Assert.That(
                controller.GroundSpeedMultiplier,
                Is.EqualTo(PowerSuitController.MinimumSpeedMultiplier)
            );
            Assert.That(
                registry.Execute("player.flight_speed_multiplier 999").Succeeded,
                Is.True
            );
            Assert.That(
                controller.FlightSpeedMultiplier,
                Is.EqualTo(PowerSuitController.MaximumSpeedMultiplier)
            );

            Assert.That(registry.Execute("player.hp NaN").Succeeded, Is.False);
            Assert.That(registry.Execute("god maybe").Succeeded, Is.False);
            Assert.That(registry.Execute("heal extra").Succeeded, Is.False);
        }

        [Test]
        public void AbilityCommands_ResetCooldownAndControlUltimateMeter()
        {
            GameObject player = CreateObject("ConsoleAbilityTest");
            ShoulderRocketAbility rocket =
                player.AddComponent<ShoulderRocketAbility>();
            LightningStrikeAbility lightning =
                player.AddComponent<LightningStrikeAbility>();
            VoidUltimateAbility ultimate =
                player.AddComponent<VoidUltimateAbility>();
            DeveloperConsoleGameplayCommandPack pack =
                player.AddComponent<DeveloperConsoleGameplayCommandPack>();
            pack.Configure(
                null,
                null,
                null,
                null,
                null,
                rocket,
                lightning,
                ultimate
            );
            var registry = new ConsoleCommandRegistry();
            pack.Register(registry);

            Assert.That(rocket.TryLaunch(Vector3.forward * 10f), Is.True);
            Assert.That(rocket.CooldownRemaining, Is.GreaterThan(0f));

            ConsoleCommandResult reset = registry.Execute("ability.reset");
            Assert.That(reset.Succeeded, Is.True, reset.Message);
            Assert.That(rocket.CooldownRemaining, Is.Zero);

            ConsoleCommandResult filled = registry.Execute("ultimate full");
            Assert.That(filled.Succeeded, Is.True, filled.Message);
            Assert.That(ultimate.IsMeterFull, Is.True);

            ConsoleCommandResult emptied = registry.Execute("ultimate empty");
            Assert.That(emptied.Succeeded, Is.True, emptied.Message);
            Assert.That(ultimate.MeterValue, Is.Zero);
            Assert.That(registry.Execute("ultimate invalid").Succeeded, Is.False);

            Assert.That(registry.Execute("cooldowns off").Succeeded, Is.True);
            Assert.That(rocket.CooldownsEnabled, Is.False);
            Assert.That(lightning.CooldownsEnabled, Is.False);
            Assert.That(registry.Execute("cooldowns on").Succeeded, Is.True);
            Assert.That(rocket.CooldownsEnabled, Is.True);
            Assert.That(lightning.CooldownsEnabled, Is.True);

            Assert.That(registry.Execute("rocket.damage -1").Succeeded, Is.True);
            Assert.That(rocket.ExplosionDamage, Is.Zero);
            Assert.That(registry.Execute("rocket.radius 0").Succeeded, Is.True);
            Assert.That(rocket.ExplosionRadius, Is.EqualTo(0.01f));
            Assert.That(registry.Execute("lightning.damage 123").Succeeded, Is.True);
            Assert.That(lightning.Damage, Is.EqualTo(123f));
            Assert.That(registry.Execute("lightning.radius 7.5").Succeeded, Is.True);
            Assert.That(lightning.Radius, Is.EqualTo(7.5f));
            Assert.That(registry.Execute("void.damage 42").Succeeded, Is.True);
            Assert.That(ultimate.TickDamage, Is.EqualTo(42f));
            Assert.That(registry.Execute("void.pull 9").Succeeded, Is.True);
            Assert.That(ultimate.PullImpulsePerTick, Is.EqualTo(9f));
            Assert.That(registry.Execute("rocket.damage NaN").Succeeded, Is.False);
        }

        [Test]
        public void AmmoCommand_UsesInitializedWeaponRuntimeAndValidatesSyntax()
        {
            GameObject player = CreateObject("ConsoleAmmoTest");
            PowerSuitWeapon weapon = player.AddComponent<PowerSuitWeapon>();
            MethodInfo ensureRuntime = typeof(PowerSuitWeapon).GetMethod(
                "EnsureRuntimeState",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(ensureRuntime, Is.Not.Null);
            ensureRuntime.Invoke(weapon, null);

            DeveloperConsoleGameplayCommandPack pack =
                player.AddComponent<DeveloperConsoleGameplayCommandPack>();
            pack.Configure(
                null,
                null,
                null,
                weapon,
                null,
                null,
                null,
                null
            );
            var registry = new ConsoleCommandRegistry();
            pack.Register(registry);

            ConsoleCommandResult filled = registry.Execute("ammo full");
            Assert.That(filled.Succeeded, Is.True, filled.Message);
            Assert.That(filled.Message, Does.Contain("infinite ammunition"));
            Assert.That(registry.Execute("ammo half").Succeeded, Is.False);
            Assert.That(registry.Execute("ammo").Succeeded, Is.False);
        }

        [Test]
        public void PlayerState_ReportsInjectedSubsystemsWithoutMutation()
        {
            GameObject player = CreateObject("ConsoleStateTest");
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            ShoulderRocketAbility rocket =
                player.AddComponent<ShoulderRocketAbility>();
            DeveloperConsoleGameplayCommandPack pack =
                player.AddComponent<DeveloperConsoleGameplayCommandPack>();
            pack.Configure(
                null,
                health,
                null,
                null,
                null,
                rocket,
                null,
                null
            );
            var registry = new ConsoleCommandRegistry();
            pack.Register(registry);

            ConsoleCommandResult state = registry.Execute("playerstate");

            Assert.That(state.Succeeded, Is.True, state.Message);
            Assert.That(state.Message, Does.Contain("Player HP:"));
            Assert.That(state.Message, Does.Contain("rocket"));
        }

        [Test]
        public void DemoCommands_RegisterFromSamePlayerBootstrapAndNeverSearchGlobally()
        {
            GameObject player = CreateObject("ConsoleBootstrapTest");
            PowerSuitDemoBootstrap bootstrap =
                player.AddComponent<PowerSuitDemoBootstrap>();
            DeveloperConsoleGameplayCommandPack pack =
                player.AddComponent<DeveloperConsoleGameplayCommandPack>();
            pack.Configure(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                bootstrap
            );

            // A director elsewhere in the scene must never be adopted by the
            // player-owned command pack.
            CreateObject("UnownedDirector").AddComponent<EnemySpawnDirector>();

            var registry = new ConsoleCommandRegistry();
            pack.Register(registry);

            string[] commands =
            {
                "seed",
                "spawn",
                "spawn.list",
                "killall",
                "despawnall",
                "enemy.cap",
                "enemy.spawnrate",
                "enemy.hp_multiplier",
                "enemy.damage_multiplier",
                "enemy.speed_multiplier",
                "spawner",
                "enemies"
            };
            for (int index = 0; index < commands.Length; index++)
            {
                Assert.That(
                    registry.TryGetCommand(commands[index], out _),
                    Is.True,
                    commands[index]
                );
            }

            ConsoleCommandResult unavailable = registry.Execute("spawn random 1");
            Assert.That(unavailable.Succeeded, Is.False);
            Assert.That(unavailable.Message, Does.Contain("owned demo world"));
            Assert.That(registry.Execute("seed nope").Succeeded, Is.False);
            Assert.That(registry.Execute("spawn random nope").Succeeded, Is.False);
            Assert.That(registry.Execute("enemy.cap nope").Succeeded, Is.False);

            Assert.That(registry.TryGetCommand("projectiles", out _), Is.True);
            Assert.That(registry.TryGetCommand("ai.debug", out _), Is.False);
        }

        [Test]
        public void GeneralDiagnostics_AreHonestAndValidateArity()
        {
            GameObject player = CreateObject("ConsoleDiagnosticsTest");
            DeveloperConsoleGameplayCommandPack pack =
                player.AddComponent<DeveloperConsoleGameplayCommandPack>();
            CombatFeedbackPool pool = CreateObject("ConsolePoolTest")
                .AddComponent<CombatFeedbackPool>();
            InvokePrivate(pool, "Awake");
            var registry = new ConsoleCommandRegistry();
            pack.Register(registry);

            Assert.That(registry.Execute("fps").Succeeded, Is.True);
            ConsoleCommandResult pools = registry.Execute("pools");
            Assert.That(pools.Succeeded, Is.True);
            Assert.That(pools.Message, Does.Contain("peak"));
            Assert.That(pools.Message, Does.Contain("reused"));
            Assert.That(pools.Message, Does.Contain("runtime-created"));
            Assert.That(pools.Message, Does.Contain("prewarmed"));
            ConsoleCommandResult projectiles = registry.Execute("projectiles");
            Assert.That(projectiles.Succeeded, Is.True);
            Assert.That(projectiles.Message, Does.Contain("active"));
            Assert.That(projectiles.Message, Does.Contain("peak"));

            var statistics = new StringBuilder();
            pack.AppendDeveloperStatistics(statistics);
            Assert.That(statistics.ToString(), Does.Contain("runtime-created"));
            Assert.That(statistics.ToString(), Does.Contain("peak"));
            Assert.That(statistics.ToString(), Does.Contain("projectiles"));
            Assert.That(registry.Execute("fps extra").Succeeded, Is.False);
            Assert.That(registry.Execute("pools extra").Succeeded, Is.False);
            Assert.That(registry.Execute("projectiles extra").Succeeded, Is.False);
            Assert.That(registry.TryGetCommand("reloadscene", out _), Is.True);
        }

        private static void InvokePrivate(Component target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, null);
        }

        private GameObject CreateObject(string name)
        {
            var created = new GameObject(name);
            createdObjects.Add(created);
            return created;
        }
    }
}
