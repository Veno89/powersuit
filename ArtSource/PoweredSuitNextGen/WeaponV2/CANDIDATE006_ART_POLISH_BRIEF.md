# Candidate006 art-polish brief

This is a bounded implementation brief for `build_nextgen_precision_rifle_candidate006.py`.
It does not change `NGPR001_HARDPOINTS_V1`, weapon controls, action names/ranges,
clearance semantics, renderer budgets, or Unity assets.

> Historical note (2026-08-13): this brief records the pre-polish audit and
> implementation target. The resulting isolated Candidate006 structural gate
> passes 156/0/0 and produces 13 unique source-bound renders, but visual
> promotion remains on hold. The authored visible-clearance result is 377
> forbidden instances/17 groups over 162 samples (301 grip/wrist-shell, 35
> containment, 41 manipulation/transition). Stow/draw/sheathe, reload/bolt, and
> final PBR/art polish remain unfinished; no FBX or Unity integration exists.

## Audit baseline

- The current rifle LOD0 is 11,792 triangles and LOD1 is also 11,792. The
  production profile targets 20,000-30,000 and 10,000-15,000 respectively, so
  there is room for roughly 8,000-10,000 triangles of purposeful hero detail.
- `join_renderer()` clears every joined material and assigns `armor`. This
  collapses carbon, rubber, gunmetal, chrome, and status-light reads into one
  uniform grey/checkered surface.
- The receiver and handguard are dominated by long beveled boxes. Small square
  vents sit on top of the slab instead of creating a layered cage silhouette.
- The scope corridor is geometrically obstructed. Each centered optic mount
  reaches Z=0.305 while the optic axis is Z=0.315 and the inner radius is
  0.020. The windage turret reaches X=-0.016 and the elevation turret reaches
  Z=0.327, so both penetrate the open bore.
- The generated neutral images currently contain the suit, so they do not
  provide reliable rifle-only silhouette review.

## Locked coordinates

Do not move these helpers or their visible contact surfaces:

| Role | Local XYZ (m) |
| --- | --- |
| Primary grip | `(-0.070, -0.050, -0.040)` |
| Support grip | `(0.108, 0.300, -0.035)` |
| Stock contact | `(-0.112, -0.448, 0.132)` |
| Sight ocular | `(0.000, -0.280, 0.315)` |
| Muzzle | `(0.000, 1.175, 0.145)` |

The bore stays on +Y, the optic stays centered on X=0, and the magazine and
bolt details must remain assigned to `WeaponMagazine` and `WeaponBolt`.

## Implementation order

### 1. Preserve a four-material rifle palette

Keep at most four unique materials on the rifle renderer:

1. soot-black armor,
2. blue-black carbon/rubber,
3. oily gunmetal/tarnished chrome,
4. restrained cyan emission.

The optic renderer may retain glass separately. Do not clear material slots
after join. Remap duplicate slots and delete unused slots instead. Every
retained material must link Base Color, Metallic, Roughness, Normal, and
Emission on its Principled node to the Candidate006 maps. Multiply the common
base-color map by a material tint so the palette remains visibly distinct;
otherwise the same texture erases the material hierarchy again.

Visual target: about 70% near-black armor/carbon, 20-25% gunmetal, less than 5%
chrome edge/collar accents, and less than 1% cyan. Chrome must not cover the
entire barrel.

### 2. Clear the optic bore before adding detail

- Replace each centered 64x42x82 mm mount with either two side feet or a lower
  bridge whose top is at or below Z=0.283. A suggested lower bridge is centered
  at Z=0.257 with height 0.052.
- Move the windage turret outward: center X=-0.047, length 0.028 along X. Its
  inner end is then X=-0.033, beyond the 25 mm outer tube radius.
- Move the elevation turret up: center Z=0.360, length 0.032 along Z. Its lower
  end is then Z=0.344, beyond the optic outer surface at Z=0.340.
- Add thin front/rear glass discs just inside the objective and ocular rings;
  keep the center transmissive. If a reticle is authored, use sub-millimetre
  line geometry and leave the central 3 mm open.
- Add an automated bore guard: excluding lens/reticle faces, no optic component
  may intersect local `X=[-0.012,0.012]`, `Z=[0.303,0.327]` across
  `Y=[-0.282,0.295]`.

The ocular render must show an uninterrupted circular target corridor. Mounts,
turrets, rail, receiver, helmet, and weapon must not cover its center.

