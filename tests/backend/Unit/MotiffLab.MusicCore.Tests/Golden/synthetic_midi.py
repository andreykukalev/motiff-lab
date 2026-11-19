"""Replaces the MIDI `source` of a project file with a synthetic one, keeping every other byte (task #293).

The two version 12 golden files are projects Andrei made from transcriptions of copyrighted recordings. The
repository is public, so their MIDI is replaced with this one before they are committed: a I-V-vi-IV pattern in C
major at 120 BPM in 4/4, in the original's ticks per quarter: every four beats from the original's first note, the bass
and two notes above it an eighth apart. It lasts as long as the original, so that the bars worked out for a
transcription still fit, and the first three notes fall in bar 1 as the original's did under the saved motif.
Nothing of the original but its length, its ticks per quarter and its first onset is kept.

Usage: python3 synthetic_midi.py <file.motiff.json>   (rewrites the file in place; running it again changes nothing)
"""

from __future__ import annotations

import base64
import re
import struct
import sys
from pathlib import Path

# I, V, vi, IV in C major: the bass and two notes above it.
CHORDS = [(48, 64, 67), (43, 62, 67), (45, 64, 69), (41, 65, 69)]


def variable(value: int) -> bytes:
    out = [value & 0x7F]
    value >>= 7
    while value:
        out.append((value & 0x7F) | 0x80)
        value >>= 7
    return bytes(reversed(out))


def track(events: list[tuple[int, bytes]]) -> bytes:
    data, now = b"", 0
    for tick, event in sorted(events, key=lambda pair: (pair[0], pair[1][0] & 0xF0 == 0x90)):
        data += variable(tick - now) + event
        now = tick
    data += variable(0) + b"\xff\x2f\x00"
    return b"MTrk" + struct.pack(">I", len(data)) + data


def synthetic(original: bytes) -> bytes:
    _, _, quarter = struct.unpack(">HHH", original[8:14])
    first, last = span(original)
    bar = 4 * quarter
    notes: list[tuple[int, bytes]] = [(0, b"\xc0\x00")]
    for index, start in enumerate(range(first, last, bar)):
        bass, middle, top = CHORDS[index % len(CHORDS)]
        for eighths, pitch, velocity in ((0, bass, 64), (1, middle, 72), (2, top, 80)):
            on = start + eighths * quarter // 2
            notes.append((on, bytes([0x90, pitch, velocity])))
            notes.append((start + 3 * quarter, bytes([0x80, pitch, 0])))
        notes.append((start, b"\xb0\x40\x7f"))
        notes.append((start + bar - quarter // 4, b"\xb0\x40\x00"))
    conductor = track([(0, b"\xff\x51\x03\x07\xa1\x20"), (0, b"\xff\x58\x04\x04\x02\x18\x08")])
    return b"MThd" + struct.pack(">IHHH", 6, 1, 2, quarter) + conductor + track(notes)


def span(midi: bytes) -> tuple[int, int]:
    """The first note-on and the last event of a MIDI file, in its own ticks."""
    first, last, at = None, 0, 14
    while at < len(midi):
        length = struct.unpack(">I", midi[at + 4:at + 8])[0]
        data, at = midi[at + 8:at + 8 + length], at + 8 + length
        i = tick = 0
        status = 0
        while i < len(data):
            delta = 0
            while True:
                byte = data[i]
                i += 1
                delta = (delta << 7) | (byte & 0x7F)
                if byte < 0x80:
                    break
            tick += delta
            if data[i] == 0xFF:
                i += 2
                size = 0
                while True:
                    byte = data[i]
                    i += 1
                    size = (size << 7) | (byte & 0x7F)
                    if byte < 0x80:
                        break
                i += size
            else:
                if data[i] & 0x80:
                    status = data[i]
                    i += 1
                count = 1 if status & 0xF0 in (0xC0, 0xD0) else 2
                if status & 0xF0 == 0x90 and data[i + 1] > 0 and first is None:
                    first = tick
                i += count
            last = max(last, tick)
    return first or 0, last


def main() -> None:
    path = Path(sys.argv[1])
    text = path.read_text(encoding="utf-8")
    match = re.search(r'"source": "([A-Za-z0-9+/=]*)"', text)
    assert match, "no source"
    replaced = base64.b64encode(synthetic(base64.b64decode(match.group(1)))).decode("ascii")
    path.write_text(text[:match.start(1)] + replaced + text[match.end(1):], encoding="utf-8", newline="")


if __name__ == "__main__":
    main()
