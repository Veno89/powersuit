# PowerSuit Feel-First Combat and Flight Tech Demo Roadmap

This is the canonical status ledger for the compact 10–15 minute combat-and-flight sandbox. `[x]` means implementation plus the stated automated verification exists in the current workspace. Manual owner acceptance and detailed connected GPU/render evidence are tracked separately and are never implied by an automated pass.

## Milestone snapshot

| Phase | Implementation | Automated evidence | Open acceptance |
| --- | --- | --- | --- |
| A — Audit and stabilize | Complete for current batch | Compile, suites, generated-content validation, build, smoke | Owner combined-input review |
| B — Ground movement feel | Implemented with sprint/run | Plain-C# and adapter tests pass | Sprint cadence, slopes, steps, landing and feel review |
| C — Flight feel | Implemented with hold-to-flight/touchdown | State/adapter tests and smoke pass | Jump/flight timing, hover, boost, landing and frame-rate feel review |
| D — Camera and aiming | Implemented profiles and true scope | Camera/source validation passes | Fit/1x framing, close cover and scope feel review |
| E — Animation integration | Generator114 24-clip directional powered-gait set implemented | 2D controller/prefab and 35-render source validation pass | Owner feel, retargeting and replacement character |
| F — Weapon loadout | Precision + Assault + Heavy Plasma implemented | Runtime/switching/charge/radial-impact tests pass | Three-role combat tuning review |
| G — Three abilities | Implemented | State, targeting and adapter tests pass | Combat tuning and presentation review |
| H — Enemy architecture | Implemented with six archetypes | Runtime/adapter/content tests pass | Archetype readability and fair-combat review |
| I — Encounter direction | Structured three-phase encounter plus reusable SpawnDirector implemented | Deterministic state/director tests pass | Encounter pacing review and stress tuning |
| J — Three-zone world | Implemented as generated prefab | Generator/integration validation passes | Traversal, sightline and layout polish |
| K — HUD | Implemented | Formatter/presenter tests pass | Resolution/readability/accessibility review |
| L — Developer console | Implemented | Parser, registry and gameplay-command tests pass | Hands-on malformed-command pass |
| M — Pooling/performance | Pooling, diagnostics, and opt-in Development Build soak implemented | 30/60/120 matrix and two-minute 48-enemy soak pass | Connected GPU/render capture, uncapped and remaining lifecycle cases |
| N — Hero Suit V2 art | Candidate004 visual maquette plus non-integrated Candidate005 production-architecture prototype | Generator114/Candidate004 preserved; Candidate005 has 3 renderers/draws, 88,316 tris, one connected skinned undersuit, UV0 overlap audit 0, and HeroV2 structural PASS | Repair canonical visible clearance; manual sculpt/retopo/weights/PBR/LODs; Unity A/B |

## Completed implementation

### A — Ownership, safety, and foundations

- [x] Keep `Assets/Scenes/PoweredSuitAimDemo.unity` as the canonical scene and `FlightPrototype` as rollback/donor content.
- [x] Prevent legacy editor initialization from silently overwriting build-scene ownership.
- [x] Make integration idempotent and preserve an existing AimDemo instead of recreating it.
- [x] Audit the recovery snapshot; it is semantically equivalent to the committed canonical scene and is not part of the intended change set.
- [x] Separate engine-independent combat, ability, enemy, spawn, HUD, and console rules into testable assemblies.
- [x] Add explicit factions, fail-closed unassigned ownership, authoritative damage results, player defeat/restore/respawn events, and lifecycle reset seams.
- [x] Centralize gameplay input arbitration and suppress gameplay while console/UI ownership is active.

### B–F — Player feel, camera, animation, and rifle

