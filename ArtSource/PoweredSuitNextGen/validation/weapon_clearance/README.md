# Weapon-clearance gate

`validate_weapon_clearance.py` is an isolated, read-only Blender 5.2 audit for
the NextGen suit lane. It evaluates the real `PowerSuit_Armature`, all 24
`PS_*` actions, the selected suit collision geometry, and the explicit
Candidate006 or Candidate007 LOD0 weapon renderers (with a legacy `Rifle_*`
fallback). It does not save the open blend or modify Generator114, an FBX, a
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

Candidate005 predates the face manifest. Use a temporary label when smoke
testing backward-compatible, conservative failure so the archived schema-2
reports below are not overwritten:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --python-exit-code 1 `
  --background "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v005.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --output-dir "Temp\candidate005_clearance_policy_smoke" `
  --label candidate005_face_policy_smoke
```

Explicit proxy diagnostic with a separate output label:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --python-exit-code 1 `
  --background "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v005.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --geometry-source proxy `
  --output-dir "Temp\candidate005_clearance_policy_smoke" `
  --label candidate005_proxy_policy_smoke
```

The default pass samples authored keyframes. `--all-frames` samples every
integer action frame. Repeat the case-sensitive `--action PS_ActionName` option
to audit an exact subset in any mode. `--frame-step N` selects inclusive dense
sampling and accepts only finite values in `(0, 1]`; it cannot be combined with
`--all-frames`. Unknown or duplicate action filters, invalid steps, conflicting
modes, and ranges that could emit an out-of-range or unsafe sample set fail
closed before geometry evaluation. `--strict` returns non-zero after writing
reports when a forbidden contact remains, and `--include-instances` includes
every raw contact instance. These flags belong after Blender's `--` separator.
Visible-mode metadata or manifest failure is a structural hard error after the
reports are written, even without `--strict`; `--strict` additionally makes
ordinary forbidden-contact failures an error. Blender reports Python errors as
a non-zero process status when invoked with the documented
`--python-exit-code 1` flag.

Candidate006's canonical promotion command uses visible geometry explicitly and
must include `--all-frames --strict`:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --python-exit-code 1 `
  --background "ArtSource\PoweredSuitNextGen\candidates\nextgen_precision_rifle_candidate_v006.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --geometry-source visible `
  --all-frames `
  --strict `
  --label nextgen_precision_rifle_candidate_v006_weapon_clearance
```

The separate canonical dense-transition audit samples both endpoints of the
four manipulation and carry-transition ranges every half frame. Its exact
counts are Reload 167, BoltCycle 39, Draw 59, and Sheathe 59: **324 samples**.

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --python-exit-code 1 `
  --background "ArtSource\PoweredSuitNextGen\candidates\nextgen_precision_rifle_candidate_v006.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --geometry-source visible `
  --action PS_Reload `
  --action PS_BoltCycle `
  --action PS_Weapon_Draw `
  --action PS_Weapon_Sheathe `
  --frame-step 0.5 `
  --strict `
  --label nextgen_precision_rifle_candidate_v006_dense_weapon_clearance
```

Schema-3 reports retain the legacy top-level `sample_mode` and
`sampled_frame_count` fields and add a `sampling` object containing the exact
requested filters, canonical selected-action order, frame step, inclusive-
endpoint flag, and repeated sample count.

## Run Candidate007 certification

Candidate007 requires three strict, visible-geometry reports bound to the exact
same source blend. Run the authored-keyframe prerequisite first:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --python-exit-code 1 `
  --background "ArtSource\PoweredSuitNextGen\candidates\nextgen_precision_rifle_candidate_v007.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --geometry-source visible `
  --strict `
  --output-dir "ArtSource\PoweredSuitNextGen\validation\weapon_clearance" `
  --label nextgen_precision_rifle_candidate_v007_authored_weapon_clearance
```

Then certify every integer frame across all 24 actions:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --python-exit-code 1 `
  --background "ArtSource\PoweredSuitNextGen\candidates\nextgen_precision_rifle_candidate_v007.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --geometry-source visible `
  --all-frames `
  --strict `
  --output-dir "ArtSource\PoweredSuitNextGen\validation\weapon_clearance" `
  --label nextgen_precision_rifle_candidate_v007_all_frames_weapon_clearance
```

