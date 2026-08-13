# Candidate006 WeaponV2 production gate

This isolated lane validates **Candidate006 / NextGen Precision Rifle 001** as
a parallel Blender review candidate. It never modifies or replaces a Unity
asset, exports an FBX, or treats proxy clearance as promotion evidence.

## Final isolated result

The final blend hashes to
`093d5f8dcaede5eb7e7317bb63b98d08776d204f3fbaaf627a271bb899fb1227`,
and the production report records that source hash before and after validation.
Candidate005 remains unchanged at
`0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`.

The structural gate is **PASS** with 156 checks, 0 errors, and 0 warnings, but
`promotion_authorized` is false. The source retains exactly 23 bones/24 actions;
rifle-plus-optic LOD totals are `23,216 -> 13,168 -> 5,512 -> 1,884`.
Combined with Candidate005, LOD0 is 111,532 triangles across five renderers and
an estimated eight draws, at the hard ceiling. All 13 required renders are
unique and source-bound.

The scope proof uses a real inner-aperture proxy with maximum distance
`1.69e-7 m`, 0.021 m eye relief, an actual target board at 6 m, and five rays
that all hit that board. The sight picture contains a readable four-line reticle
and four range ticks. Aim evidence is 0.01969 m lateral, 0.01720 m vertical,
3.256 degrees axis error, and 0.19414 m front clearance. Magazine travel is
0.333135 m and bolt travel is 0.095 m; both return with zero error.

The separate authored visible-clearance audit is **FAIL**: 377 forbidden
instances/17 groups across 162 samples, plus 197 allowed contacts. The failures
are 301 recurring grip/wrist-shell contacts, 35 containment, and 41
manipulation/transition contacts. Full all-frame and dense-transition audits
were not run because the authored prerequisite fails. Candidate006 therefore
remains an isolated production-architecture/review candidate, not Unity-ready;
stow/draw/sheathe, reload/bolt, and final PBR/art polish remain open.

## Frozen contract

- Candidate004, Candidate005, Generator114, its recovered source, its action
  manifest, the Candidate005 production report, and the concept reference are
  SHA-256 pinned in `production_profile.json`.
- `PowerSuit_Armature` must retain exactly 23 named bones and the three weapon
  controls `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt`. Candidate006's
  isolated armature copy must enable deformation on those controls so its
  one-hot weighted production renderers follow the animated carrier,
  magazine, and bolt. The hash-pinned Generator114 and Candidate005 sources
  remain unchanged.
- The exact 24 Generator114 `PS_*` action names and frame ranges are mandatory.
  Each action must contain one `OBJECT` Action Slot, all three weapon controls,
  and no root motion.
- The gate independently evaluates LOD0 at Aim frame 1, stowed frame 1,
  reload frame 50, and bolt frame 12. Every vertex must match Blender's
  explicit `pose @ rest^-1 @ bind` result within 1 mm; the carrier must move
  between ready and stowed, magazine and bolt must exceed their minimum travel,
  and all three transitions must return to their authored endpoint/rest poses.
- `RifleRoot` keeps the weapon contract v3, rigid signature v6, `+Y` forward,
  `+Z` up, and all five canonical hardpoints. Magazine and bolt pieces may use
  only their matching armature controls.
- Actual visible meshes come only from `WeaponV2_LOD0` through
  `WeaponV2_LOD3`. Every LOD has exactly one rifle renderer and at most one
  optic renderer. Clearance proxies are never selected by this gate.
- Rifle triangle targets are 20-30k, 10-15k, 4-6k, and 1-2k for LOD0-LOD3.
  The gate also combines these with the hash-pinned Candidate005 suit metrics
  and enforces the existing HeroV2 runtime ceilings.
- Visible meshes must be triangulated, closed, clean, transform-applied,
  rigidly weighted to weapon controls, fully covered by finite in-bounds
  non-overlapping `UV0`, and driven by a hash-verified 2K BaseColor, Normal,
  packed MRAO, and Emission set.
- The embedded `PS_CLEARANCE_MANIFEST.json` and actual visible face attributes
  must match `PS_CLEARANCE_FACE_POLICY_V1`. Missing or inconsistent semantics
  fail closed.
- Aim validation measures ocular placement, bore direction, helmet/optic
  overlap, and five evaluated rays through the sight corridor.
- The exact 13 review PNGs listed in the production profile are required and
  hash-recorded. Their presence is structural evidence, not visual approval.

## Pure tests

```powershell
python -m unittest discover -s ArtSource/PoweredSuitNextGen/WeaponV2/tests -v
```

The tests cover profile integrity, immutable path safety, exact action ranges
and Action Slots, skin-motion sample/return thresholds, triangle budgets, PBR
manifests and hashes, render manifests, canonical hashing, and fail-closed
report finalisation.

## Blender validation

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
  --background `
  --python-exit-code 1 `
  --python ArtSource/PoweredSuitNextGen/WeaponV2/validate_candidate006.py `
  -- `
  --source ArtSource/PoweredSuitNextGen/candidates/nextgen_precision_rifle_candidate_v006.blend `
  --report ArtSource/PoweredSuitNextGen/WeaponV2/reports/candidate006_production.json `
  --render-dir ArtSource/PoweredSuitNextGen/renders/nextgen_precision_rifle_candidate_v006
```

Use `--soft-fail` only while diagnosing an incomplete candidate. It still
writes `status: FAIL` and `promotion_authorized: false`; it changes only the
process exit code. A strict successful run is necessary but not sufficient for
promotion: the separate all-frame and dense-transition visible clearance
sweeps and owner visual approval remain mandatory.
