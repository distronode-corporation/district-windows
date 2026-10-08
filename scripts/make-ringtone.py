#!/usr/bin/env python3
"""Write the app's ringtone, src/DistrictAI/Assets/Sounds/ringtone.wav.

    python3 scripts/make-ringtone.py           # rewrite the file
    python3 scripts/make-ringtone.py --check   # fail if the file is not what this writes

The ringtone is made here, from nothing but arithmetic, so the project owns it
outright and it is licensed with the rest of the repository (Apache-2.0). It is
two soft bell strikes, a fifth apart, twice, then silence: two seconds that the
app loops while a call rings here. Mono, 16-bit, 16 kHz WAV, which Windows'
media player reads with no codec installed.

It is the same ringtone as District AI for Linux's, written by the same
arithmetic (that repository's scripts/make-ringtone.py), so the two apps ring
alike.

The output is the same, byte for byte, on every run on one machine, so --check
can hold the committed file to this script.

Python 3.11 or newer, standard library only.
"""

from __future__ import annotations

import argparse
import io
import math
import struct
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TARGET = ROOT / "src" / "DistrictAI" / "Assets" / "Sounds" / "ringtone.wav"

RATE = 16_000
LENGTH = 2.0
PEAK = 0.45
# (start in seconds, pitch in hertz): A5 then E5, and again.
STRIKES = ((0.00, 880.0), (0.20, 659.25), (0.60, 880.0), (0.80, 659.25))
DECAY = 7.0  # per second: a strike fades to under 1% in about 0.65 s
ATTACK = 0.004  # seconds of fade-in, so no strike starts with a click


def sample(t: float) -> float:
    total = 0.0
    for start, pitch in STRIKES:
        age = t - start
        if age < 0:
            continue
        envelope = math.exp(-DECAY * age) * min(1.0, age / ATTACK)
        # The fundamental and a quieter octave, for a bell rather than a beep.
        total += envelope * (
            math.sin(2 * math.pi * pitch * age) + 0.3 * math.sin(4 * math.pi * pitch * age)
        )
    return total


def render() -> bytes:
    frames = int(RATE * LENGTH)
    raw = [sample(n / RATE) for n in range(frames)]
    scale = PEAK / max(abs(value) for value in raw)
    # The last 50 ms fade out, so the loop joins without a click.
    tail = int(RATE * 0.05)
    pcm = bytearray()
    for n, value in enumerate(raw):
        fade = min(1.0, (frames - n) / tail)
        pcm += struct.pack("<h", round(value * scale * fade * 32767))
    out = io.BytesIO()
    with wave.open(out, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(bytes(pcm))
    return out.getvalue()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    data = render()
    if args.check:
        same = TARGET.exists() and TARGET.read_bytes() == data
        print("ringtone is current" if same else f"{TARGET.relative_to(ROOT)} is STALE")
        return 0 if same else 1
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_bytes(data)
    print(f"wrote {TARGET.relative_to(ROOT)} ({len(data)} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
