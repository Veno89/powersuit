# PowerSuit Polished Tech Demo / Reusable Framework Roadmap

PowerSuit remains a game first. The immediate milestone is a small, exceptionally polished combat-and-flight sandbox whose underlying systems are modular, configurable, documented, asset-agnostic, and reusable enough to approach commercial Unity-package quality naturally. It is not a pivot into a generic framework at the expense of the game.

Generator 111 is the current technically verified character/Precision Rifle candidate; Generator 110 and the legacy Unity assets remain the rollback baseline. The detailed Generator 111 pass history is preserved below, but it is only one part of this broader milestone.

## Status rules

- `[x]` means implementation and objective automated/technical verification are complete.
- `[ ]` means work or the stated verification is still outstanding.
- A milestone's final `accepted` box remains unchecked until the user has exercised that feature in `PoweredSuitAimDemo` and accepted its feel and appearance.

## Completed

- Generator 111 suit/rifle source, 17 exact clips, 32-view Blender validation, rollback evidence, and additive Unity integration.
- CharacterController ground/flight baseline, physical projectile combat, basic enemies, combat feedback, and editor setup tooling.
- One data-driven Precision Rifle with finite ammunition, reload timing, critical hits, manual bolt cycling, and a masked weapon-action layer.
- Initial camera collision/damping/non-allocation work, frame pacing, root-transform containment, and a successful Windows development build.
- Current integrated camera profiles: grounded `9.5 m` / `1.5 m` / `72` degrees, flight `11 m` / `1.75 m` / `74` degrees, and shoulder aim `4.3 m` / `1.45 m` / local offset `(-1.2, 0.05, 0)` / `62` degrees.
- An integrated four-layer architecture in exact order: `Base`, masked override `Forward Weapon Pose`, masked additive `Bolt Cycle Action`, and masked override `Weapon Actions`; airborne reload remains available from stable `Ready`.
- A staged hip-fire request path that prepares the forward pose and body heading before committing an accepted shot, without enabling aim zoom/state/spread, plus a floor-safe grounded orbit calculation.

These are implementation/technical results, not claims that movement, flight, camera, aiming, animation, or combat feel has received final owner acceptance.

## Current

The latest camera and hip-fire correction is integrated. `Logs/Editor.log` records the completed Generator 109 integration; the regenerated controller, additive clip, and prefabs match the exact source contract; the scene's variant-prefab reference is verified; and a post-integration restore/static build passes with 0 warnings/errors. The regenerated scene file is excluded because its remaining diff is local-ID/order churn plus unrelated URP light-data removal. A subsequent live-play log has no gameplay C# or common Unity reference/operation exceptions; only Unity MCP signature/licensing warnings remain. The current Unity EditMode/PlayMode suites, a current development build, actual-input checks, and owner acceptance are still outstanding. An isolated Test Runner attempt in a temporary project copy was stopped because the concurrent live editor held the Unity licence, so no XML test result exists and no test pass is claimed. The Unity Game view must be set to **Fit** or `1x`: a saved local `4x`, panned layout was independently cropping the render and producing a false appearance of runtime zoom. True weapon-specific through-scope ADS remains separate later work; RMB still uses the shared third-person shoulder profile.

## Next

1. Run the current EditMode and expanded PlayMode suites, validate a current development build, and execute the concise owner matrix at Game view **Fit**/`1x` using actual hip-fire/aim/reload/flight inputs while moving, ascending, descending, boosting, and recovering from camera collision.
2. Audit and tune broader ground/flight feel across acceleration, braking, takeoff, hover, boost, landing, strafing, camera relationship, and 30/60/120+/uncapped frame rates.
3. Harden animation layering, airborne combat poses, IK/hardpoints, transitions, and replacement-asset assumptions.
4. Continue gunplay polish, then add a small set of meaningfully distinct data-driven weapons, improved enemy roles, 1-2 reusable abilities, sandbox polish/performance, and replacement-character validation.

## Milestone phase status

