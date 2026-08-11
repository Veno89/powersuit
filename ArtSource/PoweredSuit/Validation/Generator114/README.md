# Generator 114 — Directional Locomotion Candidate

Generator 114 preserves Generator 113's repaired geometry, weapon rig, ready,
aim, stowed, reload, bolt, and run work while advancing the animation contract
from v4 to v5. It adds six stance-aware lateral loops:

- `PS_Walk_Left` / `PS_Walk_Right`
- `PS_Aim_Walk_Left` / `PS_Aim_Walk_Right`
- `PS_WeaponStowed_Walk_Left` / `PS_WeaponStowed_Walk_Right`

Unity combines these cardinal motions with the existing forward/backward loops
in signed 2D blend trees, so diagonal movement is blended from authored
directional footwork rather than rotating a forward walk.

## Frozen evidence

- Blender: 5.2.0 LTS
- animation contract: v5
- exact exported armature actions: 24
- mandatory renders: 13 aim + 5 rifle + 17 weapon animation = 35/35
- automated validation: PASS, 0 blockers
- technical visual review: APPROVED
- blend SHA-256: `6f2e09a53b46408ba2c3d485303b8c28811c263f1dae9a1e230fd3bafcda3f8a`
- validation report SHA-256: `0d71b5eb79f9fec697c587513774eb57ffaf23c029a80ec1c55852018c67f7f9`
- visual approval SHA-256: `811e6ebebe0f7ab373473da4339a78d88082379bb5df1cde523897f0756f781d`
- export manifest SHA-256: `6ce5c612b35a118076357d33de27da3ff669e6b743eb812eccd0956652c1faaf`
- exported FBX SHA-256: `4b5282d52470bbd624c8e18331bdd15b6f99b20174cfeea770f08134200d3b79`

Key automated measurements include 0.7130 m left/right foot separation,
0.8379 m powered-walk stride, 0.9341 m run stride, and 0.0365 m run airborne
clearance. User hands-on evaluation remains a separate repository acceptance
gate and was intentionally deferred for this batch.
