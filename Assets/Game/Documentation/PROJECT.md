# Powersuit Project Foundation

## Product concept

Powersuit is a single-player third-person powered-flight action game. The target is a compact 3D sandbox where the player flies a powered suit, fights enemies, collects randomized equipment, and grows stronger. The current vertical slice includes grounded movement, powered flight, a third-person camera, over-the-shoulder aiming, projectile combat, basic enemies, combat feedback, and the approved Generator 109 suit-and-rifle model.

## Technical architecture

- Unity 6000.5.7f1 with Universal Render Pipeline 17.5.0 is the fixed editor and renderer baseline.
- The Input System 1.20.0 is the primary input backend, with guarded legacy-input fallbacks for the prototype controls.
- Important rules and calculations should be plain C# with Edit Mode tests where practical.
- MonoBehaviours should adapt plain C# logic to Unity input, transforms, physics, animation, audio, and presentation.
- Scene objects hold composition and references, not large gameplay algorithms.
- Content assets and tuning data belong under `Content`; code belongs to its owning feature folder.
- Cross-feature dependencies should point toward `Core`, not between unrelated feature modules.

## Folder conventions

| Folder | Ownership |
| --- | --- |
| `Core` | Shared plain C# types, contracts, and utilities |
| `Player` | Player-domain logic and Unity adapters |
| `Camera` | Third-person camera logic and adapters |
| `Combat` | Damage, weapons, targeting, and combat adapters |
| `Enemies` | Enemy-domain logic and adapters |
| `Progression` | Equipment, loot, inventory, and progression logic |
| `World` | Sandbox geometry, encounters, and world adapters |
| `UI` | Runtime UI and presentation adapters |
| `Content` | Materials, prefabs, ScriptableObjects, and other authored assets |
| `Editor` | Editor-only automation and validation utilities |
| `Tests` | Edit Mode and Play Mode test assemblies |
| `Documentation` | Product, architecture, workflow, and phase records |

Namespaces should begin with `Powersuit`. Editor-only code stays in an `Editor` folder or editor-only assembly. Tests are separated into `EditMode` and `PlayMode` assemblies. Preserve Unity-generated `.meta` files whenever assets are moved or renamed.

## Agent operating rules

Repository-wide permanent rules are in the root `AGENTS.md`. Agents must keep gameplay logic in C#, prefer testable plain C# code, treat Unity components as adapters, avoid large visual scripting graphs, protect `.meta` files, preserve the editor version, and respect phase scope.

## Validation workflow

1. Allow Unity to import assets and confirm the Console has no compiler errors.
2. Run Edit Mode tests.
3. Run Play Mode tests.
4. Open `Assets/Scenes/PoweredSuitAimDemo.unity`; verify movement, flight, shoulder aim, `PS_Aim`, rifle muzzle alignment, firing, target hits, and hit feedback.
5. Confirm `Assets/Scenes/FlightPrototype.unity` remains the shared Build Profile scene and still satisfies the original Phase 0 tests.
6. Run `Tools > Powered Suit > Build Generator 109 Demo` for the focused Windows development build.
7. Review source-control changes and confirm no generated directories, Blender working outputs, build products, or unapproved external assets are staged.

## Current phase status

The Phase 0 project foundation and `FlightPrototype` greybox are preserved. The active prototype now adds a controllable powered suit, grounded and flight movement, camera transitions, an over-the-shoulder aiming mode, projectile combat, enemy damage/death behavior, pooled impact and muzzle feedback, hit markers, and focused Generator 109 presentation.

Generator 109 is integrated as an additive player prefab and demo scene rather than overwriting the legacy model. Its animator controller retains the existing asset GUID and now contains Idle, Walk, Hover, and Aim states. The weapon fires from the imported `Rifle_Muzzle`, not a fixed placeholder transform. Blender sources and evidence are maintained outside `Assets`, while only approved Unity-ready artifacts enter the import tree.

## Planned next phase

The next phase should stabilize this vertical slice before expanding scope: tune movement and camera feel in play, visually inspect hand/rifle contact in Unity, add gameplay-focused tests for damage and pooling, resolve remaining prototype presentation issues, and keep progression systems deferred until the combat loop is reliable.
