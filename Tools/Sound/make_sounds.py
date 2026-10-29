"""Makes every game sound effect from scratch (no samples), so the game owns them outright.

Run:  python3 Tools/Sound/make_sounds.py
Writes 48 kHz mono WAVs to Assets/Resources/Audio/Sfx/. Needs numpy and scipy.

Everything pitched is tuned to D (Arcade Rush 1 is in D minor): wins land on D major,
losses on D minor. Sounds were picked by Brandon from the sketches on the sound plan page
(blob button, move C1 "zap", capture, check B "sonar", checkmate B "level up",
ice slide, zombie rises).
"""
import os, wave, zlib
import numpy as np
from scipy import signal

SR = 48000
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "Audio", "Sfx")
rng = np.random.default_rng(7)

D4 = 293.66
SOFT_TOP = 5000   # highest harmonic in saw / square / triangle tones
SOFT_LP = 4500    # every sound gets a gentle roll-off above this
SOFT_ATTACK = 0.004  # and no click at the start
def hz(semi):
    """Semitones above D4"""
    return D4 * 2 ** (semi / 12)

def buf(dur):
    return np.zeros(int(SR * dur))

def at(out, t, x):
    """Mix x into out starting at t seconds (grows out if needed)"""
    i = int(SR * t)
    if i + len(x) > len(out):
        out = np.concatenate([out, np.zeros(i + len(x) - len(out))])
    out[i:i + len(x)] += x
    return out

def env(n, att, rel_shape=1.0):
    """Fast attack, exponential-ish decay to silence at the end"""
    t = np.arange(n) / SR
    a = np.minimum(1, t / max(att, 1e-4))
    d = (1 - np.arange(n) / n) ** (3 * rel_shape)
    return a * d

