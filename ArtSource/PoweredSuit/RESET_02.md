# Weapon Framework Reset 02

This revision fixes a false-positive rigid-weapon gate failure seen immediately after Blender saved/reloaded the freshly generated rifle.

The rigid signature still protects every direct weapon-child transform and all generated mesh vertex coordinates, but canonicalises floating-point values to 6 decimal places before hashing. Blender 5.2 can renormalise matrix/quaternion values by tiny amounts during file serialisation; the previous 8-decimal hash treated that harmless numerical noise as weapon deformation.

No stance, weapon dimensions, scope placement, animation solve, validation limits, or export behaviour were loosened. The scope remains a rigid centred asset and animation may still move only RifleRoot.
