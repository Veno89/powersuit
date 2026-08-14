# Powered Suit NextGen - Aegis Vanguard

This is an isolated visual-development lane for a future high-fidelity player
suit. It does **not** replace Generator114, its Unity FBX or `.meta`, its
controller, or the canonical player prefab.

## Current visual target

Candidate005 is the current production-architecture candidate. It derives from
the preserved Candidate004 review maquette, keeps its adult industrial-gothic
direction, and moves the asset from hundreds of rigid review parts toward a
runtime-shaped handoff with:

- soot-black coated armor, blue-black carbon fibre, oily gunmetal, restrained
  tarnished chrome, sparse cyan optics, and large-scale grime variation;
- a narrower helmet with a hooded visor and armored occipital shell;
- a sternum keel, oblique structural ribs, raised gorget, and other restrained
  industrial-gothic forms without symbols, spikes, capes, or fantasy ornament;
- thinner three-tier pauldrons, articulated dark gloves, tapered combat boots,
  armored lumbar/pelvis coverage, and more recessed dorsal turbines; and
- exact preservation of the 23-bone rig, all 24 `PS_*` actions, and the four
  runtime exhaust anchors.

The two strongest authored targets are:

- `Concepts/aegis_vanguard_gothic_grit_v003.png` - front three-quarter identity,
  materials, proportions, weathering, and armor hierarchy target.
- `Concepts/aegis_vanguard_gothic_grit_rear_v003.png` - rear construction,
  turbine shrouds, and diagonal rifle corridor target.

Candidate005's 13 review renders are under
`renders/aegis_vanguard_candidate_v005/`. Its reproducible local Blender file is
`candidates/aegis_vanguard_candidate_v005.blend`; `.blend` candidates remain
ignored until path-scoped Git LFS is configured and verified. Candidate004 and
its tracked reports/renders remain the immutable comparison baseline.

## Safety boundary

The deterministic Candidate005 builder:

1. requires the hash-verified Candidate004 blend;
2. hashes that source before and after generation;
3. never edits Candidate004, Generator114, or an active Unity asset;
4. builds Candidate005 on the same 23-bone armature and 24 actions;
5. writes only beneath `ArtSource/PoweredSuitNextGen/`;
6. retains the four exact runtime anchors and all weapon helpers; and
7. does not export or modify a Unity asset.

Candidate004 remained exactly
`86fccc2779b0d87658bdc5164bfcb01d8466e7d05d3f92a8172cc0382a01daeb`.
The final Candidate005 evidence refers to local blend SHA-256
`0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`.
The approved Generator114 source and Unity rollback assets also remain untouched.

## Production work has started

Two isolated gates now make the remaining work explicit:

- `scripts/validate_weapon_clearance.py` evaluates all 24 actions at 162
  authored keyframes using evaluated-mesh BVHs and semantic grip/stock contact
  rules.
- `HeroV2/validate_and_generate_lods.py` validates topology, UV0 coverage,
  texel-density estimates, renderer/material budgets, immutable hashes, and
  deterministic draft LOD1-LOD3 generation.

Candidate005 now **passes the isolated HeroV2 production-geometry profile**:

- exactly three suit renderers/draw calls: continuous skinned undersuit,
  consolidated rigid-weighted armor, and consolidated emission;
- 88,316 LOD0 triangles, complete finite `UV0`, closed triangulated topology,
  no boundary/non-manifold/loose/degenerate/duplicate defects, and normalized
  one-to-four bone influences;
- one connected undersuit with 11,594 blended vertices and a separate Blender
  overlap audit reporting zero selected `UV0` overlap faces or loops;
- diagnostic LOD totals of `88,316 -> 44,158 -> 17,660 -> 6,178`, all within
  the authored suit budgets; and
- 162/162 authored animation keyframes evaluated with finite geometry, no
  collapsed triangle, and a recorded maximum local edge-stretch ratio of
  5.801599 under the deliberately permissive 8x catastrophic-failure ceiling.
  This is deformation-scaffold evidence, not final artist weight polish.

The HeroV2 gate reports 0 errors and four texel-density warnings. Candidate005
wires these deterministic, licence-free 1K preview maps through its UV/material
path:

- `textures/candidate005/AV_H2_Detail_BaseColor.png`
- `textures/candidate005/AV_H2_Detail_Normal.png`
- `textures/candidate005/AV_H2_Detail_MRAO.png`
- `textures/candidate005/AV_H2_Detail_Emission.png`

They are material-development scaffolds, not a unique 4K character bake or a
finished painted wear pass. Final work still requires deliberate sculpting,
production retopology and seam placement, artist-polished weights, a unique 4K
PBR bake/paint pass, and hand-repaired LOD silhouettes.

