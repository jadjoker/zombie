"""
Placeholder sound effects for the Godot port, made from code (no downloads, nothing to license).
Writes 16-bit mono .wav files into godot/assets/sounds, one per Sfx name. Rerun after tweaking:

    python Tools/godot/make_sounds.py            (all)
    python Tools/godot/make_sounds.py rifle_shoot  (just that one)

Every sound is built from a few pieces: noise and tones shaped by envelopes and filters, plus a little room
reverb. Swap any file for a real recording later; keep the name and the game picks it up.
"""
import os
import wave
import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "godot", "assets", "sounds")
rng = np.random.default_rng(7)


# ---- building blocks -------------------------------------------------------------------------------------

def t(seconds):
    return np.arange(int(SR * seconds)) / SR


def noise(seconds):
    return rng.uniform(-1, 1, int(SR * seconds))


def env(n, attack=0.002, decay=0.2, hold=0.0):
    """Fast attack, optional hold, exponential decay (decay = time to fall to ~5%)."""
    x = np.arange(n) / SR
    a = np.clip(x / max(attack, 1e-5), 0, 1)
    d = np.where(x < attack + hold, 1.0, np.exp(-3.0 * (x - attack - hold) / max(decay, 1e-5)))
    return a * d


def bell(n, peak=0.5):
    """Smooth rise and fall, peaking at `peak` (0-1) of the length."""
    x = np.linspace(0, 1, n)
    up = np.clip(x / peak, 0, 1)
    down = np.clip((1 - x) / (1 - peak), 0, 1)
    return np.sin(np.minimum(up, down) * np.pi / 2) ** 2


def spectral(x, lo=None, hi=None, slope=1.0):
    """Static band filter in the frequency domain (soft edges)."""
    f = np.fft.rfftfreq(len(x), 1 / SR)
    g = np.ones_like(f)
    if lo:
        g *= 1 / (1 + (lo / np.maximum(f, 1)) ** (2 * slope))
    if hi:
        g *= 1 / (1 + (f / hi) ** (2 * slope))
    return np.fft.irfft(np.fft.rfft(x) * g, len(x))


def sweep_lp(x, f_start, f_end, poles=3):
    """Lowpass (a few one-pole stages, so it is dark enough) whose cutoff glides from f_start to f_end (Hz)."""
    fc = np.geomspace(max(f_start, 1), max(f_end, 1), len(x))
    a = 1 - np.exp(-2 * np.pi * fc / SR)
    y = np.asarray(x, dtype=float)
    for _ in range(poles):
        out = np.empty_like(y)
        s = 0.0
        for i in range(len(y)):
            s += a[i] * (y[i] - s)
            out[i] = s
        y = out
    return y


def bandsweep(x, f_start, f_end, width=0.6):
    """A moving band: lowpass at f*(1+width) minus lowpass at f*(1-width)."""
    return sweep_lp(x, f_start * (1 + width), f_end * (1 + width)) - sweep_lp(x, f_start * (1 - width), f_end * (1 - width))


def tone(seconds, f0, f1=None, shape="sine"):
    """A tone gliding (exponentially) from f0 to f1."""
    n = int(SR * seconds)
    f = np.geomspace(f0, f1 or f0, n)
    ph = 2 * np.pi * np.cumsum(f) / SR
    if shape == "sine":
        return np.sin(ph)
    if shape == "saw":
        return 2 * ((ph / (2 * np.pi)) % 1) - 1
    if shape == "square":
        return np.sign(np.sin(ph))
    if shape == "tri":
        return 2 * np.abs(2 * ((ph / (2 * np.pi)) % 1) - 1) - 1
    raise ValueError(shape)


def pad(x, seconds):
    n = int(SR * seconds)
    return np.pad(x, (0, max(0, n - len(x))))[:max(n, len(x))]