| Phase | Honest current status |
| --- | --- |
| A - Stability and architecture | In progress; integration, generated-asset audit, and static build pass, while current Unity test execution and the repository-wide coupling/asset-assumption audit remain |
| B - Movement and camera | Wider ground/flight/shoulder profiles and floor-safe orbit are implemented in source; Unity runtime framing and owner acceptance remain |
| C - Animation | Partial; the four-layer forward-pose/additive-cycle/action assets pass integration audit, while current Unity test execution, broader flight transitions, IK, and retargeting remain |
| D - Gunplay | Partial; one Precision Rifle pipeline exists, while feel, true scope, cover, recoil, and pooling/GC work remain |
| E - Weapon modularity | Partial; one data-driven rifle exists, while hardpoints, switching, and additional archetypes remain |
| F - Enemy combat | Basic baseline only; range management, roles, flight response, and encounter quality remain |
| G - Abilities | Not started |
| H - Sandbox and performance | Partial; one profile snapshot exists, while representative load, GC/FPS matrix, debug tools, and arena coverage remain |
| I - Asset replacement validation | Not started |

## Historical owner-reported flight and camera regressions - 2026-08-09

This section records the superseded `7.5 m` / `9 m` / `3.4 m` pass and its evidence. Checked boxes distinguish its completed implementation/technical verification from the unchecked expanded fixture and owner play acceptance; these values are not the current profiles.

- [x] Reproduce the normal-camera complaint and record the live state: airborne, not aiming, unobstructed, FOV `65`, distance `6 m`, and approximately 39% viewport-height occupancy.
- [x] Add configurable wider exploration profiles: grounded `7.5 m` / `1.65 m` / `68` degrees and flight `9 m` / `1.9 m` / `72` degrees, while retaining the shared shoulder aim at `3.4 m` / `58` degrees on local `-X`.
- [x] Permit reload from stable `Ready` while airborne without grounding or cancelling the active flight presentation; direct live verification completed a reload from `4` to `5` rounds.
- [x] Add a masked Airborne Aim layer over `Hover`, keep Weapon Actions above it, and preserve hover/aim beneath reload; the physical bore remained forward at dot `0.9997` before and after reload.
- [x] Pass the generated asset validator/integration checks, the focused `Generator109IntegrationTests` (`3/3` EditMode), the direct live profile/aim/reload probe, and screenshot inspection.
- [ ] Run the expanded Unity PlayMode fixture; the TestRunner API did not start the requested fixture in this verification session.
- [ ] Exercise actual aim/reload/flight inputs while moving, ascending, descending, and boosting; verify repeated/empty/partial reload behaviour, camera recovery, and uninterrupted control.
- [ ] Verify complete camera-to-reticle-to-muzzle convergence in flight and prevent firing through the suit or nearby cover.
- [ ] Design and implement true Precision Rifle through-scope ADS separately: weapon-specific `ScopePoint`, scope FOV/profile, reticle/overlay, transition, hardpoint validation, and close-cover behaviour.
- [ ] Owner re-evaluates the revised shoulder composition for weapon visibility, target readability, reticle clearance, and motion comfort.

## Progress summary

| Milestone | Implementation | Automated/technical verification | User play acceptance |
| --- | --- | --- | --- |
| Contracts and rollback | Complete | Complete | Pending |
| Data-driven Precision Rifle | Complete | Complete | Pending |
| Ready/stowed poses | Complete | Complete | Pending |
| Draw/sheathe | Complete | Complete | Pending |
| Forward/backward locomotion | Complete | Complete | Pending |
| Aim-walk and shoulder camera | Ground clips plus current `9.5 m` / `11 m` / `4.3 m` profiles and forward-pose layer integrated | Generated-asset audit, static build, and live-log smoke check pass; current EditMode/PlayMode execution pending | Rejected prior framing; retest required at Fit/1x |
| Magazine reload | Grounded and stable-Ready airborne implementation complete | Grounded checks plus direct live airborne `4 -> 5` reload passed; moving/input PlayMode matrix pending | Retest required |
| Manual bolt cycle | Complete | Complete | Pending |

