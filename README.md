# Powersuit

Powersuit is a single-player third-person powered-suit action prototype built in Unity 6. The current vertical slice combines grounded movement, powered flight, an over-the-shoulder aim camera, projectile combat, target reactions, and the Generator 111 suit-and-precision-rifle animation set.

## Try the powered-suit demo

1. Open the project with Unity `6000.5.7f1`.
2. Open `Assets/Scenes/PoweredSuitAimDemo.unity`.
3. Press Play.

Controls:

- `WASD`: move; `S` backpedals without turning the suit toward the camera
- Mouse: look
- Right mouse: over-the-shoulder aim
- Left mouse: fire
- `R`: reload
- `Q`: draw or stow the rifle
- `F`: toggle flight
- `Space`: jump or ascend
- `Ctrl` or `C`: descend
- `Shift`: boost
- `Esc`: release the cursor; click the Game view to capture it again

The normal exploration camera now uses a wider `6 m`, `1.55 m` high, `65` degree composition so the full suit and its animation remain readable. The aim camera keeps its separately tuned rifle-side view on local `-X` (`3.4 m` distance, `-1.6 m` shoulder offset, `58` degree FOV), so the receiver and barrel read beside the suit instead of through its back and helmet. Dedicated forward and backward aim-walk clips keep the lower body moving while the weapon remains shouldered. Pure lateral aim strafing currently uses the locomotion fallback; authored left/right strafe clips are intentionally deferred.

Camera transitions and orbit input use frame-rate-independent damping. Collision checks use a reusable hit buffer, pull in immediately to avoid clipping, and release smoothly after clearing cover. The focused demo synchronizes to displays running at least 60 Hz (100 Hz on the current test display), with a 60 FPS fallback and background execution enabled so changing editor focus does not collapse the simulation rate.

The original `FlightPrototype` greybox and legacy FBXs remain available for comparison and rollback. The focused demo uses `PlayerPrototype_Generator109.prefab`; the `Generator109` Unity names are retained to preserve their existing GUIDs and references even though the nested model is now Generator 111.

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

The Animator uses a locomotion base layer and a masked upper-body/weapon-action layer, so the legs can continue walking during reload and bolt cycling. Unity-owned action clips contain only the approved spine, arm, magazine, bolt, and `WeaponRoot` curves. A small root lock keeps Unity's Generic Animator from leaking the FBX's `-90` degree axis pose into the imported model during an override-layer transition. The non-animated wrapper and physical muzzle adapter remain the authorities for facing and bore axes.

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

The subsequent camera/pacing pass was profiled in the live demo at `3422x1230`: approximately `4.2 ms` total frame time, `2.0 ms` render time, `25` SetPass calls, and about `42k` visible triangles. The scene therefore has comfortable 60 FPS throughput; the perceived chop came from uncapped, unsynchronised presentation and frame-dependent camera response rather than a rendering bottleneck. The pass adds display synchronization with a 60 FPS fallback, a wider normal view, non-allocating steady-state camera/aim casts, smooth collision recovery, and conditional Animator-root writes.

The expanded full Unity suites still need one rerun after the local headless-license entitlement is restored; Unity returned `com.unity.editor.headless was not found` when the stalled live runner was restarted. The prior 35/35 EditMode, 4/4 PlayMode, and development-build results remain recorded above, while the new regression assertions are checked by compilation and the direct live action exercise.

The candidate's original automated and technical validation is complete; the hotfix's targeted checks pass, with its expanded full-suite rerun and hands-on play review still open. The exact matrix and milestone status are recorded in `ROADMAP.md`. See `Assets/Game/Documentation/PROJECT.md` for architecture and phase details.
