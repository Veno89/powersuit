using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Powersuit.Abilities.UnityAdapters;
using Powersuit.Combat;
using Powersuit.DeveloperConsole.UnityAdapters;
using Powersuit.Enemies.UnityAdapters;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.DeveloperConsole.Integration
{
    /// <summary>
    /// Registers only gameplay commands backed by current public APIs. Commands
    /// that would require private-field reflection or pretend multipliers are
    /// deliberately omitted until their owning systems expose safe overrides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeveloperConsoleGameplayCommandPack :
        MonoBehaviour,
        IDeveloperStatisticsProvider
    {
        private const float FpsSampleWindowSeconds = 0.5f;

        [Header("Console")]
        [SerializeField] private DeveloperConsoleOverlay consoleOverlay;

        [Header("Player")]
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private PowerSuitController playerController;
        [SerializeField] private PowerSuitWeapon playerWeapon;

        [Header("Abilities")]
        [SerializeField] private PowerSuitAbilityController abilityController;
        [SerializeField] private ShoulderRocketAbility shoulderRocket;
        [SerializeField] private LightningStrikeAbility lightningStrike;
        [SerializeField] private VoidUltimateAbility voidUltimate;

        [Header("Demo World")]
        [Tooltip("Resolved only from this player; no global scene search is used.")]
        [SerializeField] private PowerSuitDemoBootstrap demoBootstrap;

        private float fpsSampleElapsed;
        private int fpsSampleFrames;
        private float sampledFps;
        private float sampledFrameMilliseconds;

        public DeveloperConsoleOverlay ConsoleOverlay => consoleOverlay;
        public PlayerHealth PlayerHealth => playerHealth;
        public PowerSuitController PlayerController => playerController;
        public PowerSuitWeapon PlayerWeapon => playerWeapon;
        public PowerSuitAbilityController AbilityController => abilityController;
        public ShoulderRocketAbility ShoulderRocket => shoulderRocket;
        public LightningStrikeAbility LightningStrike => lightningStrike;
        public VoidUltimateAbility VoidUltimate => voidUltimate;
        public PowerSuitDemoBootstrap DemoBootstrap => demoBootstrap;

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta))
            {
                return;
            }

            fpsSampleElapsed += delta;
            fpsSampleFrames++;
            if (fpsSampleElapsed < FpsSampleWindowSeconds)
            {
                return;
            }

            sampledFps = fpsSampleFrames / fpsSampleElapsed;
            sampledFrameMilliseconds = fpsSampleElapsed * 1000f / fpsSampleFrames;
            fpsSampleElapsed = 0f;
            fpsSampleFrames = 0;
        }

        private void Start()
        {
            Register();
        }

        private void OnDestroy()
        {
            if (consoleOverlay != null)
            {
                consoleOverlay.UnregisterStatisticsProvider(this);
            }
        }

        /// <summary>
        /// Explicit scene/generator wiring boundary. Any null gameplay reference
        /// can still be resolved from this GameObject when Register is called.
        /// </summary>
        public void Configure(
            DeveloperConsoleOverlay overlay,
            PlayerHealth health,
            PowerSuitController controller,
            PowerSuitWeapon weapon,
            PowerSuitAbilityController abilities,
            ShoulderRocketAbility rocket,
            LightningStrikeAbility lightning,
            VoidUltimateAbility ultimate
        )
        {
            consoleOverlay = overlay;
            playerHealth = health;
            playerController = controller;
            playerWeapon = weapon;
            abilityController = abilities;
            shoulderRocket = rocket;
            lightningStrike = lightning;
            voidUltimate = ultimate;
            ResolveAbilityReferences();
        }

        /// <summary>
        /// Extended configuration overload for a generated demo player. The
        /// original overload remains source-compatible with existing wiring.
        /// </summary>
        public void Configure(
            DeveloperConsoleOverlay overlay,
            PlayerHealth health,
            PowerSuitController controller,
            PowerSuitWeapon weapon,
            PowerSuitAbilityController abilities,
            ShoulderRocketAbility rocket,
            LightningStrikeAbility lightning,
            VoidUltimateAbility ultimate,
            PowerSuitDemoBootstrap bootstrap
        )
        {
            Configure(
                overlay,
                health,
                controller,
                weapon,
                abilities,
                rocket,
                lightning,
                ultimate
            );
            demoBootstrap = bootstrap;
        }

        public void ConfigureDemoBootstrap(PowerSuitDemoBootstrap bootstrap)
        {
            demoBootstrap = bootstrap;
        }

        /// <summary>
        /// Registers with the configured overlay. The operation is idempotent:
        /// commands already owned by the registry are left untouched.
        /// </summary>
        public int Register()
        {
            CacheLocalReferences();
            if (consoleOverlay == null)
            {
                return 0;
            }

            int count = Register(consoleOverlay.Registry);
            consoleOverlay.RegisterStatisticsProvider(this);
            return count;
        }

        public int Register(DeveloperConsoleOverlay overlay)
        {
            consoleOverlay = overlay;
            return Register();
        }

        /// <summary>
        /// Registry-level overload for tests and non-MonoBehaviour bootstrap
        /// code. It never initializes an overlay or changes cursor state.
        /// </summary>
        public int Register(ConsoleCommandRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            CacheLocalReferences();
            int registered = 0;

            registered += TryRegister(
                registry,
                "reloadscene",
                "reloadscene",
                "Reloads the active scene when it is present in Build Settings.",
                ReloadScene
            );
            registered += TryRegister(
                registry,
                "fps",
                "fps",
                "Prints the command pack's recent unscaled frame-rate sample.",
                PrintFps
            );
            registered += TryRegister(
                registry,
                "pools",
                "pools",
                "Prints aggregate active and inactive combat-pool counts.",
                PrintPools
            );
            registered += TryRegister(
                registry,
                "projectiles",
                "projectiles",
                "Prints active and peak pooled physical-projectile counts.",
                PrintProjectiles
            );

            if (playerHealth != null)
            {
                registered += TryRegisterBoolean(
                    registry,
                    "god",
                    "Enables damage immunity and restores health.",
                    SetGodMode
                );
                registered += TryRegisterBoolean(
                    registry,
                    "invulnerable",
                    "Enables damage immunity without changing health.",
                    SetInvulnerable
                );
                registered += TryRegister(
                    registry,
                    "heal",
                    "heal",
                    "Restores player health to its current maximum.",
                    HealPlayer
                );
                registered += TryRegister(
                    registry,
                    "killplayer",
                    "killplayer",
                    "Defeats the player through PlayerHealth's normal lifecycle.",
                    KillPlayer
                );
                registered += TryRegister(
                    registry,
                    "player.hp",
                    "player.hp <value>",
                    "Sets current health, clamped to zero and current maximum.",
                    SetPlayerHealth
                );
                registered += TryRegisterClampedFloat(
                    registry,
                    "player.maxhp",
                    "value",
                    "Sets the player health ceiling.",
                    PlayerHealth.MinimumMaximumHealth,
                    PlayerHealth.MaximumMaximumHealth,
                    SetPlayerMaximumHealth
                );
            }

            if (playerWeapon != null)
            {
                registered += TryRegister(
                    registry,
                    "ammo",
                    "ammo full",
                    "Fills the current magazine and reserve ammunition.",
                    FillAmmunition
                );
                registered += TryRegisterClampedFloat(
                    registry,
                    "player.damage_multiplier",
                    "value",
                    "Scales outgoing player-weapon damage.",
                    PowerSuitWeapon.MinimumDamageMultiplier,
                    PowerSuitWeapon.MaximumDamageMultiplier,
                    SetPlayerDamageMultiplier
                );
            }

            if (playerController != null)
            {
                registered += TryRegisterClampedFloat(
                    registry,
                    "player.speed_multiplier",
                    "value",
                    "Scales ground movement speed.",
                    PowerSuitController.MinimumSpeedMultiplier,
                    PowerSuitController.MaximumSpeedMultiplier,
                    SetGroundSpeedMultiplier
                );
                registered += TryRegisterClampedFloat(
                    registry,
                    "player.flight_speed_multiplier",
                    "value",
                    "Scales normal flight, boost, and vertical flight speed.",
                    PowerSuitController.MinimumSpeedMultiplier,
                    PowerSuitController.MaximumSpeedMultiplier,
                    SetFlightSpeedMultiplier
                );
            }

            if (HasAnyAbilityReference())
            {
                registered += TryRegister(
                    registry,
                    "ability.reset",
                    "ability.reset",
                    "Resets ability cooldowns and targeting without clearing ultimate meter.",
                    ResetAbilities
                );
            }

            if (
                abilityController != null ||
                shoulderRocket != null ||
                lightningStrike != null
            )
            {
                registered += TryRegisterBoolean(
                    registry,
                    "cooldowns",
                    "Enables or bypasses rocket and lightning cooldowns.",
                    SetCooldownsEnabled
                );
            }

            if (shoulderRocket != null)
            {
                registered += TryRegisterClampedFloat(
                    registry,
                    "rocket.damage",
                    "value",
                    "Sets shoulder-rocket explosion damage.",
                    0f,
                    ShoulderRocketAbility.MaximumTunableDamage,
                    SetRocketDamage
                );
                registered += TryRegisterClampedFloat(
                    registry,
                    "rocket.radius",
                    "value",
                    "Sets shoulder-rocket explosion radius.",
                    0.01f,
                    ShoulderRocketAbility.MaximumTunableRadius,
                    SetRocketRadius
                );
            }

            if (lightningStrike != null)
            {
                registered += TryRegisterClampedFloat(
                    registry,
                    "lightning.damage",
                    "value",
                    "Sets lightning-strike area damage.",
                    0f,
                    LightningStrikeAbility.MaximumTunableDamage,
                    SetLightningDamage
                );
                registered += TryRegisterClampedFloat(
                    registry,
                    "lightning.radius",
                    "value",
                    "Sets lightning-strike area radius.",
                    0.01f,
                    LightningStrikeAbility.MaximumTunableRadius,
                    SetLightningRadius
                );
            }

            if (voidUltimate != null)
            {
                registered += TryRegister(
                    registry,
                    "ultimate",
                    "ultimate <full|empty>",
                    "Fills or clears the ultimate meter through its public adapter.",
                    SetUltimateMeter
                );
                registered += TryRegisterClampedFloat(
                    registry,
                    "void.damage",
                    "value",
                    "Sets periodic void-field damage.",
                    0f,
                    VoidUltimateAbility.MaximumTunableDamage,
                    SetVoidDamage
                );
                registered += TryRegisterClampedFloat(
                    registry,
                    "void.pull",
                    "value",
                    "Sets void-field pull impulse per tick.",
                    0f,
                    VoidUltimateAbility.MaximumTunableImpulse,
                    SetVoidPull
                );
            }

            if (demoBootstrap != null)
            {
                registered += RegisterDemoWorldCommands(registry);
            }

            if (HasAnyGameplayReference())
            {
                registered += TryRegister(
                    registry,
                    "playerstate",
                    "playerstate",
                    "Prints current player, weapon, and ability state.",
                    PrintPlayerState
                );
            }

            return registered;
        }

        public void AppendDeveloperStatistics(StringBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (playerHealth != null)
            {
                builder.Append("Player HP: ");
                AppendFloat(builder, playerHealth.CurrentHealth);
                builder.Append('/');
                AppendFloat(builder, playerHealth.MaximumHealth);
                if (playerHealth.IsDefeated)
                {
                    builder.Append(" (defeated)");
                }
            }

            if (playerController != null)
            {
                AppendLineSeparator(builder);
                builder.Append("Move: ");
                AppendFloat(builder, playerController.MovementSpeedNormalized);
                builder.Append("  vertical ");
                AppendFloat(builder, playerController.VerticalSpeed);
                builder.Append("  ");
                builder.Append(DescribeMovementState());
            }

            if (playerWeapon != null)
            {
                AppendLineSeparator(builder);
                builder.Append("Weapon: ");
                builder.Append(playerWeapon.CurrentMagazineAmmo);
                builder.Append(" mag / ");
                builder.Append(playerWeapon.ReserveAmmo);
                builder.Append(" reserve  ");
                builder.Append(DescribeWeaponState());
            }

            if (HasAnyAbilityReference())
            {
                AppendLineSeparator(builder);
                builder.Append("Abilities: ");
                AppendAbilitySummary(builder);
            }

            EnemySpawnDirector director = ResolveSpawnDirector();
            if (director != null && director.IsInitialized)
            {
                AppendLineSeparator(builder);
                builder.Append("Enemies: ");
                builder.Append(director.ActiveInstanceCount);
                builder.Append(" active / ");
                builder.Append(director.PendingSpawnCount);
                builder.Append(" pending / cap ");
                builder.Append(director.ActiveEnemyCap);
            }

            if (CombatFeedbackPool.TryGetStatistics(out CombatFeedbackPool.Statistics pool))
            {
                AppendLineSeparator(builder);
                builder.Append("Pool: ");
                builder.Append(pool.ActiveCount);
                builder.Append(" active / ");
                builder.Append(pool.InactiveCount);
                builder.Append(" inactive / peak ");
                builder.Append(pool.PeakActiveCount);
                builder.Append(" / runtime-created ");
                builder.Append(pool.RuntimeInstantiationCount);
                builder.Append(" / projectiles ");
                builder.Append(pool.ActiveProjectileCount);
            }
        }

        private ConsoleCommandResult ReloadScene(IReadOnlyList<string> arguments)
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: reloadscene");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return ConsoleCommandResult.Error("No loaded active scene is available.");
            }

            if (activeScene.buildIndex < 0)
            {
                return ConsoleCommandResult.Error(
                    "The active scene is not present in Build Settings."
                );
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(
                activeScene.buildIndex,
                LoadSceneMode.Single
            );
            return operation != null
                ? ConsoleCommandResult.Success(
                    $"Reloading scene '{activeScene.name}'."
                )
                : ConsoleCommandResult.Error("Unity rejected the scene reload request.");
        }

        private ConsoleCommandResult PrintFps(IReadOnlyList<string> arguments)
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: fps");
            }

            float fps = sampledFps;
            float milliseconds = sampledFrameMilliseconds;
            float currentDelta = Time.unscaledDeltaTime;
            if (fps <= 0f && currentDelta > 0f && IsFinite(currentDelta))
            {
                fps = 1f / currentDelta;
                milliseconds = currentDelta * 1000f;
            }

            if (fps <= 0f)
            {
                return ConsoleCommandResult.Information(
                    "FPS sample is warming up; try again after half a second."
                );
            }

            return ConsoleCommandResult.Information(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "FPS: {0:0.0} ({1:0.00} ms sampled frame time).",
                    fps,
                    milliseconds
                )
            );
        }

        private static ConsoleCommandResult PrintPools(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: pools");
            }

            if (!CombatFeedbackPool.TryGetStatistics(out CombatFeedbackPool.Statistics pool))
            {
                return ConsoleCommandResult.Information(
                    "Combat pool has not been created yet: 0 active, 0 inactive."
                );
            }

            return ConsoleCommandResult.Information(
                $"Combat pool: {pool.PrefabPoolCount} prefab pools, " +
                $"{pool.ActiveCount} active, {pool.InactiveCount} inactive, " +
                $"peak {pool.PeakActiveCount}; {pool.SpawnCount} spawns, " +
                $"{pool.ActiveProjectileCount} projectiles active " +
                $"(peak {pool.PeakActiveProjectileCount}), " +
                $"{pool.ReusedSpawnCount} reused, " +
                $"{pool.RuntimeInstantiationCount} runtime-created, " +
                $"{pool.PrewarmedInstantiationCount} prewarmed, " +
                $"{pool.RecycleCount} recycled, " +
                $"{pool.DestroyedUntrackedCount} untracked destroyed."
            );
        }

        private static ConsoleCommandResult PrintProjectiles(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: projectiles");
            }

            if (!CombatFeedbackPool.TryGetStatistics(out CombatFeedbackPool.Statistics pool))
            {
                return ConsoleCommandResult.Information(
                    "Physical pooled projectiles: 0 active, peak 0."
                );
            }

            return ConsoleCommandResult.Information(
                $"Physical pooled projectiles: {pool.ActiveProjectileCount} active, " +
                $"peak {pool.PeakActiveProjectileCount}."
            );
        }

        private ConsoleCommandResult SetGodMode(bool enabled)
        {
            if (playerHealth == null)
            {
                return ConsoleCommandResult.Error("Player health is unavailable.");
            }

            playerHealth.SetGodMode(enabled);
            return ConsoleCommandResult.Success(
                $"God mode {(enabled ? "enabled" : "disabled")}."
            );
        }

        private ConsoleCommandResult SetInvulnerable(bool enabled)
        {
            if (playerHealth == null)
            {
                return ConsoleCommandResult.Error("Player health is unavailable.");
            }

            playerHealth.SetInvulnerable(enabled);
            return ConsoleCommandResult.Success(
                $"Invulnerability {(enabled ? "enabled" : "disabled")}."
            );
        }

        private ConsoleCommandResult HealPlayer(IReadOnlyList<string> arguments)
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: heal");
            }

            if (playerHealth == null)
            {
                return ConsoleCommandResult.Error("Player health is unavailable.");
            }

            if (playerHealth.IsDefeated)
            {
                return ConsoleCommandResult.Error(
                    "A defeated player must complete the normal respawn lifecycle."
                );
            }

            float restored = playerHealth.HealToFull();
            return ConsoleCommandResult.Success(
                restored > 0f
                    ? FormatFloatMessage("Restored {0:0.###} health.", restored)
                    : "Player health is already full."
            );
        }

        private ConsoleCommandResult SetPlayerHealth(
            IReadOnlyList<string> arguments
        )
        {
            if (!TryReadFiniteFloat(arguments, "player.hp <value>", out float value, out string error))
            {
                return ConsoleCommandResult.Error(error);
            }

            if (playerHealth == null)
            {
                return ConsoleCommandResult.Error("Player health is unavailable.");
            }

            if (playerHealth.IsDefeated)
            {
                return ConsoleCommandResult.Error(
                    "Current health cannot be changed during respawn."
                );
            }

            float effective = playerHealth.SetCurrentHealth(value);
            return TunedFloatResult("Player health", value, effective);
        }

        private ConsoleCommandResult SetPlayerMaximumHealth(float value)
        {
            if (playerHealth == null)
            {
                return ConsoleCommandResult.Error("Player health is unavailable.");
            }

            float effective = playerHealth.SetMaximumHealth(value);
            return TunedFloatResult("Player maximum health", value, effective);
        }

        private ConsoleCommandResult SetPlayerDamageMultiplier(float value)
        {
            if (playerWeapon == null)
            {
                return ConsoleCommandResult.Error("Player weapon is unavailable.");
            }

            float effective = playerWeapon.SetDamageMultiplier(value);
            return TunedFloatResult("Player damage multiplier", value, effective);
        }

        private ConsoleCommandResult SetGroundSpeedMultiplier(float value)
        {
            if (playerController == null)
            {
                return ConsoleCommandResult.Error("Player controller is unavailable.");
            }

            float effective = playerController.SetGroundSpeedMultiplier(value);
            return TunedFloatResult("Ground speed multiplier", value, effective);
        }

        private ConsoleCommandResult SetFlightSpeedMultiplier(float value)
        {
            if (playerController == null)
            {
                return ConsoleCommandResult.Error("Player controller is unavailable.");
            }

            float effective = playerController.SetFlightSpeedMultiplier(value);
            return TunedFloatResult("Flight speed multiplier", value, effective);
        }

        private ConsoleCommandResult KillPlayer(IReadOnlyList<string> arguments)
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: killplayer");
            }

            if (playerHealth == null)
            {
                return ConsoleCommandResult.Error("Player health is unavailable.");
            }

            if (playerHealth.IsDefeated || playerHealth.CurrentHealth <= 0f)
            {
                return ConsoleCommandResult.Error("The player is already defeated.");
            }

            return playerHealth.Kill()
                ? ConsoleCommandResult.Success(
                    "Player defeated; normal respawn remains active."
                )
                : ConsoleCommandResult.Error("Player defeat was not accepted.");
        }

        private ConsoleCommandResult FillAmmunition(
            IReadOnlyList<string> arguments
        )
        {
            if (
                arguments.Count != 1 ||
                !string.Equals(
                    arguments[0],
                    "full",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ConsoleCommandResult.Error("Usage: ammo full");
            }

            if (playerWeapon == null)
            {
                return ConsoleCommandResult.Error("Player weapon is unavailable.");
            }

            WeaponRuntimeState state = playerWeapon.RuntimeState;
            WeaponRuntimeConfig configuration = playerWeapon.ActiveConfiguration;
            if (state == null || configuration == null)
            {
                return ConsoleCommandResult.Error(
                    "The weapon runtime has not been initialized."
                );
            }

            if (configuration.UsesInfiniteAmmo)
            {
                return ConsoleCommandResult.Success(
                    "The equipped weapon already uses infinite ammunition."
                );
            }

            state.Reset();
            playerWeapon.AddReserveAmmo(configuration.MaximumReserveAmmo);
            return ConsoleCommandResult.Success(
                $"Ammo filled: {state.CurrentMagazineAmmo}/{configuration.MagazineCapacity} magazine, " +
                $"{state.CurrentReserveAmmo}/{configuration.MaximumReserveAmmo} reserve."
            );
        }

        private ConsoleCommandResult ResetAbilities(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: ability.reset");
            }

            bool resetAny = false;
            if (abilityController != null)
            {
                abilityController.ResetAbilities(clearUltimateMeter: false);
                resetAny = true;
            }

            if (
                shoulderRocket != null &&
                (abilityController == null ||
                 shoulderRocket != abilityController.ShoulderRocket)
            )
            {
                shoulderRocket.ResetAbility();
                resetAny = true;
            }

            if (
                lightningStrike != null &&
                (abilityController == null ||
                 lightningStrike != abilityController.LightningStrike)
            )
            {
                lightningStrike.ResetAbility();
                resetAny = true;
            }

            if (
                voidUltimate != null &&
                (abilityController == null ||
                 voidUltimate != abilityController.VoidUltimate)
            )
            {
                voidUltimate.ResetAbility(clearMeter: false);
                resetAny = true;
            }

            return resetAny
                ? ConsoleCommandResult.Success(
                    "Ability cooldowns and targeting reset; ultimate meter preserved."
                )
                : ConsoleCommandResult.Error("No ability adapters are available.");
        }

        private ConsoleCommandResult SetCooldownsEnabled(bool enabled)
        {
            bool changedAny = false;
            if (abilityController != null)
            {
                abilityController.SetCooldownsEnabled(enabled);
                changedAny = true;
            }

            if (
                shoulderRocket != null &&
                (abilityController == null ||
                 shoulderRocket != abilityController.ShoulderRocket)
            )
            {
                shoulderRocket.SetCooldownsEnabled(enabled);
                changedAny = true;
            }

            if (
                lightningStrike != null &&
                (abilityController == null ||
                 lightningStrike != abilityController.LightningStrike)
            )
            {
                lightningStrike.SetCooldownsEnabled(enabled);
                changedAny = true;
            }

            return changedAny
                ? ConsoleCommandResult.Success(
                    $"Ability cooldowns {(enabled ? "enabled" : "bypassed")}."
                )
                : ConsoleCommandResult.Error(
                    "No cooldown-based ability adapters are available."
                );
        }

        private ConsoleCommandResult SetRocketDamage(float value)
        {
            if (shoulderRocket == null)
            {
                return ConsoleCommandResult.Error("Shoulder rocket is unavailable.");
            }

            return TunedFloatResult(
                "Rocket damage",
                value,
                shoulderRocket.SetExplosionDamage(value)
            );
        }

        private ConsoleCommandResult SetRocketRadius(float value)
        {
            if (shoulderRocket == null)
            {
                return ConsoleCommandResult.Error("Shoulder rocket is unavailable.");
            }

            return TunedFloatResult(
                "Rocket radius",
                value,
                shoulderRocket.SetExplosionRadius(value)
            );
        }

        private ConsoleCommandResult SetLightningDamage(float value)
        {
            if (lightningStrike == null)
            {
                return ConsoleCommandResult.Error("Lightning strike is unavailable.");
            }

            return TunedFloatResult(
                "Lightning damage",
                value,
                lightningStrike.SetDamage(value)
            );
        }

        private ConsoleCommandResult SetLightningRadius(float value)
        {
            if (lightningStrike == null)
            {
                return ConsoleCommandResult.Error("Lightning strike is unavailable.");
            }

            return TunedFloatResult(
                "Lightning radius",
                value,
                lightningStrike.SetRadius(value)
            );
        }

        private ConsoleCommandResult SetVoidDamage(float value)
        {
            if (voidUltimate == null)
            {
                return ConsoleCommandResult.Error("Void ultimate is unavailable.");
            }

            return TunedFloatResult(
                "Void tick damage",
                value,
                voidUltimate.SetDamage(value)
            );
        }

        private ConsoleCommandResult SetVoidPull(float value)
        {
            if (voidUltimate == null)
            {
                return ConsoleCommandResult.Error("Void ultimate is unavailable.");
            }

            return TunedFloatResult(
                "Void pull impulse",
                value,
                voidUltimate.SetPullImpulsePerTick(value)
            );
        }

        private ConsoleCommandResult SetUltimateMeter(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 1)
            {
                return ConsoleCommandResult.Error("Usage: ultimate <full|empty>");
            }

            if (voidUltimate == null)
            {
                return ConsoleCommandResult.Error("The ultimate adapter is unavailable.");
            }

            if (string.Equals(arguments[0], "full", StringComparison.OrdinalIgnoreCase))
            {
                if (voidUltimate.IsActive)
                {
                    return ConsoleCommandResult.Error(
                        "The ultimate meter cannot be filled while its field is active."
                    );
                }

                voidUltimate.FillMeter();
                return ConsoleCommandResult.Success("Ultimate meter filled.");
            }

            if (string.Equals(arguments[0], "empty", StringComparison.OrdinalIgnoreCase))
            {
                voidUltimate.ResetAbility(clearMeter: true);
                return ConsoleCommandResult.Success(
                    "Ultimate meter cleared and any active field cancelled."
                );
            }

            return ConsoleCommandResult.Error("Usage: ultimate <full|empty>");
        }

        private int RegisterDemoWorldCommands(ConsoleCommandRegistry registry)
        {
            int registered = 0;
            registered += TryRegister(
                registry,
                "seed",
                "seed <integer>",
                "Resets the owned spawn director with a deterministic seed.",
                ResetSpawnSeed
            );
            registered += TryRegister(
                registry,
                "spawn",
                "spawn <archetype|random> <count>",
                "Immediately spawns configured enemies within fairness and cap rules.",
                SpawnEnemies
            );
            registered += TryRegister(
                registry,
                "spawn.list",
                "spawn.list",
                "Lists stable archetype ids configured on the owned director.",
                ListSpawnArchetypes
            );
            registered += TryRegister(
                registry,
                "killall",
                "killall",
                "Defeats every active enemy through its death lifecycle.",
                KillAllEnemies
            );
            registered += TryRegister(
                registry,
                "despawnall",
                "despawnall",
                "Immediately recycles all active and pending enemies.",
                DespawnAllEnemies
            );
            registered += TryRegisterClampedInteger(
                registry,
                "enemy.cap",
                "count",
                "Sets the active-enemy cap.",
                EnemySpawnDirector.MinimumActiveEnemyCap,
                EnemySpawnDirector.MaximumActiveEnemyCap,
                SetEnemyCap
            );
            registered += TryRegisterClampedFloat(
                registry,
                "enemy.spawnrate",
                "seconds",
                "Sets seconds between automatic spawn cycles.",
                EnemySpawnDirector.MinimumSpawnIntervalSeconds,
                EnemySpawnDirector.MaximumSpawnIntervalSeconds,
                SetEnemySpawnRate
            );
            registered += TryRegisterClampedFloat(
                registry,
                "enemy.hp_multiplier",
                "value",
                "Scales health for active and future enemies.",
                EnemyArchetypeController.MinimumHealthMultiplier,
                EnemyArchetypeController.MaximumHealthMultiplier,
                SetEnemyHealthMultiplier
            );
            registered += TryRegisterClampedFloat(
                registry,
                "enemy.damage_multiplier",
                "value",
                "Scales outgoing damage for active and future enemies.",
                EnemyArchetypeController.MinimumDamageMultiplier,
                EnemyArchetypeController.MaximumDamageMultiplier,
                SetEnemyDamageMultiplier
            );
            registered += TryRegisterClampedFloat(
                registry,
                "enemy.speed_multiplier",
                "value",
                "Scales movement speed for active and future enemies.",
                EnemyArchetypeController.MinimumSpeedMultiplier,
                EnemyArchetypeController.MaximumSpeedMultiplier,
                SetEnemySpeedMultiplier
            );
            registered += TryRegisterBoolean(
                registry,
                "spawner",
                "Enables or disables automatic spawn cycles.",
                SetSpawnerEnabled
            );
            registered += TryRegister(
                registry,
                "enemies",
                "enemies",
                "Prints owned spawn-director counts and tuning state.",
                PrintEnemies
            );
            return registered;
        }

        private ConsoleCommandResult ResetSpawnSeed(
            IReadOnlyList<string> arguments
        )
        {
            if (
                arguments.Count != 1 ||
                !ConsoleCommandRegistry.TryParseInteger(arguments[0], out int seed)
            )
            {
                return ConsoleCommandResult.Error("Usage: seed <integer>");
            }

            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            uint unsignedSeed = unchecked((uint)seed);
            director.ResetDirectorWithSeed(
                unsignedSeed,
                clearExistingEnemies: true,
                shouldSpawnImmediately: true
            );
            return ConsoleCommandResult.Success(
                $"Spawn seed reset to {seed} (0x{unsignedSeed:X8}); " +
                "existing enemies were recycled."
            );
        }

        private ConsoleCommandResult SpawnEnemies(
            IReadOnlyList<string> arguments
        )
        {
            if (
                arguments.Count != 2 ||
                !ConsoleCommandRegistry.TryParseInteger(arguments[1], out int requested)
            )
            {
                return ConsoleCommandResult.Error(
                    "Usage: spawn <archetype|random> <count>"
                );
            }

            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            int count = Mathf.Clamp(
                requested,
                1,
                EnemySpawnDirector.MaximumActiveEnemyCap
            );
            string archetype = arguments[0];
            bool random = string.Equals(
                archetype,
                "random",
                StringComparison.OrdinalIgnoreCase
            );
            if (!random && !HasSpawnArchetype(director, archetype))
            {
                return ConsoleCommandResult.Error(
                    $"Unknown spawn archetype '{archetype}'. Enter 'spawn.list'."
                );
            }

            int spawned = random
                ? director.SpawnRandom(count)
                : director.SpawnArchetype(archetype, count);
            string clampNotice = requested == count
                ? string.Empty
                : $" Requested count {requested} was clamped to {count}.";
            if (spawned <= 0)
            {
                return ConsoleCommandResult.Error(
                    "No enemies spawned; active cap, safe-radius, or visibility " +
                    "rules currently block the request." + clampNotice
                );
            }

            return ConsoleCommandResult.Success(
                $"Spawned {spawned} of {count} " +
                $"{(random ? "random enemies" : archetype)}.{clampNotice}"
            );
        }

        private ConsoleCommandResult ListSpawnArchetypes(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: spawn.list");
            }

            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            if (director.SpawnEntryCount <= 0)
            {
                return ConsoleCommandResult.Error(
                    "The owned director has no configured spawn archetypes."
                );
            }

            var builder = new StringBuilder(192);
            builder.Append("Spawn archetypes:");
            for (int index = 0; index < director.SpawnEntryCount; index++)
            {
                builder.Append(index == 0 ? " " : ", ");
                builder.Append(director.GetSpawnArchetypeId(index));
            }
            return ConsoleCommandResult.Information(builder.ToString());
        }

        private ConsoleCommandResult KillAllEnemies(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: killall");
            }

            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            int killed = director.KillAllActiveEnemies();
            return ConsoleCommandResult.Success($"Defeated {killed} active enemies.");
        }

        private ConsoleCommandResult DespawnAllEnemies(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: despawnall");
            }

            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            int removed = director.DespawnAllEnemies();
            return ConsoleCommandResult.Success(
                $"Recycled {removed} active or pending enemies."
            );
        }

        private ConsoleCommandResult SetEnemyCap(int value)
        {
            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            int effective = director.SetActiveEnemyCap(value);
            return ConsoleCommandResult.Success(
                $"Enemy cap set to {effective}."
            );
        }

        private ConsoleCommandResult SetEnemySpawnRate(float value)
        {
            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            return TunedFloatResult(
                "Enemy spawn interval",
                value,
                director.SetSpawnIntervalSeconds(value),
                "seconds"
            );
        }

        private ConsoleCommandResult SetEnemyHealthMultiplier(float value)
        {
            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            return TunedFloatResult(
                "Enemy health multiplier",
                value,
                director.SetEnemyHealthMultiplier(value)
            );
        }

        private ConsoleCommandResult SetEnemyDamageMultiplier(float value)
        {
            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            return TunedFloatResult(
                "Enemy damage multiplier",
                value,
                director.SetEnemyDamageMultiplier(value)
            );
        }

        private ConsoleCommandResult SetEnemySpeedMultiplier(float value)
        {
            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            return TunedFloatResult(
                "Enemy speed multiplier",
                value,
                director.SetEnemySpeedMultiplier(value)
            );
        }

        private ConsoleCommandResult SetSpawnerEnabled(bool enabled)
        {
            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            director.SetDirectorEnabled(enabled);
            return ConsoleCommandResult.Success(
                $"Automatic spawner {(enabled ? "enabled" : "disabled")}."
            );
        }

        private ConsoleCommandResult PrintEnemies(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: enemies");
            }

            if (!TryResolveSpawnDirector(out EnemySpawnDirector director, out ConsoleCommandResult error))
            {
                return error;
            }

            return ConsoleCommandResult.Information(
                $"Enemies: {director.ActiveInstanceCount} active, " +
                $"{director.PendingSpawnCount} pending, " +
                $"{director.ReservedEnemyCount} reserved, cap {director.ActiveEnemyCap}; " +
                $"spawner {(director.IsDirectorEnabled ? "on" : "off")}, " +
                $"interval {director.SpawnIntervalSeconds:0.###}s; " +
                $"HP x{director.EnemyHealthMultiplier:0.###}, " +
                $"damage x{director.EnemyDamageMultiplier:0.###}, " +
                $"speed x{director.EnemySpeedMultiplier:0.###}."
            );
        }

        private ConsoleCommandResult PrintPlayerState(
            IReadOnlyList<string> arguments
        )
        {
            if (arguments.Count != 0)
            {
                return ConsoleCommandResult.Error("Usage: playerstate");
            }

            var builder = new StringBuilder(256);
            AppendDeveloperStatistics(builder);
            return ConsoleCommandResult.Information(builder.ToString());
        }

        private void CacheLocalReferences()
        {
            consoleOverlay ??= GetComponent<DeveloperConsoleOverlay>();
            playerHealth ??= GetComponent<PlayerHealth>();
            playerController ??= GetComponent<PowerSuitController>();
            playerWeapon ??= GetComponent<PowerSuitWeapon>();
            abilityController ??= GetComponent<PowerSuitAbilityController>();
            shoulderRocket ??= GetComponent<ShoulderRocketAbility>();
            lightningStrike ??= GetComponent<LightningStrikeAbility>();
            voidUltimate ??= GetComponent<VoidUltimateAbility>();
            demoBootstrap ??= GetComponent<PowerSuitDemoBootstrap>();
            ResolveAbilityReferences();
        }

        private void ResolveAbilityReferences()
        {
            if (abilityController == null)
            {
                return;
            }

            shoulderRocket ??= abilityController.ShoulderRocket;
            lightningStrike ??= abilityController.LightningStrike;
            voidUltimate ??= abilityController.VoidUltimate;
        }

        private bool HasAnyGameplayReference()
        {
            return playerHealth != null ||
                   playerController != null ||
                   playerWeapon != null ||
                   HasAnyAbilityReference() ||
                   demoBootstrap != null;
        }

        private bool HasAnyAbilityReference()
        {
            return abilityController != null ||
                   shoulderRocket != null ||
                   lightningStrike != null ||
                   voidUltimate != null;
        }

        private string DescribeMovementState()
        {
            if (playerController == null)
            {
                return "unavailable";
            }

            if (playerController.IsBoosting)
            {
                return "boost";
            }

            if (playerController.IsFlying)
            {
                return "flight";
            }

            return playerController.IsGrounded ? "grounded" : "airborne";
        }

        private string DescribeWeaponState()
        {
            if (playerWeapon == null)
            {
                return "unavailable";
            }

            if (playerWeapon.IsReloading)
            {
                return "reloading";
            }

            if (playerWeapon.IsCycling)
            {
                return "cycling";
            }

            return playerWeapon.CanFire
                ? "ready"
                : playerWeapon.CurrentFireBlockReason.ToString();
        }

        private void AppendAbilitySummary(StringBuilder builder)
        {
            if (shoulderRocket != null)
            {
                builder.Append("rocket ");
                AppendFloat(builder, shoulderRocket.CooldownRemaining);
                builder.Append("s");
            }

            if (lightningStrike != null)
            {
                AppendInlineSeparator(builder, shoulderRocket != null);
                builder.Append("lightning ");
                AppendFloat(builder, lightningStrike.CooldownRemaining);
                builder.Append("s");
                if (lightningStrike.IsTargeting)
                {
                    builder.Append(" targeting");
                }
            }

            if (voidUltimate != null)
            {
                AppendInlineSeparator(
                    builder,
                    shoulderRocket != null || lightningStrike != null
                );
                builder.Append("ultimate ");
                AppendFloat(builder, voidUltimate.MeterNormalized * 100f);
                builder.Append('%');
                if (voidUltimate.IsActive)
                {
                    builder.Append(" active");
                }
            }
        }

        private static int TryRegister(
            ConsoleCommandRegistry registry,
            string name,
            string usage,
            string description,
            ConsoleCommandHandler handler
        )
        {
            if (registry.TryGetCommand(name, out _))
            {
                return 0;
            }

            registry.Register(name, usage, description, handler);
            return 1;
        }

        private static int TryRegisterBoolean(
            ConsoleCommandRegistry registry,
            string name,
            string description,
            Func<bool, ConsoleCommandResult> handler
        )
        {
            if (registry.TryGetCommand(name, out _))
            {
                return 0;
            }

            registry.RegisterBoolean(name, description, handler);
            return 1;
        }

        private static int TryRegisterClampedFloat(
            ConsoleCommandRegistry registry,
            string name,
            string valueName,
            string description,
            float minimum,
            float maximum,
            Func<float, ConsoleCommandResult> handler
        )
        {
            if (registry.TryGetCommand(name, out _))
            {
                return 0;
            }

            registry.RegisterClampedFloat(
                name,
                valueName,
                description,
                minimum,
                maximum,
                handler
            );
            return 1;
        }

        private static int TryRegisterClampedInteger(
            ConsoleCommandRegistry registry,
            string name,
            string valueName,
            string description,
            int minimum,
            int maximum,
            Func<int, ConsoleCommandResult> handler
        )
        {
            if (registry.TryGetCommand(name, out _))
            {
                return 0;
            }

            registry.RegisterClampedInteger(
                name,
                valueName,
                description,
                minimum,
                maximum,
                handler
            );
            return 1;
        }

        private EnemySpawnDirector ResolveSpawnDirector()
        {
            return demoBootstrap != null ? demoBootstrap.SpawnDirector : null;
        }

        private bool TryResolveSpawnDirector(
            out EnemySpawnDirector director,
            out ConsoleCommandResult error
        )
        {
            director = ResolveSpawnDirector();
            if (demoBootstrap == null)
            {
                error = ConsoleCommandResult.Error(
                    "This player has no PowerSuitDemoBootstrap."
                );
                return false;
            }

            if (director == null)
            {
                error = ConsoleCommandResult.Error(
                    "The owned demo world has not created its spawn director yet."
                );
                return false;
            }

            if (!director.IsInitialized)
            {
                string detail = director.LastValidationError;
                error = ConsoleCommandResult.Error(
                    string.IsNullOrWhiteSpace(detail)
                        ? "The owned spawn director is not initialized yet."
                        : "The owned spawn director is unavailable: " + detail
                );
                return false;
            }

            error = default;
            return true;
        }

        private static bool HasSpawnArchetype(
            EnemySpawnDirector director,
            string archetypeId
        )
        {
            if (director == null || string.IsNullOrWhiteSpace(archetypeId))
            {
                return false;
            }

            for (int index = 0; index < director.SpawnEntryCount; index++)
            {
                if (
                    string.Equals(
                        director.GetSpawnArchetypeId(index),
                        archetypeId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadFiniteFloat(
            IReadOnlyList<string> arguments,
            string usage,
            out float value,
            out string error
        )
        {
            if (
                arguments.Count != 1 ||
                !ConsoleCommandRegistry.TryParseFiniteFloat(arguments[0], out value)
            )
            {
                value = 0f;
                error = "Usage: " + usage;
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static ConsoleCommandResult TunedFloatResult(
            string label,
            float requested,
            float effective,
            string units = null
        )
        {
            string suffix = string.IsNullOrWhiteSpace(units)
                ? string.Empty
                : " " + units;
            if (!Mathf.Approximately(requested, effective))
            {
                return ConsoleCommandResult.Success(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1:0.###} was clamped to {2:0.###}{3}.",
                        label,
                        requested,
                        effective,
                        suffix
                    )
                );
            }

            return ConsoleCommandResult.Success(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} set to {1:0.###}{2}.",
                    label,
                    effective,
                    suffix
                )
            );
        }

        private static string FormatFloatMessage(string format, float value)
        {
            return string.Format(CultureInfo.InvariantCulture, format, value);
        }

        private static void AppendFloat(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("0.0", CultureInfo.InvariantCulture));
        }

        private static void AppendLineSeparator(StringBuilder builder)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }
        }

        private static void AppendInlineSeparator(
            StringBuilder builder,
            bool hasEarlierEntry
        )
        {
            if (hasEarlierEntry)
            {
                builder.Append(" | ");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
