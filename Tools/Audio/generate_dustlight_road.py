#!/usr/bin/env python3
"""Render Road Gunner's ORIGINAL Dustlight Road loop. Python standard library only.

No samples, recordings, soundfonts, external scores or third-party media are read.
All musical decisions, oscillators, noise and percussion are specified below.
Re-run from any working directory; output is deterministic for this source/seed.
"""
from array import array
from pathlib import Path
import hashlib
import json
import math
import random
import struct
import sys
import wave

RATE = 32000
SECONDS = 48
FRAMES = RATE * SECONDS
GUARD = 2048
SEED = 20261025
TAU = 2 * math.pi
OUTPUT = Path(__file__).resolve().parents[2] / "Assets/RoadGunner/Audio/Music/Dustlight Road.wav"


def pitch(note):
    return 440 * 2 ** ((note - 69) / 12)


def periodic_pitch(note):
    # Integer cycle counts make every sustained oscillator continuous at the seam.
    return round(pitch(note) * SECONDS) / SECONDS


def main():
    rng = random.Random(SEED)
    left, right = array("d", [0]) * FRAMES, array("d", [0]) * FRAMES

    # Four broad, overlapping suspended/add-nine colors over a gentle D pedal.
    # D-A-E / F-C-G / C-G-D / G-D-A: warm open intervals, without horror clusters.
    chords = [(6, (50, 57, 64)), (18, (53, 60, 67)), (30, (48, 55, 62)), (42, (43, 50, 57))]
    for center, notes in chords:
        for voice, note in enumerate(notes):
            frequency = periodic_pitch(note)
            pan = (-.42, .12, .43)[voice]
            gain_l, gain_r = math.sqrt((1 - pan) / 2), math.sqrt((1 + pan) / 2)
            phase = rng.random() * TAU
            for index in range(FRAMES):
                t = index / RATE
                distance = (t - center + SECONDS / 2) % SECONDS - SECONDS / 2
                if abs(distance) >= 12:
                    continue
                envelope = .5 + .5 * math.cos(math.pi * distance / 12)
                breath = .90 + .10 * math.sin(TAU * 2 * t / SECONDS + voice)
                theta = TAU * frequency * t + phase
                tone = math.sin(theta) + .16 * math.sin(theta * 2 + .2) + .045 * math.sin(theta * 3)
                value = tone * envelope * breath * .026
                left[index] += value * gain_l
                right[index] += value * gain_r

    pedal = periodic_pitch(38)
    for index in range(FRAMES):
        t = index / RATE
        amplitude = .016 * (.88 + .12 * math.sin(TAU * t / SECONDS))
        value = math.sin(TAU * pedal * t) * amplitude
        left[index] += value; right[index] += value

    # Seeded wind, filtered into a soft band. A warm-up pass finds periodic filter
    # state, so there is no artificial fade-to-silence between musical cycles.
    noise = array("f", (rng.uniform(-1, 1) for _ in range(FRAMES)))
    fast_a, slow_a = math.exp(-TAU * 700 / RATE), math.exp(-TAU * 125 / RATE)
    fast_l = slow_l = fast_r = slow_r = 0.0
    for _ in range(2):
        for index, white in enumerate(noise):
            delayed = noise[(index + 1597) % FRAMES]
            fast_l = fast_a * fast_l + (1 - fast_a) * white
            slow_l = slow_a * slow_l + (1 - slow_a) * white
            fast_r = fast_a * fast_r + (1 - fast_a) * delayed
            slow_r = slow_a * slow_r + (1 - slow_a) * delayed
            if _ == 1:
                envelope = .035 * (.72 + .28 * math.sin(TAU * 3 * index / FRAMES + .6))
                left[index] += (fast_l - slow_l) * envelope
                right[index] += (fast_r - slow_r) * envelope

    # Original sparse wooden/metallic plucks; soft attack and wrapped stereo echoes.
    melody = [(1.5, 62), (7.5, 69), (10.5, 64), (16.5, 67), (22.5, 65),
              (28.5, 62), (31.5, 72), (37.5, 67), (43.5, 64), (46.5, 69)]
    for number, (onset, note) in enumerate(melody):
        frequency = pitch(note)
        pan = -.38 if number % 2 == 0 else .38
        gain_l, gain_r = math.sqrt((1 - pan) / 2), math.sqrt((1 + pan) / 2)
        start = round(onset * RATE)
        for offset in range(round(3.7 * RATE)):
            age = offset / RATE
            envelope = (1 - math.exp(-age / .018)) * math.exp(-age / 1.1) * (.5 + .5 * math.cos(math.pi * age / 3.7))
            tone = math.sin(TAU * frequency * age) + .22 * math.sin(TAU * frequency * 2 * age) * math.exp(-age * 3)
            value = tone * envelope * .029
            i = (start + offset) % FRAMES
            left[i] += value * gain_l; right[i] += value * gain_r
            echo = (i + round(.53 * RATE)) % FRAMES
            left[echo] += value * gain_r * .25; right[echo] += value * gain_l * .25
            echo = (i + round(.91 * RATE)) % FRAMES
            left[echo] += value * gain_l * .12; right[echo] += value * gain_r * .12

    # Restrained low thumps and brushed mechanical taps; no heavy combat rhythm.
    for onset in (0, 9, 18, 24, 33, 42):
        for offset in range(round(.45 * RATE)):
            age = offset / RATE
            envelope = (1 - math.exp(-age / .005)) * math.exp(-age * 13) * (.5 + .5 * math.cos(math.pi * age / .45))
            phase = TAU * (51 * age + 1.8 * (1 - math.exp(-age * 28)))
            value = math.sin(phase) * envelope * .018
            i = (round(onset * RATE) + offset) % FRAMES
            left[i] += value; right[i] += value
    for number in range(12):
        start = round((number * 3.75 + 1.125) * RATE)
        for offset in range(round(.14 * RATE)):
            age = offset / RATE
            envelope = math.sin(math.pi * age / .14) ** 2 * math.exp(-age * 28)
            value = (noise[(start + offset) % FRAMES] * .55 + math.sin(TAU * 780 * age) * .25) * envelope * .006
            i = (start + offset) % FRAMES
            left[i] += value * (.8 if number % 2 else .4)
            right[i] += value * (.4 if number % 2 else .8)

    # Quiet mastering leaves substantial headroom for the existing guns and engine.
    means = [sum(channel) / FRAMES for channel in (left, right)]
    square_sum, peak = 0.0, 0.0
    for index in range(FRAMES):
        left[index] -= means[0]; right[index] -= means[1]
        square_sum += left[index] ** 2 + right[index] ** 2
        peak = max(peak, abs(left[index]), abs(right[index]))
    rms = math.sqrt(square_sum / (FRAMES * 2))
    gain = min(.085 / rms, .60 / peak)
    pcm = array("h", [0]) * (2 * (FRAMES + GUARD))
    for index in range(FRAMES):
        pcm[(index + GUARD) * 2] = round(max(-1, min(1, left[index] * gain)) * 32767)
        pcm[(index + GUARD) * 2 + 1] = round(max(-1, min(1, right[index] * gain)) * 32767)
    if sys.byteorder != "little": pcm.byteswap()

    # Unity's documented AAC loop workaround: encoder guard + RIFF sampler loop.
    # Loop start skips guard; loop end is inclusive. No imported/external samples.
    fmt = struct.pack("<HHIIHH", 1, 2, RATE, RATE * 4, 4, 16)
    smpl = struct.pack("<9I", 0, 0, round(1_000_000_000 / RATE), 50, 0, 0, 0, 1, 0)
    smpl += struct.pack("<6I", 0, 0, GUARD, GUARD + FRAMES - 1, 0, 0)
    def chunk(name, data): return name + struct.pack("<I", len(data)) + data + (b"\0" if len(data) % 2 else b"")
    body = b"WAVE" + chunk(b"fmt ", fmt) + chunk(b"smpl", smpl) + chunk(b"data", pcm.tobytes())
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(b"RIFF" + struct.pack("<I", len(body)) + body)
    seam = max(abs(left[0] - left[-1]), abs(right[0] - right[-1])) * gain
    assert seam < .02, "Unexpected PCM discontinuity at the musical loop seam"
    with wave.open(str(OUTPUT), "rb") as check:
        assert check.getnframes() == FRAMES + GUARD and check.getnchannels() == 2
    print(json.dumps({"file": str(OUTPUT), "musical_seconds": SECONDS, "guard_samples": GUARD,
                      "sample_rate": RATE, "channels": 2, "rms": rms * gain, "peak": peak * gain,
                      "musical_seam_delta": seam, "bytes": OUTPUT.stat().st_size,
                      "sha256": hashlib.sha256(OUTPUT.read_bytes()).hexdigest()}, indent=2))


if __name__ == "__main__": main()
