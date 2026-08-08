# Weapon Framework Reset 03

Reset 02 proved that decimal precision was not the real rigidity problem: the rifle validated before `save_current_blend()`, then failed at the first aim-stage rigidity check in the same Blender process. No pose code had modified the weapon in between.

The v2 gate hashed `child.matrix_local`. That value includes Blender parenting bookkeeping and can be rewritten when Blender serialises the file even when the child remains visibly fixed relative to `RifleRoot`.

Reset 03 changes the rigid-asset contract to measure the thing we actually care about:

`RifleRoot.matrix_world.inverted_safe() @ child.matrix_world`

That is the effective child transform in weapon-root space. Moving the whole weapon root cancels out; moving, rotating, or scaling an individual weapon part still changes the manifest.

The new v3 manifest also records generated mesh geometry and visible modifier settings. On a real mismatch the error now identifies which child changed and whether the difference is its root-relative transform, mesh geometry, or modifiers.

No weapon dimensions, scope placement, stance values, arm solve, render gate, approval gate, or export gate were loosened. The scope remains centred and rigid.

The same semantic root-space accessor is now used by aim placement and rifle validation for stock/grip/sight locations. Runtime stages no longer depend on post-parenting `matrix_local` values. The only remaining `matrix_local` write is during helper authoring before those helpers are parented under the identity root.