## Milestone 0 — Baseline and contracts

- [x] Preserve the approved Generator 110 FBX, validation evidence, Aim pose, physical muzzle helper, and rollback path.
- [x] Define carry states in plain C#: `Ready`, `Drawing`, `Stowed`, and `Sheathing`; keep reload/cycle activity in the plain-C# weapon runtime.
- [x] Define transition and interruption rules: firing only while Ready, reload blocks firing, aim from Stowed requests Draw, and carry changes cannot interrupt Reload/Cycling.
- [x] Extend the Blender contract with explicit movable `Magazine` and `Bolt` components while keeping the remaining rifle rigid.
- [x] Validate that only contract-approved articulated components can move and include their semantics in the rigid signature.
- [x] Retain compatibility clips and pass the existing Unity tests before promotion.
- [ ] User accepts the preserved baseline and transition behavior in the demo.
- [ ] Milestone 0 accepted.

## Milestone 1 — Data-driven Precision Rifle

- [x] Add reusable `WeaponDefinition` ScriptableObjects.
- [x] Add testable plain-C# magazine, reserve, cadence, reload, critical, and optional manual-cycle state.
- [x] Refactor `PowerSuitWeapon` into the Unity input/projectile/effects/HUD adapter.
- [x] Support weapon identity/class, trigger mode, damage, RPM, magazine/reserve capacity, reload timing, criticals, projectile behavior, spread, recoil, and manual cycling.
- [x] Create and wire `PrecisionRifle.asset`: 60 damage, 45 RPM, 5/25 ammunition, 2.8-second reload, 10% critical chance, 2.0x critical damage, 100 m/s projectile, and 0.67-second manual cycle.
- [x] Add a finite-ammo HUD with READY, RELOADING, CYCLING, and EMPTY feedback.
- [x] Validate invalid definitions and deterministic critical boundaries.
- [x] Verify cadence, consumption, empty blocking, partial/full reload, reserve transfer, commit timing, cycle behavior, and the authored asset in EditMode.
- [x] Verify the demo prefab uses `PrecisionRifle.asset` and the runtime state in PlayMode.
- [ ] User accepts weapon feel, cadence, HUD, and initial tuning.
- [ ] Milestone 1 accepted.

## Milestone 2 — Ready and stowed poses

- [x] Author and validate the back-mounted rifle location with armor/jetpack clearance.
- [x] Create `PS_WeaponStowed_Idle` with the rifle diagonally across the back.
- [x] Create `PS_WeaponReady_Idle` with the rifle held diagonally in both arms in front of the chest.
- [x] Preserve rifle rigidity and physical stock-to-muzzle direction in both poses.
- [x] Produce and technically review the required ready/stowed/contact renders.
- [x] Pass clean Blender rigidity, contact, finite-transform, action, and framing checks.
- [x] Import both clips by exact name without root motion or facing regression.
- [x] Confirm the Generator 110 face-winding fix remains active; no hollow/back-face geometry is present in the exported candidate.
- [ ] User accepts the ready and stowed silhouettes in Play mode.
- [ ] Milestone 2 accepted.

## Milestone 3 — Draw and sheathe

- [x] Create `PS_Weapon_Draw` from back-mounted to ready.
- [x] Create `PS_Weapon_Sheathe` from ready to back-mounted.
- [x] Keep the rifle on one continuous animated `WeaponRoot` so no attachment swap or pop is required.
- [x] Drive transitions from `PowerSuitWeaponPresentation`; `Q` requests state changes rather than forcing Animator states.
- [x] Gate firing, aiming, reload, flight carry changes, and repeated transition input.
- [x] Verify legal/illegal transitions and presentation fire/reload gates in EditMode.
- [ ] Play-check repeated `Q` cycles for snapping, hand discontinuity, camera jumps, and back-mounted muzzle firing.
- [ ] Milestone 3 accepted.

## Milestone 4 — Directional grounded locomotion

