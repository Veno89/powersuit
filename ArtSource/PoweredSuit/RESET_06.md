# Reset 06 — validator consistency + character-side sight settling

## Why this reset exists

Reset 05 finally reached the visual validation stage, but the standalone rifle stage then failed because the validator contained an obsolete assumption: it required both the stock and sight hardpoints to be centred even though the new powered-suit stock was deliberately offset.

That was a validation bug, not a Blender/runtime failure.

## Correct invariant

For the reference sniper:

- bore/receiver/optic remain centred;
- sight ocular remains on the optic axis;
- stock may be laterally offset during weapon design;
- stock contact must remain attached to the physical buttpad;
- animation may move only `RifleRoot`, never individual rifle parts.

## Pose correction

The Reset 05 renders show that a large rigid helmet cannot reach a shouldered centred optic using yaw/pitch alone. Reset 06 therefore adds a bounded head/neck roll to the `shouldered_precision` stance. This is a reusable character behavior, not a weapon deformation.

The solver evaluates integer roll candidates from -12 to +12 degrees, scores the resulting visor-to-ocular relationship, and applies the best candidate. The weapon remains rigid throughout.

## Review-first behavior

Non-structural rifle geometry concerns are written into `automated_blockers` and the five standalone rifle renders are still produced. Missing objects, broken rigidity, missing optic-axis parts, or actual optic warping remain structural failures.
