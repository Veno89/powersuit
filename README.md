# Powersuit

Powersuit is a single-player third-person powered-suit action prototype built in Unity 6. The current vertical slice combines ground movement, powered flight, an over-the-shoulder aim camera, projectile combat, target reactions, and the approved Generator 109 suit-and-rifle model.

## Try the Generator 109 demo

1. Open the project with Unity `6000.5.7f1`.
2. Open `Assets/Scenes/PoweredSuitAimDemo.unity`.
3. Press Play.

Controls:

- `WASD`: move
- Mouse: look
- Right mouse: over-the-shoulder aim and `PS_Aim` pose
- Left mouse: fire from the imported `Rifle_Muzzle`
- `F`: toggle flight
- `Space`: jump or ascend
- `Ctrl` or `C`: descend
- `Shift`: boost
- `Esc`: release the cursor; click the Game view to capture it again

The original `FlightPrototype` greybox remains available as the shared Build Profile scene. The focused demo uses `PlayerPrototype_Generator109.prefab` and an explicit development-build scene list, so the legacy player/model remains intact for comparison and rollback.

## Current asset status

The canonical Blender pipeline lives under `ArtSource/PoweredSuit`. Generator 109 passed the automated geometry, rig, animation, sightline, grip/contact, and render checks. Its 18 validation images were explicitly approved before export. Unity imports the resulting FBX alongside the legacy model with cameras and lights disabled and four clips:

- `PS_Idle`
- `PS_Walk`
- `PS_Hover`
- `PS_Aim`

The Unity-facing artifact is `Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx`. The source pipeline, provenance, QA details, and regeneration workflow are documented in `ArtSource/PoweredSuit/README.md` and `ArtSource/PoweredSuit/PROVENANCE.md`.

## Project layout

- `Assets/Game`: runtime code, editor automation, prefabs, tests, and game content
- `Assets/Scenes`: playable prototype and focused model/combat demo scenes
- `ArtSource/PoweredSuit`: Blender source, deterministic build scripts, validation, and approval tooling
- `Packages` and `ProjectSettings`: fixed Unity project configuration

Gameplay behavior is implemented in C#. Editor scripts create or update Unity assets through supported APIs so prefab, scene, importer, and `.meta` ownership stays with Unity.

## Validation

From Unity, run the EditMode and PlayMode test suites in Test Runner. The Generator 109 tests verify the importer, four animation clips, Aim state, prefab wiring, real muzzle helper, safe demo spawns, and runtime scene load.

The editor integration commands are:

- `Tools > Powered Suit > Integrate Generator 109`
- `Tools > Powered Suit > Build Generator 109 Demo`

The development build is written to `Builds/Windows/PoweredSuitGenerator109/PoweredSuitGenerator109.exe` and is intentionally excluded from source control.

Verified on 2026-08-08 with Unity `6000.5.7f1`:

- C# solution compile: 0 warnings, 0 errors
- EditMode: 5/5 passed
- PlayMode: 2/2 passed
- Windows 64-bit Development Player: succeeded
- headless player smoke: demo loaded and combat ran for 10 seconds with no missing-reference or runtime exceptions

See `Assets/Game/Documentation/PROJECT.md` for the technical architecture and phase record.
