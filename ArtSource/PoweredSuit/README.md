# Powered Suit Blender Pipeline

This directory is the authoritative Blender authoring source and deterministic tooling for the Powered Suit character and its reusable rigid-weapon framework.

## Current status

Approved Generator 112 candidate:

- Blender: 5.2 LTS
- rifle generator: 111
- weapon contract: v3; rigid signature: v6
- animation contract: v3
- automated validation: `PASS`
- technical visual validation: `APPROVED`
- export allowed: `true`
- required renders: 13 aim + 5 rifle + 15 weapon-animation = 33
- exported clips: 18 exact armature actions
- generated blend SHA-256: `0295acb528b0ca8c0f3ec68f642ad6c56c3f4ddaf9fe2f5a0ab84adae9311876`
- exported FBX SHA-256: `054b5a1875730b225cbb9192bbf760a75919126043ce3ae1503308d21fa8e409`

The clean build passed with no automated blockers. All 33 mandatory renders were technically reviewed and hash-locked before the gated export on 2026-08-10. The exact report, approval, images, export manifest, and FBX are archived under `Validation/Generator112`.

Generator 110 remains the winding/closed-geometry repair baseline, and Generator 111 remains the 17-action rollback candidate. Generator 112 preserves their geometry, rig, weapon controls, and original action ranges while adding the synchronized `PS_Run_Forward` loop. The run uses frames 1-21 at 30 FPS, a 20-frame cycle with a native cadence of 180 steps per minute.

## What is authoritative

- `source/powersuit_source.blend` — immutable audited recovered input
- `scripts/` — active build, animation, validation, approval, and export tools
- `Validation/Generator109/` through `Validation/Generator112/` — immutable candidate evidence and rollback artifacts
- `WEAPON_FRAMEWORK.md` — weapon/stance architecture reference
- `PROVENANCE.md` — exact lineage and historical Reset06 diagnosis
- `UNITY_INTEGRATION.md` — safe Unity integration procedure
- `audit/legacy/` — frozen evidence about recovered older iterations
- `RESET_01.md` through `RESET_06.md` — historical changelog, not active code

Active `powersuit_pipeline.blend`, working `renders/`, working `exports/`, Blender backups, machine-local Blender paths, and Python bytecode are generated and ignored. Named validation archives are deliberate versioned exceptions.

## Build and review on Windows

1. If needed, run `00_SET_BLENDER_PATH_WINDOWS.bat` once.
2. Run `01_BUILD_AND_RENDER_WINDOWS.bat`.
3. Confirm `renders/validation_report.json` says `PASS` with no blockers.
4. Inspect every PNG under `renders/aim_validation`, `renders/rifle_validation`, and `renders/weapon_animation_validation`.
5. Reject and revise if pose readability, hand contact, stock/sight alignment, camera framing, locomotion, draw/sheathe continuity, magazine handling, bolt handling, clipping, or frame stability remains unacceptable.
6. Only after genuine technical visual acceptance, run `02_APPROVE_AND_EXPORT_WINDOWS.bat` and type `APPROVE` when prompted.

The build launcher resets the working scene from the audited source, runs all modelling/rig/pose/animation stages, renders all three validation sets, and writes the aggregate report. It does not depend on a hand-edited intermediate `.blend`.

## Animation set

Generator 112 exports exactly:

- `PS_Idle`, `PS_Walk`, `PS_Hover`, `PS_Aim`
- `PS_WeaponReady_Idle`, `PS_WeaponStowed_Idle`, `PS_WeaponStowed_Hover`
- `PS_Weapon_Draw`, `PS_Weapon_Sheathe`
- `PS_Walk_Forward`, `PS_Walk_Backward`
- `PS_Run_Forward`
- `PS_Aim_Walk_Forward`, `PS_Aim_Walk_Backward`
- `PS_WeaponStowed_Walk_Forward`, `PS_WeaponStowed_Walk_Backward`
- `PS_Reload`, `PS_BoltCycle`

All use one armature Action Slot. Rifle and articulated-component animation is carried by non-deforming armature controls, avoiding unsynchronized object-action FBX takes.

## Architecture boundary

The weapon is rigid beneath `RifleRoot`, except for contract-approved magazine and bolt component sets. Semantic hardpoints and contact-offset vectors are part of the rigid signature. Pose solving moves the complete weapon, adapts the character to fixed targets, removes temporary IK, and bakes synchronized armature actions. Unapproved rifle-part movement fails validation.

Blender files are authoring inputs. Unity receives only a gated FBX; gameplay state, ammunition, damage, cadence, reload, critical hits, and cycling remain in C#.
