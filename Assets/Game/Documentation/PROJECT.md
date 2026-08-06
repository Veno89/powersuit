# Powersuit Project Foundation

## Product concept

Powersuit is a single-player third-person powered-flight action game. The target is a compact 3D sandbox where the player flies a powered suit, fights enemies, collects randomized equipment, and grows stronger. Phase 0 contains only project foundations, test infrastructure, a primitive grey-box flight space, and a visible player placeholder.

## Technical architecture

- Unity 6000.5.7f1 with Universal Render Pipeline 17.5.0 is the fixed editor and renderer baseline.
- The Input System 1.20.0 is retained for future controls; Phase 0 defines no gameplay input or movement.
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
4. Open `Assets/Scenes/FlightPrototype.unity` and verify the player placeholder, start marker, lighting, and grey-box routes are visible.
5. When Windows build support is installed, run `Tools > Powersuit > Phase 0 > Build Windows Development Player` or invoke `Powersuit.Editor.PhaseZeroSceneBuilder.BuildWindowsDevelopmentPlayer` in batch mode.
6. Review source-control changes and confirm no generated directories or licensed assets are staged.

## Current phase status

Phase 0 establishes the `Assets/Game` module layout, Unity-aware Git exclusions, permanent agent rules, Edit/Play Mode smoke-test assemblies, and the preserved `FlightPrototype` scene. The scene builder augments the original scene with primitive ground areas, gaps, walls, pillars, ramps, elevated platforms, a marked start area, lighting, and a non-functional powered-suit placeholder. No flight, combat, enemies, loot, inventory, or progression behavior is present.

No new packages were added. Unneeded template packages for collaboration, navigation, multiplayer guidance, Timeline, and visual scripting were removed. URP, Input System, the Unity Test Framework, uGUI, and IDE integrations remain.

## Planned next phase

Phase 1 should implement only a testable powered-flight vertical slice: input actions, a plain C# flight model, a thin player movement adapter, a third-person camera adapter, focused tests, and tuning in the existing grey-box scene. Combat and progression remain later-phase work.