# Powered Suit Combat and Animation Roadmap

This roadmap tracks the requested weapon handling, locomotion, aiming, and Precision Rifle pass. Generator 111 is the current verified candidate; Generator 110 and the legacy Unity assets remain the rollback baseline.

## Status rules

- `[x]` means implementation and objective automated/technical verification are complete.
- `[ ]` means work or the stated verification is still outstanding.
- A milestone's final `accepted` box remains unchecked until the user has exercised that feature in `PoweredSuitAimDemo` and accepted its feel and appearance.

## Progress summary

| Milestone | Implementation | Automated/technical verification | User play acceptance |
| --- | --- | --- | --- |
| Contracts and rollback | Complete | Complete | Pending |
| Data-driven Precision Rifle | Complete | Complete | Pending |
| Ready/stowed poses | Complete | Complete | Pending |
| Draw/sheathe | Complete | Complete | Pending |
| Forward/backward locomotion | Complete | Complete | Pending |
| Aim-walk and shoulder camera | Complete for forward/backward movement | Hotfix targeted checks complete; full-suite rerun pending | Pending |
| Magazine reload | Complete | Complete | Pending |
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
- [ ] Play-check aimed W/S and firing for weapon visibility, target readability, unobstructed reticle, and stable sight picture.
- [ ] Author dedicated left/right aim-strafe clips if the current lateral locomotion fallback is not acceptable.
- [ ] Milestone 5 accepted.

## Milestone 6 — Magazine reload

- [x] Create `PS_Reload`, including magazine removal, travel, insertion, and return to the weapon.
- [x] Animate only the contract-approved magazine controls while keeping receiver and sightline rigid.
- [x] Align ammunition commit to authored frame 75/84 rather than button press.
- [x] Block firing and carry transitions while reloading; block reload in flight.
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

## Immediate play-feedback hotfix — 2026-08-09

- [x] Reproduce the firing face-plant and trace it to Unity's Generic override-layer transition applying the FBX `-90` degree axis pose to the Animator GameObject.
- [x] Generate Unity-owned weapon-action clips that whitelist only spine/arm and `WeaponRoot`/magazine/bolt curves, with Write Defaults disabled.
- [x] Lock the imported Animator root at its authored identity transform after animation evaluation; controller movement and the non-animated facing wrapper remain authoritative.
- [x] Exercise Draw, Sheathe, Reload, and Bolt Cycle through the real controller graph with `0` degree root rotation, zero position/scale drift, and more than `1.55 m` head-to-feet clearance.
- [x] Move the aim camera to the rifle's local `-X` shoulder and widen its composition to distance `3.4`, offset `(-1.6, 0.3, 0)`, and FOV `58`.
- [x] Measure the camera change with an occlusion diagnostic: visible rifle silhouette improves from roughly `10%` to `35%` while muzzle and ocular remain on screen.
- [ ] Rerun the expanded full EditMode and PlayMode suites after Unity's local headless entitlement is restored; the restart currently reports `com.unity.editor.headless was not found`.
- [ ] User rechecks firing, reload, bolt cycle, and right-mouse aiming in `PoweredSuitAimDemo`.

## Camera framing and smoothness hotfix — 2026-08-09

- [x] Confirm the normal camera was not stuck in aim mode or collision: the live state was non-aiming at the full configured `5 m` and `60` degree FOV with no external obstruction.
- [x] Widen normal exploration framing to distance `6 m`, height `1.55 m`, and FOV `65` while preserving the rifle-readable `3.4 m` / `58` degree aim profile.
- [x] Replace frame-dependent camera interpolation with exponential damping and add mild orbit smoothing without moving controller motion into `FixedUpdate`.
- [x] Replace steady-state allocating camera and aim casts with reusable hit buffers; preserve a correctness fallback for saturated buffers.
- [x] Make camera collision pull in immediately and recover smoothly after clearing cover.
- [x] Add a demo frame-pacing policy: synchronize to displays at 60 Hz or faster, retain a 60 FPS fallback, and keep the simulation active when editor focus changes.
- [x] Avoid dirtying the 106-renderer animated subtree twice per frame by restoring the imported Animator root only when a transform channel actually drifts.
- [x] Profile the live demo at native `3422x1230`: roughly `4.2 ms` frame / `2.0 ms` render, `25` SetPass calls, and `42k` visible triangles, confirming comfortable 60 FPS headroom.
- [x] Add camera math, collision recovery, frame-policy, prefab contract, and PlayMode framing regression coverage; C# solution compiles with zero warnings/errors.
- [ ] Rerun the new focused Unity fixtures after the current MCP test-runner session is re-established.
- [ ] User accepts normal framing, orbit smoothness, cover collision recovery, aim transition, and presentation smoothness in `PoweredSuitAimDemo`.

## Final release gate

- [x] Run a clean Blender build from immutable source; all 17 actions, geometry/rigidity/contact checks, and 32 required renders pass.
- [x] Technically review and hash all 32 pose, transition, locomotion, reload, bolt, aim, and rifle views before gated export.
- [x] Import additively while preserving legacy FBXs, prefab/controller GUIDs, and Generator 110 rollback evidence.
- [x] Update the Animator controller in place and verify both layers and their Avatar Mask.
- [x] Original Generator 111 candidate compiled with 0 warnings/errors and passed the 35-test EditMode and 4-test PlayMode suites.
- [ ] Rerun the expanded suites containing the Animator-root and camera regression assertions after the local headless-license issue is repaired.
- [x] Produce a Windows x64 Development build.
- [x] Update README, project architecture, controls, weapon tuning, asset provenance, and verification totals.
- [ ] Complete the manual `PoweredSuitAimDemo` matrix: ready, stowed, draw, sheathe, W, S, aimed W/S, fire, empty fire, partial reload, empty reload, critical hit, flight regression, camera collision, and repeated state transitions.
- [x] Commit and push the reviewed candidate to GitHub.
- [ ] User accepts the combat-and-animation pass.

## Explicitly deferred

The architecture supports more weapon definitions, but this pass does not add an inventory, loot generation, weapon switching UI, procedural animation, multiplayer, crafting, or a full arsenal. Dedicated lateral aim-strafe clips are also deferred unless the current fallback fails play review.
