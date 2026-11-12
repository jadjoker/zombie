"""
Turns the CC0 recordings the game uses (issue #36) into game sounds. Run with a folder holding the raw downloads:
    python Tools/godot/import_sounds.py <folder>
Needs numpy. Writes godot/assets/sounds/{rifle_shoot,shotgun_shoot}.wav (trimmed to one shot, mono, 44.1 kHz) and
copies the Kenney .ogg files under the names the game looks for (footstep_dock_N, footstep_floor_N).

Sources (all CC0, no credit needed; see assets/sounds/README.md):
  AR-15 and Benelli Nova shots: Free Firearm Sound Library (Ben Jaszczak), D_32P.wav and O_21P.wav.
  Footsteps: Kenney Impact Sounds (footstep_wood, footstep_snow).
"""
import os
import shutil
import sys
import wave

import numpy as np

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "godot", "assets", "sounds")
RATE = 44100


def read24(path):
    w = wave.open(path)
    sr, ch, n = w.getframerate(), w.getnchannels(), w.getnframes()
    a = np.frombuffer(w.readframes(n), dtype=np.uint8).reshape(-1, 3)
    x = (a[:, 0].astype(np.int32) | (a[:, 1].astype(np.int32) << 8) | (a[:, 2].astype(np.int8).astype(np.int32) << 16)) / 8388608.0
    return x.reshape(-1, ch).mean(1), sr


def resample(x, sr):
    # Lowpass below the new Nyquist in the frequency domain, then interpolate.
    spectrum = np.fft.rfft(x)
    freq = np.fft.rfftfreq(len(x), 1 / sr)
    spectrum[freq > RATE * 0.45] = 0
    x = np.fft.irfft(spectrum, len(x))
    t = np.arange(int(len(x) * RATE / sr)) * sr / RATE
    return np.interp(t, np.arange(len(x)), x)


def first_shot(x, sr, tail, lead=0.01):
    """The first shot in a recording: from just before its crack to `tail` seconds after, faded out."""
    win = int(sr * 0.005)
    env = np.convolve(np.abs(x), np.ones(win) / win, "same")
    start = int(np.argmax(env > env.max() * 0.2))
    cut = x[max(0, start - int(lead * sr)):start + int(tail * sr)]
    fade = int(sr * 0.25)
    cut = cut.copy()
    cut[-fade:] *= np.linspace(1, 0, fade) ** 2
    return resample(cut, sr)


def write16(name, x, gain=0.9):
    x = x / max(1e-6, np.abs(x).max()) * gain
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print("wrote", os.path.normpath(path), round(len(x) / RATE, 2), "s")


def main(src):
    for raw, name, tail in (("D_32P.wav", "rifle_shoot.wav", 1.3), ("O_21P.wav", "shotgun_shoot.wav", 1.6)):
        x, sr = read24(os.path.join(src, raw))
        write16(name, first_shot(x, sr, tail))
    for i in range(5):
        for kenney, game in ((f"footstep_wood_00{i}.ogg", f"footstep_dock_{i}.ogg"), (f"footstep_snow_00{i}.ogg", f"footstep_floor_{i}.ogg")):
            shutil.copyfile(os.path.join(src, kenney), os.path.join(OUT, game))
    print("copied 10 Kenney files")


if __name__ == "__main__":
    main(sys.argv[1])
