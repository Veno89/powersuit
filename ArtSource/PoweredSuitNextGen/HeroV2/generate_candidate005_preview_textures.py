"""Generate deterministic, licence-free HeroV2 preview PBR detail maps.

These maps are a procedural material-development scaffold, not the final baked
character atlas.  Candidate005 uses them through authored UV0 to prove the PBR
data path without introducing a third-party texture dependency.
"""
from __future__ import annotations

import hashlib
import json
import math
import random
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "ArtSource" / "PoweredSuitNextGen" / "textures" / "candidate005"
RESOLUTION = 1024
SEED = 5005


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def height_at(x: int, y: int, noise: list[float]) -> float:
    weave_a = 0.5 + 0.5 * math.sin((x + y) * math.tau / 18.0)
    weave_b = 0.5 + 0.5 * math.sin((x - y) * math.tau / 18.0)
    crossing = max(weave_a, weave_b) * 0.55 + min(weave_a, weave_b) * 0.20
    scratch = 0.0
    if (x * 13 + y * 7) % 521 < 2:
        scratch = -0.22
    return max(0.0, min(1.0, 0.18 + crossing * 0.55 + noise[y * RESOLUTION + x] * 0.08 + scratch))


def generate() -> dict[str, object]:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    rng = random.Random(SEED)
    noise = [rng.random() for _ in range(RESOLUTION * RESOLUTION)]
    heights = [0.0] * (RESOLUTION * RESOLUTION)
    for y in range(RESOLUTION):
        for x in range(RESOLUTION):
            heights[y * RESOLUTION + x] = height_at(x, y, noise)

    base = bytearray(RESOLUTION * RESOLUTION * 4)
    normal = bytearray(RESOLUTION * RESOLUTION * 4)
    mrao = bytearray(RESOLUTION * RESOLUTION * 4)
    emission = bytearray(RESOLUTION * RESOLUTION * 4)
    for y in range(RESOLUTION):
        for x in range(RESOLUTION):
            index = y * RESOLUTION + x
            offset = index * 4
            height = heights[index]
            value = int(150 + height * 80)
            base[offset : offset + 4] = bytes((value - 9, value - 5, value, 255))

            left = heights[y * RESOLUTION + ((x - 1) % RESOLUTION)]
            right = heights[y * RESOLUTION + ((x + 1) % RESOLUTION)]
            down = heights[((y - 1) % RESOLUTION) * RESOLUTION + x]
            up = heights[((y + 1) % RESOLUTION) * RESOLUTION + x]
            nx = (left - right) * 1.45
            ny = (down - up) * 1.45
            nz = 1.0
            length = math.sqrt(nx * nx + ny * ny + nz * nz)
            normal[offset : offset + 4] = bytes((
                int((nx / length * 0.5 + 0.5) * 255),
                int((ny / length * 0.5 + 0.5) * 255),
                int((nz / length * 0.5 + 0.5) * 255),
                255,
            ))
            # Metallic remains semantic/vertex-authored, so the texture's R is
            # a neutral multiplier. G is AO, B is detail, and A is smoothness.
            mrao[offset : offset + 4] = bytes((255, 242, int(height * 255), 92))
            # Emission geometry already defines where light may appear; this
            # white map explicitly exercises the UV-driven mask path.
            emission[offset : offset + 4] = bytes((255, 255, 255, 255))

    outputs = {
        "base_color": ("AV_H2_Detail_BaseColor.png", base),
        "normal": ("AV_H2_Detail_Normal.png", normal),
        "mrao": ("AV_H2_Detail_MRAO.png", mrao),
        "emission": ("AV_H2_Detail_Emission.png", emission),
    }
    manifest: dict[str, object] = {
        "schema_version": 1,
        "status": "PROCEDURAL_PREVIEW_NOT_FINAL_CHARACTER_BAKE",
        "resolution": [RESOLUTION, RESOLUTION],
        "seed": SEED,
        "maps": {},
        "channel_contract": {
            "MRAO.R": "metallic scaffold",
            "MRAO.G": "ambient-occlusion scaffold",
            "MRAO.B": "woven detail mask",
            "MRAO.A": "smoothness scaffold",
        },
    }
    for role, (filename, pixels) in outputs.items():
        path = OUTPUT / filename
        Image.frombytes("RGBA", (RESOLUTION, RESOLUTION), bytes(pixels)).save(
            path, optimize=True
        )
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
