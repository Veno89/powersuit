# Aegis Vanguard candidate validation

Validated on 2026-08-11 with Blender 5.2 LTS.

## Rollback integrity

- Approved Generator114 working blend SHA-256 before and after Candidate004:
  `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- Frozen Generator114 FBX and active Unity FBX SHA-256:
  `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`
- No approved Blender source, FBX, Unity model, GUID, controller, prefab, or
  scene was overwritten by the NextGen lane.

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

## Weapon-clearance gate

The standalone gate sampled all 24 actions at all 162 authored keyframes using
evaluated render meshes, AABB broad phase, BVH triangle intersections, and
containment sampling.

- Status: **FAIL**
- Accepted semantic grip/stock contacts remain distinguished from penetrations.
- Forbidden instances: 7,837
- Forbidden object-pair groups: 304
- Report:
  `validation/weapon_clearance/aegis_vanguard_candidate_v004_weapon_clearance.json`

Highest-priority repairs:

1. slim and retopologize both wrist/forearm envelopes around the correct grip
   anchors;
2. move the stowed WeaponRoot rearward roughly 0.12-0.15 m and author matching
   draw/sheathe endpoints;
3. split/thin the central backpack and route turbine feeds fully outboard;
4. rebuild reload clearances around the right forearm/stock and left
   magazine-hand contact; and
5. keep only explicit primary-grip, support-grip, and stock-pocket contacts as
   allowed semantic zones.

## HeroV2 production gate

- Status: **FAIL**, intentionally
- Errors: 25; warnings: 16
- LOD0: 73,140 triangles, 254 renderers/draw calls, 10 materials
- Draft LOD1: 36,570 triangles
- Draft LOD2: 14,344 triangles
- Draft LOD3: 4,846 triangles
- Source hash before/after validation: identical
- Report: `HeroV2/reports/candidate004_production.json`

The triangle ratios are useful starting points, but the current 254-piece shell
has no authored `UV0`, no continuous skinned undersuit, 348 n-gons, 80 boundary
edges on its newly baked cable meshes, and no runtime renderer consolidation.
Draft LODs are ignored derivative files, not approved art. This Candidate004
profile deliberately validates the replacement suit only; the inherited
Generator114 rifle remains outside `HeroV2_LOD0` until a separate production
rifle pass is ready, and will be mandatory before Unity promotion.

## Release status

`REVIEW_ONLY_NOT_UNITY_INTEGRATED`

Candidate004 is the strongest procedural review maquette in this lane. It is
not production-safe until clearance, retopology, continuous deformation,
authored PBR texture sets, repaired LODs, Unity integration, performance, and
owner A/B gates pass.
