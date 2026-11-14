"""
Beach pebble skin for the Rocky Shark (Risk of Copyright, Godot): rounded stones packed together, tileable.
Run:  python Tools/blender/pebble_texture.py   (numpy only, no Blender)
Writes godot/assets/materials/creature/pebbles_{color,normal,rough}.png for Shapes.Material( Colors.White, "pebbles" ).

Colours follow the reference photo of a pebble beach: about 30% of the stones are the shark's own sandy tan, the
other 70% greys, slate blues, near-black blue, rust orange, cream, browns and olive. Every stone gets its own
character so no two read the same: granite speckles, a white quartz vein or two, mottled patches, or plain and glossy.
"""
import os
import struct
import zlib

import numpy as np

# The repo's own folder, wherever it is checked out.
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "godot", "assets", "materials", "creature")
N = 1024
SEED = 29

TAN = [(0.62, 0.48, 0.32), (0.68, 0.54, 0.37), (0.56, 0.42, 0.28), (0.72, 0.60, 0.44)]
OTHERS = [
    (0.55, 0.55, 0.53), (0.70, 0.69, 0.66), (0.62, 0.63, 0.64),   # greys
    (0.36, 0.46, 0.54), (0.46, 0.56, 0.64),                       # slate blues
    (0.16, 0.20, 0.25), (0.24, 0.27, 0.30),                       # near-black blue
    (0.78, 0.45, 0.18), (0.66, 0.36, 0.16),                       # rust orange
    (0.86, 0.81, 0.69), (0.80, 0.74, 0.60),                       # cream
    (0.42, 0.26, 0.18), (0.52, 0.34, 0.24),                       # browns
    (0.54, 0.52, 0.34),                                           # olive
    (0.22, 0.35, 0.36),                                           # dark teal
]


def write_png(path, rgb):
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].astype(np.uint8).tobytes() for y in range(h))

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def value_noise(rng, cells, n=N):
    """Smooth tileable noise in 0..1."""
    g = rng.random((cells, cells))
    x = np.arange(n) / n * cells
    i0 = np.floor(x).astype(int) % cells
    i1 = (i0 + 1) % cells
    t = x - np.floor(x)
    t = t * t * (3 - 2 * t)
    rows = g[i0][:, None, :] * (1 - t)[:, None, None] + g[i1][:, None, :] * t[:, None, None]
    rows = rows[:, 0, :]
    out = rows[:, i0] * (1 - t)[None, :] + rows[:, i1] * t[None, :]
    return out


