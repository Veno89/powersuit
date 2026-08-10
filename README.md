# PowerSuit

PowerSuit is a single-player third-person powered-suit action game foundation built in Unity 6. The immediate target is a small, exceptionally polished combat-and-flight sandbox that is fun as a game first. Its underlying movement, camera, combat, animation, enemy, ability, and setup systems are also being developed toward reusable, configurable, asset-agnostic Unity-package quality without turning the project into a generic framework at the game's expense.

The current vertical slice combines grounded movement, powered flight, a third-person shoulder-aim camera, projectile combat, target reactions, and the Generator 111 suit-and-Precision-Rifle animation set. The broader polished-demo milestone is still in progress.

## Try the powered-suit demo

1. Open the project with Unity `6000.5.7f1`.
2. Open `Assets/Scenes/PoweredSuitAimDemo.unity`.
3. In the Game view toolbar, set **Scale** to **Fit** (or `1x`) before judging framing. A saved local editor layout was found at `4x` and panned, which crops the rendered image and can look like an in-game camera zoom even when the runtime camera is correct.
4. Press Play.

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

The current integrated profiles are intentionally wider: grounded play uses `9.5 m`, `1.5 m` pivot height, and `72` degree FOV; flight uses `11 m`, `1.75 m` pivot height, and `74` degree FOV; shoulder aim uses `4.3 m`, `1.45 m` pivot height, local offset `(-1.2, 0.05, 0)`, and `62` degree FOV. The earlier `6 m`, `7.5 m`, `9 m`, and `3.4 m` compositions are retained only in the roadmap's dated history. The new values still require current Unity PlayMode test-suite execution and owner acceptance. Dedicated forward/backward grounded aim-walk clips exist, while pure lateral aim strafing still uses the locomotion fallback.

Camera transitions and orbit input use frame-rate-independent damping. Collision checks use a reusable hit buffer, pull in immediately to avoid clipping, and release smoothly after clearing cover. Grounded orbit pitch also derives a floor-safe minimum from camera distance, pivot height, collision radius, and padding so a low grounded orbit cannot drive the collision sphere through the floor and falsely pull the camera inward. The focused demo synchronizes to displays running at least 60 Hz (100 Hz on the current test display), with a 60 FPS fallback and background execution enabled so changing editor focus does not collapse the simulation rate.

The original `FlightPrototype` greybox and legacy FBXs remain available for comparison and rollback. The focused demo uses `PlayerPrototype_Generator109.prefab`; the `Generator109` Unity names are retained to preserve their existing GUIDs and references even though the nested model is now Generator 111.

## Development status

### Completed

- Generator 111 suit/rifle source and additive Unity integration, including 17 clips and 32-view Blender validation
- the CharacterController movement/flight baseline, physical-projectile combat, basic enemies, and one data-driven Precision Rifle
- grounded ready/stowed/draw/sheathe/aim-walk/reload/bolt-cycle presentation and the initial camera/performance pass
- wider configurable ground/flight/shoulder profiles, a four-layer weapon presentation stack, floor-safe orbit, staged hip-fire presentation, and airborne reload

These are source-implementation and prior technical-verification results, not a claim that the latest Unity runtime batch or its feel/composition has been accepted.

### Current

- run the current Unity EditMode and expanded PlayMode suites; an isolated Test Runner attempt was stopped because the concurrent live editor held the Unity licence, so there is no current XML test result to claim
- produce and validate a current Windows development build
- exercise actual aim, reload, ascent/descent, movement, and boost input together in flight, including repeated/empty/partial reload and camera recovery cases
- obtain owner acceptance of the revised ground/flight/shoulder composition, hip-fire weapon presentation, control continuity, and airborne animation feel

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

The current source generator defines four Animator layers in exact priority order: `Base`, masked override `Forward Weapon Pose`, masked additive `Bolt Cycle Action`, and masked override `Weapon Actions`. `Forward Weapon Pose` supplies the shouldered rifle independently of aim zoom; the additive bolt layer moves only the cycling mechanism and firing arm relative to its reference pose; `Weapon Actions` remains highest priority for draw, sheathe, and reload. Unity-owned clips contain only approved spine, arm, magazine, bolt, and `WeaponRoot` curves. Airborne reload is permitted from stable `Ready`; the action layer temporarily overrides the upper body while the hover/forward-pose presentation remains beneath it, then returns to zero weight. A small root lock keeps Unity's Generic Animator from leaking the FBX's `-90` degree axis pose into the imported model during an override-layer transition. The non-animated wrapper and physical muzzle adapter remain the authorities for facing and bore axes.

Non-aim firing uses a staged `RequestFire` flow. An otherwise valid hip-fire request first faces the suit toward the camera combat ray and prepares `Forward Weapon Pose`; the gameplay fire transaction runs on the following update, after the forward pose has been evaluated. Accepted shots keep that pose through the manual cycle and a short release hold, while aim state, aim FOV, and aim spread remain unchanged. Blocked fire requests do not rotate or stage the suit.

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

The earlier aim-state repair made draw, sheathe, reload, and cycle triggers activate the then-current masked action layer, then released it at `No Weapon Action`; its PlayMode regression required the physical rifle bore to return to forward aim after cycling. The 2026-08-10 source design supersedes the cycle portion with the separate additive `Bolt Cycle Action` layer while retaining `Weapon Actions` for draw, sheathe, and reload.

The prior ground/flight framing and airborne-combat batch has separate historical verification: the full .NET solution compiled with 0 warnings/errors; generated asset validation/integration passed; focused `Generator109IntegrationTests` passed `3/3` in EditMode; and a direct live runtime probe confirmed the then-current `9 m` / `72` degree flight profile, forward physical-bore dot `0.9997` before and after reload, ammunition `4 -> 5`, and completed reload state. Those results do not verify the newer `9.5 m` / `11 m` / `4.3 m` profiles or four-layer controller.

For the 2026-08-10 batch, `Logs/Editor.log` records `[Powersuit] Generator 109 integration complete`; the regenerated controller, additive bolt clip, and prefabs were audited against the exact source contract, and the scene's variant-prefab reference was verified. The regenerated scene file is excluded from this batch because its remaining diff is local-ID/order churn plus unrelated URP light-data removal. Post-integration restore and static C# build both pass with 0 warnings/errors. The subsequent live-play log contains no gameplay C#, `NullReferenceException`, `MissingReferenceException`, or `InvalidOperationException` errors; its remaining Unity MCP signature/licensing warnings are external to gameplay code. Regression coverage has also been added for camera profiles, floor-safe orbit, layer contracts, viewport framing, and staged hip fire.

Current Unity EditMode/PlayMode execution and a new development build remain pending. An isolated Test Runner attempt in a temporary project copy could not acquire a licence while the live Unity editor was open, so it produced no XML result and is not counted as a pass. Actual-input and owner checks also remain open; the prior 35/35 EditMode, 4/4 PlayMode, and development-build results apply only to the earlier Generator 111 candidate.

The latest owner-reported camera and hip-fire corrections are integrated and pass static/generated-asset checks plus a live log smoke check, but are not claimed as full Unity test/build verified or owner-accepted until the current suites, development build, and hands-on matrix pass. True through-scope ADS remains separate later work. The exact matrix and status are recorded in `ROADMAP.md`. See `Assets/Game/Documentation/PROJECT.md` for architecture and phase details.