- [x] Add acceleration/deceleration, camera-relative signed movement, backpedal, grounding hysteresis, coyote time, jump buffer, air control, and landing response.
- [x] Apply the focused fast-response tune: 6.5 m/s ground speed, strong acceleration/deceleration/reversal braking, responsive 14/28 m/s flight/boost, faster combat turning, and stride playback matched closer to the new speed.
- [x] Add stable-ground `Shift` sprinting for forward/lateral movement with a 1.65x speed multiplier; keep aim and backpedal in their dedicated locomotion states.
- [x] Add the looping `PS_Run_Forward` clip and generated `Run Locomotion` state, using 1.35x state playback rather than the ordinary locomotion speed parameter.
- [x] Preserve the 6.5 m/s walk and 10.725 m/s sprint speeds while advancing Generator114 to 24 actions: powered forward/backward gaits plus authored ready/aimed/stowed left/right loops and 2D diagonal blends.
- [x] Add contact-aware foot planting plus procedural start/stop, braking, strafe, turn, and run attitude without giving presentation code movement authority.
- [x] Add cached backpack and boot exhaust for sprint, hover, and boost, with heat-reactive blue-white through orange/red presentation.
- [x] Add a shared, testable sprint/flight/boost propulsion-heat resource, cooldown delay, overheat/recovery lock, respawn reset, and safe-area HUD bar.
- [x] Replace the `F` flight toggle with tap-versus-hold jump ownership: tap `Space` jumps, while continuously holding an accepted ground/coyote jump for about 0.9 seconds enters flight.
- [x] Prevent falling + `Space` from arming flight, cancel the hold sequence on early landing/release, and return to ground locomotion automatically when the player's feet touch down.
- [x] Add hover, vertical control, braking, boost, banking, landing, and flight-compatible weapon/ability actions; `Shift` remains the flight boost while airborne.
- [x] Add exploration, shoulder, flight, boost, targeting, and weapon-specific scope camera profiles with damped collision response.
- [x] Raise mouse/pad aim response, camera damping, shoulder/scope sensitivity, and aim transition sharpness without changing ballistic or reticle ownership.
- [x] Use the rifle `ScopePoint` for actual scoped camera placement; allow `V` only for a scope-enabled Precision Rifle while RMB owns shoulder aim.
- [x] Reversibly hide every renderer under `RifleRoot` while scoped, keeping weapon transforms/ballistics active, and present a circular sight, crosshair, mil ticks, and range stadia aligned to the same weapon aim ray.
- [x] Integrate ready/stowed carry, draw/sheathe, directional locomotion, aim-walk, flight, reload, and additive bolt action through the generated four-layer controller.
- [x] Keep rifle authority in C#: finite ammo, cadence, criticals, timed manual/empty-magazine automatic reload, physical projectile, bolt gate, death/reset, and action priorities.
- [x] Keep the rifle forward for accepted hip-fire and flight-fire staging instead of leaving it diagonally across the chest.
- [x] Add a fixed, data-driven three-slot loadout with independent ammunition/cadence state, safe reload/cycle/charge cancellation, respawn reset, and centralized `1`/`2`/`3`/wheel/gamepad input. Visible switching sheathes the current weapon, commits the receiver swap while hidden, and draws the selected weapon; the newest request remains queued through carry transitions.
- [x] Add a 720 RPM, 30-round Assault Rifle definition with automatic fire, distinct damage/spread/recoil/aim tuning, 48-projectile prewarm, auto-reload, and Precision-Rifle-only scope enforcement.
- [x] Generate a distinct scope-free 16-piece Assault Rifle receiver using three shared materials and no colliders; toggle it against the precision receiver without moving the rig-owned gameplay hardpoints.
- [x] Add data-driven presentation identity: a dynamic orange automatic-fire reticle, warm muzzle flash, stronger presentation-only receiver kick, and weapon-specific reticle recovery while retaining pooled projectile/tracer authority.
- [x] Add a four-round Heavy Plasma Cannon with hold/release charge ownership, a 0.8-second full charge, sub-threshold cancellation, slow projectile override, charge-scaled damage/radius, explosive faction-safe radial damage, stagger, impulse, heavy recoil, magenta reticle, distinct generated receiver, and expanding impact rings.

### G — Combat abilities

