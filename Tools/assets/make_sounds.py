"""Synthesize the game's procedural sound layers (docs/DESIGN.md 3I). Our own output, CC0.

Writes 48 kHz 16-bit WAVs to Assets/Resources/Audio/:
  rain_outdoor.wav   stereo 30 s seamless loop: hiss + thousands of droplet ticks
  rain_indoor.wav    stereo 30 s loop: rain heard through glass (muffled) + sparse taps on the pane
  city_hum.wav       stereo 40 s loop: low traffic rumble with slow swells
  room_tone.wav      mono 20 s loop: quiet HVAC air + 60 Hz mains hum (fridge)
  phone_ring.wav     2.4 s original two-note marimba ring (loops while ringing)
  phone_ping.wav     short message ping
  elevator_ding.wav  two-tone chime
  streetcar_bell.wav double "ding-ding"
  siren_wail.wav     12 s police-style wail (played far away, 3D)
"""
import pathlib
import wave
import numpy as np

SR = 48000
OUT = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "Resources" / "Audio"
rng = np.random.default_rng(416)   # Toronto area code


def write(name, data):
    data = np.asarray(data, dtype=np.float64)
    if data.ndim == 1:
        data = data[:, None]
    peak = np.max(np.abs(data)) or 1.0
    data = data / peak * 0.89
    pcm = (data * 32767).astype(np.int16)
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(pcm.shape[1]); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"{name}: {len(pcm) / SR:.1f} s, {pcm.shape[1]} ch")


def onepole_lp(x, hz):
    a = np.exp(-2 * np.pi * hz / SR)
    y = np.empty_like(x); acc = 0.0
    for i in range(len(x)):
        acc = (1 - a) * x[i] + a * acc; y[i] = acc
    return y


def fft_band(x, lo, hi):
    """band-pass by FFT masking (fast for long buffers)"""
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR)
    X[(f < lo) | (f > hi)] = 0
    return np.fft.irfft(X, len(x))


def loopable(x, fade_s=1.5):
    """crossfade the tail into the head so the clip loops without a seam"""
    n = int(fade_s * SR)
    head, tail = x[:n].copy(), x[-n:]
    ramp = np.linspace(0, 1, n)[:, None] if x.ndim == 2 else np.linspace(0, 1, n)
    x = x[:-n].copy()
    x[:n] = head * ramp + tail * (1 - ramp)
    return x


def droplets(seconds, rate, lo, hi, gain):
    n = int(seconds * SR)
    out = np.zeros(n)
    count = int(seconds * rate)
    starts = rng.integers(0, n - 800, count)
    for s in starts:
        L = rng.integers(80, 700)
        t = np.arange(L)
        tick = rng.standard_normal(L) * np.exp(-t / rng.uniform(25, 160))
        out[s:s + L] += tick * rng.uniform(0.2, 1.0)
    return fft_band(out, lo, hi) * gain


def rain_outdoor():
    sec = 31.5
    n = int(sec * SR)
    chans = []
    for c in range(2):
        hiss = fft_band(rng.standard_normal(n), 400, 9000) * 0.35
        body = fft_band(rng.standard_normal(n), 120, 900) * 0.25   # heavy rain on pavement
        drops = droplets(sec, 2200, 700, 12000, 0.9)
        chans.append(hiss + body + drops)
    write("rain_outdoor.wav", loopable(np.stack(chans, 1)))


def soft_taps(r, seconds, rate, lo, hi, gain):
    """drops hitting the window, heard from inside: a ~1 ms rise and a 3-9 ms decay, band-limited so they
    land as soft pats. Instant-onset white-noise ticks over the muffled bed read as crackle (2026-10-22)."""
    n = int(seconds * SR)
    out = np.zeros(n)
    for s in r.integers(0, n - 2000, int(seconds * rate)):
        L = int(r.integers(600, 1800)); t = np.arange(L)
        env = (1 - np.exp(-t / 48)) * np.exp(-t / r.uniform(150, 420))
        out[s:s + L] += r.standard_normal(L) * env * r.uniform(0.3, 1.0)
    return fft_band(out, lo, hi) * gain


def rain_indoor():
    r = np.random.default_rng(4162)         # own stream so it can be regenerated alone
    sec = 31.5
    n = int(sec * SR)
    chans = []
    for c in range(2):
        muffled = fft_band(r.standard_normal(n), 60, 1200) * 0.5
        air = fft_band(r.standard_normal(n), 1200, 4000) * 0.06    # glass passes a little top end
        taps = soft_taps(r, sec, 28, 200, 1100, 0.35)               # kept inside the bed's band: pats, not ticks
        chans.append(muffled + air + taps)
    write("rain_indoor.wav", loopable(np.stack(chans, 1)))


