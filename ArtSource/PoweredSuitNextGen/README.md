# Powered Suit NextGen — Aegis Vanguard

This is a completely separate visual-development lane for a future high-fidelity
player suit. It does **not** replace Generator114, its Unity FBX, its `.meta`, its
controller, or the active player prefab.

## Current review artifacts

- `Concepts/aegis_vanguard_concept_v001.png` — original four-view design target.
- `Concepts/aegis_vanguard_hero_v001.png` — original material/fidelity target.
- `renders/aegis_vanguard_blockout_v001/` — preserved first automated blockout.
- `renders/aegis_vanguard_blockout_v002/` — refined rig-compatible blockout.
- `candidates/aegis_vanguard_blockout_v002.json` — hashes, rig identity, and mesh metrics.
- `scripts/build_aegis_vanguard_candidate.py` — deterministic review-candidate generator.
- `MASTERPIECE_PLAN.md` — production path and acceptance gates.
- `PROMPTS.md` — image-generation provenance and final prompt specifications.

The `.blend` candidates are kept locally and ignored until path-scoped Git LFS is
configured and tested. They are reproducible from the script and the approved
Generator114 working blend.

## Safety boundary

The candidate builder:

1. requires the existing approved `ArtSource/PoweredSuit/powersuit_pipeline.blend`;
2. records its SHA-256 before generation;
3. hides but does not delete the validated suit inside the candidate copy;
4. builds the new visual shell on the same 23-bone armature;
5. saves only beneath `ArtSource/PoweredSuitNextGen/candidates/`;
6. verifies that the approved source hash is unchanged afterward; and
7. does not export or modify any Unity asset.

The v002 report verified the Generator114 working blend remained exactly
`6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`.

## Rebuild the blockout

From the repository root on Windows:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background "ArtSource\PoweredSuit\powersuit_pipeline.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\build_aegis_vanguard_candidate.py"
```

This blockout is a silhouette and engineering prototype, not a production-ready
replacement. It deliberately retains separate pieces for rapid iteration. The
final game mesh must use a continuous skinned undersuit, rigid weighted armor,
authored UVs/PBR textures, repaired topology, LODs, and consolidated renderers.

## Promotion rule

Do not point the canonical demo at this suit until every gate in
`MASTERPIECE_PLAN.md` passes and the owner explicitly approves the in-game A/B
comparison. Generator114 remains the rollback model even after a later promotion.
