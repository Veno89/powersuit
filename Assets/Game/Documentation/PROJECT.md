# PowerSuit Project Foundation

## Product concept

PowerSuit is a single-player third-person powered-flight action game. The immediate product milestone is a compact, exceptionally polished combat-and-flight sandbox where movement, flight, aiming, shooting, dodging, and fighting feel excellent before content breadth expands.

The project has two compatible objectives: form the foundation of the eventual PowerSuit game, and build its underlying systems with the modularity, configuration, documentation, asset independence, and validation expected of a potentially commercial reusable Unity package. The game remains the priority; framework concerns must not justify speculative rewrites or weaken its identity.

The current vertical slice includes grounded movement, backpedalling, powered flight, configurable ground/flight exploration cameras, third-person shoulder aim on the ground and in flight, projectile combat, basic enemies, combat feedback, weapon carry transitions, finite ammunition, grounded/airborne reload, and a manual Precision Rifle bolt cycle. True weapon-specific through-scope ADS remains separate later work.

## Technical architecture

- Unity `6000.5.7f1` with Universal Render Pipeline `17.5.0` is the fixed editor and renderer baseline.
- The Input System `1.20.0` is the primary input backend, with guarded legacy-input fallbacks for prototype controls.
- Important rules and calculations are plain C# with EditMode tests where practical.
- MonoBehaviours adapt plain C# logic to Unity input, transforms, physics, animation, audio, and presentation.
- Scene objects hold composition and references, not large gameplay algorithms.
- Authored weapon tuning lives in `WeaponDefinition` assets under `Content`; runtime ammo/cadence/reload/cycle rules live in the `Powersuit.Combat.Runtime` assembly.
- Content assets and tuning data belong under `Content`; code belongs to its owning feature folder.
- Cross-feature dependencies should point toward `Core`, not between unrelated feature modules.

## Folder conventions

| Folder | Ownership |
| --- | --- |
| `Core` | Shared plain C# types, contracts, and utilities |
| `Player` | Player-domain logic and Unity adapters |
| `Camera` | Third-person camera logic and adapters |
| `Combat` | Damage, weapons, targeting, and combat adapters |
| `Enemies` | Enemy-domain logic and Unity adapters |
| `Progression` | Equipment, loot, inventory, and progression logic |
| `World` | Sandbox geometry, encounters, and world adapters |
| `UI` | Runtime UI and presentation adapters |
| `Content` | Materials, prefabs, ScriptableObjects, and other authored assets |
| `Editor` | Editor-only automation and validation utilities |
| `Tests` | EditMode and PlayMode test assemblies |
| `Documentation` | Product, architecture, workflow, and phase records |

Namespaces should begin with `Powersuit`. Editor-only code stays in an `Editor` folder or editor-only assembly. Tests are separated into `EditMode` and `PlayMode` assemblies. Preserve Unity-generated `.meta` files whenever assets are moved or renamed.

## Animation and presentation architecture

- Generator 111 exports 17 exact Generic-rig clips from one Blender armature.
- `PowerSuitController` exposes signed local velocity (`MovementX`, `MovementY`, normalized speed, backpedal, and aim-walk state) based on actual `CharacterController` motion.
- `PowerSuitController` also owns the late-frame third-person orbit. Configurable exploration profiles select grounded `7.5 m` / `1.65 m` / `68` degrees or flight `9 m` / `1.9 m` / `72` degrees. The shared shoulder-aim profile remains `3.4 m` / `58` degrees on the rifle-side local `-X` shoulder. Exponential damping is frame-rate invariant, steady-state collision/aim queries reuse buffers, and wall recovery is smoothed without delaying collision pull-in. These profiles are not yet weapon-specific through-scope profiles.
- `PowerSuitFramePacing` is the demo/runtime presentation adapter. It synchronizes to displays at 60 Hz or faster, uses 60 FPS as a fallback, and keeps the simulation active when the Unity Game view loses focus. It changes runtime state only and does not rewrite the shared QualitySettings asset.
- The Animator base layer selects ready, stowed, grounded aim, walk/backpedal, and `Hover` locomotion. A masked Airborne Aim layer above it applies the forward-shouldered upper body while preserving the flight base.
- A higher-priority masked Weapon Actions layer owns draw, sheathe, reload, and bolt-cycle upper-body motion. Its runtime adapter raises the layer only for active actions and returns it to zero weight at the neutral state, revealing the underlying locomotion/Airborne Aim result again.
- The integration generator extracts each weapon action into a Unity-owned `.anim` that whitelists only the spine/arms and `WeaponRoot`/magazine/bolt controls. Raw FBX action takes must not be assigned directly to the override layer.
- `PowerSuitAnimatorRootLock` keeps the imported Animator GameObject at its captured identity transform after evaluation. This contains a Unity Generic-Animator transition leak of the FBX axis pose; controller movement and the non-animated wrapper remain the only owners of player motion/facing.
- `PowerSuitWeaponPresentation` is the carry-state adapter (`Ready`, `Drawing`, `Stowed`, `Sheathing`) and gates weapon use during transitions.
- `WeaponRuntimeState` owns ammunition, cadence, reload commit, critical hits, and manual cycling. `PowerSuitWeapon` adapts it to input, projectiles, effects, HUD, and animation events. Reload is permitted in flight from stable `Ready`; the Weapon Actions layer plays above and then returns to the preserved `Hover` or Airborne Aim presentation.
- Imported `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt` bones keep suit and rifle motion synchronized in the FBX.
- A non-animated wrapper preserves the measured Blender-to-Unity facing correction after Animator evaluation. Firing originates at an axis-correct child of imported `Rifle_Muzzle`, never a floating placeholder.

