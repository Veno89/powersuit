# Generator 113 Validation Archive

This directory freezes the powered-locomotion stride candidate technically
approved and exported on 2026-08-10:

- automated validation: `PASS`
- technical visual validation: `APPROVED`
- aim renders: 13/13
- rifle renders: 5/5
- weapon-animation renders: 15/15
- validation backend: deterministic Cycles CPU, 8 samples
- generated blend SHA-256: `a5054d65af2cb6a04836216456a1a3162f8d860c6c421533a7ac08a9f70d2d4b`
- validation report SHA-256: `a6d8c7bd98659f2cee33c906ca845d6e01e2d060dbc906b31de647ab664bf4ee`
- visual approval SHA-256: `7d6cc01a5ecce4067d1ae4409cb3dd23df68ed48da2ec3fa5acf279a8ee66d9b`
- export manifest SHA-256: `0a14ed949eba508857de4a004c317769eb1d6449f050318fd3c82b493815813d`
- exported and Unity-imported FBX SHA-256: `fe18bc8f3e93b2d5ba9e8c9edbd4e8910ad1e27197f806e0b24b95b36136f3dd`

Generator 113 preserves Generator 112's weapon geometry, rig, 18 action names,
and action ranges while advancing the animation contract to version 4. The six
stance-aware walking variants now extrapolate the audited lower-body motion to
a powered 0.8379 m stride. `PS_Run_Forward` reaches a 0.9341 m stride and 0.0365 m
airborne clearance while retaining its 20-frame, 180-steps/minute native loop,
forward torso commitment, and sub-0.1 mm two-hand grip errors.

Unity presents ordinary locomotion at a reduced 2.75x full-speed playback, and
procedural blue-white exhaust communicates the deliberately assisted gap between
leg travel and the unchanged 6.5 m/s / 10.725 m/s controller speeds. Hands-on
foot-slide, cadence, and propulsion acceptance remain tracked in the repository
roadmap.

The original headless Workbench path repeatedly failed inside the NVIDIA OpenGL
driver while Unity was open. The same structural and image-content gates were
completed through the explicit `cycles_cpu` fallback. Crash-resume reused images
only when requested and only after the canonical content validator accepted each
existing PNG.
