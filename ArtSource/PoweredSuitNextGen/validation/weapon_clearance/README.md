# Weapon-clearance gate

`validate_weapon_clearance.py` is an isolated, read-only Blender 5.2 audit for
the NextGen suit lane. It evaluates the real `PowerSuit_Armature`, all 24
`PS_*` actions, the selected suit collision geometry, and all `Rifle_*` mesh
components. It does not save the open blend or modify Generator114, an FBX, a
Unity prefab, a controller, or a scene.

## Geometry-source modes

The geometry source is an explicit part of every report:

- `--geometry-source visible` is the default and the only canonical promotion
  result. It evaluates the candidate's actual rendered meshes and excludes
  hidden clearance proxies and runtime anchors.
- `--geometry-source proxy` is an opt-in diagnostic. It evaluates only objects
  tagged `aegis_clearance_proxy=true`, which retain per-piece bone and contact
  semantics useful for localising problems.

Proxy geometry is not visible-geometry clearance proof. Candidate005's visible
undersuit is voxel-remeshed and smooth-skinned, and its visible armor is
consolidated and repaired. The 254 retained source pieces therefore do not have
surface or deformation equivalence with the three production renderers. Never
substitute a proxy PASS for the canonical visible result or compare raw proxy
and visible group counts as if they used the same granularity.

## Run Candidate005

Canonical visible-geometry audit:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v005.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py"
```

Explicit proxy diagnostic with a separate output label:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v005.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --geometry-source proxy `
  --label aegis_vanguard_candidate_v005_proxy_weapon_clearance
```

The default pass samples authored keyframes. `--all-frames` samples every
integer action frame, `--strict` returns non-zero after writing reports when a
forbidden contact remains, and `--include-instances` includes every raw contact
instance. These flags belong after Blender's `--` separator.

## Candidate005 results

Both final reports audit the same preserved Candidate005 blend:

`0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`

| Report | Geometry | Actions / samples | Forbidden instances | Groups | Status |
|---|---:|---:|---:|---:|---:|
| Canonical | 3 visible consolidated meshes | 24 / 162 authored keyframes | 3,894 | 72 | **FAIL** |
| Diagnostic | 254 hidden per-piece proxies | 24 / 162 authored keyframes | 5,489 | 240 | **FAIL** |

The canonical report is
`aegis_vanguard_candidate_v005_weapon_clearance.{json,txt}`. The diagnostic is
`aegis_vanguard_candidate_v005_proxy_weapon_clearance.{json,txt}`.

Neither result is the final clearance proof: both sample only 162 authored
keyframes. Candidate006 must reach zero forbidden intersections on the visible
geometry across all 923 integer frames, then repeat the manipulation and
draw/sheathe transitions at denser subframe spacing before any Unity promotion.

## Current contact policy

The current object-level policy recognises three intended contact families:

- primary-grip components against the right hand;
- support-grip components against the left hand; and
- the tagged buttpad against the right shoulder/chest stock zone in ready
  action families.

Everything else is forbidden, including backpack/turbine contact while stowed
and armor penetration during draw or sheathe. Per-piece candidates and proxy
diagnostics can identify the hand and shoulder zones by bone, object, or
`aegis_contact_zone` metadata.

The three consolidated Candidate005 renderers do not carry sufficiently precise
face-level contact zones, so the canonical report deliberately grants no broad
whole-renderer exception. Candidate006 must replace this object-wide policy
with evaluated face-level suit/weapon tags and action/frame-bounded grip,
magazine, bolt, and stock rules. Missing or incompatible semantic tags must fail
closed, and containment must never be accepted as an intentional contact.

## Historical baselines

Candidate003 and Candidate004 reports remain expected failures. They establish
that the validator sees recurring receiver/forearm, magazine/hand, stock,
stowed-backpack, and draw/sheathe problems while preserving each source blend.
They are comparison evidence, not promotion waivers.

## Limitations

- The default authored-keyframe pass cannot see an inter-keyframe collision.
  Integer `--all-frames` sampling is stronger, but fast manipulation and
  transition actions still require denser subframe sampling.
- Visible mode evaluates rendered triangles; proxy mode intentionally does not.
- BVH surface crossings are exact for the evaluated triangles. Full containment
  uses deterministic vertex/ray sampling and is not a general CSG solver.
- AABB overlap depth and volume are prioritisation proxies, not physical
  penetration depth or swept volume.
- Semantic exceptions express authored intent; they do not prove finger pose,
  comfort, skin deformation, or visual quality.
- The gate does not test suit self-intersection, cloth, Unity colliders,
  retargeting, LOD equivalence, or runtime visibility rules.
