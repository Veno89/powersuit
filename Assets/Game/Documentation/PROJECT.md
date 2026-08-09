# Powersuit Project Foundation

## Product concept

Powersuit is a single-player third-person powered-flight action game. The target is a compact 3D sandbox where the player flies a powered suit, fights enemies, collects equipment, and grows stronger. The current vertical slice includes grounded movement, backpedalling, powered flight, a third-person shoulder camera, aimed locomotion, projectile combat, basic enemies, combat feedback, weapon carry transitions, finite ammunition, reload, and a manual precision-rifle bolt cycle.

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
- `PowerSuitController` also owns the late-frame third-person orbit. Normal and aim profiles remain independent; exponential damping is frame-rate invariant, steady-state collision/aim queries reuse buffers, and wall recovery is smoothed without delaying collision pull-in.
- `PowerSuitFramePacing` is the demo/runtime presentation adapter. It synchronizes to displays at 60 Hz or faster, uses 60 FPS as a fallback, and keeps the simulation active when the Unity Game view loses focus. It changes runtime state only and does not rewrite the shared QualitySettings asset.
- The Animator base layer selects ready, stowed, aim, walk/backpedal, and hover locomotion.
- A masked Weapon Actions layer owns draw, sheathe, reload, and bolt-cycle upper-body motion while leaving the legs on base locomotion. Its runtime adapter raises the layer only for active actions and returns it to zero weight at the neutral state, so the locomotion layer immediately regains authority over ready/stowed/aim poses.
- The integration generator extracts each weapon action into a Unity-owned `.anim` that whitelists only the spine/arms and `WeaponRoot`/magazine/bolt controls. Raw FBX action takes must not be assigned directly to the override layer.
- `PowerSuitAnimatorRootLock` keeps the imported Animator GameObject at its captured identity transform after evaluation. This contains a Unity Generic-Animator transition leak of the FBX axis pose; controller movement and the non-animated wrapper remain the only owners of player motion/facing.
- `PowerSuitWeaponPresentation` is the carry-state adapter (`Ready`, `Drawing`, `Stowed`, `Sheathing`) and gates weapon use during transitions.
- `WeaponRuntimeState` owns ammunition, cadence, reload commit, critical hits, and manual cycling. `PowerSuitWeapon` adapts it to input, projectiles, effects, HUD, and animation events.
- Imported `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt` bones keep suit and rifle motion synchronized in the FBX.
- A non-animated wrapper preserves the measured Blender-to-Unity facing correction after Animator evaluation. Firing originates at an axis-correct child of imported `Rifle_Muzzle`, never a floating placeholder.

## Validation workflow

1. Allow Unity to import assets and confirm the Console has no compiler errors.
2. Run the complete EditMode suite.
3. Run the complete PlayMode suite.
4. Open `Assets/Scenes/PoweredSuitAimDemo.unity` and execute the manual matrix in the repository `ROADMAP.md`.
5. Confirm `Assets/Scenes/FlightPrototype.unity` remains the shared Build Profile scene and still satisfies the original Phase 0 tests.
6. Run `Tools > Powered Suit > Build Generator 109 Demo` for the focused Windows development build.
7. Review source-control changes and confirm no Blender working outputs, build products, caches, or unapproved external assets are staged.

## Current phase status

The Phase 0 foundation, `FlightPrototype` greybox, legacy FBXs, and Generator 110 validation archive remain available for rollback. Generator 111 is integrated through the existing additive `Generator109`-named player prefab/demo assets so their GUIDs remain stable.

Implemented in the current candidate:

- ready/stowed rifle poses and draw/sheathe transitions
- forward walk, backpedal, forward/backward aim-walk, and stowed locomotion
- gait-speed matching to reduce visible skating
- a wider over-the-shoulder composition showing more of the rifle
- a wider 6 m / 65 degree normal exploration composition, smooth orbit/aim transitions, non-allocating steady-state camera collision, and display-synchronized presentation with a 60 FPS fallback
- data-driven Precision Rifle tuning, finite ammunition, reload, critical hits, and manual bolt cycle
- runtime reload/cycle timing aligned to authored magazine/bolt frames, with animation triggers kept presentation-only
- a two-layer Animator that preserves lower-body locomotion during weapon actions
- exact clip/importer, hierarchy, mask, axis, muzzle, runtime-state, carry-state, and scene tests

Verification on 2026-08-09: Blender `PASS` with 32 reviewed renders, C# compile with 0 warnings/errors, 35/35 EditMode tests, 4/4 PlayMode tests, zero Unity Console errors after verification, and a successful Windows x64 Development build.

The subsequent hands-on hotfix adds stronger root/action and camera assertions. Its direct live controller exercise passed Draw, Sheathe, Reload, and Bolt Cycle with zero imported-root transform drift and an upright suit throughout; the revised camera diagnostic increased visible rifle silhouette from about 10% to 35%. A complete rerun of the expanded Unity suites is pending because the restarted batch editor currently lacks the local `com.unity.editor.headless` entitlement.

The remaining phase gate is user play acceptance of the demo matrix. Dedicated lateral aim-strafe clips and broader inventory/arsenal systems remain deferred.
