# Generator 113 Approved QA Report

Date: 2026-08-10

Environment: Blender 5.2.0 LTS and Unity 6000.5.7f1 on Windows

Formal asset state: automated `PASS`, technical visual `APPROVED`, export allowed

## Packaged candidate

- immutable source blend SHA-256: `49c2a9a09c71989a72e6b81c97045e609d825c2bf41e21a62f216adc277402f4`
- generated `powersuit_pipeline.blend` SHA-256: `a5054d65af2cb6a04836216456a1a3162f8d860c6c421533a7ac08a9f70d2d4b`
- approved `validation_report.json` SHA-256: `a6d8c7bd98659f2cee33c906ca845d6e01e2d060dbc906b31de647ab664bf4ee`
- `visual_approval.json` SHA-256: `7d6cc01a5ecce4067d1ae4409cb3dd23df68ed48da2ec3fa5acf279a8ee66d9b`
- `export_manifest.json` SHA-256: `0a14ed949eba508857de4a004c317769eb1d6449f050318fd3c82b493815813d`
- exported FBX SHA-256: `fe18bc8f3e93b2d5ba9e8c9edbd4e8910ad1e27197f806e0b24b95b36136f3dd`
- Unity FBX hash matches the approved export: yes
- required renders: 13 aim + 5 rifle + 15 weapon animation = 33/33
- automated blockers: 0

## Asset and animation result

- rifle generator: 111
- weapon contract: v3
- rigid signature: v6
- animation contract: v4
- rig upgrade: v2
- hand geometry: v3 on both hands
- exact exported armature actions: 18
- non-deforming weapon controls: `WeaponRoot`, `WeaponMagazine`, `WeaponBolt`
- root motion: 0 m
- draw/sheathe endpoint deltas: 0 m
- reload magazine travel: `0.32194 m`
- bolt travel: `0.09500 m`
- reload timing: frames 1-84 at 30 FPS, ammunition commit at frame 75
- bolt timing: frames 1-20 at 30 FPS
- run timing: frames 1-21 at 30 FPS, 20-frame loop, 180 native steps per minute

Selected aim/contact metrics:

- sight lateral / vertical: `0.011011 / 0.011282 m`
- sight front clearance: `0.088841 m`
- visor/rifle sight-axis angle: `1.7457 degrees`
- firing-side receptor-to-ocular ray angle: `9.9458 degrees`
- trigger/support wrist target error: below `0.000054 m`
- trigger/support hand-grip overlap pairs: `18 / 6`
- stock/shoulder overlap pairs: `22`
- non-stock weapon/torso overlap pairs: `0`
- forward/backward foot phase delta: `0.21299 m`
- run/walk stride: `0.93411 / 0.83793 m`
- run airborne clearance: `0.03650 m`
- run/ready torso forward projection: `0.10809 / -0.01189 m`
- run trigger/support wrist error: below `0.000053 m`

## Blender verification

1. Rebuilt from immutable source through model, rig, rifle, Aim, 14-action weapon/locomotion authoring, and all three render stages.
2. Validated exact action names/ranges, one armature slot per action, finite transforms, hierarchy, articulated-component ownership, rigid manifests, root motion, grip/stock/sight contact, collision, framing, draw/sheathe endpoints, locomotion phase, run stride/flight/forward commitment, magazine travel, and bolt travel.
3. Produced and inspected all 33 mandatory images, including the run flight-phase side view. Reload cameras expose magazine removal/insertion and hand contact rather than hiding them behind armor.
4. Recorded technical visual approval against the exact report, blend, and 33 image hashes; export remained locked until approval.
5. Exported exactly the 18 required actions and verified the FBX manifest, timing markers, selected objects, hash, and size.
6. Confirmed Generator 110's outward face winding and positive-volume export gate remain active, preventing the hollow/open-box appearance caused by back-face culling.

## Unity verification

- active Unity FBX SHA-256 matches the approved Generator 113 export exactly
- retained FBX GUID `f48464ae4ba58b54f976e658ece758b3`
- imported with Generic rig, cameras/lights disabled, hierarchy retained, and optimization disabled
- ModelImporter contains exactly 18 configured clips with unique matching takes
- Unity exposes exactly 18 imported `AnimationClip` subassets
- `PS_Run_Forward` imports as frames 0-20, 30 FPS, `0.667 s`, with loop time and loop pose enabled
- the `.meta` diff preserves every Generator 111 clip entry and adds only the run clip entry
- regenerated controller and player/world assets completed successfully
- C# solution: 18 assemblies, 0 warnings, 0 errors
- Unity Console after verification: 0 errors
- EditMode: 228/228 passed
- PlayMode: 12/12 passed
- Windows x64 Development build: succeeded on 2026-08-10 at 21:01
- 15-second headless smoke: no gameplay exception, assertion, or missing-reference patterns

Unity's AI Inference/Sentis package emitted non-blocking third-party shader performance warnings during the Development build. No compiler, test, scene-load, smoke, or build failure resulted.

## Acceptance boundary

Generator 113 is technically approved and integrated for hands-on evaluation. The exact evidence is frozen under `Validation/Generator113`; Generators 110 through 112 remain immutable rollback candidates. Generator113 records 0.8379 m walk stride, 0.9341 m run stride, 0.0365 m run airborne clearance, animation contract version 4, and the deterministic Cycles CPU validation fallback used after the NVIDIA headless Workbench crash. User play acceptance of powered gait/propulsion, residual foot slide, transitions, camera feel, reload contact, bolt feel, jump-to-flight timing, and scoped sight feel remains a separate gate in the repository `ROADMAP.md`.
