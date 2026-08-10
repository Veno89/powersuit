# Generator 112 Validation Archive

This directory freezes the powered-sprint animation candidate technically
approved and exported on 2026-08-10:

- automated validation: `PASS`
- technical visual validation: `APPROVED`
- aim renders: 13/13
- rifle renders: 5/5
- weapon-animation renders: 15/15
- generated blend SHA-256: `0295acb528b0ca8c0f3ec68f642ad6c56c3f4ddaf9fe2f5a0ab84adae9311876`
- validation report SHA-256: `a7f3a487f3f549bbc8b3ac55c6a43f90a4ce67a5e42f9e6720a806c600442546`
- visual approval SHA-256: `1f7a4554c4a4290a803769f5492f526a76b1840fa45992013430909871e9fd37`
- export manifest SHA-256: `417c69c0ba33e69f19e9363e64992fd0a4747fc6fb7ef6380ba3aeda852474e4`
- exported and Unity-imported FBX SHA-256: `054b5a1875730b225cbb9192bbf760a75919126043ce3ae1503308d21fa8e409`

Generator 112 preserves Generator 111's weapon geometry, rig, and original 17
action ranges while advancing the animation contract to version 3 and adding
the synchronized `PS_Run_Forward` action. The new loop spans frames 1–21 at
30 FPS (20-frame cycle, 180 native steps/minute) and retains the one-armature-
slot export contract.

The run validation records a 0.6853 m stride versus the 0.5405 m walk stride,
0.0343 m airborne clearance, 0.1081 m forward torso projection, and sub-0.1 mm
two-hand grip errors. Unity presents the powered sprint at 1.35x cadence while
gameplay remains controller-driven and in-place.

`renders/visual_approval.json` hashes every mandatory image and the approved
report. These files are immutable technical review evidence. Hands-on Unity
feel and foot-slide acceptance remain tracked separately in the repository
roadmap.