### 3. Replace the slab silhouette with an internal chassis plus armor skins

Keep the current outer envelope, but reduce the visible central volumes and
let separated layers cast readable shadows:

- Receiver core: retain the existing 0.340 m length, but use it as a recessed
  carbon chassis. Add 6-8 mm-thick asymmetric armor skins at X=+/-0.067. Split
  each side into an aft plate, action plate, and forward trunnion with 4-8 mm
  gaps. Avoid mirroring the ejection/bolt side.
- Handguard core: reduce toward `(0.082, 0.440, 0.060)` at Z=0.130. Add a top
  spine near Z=0.185, a lower keel near Z=0.085, and thin side rails at
  X=+/-0.052. Bridge them with four diagonal ribs per side. The recessed core
  supplies darkness; the ribs supply the silhouette and actual negative-space
  read.
- Break any uninterrupted planar run longer than 0.18 m with a seam, offset
  plate, collar, rib, or real step in depth. Texture-only rectangles do not
  count.
- Keep layer separation in the 3-6 mm range so it produces at least a 3-pixel
  shadow break in the 1280 px three-quarter render.

A small `extruded_x_panel(profile_yz, thickness)` helper and a rectangular
`beam_between(a, b, width, depth)` helper will produce more intentional armor
than stacking more generic cubes. Bevel segments may remain at two.

### 4. Add functional, asymmetric detail

Spend the remaining hero triangles on readable mechanisms, not evenly spaced
greeble:

- Right/action side: recessed ejection-port frame, bolt track cover, selector
  dial, magazine release, and two protected fasteners.
- Left side: a shallow removable service panel, one cable junction, and sparse
  status strip.
- Handguard: four framed vent bays per side, cable clamps at roughly
  Y=0.10/0.25/0.42/0.57, and a continuous 5-7 segment braided cable with a
  gentle Z offset. Keep the cable inside the armor envelope.
- Magazine: magwell collar, two shallow longitudinal ribs, and a layered base
  plate. Every new moving magazine part must be tagged and weighted to
  `WeaponMagazine`.
- Bolt: add a protected root collar and flattened tactile knob. Every new
  moving bolt part must be tagged and weighted to `WeaponBolt`.
- Primary grip: add a backstrap and three shallow finger/rubber ribs without
  changing the helper or the semantic contact surface.
- Support yoke: add hinge caps and a thin guard plate. Do not turn it into a
  bipod and do not extend below the existing grip silhouette.

### 5. Mature the stock, barrel, muzzle, and optic silhouette

- Stock: use two slimmer struts, a visible receiver hinge collar, a two-layer
  cheek riser, and a three-layer butt assembly. Keep the butt contact centered
  at the locked coordinate. Add one adjustment wheel or pin; do not add toy-like
  bilateral knobs everywhere.
- Barrel: make the long tube oily gunmetal, with chrome restricted to collars
  and worn edges. Add a short thermal sleeve near the handguard exit and one
  step before the muzzle brake.
- Muzzle: use a compact two-stage brake with real side ports and a dark bore.
  Keep its final center on the muzzle helper and do not add an oversized can.
- Optic: add two clamp rings, a low bridge, rubber ocular bellows, a modest
  objective hood, and knurled turret caps. The scope should remain slimmer than
  the receiver and must never resemble two binocular tubes.

## Review acceptance

The art pass is ready for technical validation only when all of these are true:

- Rifle LOD0 is 20,000-30,000 triangles; LOD1 is 10,000-15,000; existing LOD2
  and LOD3 targets remain valid.
- Rifle uses no more than four material slots, optic remains the optional second
  renderer, and combined draw/renderer budgets remain within profile limits.
- The three neutral renders contain only the LOD0 rifle/optic and studio. In the
  side view the rifle occupies 72-92% of frame width and 18-48% of frame height.
- Side and three-quarter renders show at least three clearly different material
  responses without lifting the whole weapon to mid-grey.
- The side silhouette contains a readable stock triangle, stepped receiver,
  framed handguard bays, exposed barrel transition, and compact brake at 25%
  thumbnail scale.
- The scope-ocular image has an unobstructed center and a complete circular rim;
  no grey mount, turret, rail, receiver, helmet, or weapon slab crosses it.
- Cyan remains a status accent, never a continuous neon outline.
- All hardpoint coordinates, rigid signatures, exact 23-bone/24-action contract,
  articulated provenance, UV/topology gates, and face-clearance policy still
  pass unchanged.
