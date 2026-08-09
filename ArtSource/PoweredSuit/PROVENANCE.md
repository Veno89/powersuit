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

Generator 111 guarantees include:

- explicit wrist-head target semantics and signed contact-offset vectors
- corrected stock-side convention and intended-target stock validation
- topology/material/modifier/semantic rigid manifests and finite-transform checks
- rig v2 and hand-geometry v3 preflight gates
- bounded deterministic head settling and explicit sight-axis/eye-ray metrics
- grip, stock, torso, framing, and articulated-component checks
- three validation render sets that are produced even when review blockers exist
- exact 32-render hashing before approval and export
- outward face winding and signed-volume rejection for Unity back-face culling
- exactly 17 armature actions, with `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt` synchronization

## Artifact policy

Canonical inputs are the audited source blend, scripts, launchers, documentation, and provenance records. Working blends/renders/exports are regenerable and ignored. Important candidates are frozen under named validation directories rather than silently replacing history.

Generator 110 remains available under `Validation/Generator110`. Generator 111 evidence and the exact exported FBX are under `Validation/Generator111`; the same FBX is imported additively in Unity. Existing `Generator109` Unity asset names remain in place for GUID/reference continuity.
