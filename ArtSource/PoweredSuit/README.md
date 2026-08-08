# Powered Suit Blender Pipeline

This directory is the authoritative Blender authoring source and tooling for
the Powered Suit character and its reusable rigid-weapon framework.

## Current status

Approved Generator 109 release:

- Blender: 5.2 LTS
- rifle generator: 109
- automated validation: `PASS`
- visual validation: `APPROVED`
- export allowed: `true`
- required renders produced: 13 aim + 5 rifle

The automated pass and all 18 required renders received explicit visual
approval on 2026-08-08. The approval record and exact reviewed images are
archived under `Validation/Generator109`; the approved FBX was then exported
and copied into Unity alongside the legacy model.

## What is authoritative

- `source/powersuit_source.blend` — immutable, audited recovered input
- `scripts/` — active build, pose, validation, approval, and export tools
- `Validation/Generator109/` — immutable approval report and reviewed images
- `WEAPON_FRAMEWORK.md` — weapon/stance architecture reference
- `PROVENANCE.md` — exact lineage and historical Reset06 diagnosis
- `UNITY_INTEGRATION.md` — safe later Unity import procedure
- `audit/legacy/` — frozen evidence about recovered older iterations
- `RESET_01.md` through `RESET_06.md` — historical changelog, not active code

The following are generated working artifacts and are intentionally ignored by
Git: active `powersuit_pipeline.blend`, working `renders/`, working `exports/`,
Blender backups, machine-local Blender paths, and Python bytecode. Named
validation archives are versioned exceptions.

## Build and review on Windows

1. If needed, run `00_SET_BLENDER_PATH_WINDOWS.bat` once.
2. Run `01_BUILD_AND_RENDER_WINDOWS.bat`.
3. Open `renders/validation_report.json` and confirm automated status is
   `PASS` with no blockers.
4. Inspect every PNG under `renders/aim_validation` and
   `renders/rifle_validation`.
5. Reject and revise the result if a grip, stock, sight line, elbow, camera,
   silhouette, clipping, or frame-stability problem remains visible.
6. Only after genuine visual acceptance, run
   `02_APPROVE_AND_EXPORT_WINDOWS.bat` and type `APPROVE` when prompted.

The build launcher always resets the working scene from the audited source,
then runs every modelling, rig, pose, and validation stage in Blender. It does
not rely on a hand-edited intermediate `.blend`.

## Architecture boundary

The weapon remains rigid below one `RifleRoot`. The weapon owns visible
geometry and semantic hardpoints; a stance profile owns character behaviour.
Pose solving moves the complete weapon, adapts the character to fixed targets,
bakes `PS_Aim`, removes temporary IK, and only then attaches `RifleRoot` to
`Hand.R`. Individual weapon parts must never be moved to force a pose to pass.

Blender files are authoring inputs. Unity should receive only an explicitly
approved FBX imported alongside the current live model; gameplay logic remains
in C#.
