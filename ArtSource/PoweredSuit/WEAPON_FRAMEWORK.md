# Powered Suit Weapon Handling Framework — Reset 06

The framework is designed for many future weapons, not only the reference sniper.

## Core rule

A weapon is designed once and then frozen. Animation may move only the weapon root. It may never offset, rotate, scale, or remodel individual scope, stock, receiver, grip, barrel, magazine, or other weapon components to make a pose pass.

## Weapon hardpoint contract

Required semantic roles:

- `primary_grip`
- `support_grip`
- `stock_contact`
- `sight_ocular`
- `muzzle`

Optional roles already supported:

- `support_grip_min`
- `support_grip_max`

The weapon owns these hardpoints and its physical dimensions. A powered-suit-specific weapon may deliberately use an offset or shaped stock, but that is an asset-design decision and becomes rigid afterward.

## Stance families

A stance owns character behavior:

- torso pitch/yaw
- shoulder participation
- weapon-root pitch and shoulder seating
- arm reach limits
- head yaw/pitch/roll settling
- acceptable sight envelope

The reference stance is `shouldered_precision`.

Reset 06 adds bounded head/neck roll because a large rigid helmet cannot reproduce a human cheek weld through yaw/pitch alone. The roll solver modifies only the character and is limited by the stance profile.

## Validation philosophy

Structural corruption aborts immediately: missing rig/weapon data, broken rigidity, impossible reach, invalid Action data, or genuine optic-axis warping.

Reviewable ergonomic problems do not hide the evidence. They are accumulated as blockers, all mandatory validation renders are produced, and export stays locked.

Visual inspection remains authoritative for clipping, hand grip quality, shoulder contact, helmet sighting, proportions and silhouettes.

## Future weapons

Likely stance families include:

- `shouldered_combat`
- `shotgun_wide`
- `heavy_braced`
- `two_handed_pistol`
- `one_handed`
- `launcher`

Do not add all families at once. First visually approve the reference sniper/`shouldered_precision` implementation, then reuse the contract for new weapons.