def mix(*parts, length=None):
    """Adds (signal, start_seconds, gain) parts together."""
    end = max(int(start * SR) + len(sig) for sig, start, _ in parts)
    if length:
        end = max(end, int(length * SR))
    out = np.zeros(end)
    for sig, start, gain in parts:
        i = int(start * SR)
        out[i:i + len(sig)] += sig * gain
    return out


def reverb(x, room=0.25, wet=0.25, bright=4000):
    """Short room tail: convolve with decaying filtered noise (FFT)."""
    ir = spectral(noise(room), hi=bright) * env(int(SR * room), 0.001, room * 0.7)
    ir[0] = 0
    n = len(x) + len(ir)
    wetsig = np.fft.irfft(np.fft.rfft(x, n) * np.fft.rfft(ir, n), n)
    wetsig /= max(np.max(np.abs(wetsig)), 1e-9)
    dry = np.pad(x, (0, len(ir)))
    return dry + wetsig * wet * np.max(np.abs(x))


def drive(x, amount=2.0):
    """Soft saturation for punch."""
    return np.tanh(x * amount) / np.tanh(amount)


def finish(x, peak=0.9, fade=0.01):
    x = x - np.mean(x)
    n = int(SR * fade)
    if len(x) > n:
        x[-n:] *= np.linspace(1, 0, n)
    m = np.max(np.abs(x))
    return x / m * peak if m > 0 else x


def save(name, x, peak=0.9):
    x = finish(np.asarray(x, dtype=float), peak)
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())


# ---- reusable sound shapes ---------------------------------------------------------------------------------

def click(freq=3500, seconds=0.03, ring=0.012):
    n = int(SR * seconds)
    body = spectral(noise(seconds), lo=freq * 0.5, hi=freq * 1.8) * env(n, 0.0005, ring)
    ping = tone(seconds, freq * 1.1) * env(n, 0.0005, ring * 0.7) * 0.4
    return body + ping


def thump(seconds=0.25, f0=120, f1=45, decay=0.18):
    n = int(SR * seconds)
    return tone(seconds, f0, f1) * env(n, 0.001, decay)


def whoosh(seconds, f0, f1, peak=0.5, width=0.5):
    n = int(SR * seconds)
    return bandsweep(noise(seconds), f0, f1, width) * bell(n, peak)


def squelch(seconds=0.35, low=250, high=1400, wet=1.0):
    """Wet organic burst: noise through a wobbling lowpass, a few sub-bursts."""
    n = int(SR * seconds)
    x = np.zeros(n)
    for _ in range(rng.integers(3, 6)):
        start = rng.uniform(0, seconds * 0.35)
        dur = rng.uniform(0.05, seconds * 0.6)
        f_a, f_b = rng.uniform(low, high), rng.uniform(low * 0.5, high * 0.6)
        burst = sweep_lp(noise(dur), f_a, f_b) * env(int(SR * dur), 0.002, dur * 0.5)
        i = int(start * SR)
        x[i:i + len(burst)] += burst[:n - i] * rng.uniform(0.5, 1.0)
    x = spectral(x, hi=high * 1.5)
    # bubbly wet pops
    for _ in range(int(4 * wet)):
        dur = rng.uniform(0.02, 0.05)
        pop = tone(dur, rng.uniform(250, 500), rng.uniform(600, 1200)) * env(int(SR * dur), 0.001, dur * 0.6)
        i = int(rng.uniform(0, seconds * 0.6) * SR)
        x[i:i + len(pop)] += pop[:n - i] * 0.25
    return x


def blub(f0=280, f1=900, seconds=0.07):
    return tone(seconds, f0, f1) * env(int(SR * seconds), 0.002, seconds * 0.6)


def chime(freqs, step=0.08, ring=0.5, shape="sine"):
    parts = []
    for i, f in enumerate(freqs):
        s = ring + 0.05
        n = int(SR * s)
        partial = tone(s, f, shape=shape) + 0.3 * tone(s, f * 2.01) + 0.12 * tone(s, f * 3.02)
        parts.append((partial * env(n, 0.003, ring), i * step, 1.0))
    return mix(*parts)


