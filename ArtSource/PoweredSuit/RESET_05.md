# Reset 05 — ergonomic stock + render-first validation

## Why Reset 04 stopped

Reset 04 successfully passed the new rigid-weapon infrastructure and reached the actual stance check. The local Blender result was:

- sight lateral: `0.228 m`
- sight vertical: `0.087 m`
- sight front clearance: `0.080 m`

Only the lateral relationship exceeded the `shouldered_precision` preferred envelope (`0.180 m`). This is a design-fit issue: a centred long-gun receiver placed directly from a centred buttpad on the outer right shoulder naturally sits too far from the helmet centreline on this bulky powered suit.

## Weapon-design correction

Reset 05 keeps the scope/bore/receiver at local X=0 and moves the **rear stock interface**, not the optic.

- stock contact X: `+0.085 m`
- buttpad centre X: `+0.085 m`
- rear bridge centre X: `+0.085 m`
- intermediate stock rails/struts progressively dogleg from the receiver toward that shoulder offset

When the offset stock-contact helper is seated on the same right-shoulder stance anchor, the rigid receiver/scope centreline moves inward by approximately 85 mm. Based on Reset 04's measured `0.228 m` lateral relationship, the nominal expected relationship is approximately `0.143 m` before small orientation/projection differences.

This is the intended future-weapon pattern: grips/stocks may be designed within declared ergonomic limits before the asset is frozen; the animation solver never moves weapon children.

## Pipeline-behaviour correction

The previous pipeline failed at the first reviewable numerical mismatch, requiring another local run before any images could be inspected. Reset 05 separates **structural blockers** from **review blockers**.

Structural blockers still abort. Review blockers are collected, the mandatory renders are produced, and the report is marked `REVIEW_BLOCKED`. Approval already requires `automated_validation == PASS`, so export remains safely locked.

This change is intended to make one local run reveal the whole current visual/ergonomic state rather than one threshold at a time.
