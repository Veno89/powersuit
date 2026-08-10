#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
BLENDER="${BLENDER_EXE:-blender}"

"$BLENDER" --background --factory-startup --python-exit-code 1 \
  --python-expr "import bpy; assert bpy.app.version[:2] >= (5, 2), 'Blender 5.2 or newer is required'" \
  >/dev/null

[[ -f powersuit_pipeline.blend ]] || { echo "Run 01_build_and_render.sh first."; exit 1; }
read -r -p "After inspecting all 33 PNGs, type APPROVE to export: " confirm
[[ "$confirm" == "APPROVE" ]] || { echo "Approval cancelled; no FBX exported."; exit 1; }

"$BLENDER" --background powersuit_pipeline.blend --python-exit-code 1 --python scripts/approve_validation.py -- --approve
"$BLENDER" --background powersuit_pipeline.blend --python-exit-code 1 --python scripts/export_powersuit_with_aim.py

echo "Export completed: exports/powersuit_animated_with_aim.fbx"
