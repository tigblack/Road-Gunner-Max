#!/usr/bin/env python3
"""Turn Road Gunner's raw ElevenLabs generations into Unity-ready 16-bit PCM WAV assets.

Run on the Mac from the project root:   python3 Tools/Audio/process_generated_audio.py
Needs python3 (standard library only) plus ffmpeg/ffprobe on PATH.

Idempotent: every run re-processes whatever raw files exist and rewrites identical outputs
(files whose bytes did not change are not touched, so Unity does not re-import them).
Missing raws are reported and never faked.

Inputs   Tools/Audio/Generated/raw/<id>.wav|.mp3|...   (ids in SFX below + music_main; REJECTED ids are ignored)
Outputs  Assets/RoadGunner/Audio/Arcade/*.wav            SFX: 44.1 kHz mono 16-bit
         Assets/RoadGunner/Audio/Music/Desert Run.wav    music loop: 44.1 kHz stereo 16-bit
         Assets/RoadGunner/Audio/Music/Desert Run - source.txt
         Tools/Audio/Generated/manifest.json
Loops (tyre roll, skid, wind, music) carry Unity's WebGL AAC loop workaround: 2048 silent guard
samples, then a RIFF 'smpl' loop from 2048 to the inclusive last sample (as generate_dustlight_road.py).
"""
import argparse
import json
import math
import random
import subprocess
import struct
import sys
from array import array
from collections import deque
from itertools import accumulate
from operator import mul
from pathlib import Path

RATE = 44100
GUARD = 2048
ROOT = Path(__file__).resolve().parents[2]
RAW_EXT = (".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac")
TAU = 2 * math.pi

# Loudness by category: (sample-peak ceiling dBFS, RMS target dBFS, RMS window s or None=whole file, soft-clip).
# The RMS is measured over the loudest window, so variants of one category land at the same punch.
CATEGORIES = {
    # Casual blaster pops (owner feedback 2 Nov 2026): no soft-clip saturation, the guns must stay round and soft.
    "gun_player":      (-1.0, -12.0, .06, False),
    "gun_enemy":       (-1.5, -13.5, .06, False),
    "impact":          (-3.0, -15.0, .04, False),
    "hit":             (-2.0, -13.5, .06, False),
    "explosion":       (-1.0, -10.5, .30, True),
    "crash":           (-1.5, -12.0, .15, True),
    "crate":           (-2.0, -13.0, .12, False),
    "crate_hit":       (-3.0, -15.0, .05, False),
    "ui":              (-6.0, -19.0, .03, False),
    "pickup":          (-5.0, -17.0, .10, False),
    "powerup":         (-3.0, -15.0, .30, False),
    "warning":         (-2.0, -13.0, .40, False),
    "stinger":         (-2.0, -15.0, .80, False),
    "harpoon":         (-3.0, -15.0, .10, False),
    "whoosh":          (-6.0, -20.0, .20, False),
    "loop_roll":         (-4.0, -18.0, None, False),
    "loop_skid":         (-4.0, -18.0, None, False),
    "loop_wind":         (-9.0, -24.0, None, False),
}

# id -> processing. hp = high-pass Hz (also removes DC), lp = low-pass Hz, eq = extra ffmpeg filters,
# at = use only this window of the raw (s), pre = pre-roll s, fade = fade-out s.
# takes = hand-picked hits: generations rarely follow "N separate hits" literally, so the usable hits were chosen from
# spectrograms of the raw files (at = window in the raw, s). pitches = one variant per entry (varispeed, semitones);
# layer = a second window mixed on top (gains_db per variant relative to the main hit, None = no layer).
SFX = {
    "sfx_ui_click":          dict(out="UI_Click", cat="ui", hp=110, pre=.002, fade=.03, max_len=.6),
    "sfx_boss_warning":      dict(out="Boss_Warning", cat="warning", hp=45, pre=.006, fade=.25, max_len=3.2),
    # The only movement sound (no engine drone): the steady 2.0-5.45 s of rolling crunch (the generation ramps in from
    # 1.62 s and decays after 5.4 s, which made the loop dip at every wrap); the ~1 kHz tyre-hum band is tamed and the
    # top rolled off so it stays a soft bed under the music.
    "sfx_tire_roll_loop":    dict(out="Tire_Roll_Loop", cat="loop_roll", hp=50, lp=7000, at=(2.0, 5.45),
                                  eq=["equalizer=f=1000:t=q:w=1.4:g=-5"], loop=.35, max_len=4.0),
    "sfx_tire_skid_loop":    dict(out="Tire_Skid_Loop", cat="loop_skid", hp=160, loop=.25, max_len=4.0),
    # 8 s keeps the AAC asset under Unity WebGL's 128 KB "always decode to AudioBuffer" cut-off (true loop points).
    "sfx_desert_wind_loop":  dict(out="Wind_Desert_Loop", cat="loop_wind", hp=40, loop=1.0, max_len=8.0, whoosh="Whoosh"),
    # Toy-blaster generation: a rounded pop (click + soft low tail) at 1.65 s for the player, the descending cartoon
    # "pew" at 0.02 s for enemies (different voice, same family); the heavy boom at 0.7 s is not used.
    "sfx_player_gun":        dict(takes=[
        dict(out="Gun_Player", cat="gun_player", at=(1.60, 1.98), hp=90, lp=9000, pre=.002, fade=.07, max_len=.24, pitches=(0, -.8, .7)),
        dict(out="Gun_Enemy", cat="gun_enemy", at=(0.0, 0.32), hp=140, lp=10000, pre=.002, fade=.07, max_len=.26, pitches=(0, .9, -.6)),
    ]),
    # Soft wooden "tock" at 0.04 s with the tin-can "tink" from 1.82 s as a quiet top layer (three timbres).
    "sfx_bullet_impacts":    dict(takes=[
        dict(out="Impact_Bullet", cat="impact", at=(0.0, 0.16), hp=120, lp=9000, pre=.002, fade=.05, max_len=.14, pitches=(0, 1.2, -1.0),
             layer=dict(at=(1.79, 2.10), hp=1500, lp=12000, fade=.08, max_len=.12, gains_db=(-14, -20, None))),
    ]),
    "sfx_enemy_hits":        dict(out="Hit_Enemy", cat="hit", hp=50, pre=.003, fade=.06, max_len=.6, slices=3, gap=.14),
    "sfx_harpoon":           dict(out=("Harpoon_Fire", "Harpoon_Reel"), cat="harpoon", hp=60, pre=.003, fade=.08, max_len=2.5, split2=True),
    "sfx_explosion":         dict(out="Explosion", cat="explosion", hp=25, pre=.004, fade=.35, max_len=3.0),
    "sfx_crash_impact":      dict(out="Crash_Impact", cat="crash", hp=30, pre=.003, fade=.18, max_len=1.6),
    "sfx_crate_break":       dict(out="Crate_Break", cat="crate", hp=50, pre=.003, fade=.15, max_len=1.6, crate_hit="Crate_Hit"),
    "sfx_scrap_pickup":      dict(out="Scrap_Pickup", cat="pickup", hp=150, pre=.002, fade=.12, max_len=1.0),
    "sfx_upgrade_powerup":   dict(out="Upgrade_Powerup", cat="powerup", hp=60, pre=.004, fade=.3, max_len=2.5),
    "sfx_victory_stinger":   dict(out="Stinger_Victory", cat="stinger", hp=35, pre=.006, fade=.45, max_len=6.0),
    "sfx_defeat_stinger":    dict(out="Stinger_Defeat", cat="stinger", hp=35, pre=.006, fade=.45, max_len=6.0),
}
# Raw generations that came out unusable; kept for provenance, never processed.
REJECTED = {
    "sfx_enemy_gun": "sub-bass boom under an automatic volley, no single shots (2 Nov 2026); Gun_Enemy comes from sfx_player_gun",
}
TRUE_PEAK_LIMIT = -0.5                    # dBTP, every SFX
MUSIC_ID = "music_main"
MUSIC_RMS = .135                         # mastering target (linear RMS, both channels)
MUSIC_CEILING = 10 ** (-1.0 / 20)         # sample peak <= -1 dBFS
MUSIC_MIN_S, MUSIC_MAX_S = 42.0, 122.0   # ArenaMusicPolishChecks accepts 40-125 s for Desert Run


