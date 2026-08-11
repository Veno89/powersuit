# PowerSuit Technical Demo Architecture

This document describes the current **Feel-First Combat and Flight Tech Demo** implementation. `Assets/Scenes/PoweredSuitAimDemo.unity` is the canonical scene; the reusable gameplay composition is supplied through prefabs and C# runtime bootstrap rather than by rewriting that scene.

## Runtime composition

`PlayerPrototype_Generator109.prefab` is the canonical player variant. The retained `Generator109` filename preserves GUID continuity while the nested suit and animation content comes from the later Generator114 asset pass. Generator114 contains 24 exact animation actions and 35 mandatory validation renders, preserving Generator113's powered gait while adding six stance-aware lateral loops under animation contract version 5.

At runtime, `PowerSuitDemoBootstrap`:

1. resolves the player camera after the prefab is instantiated;
2. instantiates one `PowerSuitCombatSandbox.prefab`;
3. binds the owned `EnemySpawnDirector` to the player and HUD;
4. suppresses the three legacy scene `SimpleEnemy` rollback actors so only the generated encounter architecture runs; and
5. restores those legacy actors when its ownership ends.

The generated world prefab contains three connected greybox areas—central landing/combat, open flight/long range, and vertical/aerial combat—plus five spawn zones and 19 ground/flight points. It is tech-demo geometry, not final environment art.

## Ownership boundaries

| Location | Responsibility |
| --- | --- |
| `Combat/Runtime` | Engine-independent damage, faction, weapon, cooldown, ultimate, and aim state |
| `Combat` | Rifle, physical player projectile, damage receivers, and Unity conversion/adapters |
| `Abilities/Runtime` | Rocket, lightning, void, and target-validation state |
| `Abilities/Unity` | Target indicator, area-effect execution, projectiles/actors, forces, VFX hooks, and pooling |
| `Enemies/Runtime` | Archetype configuration, decisions, runtime state, spawn configuration/planning |
| `Enemies/Unity` | Definitions, controller/motor, attack emitter/projectile, SpawnDirector, zones, signals, and mesh health bars |
| `Player` | Central input routing, movement/flight, health, ability coordination, animation, rifle presentation, and visual flight response |
| `UI/HUD` | Testable snapshots/formatting and the screen-space presenter |
| `DeveloperConsole` | Pure parser/registry/session, Unity overlay/input gate/statistics, and typed gameplay command pack |
| `Demo` | Runtime world/bootstrap ownership |
| `Core` | Pool/reset contracts, feedback pool, and frame pacing |
| `Content` | Authored weapon/enemy definitions and materials |
| `Prefab` | Generated player, abilities, enemies, combat effects, and world composition |
| `Editor` | Idempotent generation/integration, validation, and focused Development Build tools |
| `Tests` | EditMode and PlayMode correctness, integration, content, and lifecycle coverage |

Important gameplay authority remains in C#. Animation events may align presentation, but ammunition, damage, cooldown, ultimate meter, spawn ownership, reset, and action availability all have code-owned fallbacks.

## Input and action ownership

`PowerSuitInputRouter` samples keyboard/mouse or gamepad once per frame and exposes an immutable gameplay frame. It arbitrates held/pressed/released actions, cursor recapture, console focus, scope, targeting, and cancel behavior so the player, weapon, and abilities do not independently compete for raw input.

Keyboard/mouse bindings are:

- `WASD` movement; `Shift` sprints forward/sideways on stable ground and boosts in flight
- tap `Space` for a normal ground jump; keep an accepted jump held for about 0.9 seconds to enter flight; `Space` then ascends, while `Ctrl` or `C` descends
- RMB shoulder aim, RMB + `V` Precision Rifle scope toggle, LMB fire, `R` reload, `Q` draw/stow
- `G` rocket, hold/release `E` lightning, `X` void ultimate
- Backquote or `F1` console, `Esc` cancel/release cursor

There is no `F` flight binding. A jump-to-flight hold must begin with a consumed ground/coyote jump; pressing and holding `Space` only after the player is already falling cannot arm flight. Releasing early or landing before the threshold cancels the sequence, and feet-level touchdown exits flight automatically.

