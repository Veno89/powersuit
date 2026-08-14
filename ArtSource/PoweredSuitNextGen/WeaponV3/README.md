# WeaponV3 / Candidate007 validation lane

This directory owns the isolated, fail-closed production gate for
`nextgen_precision_rifle_candidate_v007`. It does not build the rifle, modify
Candidate006, export an FBX, or authorize Unity integration.

## Frozen identity and provenance

- Candidate: `NextGen Precision Rifle 002` / candidate 7
- Weapon ID: `PS_NextGenPrecisionRifle002`
- Production renderer prefix: `NGPR002_`
- LOD collections: `WeaponV3_LOD0` through `WeaponV3_LOD3`
- Source blend: `ArtSource/PoweredSuitNextGen/candidates/nextgen_precision_rifle_candidate_v007.blend`
- Source-adjacent handoff: the same path with `.json`
- Review directory: `ArtSource/PoweredSuitNextGen/renders/nextgen_precision_rifle_candidate_v007/`

Candidate007 is built from the pinned Candidate005 `.blend`. Candidate006 is
predecessor and rollback-comparison evidence, not a build input. Its ignored
local `.blend` is therefore deliberately not a mandatory immutable input: a
fresh checkout must be able to validate the tracked contract without it. The
tracked Candidate006 JSON manifest and production report remain pinned as
canonical JSON; whitespace, key order, and LF/CRLF differences do not change
those pins. Raw-byte SHA-256 is reserved for required `.blend` and `.png`
inputs.

## Candidate007 additions

The lane freezes the measured stow correction at `0.33 m` rearward while
retaining the existing `-right * 0.04 m` offset, orientation, and extraction
waypoint. Action authoring is pinned to
`CANDIDATE007_WEAPON_ACTIONS_V11` / `CANDIDATE007_ACTION_SEMANTICS_V10`.
Draw uses the measured powered-back-mount guide through frame 26, a 12 mm
pregrasp at frame 27, and exact `Hand.R` ownership from frame 28 through the
29.875 dense sample before the Ready endpoint. Sheathe must be its exact
subframe time reverse. The handoff binds the complete transition key schedule,
linear interpolation counts, ownership mode, and the `0.125`-frame, 233-sample
reversal proof.
Candidate007's embedded clearance manifest must retain face policy V1 while
declaring Candidate007 contact-window policy V3. Its measured transition
windows are primary grip Draw 26.75–30 / Sheathe 1–4.25 and support grip Draw
29–30 / Sheathe 1–2; no other transition contact window is accepted. It also freezes
the Candidate007 primary grip at
`(-0.085, -0.070, 0.025)`, support target at `(0.120, 0.280, 0.015)`, and the
support range helpers at `(0.097, 0.250, 0.015)` and
`(0.137, 0.315, 0.015)`. The source manifest must contain matching versioned authoring
evidence and draw/sheath endpoint and reversal errors within `1e-5`.

Manipulation is separately gated at solver V3 and densification V5. The source handoff must bind the
measured distal contact-pad centres for both hands, preserve the seated reload
poses at frames 14/25/75, and use the pad-to-moving-magazine positive-X face
solve at frames 36/50/64. It must also identify `NGPR_BoltKnob` explicitly and
solve frames 4/8/12/16 against that tagged knob's minimum-X face. The measured
pad centres, frame lists, target offsets, magazine dimensions, contact insets,
reload twist, exact root-local bolt classifier corridor, Bolt release-path V2
substitutions/deltas, and the five measured eighth-frame Bolt clearances are
exact profile evidence. Reload return-path V1 also pins its 79/82 blend
endpoints, 79.75/80 anchors, and the 2 mm root-local frame-79.875 correction.
The resulting manipulation key schedules contain exactly 213 Reload frames and
76 BoltCycle frames, while their independent dense clearance sweep remains at
`0.125`-frame cadence. Reload must contain 201 co-solved contact samples and
Bolt 52 (49 quarter-frame samples plus three measured in-window eighth-frame
samples); reverting to wrist or component-centre targeting fails the production
gate even if stow evidence still passes.