Finally, certify the four fast actions at inclusive `0.125`-frame cadence:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --python-exit-code 1 `
  --background "ArtSource\PoweredSuitNextGen\candidates\nextgen_precision_rifle_candidate_v007.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py" `
  -- `
  --geometry-source visible `
  --action PS_BoltCycle `
  --action PS_Reload `
  --action PS_Weapon_Draw `
  --action PS_Weapon_Sheathe `
  --frame-step 0.125 `
  --strict `
  --output-dir "ArtSource\PoweredSuitNextGen\validation\weapon_clearance" `
  --label nextgen_precision_rifle_candidate_v007_dense_transition_weapon_clearance
```

## Candidate005 results

Both final reports audit the same preserved Candidate005 blend:

`0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`

| Report | Geometry | Actions / samples | Forbidden instances | Groups | Status |
|---|---:|---:|---:|---:|---:|
| Canonical | 3 visible consolidated meshes | 24 / 162 authored keyframes | 3,894 | 72 | **FAIL** |
| Diagnostic | 254 hidden per-piece proxies | 24 / 162 authored keyframes | 5,489 | 240 | **FAIL** |

These are archived schema-2 baseline reports generated before the Candidate006
face-policy upgrade. The canonical report is
`aegis_vanguard_candidate_v005_weapon_clearance.{json,txt}`. The diagnostic is
`aegis_vanguard_candidate_v005_proxy_weapon_clearance.{json,txt}`.

Under schema 3, Candidate005 visible mode still detects all 3,894 historical
instances, preserves the blend hash, and fails closed because its three suit
renderers and retained rifle parts lack the new manifest and face attributes.
The temporary runtime smoke records 110 face-evidence groups; this count must
not be compared to the schema-2 object's 72 groups as if their grouping policy
were identical.

Neither result is the final clearance proof: both sample only 162 authored
keyframes. Candidate006 must reach zero forbidden intersections on the visible
geometry across all 923 integer frames, then repeat the manipulation and
draw/sheathe transitions at denser subframe spacing before any Unity promotion.

## Candidate006 authored result

The preserved Candidate006 blend hashes to
`093d5f8dcaede5eb7e7317bb63b98d08776d204f3fbaaf627a271bb899fb1227`,
and the production report records that source hash before and after validation.
Its first canonical visible-geometry prerequisite samples all 162 authored
keyframes:

| Geometry | Actions / samples | Forbidden instances | Groups | Allowed contacts | Status |
|---|---:|---:|---:|---:|---:|
| Candidate005 suit + Candidate006 LOD0 rifle/optic | 24 / 162 authored keyframes | 377 | 17 | 197 | **FAIL** |

The 377 forbidden instances comprise 301 recurring grip/wrist-shell contacts,
35 containment failures, and 41 manipulation/transition failures. Because this
authored pass fails, neither the full 923-integer-frame audit nor the 324-sample
dense-transition audit was run. That is intentional fail-fast sequencing, not
missing promotion evidence: both stronger sweeps remain mandatory after the
authored result reaches zero forbidden contacts.

The durable authored reports are
`nextgen_precision_rifle_candidate_v006_authored_weapon_clearance.{json,txt}`.
Candidate006 remains an isolated review candidate; it has not been exported or
integrated into Unity.

## Candidate007 certified result

Candidate007 is a parallel successor, not a rewrite of Candidate006's archived
failure. Its final source hashes to
`686dd185c800bc44c897948026da17988a5083c17993c4ef9d03af247f6c5ff2`,
and every final report records that SHA-256 unchanged before and after the run.

| Sweep | Geometry | Actions / samples | Allowed contacts | Forbidden | Groups | Status |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Authored | Candidate005 suit + Candidate007 LOD0 rifle/optic | 24 / 483 | 783 | 0 | 0 | **PASS** |
| All integer frames | Candidate005 suit + Candidate007 LOD0 rifle/optic | 24 / 923 | 922 | 0 | 0 | **PASS** |
| Dense `0.125` frame | Candidate005 suit + Candidate007 LOD0 rifle/optic | 4 / 1,284 | 1,565 | 0 | 0 | **PASS** |

The dense action coverage is exact: BoltCycle 153, Reload 665, Draw 233, and
Sheathe 233 samples. These reports are
`nextgen_precision_rifle_candidate_v007_{authored,all_frames,dense_transition}_weapon_clearance.{json,txt}`.
They certify sampled Blender geometry only; no Candidate007 FBX or Unity
integration exists.