The scope uses the Precision Rifle's configured `ScopePoint` and aim profile. `V` is accepted only when the ready weapon is both `PrecisionRifle` class and scope-enabled; RMB remains the over-shoulder profile. While scoped, every renderer beneath `RifleRoot` is reversibly suppressed so receiver, barrel, and optic geometry cannot enter the sight picture. Weapon transforms and ballistics continue evaluating, and the presenter draws an aspect-safe circular sight, center crosshair, mil ticks, and range stadia against the same aim-ray point used by the weapon. Flight does not remove weapon reload, shoulder aim, scope, or ability ownership.

## Player, camera, animation, and weapon

`PowerSuitController` adapts a CharacterController to plain-C# movement helpers: acceleration/deceleration, signed camera-relative movement, grounding hysteresis, coyote time, buffered jump, air control, landing, hold-to-flight, hover/braking, vertical flight, boost, banking, and safe ground/flight transitions. The focused player prefab persists the responsive profile (6.5 m/s ground, 14 m/s flight, 28 m/s boost), with piecewise zero-crossing reversal, stronger braking, 20/32 free/combat turning sharpness, 1.65x stable-ground sprint, a 0.9-second accepted-jump flight threshold, and 0.55 held-jump gravity scale. The rollback base prefab retains its legacy 5 m/s tune.

Camera state blends exploration, shoulder, flight, boost, ability-targeting, and weapon-specific scope profiles. Collision uses reusable hit storage, immediate pull-in, and damped release. The focused response profile uses 0.18 mouse sensitivity, 180 degrees/second pad look, camera sharpness 45, aim transition sharpness 22, and rifle shoulder/scope multipliers 0.9/0.45. `PowerSuitVisualFlightResponse` keeps ground/flight attitude and landing squash presentation-only. `PowerSuitThrusterPresentation` builds cached backpack/boot jets and glows, driven by sprint/flight/boost state and colored blue-white through orange/red by the separate propulsion-heat adapter.

The generated Animator uses four layers in this order:

1. `Base`
2. masked override `Forward Weapon Pose`
3. masked additive `Bolt Cycle Action`
4. masked override `Weapon Actions`

The base layer owns locomotion/flight, including a dedicated `Run Locomotion` state driven by `IsRunning`. Generator114 keeps the 0.8379 m powered walk and 0.9341 m run strides, adds ready/aimed/stowed lateral actions, and feeds signed `MovementX`/`MovementY` into three cardinal 2D blend trees. Its looping `PS_Run_Forward` clip stays at 1.35x state playback; propulsion feedback intentionally communicates powered assistance at 10.725 m/s. `PowerSuitFootPlanting` applies bounded post-Animator contact correction only to feet near a surface, while the visual response supplies start/stop, braking, strafe, turn, and run attitude without changing motor velocity. Forward pose keeps accepted hip/flight fire pointed forward without forcing aim FOV. The additive layer cycles the articulated bolt, while the highest override owns draw, sheathe, and reload.

`PowerSuitPropulsionHeatState` is the plain-C# shared stamina/heat authority for sprint, flight, and boost. Defaults are 100 capacity, 8/5/14 heat per second for sprint/flight/boost, a one-second cooldown delay, 26 heat per second cooling, and an overheat lock that recovers at 35%. `PowerSuitPropulsionHeat` adapts it to controller state, disables propulsion while locked, resets on respawn, drives HUD state, and informs thruster color without moving the character.

`WeaponDefinition` owns authored rifle tuning, empty-magazine auto-reload policy, and ground/shoulder/scope camera data. `WeaponRuntimeState` owns ammo, cadence, reload availability/commit, critical resolution, and manual cycle. `PowerSuitWeapon` owns muzzle-origin physical projectiles, target path, feedback, runtime tuning, pooling, and adapters to HUD/animation. Automatic reload waits for a manual bolt cycle to finish, requires reserve ammunition, and uses the same presentation and animation gates as an explicit reload.

## Abilities

