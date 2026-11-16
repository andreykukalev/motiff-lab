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


class Lcg:
    """A fixed linear congruential generator, so the "played" fixtures never depend on Python's random module."""

    def __init__(self, seed):
        self.state = seed

    def next(self):
        self.state = (self.state * 1103515245 + 12345) % 2**31
        return self.state / 2**31

    def offset(self, spread):
        """Roughly bell-shaped in [-spread, spread]: the mean of three uniform draws."""
        return round((self.next() + self.next() + self.next() - 1.5) / 1.5 * spread)


def notes_to_events(notes, channel=0):
    """notes: (pitch, on, off, velocity) in absolute ticks. Note-offs sort before note-ons at a tick."""
    events = []
    for pitch, on, off, velocity in notes:
        events.append((on, 1, bytes([0x90 | channel, pitch, velocity])))
        events.append((off, 0, bytes([0x80 | channel, pitch, 0])))
    events.sort(key=lambda e: (e[0], e[1]))
    return [(tick, raw) for tick, _, raw in events]


def time_signature(numerator, denominator_power):
    return meta(0x58, bytes([numerator, denominator_power, 24, 8]))


def piano_single_track():
    """S1-I4, AC-S1-04: type 0, PPQ 480, both hands of a piano piece on one track and channel.

    4/4, C major, 84 BPM, eight bars, quantised. The right hand plays a melody, the left
    hand a whole-bar bass note and a chord tone on beats 2 to 4. The hands overlap in
    register: in bars 5 and 6 the left hand climbs to E4 while the right hand drops to E4,
    so a fixed split point misassigns notes. The two hands never hold the same key at once. The intended hand of every note is in piano-single-track.hands.txt.
    """
    q = 480
    bar = 4 * q
    # Right hand per bar: (pitch, offset in eighths, length in eighths).
    melody = [
        [(72, 0, 4), (74, 4, 2), (76, 6, 2)],
        [(77, 0, 3), (76, 3, 1), (74, 4, 4)],
        [(72, 0, 2), (71, 2, 2), (69, 4, 2), (67, 6, 2)],
        [(74, 0, 8)],
        [(64, 0, 2), (65, 2, 2), (67, 4, 4)],
        [(65, 0, 2), (64, 2, 2), (65, 4, 2), (64, 6, 2)],
        [(67, 0, 2), (72, 2, 2), (71, 4, 2), (74, 6, 2)],
        [(72, 0, 8)],
    ]
    left_chords = [(48, 52, 55), (53, 57, 60), (50, 53, 57), (55, 59, 62),
                   (57, 60, 64), (55, 59, 62), (53, 57, 60), (48, 55, 64)]
    notes = []
    hands = []
    for b in range(8):
        start = b * bar
        for pitch, at, length in melody[b]:
            notes.append((pitch, start + at * q // 2, start + (at + length) * q // 2, 88))
            hands.append((start + at * q // 2, pitch, "R"))
        root, third, fifth = left_chords[b]
        bass = root - 12
        notes.append((bass, start, start + bar, 70))
        hands.append((start, bass, "L"))
        for beat, pitch in enumerate([third, fifth, third + 12 if b < 4 else third], start=1):
            on = start + beat * q
            notes.append((pitch, on, on + q, 56))
            hands.append((on, pitch, "L"))
    events = [(0, name("Piano")), (0, tempo(84)), (0, time_signature(4, 2)), (0, meta(0x59, bytes([0, 0]))),
              (0, bytes([0xC0, 0]))]
    events += notes_to_events(notes)
    events.append((8 * bar, end()))
    hands.sort()
    return smf(0, q, [track(events)]), hands


def performance_click():
    """S1-R1, AC-S1-05: type 1, PPQ 480, a performance played to a 96 BPM click.

    The tempo map and 4/4 meter fit the playing: every onset is an 8th or 16th grid point
    plus a deterministic human offset of up to ±40 ticks (about ±50 ms), with legato
    lengths and varied velocities. Eight bars and a beat of release; right hand on
    track 2, left hand on track 3. There is no click track in the file.
    """
    q = 480
    bar = 4 * q
    rng = Lcg(7)
    # Right hand: a 16th-note figure per beat, with 8th-note beats in between.
    figures = [[0, 2, 4, 7], [7, 4], [5, 4, 2, 0], [2, 4]]
    scale = [60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79]
    right = []
    for b in range(8):
        for beat in range(4):
            figure = figures[(b + beat) % 4]
            step = q // len(figure)
            for i, degree in enumerate(figure):
                pitch = scale[(degree + b) % len(scale)]
                on = b * bar + beat * q + i * step
                right.append((pitch, on, step))
    left = []
    roots = [48, 45, 41, 43, 48, 45, 41, 43]
    for b, root in enumerate(roots):
        for beat in range(4):
            left.append((root if beat % 2 == 0 else root + 7, b * bar + beat * q, q))

    def play(written, base_velocity):
        played = []
        for pitch, on, length in written:
            start = max(0, on + rng.offset(40))
            off = start + max(30, round(length * (0.85 + 0.1 * rng.next())))
            played.append([pitch, start, off, base_velocity + rng.offset(12)])
        # A key is released before it is struck again.
        for i, note in enumerate(played):
            again = next((other for other in played[i + 1:] if other[0] == note[0]), None)
            if again is not None:
                note[2] = min(note[2], again[1] - 10)
        return [tuple(note) for note in played]

    conductor = track([(0, name("Played to a click")), (0, tempo(96)), (0, time_signature(4, 2)),
                       (8 * bar + q, end())])
    rh = [(0, name("Right hand"))] + notes_to_events(play(right, 84))
    rh.append((8 * bar + q, end()))
    lh = [(0, name("Left hand"))] + notes_to_events(play(left, 64), channel=1)
    lh.append((8 * bar + q, end()))
    return smf(1, q, [conductor, track(rh), track(lh)])


def free_tempo():
    """S1-R2, AC-S1-06: type 0, PPQ 480, a rubato performance recorded without a click.

    The file declares 120 BPM and 4/4 (a sequencer's defaults), but the player's beat
    drifts between about 0.45 s and 1.3 s, slowing hard at each phrase end, so onsets do
    not line up with the declared beats at any common grid. Written as eight bars of
    quarters and eighths with a bass note per half bar; played times are the written
    beats mapped through a smooth tempo curve, plus a small human offset.
    """
    q = 480  # at 120 BPM a quarter is 0.5 s, so one second is 960 ticks
    rng = Lcg(11)
    melody = [(67, 2), (69, 1), (71, 1), (72, 2), (71, 2), (69, 3), (67, 1), (64, 4),
              (65, 2), (67, 1), (69, 1), (71, 2), (72, 2), (74, 3), (72, 1), (72, 4),
              (76, 2), (74, 1), (72, 1), (71, 2), (69, 2), (67, 2), (65, 2), (64, 4),
              (62, 2), (64, 1), (65, 1), (67, 2), (71, 2), (72, 8)]
    # Beat length in seconds per written beat (eighth units / 2), accelerating then easing off.
    def beat_seconds(beat):
        phrase = beat % 8
        return 0.45 + 0.1 * abs(4 - phrase) + (0.55 if phrase >= 7 else 0.0) + 0.05 * ((beat * 7) % 3)

    def seconds_at(eighths):
        whole, half = divmod(eighths, 2)
        return sum(beat_seconds(b) for b in range(whole)) + half * beat_seconds(whole) / 2

    notes = []
    position = 0
    for pitch, length in melody:
        on = round(seconds_at(position) * 960) + rng.offset(20)
        off = round(seconds_at(position + length) * 960) - 30
        notes.append((pitch, max(0, on), off, 76 + rng.offset(14)))
        position += length
    for half_bar in range(position // 4):
        root = [48, 43, 45, 41, 48, 43, 41, 43, 48, 48][half_bar % 10]
        on = round(seconds_at(half_bar * 4) * 960) + rng.offset(25)
        off = round(seconds_at(half_bar * 4 + 4) * 960) - 20
        notes.append((root, max(0, on), off, 58 + rng.offset(8)))
    last = max(off for _, _, off, _ in notes)
    events = [(0, name("Rubato")), (0, tempo(120)), (0, time_signature(4, 2))] + notes_to_events(notes)
    events.append((last + q, end()))
    return smf(0, q, [track(events)])


def drums_channel10():
    """S1-I2: type 0, PPQ 480, piano and drums interleaved on one track.

    4/4 at 110 BPM, two bars. Channel 1 has 12 piano notes (a bass line and chords);
    channel 10 has 24 drum hits (kick, snare and closed hi-hat). The drum notes must be
    left out with a notice; the piano notes stay.
    """
    q = 480
    bar = 4 * q
    piano = []
    for b, (root, chord) in enumerate([(36, (60, 64, 67)), (41, (60, 65, 69))]):
        start = b * bar
        piano += [(root, start, start + 2 * q, 80), (root + 7, start + 2 * q, start + bar, 76)]
        for beat in (1, 3):
            piano += [(pitch, start + beat * q, start + beat * q + q // 2, 66) for pitch in chord[:2]]
    drums = []
    for b in range(2):
        for eighth in range(8):
            on = b * bar + eighth * q // 2
            drums.append((42, on, on + 60, 70))
            if eighth % 4 == 0:
                drums.append((36, on, on + 60, 100))
            if eighth % 4 == 2:
                drums.append((38, on, on + 60, 96))
    events = [(0, name("Groove")), (0, tempo(110)), (0, time_signature(4, 2))]
    merged = [(t, raw) for t, raw in notes_to_events(piano)] + [(t, raw) for t, raw in notes_to_events(drums, channel=9)]
    merged.sort(key=lambda e: (e[0], e[1][0] & 0xF0 != 0x80))
    events += merged
    events.append((2 * bar, end()))
    return smf(0, q, [track(events)])


def no_time_signature():
    """S1-I3: type 1, PPQ 240, quantised notes with a tempo but no time signature.

    The tune is a waltz (three beats a bar, 76 BPM, D major key signature), but the file
    has no time signature event, so the importer must assume 4/4 and say so. 24 notes over
    eight three-beat bars (24 quarters), which is six bars of the assumed 4/4.
    """
    q = 240
    tune = [62, 66, 69, 74, 69, 66, 64, 67, 71, 73, 71, 67, 66, 69, 74, 78, 74, 69, 64, 67, 73, 74, 74, 74]
    lengths = [1] * 21 + [1, 1, 1]
    notes = []
    position = 0
    for pitch, length in zip(tune, lengths):
        notes.append((pitch, position * q, (position + length) * q, 78 if position % 3 == 0 else 62))
        position += length
    conductor = track([(0, name("Waltz without a meter")), (0, tempo(76)), (0, meta(0x59, bytes([2, 0]))),
                       (position * q, end())])
    melody = [(0, name("Melody"))] + notes_to_events(notes)
    melody.append((position * q, end()))
    return smf(1, q, [conductor, track(melody)])


def large_5000():
    """S1-I1, AC-S1-01: type 1, PPQ 480, exactly 5,000 notes for the import performance target.

    120 BPM, 4/4, C major, 200 bars (6 min 40 s). Track 1 is the conductor track, track 2
    the right hand (16th-note arpeggios: 3,200 notes) and track 3 the left hand (8th-note
    broken chords and a whole-bar bass note: 1,800 notes). Quantised, so nothing is rounded.
    """
    q = 480
    bar = 4 * q
    bars = 200
    progression = [(60, 64, 67), (57, 60, 64), (53, 57, 60), (55, 59, 62)]
    right = []
    left = []
    for b in range(bars):
        chord = progression[(b // 2) % 4]
        lift = 12 * ((b // 16) % 2)
        arpeggio = [chord[0], chord[1], chord[2], chord[0] + 12]
        for i in range(16):
            pitch = arpeggio[i % 4 if (b + i // 4) % 2 == 0 else 3 - i % 4] + lift
            on = b * bar + i * q // 4
            right.append((pitch, on, on + q // 4, 70 + (i % 4 == 0) * 14 + (b % 4)))
        for i in range(8):
            pitch = chord[[0, 2, 1, 2][i % 4]] - 12
            on = b * bar + i * q // 2
            left.append((pitch, on, on + q // 2, 58 + (i % 2 == 0) * 8))
        left.append((chord[0] - 24, b * bar, (b + 1) * bar, 72))
    conductor = track([(0, name("Five thousand notes")), (0, tempo(120)), (0, time_signature(4, 2)),
                       (0, meta(0x59, bytes([0, 0]))), (bars * bar, end())])
    rh = [(0, name("Right hand"))] + notes_to_events(right)
    rh.append((bars * bar, end()))
    lh = [(0, name("Left hand"))] + notes_to_events(left, channel=1)
    lh.append((bars * bar, end()))
    assert len(right) + len(left) == 5000
    return smf(1, q, [conductor, track(rh), track(lh)])


FIXTURES = {
    "scale-type0.mid": scale_type0,
    "two-hands-type1.mid": two_hands_type1,
    "performance-type1.mid": performance_type1,
    "performance-click.mid": performance_click,
    "free-tempo.mid": free_tempo,
    "drums-channel10.mid": drums_channel10,
    "no-time-signature.mid": no_time_signature,
    "large-5000-notes.mid": large_5000,
}

if __name__ == "__main__":
    for file_name, make in FIXTURES.items():
        with open(file_name, "wb") as f:
            f.write(make())
    piano, hands = piano_single_track()
    with open("piano-single-track.mid", "wb") as f:
        f.write(piano)
    with open("piano-single-track.hands.txt", "w", encoding="utf-8", newline="\n") as f:
        f.write("# Intended hand of each note in piano-single-track.mid: MIDI tick, pitch, R or L.\n")
        f.write("# Sorted by tick, then pitch. Written with the file; use it to score hand inference (S1-I4).\n")
        f.writelines(f"{tick} {pitch} {hand}\n" for tick, pitch, hand in hands)
