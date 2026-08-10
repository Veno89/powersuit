# Generator 111 Validation Archive

This directory freezes the exact candidate technically approved and exported on 2026-08-09:

- automated validation: `PASS`
- technical visual validation: `APPROVED`
- aim renders: 13/13
- rifle renders: 5/5
- weapon-animation renders: 14/14
- generated blend SHA-256: `7cf96287bcb9b0b67c2feb9dcc6e416e023da20f97363c598d8d31ec8cd2851f`
- validation report SHA-256: `489fab54cf950abac2e8ce9b0beb401f8a6bfbdd3fcadeddaa76d38e57de5a20`
- visual approval SHA-256: `c001108f4a149cd7a4328faa5c61569e8001b2d7d5a272871f56308b40949a21`
- export manifest SHA-256: `5617055f969c78e9ecf15a8e656a10f4bc4e3fd542ef569298a761ad87e7453c`
- exported and Unity-imported FBX SHA-256: `1c3fb62a3d978de6d5205af5c2f04ebf143bbcd5c10bee3f26ff4e4b4ad3d814`

Generator 111 retains Generator 110's outward face winding and positive-volume gate, upgrades the weapon contract to v3/signature v6, and adds synchronized `WeaponRoot`, `WeaponMagazine`, and `WeaponBolt` armature controls. The manifest records exactly 17 clips and the reload/bolt gameplay timing markers.

`renders/visual_approval.json` hashes every mandatory image and the approved report. These files are immutable technical review evidence. Rebuilding the ignored working blend, renders, and export must not rewrite this archive.

Hands-on Unity play acceptance remains tracked separately in the repository-root `ROADMAP.md`.
