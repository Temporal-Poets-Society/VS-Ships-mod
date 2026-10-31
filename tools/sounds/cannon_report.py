"""
The guns' report: assets/ships/sounds/effect/cannon1..3.ogg (mono, 44.1 kHz Ogg Vorbis, quality 0.6), played one at
random at the muzzle by CannonClient (pitch varied a little, duller and lower with distance).

History: till 0.3.24 the server played vanilla's large explosion as it is (0.29 s of silence before it, then three seconds
of rumble at nearly full loudness: "reverberating and with delay"). 0.3.25 synthesised a short shot from noise and a
thump (still here, behind --synth), but the user found it "muffled and does not sound like a cannon".

Since 0.3.26 (the default) the report is cut from vanilla's RECORDED explosions (read only, never modified):
  1. Each source (survival/sounds/effect/{large,medium,small}explosion.ogg) is decoded and downmixed to mono, and its
     onset found by threshold, not hard-coded: the attack is the first sample above 5 % of the peak (the medium and small
     ones start with their crack), the large one's body the first sample above 50 % of its peak (its recording has a
     weak transient ~38 ms before the real hit, after ~0.29 s of silence). Everything before is cut.
  2. Body + crack, onsets aligned to the sample: the large explosion's body (the weight and the rumble) starts at
     t = BODY_AT; over it, from t = 0, the medium or small one (its crack, full for 20 ms, then an exponential fade,
     tau 0.2-0.25 s, so its dense noise also fills the large recording's choppy gaps in the first 0.3 s). Variant 3
     takes the medium explosion as body, the large under it for weight and the small's first ~60 ms as crack.
  3. Brightness and punch, no low-pass: a gentle zero-phase high shelf (+3 dB above ~2.5 kHz), a head boost
     (+4 dB at t = 0 falling off over ~40 ms), then a soft (tanh, DRIVE) limiter for loudness.
  4. A short tail: full level till ~0.6-0.8 s, then an exponential fade (tau ~0.3 s) and a raised-cosine taper to
     silence by 1.5-1.8 s; no hard cut.
  5. Normalised to -0.8 dBFS and encoded; the file is decoded again and, if the Vorbis peak overshoots -0.5 dBFS,
     re-encoded a little quieter.

  python tools/sounds/cannon_report.py [VINTAGE_STORY_DIR | path/to/assets/survival/sounds/effect] [--synth]
    (default: %VINTAGE_STORY% or %APPDATA%/Vintagestory; needs numpy and soundfile, whose wheel bundles libsndfile with
     Vorbis)
"""
import os
import sys

import numpy as np
import soundfile as sf

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "ships", "sounds", "effect")
QUALITY = 0.6          # Vorbis quality (libsndfile: compression_level = 1 - quality)
TARGET_DB = -0.8       # peak after normalising
MAX_DB = -0.5          # decoded peak ceiling
BODY_AT = 0.002        # where the body's hit lands (s), just after the crack's first transient
DRIVE = 1.8            # soft limiter drive (tanh): higher = louder body, more saturation


# ---------------------------------------------------------------- recorded (default, 0.3.26)

def vanilla_dir(arg=None):
    base = arg or os.environ.get("VINTAGE_STORY") or os.path.join(os.environ.get("APPDATA", ""), "Vintagestory")
    for d in (base, os.path.join(base, "assets", "survival", "sounds", "effect")):
        if os.path.isfile(os.path.join(d, "largeexplosion.ogg")):
            return d
    sys.exit(f"vanilla explosions not found under {base} (give the Vintage Story folder or its sounds/effect folder)")


def load(folder, name):
    x, sr = sf.read(os.path.join(folder, name + "explosion.ogg"), dtype="float64", always_2d=True)
    if sr != SR:
        sys.exit(f"{name}explosion.ogg is {sr} Hz, expected {SR}")
    return x.mean(axis=1)


def onset(x, rel):
    """First sample above rel x the peak."""
    a = np.abs(x)
    return int(np.argmax(a > rel * a.max()))


def from_onset(x, rel, at=0.0, pre=0.0005):
    """x cut so its onset (by threshold rel) lands at t = at; a 'pre' s run-up kept, faded in over it."""
    i = max(0, onset(x, rel) - int(pre * SR))
    y = x[i:].copy()
    k = int(pre * SR)
    if k > 0:
        y[:k] *= np.linspace(0, 1, k) ** 2
    lead = max(0, int(round((at - pre) * SR)))
    return np.concatenate([np.zeros(lead), y])


def mix(n, *layers):
    out = np.zeros(n)
    for g, x in layers:
        m = min(n, len(x))
        out[:m] += g * x[:m]
    return out


def high_shelf(x, hz=2500.0, db=3.0):
    """Zero-phase shelf in the frequency domain: smooth rise of db above hz."""
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    g = 10 ** (db / 20)
    gain = 1 + (g - 1) * (f / hz) ** 2 / (1 + (f / hz) ** 2)
    return np.fft.irfft(np.fft.rfft(x) * gain, n)