- [x] Expose signed local movement, normalized speed, backpedal, and aim-walk state from actual controller velocity.
- [x] Create improved `PS_Walk_Forward` and `PS_Walk_Backward` clips.
- [x] Make `S` travel backward while the suit continues facing forward instead of rotating 180 degrees toward the player.
- [x] Add ready/stowed forward and backward locomotion blends.
- [x] Match Animator playback speed to controller travel speed to reduce obvious skating/moonwalking.
- [x] Verify signed W/S movement, facing resolution, playback-speed calculation, and backpedal selection in EditMode/PlayMode.
- [ ] Play-check foot planting at normal speed and slow motion, including diagonal direction changes.
- [ ] Milestone 4 accepted.

## Milestone 5 — Aim while walking and shoulder camera

- [x] Create `PS_Aim_Walk_Forward` and `PS_Aim_Walk_Backward` with a stable shouldered upper body and moving legs.
- [x] Support stationary aim plus forward/backward aim-walk without lowering or mirroring the rifle.
- [x] Keep muzzle, optic, stock, and grip contacts stable through the authored gait.
- [x] Retune camera distance, shoulder offset, height, and FOV to show more weapon and less back/helmet.
- [x] Preserve collision, reticle alignment, physical muzzle direction, and the rule that firing never aims through the player.
- [x] Add aim-walk renders and runtime orientation/muzzle tests.
- [x] Replace the owner-rejected exploration composition in source with grounded (`9.5 m` / `1.5 m` / `72`) and flight (`11 m` / `1.75 m` / `74`) profiles, plus shoulder aim (`4.3 m` / `1.45 m` / offset `(-1.2, 0.05, 0)` / `62`).
- [x] Keep the `Hover` base while applying the masked `Forward Weapon Pose`, with additive `Bolt Cycle Action` above it and override `Weapon Actions` at highest priority.
- [ ] Implement true weapon-specific scope presentation as a separate follow-on from the current global third-person shoulder-aim mode.
- [ ] Play-check aimed W/S and firing for weapon visibility, target readability, unobstructed reticle, and stable sight picture.
- [ ] Author dedicated left/right aim-strafe clips if the current lateral locomotion fallback is not acceptable.
- [ ] Milestone 5 accepted.

## Milestone 6 — Magazine reload

- [x] Create `PS_Reload`, including magazine removal, travel, insertion, and return to the weapon.
- [x] Animate only the contract-approved magazine controls while keeping receiver and sightline rigid.
- [x] Align ammunition commit to authored frame 75/84 rather than button press.
- [x] Block firing and carry transitions while reloading under the original grounded-only contract.
- [x] Supersede the original flight prohibition with reload from stable airborne `Ready`; the live probe preserved hover/aim and completed one ammunition commit from `4` to `5`.
- [ ] Play-check airborne reload with actual input while moving and boosting, including empty, partial, repeated-input, and interruption cases.
- [x] Verify empty, partial, full, insufficient-reserve, repeated-input, cancellation, and exactly-once commit behavior.
- [x] Technically review exposed frame-50 and frame-64 magazine/hand contact renders.
- [ ] Play-check HUD, animation, firing lock, magazine ownership, magwell alignment, and repeated reloads.
- [ ] Decide whether taking damage should interrupt reload in a future combat-state pass.
- [ ] Milestone 6 accepted.

## Milestone 7 — Manual bolt cycle

- [x] Add contract-approved bolt/charging components and travel limits.
- [x] Create `PS_BoltCycle` with synchronized mechanism and firing-hand motion.
- [x] Enable manual cycling from `WeaponDefinition`, leaving future automatic weapons unaffected.
- [x] Require both cadence and manual cycle completion before the next accepted shot.
- [x] Verify one cycle per accepted shot, no cycle for rejected/empty shots, blocking before completion, and bypass for non-manual weapons.
- [x] Technically review the bolt close-up and verify the action layer preserves lower-body locomotion.
- [ ] Play-check mechanical plausibility, hand contact, sight recovery, and fire/reload/draw transitions.
- [ ] Milestone 7 accepted.

