# PowerSuit

PowerSuit is a single-player third-person powered-suit action game foundation built in Unity 6. The immediate target is a small, exceptionally polished combat-and-flight sandbox that is fun as a game first. Its underlying movement, camera, combat, animation, enemy, ability, and setup systems are also being developed toward reusable, configurable, asset-agnostic Unity-package quality without turning the project into a generic framework at the game's expense.

The current vertical slice combines grounded movement, powered flight, a third-person shoulder-aim camera, projectile combat, target reactions, and the Generator 111 suit-and-Precision-Rifle animation set. The broader polished-demo milestone is still in progress.

## Try the powered-suit demo

1. Open the project with Unity `6000.5.7f1`.
2. Open `Assets/Scenes/PoweredSuitAimDemo.unity`.
3. Press Play.

Controls:

- `WASD`: move; `S` backpedals without turning the suit toward the camera
- Mouse: look
- Right mouse: current third-person over-the-shoulder aim
- Left mouse: fire
- `R`: reload
- `Q`: draw or stow the rifle
- `F`: toggle flight
- `Space`: jump or ascend
- `Ctrl` or `C`: descend
- `Shift`: boost
- `Esc`: release the cursor; click the Game view to capture it again

The rejected `6 m` / `65` degree exploration composition has been replaced with separately configurable profiles: grounded play uses `7.5 m`, `1.65 m` height, and `68` degree FOV; flight uses `9 m`, `1.9 m` height, and `72` degree FOV. The shared shoulder-aim profile remains `3.4 m`, `58` degree FOV, and a `-1.6 m` rifle-side offset on local `-X`. These values and the flight profile were confirmed in a direct live probe, but final composition and feel still require owner play acceptance. Dedicated forward/backward grounded aim-walk clips exist, while pure lateral aim strafing still uses the locomotion fallback.

Camera transitions and orbit input use frame-rate-independent damping. Collision checks use a reusable hit buffer, pull in immediately to avoid clipping, and release smoothly after clearing cover. The focused demo synchronizes to displays running at least 60 Hz (100 Hz on the current test display), with a 60 FPS fallback and background execution enabled so changing editor focus does not collapse the simulation rate.

The original `FlightPrototype` greybox and legacy FBXs remain available for comparison and rollback. The focused demo uses `PlayerPrototype_Generator109.prefab`; the `Generator109` Unity names are retained to preserve their existing GUIDs and references even though the nested model is now Generator 111.

## Development status

### Completed

- Generator 111 suit/rifle source and additive Unity integration, including 17 clips and 32-view Blender validation
- the CharacterController movement/flight baseline, physical-projectile combat, basic enemies, and one data-driven Precision Rifle
- grounded ready/stowed/draw/sheathe/aim-walk/reload/bolt-cycle presentation and the initial camera/performance pass
- wider configurable ground/flight exploration profiles, a masked Airborne Aim layer over `Hover`, and airborne reload with Weapon Actions retaining higher layer priority

These are implementation and technical-verification results, not final owner acceptance of feel or composition.

### Current

- run the expanded Unity PlayMode fixture; the TestRunner API did not start it during the current verification session
- exercise actual aim, reload, ascent/descent, movement, and boost input together in flight, including repeated/empty/partial reload and camera recovery cases
- obtain owner acceptance of the revised ground/flight composition, weapon visibility, control continuity, and airborne animation feel

The current RMB implementation is a global third-person shoulder-aim profile. True weapon-specific scoped aiming, using a validated `ScopePoint`, weapon-specific scope FOV/presentation, and close-cover rules, is separate later work within the broader milestone.

### Next

After the remaining fixture and owner checks, development proceeds through broader movement/flight feel, animation and IK, gunplay polish, true weapon-specific scope support, a small set of distinct configurable weapons, improved enemy roles, one or two abilities, sandbox/performance validation, and replacement-character/weapon testing. See `ROADMAP.md` for the ordered gates.

## Precision Rifle

Weapon behavior is data-driven through `WeaponDefinition` and a testable plain-C# runtime state. The current `PrecisionRifle.asset` tune is:

- 60 body damage, 10% critical chance, 2.0x critical damage
- 45 rounds per minute
- 5-round magazine, 25 starting reserve, 50 maximum reserve
- 2.8-second reload with the ammunition commit aligned to the authored insertion frame
- 100 m/s projectile speed
- manual 0.67-second bolt cycle after each accepted shot

The HUD shows magazine/reserve ammunition and the current ready, reload, cycle, or empty state. Presentation gates prevent firing while the rifle is stowed, drawing, sheathing, reloading, or cycling.

## Current asset status

The canonical Blender pipeline lives under `ArtSource/PoweredSuit`. Generator 111 preserves the Generator 110 winding fix and adds articulated magazine/bolt controls plus 13 weapon-handling and locomotion actions, for 17 exact exported clips total:

- compatibility: `PS_Idle`, `PS_Walk`, `PS_Hover`, `PS_Aim`
- carry: `PS_WeaponReady_Idle`, `PS_WeaponStowed_Idle`, `PS_WeaponStowed_Hover`
- transitions: `PS_Weapon_Draw`, `PS_Weapon_Sheathe`
- ready locomotion: `PS_Walk_Forward`, `PS_Walk_Backward`
- aimed locomotion: `PS_Aim_Walk_Forward`, `PS_Aim_Walk_Backward`
- stowed locomotion: `PS_WeaponStowed_Walk_Forward`, `PS_WeaponStowed_Walk_Backward`
- weapon actions: `PS_Reload`, `PS_BoltCycle`

