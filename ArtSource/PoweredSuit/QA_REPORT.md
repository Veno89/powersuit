# Generator 114 Approved QA Report

Date: 2026-08-10

Environment: Blender 5.2.0 LTS and Unity 6000.5.7f1 on Windows

Formal asset state: automated `PASS`, technical visual `APPROVED`, export allowed

## Packaged candidate

- immutable source blend SHA-256: `49c2a9a09c71989a72e6b81c97045e609d825c2bf41e21a62f216adc277402f4`
- generated `powersuit_pipeline.blend` SHA-256: `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- approved `validation_report.json` SHA-256: `0d71b5eb79f9fec697c587513774eb57ffaf23c029a80ec1c55852018c67f7f9`
- `visual_approval.json` SHA-256: `811e6ebebe0f7ab373473da4339a78d88082379bb5df1cde523897f0756f781d`
- `export_manifest.json` SHA-256: `6ce5c612b35a118076357d33de27da3ff669e6b743eb812eccd0956652c1faaf`
- exported FBX SHA-256: `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`
- Unity FBX hash matches the approved export: yes
- required renders: 13 aim + 5 rifle + 17 weapon animation = 35/35
- automated blockers: 0

## Asset and animation result

- rifle generator: 111
- weapon contract: v3
- rigid signature: v6
- animation contract: v5
- rig upgrade: v2
- hand geometry: v3 on both hands
- exact exported armature actions: 24
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
- lateral flight-phase foot separation: `0.7130 m`
- run/walk stride: `0.93411 / 0.83793 m`
- run airborne clearance: `0.03650 m`
- run/ready torso forward projection: `0.10809 / -0.01189 m`
- run trigger/support wrist error: below `0.000053 m`

## Blender verification

1. Rebuilt from immutable source through model, rig, rifle, Aim, 20-action weapon/locomotion authoring, and all three render stages.
2. Validated exact action names/ranges, one armature slot per action, finite transforms, hierarchy, articulated-component ownership, rigid manifests, root motion, grip/stock/sight contact, collision, framing, draw/sheathe endpoints, locomotion phase, run stride/flight/forward commitment, magazine travel, and bolt travel.
3. Produced and inspected all 35 mandatory images, including run flight phase and lateral ready/aim evidence. Reload cameras expose magazine removal/insertion and hand contact rather than hiding them behind armor.
4. Recorded technical visual approval against the exact report, blend, and 35 image hashes; export remained locked until approval.
5. Exported exactly the 24 required actions and verified the FBX manifest, timing markers, selected objects, hash, and size.
6. Confirmed Generator 110's outward face winding and positive-volume export gate remain active, preventing the hollow/open-box appearance caused by back-face culling.

## Unity verification

- active Unity FBX SHA-256 matches the approved Generator 114 export exactly
- retained FBX GUID `f48464ae4ba58b54f976e658ece758b3`
- imported with Generic rig, cameras/lights disabled, hierarchy retained, and optimization disabled
- ModelImporter contains exactly 24 configured clips with unique matching takes
- Unity exposes exactly 24 imported `AnimationClip` subassets
- `PS_Run_Forward` imports as frames 0-20, 30 FPS, `0.667 s`, with loop time and loop pose enabled
- the `.meta` preserves all prior entries and adds only the six Generator 114 lateral clips
- regenerated controller and player/world assets completed successfully
- C# solution: 18 assemblies, 0 warnings, 0 errors
- Unity Console after verification: 0 errors
- EditMode: 234/234 passed
- PlayMode: 12/12 passed
- Windows x64 Development build: succeeded on 2026-08-10 at 22:46
- 15-second headless smoke: no gameplay exception, assertion, or missing-reference patterns

Unity's AI Inference/Sentis package emitted non-blocking third-party shader performance warnings during the Development build. No compiler, test, scene-load, smoke, or build failure resulted.

## Acceptance boundary

Generator 114 is technically approved and integrated. The exact evidence is frozen under `Validation/Generator114`; Generators 110 through 113 remain immutable rollback candidates. Generator114 records 0.8379 m walk stride, 0.9341 m run stride, 0.0365 m run airborne clearance, 0.7130 m lateral foot separation, and animation contract version 5. A broad owner hands-on pass on 2026-08-11 reported that the integrated movement, aiming, heat, effects, abilities, and encounter loop work and feel decent. Targeted edge cases and objective performance measurements remain tracked in the repository `ROADMAP.md`.