## Historical immediate play-feedback hotfix — 2026-08-09

- [x] Reproduce the firing face-plant and trace it to Unity's Generic override-layer transition applying the FBX `-90` degree axis pose to the Animator GameObject.
- [x] Generate Unity-owned weapon-action clips that whitelist only spine/arm and `WeaponRoot`/magazine/bolt curves, with Write Defaults disabled.
- [x] Lock the imported Animator root at its authored identity transform after animation evaluation; controller movement and the non-animated facing wrapper remain authoritative.
- [x] Exercise Draw, Sheathe, Reload, and Bolt Cycle through the real controller graph with `0` degree root rotation, zero position/scale drift, and more than `1.55 m` head-to-feet clearance.
- [x] Move the aim camera to the rifle's local `-X` shoulder and widen its composition to distance `3.4`, offset `(-1.6, 0.3, 0)`, and FOV `58`.
- [x] Measure the camera change with an occlusion diagnostic: visible rifle silhouette improves from roughly `10%` to `35%` while muzzle and ocular remain on screen.
- [ ] Rerun the expanded full EditMode/PlayMode suites; focused `3/3` EditMode integration passed, but the Unity TestRunner API did not start the requested new PlayMode fixture.
- [ ] User rechecks firing, reload, bolt cycle, and right-mouse aiming in `PoweredSuitAimDemo`.

## Historical camera framing and smoothness hotfix — 2026-08-09

- [x] Confirm the normal camera was not stuck in aim mode or collision: the live state was non-aiming at the full configured `5 m` and `60` degree FOV with no external obstruction.
- [x] Widen normal exploration framing to distance `6 m`, height `1.55 m`, and FOV `65` while preserving the rifle-readable `3.4 m` / `58` degree aim profile.
- [x] Replace frame-dependent camera interpolation with exponential damping and add mild orbit smoothing without moving controller motion into `FixedUpdate`.
- [x] Replace steady-state allocating camera and aim casts with reusable hit buffers; preserve a correctness fallback for saturated buffers.
- [x] Make camera collision pull in immediately and recover smoothly after clearing cover.
- [x] Add a demo frame-pacing policy: synchronize to displays at 60 Hz or faster, retain a 60 FPS fallback, and keep the simulation active when editor focus changes.
- [x] Avoid dirtying the 106-renderer animated subtree twice per frame by restoring the imported Animator root only when a transform channel actually drifts.
- [x] Profile the live demo at native `3422x1230`: roughly `4.2 ms` frame / `2.0 ms` render, `25` SetPass calls, and `42k` visible triangles, confirming comfortable 60 FPS headroom.
- [x] Add camera math, collision recovery, frame-policy, prefab contract, and PlayMode framing regression coverage; C# solution compiles with zero warnings/errors.
- [x] Release the masked Weapon Actions layer to zero weight after draw, sheathe, reload, or bolt cycle so a completed action cannot retain the chest-ready pose over stationary or moving aim.
- [x] Add an end-to-end PlayMode regression for controller aim -> `Aim Locomotion` -> bolt cycle -> forward rifle aim recovery.
- [x] Run generated asset validation/integration and the focused `Generator109IntegrationTests` (`3/3` EditMode); the full .NET solution compiles with 0 warnings/errors.
- [x] Replace the owner-rejected `6 m` / `65` degree composition with grounded `7.5 m` / `68` and flight `9 m` / `72` profiles; retain the earlier live reproduction above as the reason for the change.
- [ ] Run the expanded Unity PlayMode fixture; the TestRunner API did not start it during this verification session.
- [ ] User accepts revised ground/flight framing, orbit smoothness, cover collision recovery, shoulder-aim transition, and presentation smoothness in `PoweredSuitAimDemo`.

## Camera framing and staged hip-fire correction — 2026-08-10

