# Aegis Vanguard High-Fidelity Production Plan

## Objective

Create an original premium science-fantasy powered exosuit that reaches toward
Anthem's level of silhouette, layered construction, believable articulation, and
material richness without reproducing a specific Javelin design. Preserve the
current validated suit as a fully functional rollback throughout production.

The working design is **Aegis Vanguard**: an athletic assault/recon exosuit with
satin-black layered armor, blue-black carbon composite and braided carbon cable
jackets, polished and black-chrome mechanical details, a three-facet cyan optical
band, three-tier articulated pauldrons, and two annular dorsal turbines.

## Reference lessons, not copied shapes

BioWare described its design method as grounded realism followed by idealized
science fantasy. The suits retained recognizable human movement, familiar steel
and fabric-like materials, and class-readable silhouettes. Physical display suits
were built at human scale and remained mobile. A production artist described one
proto-Javelin as several months of modeling plus supporting engineering, shader,
and customization work.

Sources:

- https://www.wired.com/story/anthem-javelins-video-game-design/
- https://blog.bioware.com/2018/06/26/suit-up-building-the-javelins-of-anthem/
- https://hazed_blue.artstation.com/projects/dO414e

We are borrowing principles—physical plausibility, armor hierarchy, material
storytelling, and role readability—not Anthem's helmets, panel layouts, shoulder
profiles, thrusters, symbols, or exact proportions.

## Measured starting point

Generator114 is a reliable gameplay prototype, not a hero art asset:

- approximately 19.8k triangles for suit and rifle;
- 106 renderer-bearing mesh objects;
- rigid bone-parented pieces and no continuous skinned undersuit;
- almost no authored UV data and no image-based PBR texture sets;
- 12 simple constant-value materials;
- 23 bones and 24 exact animation actions.

The current Candidate004 automated maquette is approximately 73.1k mesh triangles
across 258 review objects. Its four authored cable curves are baked to mesh for
the formal handoff, yielding 254 renderer-bearing meshes. It preserves the same
23-bone rig and all 24 actions. That is acceptable for silhouette, material, and
pose iteration, but not the runtime architecture we will ship.

Candidate005 is the derived production-architecture prototype, not a visual
promotion over Candidate004 and not a Unity replacement. It consolidates the
visible suit to three skinned renderers/estimated draw calls at 88,316 LOD0
triangles, with one connected skinned undersuit and complete `UV0`. A dedicated
Blender audit reports zero selected overlap faces or loops. This proves a viable
data structure; it does not substitute for deliberate character art.

## Non-negotiable gameplay contracts

Preserve the current character height and gameplay footprint, all existing bone
names, all 24 action names/ranges, zero root motion, and these semantic controls or
anchors:

- `PowerSuit_Armature`, `Root`, all existing body bones;
- `WeaponRoot`, `WeaponMagazine`, `WeaponBolt`;
- `RifleRoot`, `Rifle_Muzzle`, `Rifle_PrimaryGrip`, `Rifle_SightOcular`;
- `Rifle_StockContact` and support-grip targets;
- `Thruster_Nozzle.L/R`, `Heavy_Boot.L/R`, and `Foot.L/R`.

The back must retain a diagonal rifle-docking corridor between/below the turbines.
The firing-side pauldron and backpack edge must not obscure the rifle or reticle in
the shoulder camera. Armor must clear aim, reload, bolt, draw, sheathe, sprint,
flight, landing, strafe, and backpedal poses.

## Target runtime construction

Author modularly, then consolidate for runtime:

1. upper suit skinned mesh;
2. lower suit skinned mesh;
3. visor/emissive mesh;
4. precision-rifle mesh;
5. optional optic-glass mesh.

Armor plates should be rigidly weighted to a single bone. Flexible neck, waist,
underarm, groin, elbow, hip, knee, and ankle regions should form a continuous
undersuit using two to four bone influences. Keep invisible named transforms for
all gameplay anchors after renderer consolidation.

### Geometry budgets

| Asset | LOD0 | LOD1 | LOD2 | LOD3 |
|---|---:|---:|---:|---:|
| Suit | 80–100k tris | 40–50k | 16–20k | 5–7k |
| Rifle | 20–30k tris | 10–15k | 4–6k | 1–2k |
| Combined hard cap | 130k | 65k | 26k | 9k |

Target no more than six ordinary suit/weapon draw calls, with eight as the hard
ceiling. Use at most four skin weights per vertex. Add twist/helper/finger bones
only after a candidate passes on the unchanged 23-bone rig.

## PBR and UV target

- Two conventional 0–1 4K source texture sets for the suit; one 2K set for the rifle.
- BaseColor, MikkTSpace Normal, packed Metallic/AO/Detail/Smoothness, and Emission.
- Optional shared tiled micro-normal for woven undersuit and coated armor grain.
- Roughly 10.24 px/cm baseline density; more for helmet, hands, visor, and scope.
- At least 16 pixels of 4K island padding.
- Unique visible armor UVs for asymmetric wear; mirror only low-attention undersuit.
- Physically distinct coated ceramic, machined metal, rubber weave, heat-stained
  exhaust, and visor glass—not color-swapped copies of one shader.

