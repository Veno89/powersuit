# Generator 111 Approved QA Report

Date: 2026-08-09

Environment: Blender 5.2.0 LTS and Unity 6000.5.7f1 on Windows

Formal asset state: automated `PASS`, technical visual `APPROVED`, export allowed

## Packaged candidate

- immutable source blend SHA-256: `49c2a9a09c71989a72e6b81c97045e609d825c2bf41e21a62f216adc277402f4`
- generated `powersuit_pipeline.blend` SHA-256: `7cf96287bcb9b0b67c2feb9dcc6e416e023da20f97363c598d8d31ec8cd2851f`
- approved `validation_report.json` SHA-256: `489fab54cf950abac2e8ce9b0beb401f8a6bfbdd3fcadeddaa76d38e57de5a20`
- `visual_approval.json` SHA-256: `c001108f4a149cd7a4328faa5c61569e8001b2d7d5a272871f56308b40949a21`
- `export_manifest.json` SHA-256: `5617055f969c78e9ecf15a8e656a10f4bc4e3fd542ef569298a761ad87e7453c`
- exported FBX SHA-256: `1c3fb62a3d978de6d5205af5c2f04ebf143bbcd5c10bee3f26ff4e4b4ad3d814`
- Unity FBX hash matches the approved export: yes
- required renders: 13 aim + 5 rifle + 14 weapon animation = 32/32
- automated blockers: 0

## Asset and animation result

- rifle generator: 111
- weapon contract: v3
- rigid signature: v6
- animation contract: v2
- rig upgrade: v2
- hand geometry: v3 on both hands
- exact exported armature actions: 17
- non-deforming weapon controls: `WeaponRoot`, `WeaponMagazine`, `WeaponBolt`
- root motion: 0 m
- draw/sheathe endpoint deltas: 0 m
- reload magazine travel: `0.32194 m`
- bolt travel: `0.09500 m`
- reload timing: frames 1–84 at 30 FPS, ammunition commit at frame 75
- bolt timing: frames 1–20 at 30 FPS

Selected aim/contact metrics:

- sight lateral / vertical: `0.011011 / 0.011282 m`
- sight front clearance: `0.088841 m`
- visor/rifle sight-axis angle: `1.7457°`
- firing-side receptor-to-ocular ray angle: `9.9458°`
- trigger/support wrist target error: below `0.000054 m`
- trigger/support hand-grip overlap pairs: `18 / 6`
- stock/shoulder overlap pairs: `22`
- non-stock weapon/torso overlap pairs: `0`
- forward/backward foot phase delta: `0.12655 m`

## Blender verification

1. Rebuilt from immutable source through model, rig, rifle, Aim, 13-action weapon/locomotion authoring, and all three render stages.
2. Validated exact action names/ranges, one armature slot per action, finite transforms, hierarchy, articulated-component ownership, rigid manifests, root motion, grip/stock/sight contact, collision, framing, draw/sheathe endpoints, locomotion phase, magazine travel, and bolt travel.
3. Produced and inspected all 32 mandatory images. Reload cameras were corrected before the final clean run so magazine removal/insertion and hand contact are exposed rather than hidden by armor.
4. Recorded technical visual approval against the exact report, blend, and 32 image hashes; export remained locked until approval.
5. Exported exactly the 17 required actions and verified the FBX manifest, timing markers, selected objects, hash, and size.
6. Confirmed Generator 110's outward face winding and positive-volume export gate remain active, preventing the hollow/open-box appearance caused by back-face culling.

## Unity verification

- imported with Generic rig, cameras/lights disabled, and 17 exact clips
- retained existing FBX `.meta`, prefab, controller, and scene GUID ownership
- two-layer Animator with an upper-body/weapon Avatar Mask; legs remain on locomotion during reload/cycle
- physical model correction measured body-up dot `0.9997`, body-forward dot `0.9950`, bore-forward dot `0.9997`, and muzzle/bore dot `0.9945`
- C# solution: 0 warnings, 0 errors
- Unity Console after verification: 0 errors
- EditMode: 35/35 passed
- PlayMode: 4/4 passed
- Windows x64 Development build: succeeded

Unity's AI Inference/Sentis package emits non-blocking shader performance warnings during the Development build. No compiler, test, scene-load, or build failure resulted.

## Acceptance boundary

Generator 111 is technically approved and integrated for hands-on evaluation. The exact evidence is frozen under `Validation/Generator111`; Generator 110 remains an immutable rollback candidate. User play acceptance of transitions, camera feel, foot planting, reload contact, and bolt feel remains a separate gate in the repository `ROADMAP.md`.