def osc(f0, f1, dur, kind="sine", glide=None, detune=0.0):
    """Oscillator with an exponential pitch glide; saw and square are band limited"""
    n = int(SR * dur)
    t = np.arange(n) / SR
    if f1 is None or f1 == f0:
        f = np.full(n, f0)
    else:
        g = glide or dur
        k = np.minimum(t / g, 1)
        f = f0 * (f1 / f0) ** k
    f = f * 2 ** (detune / 1200)
    ph = 2 * np.pi * np.cumsum(f) / SR
    if kind == "sine":
        return np.sin(ph)
    # harmonics stop at 5 kHz: rounder, like the button taps (Brandon: everything a bit less harsh)
    top = SOFT_TOP / f.max()
    if kind == "triangle":
        x = np.zeros(n)
        for k in range(1, int(top) + 1, 2):
            x += ((-1) ** ((k - 1) // 2)) * np.sin(k * ph) / k ** 2
        return x * 8 / np.pi ** 2
    if kind == "saw":
        x = np.zeros(n)
        for k in range(1, int(top) + 1):
            x += np.sin(k * ph) / k
        return x * 2 / np.pi
    if kind == "square":
        x = np.zeros(n)
        for k in range(1, int(top) + 1, 2):
            x += np.sin(k * ph) / k
        return x * 4 / np.pi
    raise ValueError(kind)

def lp(x, fc, order=2):
    b, a = signal.butter(order, min(fc, SR / 2.2) / (SR / 2), "low")
    return signal.lfilter(b, a, x)

def hp(x, fc, order=2):
    b, a = signal.butter(order, fc / (SR / 2), "high")
    return signal.lfilter(b, a, x)

def bp(x, fc, q=1.0):
    lo, hi = fc / (1 + 1 / (2 * q)), min(fc * (1 + 1 / (2 * q)), SR / 2.2)
    b, a = signal.butter(2, [lo / (SR / 2), hi / (SR / 2)], "band")
    return signal.lfilter(b, a, x)

def noise(dur):
    return rng.uniform(-1, 1, int(SR * dur))

def sweep_bp(x, f0, f1, q=1.0, blocks=64):
    """Band-pass whose centre glides from f0 to f1 (block-wise)"""
    out = np.zeros_like(x)
    n = len(x)
    edges = np.linspace(0, n, blocks + 1).astype(int)
    zi = None
    for i in range(blocks):
        fc = f0 * (f1 / f0) ** (i / max(blocks - 1, 1))
        lo, hi = fc / (1 + 1 / (2 * q)), min(fc * (1 + 1 / (2 * q)), SR / 2.2)
        b, a = signal.butter(2, [lo / (SR / 2), hi / (SR / 2)], "band")
        if zi is None:
            zi = signal.lfilter_zi(b, a) * 0
        seg, zi = signal.lfilter(b, a, x[edges[i]:edges[i + 1]], zi=zi)
        out[edges[i]:edges[i + 1]] = seg
    return out

def tone(f0, dur, vol=0.5, kind="sine", f1=None, glide=None, att=0.004, cutoff=None, detune=0.0, shape=1.0):
    x = osc(f0, f1, dur, kind, glide, detune)
    if cutoff:
        x = lp(x, cutoff)
    return x * env(len(x), att, shape) * vol

def hiss(dur, f0, vol=0.3, f1=None, q=1.0, att=0.01, kind="band", shape=1.0):
    x = noise(dur)
    if kind == "band":
        x = sweep_bp(x, f0, f1 or f0, q)
    elif kind == "high":
        x = hp(x, f0)
    else:
        x = lp(x, f0)
    x = x / (np.abs(x).max() + 1e-9)
    return x * env(len(x), att, shape) * vol

def thud(f0=150, f1=55, dur=0.22, vol=0.7):
    return tone(f0, dur, vol, f1=f1, glide=dur * 0.6, att=0.002)

_ir = None
def room(x, wet=0.12):
    """A small bright room so the sounds aren't bone dry"""
    global _ir
    if _ir is None:
        n = int(SR * 0.55)
        _ir = rng.standard_normal(n) * np.exp(-np.arange(n) / (SR * 0.11))
        _ir = hp(lp(_ir, 7000), 300)
        _ir /= np.sqrt(np.sum(_ir ** 2))
    y = np.concatenate([x, np.zeros(len(_ir))])
    w = np.zeros(len(y)); c = signal.fftconvolve(x, _ir)[:len(y)]; w[:len(c)] = c
    return y + w * wet

def finish(x, peak_db=-1.0, loud=0.0, wet=0.12):
    """Room, gentle saturation, trim the silent tail, fades, level"""
    # soften: a gentle one-pole roll-off of the top end and no sharp start
    b, a = signal.butter(1, SOFT_LP / (SR / 2), "low")
    x = signal.lfilter(b, a, x)
    n = int(SR * SOFT_ATTACK)
    x[:n] *= np.linspace(0, 1, n)
    if wet:
        x = room(x, wet)
    x = np.tanh(x * 1.05) / np.tanh(1.05)
    # trim tail below -60 dB
    a = np.abs(x)
    thresh = a.max() * 10 ** (-60 / 20)
    last = np.nonzero(a > thresh)[0]
    x = x[:last[-1] + 1] if len(last) else x
    fade = min(len(x) // 4, int(SR * 0.02))
    x[-fade:] *= np.linspace(1, 0, fade)
    x = x / (np.abs(x).max() + 1e-9) * 10 ** ((peak_db + loud) / 20)
    return x

def write(name, x, **kw):
    x = finish(x, **kw)
    os.makedirs(OUT, exist_ok=True)
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"{name:18s} {len(x) / SR:5.2f}s")
    # Fresh random grain for the next sound, seeded by this one's name, so changing one
    # sound's recipe doesn't re-roll the noise in every sound after it
    global rng
    rng = np.random.default_rng(zlib.crc32(name.encode()))

# ---------------- Menus ----------------
def ui():
    # Small and light (Brandon: the first version felt too big): a short high bloop, no low end
    for i, end in enumerate([880, 940, 830]):
        x = tone(440, 0.07, 0.6, f1=end, glide=0.03, att=0.002, shape=0.8)
        x = at(x, 0.01, tone(end * 1.5, 0.04, 0.12, "triangle"))
        write(f"ui_tap_{i + 1}", hp(x, 300), loud=-11, wet=0.02)
    x = tone(880, 0.08, 0.6, f1=420, glide=0.04, att=0.002, shape=0.8)
    write("ui_back", hp(x, 300), loud=-12, wet=0.02)
    write("ui_toggle_on", at(tone(hz(12), 0.07, 0.4, "triangle"), 0.06, tone(hz(19), 0.12, 0.4, "triangle")), loud=-6, wet=0.08)
    write("ui_toggle_off", at(tone(hz(19), 0.07, 0.4, "triangle"), 0.06, tone(hz(12), 0.12, 0.4, "triangle")), loud=-6, wet=0.08)
    x = tone(150, 0.12, 0.6, "square", f1=110, cutoff=700)
    write("ui_error", at(x, 0.11, tone(140, 0.16, 0.6, "square", f1=95, cutoff=600)), loud=-4, wet=0.05)
    x = hiss(0.32, 700, 0.35, f1=4000, q=1.2, att=0.12)
    for k, s in enumerate([24, 28, 31]):
        x = at(x, 0.12 + k * 0.04, tone(hz(s), 0.18, 0.06))
    write("ui_open", x, loud=-8)
    x = buf(0)
    for k, s in enumerate([12, 16, 19, 24]):
        x = at(x, k * 0.05, tone(hz(s), 0.35, 0.18, "triangle"))
    for k in range(10):
        x = at(x, 0.15 + k * 0.03, tone(hz(31 + (k % 4) * 2), 0.1, 0.05))
    write("unlock", x, loud=-4)
    x = buf(0)
    for k in range(8):
        x = at(x, k * 0.045, tone(hz(31) * (1 + 0.02 * (k % 3)), 0.25, 0.25 * (1 - k / 10), "triangle"))
        x = at(x, k * 0.045, tone(hz(38), 0.06, 0.08))
    write("purchase", x, loud=-3)

# ---------------- Setup ----------------
def setup():
    x = hiss(0.04, 3000, 0.3, q=2, att=0.001)
    write("piece_pick", at(x, 0.0, tone(hz(19), 0.06, 0.15)), loud=-8, wet=0.05)
    x = tone(hz(12), 0.2, 0.4, "triangle")
    x = at(x, 0, thud(160, 70, 0.12, 0.5))
    write("piece_place", x, loud=-5, wet=0.08)
    x = buf(0)
    for k, s in enumerate([0, 3, 5, 7, 10, 12, 15, 17, 19, 22]):
        x = at(x, k * 0.055, tone(hz(12 + s), 0.14, 0.3, "triangle"))
        x = at(x, k * 0.055, thud(140, 70, 0.08, 0.25))
    write("autoplace", x, loud=-4)
    x = tone(hz(7), 0.12, 0.35, "square", cutoff=2200)
    x = at(x, 0.12, tone(hz(12), 0.4, 0.35, "square", cutoff=2600))
    x = at(x, 0.12, tone(hz(19), 0.4, 0.15, "triangle"))
    write("ready", x, loud=-4)

# ---------------- Chess ----------------
def zap(f, take):
    """Move: Brandon's pick, the soft C1 zap (take 3 of the earlier set). Its second note is
    move_chime, played by the game at a pitch set by the square's row (GameSounds.MoveLanded).
    Takes differ only very slightly so repeats don't sound copied."""
    # kept short (Brandon: it ran past the end of the move)
    glide, bright, det = [(0.04, 2200, -7), (0.036, 2350, -4), (0.044, 2050, -10)][take]
    x = tone(f * 2, 0.09, 0.25, "saw", f1=f, glide=glide, cutoff=bright)
    x = at(x, 0.02, tone(f, 0.14, 0.2, "square", cutoff=1100, detune=det))
    return x

def chess():
    for k in range(3):
        write(f"move_{k + 1}", zap(hz(12), k), loud=-3, wet=0.04)
    for k in range(3):
        # each take rings a different note of D minor (A, F, C) over a different thud
        note = [19, 15, 22][k]
        x = thud([140, 165, 120][k], 55, 0.25, 0.7)
        x = at(x, 0, tone(hz(note), 0.4, 0.25, "triangle"))
        x = at(x, 0, tone(hz(note + 7), 0.25, 0.12))
        x = at(x, 0.01, hiss(0.25, 5000, 0.12, kind="high"))
        # little glass shards
        for j in range(4):
            x = at(x, 0.03 + j * 0.025 + rng.uniform(0, 0.01), tone(hz(31 + rng.integers(0, 7)), 0.06, 0.05))
        write(f"capture_{k + 1}", x, loud=-1)
    # Check B: low sonar pulse with a ping
    x = tone(hz(-17), 0.6, 0.6, att=0.02)  # A2
    x = at(x, 0.05, tone(hz(22), 0.9, 0.18))
    x = at(x, 0.35, tone(hz(22), 0.5, 0.06))
    write("check", x, loud=-1, wet=0.2)
    # your turn: friendly two-note bell
    # small and polite (the first version was too big): two quick high pings
    x = tone(hz(27), 0.12, 0.3, att=0.002)
    x = at(x, 0.08, tone(hz(31), 0.18, 0.3, att=0.002))
    write("your_turn", hp(x, 400), loud=-10, wet=0.06)
    # Checkmate B: arcade level up, D major
    x = buf(0)
    for k, s in enumerate([0, 4, 7, 12, 16, 19, 24]):
        x = at(x, k * 0.06, tone(hz(12 + s), 0.12, 0.2, "square", cutoff=3500))
    for s in [0, 7, 12, 16]:
        x = at(x, 0.45, tone(hz(12 + s), 0.9, 0.12, "square", cutoff=2500, att=0.01))
    x = at(x, 0.45, tone(hz(-12), 0.9, 0.35, "triangle"))
    write("win", x, loud=-1)
    # loss: same voice falling, D minor
    x = buf(0)
    for k, s in enumerate([12, 8, 5, 0]):
        x = at(x, k * 0.16, tone(hz(12 + s - 5 + 5), 0.2, 0.2, "square", cutoff=2000))
    for s in [0, 3, 7]:
        x = at(x, 0.66, tone(hz(s), 1.1, 0.12, "saw", cutoff=1400, att=0.02))
    x = at(x, 0.66, tone(hz(-24), 1.1, 0.35, "triangle"))
    write("lose", x, loud=-2)
    # draw: suspended, unresolved
    x = tone(hz(12), 0.2, 0.2, "square", cutoff=2500)
    x = at(x, 0.16, tone(hz(7), 0.2, 0.2, "square", cutoff=2500))
    for s in [0, 5, 7]:
        x = at(x, 0.34, tone(hz(12 + s), 0.9, 0.12, "triangle", att=0.02))
    write("draw", x, loud=-2)
    # undo: quick rewind zip
    x = tone(hz(24), 0.16, 0.25, "saw", f1=hz(0), glide=0.14, cutoff=3000)
    write("undo", at(x, 0, hiss(0.14, 3000, 0.1, f1=600, q=1.5)), loud=-6)
    # Glitch promotion: bit-crushed stutter resolving to a D major chord
    x = buf(0)
    src = osc(hz(12), None, 0.06, "saw")
    for k in range(7):
        step = [6, 12, 4, 16, 8, 3, 10][k]
        g = np.repeat(src[::step], step)[:len(src)] * env(len(src), 0.001, 0.4) * 0.3
        x = at(x, k * 0.055, g * (1.0 if k % 2 == 0 else 0.7))
    for s in [0, 4, 7, 12]:
        x = at(x, 0.42, tone(hz(12 + s), 0.8, 0.14, "saw", cutoff=3000, att=0.01))
    x = at(x, 0.42, tone(hz(31), 0.4, 0.06))
    write("promote", x, loud=-1)
    # chaos spin: rising whirr into a hit
    x = tone(hz(-12), 0.6, 0.3, "saw", f1=hz(12), cutoff=2000, att=0.3)
    x = x * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * np.linspace(4, 24, len(x)).cumsum() / SR)))
    x = at(x, 0.58, thud(180, 60, 0.25, 0.8))
    write("chaos_spin", at(x, 0.58, tone(hz(19), 0.4, 0.2, "triangle")), loud=-3)