## Face policy and manifest contract

Visible mode uses policy `PS_CLEARANCE_FACE_POLICY_V1`, semantic schema
`PS_CLEARANCE_FACE_SEMANTICS_V1`, and embedded canonical text
`PS_CLEARANCE_MANIFEST.json` (`PS_CLEARANCE_MANIFEST_V1`). Evaluated triangle
faces carry `INT`/`FACE` attributes:

| Domain | Attribute | Ordinary | Intentional zones |
|---|---|---:|---|
| Suit | `ps_clearance_suit_zone_id` | 0 | 101 right primary hand; 102 left support hand; 103 right shoulder stock pocket; 104 left magazine manipulation; 105 right bolt manipulation |
| Weapon | `ps_clearance_weapon_zone_id` | 0 | 201 primary grip; 202 support grip; 203 buttpad; 204 magazine grasp; 205 bolt handle |

An exception requires the exact compatible pair and an explicit manifest
action/frame window. Magazine contact is hard-bounded to `PS_Reload` frames
25-75 and bolt contact to `PS_BoltCycle` frames 4-16. Primary-grip windows cannot
overlap the bolt manipulation interval, and support-grip windows cannot overlap
the magazine manipulation interval. The archived Candidate006 baseline declares
no draw/sheathe contact windows.

Candidate007 retains face policy V1 and semantic schema V1 while declaring
contact-window policy `PS_CLEARANCE_CONTACT_WINDOWS_CANDIDATE007_V3`. It rejects
primary-grip, support-grip, and buttpad windows for stowed and legacy
idle/walk/hover carry states. The only transition exceptions are primary grip at
Draw 26.75-30 / Sheathe 1-4.25 and support grip at Draw 29-30 / Sheathe 1-2;
there is no transition buttpad window. Containment is always forbidden, even
between otherwise compatible zones.

Every evaluated render object must match its manifest entry and carry
`ps_clearance_asset_role`, `ps_clearance_policy_version`,
`ps_clearance_semantic_schema`, `ps_clearance_manifest_sha256`,
`ps_clearance_expected_face_count`, and `ps_clearance_topology_sha256`.
The manifest pins Candidate005's source hash, asset IDs, complete object face
counts, semantic coverage, topology/semantic hashes, and all contact windows.
Unknown IDs, missing required zone coverage, non-canonical JSON, missing
actions, or any property/hash mismatch fail the entire visible gate closed.

Reports contain the selected geometry source, evaluated object names and
geometry hashes, full manifest and hash, policy/schema, intersecting face IDs,
action/frame/window evidence, detection method, and a deterministic evidence
hash. Wall-clock fields are intentionally omitted and JSON keys are sorted.
The real depth metric is sampled interior-vertex distance to the opposing
evaluated surface before the currently zero tolerance; AABB dimensions remain
separate prioritisation evidence and are never called penetration depth.

Proxy mode retains the previous object/bone policy strictly for localisation.
It does not require the face manifest and remains ineligible for promotion.

The Blender-independent contract tests run with:

```powershell
python -m unittest discover -s ArtSource/PoweredSuitNextGen/tests -v
```

## Historical baselines

Candidate003 and Candidate004 reports remain expected failures. They establish
that the validator sees recurring receiver/forearm, magazine/hand, stock,
stowed-backpack, and draw/sheathe problems while preserving each source blend.
They are comparison evidence, not promotion waivers.

## Limitations

- The default authored-keyframe pass cannot see an inter-keyframe collision.
  Integer `--all-frames` sampling is stronger. Candidate006's historical target
  used `--frame-step 0.5`; Candidate007 certification uses the documented
  filtered `--frame-step 0.125` audit for the four fast actions. Even that is
  discrete evidence rather than continuous swept-collision proof.
- Visible mode evaluates rendered triangles; proxy mode intentionally does not.
- BVH surface crossings are exact for the evaluated triangles. Full containment
  uses deterministic vertex/ray sampling and is not a general CSG solver.
- The sampled surface-distance metric is geometric depth, but a crossing with
  no sampled interior vertex can report zero. AABB overlap remains only a
  prioritisation proxy and is not swept volume.
- Semantic exceptions express authored intent; they do not prove finger pose,
  comfort, skin deformation, or visual quality.
- The gate does not test suit self-intersection, cloth, Unity colliders,
  retargeting, LOD equivalence, or runtime visibility rules.
