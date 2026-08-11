# Powered Suit NextGen - Aegis Vanguard

This is an isolated visual-development lane for a future high-fidelity player
suit. It does **not** replace Generator114, its Unity FBX or `.meta`, its
controller, or the canonical player prefab.

## Current visual target

Candidate004 is the current review candidate. It moves the design away from the
clean toy-soldier read with:

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

Candidate004's 13 review renders are under
`renders/aegis_vanguard_candidate_v004/`. The reproducible local Blender file is
`candidates/aegis_vanguard_candidate_v004.blend`; `.blend` candidates remain
ignored until path-scoped Git LFS is configured and verified.

## Safety boundary

The deterministic builder:

1. requires the approved `ArtSource/PoweredSuit/powersuit_pipeline.blend`;
2. hashes that source before and after generation;
3. hides, but never deletes, the approved Generator114 shell in the candidate;
4. builds Candidate004 on the same armature and actions;
5. writes only beneath `ArtSource/PoweredSuitNextGen/`;
6. recreates and validates the four exact runtime anchors; and
7. does not export or modify a Unity asset.

The approved source remained exactly
`6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`.

## Production work has started

Two isolated gates now make the remaining work explicit:

- `scripts/validate_weapon_clearance.py` evaluates all 24 actions at 162
  authored keyframes using evaluated-mesh BVHs and semantic grip/stock contact
  rules.
- `HeroV2/validate_and_generate_lods.py` validates topology, UV0 coverage,
  texel-density estimates, renderer/material budgets, immutable hashes, and
  deterministic draft LOD1-LOD3 generation.

Candidate004 correctly remains a **FAIL**, not a release asset:

- 7,837 forbidden weapon/suit frame-object intersections across 304 object-pair
  groups. The main faults are forearm/receiver and magazine contacts, central
  back-rifle interference, and reload chest/arm crossings.
- 25 production errors and 16 warnings: 348 n-gons, 80 boundary edges on the
  four baked cable meshes, missing UV0, zero UV coverage, 254
  renderer-bearing objects/draw calls, and no continuous skinned undersuit.
- Draft diagnostic LOD triangle totals were generated successfully:
  `73,140 -> 36,570 -> 14,344 -> 4,846`, but every LOD still has 254 objects and
  requires hand consolidation, UV repair, deformation work, and silhouette QA.

The model is therefore a better visual/engineering target and a measured input
to retopology - not a Unity replacement.

## Rebuild Candidate004

From the repository root:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background "ArtSource\PoweredSuit\powersuit_pipeline.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\build_aegis_vanguard_candidate.py"
```

Then run the commands documented in:

- `validation/weapon_clearance/README.md`
- `HeroV2/README.md`

## Promotion rule

Do not point the canonical demo at this suit until every gate in
`MASTERPIECE_PLAN.md` passes and the owner approves an in-game A/B comparison.
Generator114 remains the rollback model after any later promotion.