- [x] Implement shoulder rocket with hardpoint launch, reticle targeting, cooldown, pooled projectile, radial falloff damage, and impulse.
- [x] Implement hold/release lightning targeting with projected indicator, validation/cancel, cooldown, warning, and area strike.
- [x] Visualize rocket and lightning strength with pulsing full-radius boundaries, expanding shockwaves/rays, impact light, and a vertical lightning column.
- [x] Retain a full-radius aftermath ring after rocket/lightning impact; strengthen rifle muzzle/tracer/impact feedback and critical/kill hit markers.
- [x] Implement meter-gated void orb with placement, periodic damage, pull, final outward burst, and pooled reset.
- [x] Share cooldown/meter, target validation, faction-safe radial deduplication, external-force, pool, input, and lifecycle boundaries without a giant generic skill graph.

### H–J — Enemies, encounters, and world

- [x] Add shared enemy configuration, runtime state, decision logic, signals, motor/controller, attack emitter/projectile, force response, health/death, and reset ownership.
- [x] Generate and validate six definitions and prefabs: Stationary Sentry, Patrol Rifleman, Pursuer, Heavy Artillery, Flying Harrier, and Skirmisher.
- [x] Add camera-facing, distance-culled mesh health bars suitable for pooled enemies.
- [x] Add pooled enemy damage/stagger flashes and full-duration origin-to-target telegraphs with target rings.
- [x] Add deterministic weighted/threat selection, caps, interval/group control, safe radius, ground/flying zones, pool warmup/reuse, death replacement, seed control, and diagnostics.
- [x] Generate `PowerSuitCombatSandbox.prefab` with central combat, open flight/long-range, and vertical/aerial zones, plus 28 spawn points (21 ground and 7 flight); require at least seven points per ground zone, project them onto the surface, and require full capsule clearance from sandbox geometry.
- [x] Add foundry catwalk/AoE pad, causeway bridge/AoE courtyard, and airfield hover-platform/flight-gate landmarks; tune the live cap to 10 with 4.4-second, 1–3 enemy groups.
- [x] Add a deterministic three-phase demo encounter: 7 causeway enemies, 7 foundry enemies, and 9 airfield enemies; activate phases by proximity, expose remaining counts to the HUD, and restart the active phase after player defeat.
- [x] Instantiate and bind the generated world at runtime without mutating the canonical scene; suppress and restore the legacy `Demo Environment` and three rollback enemies through the bootstrap lifecycle.
- [x] Remove the actual floor-flicker cause by preventing the old gray floor and colored sandbox floors from rendering/colliding coplanarly. Keep suit thrusters emissive without unnecessary moving real-time lights, while retaining transient weapon/ability lighting.

### K–M — HUD, tools, pooling, and performance foundations

- [x] Add health, propulsion heat, ammo/reload/charge, reticle/hit, ability cooldown, ultimate, and active-zone objective HUD state; retain detailed encounter counts in console statistics.
- [x] Avoid redundant HUD string updates through display-quantized snapshot comparison.
- [x] Remove duplicate legacy health/ammo/reload overlays from the integrated player and parent all Canvas widgets under a tested safe-area root; keep health, abilities, ammo, and reload in separate screen regions.
- [x] Add an Editor/Development-Build console with parsing, history, help, errors, clamping, cursor/input ownership, runtime tuning APIs, and optional statistics.
- [x] Add commands for player/weapon/ability/enemy/director tuning, spawn/clear/seed control, reload, FPS, pools, and projectiles.
- [x] Pool combat feedback, player/enemy/ability projectiles and actors, and enemies with explicit reset hooks and active/inactive/peak diagnostics.
- [x] Move one-time projectile component setup out of the hot spawn path and enable generated material instancing.
- [x] Add an opt-in Development Build stress runner with strict command-line parsing, bounded nonalloc sample collection, JSON reporting, error gates, and per-prefab pool-miss diagnostics.
- [x] Add stress-only enemy and shared-projectile prewarming sized for concurrent population and replacement overlap without inflating ordinary encounter warmup.

### N — Hero Suit V2 visual development

