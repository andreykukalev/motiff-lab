"""Writes the MIDI test fixtures in this folder.

Every note here was written for these tests; none of it comes from published music.
Run from this folder with `python3 make_fixtures.py`. The output is byte-for-byte
deterministic, and the tests pin what each file must import as.
"""


def vlq(value):
    out = [value & 0x7F]
    value >>= 7
    while value:
        out.insert(0, (value & 0x7F) | 0x80)
        value >>= 7
    return bytes(out)


def chunk(chunk_id, body):
    return chunk_id + len(body).to_bytes(4, "big") + body


def smf(fmt, ppq, tracks):
    header = chunk(b"MThd", fmt.to_bytes(2, "big") + len(tracks).to_bytes(2, "big") + ppq.to_bytes(2, "big"))
    return header + b"".join(chunk(b"MTrk", t) for t in tracks)


def track(events):
    """events: list of (absolute_tick, raw_bytes); written in the given order as delta times."""
    out = b""
    last = 0
    for tick, raw in events:
        out += vlq(tick - last) + raw
        last = tick
    return out


def meta(kind, data):
    return bytes([0xFF, kind]) + vlq(len(data)) + data


def name(text):
    return meta(0x03, text.encode("utf-8"))


def tempo(bpm):
    return meta(0x51, (60_000_000 // bpm).to_bytes(3, "big"))


def end():
    return meta(0x2F, b"")


def scale_type0():
    """Type 0, PPQ 96: C major scale up and down in quarter notes at 100 BPM, 4/4, C major.

    Uses running status throughout and ends notes with velocity-zero note-ons.
    """
    ppq = 96
    events = [
        (0, name("Scale study")),
        (0, tempo(100)),
        (0, meta(0x58, bytes([4, 2, 24, 8]))),
        (0, meta(0x59, bytes([0, 0]))),
    ]
    pitches = [60, 62, 64, 65, 67, 69, 71, 72, 71, 69, 67, 65, 64, 62, 60]
    first = True
    for i, pitch in enumerate(pitches):
        on = i * ppq
        events.append((on, (bytes([0x90]) if first else b"") + bytes([pitch, 80])))
        first = False
        events.append((on + ppq, bytes([pitch, 0])))
    events.append((len(pitches) * ppq, end()))
    return smf(0, ppq, [track(events)])


def two_hands_type1():
    """Type 1, PPQ 480: a conductor track, right hand, left hand and a drum track.

    3/4 in G major at 90 BPM, slowing to 72 BPM at bar 3. Four bars. The left hand uses
    the sustain pedal and a program change; the drum track is on channel 10.
    """
    q = 480
    bar = 3 * q
    conductor = track([
        (0, name("Waltz sketch")),
        (0, tempo(90)),
        (0, meta(0x58, bytes([3, 2, 24, 8]))),
        (0, meta(0x59, bytes([1, 0]))),
        (2 * bar, tempo(72)),
        (4 * bar, end()),
    ])

    right = [(0, name("Right hand")), (0, bytes([0xC0, 0]))]
    melody = [(67, 0, 2 * q), (71, 2 * q, q), (74, bar, bar),
              (72, 2 * bar, q), (71, 2 * bar + q, q), (69, 2 * bar + 2 * q, q), (67, 3 * bar, bar)]
    for pitch, on, length in melody:
        right.append((on, bytes([0x90, pitch, 90])))
        right.append((on + length, bytes([0x80, pitch, 64])))
    right.sort(key=lambda e: e[0])
    right.append((4 * bar, end()))

    left = [(0, name("Left hand")), (0, bytes([0xC1, 0]))]
    for b, (root, third, fifth) in enumerate([(43, 47, 50), (43, 47, 50), (38, 42, 45), (43, 47, 50)]):
        start = b * bar
        left.append((start, bytes([0xB1, 64, 127])))
        left.append((start, bytes([0x91, root, 70])))
        left.append((start + q, bytes([0x81, root, 0])))
        for beat in (1, 2):
            on = start + beat * q
            left.append((on, bytes([0x91, third, 60])))
            left.append((on, bytes([0x91, fifth, 60])))
            left.append((on + q, bytes([0x81, third, 0])))
            left.append((on + q, bytes([0x81, fifth, 0])))
        left.append((start + bar - 1, bytes([0xB1, 64, 0])))
    left.sort(key=lambda e: e[0])
    left.append((4 * bar, end()))

    drums = [(0, name("Click"))]
    for b in range(4):
        drums.append((b * bar, bytes([0x99, 37, 100])))
        drums.append((b * bar + 60, bytes([0x89, 37, 0])))
    drums.append((4 * bar, end()))

    return smf(1, q, [conductor, track(right), track(left), track(drums)])


def performance_type1():
    """Type 1, PPQ 1000 (does not divide 480): a played phrase with ragged timing.

    No tempo or time signature, so the importer must assume 120 BPM and 4/4. The phrase
    re-strikes a key before releasing it, has a stray note-off, a zero-length note, and
    one note left hanging at the end.
    """
    events = [(0, name("Played phrase"))]
    played = [(64, 13, 470), (66, 498, 980), (67, 1007, 1990), (67, 1500, 2420), (69, 2011, 2990)]
    for pitch, on, off in played:
        events.append((on, bytes([0x90, pitch, 72])))
        events.append((off, bytes([0x80, pitch, 0])))
    events.append((2995, bytes([0x80, 50, 0])))  # note-off with no note-on
    events.append((3001, bytes([0x90, 71, 70])))
    events.append((3001, bytes([0x80, 71, 0])))  # zero-length note
    events.append((3333, bytes([0x90, 72, 65])))  # never released
    events.sort(key=lambda e: e[0])
    events.append((4000, end()))
    return smf(1, 1000, [track(events)])


FIXTURES = {
    "scale-type0.mid": scale_type0,
    "two-hands-type1.mid": two_hands_type1,
    "performance-type1.mid": performance_type1,
}

if __name__ == "__main__":
    for file_name, make in FIXTURES.items():
        with open(file_name, "wb") as f:
            f.write(make())
