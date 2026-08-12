# HeroV2 production-validation lane

This folder is the first production-oriented slice after the procedural review
maquettes. It does not export to Unity, replace Generator114, or modify a
Candidate blend. The source `.blend` is hashed before and after every run, while
generated LODs are saved only to an ignored derivative beneath this folder.

## Candidate005 handoff contract

Candidate005 provides a recursively searchable collection named
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

Candidate005 uses three suit renderers: one connected skinned undersuit,
consolidated rigid-weighted armor, and consolidated emission. A future promotion
profile must additionally require the production `rifle` role and optional optic
glass, while the current profile deliberately validates only the replacement
suit and inherits the Generator114 rifle for pose review.

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

## Candidate005 result

The tracked report and derivative refer to Candidate005 SHA-256
`0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`.
The HeroV2 profile passes with 0 errors and four texel-density warnings. Its
measured handoff contains:

- three suit renderers and three estimated draw calls;
- 88,316 LOD0 triangles, followed by diagnostic totals of 44,158, 17,660,
  and 6,178 for LOD1-LOD3;
- one connected undersuit with 11,594 blended vertices;
- closed, triangulated geometry within every triangle budget and complete,
  finite `UV0` coverage at every LOD; and
- a separate Candidate005 builder audit with zero selected overlapping `UV0`
  faces or loops across all three LOD0 renderers.

The deformation scaffold evaluates all 162 authored keyframes without a
collapsed triangle and records a maximum local edge-stretch ratio of 5.801599.
Its 8x ceiling catches catastrophic automation failures only; it does not certify
production skin quality, anatomy, joint volume, or final artist weighting.

The four texel-density warnings remain real evidence that UV density/packing is
provisional. Candidate005 currently exercises these deterministic 1K preview
maps:

- `../textures/candidate005/AV_H2_Detail_BaseColor.png`
- `../textures/candidate005/AV_H2_Detail_Normal.png`
- `../textures/candidate005/AV_H2_Detail_MRAO.png`
- `../textures/candidate005/AV_H2_Detail_Emission.png`

These maps are not the final unique 4K character atlas. Release art still needs
final sculpting, deliberate retopology and seams, polished weights, authored 4K
PBR bakes/painting, and hand-repaired LOD silhouettes.

Weapon clearance also remains blocking. The actual visible-renderer audit fails
with 3,894 forbidden instances across 72 consolidated object-pair groups. A
separate hidden-proxy diagnostic reports 5,489/240, but it is not visible-mesh
clearance proof because the proxy undersuit differs from the remeshed skinned
surface. Candidate005 is neither Unity-integrated nor visually promoted;
Generator114 remains the active rollback-safe suit.

## Validate Candidate005 and generate draft LODs

From the repository root:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background `
  --python "ArtSource\PoweredSuitNextGen\HeroV2\validate_and_generate_lods.py" `
  -- `
  --source "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v005.blend" `
  --profile "ArtSource\PoweredSuitNextGen\HeroV2\production_profile.json" `
  --report "ArtSource\PoweredSuitNextGen\HeroV2\reports\candidate005_production.json" `
  --generate-lods `
  --output-blend "ArtSource\PoweredSuitNextGen\HeroV2\derivatives\candidate005_lods.blend"
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
