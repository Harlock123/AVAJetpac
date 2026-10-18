#!/usr/bin/env python3
"""Synthesises AVAJetpac's sound effects and tunes into ../src/AVAJetpac/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, saw and noise voices. The tunes are
new compositions. The countdown call is spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAJetpac", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Laser.wav", tone(0.18, 2600, 500, "square", attack=0.001, release=0.4, gain=0.5), 0.12)
    write("Pop.wav", mix(tone(0.2, 1200, 100, "noise", attack=0.001, release=1.0), tone(0.12, 400, 120, "square", release=1.0, gain=0.5)), 0.3)
    write("Pick.wav", cat(tone(0.04, 600, kind="square", release=0.4), tone(0.06, 900, kind="square", release=0.6)), 0.16)
    write("Drop.wav", tone(0.25, 900, 300, "tri", attack=0.002, release=0.8), 0.18)
    write("Fit.wav", cat(*[tone(0.05, hz(n), kind="square", release=0.4) for n in ["C5", "G5", "C6"]]), 0.22)
    write("Ready.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6", "C7"]]), 0.26)
    write("Gem.wav", cat(*[tone(0.04, hz(n), kind="tri", release=0.3) for n in ["E6", "B6", "E7", "B7"]]), 0.22)
    write("Die.wav", cat(*[tone(0.035, 1600 - i * 60, kind="square", release=0.2, gain=0.6) for i in range(24)]), 0.32)
    write("Launch.wav", mix(tone(3.0, 80, 400, "noise", attack=0.3, release=0.6), tone(3.0, 50, 120, "saw", attack=0.3, release=0.6, gain=0.4)), 0.42)
    write("Extra.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["G5", "C6", "E6", "G6"]]), 0.24)
    words = "L IH1 F T , AO1 F !"
    write("SayLaunch.wav", mix(say(words, rate=RATE, pitch=100, speed=1.0, size=1.05), [s * 0.3 for s in say(words, rate=RATE, pitch=150, speed=1.0, size=1.05, seed=11)]), 0.5)
    # Out in space: a spacey tune of its own, over a pulsing bass.
    e = 0.5
    lead = [("E5", e), ("G5", e), ("B5", 1), ("A5", e), ("G5", e), ("E5", 1), ("D5", e), ("E5", e), ("G5", 1), ("E5", 2),
            ("E5", e), ("G5", e), ("B5", 1), ("D6", e), ("B5", e), ("A5", 1), ("G5", e), ("A5", e), ("B5", 1), ("E5", 2)]
    bass = [("E2", e), ("E3", e)] * 8 + [("C2", e), ("C3", e)] * 4 + [("D2", e), ("D3", e)] * 4
    write("Music.wav", tune(126, lead, bass + bass[:4], kind="tri"), 0.26)
    title = [("E5", 0.5), ("B5", 0.5), ("E6", 1), ("D6", 0.5), ("B5", 0.5), ("G5", 1), ("A5", 0.5), ("B5", 0.5), ("D6", 1), ("B5", 3)]
    write("TitleTune.wav", cat(tune(110, title, [("E3", 2), ("C3", 2), ("D3", 2), ("E3", 3)], kind="pulse"), silence(1.0)), 0.32)
    write("Victory.wav", tune(150, [("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 1.5), ("D6", 0.5), ("E6", 0.5), ("F#6", 0.5), ("G6", 3)],
                              [("E3", 1), ("B2", 1), ("C3", 1), ("A2", 1), ("B2", 1), ("E3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("E4", 1), ("A4", 3)], [("A2", 2), ("F2", 2), ("A1", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
