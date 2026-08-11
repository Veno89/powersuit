# Powered Suit Blender Pipeline

This directory is the authoritative Blender authoring source and deterministic tooling for the Powered Suit character and its reusable rigid-weapon framework.

## Current status

Approved Generator 114 candidate:

- Blender: 5.2 LTS
- rifle generator: 111
- weapon contract: v3; rigid signature: v6
- animation contract: v5
- automated validation: `PASS`
- technical visual validation: `APPROVED`
- export allowed: `true`
- required renders: 13 aim + 5 rifle + 17 weapon-animation = 35
- exported clips: 24 exact armature actions
- generated blend SHA-256: `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- exported FBX SHA-256: `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`

The clean Generator114 build passed with no automated blockers. All 35 mandatory renders were technically reviewed and hash-locked before the gated export on 2026-08-10. The exact report, approval, images, export manifest, and FBX are archived under `Validation/Generator114`.

Generator 110 remains the winding/closed-geometry repair baseline, Generator 111 remains the 17-action rollback candidate, Generator 112 remains the first sprint candidate, and Generator 113 remains the powered-stride baseline. Generator 114 preserves their geometry, rig, weapon controls, and action ranges while adding six stance-aware lateral loops. The run still uses frames 1-21 at 30 FPS, a 20-frame cycle with a native cadence of 180 steps per minute.

## What is authoritative

- `source/powersuit_source.blend` — immutable audited recovered input
- `scripts/` — active build, animation, validation, approval, and export tools
- `Validation/Generator109/` through `Validation/Generator114/` — immutable candidate evidence and rollback artifacts
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

If headless Workbench rendering crashes inside a Windows display driver while Unity is open, set `POWERSUIT_VALIDATION_RENDER_ENGINE=cycles_cpu` before launching the pipeline. The fallback uses deterministic 8-sample CPU rendering and the same structural, image-content, review, hash, and export gates. `POWERSUIT_REUSE_VALIDATION_RENDERS=1` is only for an interrupted pass; it reuses a PNG only after the canonical content validator accepts it.

## Animation set

Generator 114 exports exactly:

- `PS_Idle`, `PS_Walk`, `PS_Hover`, `PS_Aim`
- `PS_WeaponReady_Idle`, `PS_WeaponStowed_Idle`, `PS_WeaponStowed_Hover`
- `PS_Weapon_Draw`, `PS_Weapon_Sheathe`
- `PS_Walk_Forward`, `PS_Walk_Backward`, `PS_Walk_Left`, `PS_Walk_Right`
- `PS_Run_Forward`
- `PS_Aim_Walk_Forward`, `PS_Aim_Walk_Backward`, `PS_Aim_Walk_Left`, `PS_Aim_Walk_Right`
- `PS_WeaponStowed_Walk_Forward`, `PS_WeaponStowed_Walk_Backward`, `PS_WeaponStowed_Walk_Left`, `PS_WeaponStowed_Walk_Right`
- `PS_Reload`, `PS_BoltCycle`

All use one armature Action Slot. Rifle and articulated-component animation is carried by non-deforming armature controls, avoiding unsynchronized object-action FBX takes.

## Architecture boundary

The weapon is rigid beneath `RifleRoot`, except for contract-approved magazine and bolt component sets. Semantic hardpoints and contact-offset vectors are part of the rigid signature. Pose solving moves the complete weapon, adapts the character to fixed targets, removes temporary IK, and bakes synchronized armature actions. Unapproved rifle-part movement fails validation.

Blender files are authoring inputs. Unity receives only a gated FBX; gameplay state, ammunition, damage, cadence, reload, critical hits, and cycling remain in C#.