- [x] Preserve Generator114, its frozen validation archive, the active Unity FBX/GUID, controller, and player prefab as the untouched rollback path.
- [x] Create the original **Aegis Vanguard** four-view concept and hero material target in a separate `ArtSource/PoweredSuitNextGen/` lane.
- [x] Build a deterministic review-only v002 mechanical blockout on the existing 23-bone armature while retaining all 24 `PS_*` actions; verify the approved source hashes remain unchanged.
- [x] Build Candidate004 with adult gritty industrial-gothic front/rear targets, soot-black armor, carbon fibre, restrained tarnished chrome, three-tier pauldrons, slimmer helmet/boots/gloves, shrouded outboard turbines, exact exhaust anchors, and 13 real-pose/detail renders.
- [x] Document measurable production targets for silhouette, articulation, rifle docking/camera clearance, hybrid rigid/skinned construction, UV/PBR work, renderer consolidation, and four LODs.
- [x] Build Candidate005 as an isolated production-architecture prototype without replacing Candidate004 or the active Generator114/Unity path: 88,316 LOD0 triangles, three skinned renderers/estimated draws, one connected skinned undersuit, complete `UV0`, and zero selected overlap faces/loops in the dedicated audit.
- [x] Pass the Candidate005 HeroV2 structural gate with 0 errors/4 texel-density warnings and generate diagnostic LOD totals of `88,316 -> 44,158 -> 17,660 -> 6,178` triangles. These automated LODs and preview textures are scaffolds, not release art.
- [x] Sweep all 24 actions/162 authored keyframes. The deformation scaffold records a 5.801599 maximum local edge-stretch ratio under an intentionally loose 8x catastrophic-failure ceiling; it does not certify artist-quality weighting.
- [x] Record Candidate005's canonical visible-mesh weapon-clearance **FAIL** at 3,894 forbidden instances/72 object-pair groups. Keep the separate hidden-proxy result of 5,489/240 diagnostic-only; it must not replace the canonical result.
- [ ] Owner-approve or revise the adult silhouette, dark material language, turbine arrangement, and helmet. Candidate005 is an architecture prototype, not a visual promotion over Candidate004.
- [ ] Replace or hand-finish the procedural forms with authored high-poly armor and anatomical joint seals; complete production retopology/seams, weight polish, final UV layout and PBR bake/paint, and hand-repaired LODs in the isolated candidate lane.
- [ ] Validate all 24 actions, back-stowed rifle clearance, aim/scope framing, hardpoints, flight effects, deformation, draw calls, and representative GPU cost in a separate Unity review prefab/scene.
- [ ] Promote only after an owner-approved old/new in-game A/B comparison; keep Generator114 available for one-click rollback.

## Verification completed

- [x] Full solution build: 18 assemblies, 0 warnings, 0 errors.
- [x] Full Unity EditMode suite: 261/261 passed, including generated-world ground projection and obstacle-clearance regressions.
- [x] Full Unity PlayMode suite: 15/15 passed, including canonical-world suppression/restoration, a complete seven-enemy opening wave, stable flight separation, three-slot sheathe/swap/draw, independent magazines, Heavy Plasma charge gating, scope restoration, multi-target radial impact, and the 1,000-projectile pool exercise.
- [x] PlayMode pool exercise: 1,000 projectile spawn/recycle operations without steady-state instantiation.
- [x] Generator114 source validation: 24 animation clips, contract version 5, and 35 mandatory renders; exact FBX hash matches Unity and all six lateral clips import into cardinal 2D blends.
- [x] Generated controller/run state, additive bolt clip, scope presenter, prefabs, definitions, player integration, world, bootstrap, HUD, and SpawnDirector validation.
- [x] Clean runtime observation after final pooling/enemy fixes: no Unity gameplay errors or recurring warnings.
- [x] Windows x64 Development Build completed after the world-ownership and encounter-spawn hotfix.
- [x] Fresh fifteen-second headless build smoke after that hotfix started successfully and remained alive until the intentional stop, with no gameplay exception, assertion, or missing-reference pattern. Only expected offline Unity cloud `curl` failures appeared; package-level Sentis shader warnings in the build were non-blocking.
- [x] Final Unity Console inspection: 0 errors.
- [x] Broad owner hands-on pass on 2026-08-11: the integrated movement, aiming, heat, effects, abilities, and encounter loop work and feel decent.
- [x] Development Player matrix at 30/60/120 FPS with 32 active enemies: stable target pacing, 0 B main-thread managed allocation p95/max, 0 post-warmup pool misses, and 0 logged errors.
- [x] Two-minute 60 FPS lifecycle soak with 48 active enemies: 7,200 measured frames, frame p95 16.669 ms, 175 enemies spawned, 2,455 pool spawns, 0 runtime pool instantiations, and 0 logged errors.

