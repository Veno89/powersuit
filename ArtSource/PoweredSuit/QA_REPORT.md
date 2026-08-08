# Generator 109 Approved QA Report

Date: 2026-08-08
Environment: Blender 5.2.0 LTS on Windows
Formal asset state: automated `PASS`, visual `APPROVED`, export allowed

## Current packaged candidate

- immutable source blend SHA-256:
  `49c2a9a09c71989a72e6b81c97045e609d825c2bf41e21a62f216adc277402f4`
- generated `powersuit_pipeline.blend` SHA-256:
  `5b41ec1bd51f69a8e91c4052d6e99e7bed2d3d2f4afcf38a56e37609c8c11e99`
- approved `renders/validation_report.json` SHA-256:
  `77cb69bd887801393f49da08993a4cf582bbabce627033a8c5cf2a52778e5d9e`
- report blend hash matches the generated blend: yes
- aim renders: 13/13
- rifle renders: 5/5
- automated blockers: 0
- `visual_approval.json` SHA-256:
  `0c4e7c0ee935d19d0363da22ce31f1a585e216c3007fbeb556cce995fe3b4f7a`
- exported FBX SHA-256:
  `b06492383c47750fbb7335dadf56794856b09bca2425c442e81e867e9a689b69`
- Unity FBX hash matches the approved export: yes

## Runtime result

- rifle generator: 109
- weapon contract: v2
- rigid signature: v5
- rig upgrade: v2
- hand geometry: v2 on both hands
- legacy Actions retained: `PS_Idle`, `PS_Walk`, `PS_Hover`
- rebuilt Action: `PS_Aim`
- all four Actions use one Blender 5.2 armature Action Slot
- only `RifleRoot` is attached directly to `Hand.R`
- rifle direct children: 60
- active temporary IK/validation objects: none
- root motion: 0 m

Selected final aim metrics:

- sight lateral / vertical: `0.011011 / 0.011282 m`
- sight front clearance: `0.088841 m`
- visor/rifle sight-axis angle: `1.7457°`
- firing-side receptor-to-ocular ray angle: `9.9458°`
- head yaw / pitch / roll: `0.5° / 5.5° / -6.0°`
- trigger/support wrist target error: below `0.00005 m`
- trigger/support hand-grip overlap pairs: `18 / 6`
- stock/shoulder overlap pairs: `22`
- non-stock weapon/torso overlap pairs: `0`

## Tests performed

1. Audited Reset06 scripts, both historical `.blend` files, all historical
   renders/reports, older `PowerSuitAsset` files, and the Unity workspace.
2. Python-compiled all 11 active scripts.
3. Ran repeated clean builds from `source/powersuit_source.blend` through all
   six Blender stages, including the final exact-code repeat.
4. Validated Action Slots, curve/key counts, preserved legacy Action data,
   hierarchy, positive transforms, finite values, rigid signature, semantic
   hardpoints, reach, sight, contact, collision, framing, and complete renders.
5. Inspected all 18 required images at full resolution. The current stylised
   candidate reads as a coherent shouldered aim pose and none of the listed
   gross rejection conditions was observed. The user then explicitly approved
   promotion, and the approval tool recorded the exact 18 render hashes.
6. Repeated the final clean build while retaining the prior output temporarily:
   semantic reports were equal and 18/18 decoded PNG pixel buffers were exactly
   equal (`max channel RMS = 0`).
7. Confirmed Blender container/encoding bytes are not reproducible across clean
   runs: 0/18 compressed PNG hashes matched even though decoded pixels were
   identical, and `.blend` hashes differed. The report hash correspondingly
   changes because it records the generated blend hash. Use the hashes above
   for this packaged candidate, not as universal generator-output hashes.
8. Confirmed canonical export refused the candidate before approval, then
   exported exactly the approved four-action asset after approval.
9. In an isolated temporary copy, confirmed approval covers exactly 18 render
   hashes, a changed approved render blocks export, and `--reject` works even
   for an automated `REVIEW_BLOCKED` report. The temporary approval was removed;
   the canonical candidate remained unchanged until explicit user approval.
10. Verified active Windows launchers use CRLF, shell launchers pass Git Bash
    syntax checking, and both platforms preflight Blender 5.2 before the build
    launcher resets generated work.

## Visual and integration boundary

The Generator 109 result is approved for Unity evaluation. It is imported
alongside the legacy model so rollback remains straightforward; the legacy FBX
and `.meta` were not overwritten. The immutable reviewed report, approval
record, and 18 images live under `Validation/Generator109`.

Unity `6000.5.7f1` imported the approved FBX with four clips and no imported
cameras or lights. The additive prefab/demo integration passed 5/5 EditMode
tests and 2/2 PlayMode tests, produced a Windows 64-bit Development Player, and
completed a 10-second headless player smoke without missing references or
runtime exceptions.

Blender emitted two non-blocking deprecation warnings for `Material.use_nodes`,
which remains valid in Blender 5.2 but is expected to change in Blender 6.0.