Weathering must follow use: light edge abrasion on knees, shoulders, knuckles and
boots; grease at pivots; soot and heat tint at exhausts; grime in recesses; large
clean regions retained. Emission is limited to optics, the sternum service channel,
small status lights, and active thruster cores.

## Production gates

### G0 — rollback freeze (complete)

- Generator114 archives, FBX, GUID, controller, prefab, and scene remain untouched.
- Candidate lane is isolated under `ArtSource/PoweredSuitNextGen`.
- Source hash is checked before and after every automated candidate build.

### G1 — art direction (complete for current review)

- Four-view concept and hero material target exist.
- Original silhouette and black/carbon/chrome palette targets are defined.
- Owner requested a darker, adult, gritty, subtly gothic refinement. The paired
  front/rear image targets and Candidate004 now encode that direction without
  adding symbols, spikes, robes, or franchise-specific shapes.

### G2 — gameplay blockout (current, measured fail)

- Candidate004 materially improves the helmet, torso hierarchy, layered shoulders,
  limbs, boots, turbines, material separation, weathering, and review lighting.
- Test exploration, shoulder aim, scope, stowed rifle, sprint, hover, and flight views.
- The gate sweeps 24 actions/162 authored keyframes. Candidate005's canonical
  three-visible-mesh result still fails at 3,894 forbidden instances/72
  object-pair groups, so clearance remains blocking. A separate hidden 254-part
  proxy reports 5,489/240 only for source-region diagnosis and is not canonical.
- Repair the forearm envelopes, pose-specific stowed WeaponRoot and draw/sheathe
  endpoints, central backpack channel, reload contacts, and shoulder-camera
  clearance before expensive detail or Unity integration.

### G3 — high-poly construction

- Rebuild primary forms with clean subdivision/boolean hard-surface topology.
- Sculpt the continuous undersuit and joint seals.
- Add controlled secondary overlaps, pivots, pistons, vents, fasteners, and access panels.
- Maintain primary/secondary/tertiary detail hierarchy; reject uniform greeble noise.

### G4 — game mesh and deformation (architecture scaffold passes)

- Retopologize to the LOD0 budgets.
- Rigid-weight armor and smoothly skin flexible regions.
- Validate normals, winding, manifold state, influence counts, and joint clearances.
- Preserve the exact animation and hardpoint contract.
- Candidate005 passes the isolated HeroV2 structural gate with 0 errors and 4
  texel-density warnings. It has three skinned renderers/draws and one connected
  undersuit. The deformation scaffold samples all 162 authored keyframes and
  records a 5.801599 maximum local edge-stretch ratio under the intentionally
  permissive 8x catastrophic-failure ceiling. That is automation-failure
  detection, not approval of seams, joint shapes, deformation, or weights.
- Manual anatomical/armor sculpting, production retopology and seam placement,
  joint cleanup, and weight polish remain required.

### G5 — UV, bake, and material finish

- Hand-author the final UV layout and material IDs; Candidate005's complete,
  zero-selected-overlap scaffold is structural evidence, not final seam work.
- Bake cage-based normal, AO, curvature, thickness, position, and ID support maps.
- Paint deliberate PBR materials and usage-based wear.
- Review in neutral studio, daylight, and dark interior lighting.

### G6 — LOD and runtime performance (draft generation operational)

- Create and hand-repair LOD1–LOD3.
- Consolidate to the renderer/material budgets.
- Verify LOD transitions in gameplay framing rather than only a turntable.
- Profile in the existing 32-enemy stress scene at target quality levels.
- Deterministic diagnostic LOD generation currently produces
  `88,316 -> 44,158 -> 17,660 -> 6,178` triangles for Candidate005. These are
  decimated diagnostics, not hand-repaired release LODs. Candidate005's preview
  textures likewise prove the PBR data path rather than final authored surfaces.

### G7 — parallel Unity candidate

- Export to a new FBX path and GUID; never overwrite the Generator114 FBX.
- Create a separate HeroV2 controller/material/prefab and art-review scene.
- Run full EditMode/PlayMode, development build, and smoke validation.
- Validate scope suppression, muzzle ray, rifle grips, reload/bolt, thrusters, and feet.

### G8 — owner A/B approval

- Compare old and new models under identical cameras, animations, and lighting.
- Owner approves visual quality, readability, and feel.

### G9 — promotion

- Point the canonical demo at the approved HeroV2 prefab.
- Keep Generator114 available as a one-step rollback.
- Freeze candidate evidence, hashes, renders, reports, FBX, and integration manifest.

## Automation boundary

Blender can automate parametric blockout, rigid weights, anchor preservation,
topology/UV/texel checks, first-pass LODs, pose sweeps, validation renders, hashes,
and gated export. The final silhouette, organic undersuit sculpt, production
retopology, seam placement, PBR storytelling, fine skinning, and hand-tuned LODs
still require deliberate art passes. That is the honest path from the current
prototype to a genuinely high-fidelity character.