The abilities share narrow services—cooldown/ultimate state, target validation, faction-safe radial queries/deduplication, external force, pool/reset, HUD, and input arbitration—rather than a large generic ability framework. `AbilityAreaEffectPresentation` procedurally builds cached line/light children once, then drives pulsing target boundaries, rocket shockwave/rays, lightning radius/impact rings, and the lightning column without per-use prefab construction.

- `ShoulderRocketAbility` launches a pooled hardpoint projectile toward the resolved target. Its explosion applies falloff damage and impulse once per receiver.
- `LightningStrikeAbility` owns hold-to-target/release-to-cast state, validity and cancel rules, target indicator, cooldown, and a pooled warning/strike actor.
- `VoidUltimateAbility` consumes a full meter, places a pooled field, periodically damages and pulls valid enemies, then applies a final outward burst.

Public tuning APIs support the developer console without exposing private fields through reflection.

## Enemies and encounter direction

The generated content set contains six `EnemyArchetypeDefinition` assets and matching prefabs:

- Stationary Sentry
- Patrol Rifleman
- Pursuer
- Heavy Artillery
- Flying Harrier
- Skirmisher

`EnemyArchetypeController` combines definition-driven runtime state, movement/flight decisions, target ownership, force response, health, telegraph/attack signals, and complete pool reset. `EnemyAttackEmitter` and the pooled enemy projectile keep attack presentation and projectile lifecycle separate. `EnemyHealthBarPresenter` uses camera-facing mesh renderers with distance culling rather than a Canvas per enemy.

`EnemySpawnDirector` wraps deterministic `SpawnPlanner` and `SpawnDirectorRuntimeState` rules: stable archetype IDs, weighted/threat-budget selection, cap, interval/group size, ground/flight zone compatibility, safe radius, spawn protection, staggered attacks, pool warmup/reuse, death replacement, seed reset, pause/clear, and live diagnostics. The generated default is tuned to a ten-enemy cap, 4.4-second interval, groups up to three, and threat budget 5.5. The world adds a foundry catwalk/AoE pad, causeway bridge/AoE courtyard, and airfield hover platforms/flight gates while keeping the original five zones and 19 spawn points.

## HUD and developer console

The HUD consumes a quantized `PowerSuitHudSnapshot` and only rebuilds display strings when visible values change. It covers health, propulsion heat/overheat, ammo/reload, reticle/hit state, rocket/lightning cooldowns, and ultimate meter. The generated Canvas owns a `PowerSuitHudSafeArea`; health and heat sit bottom-left, abilities bottom-center, and ammo/reload bottom-right. The integrated player disables the older IMGUI health and ammunition panels so they cannot overlap the instructions or reload widget. Encounter counts are reported by the developer-console statistics provider rather than the HUD snapshot.

The developer console is enabled in the Editor and Development Builds. Its pure registry/parser provides help, errors, history, quoted arguments, typed parsing, and clamping. The Unity overlay owns cursor/input focus. The gameplay pack exposes intentional APIs for player, rifle, ability, enemy, director, seed/spawn, scene, FPS, pool, and projectile commands. Run `help` in game for the complete current list.

## Pooling and hot paths

`CombatFeedbackPool` is the common lightweight pool and `ICombatPoolable` defines reset hooks. Player projectiles, enemy projectiles, ability projectiles/actors, effects, and generated enemies are prewarmed or reused. Diagnostics expose active, inactive, peak, and miss/instantiation counts. One-time projectile component setup happens during initialization rather than every spawn, and generated materials use instancing.

Reset responsibilities include timers, velocities, health/death, motor/AI state, collider/renderer state, forces, target ownership, effects/trails, subscriptions, HUD state, and director bookkeeping. The PlayMode suite includes 1,000 projectile spawn/recycle operations without steady-state instantiation.

## Generation and scene safety

`PoweredSuitGenerator109Integration` updates the generated controller, animation assets, player prefab, ability prefabs, enemy content, and world prefab, then validates references. When AimDemo already exists, it does not recreate or repopulate the scene. First-time creation remains a separate missing-scene path.

`PowerSuitDemoEnemyContentGenerator` creates the six enemy prefabs, projectile, materials, and world. Existing enemy definition assets are preserved rather than overwritten, so authored tuning survives regeneration.