## Validation workflow

1. Allow Unity to import assets and confirm the Console has no compiler errors.
2. Run the complete EditMode suite.
3. Run the complete PlayMode suite.
4. Open `Assets/Scenes/PoweredSuitAimDemo.unity` and execute the ground/flight camera, shoulder aim, reload, locomotion, firing, collision, and transition matrix in `ROADMAP.md`.
5. Profile representative play at 30, 60, 120+, and uncapped frame rates where practical; distinguish rendering headroom from frame pacing, update-order, allocation, and subjective-feel results.
6. Confirm `Assets/Scenes/FlightPrototype.unity` remains the shared Build Profile scene and still satisfies the original Phase 0 tests.
7. Run `Tools > Powered Suit > Build Generator 109 Demo` for the focused Windows development build.
8. Review source-control changes and confirm no Blender working outputs, build products, caches, or unapproved external assets are staged.

## Development progress

The Phase 0 foundation, `FlightPrototype` greybox, legacy FBXs, and Generator 110 validation archive remain available for rollback. Generator 111 is integrated through the existing additive `Generator109`-named player prefab/demo assets so their GUIDs remain stable.

### Completed

- Generator 111 ready/stowed poses, draw/sheathe transitions, forward/backward grounded locomotion, grounded aim-walk, reload, bolt cycle, articulated magazine/bolt, and additive Unity integration.
- Data-driven Precision Rifle tuning, finite ammunition, cadence/reload/cycle rules, critical hits, projectile behaviour, HUD state, and masked weapon-action presentation.
- Initial camera damping/collision/non-allocation work, display-synchronised presentation with a 60 FPS fallback, and conditional Animator-root containment.
- Configurable grounded (`7.5 m` / `1.65 m` / `68`) and flight (`9 m` / `1.9 m` / `72`) exploration profiles while retaining the shared `3.4 m` / `58` degree rifle-side shoulder aim.
- A masked Airborne Aim layer over `Hover`, higher-priority Weapon Actions, and airborne reload from stable `Ready` with the flight/aim presentation preserved beneath the action.
- Exact clip/importer, hierarchy, mask, axis, muzzle, runtime-state, carry-state, and scene tests for the original Generator 111 candidate.
- Blender `PASS` with 32 reviewed renders, C# compile with 0 warnings/errors, 35/35 EditMode tests, 4/4 PlayMode tests, zero Unity Console errors after that verification, and a successful Windows x64 Development build on 2026-08-09.
- Latest-batch verification: full .NET solution at 0 warnings/errors; generated asset validation/integration passed; focused `Generator109IntegrationTests` passed `3/3` EditMode; and a direct live runtime probe confirmed flight `9 m` / `72`, physical-bore dot `0.9997` before/after reload, ammunition `4 -> 5`, completed reload state, and visually inspected screenshots.

These are objective implementation and technical-verification results. They do not establish final owner acceptance of camera composition, movement/flight feel, aiming, animation, recoil, or combat pacing.

### Current

- **Owner/input acceptance remains:** the earlier unobstructed live `6 m` / `65` reproduction established why framing was rejected; it is retained in `ROADMAP.md` as history. The new profiles and airborne animation/reload path are technically verified, but still need actual-input and owner checks while moving, ascending, descending, boosting, aiming, reloading, and recovering from camera collision.
- **Expanded PlayMode fixture remains:** the Unity TestRunner API did not start the requested fixture during the current verification session. Focused EditMode and direct live probes passed, but do not replace that fixture or the hands-on matrix.
- **True scope is separate later work:** the current RMB mode is a global third-person shoulder profile, not a weapon-specific Precision Rifle scope. Scope work needs a validated `ScopePoint`, per-weapon FOV/presentation, reticle or overlay behaviour, transition rules, and close-cover handling.

### Next

1. Run the expanded PlayMode fixture and focused owner matrix for the new profiles, airborne shoulder aim, and airborne reload using actual movement/ascent/descent/boost input; retune from observed results.
2. Audit/tune the broader ground and flight loop: acceleration, braking, direction changes, takeoff, hover, boost, altitude control, landing, slopes, collision, camera relationship, and frame-rate independence.
3. Improve animation transitions, flight/combat layering, IK, hardpoint conventions, and replacement-asset compatibility.
4. Polish gunplay and feedback, then add a small set of distinct configurable weapons and a switching flow rather than a large shallow arsenal.
5. Improve enemy roles and combat behaviour, add one or two reusable abilities, refine the sandbox, profile representative load/GC, and perform replacement-character and replacement-weapon validation.

## Documentation and scope backlog

The broader milestone requires dedicated setup documentation for player and character replacement, weapon creation and hardpoints, enemies, animation/IK, abilities, known limitations, and performance considerations. No paid or externally licensed replacement assets may be added without approval.

Deferred beyond this polished sandbox are multiplayer, crafting, story/campaign content, a full inventory/loot/progression loop, procedural animation, a large arsenal, and publication itself. A 3-4 weapon set with switching, true scope support, improved enemy roles, one or two abilities, sandbox/performance validation, and replacement-asset tests are within the broader milestone rather than deferred.
