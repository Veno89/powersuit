# Candidate006 weapon upgrade plan

## Objective

Build **Candidate006 / NextGen Precision Rifle 001** as an isolated,
production-shaped rifle candidate for Aegis Vanguard Candidate005. The phase
solves the inherited rifle's visual quality, powered-suit ergonomics, scope
clearance, stow path, and verified weapon/suit clearance together.

This is a bounded precision-rifle phase. It does not upgrade the assault rifle
or heavy weapon, change weapon gameplay, add audio/VFX, or replace anything in
Unity.

## Measured Candidate006 status

The isolated build is complete as a production-architecture/review candidate,
not as a Unity-ready weapon. The blend hashes to
`093d5f8dcaede5eb7e7317bb63b98d08776d204f3fbaaf627a271bb899fb1227`,
and the production report records that source hash before and after validation;
Candidate005 remains unchanged at
`0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`.

- WeaponV2 structural gate: **PASS**, 156 checks, 0 errors, 0 warnings;
  `promotion_authorized: false`
- Exact preserved contract: 23 bones and 24 actions
- Rifle-plus-optic LOD totals: `23,216 -> 13,168 -> 5,512 -> 1,884`
- Combined Candidate005/Candidate006 LOD0: 111,532 triangles, 5 renderers,
  8 estimated draws at the hard ceiling
- Review package: 13 unique, source-bound renders
- Scope proof: real aperture proxy within `1.69e-7 m`, 0.021 m eye relief,
  6 m physical target, all 5 rays hitting the board, and a readable 4-line
  reticle with 4 range ticks
- Aim proof: 0.01969 m lateral, 0.01720 m vertical, 3.256 degree axis error,
  and 0.19414 m front clearance
- Mechanism proof: 0.333135 m magazine travel and 0.095 m bolt travel, both
  returning with zero error
- Authored visible clearance: **FAIL**, 377 forbidden instances/17 groups over
  162 samples, with 197 allowed contacts. Breakdown: 301 recurring
  grip/wrist-shell, 35 containment, and 41 manipulation/transition failures.

The 923-integer-frame and 324 dense-transition sweeps were not run because the
authored prerequisite already fails. Stow/draw/sheathe, reload/bolt, and final
PBR/art polish remain blocking. No FBX or Unity integration was created.

## Immutable safety boundary

- Use the hash-pinned Candidate005 blend
  `0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84`
  as the suit and animation reference; never edit it in place.
- Preserve Candidate004, Generator114, the current rifle, all existing source
  and Unity hashes, and every recorded rollback artifact.
- Generate a parallel weapon ID, collection, blend, reports, renders, and rigid
  manifest. Freeze Candidate006's own geometry and hardpoints only after the
  deliberate ergonomic design is accepted.
- Preserve the 23-bone rig, the exact 24 `PS_*` action names, ranges and Action
  Slots, zero root motion, `WeaponRoot`, `WeaponMagazine`, `WeaponBolt`, and the
  current semantic weapon-handling contract.
- Retain the canonical rifle frame: `+Y` bore/forward and `+Z` up. Keep the bore,
  muzzle and optic centered and rigid; never offset individual visual parts at
  animation time to rescue a pose.
- Do not export an FBX or modify a GUID, prefab, animator controller, scene,
  material, project setting, or other Unity asset in this phase. Unity
  integration is a separately approved promotion step after every gate passes.

## Visual target

The rifle should look like equipment made for the same adult, dark,
industrial-gothic suit rather than a collection of toy-like primitive blocks:

- soot-black coated armor over blue-black carbon composite;
- visible braided carbon-fibre wiring in protected recessed channels;
- oily gunmetal mechanisms and rails;
- restrained tarnished-chrome wear surfaces, fasteners, and edge details;
- sparse cyan status/emissive accents, never broad glowing panels;
- a layered receiver shroud, purposeful access panels, recessed fasteners,
  believable seams, heat management, and a readable mechanical bolt/magazine;
- asymmetric functional detail without symbols, spikes, fantasy ornament, or
  silhouette noise; and
- soot, grime, rubbed edges, and roughness variation at several scales.

The scope must remain a conventional, legible precision optic. Its ocular,
glass and complete sight corridor must be unobstructed in the scope view; no
receiver, weapon accessory, hand, helmet, or armor may cover the target image.

## Ergonomic and packaging changes

The following audit-derived values are the initial bounded search envelope, not
permission to move the weapon differently per pose:

- Dogleg the support grip and its helper together by `+0.09` to `+0.10 m` on
  rifle-local X and `+0.10` to `+0.12 m` on rifle-local Y.
- Dogleg the primary grip and its helper together by `-0.06` to `-0.08 m` on
  rifle-local X.
- Reduce receiver-core X/Z cross-section by `22-28%`, lower-receiver-spine X/Z
  cross-section by `25-30%`, and upper-shell X/Z cross-section by `20-22%`.
- Reduce stock rail and strut cross-sections by `18-25%` while keeping the
  buttpad in its authored shoulder pocket and preserving the centered sight
  line.
- Move the stowed weapon root `0.22-0.24 m` farther rearward from the suit.
- Re-author the draw and sheathe waypoints so the magazine, lower receiver,
  support grip, and stock sweep around the backpack, turbine, chest, and arm
  volumes rather than through them.

Model the receiver, grips, magazine well, stock, optic mounts, and handguard
around the final versioned hardpoints. Regenerate the affected ready, hip-fire,
aim, locomotion, hover, reload, bolt, stowed, draw, and sheathe transforms while
preserving all 24 action contracts. Do not solve clearance by shrinking hands,
forearms, chest armor, or the Candidate005 silhouette.

## Production geometry and materials