The local recovery snapshot was audited as semantically equivalent to the committed AimDemo and is excluded from the intended source change. Local scene, render-pipeline, package, project-setting, and recovery churn must not be staged accidentally.

## Validation workflow and current evidence

`PowerSuitValidationRunner` provides menu and callable entry points for the full Unity suites. It writes ignored summaries to:

- `Temp/PowerSuitValidationEditMode.txt`
- `Temp/PowerSuitValidationPlayMode.txt`

Accepted gameplay-batch results from 2026-08-10, followed by the 2026-08-11 Development Player certification below:

- `dotnet build Powersuit.slnx --no-restore`: 18 assemblies, 0 warnings, 0 errors.
- EditMode: 234/234 passed.
- PlayMode: 12/12 passed.
- Generator114 source validation passed for all 24 animation clips and 35 mandatory renders, including six lateral loops and 0.7130 m lateral foot separation; generated 2D blends, foot planting, propulsion heat/HUD, heat-reactive thrusters, complete-rifle scope suppression, and prefab data passed integration validation.
- Generated controller/additive bolt clip, player/ability/enemy/projectile/world prefabs, definition assets, HUD/bootstrap, and SpawnDirector validation passed.
- Windows x64 Development Build succeeded at 2026-08-10 22:46. Its package-level Sentis shader warnings were non-blocking.
- A 15-second headless build smoke started successfully and remained alive until the intentional stop, with no exception, assertion, or missing-reference pattern. The only logged errors were expected offline Unity cloud `curl` failures, not gameplay failures.
- Final Unity Console inspection reported 0 errors.
- Final runtime observation produced no Unity gameplay errors or recurring warnings. Non-blocking third-party Sentis shader warnings can appear during the build and are not gameplay/compiler failures.
- Broad owner hands-on evaluation on 2026-08-11 reported that the integrated movement, aiming, heat, effects, abilities, and encounter loop work and feel decent; targeted edge cases and the remaining GPU/render/target-hardware measurements stay open.
- The opt-in Development Player performance matrix passed at 30/60/120 FPS with 32 concurrent enemies, zero main-thread managed allocation in measured frames, zero post-warmup pool misses, and zero logged errors.
- A two-minute 60 FPS/48-enemy lifecycle run passed across 7,200 measured frames, 175 enemy spawns, and 2,455 pooled spawns; frame p95 was 16.669 ms with no runtime pool instantiation.

Before accepting the milestone, still perform:

1. Set the Game view to **Fit** or `1x`; local `2x`/`4x` or pan state can mimic camera zoom.
2. Run the complete ground/flight/aim/scope/fire/reload/ability and conflicting-input matrix, including slopes, steps, close cover, death/respawn, and owner feel review.
3. Capture connected render-thread, draw-call, and GPU evidence, plus uncapped headroom and representative target-hardware results. CPU/frame/allocation/pool behavior at capped 30/60/120 is recorded in the root `PERFORMANCE.md`.
4. Extend lifecycle coverage to player respawn, reload, seed/spawner changes, scene reload, and malformed console commands. Enemy death/replacement plus repeated enemy/projectile/ability pool reuse are covered by the automated two-minute soak.
5. Validate a replacement humanoid/retargeting path and complete final animation, character, enemy, world, UI, VFX, and audio content polish.
6. Tune and accept the implemented propulsion heat drain/cooldown/recovery behavior and its HUD/heat-reactive exhaust feedback.

Automated checks establish technical correctness and sustained capped performance on the certification machine; they do not prove subjective feel, performance on all representative hardware, detailed GPU cost, or production-quality content.

## Known limitations and exclusions

Open gaps are targeted edge-case acceptance, connected GPU/render and target-hardware profiling, the remaining lifecycle cases, replacement-character validation, and true content polish. The generated models, enemies, greybox world, effects, silent audio hooks, and UI are suitable for a tech demo, not final production art. No external audio assets were added.

Excluded are multiplayer/networking, loot/inventory/rarity, progression/skill trees, crafting, missions/quests/dialogue/story, save progression, procedural open world, bosses, multiple playable suits, a large arsenal, Steam integration, and final Asset Store publication.
