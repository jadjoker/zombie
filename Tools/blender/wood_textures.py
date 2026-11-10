"""
Wood textures for the dock (Risk of Copyright, Godot): clean, lightly cel-shaded boards. Straight grain in a few
flat tones with soft steps between them and the odd darker line, tileable, no knots or noise.
Run with plain Python (needs numpy):  python Tools/blender/wood_textures.py
Writes godot/assets/textures/dock/<name>.png (what dock_house.py puts on its materials) and the copies Godot
extracted next to the model, godot/assets/models/dock/dock_house_<name>.png (what the game shows today).
"""
import os
import struct
import zlib

import numpy as np

ROOT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets"
SIZE = 512


def write_png(path, rgb):
    """rgb: (h, w, 3) floats 0..1 (sRGB)."""
    data = (np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8)
    h, w, _ = data.shape
    raw = b"".join(b"\x00" + data[y].tobytes() for y in range(h))

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def srgb(c):
    """Linear colour (as dock_house.py writes them) to sRGB bytes space."""
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def wood(base, dark, seed, lines=9, along_v=True):
    """Grain running along one axis: broad soft streaks stepped into three tones, plus a few thin dark lines."""
    rng = np.random.default_rng(seed)
    u, v = np.meshgrid(np.arange(SIZE) / SIZE, np.arange(SIZE) / SIZE)
    if not along_v:
        u, v = v, u
    tau = 2 * np.pi
    # Streaks: waves across the board (whole numbers of cycles so it tiles), each drifting a little along it.
    t = np.zeros_like(u)
    for _ in range(7):
        f = int(rng.integers(2, 14))
        drift = 0.012 * np.sin(tau * (int(rng.integers(1, 3)) * v + rng.random()))
        t += rng.uniform(0.4, 1.0) / np.sqrt(f) * np.sin(tau * (f * (u + drift) + rng.random()))
    t = (t - t.min()) / (t.max() - t.min())
    # Three flat tones with soft edges between them (the cel look), not a smooth gradient.
    steps = 3
    scaled = t * steps
    band = np.floor(scaled)
    edge = np.clip((scaled - band - 0.5) / 0.16 + 0.5, 0, 1)
    tone = np.clip((band + edge) / steps, 0, 1)
    # A few thin darker grain lines.
    line = np.zeros_like(u)
    for _ in range(lines):
        pos = rng.random()
        wobble = 0.006 * np.sin(tau * (int(rng.integers(1, 4)) * v + rng.random()))
        d = np.abs(((u + wobble - pos + 0.5) % 1.0) - 0.5)
        line = np.maximum(line, np.clip(1 - d / 0.004, 0, 1) * rng.uniform(0.35, 0.8))
    b, k = srgb(base), srgb(dark)
    mid = k + (b - k) * 0.55
    col = mid[None, None, :] + (b - mid)[None, None, :] * tone[..., None]
    col = col + (k - col) * (line * 0.75)[..., None]
    return col


def shingles(base, dark, seed):
    """Painted roof boards: flat colour in three tones, running the other way, a faint line between courses."""
    return wood(base, dark, seed, lines=5, along_v=False)


TEXTURES = {
    "wood_pale": lambda: wood((0.62, 0.47, 0.31), (0.42, 0.3, 0.18), 1),
    "wood": lambda: wood((0.5, 0.34, 0.2), (0.32, 0.21, 0.12), 2),
    "wood_dark": lambda: wood((0.33, 0.22, 0.13), (0.2, 0.13, 0.08), 3),
    "roof": lambda: shingles((0.6, 0.2, 0.16), (0.38, 0.12, 0.1), 4),
}


def build():
    for name, make in TEXTURES.items():
        image = make()
        write_png(f"{ROOT}/textures/dock/{name}.png", image)
        write_png(f"{ROOT}/models/dock/dock_house_{name}.png", image)
        print("wrote", name)


if __name__ == "__main__":
    build()
