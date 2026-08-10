# Generator 112 Approved QA Report

Date: 2026-08-10

Environment: Blender 5.2.0 LTS and Unity 6000.5.7f1 on Windows

Formal asset state: automated `PASS`, technical visual `APPROVED`, export allowed

## Packaged candidate

- immutable source blend SHA-256: `49c2a9a09c71989a72e6b81c97045e609d825c2bf41e21a62f216adc277402f4`
- generated `powersuit_pipeline.blend` SHA-256: `0295acb528b0ca8c0f3ec68f642ad6c56c3f4ddaf9fe2f5a0ab84adae9311876`
- approved `validation_report.json` SHA-256: `a7f3a487f3f549bbc8b3ac55c6a43f90a4ce67a5e42f9e6720a806c600442546`
- `visual_approval.json` SHA-256: `1f7a4554c4a4290a803769f5492f526a76b1840fa45992013430909871e9fd37`
- `export_manifest.json` SHA-256: `417c69c0ba33e69f19e9363e64992fd0a4747fc6fb7ef6380ba3aeda852474e4`
- exported FBX SHA-256: `054b5a1875730b225cbb9192bbf760a75919126043ce3ae1503308d21fa8e409`
- Unity FBX hash matches the approved export: yes
- required renders: 13 aim + 5 rifle + 15 weapon animation = 33/33
- automated blockers: 0

## Asset and animation result

- rifle generator: 111
- weapon contract: v3
- rigid signature: v6
- animation contract: v3
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
- forward/backward foot phase delta: `0.12655 m`
- run/walk stride: `0.68534 / 0.54052 m`
- run airborne clearance: `0.03428 m`
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

- active Unity FBX SHA-256 matches the approved Generator 112 export exactly
- retained FBX GUID `f48464ae4ba58b54f976e658ece758b3`
- imported with Generic rig, cameras/lights disabled, hierarchy retained, and optimization disabled
- ModelImporter contains exactly 18 configured clips with unique matching takes
- Unity exposes exactly 18 imported `AnimationClip` subassets
- `PS_Run_Forward` imports as frames 0-20, 30 FPS, `0.667 s`, with loop time and loop pose enabled
- the `.meta` diff preserves every Generator 111 clip entry and adds only the run clip entry
- regenerated controller and player/world assets completed successfully
- C# solution: 18 assemblies, 0 warnings, 0 errors
- Unity Console after verification: 0 errors
- EditMode: 222/222 passed
- PlayMode: 12/12 passed
- Windows x64 Development build: succeeded on 2026-08-10 at 17:50
- 15-second headless smoke: no gameplay exception, assertion, or missing-reference patterns

Unity's AI Inference/Sentis package emitted non-blocking third-party shader performance warnings during the Development build. No compiler, test, scene-load, smoke, or build failure resulted.

## Acceptance boundary

Generator 112 is technically approved and integrated for hands-on evaluation. The exact evidence is frozen under `Validation/Generator112`; Generators 110 and 111 remain immutable rollback candidates. User play acceptance of powered-sprint cadence and foot slide, transitions, camera feel, reload contact, bolt feel, jump-to-flight timing, and scoped sight feel remains a separate gate in the repository `ROADMAP.md`.
