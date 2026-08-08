# Powered Suit — Weapon Framework Reset 06

Blender target: **5.2 LTS**.

Reset 06 is based on the actual Reset 05 Blender run and supplied validation renders. It keeps the rigid-weapon architecture, but fixes the validator contradiction that stopped rifle validation and makes one deliberate stance/ergonomic correction to bring the powered-suit helmet closer to the centred optic.

## Run

1. Extract this ZIP to a new folder.
2. Double-click `01_BUILD_AND_RENDER_WINDOWS.bat`.
3. Let the complete pipeline finish.
4. Send the console text plus every PNG from:
   - `renders\aim_validation`
   - `renders\rifle_validation`

Do **not** run `02_APPROVE_AND_EXPORT_WINDOWS.bat` until the renders are visually accepted.

## What Reset 05 proved

The supplied Reset 05 renders confirmed that the scope itself is now rigid and centred; it no longer sticks out sideways as it did in the old pose-specific strategy. The remaining visible problem is character/weapon fit: the helmet still sits noticeably to the side of the ocular, while the grip close-ups are not yet good enough for approval.

Reset 05 also exposed a code bug in `render_rifle_validation.py`: the weapon design intentionally used a laterally offset powered-suit buttstock, while the validator still required the stock-contact helper to have X=0. That assertion contradicted the new asset design and aborted before the five standalone rifle renders.

## Reset 06 changes

### 1. Correct stock validation semantics

The **sight/optic axis** must remain centred. The **stock** may have a deliberate powered-suit lateral offset.

Rifle validation now checks that:

- scope tube/objective/ocular/lenses/mounts remain centred on the rifle;
- the sight hardpoint remains on that optic axis;
- the stock hardpoint remains physically attached to the buttpad;
- an excessive stock dogleg is reported as an automated blocker rather than confused with optic warping.

Reviewable geometry issues no longer hide the rifle renders.

### 2. Better shouldered-precision fit

Based on the Reset 05 images, the rear stock interface is increased from 85 mm to **110 mm** of powered-suit ergonomic offset, while the receiver, barrel and scope remain centred.

The stance seats the buttpad **30 mm farther inward** on the shoulder pocket than Reset 05.

### 3. Head/helmet roll is now a legitimate stance degree of freedom

A real shooter naturally rolls/leans the head toward a shouldered optic. The previous solver only allowed small yaw/pitch corrections, which could not close the remaining side-to-side separation of this large rigid helmet.

The `shouldered_precision` stance now permits up to **12 degrees** of deterministic head/neck roll. The solver searches only this bounded character-side degree of freedom and picks the smallest useful roll. The weapon is never moved or deformed during that search.

### 4. Stricter sight target

The preferred final lateral sight envelope is tightened from 180 mm to **75 mm**. If the pose remains visibly side-by-side, validation will report `REVIEW_BLOCKED` while still producing all mandatory renders.

## Architecture preserved

- rigid weapon under `RifleRoot`
- centred receiver/barrel/optic
- semantic primary/support grip, stock, sight and muzzle hardpoints
- stance-family character solving
- explicit Blender 5.2 Action Slot handling
- legacy `PS_Idle`, `PS_Walk`, `PS_Hover` preservation
- approval/hash-gated FBX export
- visual inspection remains mandatory

See `WEAPON_FRAMEWORK.md` and `RESET_06.md` for details.