Candidate006 must extend the HeroV2 handoff without exceeding its existing
budgets:

| Asset | LOD0 | LOD1 | LOD2 | LOD3 |
|---|---:|---:|---:|---:|
| Rifle target | 20,000-30,000 | 10,000-15,000 | 4,000-6,000 | 1,000-2,000 triangles |

- Use one consolidated rifle renderer plus at most one optic/glass renderer.
  Together with Candidate005, stay at no more than five target renderers and
  six target draw calls.
- Supply complete finite `UV0` with intentional seams, padding and overlap
  proof. Use a unique 2K rifle PBR set and, if separate, a 2K optic set.
- Author BaseColor, normal, metallic/roughness/AO, and restrained emission.
  Procedural preview maps are not final production textures.
- Meet the HeroV2 topology gate: applied transforms, triangulated output, no
  loose, degenerate, duplicate, boundary, or non-manifold defects, and no
  empty material assignments.
- Generate deterministic draft LODs, then repair silhouettes, UV seams,
  normals, material boundaries, optic readability, and small-part collapse by
  hand before approval.

## Corrected clearance policy

The production gate must evaluate Candidate005's three visible render meshes.
Proxy mode remains optional localisation evidence and can never promote an
asset.

Before Candidate006 can pass, revise the validator and asset metadata as
follows:

1. Add stable face-domain semantic IDs to evaluated suit and weapon geometry.
   Suit faces distinguish the right primary hand, left support hand, right
   shoulder stock pocket, manipulation hand surfaces, and ordinary forbidden
   geometry. Weapon faces distinguish the primary grip, support grip, buttpad,
   magazine grasp surface, bolt handle, and ordinary forbidden geometry.
2. Classify the actual intersecting triangle faces. An object name or broad
   component role alone cannot grant an exception.
3. Require both compatible face IDs and an explicit action/frame window:
   - primary and support grip contact only while that hand is authored on its
     matching grip;
   - left-hand magazine contact only during `PS_Reload` frames `25-75`;
   - right-hand bolt-handle contact only during `PS_BoltCycle` frames `4-16`;
   - buttpad contact only in the tagged shoulder pocket and ready families; and
   - no armor contact while stowed or during draw/sheathe.
4. Fail closed when semantic attributes, expected face coverage, policy
   version, action, frame, or source manifest are missing or inconsistent.
   Containment is always forbidden, including inside an otherwise compatible
   contact zone.
5. Record geometry source, evaluated object names and hashes, policy and
   manifest hashes, semantic classification, action/frame evidence, and
   detection method in JSON. Report a real contact-depth metric before applying
   any small contact tolerance; do not treat AABB overlap as penetration depth.

An allowed manipulation contact must remain confined to its tagged grasp
surfaces and visual contact envelope. It may not hide receiver, forearm, chest,
scope, or deep hand penetration.

## Validation sequence

1. Freeze and hash the Candidate005, Candidate004, Generator114, current-rifle,
   action-contract, helper-contract, and Unity rollback baselines.
2. Implement and unit-test visible/proxy source selection, face-pair semantic
   classification, action/frame windows, fail-closed metadata checks, and
   deterministic report output.
3. Iterate the new rifle and affected poses with the fast 162-authored-keyframe
   visible audit. Proxy diagnostics may localise a failure but never waive it.
4. Run the canonical visible `--all-frames --strict` sweep across all 923
   integer action frames. Required result: zero forbidden instances and zero
   forbidden groups.
5. Sample `PS_Reload`, `PS_BoltCycle`, `PS_Weapon_Draw`, and
   `PS_Weapon_Sheathe` every `0.5` frame. Their inclusive ranges produce 324
   dense transition samples. Required result: zero forbidden instances and
   zero forbidden groups outside the narrowly allowed tagged contacts. Use the
   repeatable exact `--action` filters plus `--frame-step 0.5`; do not combine
   this dense audit with `--all-frames`.
6. Re-run rigid-manifest, hierarchy, helper, articulation, sighting, topology,
   UV/PBR, LOD, render-budget, and immutable-hash gates.

## Pose and visual acceptance

- Right and left wrists remain within `0.020 m` of their authored wrist targets.
- Arm reach remains at or below `1.0`; right elbow bend remains `20-168 deg`
  and left elbow bend `35-164 deg`.
- Optic/helmet and non-stock weapon/torso evaluated-mesh overlap counts are
  zero. Stock contact occurs only at the intended buttpad/shoulder faces.
- Sight error remains within the existing contract: lateral `<=0.035 m`,
  vertical `<=0.040 m`, sight-axis `<=5 deg`, and ocular front clearance
  `0.015-0.200 m`.
- The muzzle and sight point forward in idle, hip fire, aim, movement, flight,
  reload recovery, and bolt recovery. The rifle never falls diagonally across
  the chest while firing.
- Review renders cover neutral front/side/three-quarter, aim, hip fire, scope
  ocular, reload, bolt, run, hover, stowed, draw, and sheathe views. Hands,
  forearms, stock seating, magazine travel, backpack clearance, and scope
  framing must all be readable.

## Definition of done

Candidate006 is complete only when the parallel source and all reports are
reproducible from documented commands; every preserved baseline hash and
23-bone/24-action contract still matches; geometry, UV/PBR, LOD, handling,
sighting, and clearance gates pass; the 923 integer-frame and 324 dense
transition-sample visible sweeps contain zero forbidden contacts; and the owner
approves the review renders.

Candidate006 has **not** reached this definition of done. Its structural gate
passes, but its authored visible-clearance result and remaining visual polish
keep the candidate on hold.

Passing this phase authorises preparation of a separate Unity integration
proposal. It does not itself authorise replacement of the current Unity rifle
or player model.
