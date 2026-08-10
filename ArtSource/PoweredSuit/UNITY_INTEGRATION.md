# Unity Integration Record

Generator 112 is the active Unity evaluation candidate. Legacy files at `Assets/Game/Models/powersuit_animated.fbx`, `powersuit_rigged.fbx`, and `powersuit_rifle.fbx` remain unchanged for rollback and GUID stability. Generator 110 and Generator 111 evidence/FBXs are preserved under their named validation archives.

## Generator 112 integration

1. The Blender report says `PASS`, `APPROVED`, and `export_allowed: true`; approval covers exactly 33 renders and the export manifest records the validated blend and FBX hashes.
2. The gated FBX is imported at `Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx` using its existing `.meta` GUID `f48464ae4ba58b54f976e658ece758b3`. It uses a Generic rig, disables camera/light import, retains hierarchy, disables optimization, and defines exactly the 18 manifest clips.
3. The active Unity FBX SHA-256 is `054b5a1875730b225cbb9192bbf760a75919126043ce3ae1503308d21fa8e409`, byte-identical to the approved Generator 112 export and archive.
4. `PlayerPrototype_Generator109.prefab` remains the additive variant. Its historical name is retained for GUID/reference continuity; only its nested presentation model, animation wiring, and weapon definition are updated.
5. The imported armature contains `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt`, so suit, rifle, magazine, and bolt motion remain synchronized in one FBX take per clip.
6. A non-animated wrapper uses the measured facing correction `AngleAxis(+90 degrees, X) * Euler(0 degrees, 180 degrees, 0 degrees)`. The Animator writes the imported root, so the correction must remain on the wrapper.
7. `PowerSuitWeapon.MuzzleTransform` is bound to an axis-correct child of imported `Rifle_Muzzle`. The measured adapter aligns with the physical bore; do not replace it with a fixed floating muzzle.
8. `PowerSuitAnimator.controller` is updated in place. Its base layer handles ready/stowed/aim locomotion, run, and hover; `Forward Weapon Pose` supplies the shouldered upper-body pose, `Bolt Cycle Action` applies the additive mechanism/hand motion, and the highest masked `Weapon Actions` layer owns draw, sheathe, and reload.
9. The `Run Locomotion` state uses the authored `PS_Run_Forward` clip at fixed `1.35x` playback and is driven by the `IsRunning` bool. Do not inherit the walk state's movement-speed playback parameter; reverse movement continues to use the authored backpedal.
10. Do not bind raw FBX action takes directly to the override layer. Integration creates `_UpperBody.anim` copies containing only `Root/Hips/Spine...`, `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt` curves, and keeps Write Defaults off.
11. `PowerSuitUpperBody.mask` must keep both arm chains and all three weapon controls active while excluding Animator root, `Root`, `Hips`, pelvis, and upper legs.
12. `PowerSuitAnimatorRootLock` must remain on `PowerSuitModel_Generator111`. Its historical name is retained for continuity. Unity's Generic Animator synthesizes the FBX axis/default pose while blending from an empty override state; the lock restores the imported model's identity transform after evaluation.
13. `PowerSuitWeaponPresentation` owns carry transitions and blocks firing/reload while unavailable. `PowerSuitWeaponAnimationDriver` relays accepted runtime action starts into Animator triggers.
14. `PrecisionRifle.asset` supplies damage, cadence, ammunition, reload, critical, projectile, spread/recoil, manual-cycle, and sniper-scope eligibility data. The C# runtime remains authoritative; its 2.8-second reload, 0.89 normalized commit, and 0.67-second cycle align with the authored frames.

## Verified import and integration contract

- exact 18-clip ModelImporter configuration and unique matching takes
- exactly 18 imported `AnimationClip` subassets
- `PS_Run_Forward`: Unity frames 0-20, 30 FPS, `0.667 s`, loop time/pose enabled
- existing FBX GUID and all 17 Generator 111 clip entries preserved; only the run entry was added
- one active Animator and stable model-wrapper correction
- run state uses `PS_Run_Forward`, `IsRunning`, and fixed `1.35x` playback
- physical visor/bore/muzzle direction and no firing through the player
- base locomotion continues under reload and bolt-cycle actions
- Ready/Stowed and Hover/Stowed Hover transitions
- stable-ready reload works on both ground and in flight
- prefab/scene wiring, safe spawns, camera composition, controller, HUD, weapon definition, and imported helpers
- full C# solution: 18 assemblies, 0 warnings, 0 errors
- EditMode: 222/222 passed
- PlayMode: 12/12 passed
- Unity Console: 0 errors
- Windows x64 Development build: succeeded on 2026-08-10 at 17:50
- 15-second headless smoke: no gameplay exception, assertion, or missing-reference patterns

Unity's AI Inference/Sentis package emitted non-blocking third-party shader performance warnings during the Development build. No compiler, test, scene-load, smoke, or build failure resulted.

## Manual promotion check

Open `Assets/Scenes/PoweredSuitAimDemo.unity`, press Play, and run the matrix in the root `ROADMAP.md`: walk/run transitions, forward sprint cadence, W/S, quick jump, held-jump flight entry, automatic landing, ready/stowed, repeated draw/sheathe, aimed W/S, fire/empty fire, partial/empty reload, bolt cycle, flying reload/aim, sniper-only scope/reticle, camera collision, and repeated interruption attempts. Preserve Generators 110 and 111 until that review is accepted.
