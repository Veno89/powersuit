#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
BLENDER="${BLENDER_EXE:-blender}"

cp source/powersuit_source.blend powersuit_pipeline.blend
rm -rf renders

"$BLENDER" --background powersuit_pipeline.blend --python-exit-code 1 --python scripts/run_build_and_render_pipeline.py

echo "Inspect all PNGs in renders/aim_validation and renders/rifle_validation."
echo "Do not approve/export until the images are genuinely acceptable."
