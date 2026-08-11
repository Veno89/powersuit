# Weapon-clearance gate

`validate_weapon_clearance.py` is an isolated, read-only Blender 5.2 audit for
the NextGen suit lane. It evaluates the real `PowerSuit_Armature`, the complete
24-action `PS_*` contract, the candidate's evaluated render geometry, and all
`Rifle_*` mesh components. It does not save the open blend or touch Generator114,
an FBX, a Unity prefab, or a scene.

## Run Candidate004

From the repository root:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background "ArtSource\PoweredSuitNextGen\candidates\aegis_vanguard_candidate_v004.blend" `
  --python "ArtSource\PoweredSuitNextGen\scripts\validate_weapon_clearance.py"
```

The default gate samples every authored keyframe in all 24 actions. Add
`-- --all-frames` to sample every integer frame. Add `-- --strict` for a
non-zero process result when forbidden intersections are found. Reports are
written here as JSON and text; `-- --include-instances` adds every raw contact
instance when forensic detail is needed.

## Contact policy

Only three contact families are accepted:

- primary-grip rifle components against the right hand;
- support-grip rifle components against the left hand; and
- `Rifle_Stock_ButtPad` against the authored right shoulder/chest docking zone
  in ready-action families.

Everything else is forbidden, including backpack/turbine contact while stowed
and armor penetration during draw or sheathe. Candidate003 identifies the hand
zones through bone parenting. Consolidated Candidate004 meshes must keep the
three contact regions as separate objects and set `aegis_contact_zone` to one of:

- `primary_grip_hand_right`
- `support_grip_hand_left`
- `stock_shoulder_right`

An untagged whole-body skinned object is intentionally not granted an exception:
an object-level exception could hide a real forearm, chest, or helmet collision.

## Candidate003 baseline

The checked-in baseline is expected to fail. It establishes that the gate sees
the known maquette problems before Candidate004 work begins:

- 24/24 actions and 162 authored keyframes sampled;
- right forearm/receiver and stock intersections recur in ready locomotion;
- left hand, forearm, and magazine/receiver intersections recur around reload;
- stowed rifle/backpack and draw/sheathe crossings remain blockers; and
- the source Candidate003 blend hash is unchanged by validation.

Use the grouped JSON evidence to compare Candidate004 directly. Promotion
requires zero forbidden groups, not merely a smaller count.

## Limitations

- The default authored-keyframe pass cannot see an inter-keyframe collision.
  `--all-frames` is stronger, but fractional high-speed tunnelling still needs a
  denser swept-volume test.
- BVH crossings use evaluated render triangles. Full containment is supplemented
  by deterministic vertex/ray sampling, which is not a general CSG solver and
  can miss pathological open or deeply concave meshes.
- Reported AABB overlap is a prioritisation proxy, not physical penetration
  depth.
- Semantic exceptions describe intended attachment zones; they do not prove
  finger quality, comfort, skin deformation, or visual quality.
- This gate does not test suit self-intersection, cloth, Unity colliders,
  retargeting, LOD equivalence, or runtime visibility rules.
