# Powered Suit Provenance

## Recovered baseline

The inspected starting point was the extracted directory
`OtherIterations/PoweredSuit_WeaponFramework_Reset06_2026-08-07`. No original
ZIP or nested version-control history was present.

The canonical recovered input is:

- file: `source/powersuit_source.blend`
- SHA-256: `49c2a9a09c71989a72e6b81c97045e609d825c2bf41e21a62f216adc277402f4`
- retained Actions: `PS_Idle`, `PS_Walk`, `PS_Hover`, and the obsolete source
  `PS_Aim` that the active pipeline deletes and rebuilds

That source is byte-identical to
`OtherIterations/PowerSuitAsset/powersuit.blend`. The reset documents are a
cumulative changelog over that old source, not six independent clean assets.

## Historical Reset06 result

The latest recovered historical attempt is Reset06's generated
`powersuit_pipeline.blend`, not its `.blend1` backup and not the older exported
FBXs:

- rifle generator: 102
- generated blend SHA-256:
  `8e4d039b612ea406cdf76332a8b5be6d0015dbbb6be5d072e79674917e6f31b4`
- automated status: `REVIEW_BLOCKED`
- visual status: `NOT_REVIEWED`
- export allowed: `false`
- sight lateral error: approximately `0.290 m`
- stock/stance anchor distance: approximately `0.081 m`

The Reset06 stock lateral sign conflicted with the recovered rig's actual named
shoulder side. Its solver also deliberately applied an offset whose norm was
about 81 mm while its validator blocked any raw anchor distance above 75 mm.
Reset06 therefore could not pass its own intended stock placement. It is useful
historical evidence, not an import-ready asset.

`powersuit_pipeline.blend1` is only the pre-aim intermediate. The existing
Unity FBXs are byte-identical to the older `PowerSuitAsset/exports` files and
must not be described as Reset06 output.

## Active post-Reset06 iteration

The active generator-109 pipeline preserves the sound Reset06 architecture
while correcting the recovered coordinate, wrist-target, contact, head-settle,
camera, manifest, and gate semantics. Notable active guarantees include:

- weapon contract v2 and rigid signature v5
- explicit wrist-head target semantics and declared contact-offset vectors
- fixed stock-side convention and intended-offset stock validation
- rigid weapon component, topology, material, modifier, and semantic manifest
- finite-transform and stale rig/hand-version rejection
- deterministic bounded head search using a documented visor receptor proxy
- real sight-axis, eye-ray, wrist, grip, stock, torso, and framing checks
- all reviewable blockers still produce the complete render set
- exact render-set hashing before approval and export

The current report is generated at `renders/validation_report.json`. The
Generator 109 report matched its generated blend, showed 18/18 renders, passed
automated validation, and was explicitly approved on 2026-08-08. The exact
reviewed evidence is frozen under `Validation/Generator109`; active working
outputs remain regenerable and ignored.

## Artifact policy

Canonical development inputs are the audited source blend, active scripts,
launchers, architecture documentation, and provenance records. Working blends,
renders, approvals, and exports are generated artifacts. An important result
should be packaged or archived as a named immutable candidate rather than
silently replacing the Unity model. Generator 109 follows that policy and is
integrated in Unity alongside the legacy artifact.