## Targeted manual and measurement gates still open

- [ ] Owner acceptance at **Game view Fit/1x** for camera framing and perceived zoom. A saved local `2x`/`4x` or panned Game view is not a gameplay camera setting.
- [ ] Ground matrix: walk/sprint responsiveness and cadence, reverse/backpedal, diagonal movement, jump buffer/coyote behavior, slopes, steps, walls, landing recovery, and rapid direction changes.
- [ ] Flight matrix: quick-tap jump versus roughly 0.9-second hold-to-flight, automatic touchdown, hover, ascend/descend, braking, boost, banking, terrain proximity, and rapid ground/flight transitions.
- [ ] Combat matrix on ground and in flight: visible Precision/Assault switching, distinct receiver/reticle/automatic-fire readability, shoulder aim, Precision-Rifle-only RMB + `V` scope, scope readability/reticle alignment, hip fire, reload, bolt cycle, draw/stow, rocket, lightning target/cancel/cast, void cast, and conflicting-input priority.
- [ ] Camera matrix: exploration/shoulder/flight/boost/target/scope transitions, close cover, vertical aim, recoil, and keeping player/weapon readable.
- [ ] Encounter feel: six archetypes are distinguishable and fair; spawn pacing, telegraphs, threat mix, damage, cooldowns, meter gain, and readability suit the intended loop.
- [ ] Connected Unity Profiler/Frame Debugger or graphics capture for render thread, draw calls, and GPU time; the Development Player backend did not expose usable automated draw/GPU counters. Also record uncapped headroom and representative target hardware.
- [ ] Extend lifecycle coverage to player respawn, scene reload, seed resets, spawner toggles, and malformed console commands. Repeated enemy/projectile/ability reuse and enemy death/replacement are certified by the two-minute soak.
- [ ] Complete the tracked Hero Suit V2 production gates in `ArtSource/PoweredSuitNextGen/MASTERPIECE_PLAN.md`, plus replacement-humanoid/retargeting validation, hardpoint and IK robustness, and remaining VFX, audio, animation, UI, and world-content polish.
- [ ] Owner-tune the implemented propulsion heat drain/cooldown/recovery thresholds and verify the HUD/heat-reactive exhaust during sustained sprint, flight, and boost.
- [ ] Owner-accept the new cardinal/diagonal blends, foot plants, starts/stops, braking, and sharp-turn presentation at the unchanged gameplay speeds.

## Recommended owner test loop

1. Set Game view to Fit/1x and start `PoweredSuitAimDemo`.
2. Move through the central zone, hold `Shift` to sprint, tap `Space` for a normal jump, then hold an accepted jump for about 0.9 seconds to enter flight; boost into the open zone and touch down again.
3. Switch with `1`/`2`/`3` or the wheel; verify the old weapon sheathes, its receiver swaps while hidden, and the new weapon draws. Confirm every magazine persists. Check the Assault Rifle's compact scope-free receiver, automatic cadence and orange reticle; hold/release the Heavy Plasma Cannon for partial/full magenta blasts and visible area impact; then verify only the Precision Rifle can use RMB + `V` scope on ground and in flight.
4. Use rocket, hold/release lightning, and charge/cast void against mixed enemy groups.
5. Open the console and use `showstats on`, `pools`, `projectiles`, `enemies`, `spawn.list`, and controlled `spawn`/`despawnall`/`seed` commands.
6. Record specific feel issues separately from automated correctness failures; use the tuning commands to establish candidate values before changing authored defaults.

## Later, explicitly out of scope

- Licensed/owned audio content. Unity provides playback and mixing, not a production SFX library; keep hooks silent until suitable assets are approved.

Multiplayer/networking, loot and a general inventory/equipment system, rarity/progression/skill trees, crafting, missions/quests/dialogue/story, save progression, procedural open world, bosses, multiple playable suits, a large arsenal, Steam integration, and final Asset Store publication are not part of this milestone.