def db(v): return 20 * math.log10(max(v, 1e-9))
def lin(d): return 10 ** (d / 20)
def peak(x): return max(max(x), -min(x)) if len(x) else 0.0
def rms(x): return math.sqrt(sum(map(mul, x, x)) / len(x)) if len(x) else 0.0
def squares(x): return list(accumulate(map(mul, x, x), initial=0.0))
def window_rms(sq, a, b): return math.sqrt(max(0.0, sq[b] - sq[a]) / max(1, b - a))


def env_db(x, hop, win, sq=None):
    sq = sq or squares(x)
    n, half = len(x), max(1, win // 2)
    return [db(window_rms(sq, max(0, c - half), min(n, c + half))) for c in range(0, n, hop)]


def loudest_rms(x, window):
    if not window: return rms(x)
    win = max(1, min(len(x), int(window * RATE)))
    sq = squares(x)
    return max(window_rms(sq, a, a + win) for a in range(0, len(x) - win + 1, max(1, win // 4)))


def ncorr(a, b):
    den = math.sqrt(sum(map(mul, a, a)) * sum(map(mul, b, b)))
    return sum(map(mul, a, b)) / den if den > 0 else 0.0


# ---------------------------------------------------------------- I/O

def find_raw(raw_dir, ident):
    for ext in RAW_EXT:
        path = raw_dir / (ident + ext)
        if path.is_file() and path.stat().st_size > 1024: return path
    return None


def decode(path, channels, rate=RATE, filters=None):
    cmd = ["ffmpeg", "-nostdin", "-v", "error", "-i", str(path), "-vn", "-ac", str(channels), "-ar", str(rate)]
    if filters: cmd += ["-af", ",".join(filters)]
    cmd += ["-f", "f32le", "-acodec", "pcm_f32le", "pipe:1"]
    data = subprocess.run(cmd, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout
    out = array("f"); out.frombytes(data[: len(data) // 4 * 4])
    if sys.byteorder != "little": out.byteswap()
    return array("d", out)


def to_pcm16(channels, seed):
    """Interleave float channels into 16-bit PCM with TPDF dither (deterministic)."""
    rng = random.Random(seed); r = rng.random
    n, count = len(channels[0]), len(channels)
    pcm = array("h", bytes(2 * n * count))
    for c, data in enumerate(channels):
        safe = peak(data) * 32767 < 32760
        if safe: pcm[c::count] = array("h", (int(v * 32767 + r() - r() + 32768.5) - 32768 for v in data))
        else: pcm[c::count] = array("h", (max(-32768, min(32767, int(v * 32767 + r() - r() + 32768.5) - 32768)) for v in data))
    if sys.byteorder != "little": pcm.byteswap()
    return pcm


def write_wav(path, channels, loop=None, seed=1):
    """Writes PCM16; loop=(start, inclusive end) adds a RIFF smpl chunk. Returns True when bytes changed."""
    count = len(channels)
    pcm = to_pcm16(channels, seed)
    fmt = struct.pack("<HHIIHH", 1, count, RATE, RATE * 2 * count, 2 * count, 16)
    def chunk(name, data): return name + struct.pack("<I", len(data)) + data + (b"\0" if len(data) % 2 else b"")
    body = b"WAVE" + chunk(b"fmt ", fmt)
    if loop:
        smpl = struct.pack("<9I", 0, 0, round(1_000_000_000 / RATE), 60, 0, 0, 0, 1, 0)
        smpl += struct.pack("<6I", 0, 0, loop[0], loop[1], 0, 0)
        body += chunk(b"smpl", smpl)
    body += chunk(b"data", pcm.tobytes())
    blob = b"RIFF" + struct.pack("<I", len(body)) + body
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_file() and path.read_bytes() == blob: return False
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes(blob); tmp.replace(path)
    return True


def ebur128(path):
    """Integrated loudness (LUFS) and true peak (dBTP) via ffmpeg's EBU R128 meter."""
    try:
        err = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-i", str(path), "-af", "ebur128=peak=true", "-f", "null", "-"],
                             stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, check=True).stderr.decode("utf8", "replace")
    except (subprocess.CalledProcessError, FileNotFoundError):
        return None, None
    summary = err[err.rfind("Summary:"):]
    def value(tag):
        for line in summary.splitlines():
            line = line.strip()
            if line.startswith(tag):
                try: return float(line.split(":")[1].split()[0])
                except (ValueError, IndexError): return None
        return None
    return value("I:"), value("Peak:")


# ---------------------------------------------------------------- shaping

def fade(y, fade_in=.0015, fade_out=.06):
    n = len(y)
    fi = min(n // 4, int(fade_in * RATE))
    for i in range(fi): y[i] *= i / fi
    fo = min(n // 3, int(fade_out * RATE))
    for i in range(fo): y[n - fo + i] *= .5 + .5 * math.cos(math.pi * (i + 1) / fo)
    return y


def repitch(y, semitones):
    """Varispeed by linear interpolation: pitch and length change together, like AudioSource.pitch."""
    if not semitones: return array("d", y)
    step = 2 ** (semitones / 12)
    n = max(1, int((len(y) - 1) / step))
    out = array("d", bytes(8 * n))
    for i in range(n):
        p = i * step; k = int(p); f = p - k
        out[i] = y[k] * (1 - f) + y[k + 1] * f
    return out


def trim(x, pre=.004, fade_out=.06, max_len=None, tail_db=-48.0):
    """Cut leading silence (keeps a short pre-roll) and the decayed tail, then fade."""
    p = peak(x)
    if len(x) == 0 or p < 1e-5: return array("d")
    thr, start = p * lin(-38.0), 0
    for i, v in enumerate(x):
        if v > thr or -v > thr: start = i; break
    start = max(0, start - int(pre * RATE))
    hop = int(.005 * RATE)
    env = env_db(x, hop, 2 * hop)
    limit, last = max(env) + tail_db, 0
    for k in range(len(env) - 1, -1, -1):
        if env[k] > limit: last = k; break
    end = min(len(x), (last + 2) * hop)
    if max_len: end = min(end, start + int(max_len * RATE))
    if end - start < int(.01 * RATE): end = min(len(x), start + int(.01 * RATE))
    cut_level = env[min(len(env) - 1, end // hop)]
    out = array("d", x[start:end])
    return fade(out, fade_out=fade_out if cut_level < limit + 18 else max(fade_out, .14))


def soft_clip(y, ceiling):
    knee = ceiling * .6; span = ceiling - knee
    for i, v in enumerate(y):
        a = v if v >= 0 else -v
        if a > knee:
            a = knee + span * math.tanh((a - knee) / span)
            y[i] = a if v >= 0 else -a
    return y


def normalise(y, cat):
    ceiling_db, target_db, window, soft = CATEGORIES[cat]
    p, r = peak(y), loudest_rms(y, window)
    if p <= 0 or r <= 0: return y
    gain = lin(target_db - db(r))
    gain = min(gain, lin(ceiling_db - db(p)) * (lin(3.0) if soft else 1.0))
    out = array("d", (v * gain for v in y))
    if soft and peak(out) > lin(ceiling_db) * .6: soft_clip(out, lin(ceiling_db))
    if peak(out) > lin(ceiling_db):
        g = lin(ceiling_db) / peak(out); out = array("d", (v * g for v in out))
    return out


def describe(y, cat=None, extra=None):
    info = {"duration_s": round(len(y) / RATE, 4), "peak_dbfs": round(db(peak(y)), 2), "rms_dbfs": round(db(rms(y)), 2)}
    if cat:
        window = CATEGORIES[cat][2]
        if window: info["loudest_%dms_rms_dbfs" % round(window * 1000)] = round(db(loudest_rms(y, window)), 2)
    if extra: info.update(extra)
    return info


# ---------------------------------------------------------------- slicing

def onsets(x, count, min_gap):
    """Energy-rise onset detection; relaxes thresholds until `count` hits are found, keeps the strongest."""
    hop, look = int(.0025 * RATE), 10
    env = [-120.0] * look + env_db(x, hop, 2 * hop)
    top, refractory, found = max(env), max(1, int(min_gap / .0025)), []
    for rise, floor in ((12, 24), (9, 30), (6, 36), (4.5, 44)):
        found, i = [], look
        while i < len(env):
            base = min(env[i - look:i])
            if env[i] - base >= rise and env[i] >= top - floor:
                k = i
                while k > i - look + 1 and env[k - 1] > base + 3: k -= 1
                found.append(((k - look) * hop, max(env[i:i + 16])))
                i += refractory
            else:
                i += 1
        if len(found) >= count: break
    if len(found) > count:
        # Keep the strongest `count`; extra hits within 3 dB of their median become bonus variants (max +2).
        ranked = sorted(found, key=lambda o: -o[1])
        median = ranked[count // 2][1]
        found = sorted(ranked[:count] + [o for o in ranked[count:count + 2] if o[1] >= median - 3])
    return [max(0, position) for position, _ in found]


def slice_hits(x, count, min_gap, pre, max_len):
    starts = onsets(x, count, min_gap)
    pre_n, segments = int(pre * RATE), []
    for index, start in enumerate(starts):
        a = max(0, start - pre_n)
        b = starts[index + 1] - pre_n - int(.001 * RATE) if index + 1 < len(starts) else len(x)
        if max_len: b = min(b, a + int(max_len * RATE))
        if b - a > int(.02 * RATE): segments.append(x[a:b])
    return segments


def split_two(x):
    """Fire + reel: split at the most prominent valley of a smoothed (80 ms) envelope."""
    hop = int(.01 * RATE)
    env = env_db(x, hop, 8 * hop)
    n = len(env)
    lo, hi = int(.08 / .01), n - int(.15 / .01)
    if hi <= lo: return x[: len(x) // 3], x[len(x) // 3:], 0.0
    left, right = env[:], env[:]
    for k in range(1, n): left[k] = max(left[k - 1], env[k])
    for k in range(n - 2, -1, -1): right[k] = max(right[k + 1], env[k])
    best_k, best_p = lo, -1.0
    for k in range(lo, hi):
        prominence = min(left[k], right[k]) - env[k]
        if prominence > best_p + .5: best_k, best_p = k, prominence
    cut = best_k * hop
    return x[:cut], x[cut:], best_p


# ---------------------------------------------------------------- loops

def loop_body(x, xfade, max_len=None):
    """Seamless loop: drop fades, phase-align the seam, equal-power (correlation-compensated) crossfade tail->head."""
    hop = int(.05 * RATE)
    env = env_db(x, hop, hop)
    median = sorted(env)[len(env) // 2]
    first = next(k for k, e in enumerate(env) if e >= median - 5)
    last = max(k for k, e in enumerate(env) if e >= median - 5)
    x = x[first * hop: min(len(x), (last + 1) * hop)]
    X = int(xfade * RATE)
    if max_len and len(x) > int(max_len * RATE) + X: x = x[: int(max_len * RATE) + X]
    if len(x) < 4 * X: X = len(x) // 4
    natural = len(x) - X
    # Phase alignment: choose the loop length (within 30 ms) whose continuation best matches the head.
    width, search = min(X, int(.06 * RATE)), int(.03 * RATE)
    head = x[:width]
    best_l, best_c = natural, -2.0
    for length in range(max(X + width, natural - search), natural + 1, 2):
        c = ncorr(head, x[length:length + width])
        if c > best_c: best_l, best_c = length, c
    for length in range(max(X + width, best_l - 2), min(natural, best_l + 2) + 1):
        c = ncorr(head, x[length:length + width])
        if c > best_c: best_l, best_c = length, c
    length = best_l
    rho = max(0.0, ncorr(x[:X], x[length:length + X]))
    y = array("d", x[:length])
    for i in range(X):
        theta = (i + .5) / X * math.pi / 2
        a, b = math.sin(theta), math.cos(theta)
        y[i] = (x[i] * a + x[length + i] * b) / math.sqrt(1 + 2 * rho * a * b)
    w = min(X, int(.05 * RATE))
    natural = [(x[length - w:length], x[length:length + w])]
    return y, {"crossfade_s": round(X / RATE, 3), "seam_alignment_corr": round(best_c, 3), "crossfade_corr": round(rho, 3)}, natural


def seam_metrics(channels, natural=None):
    """Discontinuity at the wrap: sample jump vs. local slope RMS, and the 50 ms level step across the wrap
    compared with the level step of the same transition in the source (a downbeat naturally gets louder)."""
    jumps, slopes, level = [], [], []
    w = int(.05 * RATE)
    for index, y in enumerate(channels):
        diffs = [y[i + 1] - y[i] for i in range(200)] + [y[i + 1] - y[i] for i in range(len(y) - 201, len(y) - 1)]
        slope = math.sqrt(sum(d * d for d in diffs) / len(diffs))
        jumps.append(abs(y[0] - y[-1])); slopes.append(slope)
        step = db(rms(y[-w:])) - db(rms(y[:w]))
        if natural: pre, post = natural[index]; step -= db(rms(pre)) - db(rms(post))
        level.append(abs(step))
    ratio = max(j / s if s > 0 else 0 for j, s in zip(jumps, slopes))
    return {"seam_jump": round(max(jumps), 5), "seam_jump_vs_slope_rms": round(ratio, 2), "seam_level_step_db": round(max(level), 2),
            "seamless": ratio < 4 and max(level) < 3}


# ---------------------------------------------------------------- derived one-shots

def derive_crate_hit(raw_trimmed):
    hit = array("d", raw_trimmed[: int(.13 * RATE)])
    return normalise(fade(hit, fade_out=.05), "crate_hit")


def derive_whoosh(wind):
    """A short air swell from the generated wind: gustiest 1.2 s, pitched up, swept low-pass, swell envelope."""
    win = int(1.2 * RATE)
    if len(wind) < win + 10: return None
    sq = squares(wind)
    start = max(range(0, len(wind) - win, int(.05 * RATE)), key=lambda a: window_rms(sq, a, a + win))
    seg, step = wind[start:start + win], 1.3
    out_n = int((len(seg) - 2) / step)
    out = array("d", bytes(8 * out_n))
    lp = hp = prev = 0.0
    for i in range(out_n):
        p = i * step; k = int(p); f = p - k
        v = seg[k] * (1 - f) + seg[k + 1] * f
        t = i / out_n
        cutoff = 450 + 3800 * math.sin(math.pi * min(1.0, t * 1.15)) ** 1.5
        lp += (1 - math.exp(-TAU * cutoff / RATE)) * (v - lp)
        hp = .985 * (hp + lp - prev); prev = lp
        swell = math.sin(math.pi / 2 * t / .42) ** 2 if t < .42 else math.exp(-(t - .42) * 6.5)
        out[i] = hp * swell
    return normalise(fade(out, fade_in=.01, fade_out=.08), "whoosh")


# ---------------------------------------------------------------- music

def fft_magnitudes(frame, tables):
    rev, cos_t, sin_t = tables
    n = len(frame)
    re = [frame[r] for r in rev]; im = [0.0] * n
    size = 2
    while size <= n:
        half, step = size // 2, n // size
        for start in range(0, n, size):
            k = 0
            for j in range(start, start + half):
                l = j + half
                wr, wi = cos_t[k], sin_t[k]
                tr = re[l] * wr - im[l] * wi; ti = re[l] * wi + im[l] * wr
                re[l] = re[j] - tr; im[l] = im[j] - ti
                re[j] += tr; im[j] += ti
                k += step
        size *= 2
    return [math.hypot(re[i], im[i]) for i in range(n // 2 + 1)]


def fft_tables(n):
    bits = n.bit_length() - 1
    rev = [int(format(i, "0%db" % bits)[::-1], 2) for i in range(n)]
    return rev, [math.cos(TAU * k / n) for k in range(n // 2)], [-math.sin(TAU * k / n) for k in range(n // 2)]


def frame_energy(x, hop):
    sq = list(map(mul, x, x))
    return [sum(sq[i * hop:(i + 1) * hop]) / hop for i in range(len(x) // hop)]


def sample_at(seq, t):
    i = int(t)
    if i < 0 or i + 1 >= len(seq): return 0.0
    f = t - i
    return seq[i] * (1 - f) + seq[i + 1] * f


def cosine(a, b):
    den = math.sqrt(sum(v * v for v in a) * sum(v * v for v in b))
    return sum(p * q for p, q in zip(a, b)) / den if den > 0 else 0.0


def limiter(channels, ceiling, lookahead=.0015, release=.08):
    """Look-ahead peak limiter treating the loop as circular (no seam artefact)."""
    n = len(channels[0]); w = max(2, int(lookahead * RATE)); pad = int(.3 * RATE)
    order = list(range(n - pad, n)) + list(range(n)) + list(range(w))
    a, b = channels[0], channels[-1]
    need = [min(1.0, ceiling / max(1e-9, abs(a[i]), abs(b[i]))) for i in order]
    count, window_min, dq = len(need), [1.0] * len(need), deque()
    for i in range(count + w):                     # min over need[i : i + w]
        if i < count:
            while dq and need[dq[-1]] >= need[i]: dq.pop()
            dq.append(i)
        j = i - w + 1
        if j >= 0:
            while dq and dq[0] < j: dq.popleft()
            if j < count: window_min[j] = need[dq[0]] if dq else 1.0
    rel = 1 - math.exp(-1 / (release * RATE)); g = 1.0
    for i in range(count):
        g = min(window_min[i], g + rel * (1 - g)); window_min[i] = g
    acc = sum(window_min[:w]); smooth = window_min[:]
    for i in range(w, count):                      # box smoothing; still <= need at every peak
        acc += window_min[i] - window_min[i - w]; smooth[i] = acc / w
    gains = smooth[pad:pad + n]
    reduction = db(min(gains))
    return [array("d", (v * g for v, g in zip(ch, gains))) for ch in channels], round(reduction, 2)


def process_music(src, out_path, note_path, prompt_text):
    ar, hop = 11025, 64
    fps = ar / hop
    stereo = decode(src, 2, filters=["highpass=f=18:poles=2"])
    left, right = stereo[0::2], stereo[1::2]
    n = len(left)
    full = decode(src, 1, ar, ["highpass=f=18:poles=2"])
    bands = [decode(src, 1, ar, f) for f in (["lowpass=f=160:poles=2", "lowpass=f=160:poles=2"],
                                              ["highpass=f=200:poles=2", "lowpass=f=2500:poles=2"],
                                              ["highpass=f=3000:poles=2"])]
    energies = [frame_energy(b, hop) for b in bands]
    frames = min(len(e) for e in energies)
    # Onset envelope: weighted half-wave rectified log-energy rise per band, minus a 0.5 s local mean.
    onset = [0.0] * frames
    for weight, e in zip((1.0, .8, .6), energies):
        logs = [math.log(1e-10 + v) for v in e[:frames]]
        for i in range(1, frames): onset[i] += weight * max(0.0, logs[i] - logs[i - 1])
    span = int(.25 * fps); csum = list(accumulate(onset, initial=0.0))
    onset = [max(0.0, onset[i] - (csum[min(frames, i + span)] - csum[max(0, i - span)]) / (min(frames, i + span) - max(0, i - span)))
             for i in range(frames)]
    mean = sum(onset) / frames
    centred = [v - mean for v in onset]

    # Tempo: autocorrelation of the onset envelope with a gentle prior around 110 BPM (60-185 BPM).
    def ac(lag): return sum(map(mul, centred[:-lag], centred[lag:]))
    lags = range(int(fps * 60 / 185), int(fps * 60 / 60) + 1)
    scores = {lag: ac(lag) * math.exp(-.5 * (math.log2(fps * 60 / lag / 110) / 1.0) ** 2) for lag in lags}
    best = max(scores, key=scores.get)
    period = float(best)
    if best - 1 in scores and best + 1 in scores:
        a, b, c = scores[best - 1], scores[best], scores[best + 1]
        if a - 2 * b + c < 0: period += .5 * (a - c) / (a - 2 * b + c)
    for multiple in (4, 16, 48):                    # refine with long lags (sub-frame precision)
        centre = multiple * period
        if centre + 4 >= frames * .8: break
        cand = {lag: ac(lag) for lag in range(int(centre) - 3, int(centre) + 5)}
        lag = max(cand, key=cand.get)
        refined = float(lag)
        if lag - 1 in cand and lag + 1 in cand:
            a, b, c = cand[lag - 1], cand[lag], cand[lag + 1]
            if a - 2 * b + c < 0: refined += .5 * (a - c) / (a - 2 * b + c)
        period = refined / multiple
    bpm = fps * 60 / period

    # Beat phase: comb template over the whole track.
    def comb(phase):
        total, t = 0.0, phase
        while t < frames - 1: total += sample_at(onset, t); t += period
        return total
    phases = [p * .5 for p in range(int(period * 2))]
    comb_scores = [comb(p) for p in phases]
    phase = phases[max(range(len(phases)), key=comb_scores.__getitem__)]
    confidence = max(comb_scores) / (sum(comb_scores) / len(comb_scores))
    beats = []
    t = phase
    while t + period < frames: beats.append(t); t += period
    if len(beats) < 40: raise RuntimeError("music too short or no beat found (%d beats)" % len(beats))

    # Per-beat features: chroma (4096-pt FFT at mid-beat) + band levels.
    size = 4096; tables = fft_tables(size)
    window = [.5 - .5 * math.cos(TAU * i / size) for i in range(size)]
    pc_of_bin = []
    for k in range(size // 2 + 1):
        f = k * ar / size
        pc_of_bin.append(int(round(12 * math.log2(f / 440) + 69)) % 12 if 100 <= f <= 4000 else -1)
    chroma, levels, low_onset = [], [], []
    for t in beats:
        centre = int((t + period * .5) * hop)
        a = max(0, min(len(full) - size, centre - size // 2))
        mags = fft_magnitudes([full[a + i] * window[i] for i in range(size)], tables)
        vec = [0.0] * 12
        for k, pc in enumerate(pc_of_bin):
            if pc >= 0: vec[pc] += math.sqrt(mags[k])
        chroma.append(vec)
        i0, i1 = int(t), max(int(t) + 1, int(t + period))
        levels.append([db(math.sqrt(sum(e[i0:i1]) / (i1 - i0))) for e in energies])
        low_onset.append(max(sample_at(energies[0], t + d) for d in (-1, 0, 1, 2)))

    # Downbeat: low-band energy and harmonic change concentrate on beat 1 of the bar.
    novelty = [0.0] + [1 - cosine(chroma[k], chroma[k - 1]) for k in range(1, len(beats))]
    def z(seq):
        m = sum(seq) / len(seq); s = math.sqrt(sum((v - m) ** 2 for v in seq) / len(seq)) or 1
        return [(v - m) / s for v in seq]
    lz, nz = z([math.log(1e-10 + v) for v in low_onset]), z(novelty)
    downbeat = max(range(4), key=lambda b: sum(lz[k] + nz[k] for k in range(b, len(beats), 4)))
    bars = list(range(downbeat, len(beats) - 4, 4))           # beat index of each bar line
    bar_level = [db(math.sqrt(sum(sum(e[int(beats[k]):int(beats[k + 4])]) for e in energies) /
                              max(1, int(beats[k + 4]) - int(beats[k])))) for k in bars]
    median = sorted(bar_level)[len(bar_level) // 2]
    first_bar = next(j for j, v in enumerate(bar_level) if v >= median - 8)
    last_full = max(j for j, v in enumerate(bar_level) if v >= median - 4)
    bar_s = 4 * period / fps

    def similarity(ks, ke, count, offset=0):
        values = []
        for m in range(offset, offset + count):
            a, b = ks + m, ke + m
            if a < 0 or b < 0 or a >= len(beats) or b >= len(beats): return None
            level = 1 - min(1.0, sum(abs(p - q) for p, q in zip(levels[a], levels[b])) / 3 / 9)
            values.append(.7 * cosine(chroma[a], chroma[b]) + .3 * level)
        return sum(values) / len(values)

    candidates = []
    starts = [j for j in range(first_bar, len(bars)) if beats[bars[j]] / fps <= 30][: max(4, len(bars) // 4)]
    for js in starts:
        for je in range(js + 2, len(bars)):
            length = (beats[bars[je]] - beats[bars[js]]) / fps
            if length < MUSIC_MIN_S or length > MUSIC_MAX_S or je + 2 > last_full + 1: continue
            post = similarity(bars[js], bars[je], 8)
            if post is None: continue
            pre = similarity(bars[js], bars[je], 4, -4)
            score = .7 * post + .3 * pre if pre is not None else post
            score += .10 * length / MUSIC_MAX_S + (.03 if (je - js) % 4 == 0 else 0) + (.02 if (je - js) % 8 == 0 else 0)
            candidates.append((score, js, je, post, pre))
    if not candidates:
        raise RuntimeError("no bar-aligned loop of %.0f-%.0f s inside the usable part of the track" % (MUSIC_MIN_S, MUSIC_MAX_S))
    score, js, je, post, pre = max(candidates)

    # Sample-accurate seam: start 10 ms before the downbeat; align the end to the start by waveform correlation.
    preroll = int(.010 * ar)
    s_an = max(preroll, int(beats[bars[js]] * hop) - preroll)
    e_an = int(beats[bars[je]] * hop) - preroll
    ref = full[s_an - preroll: s_an + int(.2 * ar)]
    lag_range = int(.025 * ar)
    corr = {lag: ncorr(ref, full[e_an + lag - preroll: e_an + lag - preroll + len(ref)])
            for lag in range(-lag_range, lag_range + 1) if e_an + lag - preroll + len(ref) < len(full)}
    lag = max(corr, key=corr.get); frac = 0.0
    if lag - 1 in corr and lag + 1 in corr:
        a, b, c = corr[lag - 1], corr[lag], corr[lag + 1]
        if a - 2 * b + c < 0: frac = .5 * (a - c) / (a - 2 * b + c)
    ratio = RATE / ar
    s0 = int(round(s_an * ratio)); e0 = int(round((e_an + lag + frac) * ratio))
    X = int(min(.6, max(.25, period / fps)) * RATE)
    head_variant = e0 + X <= n
    if not head_variant: X = min(X, s0, e0)
    if X < 64: X = 0
    mono_s = [(left[s0 + i] + right[s0 + i]) * .5 for i in range(X)] if head_variant else [(left[s0 - X + i] + right[s0 - X + i]) * .5 for i in range(X)]
    mono_e = [(left[e0 + i] + right[e0 + i]) * .5 for i in range(X)] if head_variant else [(left[e0 - X + i] + right[e0 - X + i]) * .5 for i in range(X)]
    rho = max(0.0, ncorr(mono_s, mono_e))
    body = [array("d", left[s0:e0]), array("d", right[s0:e0])]
    length = e0 - s0
    for ch, src_ch in zip(body, (left, right)):
        for i in range(X):
            theta = (i + .5) / X * math.pi / 2
            a, b = math.sin(theta), math.cos(theta)
            g = 1 / math.sqrt(1 + 2 * rho * a * b)
            if head_variant: ch[i] = (src_ch[s0 + i] * a + src_ch[e0 + i] * b) * g             # post-E fades into post-S
            else: ch[length - X + i] = (src_ch[e0 - X + i] * b + src_ch[s0 - X + i] * a) * g     # pre-E fades into pre-S

    # Master: RMS target, circular look-ahead limiter to the -1 dBFS ceiling if needed.
    level = math.sqrt((sum(map(mul, body[0], body[0])) + sum(map(mul, body[1], body[1]))) / (2 * length))
    gain = MUSIC_RMS / max(level, 1e-9)
    body = [array("d", (v * gain for v in ch)) for ch in body]
    reduction = 0.0
    if max(peak(body[0]), peak(body[1])) > MUSIC_CEILING * .995:
        body, reduction = limiter(body, MUSIC_CEILING * .99)
    final_rms = math.sqrt((sum(map(mul, body[0], body[0])) + sum(map(mul, body[1], body[1]))) / (2 * length))
    w = int(.05 * RATE)
    if head_variant: natural = [(src_ch[e0 - w:e0], src_ch[e0:e0 + w]) for src_ch in (left, right)]
    else: natural = [(src_ch[s0 - w:s0], src_ch[s0:s0 + w]) for src_ch in (left, right)]
    seam = seam_metrics(body, natural)
    guard = array("d", bytes(8 * GUARD))
    changed = write_wav(out_path, [guard + body[0], guard + body[1]], loop=(GUARD, GUARD + length - 1), seed=20261101)
    lufs, true_peak = ebur128(out_path)
    if true_peak is not None and true_peak > TRUE_PEAK_LIMIT:
        g = lin(TRUE_PEAK_LIMIT - .2 - true_peak)
        body = [array("d", (v * g for v in ch)) for ch in body]
        final_rms *= g
        changed = write_wav(out_path, [guard + body[0], guard + body[1]], loop=(GUARD, GUARD + length - 1), seed=20261101) or changed
        lufs, true_peak = ebur128(out_path)
    loop_s = length / RATE
    info = {
        "file": rel(out_path), "source": rel(src), "changed": changed, "channels": 2, "sample_rate": RATE,
        "source_duration_s": round(n / RATE, 3), "tempo_bpm": round(bpm, 3), "beat_confidence": round(confidence, 2),
        "bar_s": round(bar_s, 4), "loop_start_in_source_s": round(s0 / RATE, 4), "loop_end_in_source_s": round(e0 / RATE, 4),
        "loop_bars": je - js, "loop_s": round(loop_s, 4), "clip_length_s": round((length + GUARD) / RATE, 4),
        "seam_similarity_post": round(post, 3), "seam_similarity_pre": round(pre, 3) if pre is not None else None,
        "seam_alignment_corr": round(corr[lag], 3), "crossfade_s": round(X / RATE, 3), "crossfade_corr": round(rho, 3),
        "crossfade_at": "loop head" if head_variant else "loop tail",
        "rms": round(final_rms, 4), "peak_dbfs": round(db(max(peak(body[0]), peak(body[1]))), 2),
        "limiter_max_reduction_db": reduction, "lufs_integrated": lufs, "true_peak_dbtp": true_peak,
        "guard_samples": GUARD, "smpl_loop": [GUARD, GUARD + length - 1],
    }
    info.update(seam)
    write_music_note(note_path, info, prompt_text)
    return info


def write_music_note(path, info, prompt_text):
    prompt = prompt_text.strip() if prompt_text else (
        "~120 s instrumental desert surf-rock for a sunny, toy-like post-apocalyptic arcade driving shooter: twangy reverb "
        "electric lead guitar, driving drums and bass, upbeat and adventurous, no vocals.")
    text = f"""DESERT RUN - ROAD GUNNER ARENA MUSIC
Generated with ElevenLabs Music v2.5 through the DostupAI panel at the project owner's request
(November 2026). Raw generation: {info['source']} (not imported into Unity).

Prompt summary: {prompt}

Processing (Tools/Audio/process_generated_audio.py, python3 standard library + ffmpeg; re-runnable):
- decoded to 44.1 kHz stereo float, 18 Hz high-pass (DC / subsonic removal);
- tempo from onset-envelope autocorrelation (3 bands): {info['tempo_bpm']} BPM, bar {info['bar_s']} s,
  beat-comb confidence {info['beat_confidence']};
- loop region chosen on whole bars by chroma + band-level similarity of the material around the seam:
  {info['loop_bars']} bars, {info['loop_start_in_source_s']} s -> {info['loop_end_in_source_s']} s of the source ({info['loop_s']} s);
  loop end aligned to the start by waveform correlation ({info['seam_alignment_corr']});
- equal-power crossfade of {info['crossfade_s']} s at the {info['crossfade_at']}, gain law compensated for the measured
  correlation ({info['crossfade_corr']}); seam jump/slope ratio {info['seam_jump_vs_slope_rms']}, level step {info['seam_level_step_db']} dB;
- mastered to RMS {info['rms']} with sample peak {info['peak_dbfs']} dBFS (limiter max reduction {info['limiter_max_reduction_db']} dB),
  integrated loudness {info['lufs_integrated']} LUFS, true peak {info['true_peak_dbtp']} dBTP; TPDF-dithered 16-bit PCM.

WebGL loop compatibility: {GUARD} initial silent guard samples and a RIFF smpl loop from {info['smpl_loop'][0]} to the
inclusive end {info['smpl_loop'][1]} (Unity 6000.6 AAC loop workaround, same technique as Dustlight Road).
Clip length including the guard: {info['clip_length_s']} s. Runtime: ArenaMusic on the GameSession, volume 0.30.

Rights/provenance: AI-generated music produced for this project through the owner's ElevenLabs/DostupAI access; the
service's terms for that account apply. Dustlight Road (original procedural loop) is kept unchanged as the fallback.
"""
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.is_file() or path.read_text("utf8") != text: path.write_text(text, "utf8")


# ---------------------------------------------------------------- main

def rel(path):
    try: return str(Path(path).resolve().relative_to(ROOT))
    except ValueError: return str(path)


def output(sfx_dir, name, data, cat, report, source, extra=None, loop=None):
    path = sfx_dir / (name + ".wav")
    channels = [data]
    if loop:
        channels = [array("d", bytes(8 * GUARD)) + data]
        extra = dict(extra or {}, guard_samples=GUARD, smpl_loop=[GUARD, GUARD + len(data) - 1])
    changed = write_wav(path, channels, loop=(GUARD, GUARD + len(data) - 1) if loop else None, seed=sum(map(ord, name)))
    lufs, true_peak = ebur128(path)
    if true_peak is not None and true_peak > TRUE_PEAK_LIMIT:      # inter-sample overs would clip after AAC/Vorbis decode
        g = lin(TRUE_PEAK_LIMIT - .2 - true_peak)
        data = array("d", (v * g for v in data))
        channels = [array("d", bytes(8 * GUARD)) + data] if loop else [data]
        changed = write_wav(path, channels, loop=(GUARD, GUARD + len(data) - 1) if loop else None, seed=sum(map(ord, name))) or changed
        lufs, true_peak = ebur128(path)
    if len(data) < int(.4 * RATE): lufs = None                    # EBU R128 gating needs >= 400 ms
    info = {"file": rel(path), "source": rel(source), "category": cat, "changed": changed}
    info.update(describe(data, cat, extra))
    info.update({"lufs_integrated": lufs, "true_peak_dbtp": true_peak})
    report.append(info)
    return path


def true_peak_of(data, folder):
    probe = folder / ".true_peak_probe.wav"
    write_wav(probe, [data], seed=7)
    try: return ebur128(probe)[1]
    finally: probe.unlink(missing_ok=True)


def remove_stale(sfx_dir, prefix, keep):
    for path in sorted(sfx_dir.glob(prefix + "_[0-9][0-9].wav")):
        if int(path.stem[-2:]) > keep:
            path.unlink(missing_ok=True); Path(str(path) + ".meta").unlink(missing_ok=True)


def band(spec):
    """ffmpeg filter chain of a spec or take: high-pass, optional low-pass, optional extra filters."""
    chain = ["highpass=f=%d:poles=2" % spec["hp"]]
    if spec.get("lp"): chain.append("lowpass=f=%d:poles=2" % spec["lp"])
    return chain + list(spec.get("eq", ()))


def window(x, at):
    a, b = (int(t * RATE) for t in at)
    return array("d", x[max(0, a):min(len(x), b)])


def emit_variants(name, variants, cat, src, sfx_dir, report, extra=None):
    """Writes name_01.. with one shared true-peak trim (median of the needed corrections) so the variants stay equally loud."""
    needed = [min(0.0, TRUE_PEAK_LIMIT - .2 - tp) for tp in (true_peak_of(v, sfx_dir) for v in variants) if tp is not None]
    if needed and len(needed) == len(variants):
        shared = sorted(needed)[len(needed) // 2]
        variants = [array("d", (s * lin(min(shared, c)) for s in v)) for v, c in zip(variants, needed)]
    punch = [db(loudest_rms(v, CATEGORIES[cat][2])) for v in variants]
    for i, v in enumerate(variants):
        info = {"variant": i + 1, "variants": len(variants), "variant_punch_spread_db": round(max(punch) - min(punch), 2)}
        info.update((extra or [{}] * len(variants))[i])
        output(sfx_dir, "%s_%02d" % (name, i + 1), v, cat, report, src, info)
    remove_stale(sfx_dir, name, len(variants))


def process_take(take, src, sfx_dir, report, warnings):
    cat = take["cat"]
    hit = trim(window(decode(src, 1, filters=band(take)), take["at"]), take["pre"], take["fade"], take["max_len"])
    if len(hit) < int(.02 * RATE): warnings.append("%s: take %s is empty" % (take["out"], take["at"])); return
    layer = take.get("layer")
    top = trim(window(decode(src, 1, filters=band(layer)), layer["at"]), .001, layer["fade"], layer["max_len"]) if layer else None
    variants, extra = [], []
    for index, semitones in enumerate(take.get("pitches", (0,))):
        y = array("d", hit)
        gain_db = layer["gains_db"][index % len(layer["gains_db"])] if layer else None
        if top is not None and len(top) and gain_db is not None:
            g = lin(gain_db) * peak(hit) / max(peak(top), 1e-9)
            offset = int(.002 * RATE)
            if len(y) < offset + len(top): y.extend(array("d", bytes(8 * (offset + len(top) - len(y)))))
            for i, v in enumerate(top): y[offset + i] += v * g
        variants.append(normalise(repitch(y, semitones), cat))
        extra.append({"take_s": list(take["at"]), "pitch_semitones": semitones, "layer_db": gain_db})
    emit_variants(take["out"], variants, cat, src, sfx_dir, report, extra)


def process_sfx(ident, spec, src, sfx_dir, report, warnings):
    if "takes" in spec:
        for take in spec["takes"]: process_take(take, src, sfx_dir, report, warnings)
        return
    cat = spec["cat"]
    x = decode(src, 1, filters=band(spec))
    if spec.get("at"): x = window(x, spec["at"])
    if peak(x) < 1e-4: warnings.append(ident + ": raw file is silent"); return
    if "loop" in spec:
        body, info, natural = loop_body(x, spec["loop"], spec.get("max_len"))
        body = normalise(body, cat)
        info.update(seam_metrics([body], natural))
        if not info["seamless"]: warnings.append("%s: loop seam not clean (%s)" % (ident, info))
        output(sfx_dir, spec["out"], body, cat, report, src, info, loop=True)
        if spec.get("whoosh"):
            whoosh = derive_whoosh(x)
            if whoosh: output(sfx_dir, spec["whoosh"], whoosh, "whoosh", report, src, {"derived": "swept air swell from the wind"})
        return
    if spec.get("slices"):
        parts = slice_hits(x, spec["slices"], spec["gap"], spec["pre"], spec["max_len"])
        variants = [normalise(trim(p, spec["pre"], spec["fade"], spec["max_len"]), cat) for p in parts]
        variants = [v for v in variants if len(v) > int(.02 * RATE)]
        if len(variants) < spec["slices"]:
            warnings.append("%s: found %d of %d hits" % (ident, len(variants), spec["slices"]))
        emit_variants(spec["out"], variants, cat, src, sfx_dir, report)
        return
    if spec.get("split2"):
        fire, reel, prominence = split_two(x)
        for name, part in zip(spec["out"], (fire, reel)):
            y = normalise(trim(part, spec["pre"], spec["fade"], spec["max_len"]), cat)
            if len(y) > int(.02 * RATE): output(sfx_dir, name, y, cat, report, src, {"split_valley_prominence_db": round(prominence, 1)})
        return
    trimmed = trim(x, spec["pre"], spec["fade"], spec["max_len"])
    output(sfx_dir, spec["out"], normalise(trimmed, cat), cat, report, src)
    if spec.get("crate_hit"):
        output(sfx_dir, spec["crate_hit"], derive_crate_hit(trimmed), "crate_hit", report, src, {"derived": "attack of the crate break"})


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--raw", type=Path, default=ROOT / "Tools/Audio/Generated/raw")
    parser.add_argument("--sfx-out", type=Path, default=ROOT / "Assets/RoadGunner/Audio/Arcade")
    parser.add_argument("--music-out", type=Path, default=ROOT / "Assets/RoadGunner/Audio/Music/Desert Run.wav")
    parser.add_argument("--manifest", type=Path, default=ROOT / "Tools/Audio/Generated/manifest.json")
    parser.add_argument("--only", default="", help="comma-separated raw ids to process (default: all)")
    args = parser.parse_args()
    only = {s.strip() for s in args.only.split(",") if s.strip()}
    for tool in ("ffmpeg", "ffprobe"):
        try: subprocess.run([tool, "-version"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        except (OSError, subprocess.CalledProcessError): sys.exit(tool + " is required (brew install ffmpeg)")

    report, warnings, missing, failed, music = [], [], [], [], None
    ids = list(SFX) + [MUSIC_ID]
    for ident, reason in REJECTED.items():
        if find_raw(args.raw, ident): print("skipping rejected raw %s: %s" % (ident, reason))
    for ident in ids:
        if only and ident not in only: continue
        src = find_raw(args.raw, ident)
        if not src: missing.append(ident); continue
        print("processing", ident, "<-", rel(src), flush=True)
        try:
            if ident == MUSIC_ID:
                prompt_file = args.raw / (ident + ".prompt.txt")
                prompt = prompt_file.read_text("utf8") if prompt_file.is_file() else ""
                music = process_music(src, args.music_out, args.music_out.with_name(args.music_out.stem + " - source.txt"), prompt)
                if not music["seamless"]: warnings.append("music seam not clean: " + json.dumps(music))
            else:
                process_sfx(ident, SFX[ident], src, args.sfx_out, report, warnings)
        except Exception as error:  # keep processing the rest; report clearly
            failed.append(ident); warnings.append("%s failed: %s: %s" % (ident, type(error).__name__, error))

    manifest = {"generator": "Tools/Audio/process_generated_audio.py", "sample_rate": RATE, "raw_dir": rel(args.raw),
                "sfx": report, "music": music, "missing_raw": missing, "failed": failed, "warnings": warnings}
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(manifest, indent=2) + "\n"
    if not args.manifest.is_file() or args.manifest.read_text("utf8") != text: args.manifest.write_text(text, "utf8")

    print("\n=== Road Gunner generated audio ===")
    for item in report:
        loop = " loop seam ratio %.2f" % item["seam_jump_vs_slope_rms"] if "smpl_loop" in item else ""
        punch = next((v for k, v in item.items() if k.startswith("loudest_")), None)
        loud = "%6.1f LUFS" % item["lufs_integrated"] if item["lufs_integrated"] is not None else "punch %5.1f dBFS" % punch if punch is not None else ""
        print("  %-24s %6.3fs peak %6.1f dBFS TP %5.1f dBTP rms %6.1f dBFS %s%s%s" % (
            Path(item["file"]).name, item["duration_s"], item["peak_dbfs"], item["true_peak_dbtp"] if item["true_peak_dbtp"] is not None else 0,
            item["rms_dbfs"], loud, loop, "" if item["changed"] else "  (unchanged)"))
    if music:
        print("  %-24s loop %.2fs (%d bars @ %.2f BPM) rms %.3f peak %.1f dBFS %s LUFS seam ratio %.2f" % (
            Path(music["file"]).name, music["loop_s"], music["loop_bars"], music["tempo_bpm"], music["rms"],
            music["peak_dbfs"], music["lufs_integrated"], music["seam_jump_vs_slope_rms"]))
    print("produced %d SFX files%s" % (len(report), ", music loop" if music else ", no music"))
    if missing: print("MISSING raw (re-run after they land in %s): %s" % (rel(args.raw), ", ".join(missing)))
    for w in warnings: print("WARNING:", w)
    print("manifest:", rel(args.manifest))
    print("next: Unity batch -executeMethod RoadGunner.Editor.ArcadeAudioInstaller.ApplyBatch")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