The LOD0 renderer must also expose a point-domain
`weapon_v3_component_role` attribute and a
`weapon_v3_component_role_table_json` object property. Together they prove
that receiver, barrel, stock, handguard, and optic mount vertices use only
`WeaponRoot`, while magazine and bolt vertices use only `WeaponMagazine` and
`WeaponBolt`. The source manifest must independently record the same mapping.
Every visible renderer's vertex, triangle, boundary, non-manifold, zero-area,
and duplicate-position counts must also match the source-bound builder handoff;
missing, extra, stale, or hash-unbound topology evidence fails closed.

## Clearance and promotion

Structural success is not enough. Three source-bound reports must all audit
visible geometry, preserve the exact Candidate007 blend, report `PASS`, and
contain zero forbidden intersections:

1. Full authored-keyframe sweep.
2. Full integer-frame sweep across all 24 actions.
3. Inclusive `0.125`-frame dense sweep of BoltCycle, Reload, Draw, and Sheathe.

The dense report must contain the exact action filters, both endpoints, every
sample frame, and counts `153 + 665 + 233 + 233 = 1284`. The integer sweep must
likewise contain the exact 24-action, 923-frame coverage. Summary counts cannot
substitute for the per-action frame lists. Each clearance document's
`report_evidence_sha256` is recomputed over strict canonical JSON before any of
its contents are trusted; a missing, zero, stale, or non-finite self-hash fails
closed.

The production report is sealed only after final status, summary, and promotion
blockers have been computed. Its own `report_evidence_sha256` excludes only that
top-level field. Profile and clearance-document evidence use explicit
`canonical_json` hash modes, so whitespace, key order, and LF/CRLF checkout
differences do not alter their semantic hashes.

Review projection evidence uses schema 4. The Draw render must retain the
`suit_lod0_samples_inside_2_98` context proof with at least 24 visible samples,
at least `0.20` viewport width and `0.08` viewport height, while the weapon
itself continues to occupy at least `0.50` on one viewport axis.

## Certified isolated result

The final Candidate007 blend hashes to
`686dd185c800bc44c897948026da17988a5083c17993c4ef9d03af247f6c5ff2`.
WeaponV3 reports **PASS**, `structural_gate_passed: true`, with 173 checks,
0 errors, and 0 warnings. All three source-bound strict visible reports pass:

| Sweep | Actions / samples | Allowed contacts | Forbidden / groups |
| --- | ---: | ---: | ---: |
| Authored keyframes | 24 / 483 | 783 | 0 / 0 |
| All integer frames | 24 / 923 | 922 | 0 / 0 |
| Dense transitions, `0.125` frame | 4 / 1,284 | 1,565 | 0 / 0 |

All 13 required renders are unique and match the builder manifest. Draw18 is
explicit evidence of the powered guided/magnetic transit phase, not a
conventional reach-to-back draw. Sheathe3 is evidence of the hand-owned release;
the separate stowed render provides the final mount context. Candidate007 still
uses Candidate006's hash-pinned procedural preview maps, not final hand-authored
weapon textures, and no FBX or Unity integration exists.

`promotion_authorized` remains `false` even if every machine gate passes.
Owner visual approval and separate Unity-integration approval remain mandatory.

## Validation

Pure contract checks do not require Blender:

```powershell
python -m unittest discover -s ArtSource\PoweredSuitNextGen\WeaponV3\tests -p 'test_*.py'
python -m py_compile ArtSource\PoweredSuitNextGen\WeaponV3\weapon_v3_contract.py ArtSource\PoweredSuitNextGen\WeaponV3\validate_candidate007.py ArtSource\PoweredSuitNextGen\WeaponV3\tests\test_contract.py
```

After Candidate007 and all required reports/renders exist, run the Blender
adapter explicitly:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python-exit-code 1 --python ArtSource\PoweredSuitNextGen\WeaponV3\validate_candidate007.py -- --source ArtSource\PoweredSuitNextGen\candidates\nextgen_precision_rifle_candidate_v007.blend --report ArtSource\PoweredSuitNextGen\WeaponV3\reports\candidate007_production.json --render-dir ArtSource\PoweredSuitNextGen\renders\nextgen_precision_rifle_candidate_v007
```

The adapter writes a deterministic FAIL report when the source blend is absent.
Use `--soft-fail` only for diagnostics; it does not change the report status or
authorize promotion.
