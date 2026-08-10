# PowerSuit

PowerSuit is an original single-player Unity 6 third-person powered-suit combat sandbox. The current **Feel-First Combat and Flight Tech Demo** is playable end to end: ground movement, flight, precision shooting, three suit abilities, six enemy archetypes, randomized encounters, a three-zone world, HUD, developer console, and pooled combat feedback are integrated into one reusable C# gameplay slice.

The game comes first. Runtime systems are data-driven where tuning benefits, plain-C# logic is separated from Unity adapters where practical, and the generated content pipeline preserves the canonical scene and asset GUIDs.

## Play the demo

1. Open the project in Unity `6000.5.7f1`.
2. Open `Assets/Scenes/PoweredSuitAimDemo.unity`.
3. In the Game view, select **Fit** or `1x` before judging camera distance. A saved local `2x`/`4x` scale or panned Game view crops the rendered image and can look like an in-game zoom.
4. Press Play.

The player bootstrap instantiates `PowerSuitCombatSandbox.prefab` once, connects the SpawnDirector and HUD, and suppresses the three legacy `SimpleEnemy` rollback actors while the generated world owns encounters.

## Controls

| Input | Action |
| --- | --- |
| `WASD` | Move; `S` backpedals without turning toward the camera |
| Mouse | Orbit/look |
| `Space` | Jump on ground / ascend in flight |
| `Ctrl` or `C` | Descend in flight |
| `F` | Toggle flight |
| `Shift` | Flight boost |
| Right mouse | Shoulder aim |
| `V` | Toggle rifle scope while the weapon is ready |
| Left mouse | Fire |
| `R` | Reload |
| `Q` | Draw or stow the rifle |
| `G` | Fire shoulder micro-rocket |
| Hold `E`, release | Target and cast lightning; cancel with `Esc` |
| `X` | Cast the void-orb ultimate when the meter is full |
| Backquote or `F1` | Toggle developer console |
| `Esc` | Cancel targeting / release cursor |

The centralized `PowerSuitInputRouter` arbitrates gameplay, targeting, scope, console, and cursor ownership. Click the Game view to recapture the cursor after releasing it.

## Current demo content

- Ground locomotion has acceleration/deceleration, signed directional movement, coyote time, jump buffering, air control, landing response, and backpedalling. The focused demo now uses a fast-response 6.5 m/s profile with stronger acceleration, braking, reversal, turning, and stride playback.
- Flight includes takeoff, hover, ascend/descend, braking, boost, banking, landing transitions, and flight-compatible aiming, firing, reloading, and abilities. Its 14 m/s cruise and 28 m/s boost profiles use deliberately faster acceleration and release response.
- Camera profiles cover exploration, shoulder aim, flight, boost, ability targeting, and the Precision Rifle's actual `ScopePoint`. Collision pull-in and damped release avoid recurrent camera clipping; mouse/pad look, aim sensitivity, camera damping, and aim transitions use the responsive demo profile.
- The data-driven Precision Rifle owns finite ammunition, manual and empty-magazine automatic reload, cadence, critical hits, physical pooled projectiles, bolt cycling, weapon-ready presentation, and shoulder/scope aim profiles.
- Shoulder rocket, projected lightning strike, and meter-gated void-orb ultimate use shared cooldown, targeting, faction-safe radial damage, external-force, pooling, and lifecycle boundaries without a monolithic ability framework. Rocket and lightning now render their full damage radius, expanding shockwave/rays, impact flash, and—in lightning's case—a vertical bolt; the targeting ring pulses before release.
- Six generated, data-driven enemies are available: Stationary Sentry, Patrol Rifleman, Pursuer, Heavy Artillery, Flying Harrier, and Skirmisher. Their shared runtime/controller/emitter architecture supports distinct movement and attack profiles, telegraphs, health bars, force response, death, and pool reset.
- `PowerSuitCombatSandbox.prefab` supplies three connected greybox zones, separate ground/flight spawn regions, 19 spawn points, and a deterministic weighted/threat-budget SpawnDirector. The default live cap is eight enemies.
- The safe-area-aware HUD presents player health, ammo/reload, reticle/hit state, ability cooldowns, and ultimate meter. The integrated player disables the superseded IMGUI health/ammo panels, eliminating the duplicate reload and instruction overlays. Enemy health bars are camera-facing mesh renderers with distance culling, avoiding per-enemy Canvas rebuilds; encounter counts remain available through console statistics.
- The Development-Build console provides safe, clamped runtime tuning and diagnostics.