# ---------------- Zombies! ----------------
def zombies():
    x = hiss(0.6, 300, 0.3, f1=120, kind="low")
    for d in (-14, 0, 9):
        x = at(x, 0.15, tone(hz(-29), 0.9, 0.22, "saw", f1=hz(-22), glide=0.5, cutoff=500, detune=d, att=0.15))  # A1 to E2-ish
    for j in range(10):  # dirt crumbles
        x = at(x, rng.uniform(0, 0.5), hiss(0.03, rng.uniform(800, 2500), 0.15, q=2, att=0.001))
    write("zombie_rise", x, loud=-2)
    for k in range(2):
        x = hiss(0.12, 900 + k * 200, 0.35, q=0.8, att=0.02)
        x = at(x, 0.15, hiss(0.1, 700 + k * 150, 0.3, q=0.8, att=0.02))
        write(f"zombie_step_{k + 1}", x, loud=-9, wet=0.05)
    x = thud(200, 80, 0.1, 0.7)
    x = at(x, 0.09, thud(180, 60, 0.15, 0.8))
    x = at(x, 0.09, hiss(0.12, 2500, 0.25, q=1.2, att=0.001))
    x = at(x, 0.0, tone(hz(-24), 0.3, 0.15, "saw", f1=hz(-29), cutoff=600))
    write("zombie_bite", x, loud=-2, wet=0.05)
    x = hiss(0.3, 2500, 0.45, f1=500, q=0.9, att=0.003)
    x = at(x, 0, thud(220, 90, 0.12, 0.5))
    x = at(x, 0.16, tone(hz(31), 0.25, 0.18, "triangle"))
    x = at(x, 0.16, tone(hz(36), 0.12, 0.06))
    write("zombie_kill", x, loud=-3)
    # overrun: horde roar falling into a D minor sting
    x = buf(0)
    for d in (-30, -12, 0, 11, 25):
        x = at(x, 0, tone(hz(-26), 1.1, 0.15, "saw", f1=hz(-31), cutoff=900, detune=d, att=0.2))
    x = at(x, 0, hiss(1.0, 400, 0.25, kind="low", att=0.2))
    for k, s in enumerate([7, 3, 0]):
        x = at(x, 0.7 + k * 0.18, tone(hz(s), 0.3, 0.25, "square", cutoff=1800))
    for s in [0, 3, 7]:
        x = at(x, 1.24, tone(hz(s - 12), 1.0, 0.14, "saw", cutoff=1200))
    write("overrun", x, loud=-1)
    # round step-up: drum hit and rising alarm
    x = thud(120, 45, 0.3, 0.9)
    x = at(x, 0.05, tone(hz(7), 0.5, 0.18, "square", f1=hz(14), cutoff=2200, att=0.02))
    write("level_up", x, loud=-3)

