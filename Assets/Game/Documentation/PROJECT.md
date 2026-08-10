# PowerSuit Technical Demo Architecture

This document describes the current **Feel-First Combat and Flight Tech Demo** implementation. `Assets/Scenes/PoweredSuitAimDemo.unity` is the canonical scene; the reusable gameplay composition is supplied through prefabs and C# runtime bootstrap rather than by rewriting that scene.

## Runtime composition

`PlayerPrototype_Generator109.prefab` is the canonical player variant. The retained `Generator109` filename preserves GUID continuity while the nested suit and animation content comes from the later Generator 111 asset pass.

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

- `WASD` movement, `Space` jump/ascend, `Ctrl` or `C` descend, `F` flight toggle, `Shift` boost
- RMB shoulder aim, `V` scope toggle, LMB fire, `R` reload, `Q` draw/stow
- `G` rocket, hold/release `E` lightning, `X` void ultimate
- Backquote or `F1` console, `Esc` cancel/release cursor

The scope uses the Precision Rifle's configured `ScopePoint` and aim profile. RMB remains the over-shoulder profile. Flight does not remove weapon reload, shoulder aim, scope, or ability ownership.

## Player, camera, animation, and weapon

`PowerSuitController` adapts a CharacterController to plain-C# movement helpers: acceleration/deceleration, signed camera-relative movement, grounding hysteresis, coyote time, buffered jump, air control, landing, takeoff/hover/braking, vertical flight, boost, banking, and safe ground/flight transitions. The focused player prefab persists the responsive profile (6.5 m/s ground, 14 m/s flight, 28 m/s boost), with piecewise zero-crossing reversal, stronger braking, 20/32 free/combat turning sharpness, and 4.5x full-speed locomotion playback; the rollback base prefab retains its legacy 5 m/s tune.

Camera state blends exploration, shoulder, flight, boost, ability-targeting, and weapon-specific scope profiles. Collision uses reusable hit storage, immediate pull-in, and damped release. The focused response profile uses 0.18 mouse sensitivity, 180 degrees/second pad look, camera sharpness 45, aim transition sharpness 22, and rifle shoulder/scope multipliers 0.9/0.45. Visual banking/squash is presentation-only and does not become movement authority.

The generated Animator uses four layers in this order:

1. `Base`
2. masked override `Forward Weapon Pose`
3. masked additive `Bolt Cycle Action`
4. masked override `Weapon Actions`

The base layer owns locomotion/flight. Forward pose keeps accepted hip/flight fire pointed forward without forcing aim FOV. The additive layer cycles the articulated bolt, while the highest override owns draw, sheathe, and reload. Code controls commit/cancel/reset behavior.

`WeaponDefinition` owns authored rifle tuning and ground/shoulder/scope camera data. `WeaponRuntimeState` owns ammo, cadence, reload commit, critical resolution, and manual cycle. `PowerSuitWeapon` owns muzzle-origin physical projectiles, target path, feedback, runtime tuning, pooling, and adapters to HUD/animation.

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

`EnemySpawnDirector` wraps deterministic `SpawnPlanner` and `SpawnDirectorRuntimeState` rules: stable archetype IDs, weighted/threat-budget selection, cap, interval/group size, ground/flight zone compatibility, safe radius, spawn protection, staggered attacks, pool warmup/reuse, death replacement, seed reset, pause/clear, and live diagnostics. The generated default is tuned to an eight-enemy cap, 5.5-second interval, groups up to two, and threat budget four.

## HUD and developer console

The HUD consumes a quantized `PowerSuitHudSnapshot` and only rebuilds display strings when visible values change. It covers health, ammo/reload, reticle/hit state, rocket/lightning cooldowns, and ultimate meter. The generated Canvas owns a `PowerSuitHudSafeArea`; health sits bottom-left, abilities bottom-center, and ammo/reload bottom-right. The integrated player disables the older IMGUI health and ammunition panels so they cannot overlap the instructions or reload widget. Encounter counts are reported by the developer-console statistics provider rather than the HUD snapshot.

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

Current 2026-08-10 results:

- `dotnet build Powersuit.slnx --no-restore`: 18 assemblies, 0 warnings, 0 errors.
- EditMode: 195/195 passed.
- PlayMode: 12/12 passed.
- Generated controller/additive bolt clip, player/ability/enemy/projectile/world prefabs, definition assets, HUD/bootstrap, and SpawnDirector validation passed.
- Windows x64 Development Build succeeded.
- A 15-second headless build smoke loaded the canonical scene and found no gameplay exception, assertion, or crash pattern.
- Final runtime observation produced no Unity gameplay errors or recurring warnings. Non-blocking third-party Sentis shader warnings can appear during the build and are not gameplay/compiler failures.

Before accepting the milestone, still perform:

1. Set the Game view to **Fit** or `1x`; local `2x`/`4x` or pan state can mimic camera zoom.
2. Run the complete ground/flight/aim/scope/fire/reload/ability and conflicting-input matrix, including slopes, steps, close cover, death/respawn, and owner feel review.
3. Capture real Unity Profiler evidence at representative and stress loads, including CPU/render/GC, pool misses, and 30/60/120+ frame-rate behavior.
4. Run an extended lifecycle soak with repeated pooled reuse, reloads, seed/spawner changes, scene reload, malformed console commands, and player/enemy death cycles.
5. Validate a replacement humanoid/retargeting path and complete final animation, character, enemy, world, UI, VFX, and audio content polish.

Automated checks establish technical correctness of the current batch; they do not prove subjective feel, a sustained 60 FPS target on representative hardware, or production-quality content.

## Known limitations and exclusions

Open gaps are owner/manual feel acceptance, profiler captures and performance tuning under representative stress, long lifecycle evidence, replacement-character validation, and true content polish. The generated models, enemies, greybox world, effects, audio hooks, and UI are suitable for a tech demo, not final production art.

Excluded are multiplayer/networking, loot/inventory/rarity, progression/skill trees, crafting, missions/quests/dialogue/story, save progression, procedural open world, bosses, multiple playable suits, a large arsenal, Steam integration, and final Asset Store publication.