The clean Blender build passed with no automated blockers. Technical review approved all 32 required renders, and the gated FBX export SHA-256 is `1c3fb62a3d978de6d5205af5c2f04ebf143bbcd5c10bee3f26ff4e4b4ad3d814`. Unity imports it at `Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx` with cameras and lights disabled.

The Animator now uses three responsibilities in order: the locomotion base, a masked Airborne Aim layer over `Hover`, and the higher-priority Weapon Actions layer for draw, sheathe, reload, and bolt cycle. Unity-owned action clips contain only the approved spine, arm, magazine, bolt, and `WeaponRoot` curves. Airborne reload is permitted from stable `Ready`; Weapon Actions temporarily overrides the upper body while the hover/aim presentation remains active beneath it, then returns to zero weight. A small root lock keeps Unity's Generic Animator from leaking the FBX's `-90` degree axis pose into the imported model during an override-layer transition. The non-animated wrapper and physical muzzle adapter remain the authorities for facing and bore axes.

## Project layout

- `Assets/Game`: runtime code, editor automation, prefabs, tests, and content
- `Assets/Scenes`: playable prototype and focused model/combat demo scenes
- `ArtSource/PoweredSuit`: Blender source, deterministic build scripts, validation, and approval tooling
- `Packages` and `ProjectSettings`: fixed Unity project configuration

Gameplay behavior is implemented in C#. Editor scripts create or update Unity assets through supported APIs so prefab, scene, importer, and `.meta` ownership stays with Unity.

## Validation

The editor integration commands are:

- `Tools > Powered Suit > Integrate Generator 109`
- `Tools > Powered Suit > Build Generator 109 Demo`

The menu names retain `Generator109` for GUID continuity. The development build is written to `Builds/Windows/PoweredSuitGenerator109/PoweredSuitGenerator109.exe` and is excluded from source control.

The Generator 111 candidate was verified on 2026-08-09 with Unity `6000.5.7f1`:

- C# solution compile: 0 warnings, 0 errors
- Unity Console after verification: 0 errors
- EditMode: 35/35 passed
- PlayMode: 4/4 passed
- Blender Generator 111: automated `PASS`, technical visual `APPROVED`, 32/32 reviewed renders
- imported model orientation: body-up dot `0.9997`, body-forward dot `0.9950`, physical-bore dot `0.9997`, muzzle/bore dot `0.9945`
- Windows 64-bit Development Player: succeeded

The Windows build emits non-blocking shader performance warnings from Unity's AI Inference/Sentis package; it still completes successfully.

After hands-on review exposed a firing face-plant and opposite-shoulder camera, the Unity hotfix was verified separately:

- C# solution compile: 0 warnings, 0 errors
- draw, sheathe, reload, and bolt-cycle live Animator exercise: `0` degree imported-root rotation, `0 m` position/scale drift, and at least `1.559 m` head-to-feet clearance
- all four generated upper-body clips: no Animator-root, `Root`, `Hips`, or lower-body bindings; action states use Write Defaults off
- camera occlusion diagnostic: visible rifle silhouette increased from about `10%` to `35%`

The subsequent camera/pacing pass was profiled in the live demo at `3422x1230`: approximately `4.2 ms` total frame time, `2.0 ms` render time, `25` SetPass calls, and about `42k` visible triangles. That snapshot demonstrates 60 FPS rendering headroom for the tested scene; it does not establish final frame-rate independence, representative combat-load performance, or accepted camera composition. The pass added display synchronization with a 60 FPS fallback, non-allocating steady-state camera/aim casts, smooth collision recovery, and conditional Animator-root writes.

The follow-up aim-state repair makes draw, sheathe, reload, and cycle triggers explicitly activate the masked action layer, then releases that layer as soon as it returns to `No Weapon Action`. A new PlayMode regression drives the real controller-to-animation path and requires the physical rifle bore to return to forward aim after bolt cycling. The full C# solution compiles with zero warnings/errors; the focused Unity PlayMode rerun remains part of the hands-on acceptance pass.

The latest ground/flight framing and airborne-combat batch has separate verification: the full .NET solution compiles with 0 warnings/errors; generated asset validation/integration passes; focused `Generator109IntegrationTests` pass `3/3` in EditMode; and a direct live runtime probe confirmed the exact `9 m` / `72` degree flight profile, forward physical-bore dot `0.9997` before and after reload, ammunition `4 -> 5`, and completed reload state. The resulting screenshots were visually inspected.

The expanded Unity PlayMode fixture remains open because the TestRunner API did not start it during this verification session. Actual-input and owner checks while moving, ascending, descending, boosting, aiming, and reloading also remain open; the prior 35/35 EditMode, 4/4 PlayMode, and development-build results apply to the earlier Generator 111 candidate.

The owner-reported camera, airborne reload, and airborne shouldered-aim batch is implemented and technically verified, but the broader polished-demo milestone is not accepted until the expanded fixture and hands-on flight matrix pass. True through-scope ADS remains separate later work. The exact matrix and status are recorded in `ROADMAP.md`. See `Assets/Game/Documentation/PROJECT.md` for architecture and phase details.