# ---------------- Queen Blitz ----------------
def blitz():
    # dusty scrape, grainy
    x = noise(0.55)
    x = sweep_bp(x, 900, 500, q=0.7)
    grain = 0.6 + 0.4 * np.abs(lp(rng.standard_normal(len(x)), 40))
    grain /= grain.max()
    x = x / np.abs(x).max() * grain * env(len(x), 0.03, 0.7) * 0.5
    write("blitz_slide", x, loud=-7, wet=0.06)
    # pawn knocked flying: one pop (pitched up a scale at run time)
    x = tone(hz(12) * 0.7, 0.18, 0.4, "square", f1=hz(12), glide=0.03, cutoff=2400)
    x = at(x, 0, hiss(0.03, 3000, 0.15, q=1.5, att=0.001))
    write("blitz_pop", x, loud=-4, wet=0.08)
    # ice slide: shimmer that brakes
    x = hiss(1.0, 6000, 0.12, f1=2500, q=4)
    for i in range(9):
        x = at(x, i * 0.09 + rng.uniform(0, 0.03), tone(hz(31 + (i % 3) * 4), 0.12, 0.06 + (9 - i) * 0.01))
    x = at(x, 0.85, hiss(0.18, 900, 0.25, q=2))
    x = at(x, 0.88, thud(120, 70, 0.15, 0.4))
    write("ice_slide", x, loud=-3)
    x = thud(260, 120, 0.12, 0.7)
    write("blitz_block", at(x, 0, hiss(0.06, 1200, 0.3, q=1.5, att=0.001)), loud=-4, wet=0.05)
    x = tone(hz(24), 0.25, 0.3, "triangle")
    x = at(x, 0.0, tone(hz(31), 0.18, 0.12))
    write("pawn_ding", x, loud=-6, wet=0.1)
    # just the bright final chord (Brandon dropped the four-note run-up before it)
    x = buf(0)
    for s in [0, 4, 7, 12, 16]:
        x = at(x, 0, tone(hz(12 + s), 0.8, 0.1, "square", cutoff=3000, att=0.01))
    write("blitz_cleared", x, loud=-1)
    x = buf(0)
    for k, (a, b) in enumerate([(hz(3), hz(1)), (hz(1), hz(-4))]):
        y = tone(a, 0.35 if k == 0 else 0.6, 0.35, "saw", f1=b, glide=0.3)
        n = len(y)
        cut = 600 + 1400 * np.abs(np.sin(np.linspace(0, np.pi * (1 if k == 0 else 1.5), n)))
        y = np.concatenate([lp(y[i:i + 512], cut[i]) for i in range(0, n, 512)])
        x = at(x, k * 0.38, y)
    write("blitz_fail", x, loud=-3)

if __name__ == "__main__":
    ui(); setup(); chess(); zombies(); blitz()
    # the soft note on top of a move, 50 ms in; the game picks its pitch from the square
    # (written last, so adding it didn't change any other sound's noise)
    write("move_chime", at(buf(0.035), 0.035, tone(hz(12), 0.09, 0.5, "triangle")), loud=-3, wet=0.04)
