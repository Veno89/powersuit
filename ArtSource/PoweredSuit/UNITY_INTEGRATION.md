# Unity Integration Record

Generator 111 is the active Unity evaluation candidate. Legacy files at `Assets/Game/Models/powersuit_animated.fbx`, `powersuit_rigged.fbx`, and `powersuit_rifle.fbx` remain unchanged for rollback and GUID stability. Generator 110 evidence and FBX are preserved under `Validation/Generator110`.

## Generator 111 integration

1. The Blender report says `PASS`, `APPROVED`, and `export_allowed: true`; the approval covers exactly 32 renders and the export manifest records the validated blend and FBX hashes.
2. The gated FBX is imported at `Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx` using its existing `.meta`/GUID. It uses a Generic rig, disables camera/light import, retains hierarchy, and defines exactly the 17 manifest clips.
3. `PlayerPrototype_Generator109.prefab` remains the additive variant. Its historical name is retained for GUID/reference continuity; only its nested presentation model, animation wiring, and weapon definition were updated.
4. The imported armature contains `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt`, so suit, rifle, magazine, and bolt motion remain synchronized in one FBX take per clip.
5. A non-animated wrapper uses the measured facing correction `AngleAxis(+90°, X) * Euler(0°, 180°, 0°)`. The Animator writes the imported root, so the correction must remain on the wrapper.
6. `PowerSuitWeapon.MuzzleTransform` is bound to an axis-correct child of imported `Rifle_Muzzle`. The measured adapter aligns with the physical bore; do not replace it with a fixed floating muzzle.
7. `PowerSuitAnimator.controller` is updated in place. Its base layer handles ready/stowed/aim locomotion and hover; the Weapon Actions layer is masked to upper body, arms, and weapon controls for draw, sheathe, reload, and bolt cycle.
8. Do not bind the raw FBX action takes directly to that override layer. Integration creates `_UpperBody.anim` copies containing only `Root/Hips/Spine...`, `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt` curves, and keeps Write Defaults off.
9. `PowerSuitUpperBody.mask` must keep both arm chains and all three weapon controls active while excluding Animator root, `Root`, `Hips`, pelvis, and upper legs.
10. `PowerSuitAnimatorRootLock` must remain on `PowerSuitModel_Generator111`. Unity's Generic Animator synthesizes the FBX `-90` degree axis/default pose while blending from an empty override state even when the destination clip is rootless; the lock restores the imported model's identity transform after evaluation.
11. `PowerSuitWeaponPresentation` owns carry transitions and blocks firing/reload while unavailable. `PowerSuitWeaponAnimationDriver` relays accepted runtime action starts into Animator triggers.
12. `PrecisionRifle.asset` supplies damage, cadence, ammunition, reload, critical, projectile, spread/recoil, and manual-cycle data. The C# runtime remains the authority; its 2.8-second reload, 0.89 normalized commit, and 0.67-second cycle align with the authored frames. Public animation completion hooks remain available for a later event-driven integration, but the current controller uses deterministic runtime timing.

## Verification

- exact 17 clip importer configuration and unique take matching
- one active Animator and stable model wrapper correction
- physical visor/bore/muzzle direction and no firing through the player
- base locomotion continues under reload and bolt-cycle actions
- Ready ↔ Stowed and Hover ↔ Stowed Hover transitions
- live reload availability follows flight state
- prefab/scene wiring, safe spawns, camera composition, controller, HUD, weapon definition, and imported helpers
- full 35-test EditMode and 4-test PlayMode suites
- Windows x64 Development build

The 2026-08-09 play-feedback hotfix additionally passed a direct controller-graph exercise of all four weapon actions with zero Animator-root drift and an upright model. Its new full-suite rerun is pending because the restarted local batch editor reports a missing `com.unity.editor.headless` entitlement.

## Manual promotion check

Open `Assets/Scenes/PoweredSuitAimDemo.unity`, press Play, and run the matrix in the root `ROADMAP.md`: ready/stowed, repeated draw/sheathe, W/S, aimed W/S, fire/empty fire, partial/empty reload, bolt cycle, flight regression, camera collision, and repeated interruption attempts. Preserve Generator 110 until that review is accepted.