def gunshot(boom_hz=80, body_hi=3200, tail=0.45, crack=1.0, scale=1.0):
    crack_s = spectral(noise(0.02), lo=2500) * env(int(SR * 0.02), 0.0003, 0.006)
    body = spectral(noise(0.25), lo=300, hi=body_hi) * env(int(SR * 0.25), 0.0008, 0.08 * scale)
    boom = tone(0.35 * scale, boom_hz * 1.8, boom_hz * 0.7) * env(int(SR * 0.35 * scale), 0.001, 0.14 * scale)
    tail_s = spectral(noise(tail), hi=1400) * env(int(SR * tail), 0.01, tail * 0.6)
    shot = mix((crack_s, 0, 1.2 * crack), (body, 0, 1.0), (boom, 0, 1.3), (tail_s, 0.01, 0.35))
    return reverb(drive(shot, 2.5), room=0.6 * scale, wet=0.35)


def zap(seconds, f0, f1, buzz=0.4):
    n = int(SR * seconds)
    base = tone(seconds, f0, f1, "saw")
    fm = tone(seconds, f0 * 0.5, f1 * 0.5) * buzz
    return spectral(base * (1 + fm), hi=6000) * bell(n, 0.15)


def pluck(freq, seconds=0.6, damp=0.996):
    """Karplus-Strong string: a twang (harpoon strands)."""
    n = int(SR * seconds)
    p = max(2, int(SR / freq))
    buf = rng.uniform(-1, 1, p)
    out = np.empty(n)
    for i in range(n):
        out[i] = buf[i % p]
        buf[i % p] = damp * 0.5 * (buf[i % p] + buf[(i + 1) % p])
    return out


# ---- the sounds -----------------------------------------------------------------------------------------------