def build():
    rng = np.random.default_rng(SEED)
    # Stones: a jittered grid on a torus, with uneven sizes from a per-stone weight (power diagram).
    k = 8   # stones across a tile: fewer and bigger than the first version's 13 (playtest: they didn't stand out)
    pts, wts = [], []
    for gy in range(k):
        for gx in range(k):
            pts.append(((gx + rng.uniform(0.15, 0.85)) / k, (gy + rng.uniform(0.15, 0.85)) / k))
            wts.append(rng.uniform(-0.25, 0.35) / k ** 2)
    pts = np.array(pts, np.float32)
    wts = np.array(wts, np.float32)
    m = len(pts)

    ys, xs = np.mgrid[0:N, 0:N].astype(np.float32) / N
    # Each stone is an oval of its own size and angle; a pixel belongs to the stone it is deepest inside (smallest
    # normalised radius), and pixels inside none are the sand between them.
    best = np.full((N, N), 9.0, np.float32)
    second = np.full((N, N), 9.0, np.float32)
    owner = np.zeros((N, N), np.int32)
    spacing = 1.0 / k
    for i in range(m):
        dx = xs - pts[i, 0]; dx = dx - np.round(dx)
        dy = ys - pts[i, 1]; dy = dy - np.round(dy)
        ang = rng.uniform(0, np.pi)
        ra = spacing * rng.uniform(0.68, 0.92)
        rb = ra * rng.uniform(0.66, 0.95)
        sx = (dx * np.cos(ang) + dy * np.sin(ang)) / ra
        sy = (-dx * np.sin(ang) + dy * np.cos(ang)) / rb
        r = np.sqrt(sx * sx + sy * sy)
        closer = r < best
        second = np.where(closer, best, np.minimum(second, r))
        owner = np.where(closer, i, owner)
        best = np.where(closer, r, best)
    stone_r = 0.5 / k
    inside = np.clip(1 - best, 0, 1)
    height = np.sqrt(np.clip(1 - best * best, 0, 1))                         # rounded dome
    # Where two stones press together, a thin dark seam and both edges rounded down into it.
    seam = np.clip((second - best) / 0.12, 0, 1)
    height = height * np.sqrt(seam)
    gap = (best > 0.97) | (seam < 0.12)

    # Each stone: a colour and a character.
    colours = np.zeros((m, 3), np.float32)
    kind = rng.choice(4, m, p=[0.35, 0.15, 0.30, 0.20])     # 0 speckled, 1 veined, 2 mottled, 3 plain glossy
    for i in range(m):
        base = np.array(TAN[rng.integers(len(TAN))] if rng.random() < 0.30 else OTHERS[rng.integers(len(OTHERS))], np.float32)
        colours[i] = np.clip(base * rng.uniform(0.88, 1.12), 0, 1)
    col = colours[owner]

    fine = rng.random((N, N)).astype(np.float32)
    blotch = value_noise(rng, 24).astype(np.float32)
    big = value_noise(rng, 8).astype(np.float32)
    k_own = kind[owner]

    # Speckled granite: small light and dark flecks.
    speck = (k_own == 0)
    dark = speck & (fine < 0.10)
    light = speck & (fine > 0.93)
    col = np.where(dark[..., None], col * 0.45, col)
    col = np.where(light[..., None], np.clip(col * 0.5 + 0.55, 0, 1), col)
    # Veined: one or two white quartz bands across the stone.
    vein_a = rng.uniform(0, np.pi, m).astype(np.float32)
    vein_o = rng.uniform(-0.5, 0.5, m).astype(np.float32) * stone_r
    px = xs - pts[owner, 0]; px = px - np.round(px)
    py = ys - pts[owner, 1]; py = py - np.round(py)
    along = px * np.cos(vein_a[owner]) + py * np.sin(vein_a[owner]) - vein_o[owner]
    wobble = (blotch - 0.5) * stone_r * 0.25
    vein = (k_own == 1) & (np.abs(along + wobble) < stone_r * 0.05)
    vein2 = (k_own == 1) & (rng.random(m)[owner] < 0.3) & (np.abs(along + wobble - stone_r * 0.35) < stone_r * 0.03)
    col = np.where((vein | vein2)[..., None], np.array([0.93, 0.92, 0.88], np.float32), col)
    # Mottled: soft lighter and darker patches.
    mott = (k_own == 2)[..., None]
    col = np.where(mott, col * (0.78 + 0.44 * blotch[..., None]), col)
    # Every stone: a faint grain and a gentle large-scale shade so the tiling hides.
    col = col * (0.94 + 0.08 * fine[..., None]) * (0.92 + 0.12 * big[..., None])
    # Shade toward the rim of each stone, and the dark sandy gaps between them.
    # Round shading: darker toward each stone's rim, a soft light from the upper left.
    light = np.clip(0.5 - (xs - pts[owner, 0] - np.round(xs - pts[owner, 0])) * 3 - (ys - pts[owner, 1] - np.round(ys - pts[owner, 1])) * 3, 0, 1)
    col = col * (0.45 + 0.55 * height[..., None] ** 0.7) * (0.92 + 0.16 * light[..., None])
    sand = np.array([0.20, 0.17, 0.13], np.float32) * (0.7 + 0.6 * fine[..., None])
    col = np.where(gap[..., None], sand, col)

    # Normal map from the domes (tileable gradients).
    h = height * 1.0 + (k_own == 0) * fine * 0.04
    # Steep: the stones should read as standing proud of the hide (was N / 64).
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * N / 36
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * N / 36
    nz = np.ones_like(h)
    norm = np.sqrt(gx * gx + gy * gy + nz * nz)
    normal = np.stack([-gx / norm, gy / norm, nz / norm], -1) * 0.5 + 0.5

    # Roughness: glossy plain stones, matte speckled ones, rough gaps.
    rough = np.where(k_own == 3, 0.32, np.where(k_own == 0, 0.8, 0.6)).astype(np.float32)
    rough = np.where(gap, 1.0, rough)

    os.makedirs(OUT, exist_ok=True)
    write_png(os.path.join(OUT, "pebbles_color.png"), np.clip(col * 255, 0, 255))
    write_png(os.path.join(OUT, "pebbles_normal.png"), np.clip(normal * 255, 0, 255))
    write_png(os.path.join(OUT, "pebbles_rough.png"), np.clip(np.repeat(rough[..., None], 3, -1) * 255, 0, 255))
    print("[pebbles] wrote", OUT, m, "stones,", int((colours.sum(1) > 0).sum()), "coloured")


if __name__ == "__main__":
    build()
