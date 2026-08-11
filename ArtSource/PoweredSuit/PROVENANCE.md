# Powered Suit Provenance

## Recovered baseline

The inspected starting point was `OtherIterations/PoweredSuit_WeaponFramework_Reset06_2026-08-07`. No original ZIP or nested version-control history was present.

Canonical recovered input:

- file: `source/powersuit_source.blend`
- SHA-256: `49c2a9a09c71989a72e6b81c97045e609d825c2bf41e21a62f216adc277402f4`
- retained compatibility actions: `PS_Idle`, `PS_Walk`, `PS_Hover`; the recovered `PS_Aim` is deleted and rebuilt

The source is byte-identical to `OtherIterations/PowerSuitAsset/powersuit.blend`. Reset documents are a cumulative changelog over that old source, not six independent clean assets.

## Historical Reset06 result

The newest recovered historical attempt was Reset06's generated `powersuit_pipeline.blend`, not its `.blend1` backup or older exported FBXs:

- rifle generator: 102
- generated blend SHA-256: `8e4d039b612ea406cdf76332a8b5be6d0015dbbb6be5d072e79674917e6f31b4`
- automated status: `REVIEW_BLOCKED`
- visual status: `NOT_REVIEWED`
- export allowed: `false`
- sight lateral error: approximately `0.290 m`
- stock/stance anchor distance: approximately `0.081 m`

Reset06 had a stock-side sign conflict and compared its deliberately offset stock target against an incompatible raw-anchor threshold. It could not pass its intended placement. It remains useful evidence, not an import-ready asset. Existing legacy Unity FBXs are byte-identical to the older `PowerSuitAsset/exports` set and are not Reset06 exports.

## Post-Reset06 lineage

The active lineage preserves Reset06's useful clean-source runner, semantic weapon framework, render-first diagnosis, and approval lock while correcting its coordinate, wrist-target, contact, head-settle, camera, manifest, and validation semantics.

- Generator 109 was the first gated Unity evaluation candidate.
- Unity back-face culling exposed inward primitive face winding that Blender's normal two-sided display had hidden.
- Generator 110 corrected suit/rifle winding and added a signed-volume export gate. Its 18-view evidence and FBX remain archived as the rollback baseline.
- Generator 111 extends the contract to v3/signature v6, introduces explicit articulated magazine and bolt components, and exports 17 synchronized armature actions. Its clean build produced 32 mandatory views, passed automated validation with zero blockers, received technical visual approval, and exported on 2026-08-09.
- Generator 112 preserves Generator 111's geometry, weapon contract, controls, and all 17 original action ranges while advancing the animation contract to v3 and adding `PS_Run_Forward`. Its clean build produced 33 mandatory views, passed automated validation with zero blockers, received technical visual approval, and exported on 2026-08-10.
- Generator 113 preserves all 18 Generator 112 action names/ranges, advances the animation contract to v4, lengthens the six stance-aware walking variants to a 0.8379 m powered stride, expands the run stride to 0.9341 m with 0.0365 m airborne clearance, and exports the same one-slot armature contract. Its 33-view validation used the explicit deterministic Cycles CPU fallback after the NVIDIA headless Workbench path repeatedly crashed while Unity was open.
- Generator 114 preserves Generator 113's geometry, weapon controls, powered strides, run action, and existing ranges while advancing the animation contract to v5. It adds left/right loops for ready, aimed, and stowed locomotion, producing 24 synchronized armature actions and 35 approved views for Unity's signed 2D directional blends.

Generator 111 framework guarantees retained through Generator 113 include:

- explicit wrist-head target semantics and signed contact-offset vectors
- corrected stock-side convention and intended-target stock validation
- topology/material/modifier/semantic rigid manifests and finite-transform checks
- rig v2 and hand-geometry v3 preflight gates
- bounded deterministic head settling and explicit sight-axis/eye-ray metrics
- grip, stock, torso, framing, and articulated-component checks
- three validation render sets that are produced even when review blockers exist
- exact validation-render hashing before approval and export
- outward face winding and signed-volume rejection for Unity back-face culling
- one synchronized armature Action Slot per exported action, with `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt` synchronization

Generator 112 adds these verified guarantees:

- exactly 18 armature actions and 33 approved render hashes
- `PS_Run_Forward` frames 1-21 at 30 FPS, closing a 20-frame loop at 180 native steps per minute
- run stride `0.6853 m` versus walk stride `0.5405 m`
- airborne clearance `0.0343 m`, forward torso projection `0.1081 m`, and sub-0.1 mm two-hand grip errors
- generated blend SHA-256 `0295acb528b0ca8c0f3ec68f642ad6c56c3f4ddaf9fe2f5a0ab84adae9311876`
- exported FBX SHA-256 `054b5a1875730b225cbb9192bbf760a75919126043ce3ae1503308d21fa8e409`

Generator 113 adds these verified guarantees:

- animation contract v4 with the same 18 actions and exact ranges
- powered walk/run strides `0.8379 / 0.9341 m`
- run airborne clearance `0.0365 m`
- explicit crash-resume validation that reuses a PNG only when requested and after the canonical content check accepts it
- generated blend SHA-256 `a5054d65af2cb6a04836216456a1a3162f8d860c6c421533a7ac08a9f70d2d4b`
- exported FBX SHA-256 `fe18bc8f3e93b2d5ba9e8c9edbd4e8910ad1e27197f806e0b24b95b36136f3dd`

Generator 114 adds these verified guarantees:

- animation contract v5 with exactly 24 actions and 35 approved render hashes
- six authored lateral loops: ready, aimed, and stowed left/right variants at frames 1-31
- lateral flight-phase foot separation `0.7130 m` while retaining the `0.8379 / 0.9341 m` powered walk/run strides and `0.0365 m` run clearance
- generated blend SHA-256 `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- exported FBX SHA-256 `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`

## Artifact policy

Canonical inputs are the audited source blend, scripts, launchers, documentation, and provenance records. Working blends/renders/exports are regenerable and ignored. Important candidates are frozen under named validation directories rather than silently replacing history.

Generators 110 through 113 remain frozen under their matching `Validation` directories. Generator 114 evidence and its exact exported FBX are under `Validation/Generator114`; the same FBX binary is active in Unity. Existing `Generator109` and `Generator111` Unity object/asset names remain in place for GUID and reference continuity.
