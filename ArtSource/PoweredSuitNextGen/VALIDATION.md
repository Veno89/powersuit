# Aegis Vanguard candidate validation

Validated through Candidate005 on 2026-08-12 with Blender 5.2 LTS.

## Rollback integrity

- Approved Generator114 working blend SHA-256 before and after the isolated NextGen work:
  `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- Frozen Generator114 FBX and active Unity FBX SHA-256:
  `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`
- No approved Blender source, FBX, Unity model, GUID, controller, prefab, or
  scene was overwritten by the NextGen lane.
- Candidate004 remains preserved as the visual maquette; Candidate005 is a
  derived architecture prototype and has not replaced it or Generator114.

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

## Release status

`PRODUCTION_ARCHITECTURE_PROTOTYPE_NOT_UNITY_INTEGRATED`

Candidate005 passes the automated structural contract but is not production art
and is not clearance-safe. Manual anatomical/armor sculpting, retopology and
seam placement, joint cleanup, weight polish, authored PBR materials, clearance
repair, hand-finished LODs, Unity integration/performance validation, and owner
A/B approval remain mandatory. Candidate004 remains the visual-review maquette;
Generator114 remains the active Unity suit and rollback path.
