# Weapon Framework Reset 04

Reset 04 fixes the remaining false-positive rigid-weapon failure seen immediately after rifle construction/save:

`Rifle_Trigger: root-relative transform`

## Cause

The framework was still asking Blender object transforms to serve two jobs at once:

1. describe where a generated rifle mesh part belongs inside the weapon, and
2. prove that the part has not moved later.

Blender 5.2 may normalise object parenting/rotation bookkeeping during save/evaluation even when visible geometry has not changed. The trigger exposed that weakness.

## Reset 04 representation

Generated rifle mesh parts are now canonicalised once before the rigid asset is frozen:

- each mesh part's authored RifleRoot-space transform is recorded;
- that transform is baked into the mesh vertices;
- every mesh child is reset to identity under RifleRoot;
- helper hardpoints keep authored semantic transforms;
- the rigid signature covers authored hardpoints, baked mesh geometry, child identity/type and generated modifiers;
- runtime validation separately requires every mesh child to remain at identity and every helper to remain at its authored transform.

Moving RifleRoot as a whole is still allowed. Moving a scope, stock, grip, trigger, barrel, magazine or other weapon child is still rejected.

## Deliberately unchanged

- sniper dimensions and visible design
- scope centring
- stance-family parameters
- arm proportions
- legacy Actions
- aim solver logic
- validation cameras
- approval/export gates

This is an infrastructure correction only. Blender execution and visual validation are still required.