def recorded(src, body, crack, crack_gain, crack_tau, fade_at, fade_tau, end, under=None, under_gain=0.0):
    n = int(SR * (end + 0.05))
    t = np.arange(n) / SR
    b = from_onset(src[body], 0.5, at=BODY_AT)
    c = from_onset(src[crack], 0.05, at=0.0)
    c = c[:n] if len(c) >= n else np.pad(c, (0, n - len(c)))
    c *= np.exp(-np.maximum(0, t - 0.02) / crack_tau)
    layers = [(1.0 / np.abs(b).max(), b), (crack_gain / np.abs(c).max(), c)]
    if under:
        u = from_onset(src[under], 0.5, at=BODY_AT)
        layers.append((under_gain / np.abs(u).max(), u))
    x = mix(n, *layers)
    x = high_shelf(x)
    x *= 1 + 0.6 * np.exp(-t / 0.04)                        # (head boost: about +4 dB at the crack)
    x = np.tanh(DRIVE * x / np.abs(x).max()) / np.tanh(DRIVE)   # (soft limiter: louder body, rounded peaks)
    g = np.where(t < fade_at, 1.0, np.exp(-(t - fade_at) / fade_tau))
    taper = np.clip((end - t) / 0.35, 0, 1)
    g *= 0.5 - 0.5 * np.cos(np.pi * taper)                  # (raised cosine to silence at 'end')
    x *= g
    return x / np.abs(x).max()


VARIANTS = [
    # body,     crack,     gain, tau,  fade_at, fade_tau, end, under, under_gain
    ("large",  "medium",  0.85, 0.25, 0.75,    0.32,     1.80, None, 0.0),
    ("large",  "small",   0.85, 0.20, 0.65,    0.28,     1.65, None, 0.0),
    ("medium", "small",   0.50, 0.04, 0.60,    0.30,     1.55, "large", 0.70),
]


def write_ogg(path, x):
    """Encode at TARGET_DB; re-encode quieter while the decoded peak overshoots MAX_DB."""
    level = TARGET_DB
    for _ in range(6):
        y = (x * 10 ** (level / 20)).astype(np.float32)
        sf.write(path, y, SR, format="OGG", subtype="VORBIS", compression_level=1 - QUALITY)
        peak = 20 * np.log10(np.abs(sf.read(path, dtype="float64")[0]).max())
        if peak <= MAX_DB:
            return peak
        level -= peak - MAX_DB + 0.05
    return peak


# ---------------------------------------------------------------- synthesised (0.3.25, --synth)

def lowpass(x, hz, passes=2):
    a = np.exp(-2 * np.pi * hz / SR)
    for _ in range(passes):
        y = np.empty_like(x)
        acc = 0.0
        for i, v in enumerate(x):
            acc = (1 - a) * v + a * acc
            y[i] = acc
        x = y
    return x


def envelope(t, attack, tau):
    return np.where(t < attack, t / attack, np.exp(-(t - attack) / tau))


def synth(seed, boom_hz=70.0, boom_tau=0.25, rumble_tau=0.4, crack=0.55):
    """0.3.25: crack (high-passed noise, ~25 ms), boom (low noise + falling 70 -> 38 Hz thump), short rumble."""
    rng = np.random.default_rng(seed)
    n = int(SR * 1.6)
    t = np.arange(n) / SR
    noise = rng.standard_normal(n)
    c = noise - lowpass(noise, 1200.0, 1)
    c *= envelope(t, 0.001, 0.025) * crack
    low = lowpass(rng.standard_normal(n), 160.0)
    low /= np.abs(low).max()
    f = 38.0 + (boom_hz - 38.0) * np.exp(-t / 0.12)
    thump = np.sin(2 * np.pi * np.cumsum(f) / SR)
    b = (0.8 * low + 0.7 * thump) * envelope(t, 0.004, boom_tau)
    r = lowpass(rng.standard_normal(n), 420.0)
    r /= np.abs(r).max()
    r *= 0.35 * envelope(t, 0.02, rumble_tau)
    x = c + b + r
    x = np.tanh(1.6 * x / np.abs(x).max())
    x *= np.clip((1.45 - t) / 0.35, 0, 1) ** 2
    return x / np.abs(x).max()


SYNTH_VARIANTS = [(11, 70.0, 0.25, 0.40, 0.55), (23, 62.0, 0.28, 0.45, 0.50), (37, 76.0, 0.22, 0.38, 0.60)]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    os.makedirs(OUT, exist_ok=True)
    if "--synth" in sys.argv:
        sounds = [synth(*v) for v in SYNTH_VARIANTS]
    else:
        folder = vanilla_dir(args[0] if args else None)
        src = {k: load(folder, k) for k in ("large", "medium", "small")}
        sounds = [recorded(src, *v) for v in VARIANTS]
    for k, x in enumerate(sounds, start=1):
        p = os.path.join(OUT, f"cannon{k}.ogg")
        peak = write_ogg(p, x)
        print(f"{os.path.abspath(p)}  {len(x) / SR:.2f} s, peak {peak:.2f} dBFS")


if __name__ == "__main__":
    main()
