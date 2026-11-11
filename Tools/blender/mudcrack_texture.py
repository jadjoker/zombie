"""
Dry mud crack skin for the Kraken (Risk of Copyright, Godot): pale plates split by black cracks, tileable.
Run:  python Tools/blender/mudcrack_texture.py   (numpy only, no Blender)
Writes godot/assets/materials/creature/mudcrack_{color,normal,rough}.png. The colour is near white so the game's
tint sets the skin colour and only the cracks stay black (Shapes.Material( tint, "mudcrack" )).
"""
import os
import struct
import zlib

import numpy as np

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/materials/creature"
N = 512
CELLS = 46          # plates per tile
CRACK = 0.011       # half width of a crack, in tile lengths


def write_png(path, rgb):
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].astype(np.uint8).tobytes() for y in range(h))

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def build():
    rng = np.random.default_rng(30)
    # Plate centres on a jittered grid (even plate sizes, like real mud), repeated round the tile so it wraps.
    side = int(np.ceil(np.sqrt(CELLS)))
    gx, gy = np.meshgrid(np.arange(side), np.arange(side))
    pts = (np.stack([gx.ravel(), gy.ravel()], 1) + 0.5 + rng.uniform(-0.38, 0.38, (side * side, 2))) / side
    shade = rng.uniform(0.86, 1.0, len(pts))
    tiled = np.concatenate([pts + (dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
    tshade = np.tile(shade, 9)

    y, x = np.mgrid[0:N, 0:N]
    p = np.stack([(x + 0.5) / N, (y + 0.5) / N], -1).reshape(-1, 2)
    d1 = np.full(len(p), 9.0)
    d2 = np.full(len(p), 9.0)
    nearest = np.zeros(len(p), dtype=int)
    for i, q in enumerate(tiled):
        d = np.hypot(p[:, 0] - q[0], p[:, 1] - q[1])
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        nearest = np.where(closer, i, nearest)
        d1 = np.where(closer, d, d1)
    edge = ((d2 - d1) * 0.5).reshape(N, N)           # distance to the nearest plate boundary
    plate = tshade[nearest].reshape(N, N)

    crack = np.clip(edge / CRACK, 0, 1)                # 0 in the crack, 1 on the plate
    crack = crack * crack * (3 - 2 * crack)
    lip = np.clip(edge / (CRACK * 5), 0, 1)            # plates curl darker toward their edges
    value = plate * (0.72 + 0.28 * lip) * crack
    color = np.repeat((value[..., None] * 255).clip(0, 255), 3, 2)
    write_png(os.path.join(OUT, "mudcrack_color.png"), color)

    # Height: plates slightly domed, cracks deep. Normal from its slope.
    height = crack * (0.6 + 0.4 * lip)
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 2.2
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 2.2
    n = np.stack([-dx, -dy, np.ones_like(dx)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    write_png(os.path.join(OUT, "mudcrack_normal.png"), ((n * 0.5 + 0.5) * 255).clip(0, 255))

    rough = np.repeat(((0.82 + 0.16 * (1 - crack))[..., None] * 255).clip(0, 255), 3, 2)
    write_png(os.path.join(OUT, "mudcrack_rough.png"), rough)
    print("wrote mudcrack color, normal, rough to", os.path.normpath(OUT))


if __name__ == "__main__":
    build()
