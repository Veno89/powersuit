# HeroV2 production-validation lane

This folder is the first production-oriented slice after the procedural review
maquettes. It does not export to Unity, replace Generator114, or modify a
Candidate blend. The source `.blend` is hashed before and after every run, while
generated LODs are saved only to an ignored derivative beneath this folder.

## Candidate004 handoff contract

Candidate004 should provide a recursively searchable collection named
`HeroV2_LOD0`. Every renderable object in that collection must be a mesh and may
carry these custom properties:

| Property | Required | Values | Purpose |
|---|---|---|---|
| `hero_v2_asset` | Recommended | `suit`, `rifle`, `optic` | Selects triangle and texture budgets. |
| `hero_v2_lod` | Optional on LOD0 | `0` | Fallback selection if a collection is unavailable. |
| `hero_v2_renderer` | Future | Stable renderer ID | Reserved for the consolidation stage. |

Unlabelled objects are conservatively inferred from their names; anything that
does not clearly say rifle, weapon, scope, sight, or optic is treated as suit
geometry. Explicit but unknown roles fail validation.

The production target remains five renderer meshes: upper suit, lower suit,
visor/emissive, rifle, and optional optic glass. Candidate004's current profile
deliberately requires only the replacement `suit` role while it inherits the
Generator114 rifle for pose review. A future promotion profile must require the
production `rifle` role as well. Separate armor pieces are useful while designing
but must be joined and correctly skinned before this gate passes.

## What the gate measures

- base-mesh vertices, faces, evaluated triangle totals, and role budgets per LOD;
- boundary/non-manifold/loose/degenerate geometry, n-gons, coincident vertices,
  zero-area faces, transforms, and unapplied modifiers;
- exact `UV0` presence, face coverage, zero-area UV faces, 0–1 bounds, surface
  area, and an overlap-unaware texel-density estimate;
- renderer count, used material slots, empty assignments, unique materials, and
  approximate ordinary draw calls;
- deterministic draft LOD1–LOD3 generation and the same metrics for every result;
- immutable source SHA-256 and a canonical, stable JSON report.

The checker intentionally does **not** claim UV overlap/padding proof, animation
deformation quality, or weapon clearance. Those need dedicated later gates.

## Validate Candidate004 and generate draft LODs

From the repository root:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background `
  --python "ArtSource\PoweredSuitNextGen\HeroV2\validate_and_generate_lods.py" `
  -- `
  --source "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v004.blend" `
  --profile "ArtSource\PoweredSuitNextGen\HeroV2\production_profile.json" `
  --report "ArtSource\PoweredSuitNextGen\HeroV2\reports\candidate004_production.json" `
  --generate-lods `
  --require-lods `
  --output-blend "ArtSource\PoweredSuitNextGen\HeroV2\derivatives\candidate004_lods.blend"
```

Validation failures return exit code 2 after the report is written. Contract or
path-safety failures return 3. `--soft-fail` exists only for recording an honest
baseline of an older maquette. `--allow-fallback-property` likewise exists only
to consume Candidate003-style objects when the formal LOD0 collection is absent.

Generated LODs use deterministic Blender collapse-decimation ratios of 50%, 20%,
and 7% of each LOD0 renderer. They are diagnostic starting points, not approved
art. Silhouettes, joints, UV seams, material boundaries, normals, and deformation
must be hand-repaired before release.

## Run the pure contract tests

```powershell
python -m unittest discover `
  -s "ArtSource\PoweredSuitNextGen\HeroV2\tests" `
  -p "test_*.py" -v
```

The tests require no Blender installation. The Blender adapter is additionally
exercised by generating the tracked Candidate003 baseline report.
