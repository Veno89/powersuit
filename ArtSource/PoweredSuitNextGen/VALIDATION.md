# Aegis Vanguard Candidate Validation

Validated on 2026-08-11 with Blender 5.2 LTS.

## Rollback integrity

- Approved Generator114 working blend SHA-256 before candidate generation:
  `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- Approved Generator114 working blend SHA-256 after candidate generation:
  `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- Frozen Generator114 FBX and active Unity FBX both remain:
  `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`

No approved Blender or Unity model asset was overwritten.

## Candidate v002

- Blend SHA-256:
  `9040038ee6cec9ee8d01060765263950b1aca9c23036b2f19242533e2003829c`
- Candidate mesh objects: 113
- Estimated triangles: 32,440
- Armature: `PowerSuit_Armature`
- Bone count: 23
- Preserved `PS_*` actions: 24/24
- Candidate objects parented to the armature: 113/113
- Sampled preserved legacy suit objects visible in candidate renders: 0
- Front three-quarter, side, and back three-quarter review renders: present

## Candidate003 — current review candidate

- Visual target: satin-black armor, blue-black carbon fibre, braided carbon
  cabling, polished/black chrome, sparse cyan emission, and three-tier pauldrons.
- Blend SHA-256:
  `dfc64127742ea1513cb1c8a33155f4f421bd83ecb63d1dfeb7ea43b0ac829600`
- Candidate objects: 227 total; 219 mesh; 4 renderable curves
- Estimated mesh triangles: 63,008
- Armature: `PowerSuit_Armature`
- Bone count: 23
- Preserved `PS_*` actions: 24/24
- Neutral/detail/clay/pose review renders: 13/13
- Real actions sampled in POSE mode: stowed, aim, run, hover, and reload
- Exact runtime thruster/boot anchor names recreated at the candidate geometry
- Approved Generator114 source hash before/after build: identical
- Active Unity FBX, GUID, controller, prefab, and scene: unchanged

The dorsal turbines were moved outboard and the arm armor was slimmed after the
first pose review. The renders show substantially better weapon clearance, but a
formal triangle-intersection sweep and hand-authored docking solution remain G2
requirements rather than claimed passes.

## Release status

`REVIEW_ONLY_NOT_UNITY_INTEGRATED`

Candidate003 is the strongest mechanically coherent procedural maquette in this
lane, not an approved replacement. It
has not yet passed animation pose sweeps, weapon/back-docking clearance, UV/PBR,
deformation, LOD, renderer consolidation, Unity integration, performance, or
owner A/B acceptance gates.
