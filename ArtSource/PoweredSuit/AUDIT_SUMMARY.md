# Audit / architecture summary — Reset 06

Reset 06 is derived from the actual Reset 05 package after reviewing the user's Blender console output and all 13 supplied Aim validation renders.

Observed Reset 05 facts:

- the rigid weapon stage completed;
- the scope is visually centred on the receiver and no longer warped sideways;
- `PS_Aim` rendered frames 1, 15 and 30 consistently;
- the ocular remains visibly too far sideways from the helmet sight line;
- trigger/support hand close-ups are not yet strong enough for visual approval;
- `render_rifle_validation.py` aborted on `Sniper stock/sight hardpoints are not centred on the rigid asset.`

The validator failure was internally contradictory: Reset 05 deliberately authored a non-zero stock lateral offset but then required the stock-contact helper to be centred. Reset 06 removes that false invariant. Only the optic/sight axis must remain centred. The stock hardpoint is instead validated against the physical buttpad and a plausible maximum ergonomic offset.

The `shouldered_precision` stance is also improved at the character/ergonomic layer rather than by warping the optic:

- stock asset lateral offset: 0.085 -> 0.110 m
- stance stock inward seating: 0.050 -> 0.080 m
- new deterministic head/neck roll allowance: up to 12 deg
- preferred sight lateral tolerance: 0.180 -> 0.075 m

The head-roll solve searches only a small bounded character-side degree of freedom. Rifle child transforms remain frozen and rigidity checks remain active.

Rifle generator version is 102. Aim creation, both render validators and export all require version 102 or newer.

Visual approval remains pending. Reset 06 is not considered successful until the Blender renders are inspected.