## Developer console

Open with Backquote or `F1`. Use `help` or `help <command>` for the authoritative in-game list.

- Core/diagnostics: `help`, `clear`, `showstats on|off`, `timescale <value>`, `fps`, `pools`, `projectiles`, `reloadscene`
- Player: `god on|off`, `invulnerable on|off`, `heal`, `killplayer`, `player.hp <value>`, `player.maxhp <value>`, `player.damage_multiplier <value>`, `player.speed_multiplier <value>`, `player.flight_speed_multiplier <value>`, `playerstate`
- Weapon/abilities: `ammo full`, `ability.reset`, `cooldowns on|off`, `ultimate full|empty`, `rocket.damage <value>`, `rocket.radius <value>`, `lightning.damage <value>`, `lightning.radius <value>`, `void.damage <value>`, `void.pull <value>`
- Encounters: `seed <integer>`, `spawn <archetype|random> <count>`, `spawn.list`, `killall`, `despawnall`, `enemy.cap <count>`, `enemy.spawnrate <seconds>`, `enemy.hp_multiplier <value>`, `enemy.damage_multiplier <value>`, `enemy.speed_multiplier <value>`, `spawner on|off`, `enemies`

The console is gated to the Editor and Development Builds and routes changes through intentional runtime APIs rather than reflection into private fields.

## Architecture and generated content

- `Assets/Game/Combat/Runtime`, `Abilities/Runtime`, and `Enemies/Runtime` contain testable state, configuration, selection, and damage rules.
- Unity-facing components under `Combat`, `Abilities/Unity`, `Enemies/Unity`, `Player`, `UI`, and `Demo` adapt those rules to physics, input, animation, presentation, and scene lifecycle.
- Definitions live under `Assets/Game/Content`; generated enemy, ability, player, and world prefabs live under `Assets/Game/Prefab`.
- `PoweredSuitGenerator109Integration` updates generated assets/prefabs and validates an existing AimDemo without recreating or overwriting it. Enemy definition assets are preserved once authored.
- `PowerSuitValidationRunner` runs the full Unity suites and writes ignored summaries under `Temp/`.

See [ROADMAP.md](ROADMAP.md) for acceptance status and [PROJECT.md](Assets/Game/Documentation/PROJECT.md) for subsystem ownership and validation workflow.

## Verification record

Current workspace evidence from 2026-08-10:

- `dotnet build Powersuit.slnx --no-restore`: 18 assemblies, 0 warnings, 0 errors.
- Unity EditMode: 197/197 passed.
- Unity PlayMode: 12/12 passed, including a 1,000-projectile spawn/recycle pool exercise.
- Generated controller, ability prefabs, six enemy prefabs/definitions, projectile prefab, player prefab, HUD/bootstrap references, SpawnDirector, and three-zone world validation passed.
- Windows x64 Development Build completed successfully.
- A 15-second headless player smoke loaded the canonical demo and found no gameplay exception, assertion, or crash pattern. Unity emitted no gameplay errors; build logs may include non-blocking third-party Sentis shader warnings.

These checks prove the current automated compile/test/build path. They do **not** replace hands-on owner acceptance of ground/flight/camera/scope/combat feel, actual Unity Profiler captures under representative stress, or a long lifecycle soak.

## Remaining milestone gates

- Owner/manual feel acceptance at Game view Fit/1x, including combined ground/flight/aim/scope/fire/reload/ability inputs, slopes, steps, close cover, and camera framing.
- Real profiler captures at representative and stress loads, including 30/60/120+ FPS checks, CPU/render/GC observations, and pool-capacity tuning.
- Long lifecycle validation across death/respawn, scene reload, repeated pool reuse, malformed console input, and extended play.
- Replacement-character/retargeting validation and true art/content polish. The current generated powered suit, enemies, VFX/audio hooks, and three-zone environment remain tech-demo content rather than final production art.

## Scope exclusions

This milestone does not include multiplayer, networking, loot generation, inventory, rarity, progression, skill trees, crafting, missions, quests, dialogue, story, save progression, procedural open world, bosses, multiple playable suits, a large arsenal, Steam integration, or final Asset Store publication.