- [x] Set the current source profiles to ground `9.5 m` / `1.5 m` / `72`, flight `11 m` / `1.75 m` / `74`, and shoulder aim `4.3 m` / `1.45 m` / local offset `(-1.2, 0.05, 0)` / `62`.
- [x] Add a pure, testable floor-safe minimum-pitch calculation so a low grounded orbit cannot place the camera collision sphere below its required clearance and cause false collision zoom.
- [x] Split the generated controller source into exact layer order `Base`, override `Forward Weapon Pose`, additive `Bolt Cycle Action`, and override `Weapon Actions`; keep draw/sheathe/reload on the highest override layer.
- [x] Stage otherwise valid non-aim fire through `RequestFire`: face the camera combat ray and prepare the forward pose first, then commit the gameplay shot on the following update without enabling aim state, aim FOV, or aim spread. Blocked requests do not rotate or stage the suit.
- [x] Add source regression coverage for exact profiles, floor-safe pitch, layer order/blending, viewport containment, and hip-fire pose/camera semantics; the full C# solution compiles with 0 warnings/errors.
- [x] Diagnose the editor-only false zoom: the saved Game view was at `4x` and panned. Set Game view Scale to **Fit** or `1x` before any framing review.
- [x] Regenerate the Unity controller, additive bolt clip, and prefabs through the supported integration command; audit those assets against the exact source contract and verify that `PoweredSuitAimDemo` still references the generated player variant. Exclude the scene's unrelated serialization churn from the batch.
- [x] Pass post-integration restore/static build with 0 warnings/errors and inspect a live-play log with no gameplay C#, null/missing-reference, or invalid-operation errors.
- [ ] Run the current EditMode and expanded PlayMode suites. The isolated temporary-copy attempt was blocked by the live editor's Unity licence and produced no XML result.
- [ ] Validate a current Windows development build.
- [ ] Owner accepts normal, flight, and shoulder framing plus forward-rifle hip fire and bolt-cycle presentation in `PoweredSuitAimDemo` at Game view Fit/1x.

## Generator 111 combat-and-animation pass gate

- [x] Run a clean Blender build from immutable source; all 17 actions, geometry/rigidity/contact checks, and 32 required renders pass.
- [x] Technically review and hash all 32 pose, transition, locomotion, reload, bolt, aim, and rifle views before gated export.
- [x] Import additively while preserving legacy FBXs, prefab/controller GUIDs, and Generator 110 rollback evidence.
- [x] Update and verify the original Generator 111 candidate controller and its then-current masked layers; the later four-layer source replacement is tracked separately above.
- [x] Original Generator 111 candidate compiled with 0 warnings/errors and passed the 35-test EditMode and 4-test PlayMode suites.
- [x] Run generated asset validator/integration checks and focused `Generator109IntegrationTests` (`3/3` EditMode) for the new framing/airborne batch.
- [ ] Run the expanded PlayMode fixture containing camera, airborne aim/reload, action-layer, and Animator-root assertions; the TestRunner API did not start it during this verification session.
- [x] Produce a Windows x64 Development build.
- [x] Update README, project architecture, controls, weapon tuning, asset provenance, and verification totals.
- [ ] Complete the manual `PoweredSuitAimDemo` matrix: ready, stowed, draw, sheathe, W, S, aimed W/S, fire, empty fire, partial reload, empty reload, critical hit, normal/aim framing, airborne shoulder aim, airborne reload, flight movement/boost during weapon actions, camera collision, and repeated state transitions.
- [x] Commit and push the reviewed candidate to GitHub.
- [ ] User accepts the combat-and-animation pass.

## Explicitly deferred

Deferred beyond the polished sandbox milestone are multiplayer, crafting, story/campaign content, a full inventory/loot/progression loop, procedural animation, a large arsenal, and Asset Store publication itself. Dedicated lateral aim-strafe clips remain conditional on play review.

The broader milestone does **not** defer a small 3-4 weapon set and switching flow, true weapon-specific scope support, improved enemy roles, 1-2 reusable suit abilities, representative sandbox/performance validation, or replacement-character and replacement-weapon tests.