Weapon clearance correctly remains **FAIL**. The canonical visible-geometry
audit finds 3,894 forbidden instances across 72 consolidated object-pair groups.
The separate hidden-proxy comparison finds 5,489/240, but that result is
directional diagnostic evidence only because its rigid per-piece undersuit
proxies are not equivalent to the remeshed, smoothly skinned visible undersuit.
The inherited rifle's receiver, grips, stock, stow path, and action poses require
a coordinated NextGen weapon/animation pass.

Candidate005 has not been exported or integrated into Unity, and the technical
handoff PASS is not visual promotion. Generator114 remains active until the
remaining art, clearance, Unity/performance, and owner A/B gates pass.

## Candidate006 precision-rifle review candidate

Candidate006 / NextGen Precision Rifle 001 extends the isolated production
architecture without replacing Candidate005 or the active Unity rifle. Its
reproducible local blend hashes to
`093d5f8dcaede5eb7e7317bb63b98d08776d204f3fbaaf627a271bb899fb1227`,
and the production report records that source hash before and after validation.
The Candidate005 source remains unchanged at
`0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`.

The WeaponV2 structural gate reports **PASS**, 156 checks, 0 errors, and 0
warnings, while `promotion_authorized` remains false. Candidate006 retains
exactly 23 bones/24 actions, provides rifle-plus-optic LOD totals of
`23,216 -> 13,168 -> 5,512 -> 1,884`, and combines with Candidate005 at 111,532
LOD0 triangles, five renderers, and an estimated eight draws—the hard ceiling.
Its 13 required renders are unique and source-bound. Scope evidence records a
real inner-aperture proxy within `1.69e-7 m`, 0.021 m eye relief, a six-metre
target, five successful physical rays, and a readable four-line reticle with
four range ticks. Aim error is 0.01969 m lateral, 0.01720 m vertical, and
3.256 degrees on-axis; front clearance is 0.19414 m. Magazine and bolt travel
are 0.333135 m and 0.095 m, with zero return error.

This is still an isolated production-architecture/review candidate, **not a
Unity-ready asset**. The authored visible-clearance pass fails with 377
forbidden instances/17 groups over 162 samples and records 197 allowed contacts:
301 recurring grip/wrist-shell contacts, 35 containment failures, and 41
manipulation/transition failures. The full all-frame and dense-transition gates
were not run because the authored pass already fails. Stow/draw/sheathe,
reload/bolt, and final PBR/art polish remain blocking. No FBX was exported and
Generator114 remains active.

## Candidate007 precision-rifle clearance successor

Candidate007 / NextGen Precision Rifle 002 is a parallel successor built from
the pinned Candidate005 source, not from Candidate006. It preserves Candidate006
as historical rollback-comparison evidence and hashes to
`686dd185c800bc44c897948026da17988a5083c17993c4ef9d03af247f6c5ff2`.
The WeaponV3 gate reports **PASS** with 173 checks, 0 errors, and 0 warnings;
`structural_gate_passed` is true. The exact 23-bone/24-action contract remains
under `CANDIDATE007_WEAPON_ACTIONS_V11`,
`CANDIDATE007_ACTION_SEMANTICS_V10`, manipulation densification V5, and
Candidate007 contact-window policy V3.

All three strict visible-geometry reports are source-bound to that exact blend
and pass with zero forbidden contacts or groups:

| Sweep | Actions / samples | Allowed contacts | Forbidden / groups | Status |
| --- | ---: | ---: | ---: | ---: |
| Authored keyframes | 24 / 483 | 783 | 0 / 0 | **PASS** |
| All integer frames | 24 / 923 | 922 | 0 / 0 | **PASS** |
| Dense transitions, `0.125` frame | 4 / 1,284 | 1,565 | 0 / 0 | **PASS** |

The 13 required review renders are unique and match the source-adjacent
manifest. Draw18 deliberately records a powered guided/magnetic transit with a
late hand catch; it is not a conventional reach-to-back draw. Sheathe3 records
the hand-owned release, while the separate stowed view establishes the final
back-mount destination.

This is isolated Blender certification, not promotion. Candidate007 deliberately
reuses Candidate006's hash-pinned procedural 2K preview maps, which are pipeline
evidence rather than final authored rifle textures. No FBX or Unity asset was
exported, replaced, or modified. `promotion_authorized` remains false pending
owner visual approval and separate Unity-integration approval; Generator114
remains active.

## Rebuild Candidate005

From the repository root:

```powershell
python "ArtSource\PoweredSuitNextGen\HeroV2\generate_candidate005_preview_textures.py"

& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v004.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\build_aegis_vanguard_candidate005.py"
```

Then run the commands documented in:

- `validation/weapon_clearance/README.md`
- `HeroV2/README.md`

## Promotion rule

Do not point the canonical demo at this suit until every gate in
`MASTERPIECE_PLAN.md` passes and the owner approves an in-game A/B comparison.
Generator114 remains the rollback model after any later promotion.
