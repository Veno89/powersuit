"""Generate deterministic, licence-free Candidate006 rifle preview PBR maps.

The maps exercise the complete 2K BaseColor/Normal/MRAO/Emission handoff and
give the isolated review blend a coherent dark industrial finish.  They remain
procedural preview maps, not a substitute for a final authored bake.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "ArtSource" / "PoweredSuitNextGen" / "textures" / "candidate006"
RESOLUTION = 2048
SEED = 6006


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def height_at(x: int, y: int) -> float:
    """Return a tileable forged-carbon / machining-detail height field."""

    weave_a = math.sin((x + y) * math.tau / 31.0)
    weave_b = math.sin((x - y) * math.tau / 43.0)
    machining = math.sin(y * math.tau / 83.0) * 0.08
    deterministic_grain = (((x * 92821) ^ (y * 68917) ^ SEED) & 255) / 255.0
    scratch = -0.28 if (x * 17 + y * 29 + SEED) % 1871 < 2 else 0.0
    value = 0.48 + weave_a * 0.16 + weave_b * 0.13 + machining
    value += (deterministic_grain - 0.5) * 0.10 + scratch
    return max(0.0, min(1.0, value))


def generate() -> dict[str, object]:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    width = RESOLUTION
    height = RESOLUTION
    base = bytearray(width * height * 4)
    normal = bytearray(width * height * 4)
    mrao = bytearray(width * height * 4)
    emission = bytearray(width * height * 4)

    # Three scanlines are sufficient for a central-difference normal and keep
    # generation memory bounded even at the required 2K resolution.
    previous = [height_at(x, height - 1) for x in range(width)]
    current = [height_at(x, 0) for x in range(width)]
    following = [height_at(x, 1) for x in range(width)]
    for y in range(height):
        if y:
            previous, current = current, following
            next_y = (y + 1) % height
            following = [height_at(x, next_y) for x in range(width)]
        for x in range(width):
            index = y * width + x
            offset = index * 4
            value = current[x]
            # Blue-black carbon with warm rubbed-edge flecks. Material/vertex
            # colour remains the dominant semantic colour in Blender.
            soot = int(32 + value * 35)
            warm = int(max(0.0, value - 0.76) * 42)
            base[offset : offset + 4] = bytes((soot + warm, soot + 2, soot + 7, 255))

            left = current[(x - 1) % width]
            right = current[(x + 1) % width]
            nx = (left - right) * 1.7
            ny = (previous[x] - following[x]) * 1.7
            nz = 1.0
            length = math.sqrt(nx * nx + ny * ny + nz * nz)
            normal[offset : offset + 4] = bytes((
                int((nx / length * 0.5 + 0.5) * 255),
                int((ny / length * 0.5 + 0.5) * 255),
                int((nz / length * 0.5 + 0.5) * 255),
                255,
            ))
            ao = int(220 + value * 28)
            smoothness = int(68 + value * 46)
            mrao[offset : offset + 4] = bytes((255, ao, int(value * 255), smoothness))

            # Sparse cyan service/status traces. The emission renderer further
            # confines this map, preventing broad toy-like glowing panels.
            trace = 255 if ((x // 96 + y // 32) % 19 == 0 and y % 64 < 4) else 0
            emission[offset : offset + 4] = bytes((0, trace, trace, 255))

    outputs = {
        "base_color": ("NGPR001_BaseColor.png", base),
        "normal": ("NGPR001_Normal.png", normal),
        "mrao": ("NGPR001_MRAO.png", mrao),
        "emission": ("NGPR001_Emission.png", emission),
    }
    manifest: dict[str, object] = {
        "schema_version": 1,
        "asset_id": "PS_NextGenPrecisionRifle001",
        "status": "PROCEDURAL_2K_PREVIEW_NOT_FINAL_WEAPON_BAKE",
        "resolution": [RESOLUTION, RESOLUTION],
        "seed": SEED,
        "maps": {},
        "channel_contract": {
            "MRAO.R": "metallic multiplier",
            "MRAO.G": "ambient occlusion",
            "MRAO.B": "forged-carbon detail mask",
            "MRAO.A": "smoothness",
        },
    }
    for role, (filename, pixels) in outputs.items():
        path = OUTPUT / filename
        Image.frombytes("RGBA", (width, height), bytes(pixels)).save(path, optimize=True)
        manifest["maps"][role] = {
            "path": path.relative_to(ROOT).as_posix(),
            "sha256": sha256(path),
            "mode": "RGBA",
        }
    manifest_path = OUTPUT / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


if __name__ == "__main__":
    print(json.dumps(generate(), indent=2))
