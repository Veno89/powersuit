# Generator 110 Validation Archive

This directory freezes the exact evidence explicitly approved on 2026-08-08:

- automated validation: `PASS`
- visual validation: `APPROVED`
- aim renders: 13/13
- rifle renders: 5/5
- approved generated blend SHA-256: `7ac6d24af0fc7f0ad658d594ef37907f53a9e9145bc94c30ec792ea06c073548`
- approved report SHA-256: `dd291ac4ef3aaa99e2ee803c03116348eb6ef3012361abce42713505bbdf9cfd`
- visual approval SHA-256: `ed6c5f84e435ff08daa7a9cdc0111c6e5def34b6a731e267937c8b7cb2c3d022`
- exported and Unity-imported FBX SHA-256: `b9d700cd06ee8fa1003ae08048de8e98d603c2ef30aa119e92437f00f22e419c`

Generator 110 corrects the inward primitive face winding that Unity exposed as
open geometry under back-face culling. Its export stage also rejects any mesh
whose signed volume is not positive.

`renders/visual_approval.json` contains the SHA-256 of each mandatory image.
These files are immutable review evidence. Rebuilding the active pipeline may
replace the ignored working blend, renders, and export, but must not rewrite
this archive.
