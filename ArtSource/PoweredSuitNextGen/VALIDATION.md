# Aegis Vanguard candidate validation

Validated through isolated Candidate006 on 2026-08-13 with Blender 5.2 LTS.

## Rollback integrity

- Approved Generator114 working blend SHA-256 before and after the isolated NextGen work:
  `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- Frozen Generator114 FBX and active Unity FBX SHA-256:
  `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`
- No approved Blender source, FBX, Unity model, GUID, controller, prefab, or
  scene was overwritten by the NextGen lane.
- Candidate004 remains preserved as the visual maquette; Candidate005 is a
  derived architecture prototype and has not replaced it or Generator114.
- Candidate005 remains byte-identical at SHA-256
  `0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`.

## Candidate004 visual/contract evidence

- Candidate blend SHA-256:
  `86fccc2779b0d87658bdc5164bfcb01d8466e7d05d3f92a8172cc0382a01daeb`
- Candidate objects: 258 total; 254 mesh; 0 renderable curves; 4 authored
  cables deterministically baked to mesh before the production handoff
- Estimated mesh triangles: 73,140
- Armature: `PowerSuit_Armature`; bones: 23
- Preserved exact `PS_*` actions: 24/24
- Neutral/detail/clay/real-pose renders: 13/13
- Real actions rendered in POSE mode: stowed, aim, run, hover, and reload
- `Thruster_Nozzle.L/R` and `Heavy_Boot.L/R`: exact names and zero positional
  error at the new hardware
- Candidate renderables linked into the explicit `HeroV2_LOD0` handoff
  collection with `hero_v2_asset=suit`

## Candidate005 production-architecture evidence

Candidate005 is not a visual promotion. It consolidates the Candidate004 forms
to prove a measurable runtime architecture while preserving the 23-bone rig and
all 24 actions:

- LOD0: 88,316 triangles; 3 skinned renderers/estimated draw calls
- one connected skinned undersuit
- complete `UV0`; dedicated Blender audit: 0 selected overlap faces and 0
  selected overlap loops
- HeroV2 status: **PASS**; 0 errors; 4 texel-density warnings
- diagnostic LOD totals: `88,316 -> 44,158 -> 17,660 -> 6,178`
- HeroV2 report: `HeroV2/reports/candidate005_production.json`

The preview BaseColor, Normal, packed MRAO, and Emission maps establish the PBR
data path only. They are not a final unique character bake or authored material
finish. The generated LODs are diagnostics, not hand-repaired release assets.

## Candidate005 deformation scaffold

All 24 actions and all 162 authored keyframes were evaluated. The maximum local
edge-stretch ratio is 5.801599, below the deliberately permissive 8x ceiling.
That threshold catches catastrophic automation failures only; it does not prove
good silhouettes, joint compression, seam placement, skinning, or production
weight quality.

## Candidate005 weapon-clearance gate

The standalone gate sampled all 24 actions at all 162 authored keyframes using
evaluated render meshes, AABB broad phase, BVH triangle intersections, and
containment sampling.

- Status: **FAIL**
- The consolidated visible meshes intentionally receive no broad object-level
  grip/stock exemptions; face-level semantic contact tags are required before
  intended contacts can be distinguished safely.
- Canonical visible-mesh forbidden instances: 3,894
- Canonical visible-mesh object-pair groups: 72
- Report:
  `validation/weapon_clearance/aegis_vanguard_candidate_v005_weapon_clearance.json`

The canonical result evaluates the three consolidated visible Candidate005
meshes. A separate hidden 254-part source-proxy diagnostic reports 5,489
instances across 240 groups and exists only to help locate contributing source
regions. It is not the canonical visible result and does not supersede it.

Highest-priority repairs:

1. slim and retopologize both wrist/forearm envelopes around the correct grip
   anchors;
2. move the stowed WeaponRoot rearward roughly 0.22-0.24 m and author matching
   draw/sheathe endpoints;
3. split/thin the central backpack and route turbine feeds fully outboard;
4. rebuild reload clearances around the right forearm/stock and left
   magazine-hand contact; and
5. keep only explicit primary-grip, support-grip, and stock-pocket contacts as
   allowed semantic zones.

## Candidate006 precision-rifle evidence

Candidate006 / NextGen Precision Rifle 001 is an isolated
production-architecture/review candidate. Its final blend hashes to
`093d5f8dcaede5eb7e7317bb63b98d08776d204f3fbaaf627a271bb899fb1227`,
and the production report records that source hash before and after validation.

- WeaponV2 structural status: **PASS**; 156 checks; 0 errors; 0 warnings
- Promotion: `false`
- Contract: exactly 23 bones and 24 actions
- Rifle-plus-optic LOD totals: `23,216 -> 13,168 -> 5,512 -> 1,884`
- Combined Candidate005/Candidate006 LOD0: 111,532 triangles; 5 renderers;
  8 estimated draws at the hard ceiling
- Review evidence: 13 unique, source-bound renders
- Scope: real inner-aperture proxy maximum distance `1.69e-7 m`; 0.021 m eye
  relief; target at 6 m; all 5 physical rays hit the actual board; readable
  4-line reticle plus 4 range ticks
- Aim: 0.01969 m lateral; 0.01720 m vertical; 3.256 degrees axis error;
  0.19414 m front clearance
- Articulation: 0.333135 m magazine travel; 0.095 m bolt travel; zero return error

The authored visible-clearance gate remains **FAIL**: 377 forbidden instances
across 17 groups over 162 samples, with 197 allowed contacts. The forbidden
result comprises 301 recurring grip/wrist-shell contacts, 35 containment
failures, and 41 manipulation/transition contacts. The 923-integer-frame and
324 dense-transition sweeps were not run because this prerequisite failed.

## Release status

`PRODUCTION_ARCHITECTURE_PROTOTYPE_NOT_UNITY_INTEGRATED`

Candidate005 passes the automated structural contract but is not production art
and is not clearance-safe. Manual anatomical/armor sculpting, retopology and
seam placement, joint cleanup, weight polish, authored PBR materials, clearance
repair, hand-finished LODs, Unity integration/performance validation, and owner
A/B approval remain mandatory. Candidate004 remains the visual-review maquette;
Generator114 remains the active Unity suit and rollback path.

Candidate006 likewise remains on visual/clearance **HOLD**. Stow/draw/sheathe,
reload/bolt, and final PBR/art polish require another production pass. No FBX or
Unity integration was created, and the structural PASS does not authorize
promotion.