def build():
    s = {}
    # Guns and reloads.
    # Playtest 1: the old one boomed and smeared under rapid fire. Now a tight crack, a short punch and a
    # quick metallic action, with only a small room tail.
    crack = spectral(noise(0.012), lo=3000) * env(int(SR * 0.012), 0.0002, 0.004)
    snap = spectral(noise(0.06), lo=1200, hi=7000) * env(int(SR * 0.06), 0.0003, 0.02)
    punch = tone(0.12, 220, 90) * env(int(SR * 0.12), 0.0005, 0.04)
    body = spectral(noise(0.15), lo=400, hi=2800) * env(int(SR * 0.15), 0.0005, 0.045)
    action = click(4200, 0.03, 0.008)
    s["rifle_shoot"] = reverb(drive(mix((crack, 0, 1.4), (snap, 0, 0.9), (punch, 0, 1.0), (body, 0, 0.7), (action, 0.035, 0.18)), 3.0), room=0.25, wet=0.18)
    s["shotgun_shoot"] = mix((gunshot(boom_hz=60, body_hi=2600, tail=0.7, scale=1.5), 0, 1.0),
                             (click(1800, 0.05, 0.02), 0.32, 0.25), (click(1500, 0.06, 0.025), 0.42, 0.3))   # pump
    s["dry_fire"] = click(4200, 0.05, 0.01)
    s["mag_out"] = mix((click(3000, 0.04), 0, 1.0), (spectral(noise(0.12), lo=800, hi=4000) * bell(int(SR * 0.12), 0.3), 0.03, 0.35),
                       (click(2200, 0.05, 0.02), 0.13, 0.7))
    s["mag_in"] = mix((spectral(noise(0.08), lo=600, hi=3000) * bell(int(SR * 0.08), 0.6), 0, 0.4), (thump(0.08, 300, 150, 0.03), 0.07, 0.6),
                      (click(3200, 0.05, 0.015), 0.07, 1.0), (click(2600, 0.06, 0.02), 0.2, 0.9))
    s["weapon_switch"] = mix((click(2400, 0.05, 0.015), 0, 0.8), (spectral(noise(0.1), lo=1000, hi=5000) * bell(int(SR * 0.1)), 0.02, 0.3),
                             (click(1800, 0.06, 0.02), 0.1, 1.0))
    # Impacts.
    s["hit_world"] = mix((spectral(noise(0.09), lo=900, hi=5000) * env(int(SR * 0.09), 0.0005, 0.03), 0, 1.0),
                         (thump(0.08, 220, 110, 0.03), 0, 0.4), (tone(0.18, 3200, 2400) * env(int(SR * 0.18), 0.002, 0.07), 0.01, 0.06))
    s["hit_flesh"] = mix((sweep_lp(noise(0.16), 1800, 300) * env(int(SR * 0.16), 0.001, 0.06), 0, 1.0), (thump(0.12, 160, 70, 0.05), 0, 0.8),
                         (squelch(0.2, 300, 1000, 0.5), 0.01, 0.5))
    s["splat"] = squelch(0.4, 250, 1500)
    s["gib"] = mix((thump(0.15, 140, 60, 0.06), 0, 0.8), (spectral(noise(0.12), lo=1200, hi=6000) * env(int(SR * 0.12), 0.0005, 0.04), 0, 0.5),
                   (squelch(0.5, 200, 1200), 0.02, 1.0))
    s["creature_death"] = reverb(mix((thump(0.4, 110, 40, 0.2), 0, 1.0), (squelch(0.7, 180, 1300, 2.0), 0.0, 1.2),
                                     (sweep_lp(noise(0.8), 600, 120) * env(int(SR * 0.8), 0.02, 0.5) * (0.6 + 0.4 * tone(0.8, 9, 5)), 0.05, 0.6)),
                                 room=0.4, wet=0.2)
    # Melee.
    s["axe_swing"] = whoosh(0.32, 350, 1600, 0.45, 0.5)
    s["axe_hit"] = mix((thump(0.2, 180, 70, 0.08), 0, 1.0), (spectral(noise(0.1), lo=800, hi=6000) * env(int(SR * 0.1), 0.0005, 0.035), 0, 0.8),
                       (squelch(0.25, 300, 1100, 0.5), 0.01, 0.5))
    # Player body (a robot survivor: servos, clanks, thrusters; no voice).
    s["jump"] = mix((spectral(noise(0.14), lo=1500, hi=7000) * bell(int(SR * 0.14), 0.2), 0, 0.5), (tone(0.1, 500, 900, "tri") * env(int(SR * 0.1), 0.005, 0.05), 0, 0.25))
    s["land"] = mix((thump(0.18, 130, 50, 0.07), 0, 1.0), (spectral(noise(0.12), lo=300, hi=2500) * env(int(SR * 0.12), 0.001, 0.05), 0, 0.5),
                    (click(1600, 0.05, 0.02), 0.01, 0.25))
    s["footstep"] = mix((thump(0.09, 160, 80, 0.03), 0, 0.8), (spectral(noise(0.07), lo=500, hi=3500) * env(int(SR * 0.07), 0.001, 0.025), 0, 0.6),
                        (click(2600, 0.03, 0.008), 0.004, 0.15))
    s["hurt"] = mix((click(1300, 0.15, 0.06), 0, 1.0), (thump(0.15, 220, 90, 0.06), 0, 0.8),
                    (zap(0.25, 140, 90, 0.8) * 0.5, 0.02, 0.6))
    s["death"] = reverb(mix((click(900, 0.25, 0.12), 0, 1.0), (thump(0.4, 140, 40, 0.25), 0, 1.0),
                            (zap(1.1, 420, 40, 0.5), 0.05, 0.7)), room=0.5, wet=0.3)
    s["dash"] = mix((whoosh(0.28, 600, 2200, 0.25, 0.6), 0, 1.0), (sweep_lp(noise(0.2), 900, 200) * env(int(SR * 0.2), 0.003, 0.1), 0, 0.6))
    s["slide"] = spectral(noise(0.7), lo=400, hi=2600) * bell(int(SR * 0.7), 0.15) * (0.75 + 0.25 * noise(0.7) ** 2)
    s["jet_thrust"] = mix((sweep_lp(noise(0.5), 1400, 500) * bell(int(SR * 0.5), 0.15), 0, 1.0), (tone(0.5, 70, 55) * bell(int(SR * 0.5), 0.2), 0, 0.5))
    s["surge"] = reverb(mix((whoosh(0.6, 300, 3000, 0.7, 0.6), 0, 1.0), (thump(0.4, 120, 40, 0.2), 0.42, 1.2),
                            (sweep_lp(noise(0.5), 3000, 300) * env(int(SR * 0.5), 0.002, 0.3), 0.42, 0.8)), room=0.4, wet=0.25)
    # Water and the dive.
    s["splash"] = mix((spectral(noise(0.5), lo=500, hi=7000) * env(int(SR * 0.5), 0.003, 0.22), 0, 1.0), (thump(0.25, 90, 40, 0.1), 0, 0.6),
                      *[(blub(rng.uniform(250, 400), rng.uniform(700, 1300), rng.uniform(0.04, 0.08)), rng.uniform(0.08, 0.45), 0.25) for _ in range(9)])
    s["swim"] = mix((sweep_lp(noise(0.45), 500, 1600) * bell(int(SR * 0.45), 0.4), 0, 0.6),
                    *[(blub(rng.uniform(200, 320), rng.uniform(500, 900), 0.06), rng.uniform(0.05, 0.35), 0.2) for _ in range(4)])
    s["bubbles"] = mix(*[(blub(rng.uniform(220, 380), rng.uniform(600, 1200), rng.uniform(0.04, 0.09)), i * 0.06 + rng.uniform(0, 0.03), rng.uniform(0.4, 1)) for i in range(7)])
    s["dive_whoosh"] = whoosh(2.4, 150, 1400, 0.85, 0.7) + spectral(noise(2.4), hi=300) * bell(int(SR * 2.4), 0.9) * 0.6
    s["dive_impact"] = reverb(mix((thump(0.8, 70, 25, 0.5), 0, 1.4), (spectral(noise(1.2), lo=200, hi=6000) * env(int(SR * 1.2), 0.002, 0.5), 0, 1.0),
                                  *[(blub(rng.uniform(150, 300), rng.uniform(400, 1000), rng.uniform(0.05, 0.12)), rng.uniform(0.2, 1.2), 0.3) for _ in range(16)]),
                              room=0.9, wet=0.35)
    # Creature attacks (no voices: wet and mechanical noises only).
    s["creature_attack"] = mix((whoosh(0.25, 300, 900, 0.5, 0.5), 0, 0.7), (squelch(0.2, 200, 900, 0.5), 0.12, 0.6))
    s["ink_throw"] = mix((sweep_lp(noise(0.25), 2500, 400) * env(int(SR * 0.25), 0.002, 0.1), 0, 0.8), (squelch(0.3, 300, 1200), 0.02, 0.8))
    s["harpoon"] = mix((spectral(pluck(220, 0.7, 0.995), hi=3500), 0, 1.0), (spectral(pluck(331, 0.5, 0.993), hi=3500), 0.005, 0.5), (whoosh(0.2, 800, 3000, 0.3), 0, 0.4))
    s["crab_snap"] = mix((click(2000, 0.06, 0.02), 0, 1.0), (click(1400, 0.08, 0.03), 0.045, 1.0), (thump(0.06, 250, 150, 0.02), 0.045, 0.4))
    s["card_throw"] = mix((whoosh(0.18, 1500, 4500, 0.3, 0.4), 0, 1.0), (click(5000, 0.02, 0.005), 0, 0.3))
    s["beam_charge"] = zap(1.2, 90, 900, 0.3) * np.linspace(0.3, 1.0, int(SR * 1.2)) + tone(1.2, 180, 1800) * bell(int(SR * 1.2), 0.9) * 0.25
    s["beam_fire"] = reverb(zap(1.0, 160, 120, 0.9) + spectral(noise(1.0), lo=1500, hi=8000) * bell(int(SR * 1.0), 0.1) * 0.4, room=0.5, wet=0.3)
    s["boss_spawn"] = reverb(mix(*[(tone(2.0, f, f * 1.5) * bell(int(SR * 2.0), 0.7) * (0.7 + 0.3 * tone(2.0, 6 + i)), 0, 0.5) for i, f in enumerate((110, 165, 220, 277))],
                                 (spectral(noise(2.0), lo=3000) * bell(int(SR * 2.0), 0.8) * 0.15, 0, 1.0)), room=1.2, wet=0.4)
    # The Kraken: the sea bed rumbling as it comes up, shots on its glass dome, the dome shattering.
    s["kraken_rise"] = reverb(mix((spectral(noise(5.0), lo=25, hi=160) * bell(int(SR * 5.0), 0.55) * (0.75 + 0.25 * tone(5.0, 7)), 0, 1.6),
                                  (spectral(noise(5.0), lo=400, hi=2500) * bell(int(SR * 5.0), 0.6) * 0.12, 0, 1.0),
                                  (thump(0.9, 60, 24, 0.6), 0.1, 1.2), (thump(0.9, 55, 22, 0.6), 3.3, 1.0),
                                  (tone(3.0, 48, 96) * bell(int(SR * 3.0), 0.8) * 0.5, 1.6, 1.0)), room=1.4, wet=0.4)
    s["glass_hit"] = mix((click(5200, 0.05, 0.02), 0, 1.0), (tone(0.25, 2350) * env(int(SR * 0.25), 0.001, 0.12) * 0.3, 0, 1.0),
                         (tone(0.25, 3720) * env(int(SR * 0.25), 0.001, 0.08) * 0.18, 0, 1.0))
    s["glass_break"] = reverb(mix((spectral(noise(0.5), lo=2500) * env(int(SR * 0.5), 0.001, 0.22), 0, 1.0),
                                  *[(click(f, 0.06, 0.03), t, 0.6) for f, t in ((6200, 0.03), (4800, 0.09), (7100, 0.16), (5300, 0.27), (6600, 0.4))],
                                  (thump(0.2, 180, 70, 0.08), 0, 0.6)), room=0.5, wet=0.3)
    # Game and UI.
    s["wave_start"] = reverb(mix((spectral(tone(1.3, 73.4, shape="saw") + tone(1.3, 110, shape="saw") * 0.7, hi=900) * env(int(SR * 1.3), 0.08, 0.9, 0.3), 0, 1.0),
                                 (thump(0.6, 90, 35, 0.4), 0, 0.8)), room=1.0, wet=0.35)
    s["wave_cleared"] = reverb(chime((523, 659, 784, 1047), 0.09, 0.6), room=0.8, wet=0.25)
    s["buy"] = chime((1319, 1760), 0.07, 0.25)
    s["heal"] = chime((523, 784, 1047), 0.06, 0.35, "tri")
    s["deny"] = mix((spectral(tone(0.12, 110, shape="square"), hi=1500) * env(int(SR * 0.12), 0.003, 0.08, 0.04), 0, 1.0),
                    (spectral(tone(0.14, 98, shape="square"), hi=1500) * env(int(SR * 0.14), 0.003, 0.1, 0.05), 0.14, 1.0))
    s["ui_click"] = click(3000, 0.025, 0.006)
    s["low_health"] = mix((thump(0.15, 70, 45, 0.06), 0, 1.0), (thump(0.18, 65, 40, 0.08), 0.22, 0.8))
    return s


if __name__ == "__main__":
    import sys
    # Pass names to rewrite only those (the others would come out slightly different, since they share one
    # random sequence): python Tools/godot/make_sounds.py rifle_shoot
    only = set(sys.argv[1:])
    sounds = build()
    for name, x in sounds.items():
        if not only or name in only:
            save(name, x)
    print(f"wrote {len(only) or len(sounds)} sounds to {os.path.normpath(OUT)}")