def city_hum():
    sec = 41.5
    n = int(sec * SR)
    t = np.arange(n) / SR
    chans = []
    for c in range(2):
        rumble = fft_band(rng.standard_normal(n), 25, 260)
        swell = 0.55 + 0.45 * np.sin(2 * np.pi * t / rng.uniform(7, 13) + rng.uniform(0, 6)) ** 2
        tyres = fft_band(rng.standard_normal(n), 300, 2500) * 0.12 * swell   # wet tyres hiss on swells
        chans.append(rumble * swell + tyres)
    write("city_hum.wav", loopable(np.stack(chans, 1)))


def room_tone():
    sec = 21.5
    n = int(sec * SR)
    t = np.arange(n) / SR
    air = fft_band(rng.standard_normal(n), 40, 600) * 0.6
    mains = 0.12 * np.sin(2 * np.pi * 60 * t) + 0.05 * np.sin(2 * np.pi * 120 * t) + 0.02 * np.sin(2 * np.pi * 180 * t)
    write("room_tone.wav", loopable(air + mains))


def tone(freq, sec, decay, partials=((1, 1.0), (4, 0.25), (10, 0.06))):
    t = np.arange(int(sec * SR)) / SR
    env = np.exp(-t / decay) * np.minimum(1, t / 0.004)
    return sum(a * np.sin(2 * np.pi * freq * m * t) for m, a in partials) * env


def phone_ring():
    # original pattern (not any real phone's ringtone): E5 G5 B5 G5, twice, then a gap
    notes = [659.3, 784.0, 987.8, 784.0]
    out = np.zeros(int(2.4 * SR))
    for rep in range(2):
        for i, f in enumerate(notes):
            s = int((rep * 0.7 + i * 0.15) * SR)
            v = tone(f, 0.5, 0.18)
            out[s:s + len(v)] += v[: len(out) - s]
    write("phone_ring.wav", out)


def phone_ping():
    out = np.concatenate([tone(1318.5, 0.12, 0.05), tone(1760.0, 0.35, 0.12)])
    write("phone_ping.wav", out)


def elevator_ding():
    a = tone(880.0, 1.4, 0.6, ((1, 1), (2.76, 0.3), (5.4, 0.1)))
    b = tone(698.5, 1.6, 0.7, ((1, 1), (2.76, 0.3), (5.4, 0.1)))
    out = np.zeros(int(2.0 * SR)); out[: len(a)] += a; s = int(0.45 * SR); out[s:s + len(b)] += b[: len(out) - s]
    write("elevator_ding.wav", out)


def streetcar_bell():
    # TTC streetcar foot gong: bright metallic double ding
    bell = tone(1046.5, 1.0, 0.35, ((1, 1), (2.4, 0.5), (4.1, 0.25), (6.3, 0.1)))
    out = np.zeros(int(1.6 * SR)); out[: len(bell)] += bell; s = int(0.28 * SR); out[s:s + len(bell)] += bell[: len(out) - s] * 0.9
    write("streetcar_bell.wav", out)


def siren_wail():
    sec = 12.0
    t = np.arange(int(sec * SR)) / SR
    f = 750 + 600 * (0.5 - 0.5 * np.cos(2 * np.pi * t / 4.0))        # slow wail up and down
    phase = 2 * np.pi * np.cumsum(f) / SR
    sig = np.sin(phase) + 0.35 * np.sin(2 * phase) + 0.15 * np.sin(3 * phase)
    env = np.minimum(1, t / 1.0) * np.minimum(1, (sec - t) / 1.5)
    write("siren_wail.wav", fft_band(sig * env, 300, 6000))


def footsteps():
    """placeholders until the Freesound set: heel thump + scuff, wet splash outdoors / board resonance indoors"""
    for i in range(4):
        L = int(0.32 * SR); t = np.arange(L) / SR
        thump = np.sin(2 * np.pi * rng.uniform(70, 95) * t) * np.exp(-t / 0.035)
        scuff = fft_band(rng.standard_normal(L), 1500, 7000) * np.exp(-t / 0.05) * 0.35
        splash = fft_band(rng.standard_normal(L), 2500, 11000) * np.exp(-np.maximum(0, t - 0.01) / 0.07) * 0.5
        write(f"step_concrete_{i + 1}.wav", thump * 0.9 + scuff + splash)
        board = sum(np.sin(2 * np.pi * f * t) * np.exp(-t / d) for f, d in ((rng.uniform(140, 180), 0.06), (rng.uniform(330, 420), 0.03)))
        write(f"step_wood_{i + 1}.wav", thump + board * 0.5 + scuff * 0.6)


if __name__ == "__main__":
    import sys
    ALL = [rain_outdoor, rain_indoor, city_hum, room_tone, phone_ring, phone_ping, elevator_ding,
           streetcar_bell, siren_wail, footsteps]
    pick = sys.argv[1:]                     # e.g. `python make_sounds.py rain_indoor`; none = everything
    for fn in ALL:
        if not pick or fn.__name__ in pick:
            fn()
