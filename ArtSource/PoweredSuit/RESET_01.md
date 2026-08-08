# Reset 01

This is a deliberate architecture reset after the earlier pose-specific search approach repeatedly produced geometrically valid but visually unacceptable solutions.

Changed strategy:

1. The sniper is a rigid asset with a frozen child-transform/mesh signature.
2. The scope is centred on the receiver and cannot be moved by animation code.
3. Weapon ergonomics are expressed as semantic hardpoints.
4. `PS_Aim` uses the reusable `shouldered_precision` stance family.
5. The rifle root is placed from the stock/shoulder relationship only; there is no combinatorial optimizer that chases hand or visor targets.
6. Arms solve to the frozen primary/support grips.
7. The head may settle only slightly toward the optic.
8. Sight validation uses a tolerance envelope, not an exact eye point.
9. If the rigid asset does not fit the stance, the pipeline fails and reports that the weapon design must be revised.
10. Export remains locked until the new close-up renders are visually approved.

This package has not been run in Blender in the assistant environment. A successful Python compile is not visual approval.
