# Unity Integration Record

Generator 109 is approved for Unity evaluation. The live legacy files at
`Assets/Game/Models/powersuit_animated.fbx`, `powersuit_rigged.fbx`, and
`powersuit_rifle.fbx` remain unchanged for rollback and GUID stability.

## Generator 109 integration

1. The Blender report says `PASS`, `APPROVED`, and
   `export_allowed: true` and that the export manifest hashes all 18 approved
   renders, the validated blend, and the FBX.
2. The new FBX is imported alongside the old model at
   `Assets/Game/Models/PoweredSuit/powersuit_animated_with_aim.fbx`. Let Unity
   create a new `.meta`/GUID; do not reuse or replace an old one.
3. Configure a Generic rig with exactly `PS_Idle`, `PS_Walk`, `PS_Hover`, and
   `PS_Aim`. Disable camera and light import. Keep hierarchy optimisation off
   until the imported `Rifle_Muzzle` helper is proven accessible.
4. Create `PlayerPrototype_Generator109.prefab` from `PlayerPrototype` and
   replace only its nested visual
   model. Preserve the player root, controller, combat, flight, camera, damage,
   and collider components.
5. Bind `PowerSuitWeapon.MuzzleTransform` to the imported `Rifle_Muzzle` and
   validate its forward/up axes. Do not retain the current approximate floating
   `WeaponMuzzle` transform.
6. Update `PowerSuitAnimator.controller` in place so `IsAiming`
   parameter enters/exits a real Aim state. Do not rerun the current controller
   generator unchanged: it deletes/recreates the controller and only builds
   Idle, Walk, and Hover.
7. Verify one active Animator, clip looping, legacy movement/flight/combat,
   muzzle/projectile origin, weapon rigidity, feet/collider alignment, camera
   clearance, URP materials, hierarchy, and absence of stray Camera/Light/Cube
   objects.
8. Run EditMode tests, PlayMode tests, and a Windows development build before
   promoting the variant. Keep the old FBXs and `.meta` files until the variant
   is accepted.

## Workspace boundary

The combat-feedback pass is now treated as one atomic Unity stabilization unit:
its tracked updates, required new component types, generated feedback prefabs,
and focused tests must be committed together. The dedicated
`PoweredSuitAimDemo` keeps its clean spawn layout separate from the older
`FlightPrototype` scene while preserving that original scene as the shared
Build Profile and rollback baseline. The focused development build supplies
the demo scene explicitly.
