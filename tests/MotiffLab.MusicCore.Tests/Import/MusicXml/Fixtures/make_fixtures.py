"""Writes the MusicXML test fixtures in this folder.

Every note here was written for these tests; none of it comes from published music.
Run from this folder with `python3 make_fixtures.py`. The output is byte-for-byte
deterministic (the .mxl archive has fixed timestamps), and README.md lists what each
file contains and what it must import as.
"""

import io
import zipfile

HEADER = (
    '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n'
    '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN" '
    '"http://www.musicxml.org/dtds/partwise.dtd">\n'
    "<!-- Written for the Motiff Lab tests; not from published music. See README.md. -->\n"
)

TYPES = {1: "16th", 2: "eighth", 4: "quarter", 8: "half", 16: "whole"}


class Score:
    """Collects measures as indented XML lines."""

    def __init__(self, title, parts):
        self.title = title
        self.parts = parts  # list of (id, name)
        self.lines = []
        self.depth = 1

    def line(self, text):
        self.lines.append("  " * self.depth + text)

    def open(self, tag):
        self.line(f"<{tag}>")
        self.depth += 1

    def close(self, tag):
        self.depth -= 1
        self.line(f"</{tag}>")

    def comment(self, text):
        self.line(f"<!-- {text} -->")

    def render(self):
        out = [HEADER + '<score-partwise version="4.0">']
        out.append(f"  <work>\n    <work-title>{self.title}</work-title>\n  </work>")
        out.append("  <part-list>")
        for part_id, part_name in self.parts:
            out.append(f'    <score-part id="{part_id}">\n      <part-name>{part_name}</part-name>\n    </score-part>')
        out.append("  </part-list>")
        out.extend(self.lines)
        out.append("</score-partwise>\n")
        return "\n".join(out)


def attributes(s, divisions, fifths, beats, beat_type, clefs):
    """clefs: list of (sign, line); more than one makes a grand staff."""
    s.open("attributes")
    s.line(f"<divisions>{divisions}</divisions>")
    s.line(f"<key>\n{'  ' * (s.depth + 1)}<fifths>{fifths}</fifths>\n{'  ' * s.depth}</key>")
    s.line(f"<time>\n{'  ' * (s.depth + 1)}<beats>{beats}</beats>\n"
           f"{'  ' * (s.depth + 1)}<beat-type>{beat_type}</beat-type>\n{'  ' * s.depth}</time>")
    if len(clefs) > 1:
        s.line(f"<staves>{len(clefs)}</staves>")
    for number, (sign, clef_line) in enumerate(clefs, start=1):
        attr = f' number="{number}"' if len(clefs) > 1 else ""
        s.line(f"<clef{attr}>\n{'  ' * (s.depth + 1)}<sign>{sign}</sign>\n"
               f"{'  ' * (s.depth + 1)}<line>{clef_line}</line>\n{'  ' * s.depth}</clef>")
    s.close("attributes")


def tempo(s, bpm):
    s.open('direction placement="above"')
    s.line(f"<direction-type>\n{'  ' * (s.depth + 1)}<metronome>\n"
           f"{'  ' * (s.depth + 2)}<beat-unit>quarter</beat-unit>\n"
           f"{'  ' * (s.depth + 2)}<per-minute>{bpm}</per-minute>\n"
           f"{'  ' * (s.depth + 1)}</metronome>\n{'  ' * s.depth}</direction-type>")
    s.line(f'<sound tempo="{bpm}"/>')
    s.close("direction")


def note(s, pitch, duration, note_type, voice=1, staff=None, chord=False, dot=False,
         tie=None, tuplet=None, tuplet_mark=None, slur=None):
    """pitch: "C5", "F#4", "Bb3" or None for a rest.

    tie: "start", "stop" or "both". tuplet: (actual, normal[, normal_type]).
    tuplet_mark: "start" or "stop". slur: list of (number, "start" | "stop").
    """
    s.open("note")
    if chord:
        s.line("<chord/>")
    if pitch is None:
        s.line("<rest/>")
    else:
        step, octave = pitch[0], pitch[-1]
        alter = {"#": 1, "b": -1}.get(pitch[1]) if len(pitch) == 3 else None
        s.open("pitch")
        s.line(f"<step>{step}</step>")
        if alter is not None:
            s.line(f"<alter>{alter}</alter>")
        s.line(f"<octave>{octave}</octave>")
        s.close("pitch")
    s.line(f"<duration>{duration}</duration>")
    ties = {"start": ["start"], "stop": ["stop"], "both": ["stop", "start"]}.get(tie, [])
    for kind in ties:
        s.line(f'<tie type="{kind}"/>')
    s.line(f"<voice>{voice}</voice>")
    s.line(f"<type>{note_type}</type>")
    if dot:
        s.line("<dot/>")
    if pitch is not None and len(pitch) == 3:
        s.line(f"<accidental>{'sharp' if pitch[1] == '#' else 'flat'}</accidental>")
    if tuplet:
        s.open("time-modification")
        s.line(f"<actual-notes>{tuplet[0]}</actual-notes>")
        s.line(f"<normal-notes>{tuplet[1]}</normal-notes>")
        if len(tuplet) > 2:
            s.line(f"<normal-type>{tuplet[2]}</normal-type>")
        s.close("time-modification")
    if staff is not None:
        s.line(f"<staff>{staff}</staff>")
    marks = [f'<tied type="{kind}"/>' for kind in ties]
    if tuplet_mark:
        marks.append(f'<tuplet type="{tuplet_mark}"/>')
    marks += [f'<slur number="{number}" type="{kind}"/>' for number, kind in slur or []]
    if marks:
        s.open("notations")
        for mark in marks:
            s.line(mark)
        s.close("notations")
    s.close("note")


def backup(s, duration):
    s.line(f"<backup>\n{'  ' * (s.depth + 1)}<duration>{duration}</duration>\n{'  ' * s.depth}</backup>")


def barline(s, location, style=None, repeat=None, ending=None):
    """ending: (number, "start" | "stop" | "discontinue")."""
    s.open(f'barline location="{location}"')
    if style:
        s.line(f"<bar-style>{style}</bar-style>")
    if ending:
        s.line(f'<ending number="{ending[0]}" type="{ending[1]}">{ending[0]}.</ending>')
    if repeat:
        s.line(f'<repeat direction="{repeat}"/>')
    s.close("barline")


def ties_triplets_pickup():
    """AC-S1-02: a one-beat pickup, ties inside and across a bar, triplet 8ths and quarters.

    4/4, G major, 88 BPM, divisions 6. Bar 0 is the pickup (implicit, so bar 1 starts
    after it at quarter 1). 20 written notes, 18 after merging the two ties.
    """
    s = Score("Pickup, ties and triplets", [("P1", "Melody")])
    s.open('part id="P1"')
    s.comment("One-beat pickup: excluded from bar numbering, bar 1 starts after it.")
    s.open('measure number="0" implicit="yes"')
    attributes(s, 6, 1, 4, 4, [("G", 2)])
    tempo(s, 88)
    note(s, "B4", 3, "eighth")
    note(s, "C5", 3, "eighth")
    s.close("measure")

    s.open('measure number="1"')
    s.comment("Tie inside the bar: D5 quarter + D5 eighth is one note of 9 divisions.")
    note(s, "D5", 6, "quarter", tie="start")
    note(s, "D5", 3, "eighth", tie="stop")
    note(s, "C5", 3, "eighth")
    s.comment("Triplet eighths: three in the time of two, 2 divisions each.")
    note(s, "B4", 2, "eighth", tuplet=(3, 2), tuplet_mark="start")
    note(s, "A4", 2, "eighth", tuplet=(3, 2))
    note(s, "G4", 2, "eighth", tuplet=(3, 2), tuplet_mark="stop")
    note(s, "A4", 6, "quarter")
    s.close("measure")

    s.open('measure number="2"')
    note(s, "B4", 12, "half")
    s.comment("Triplet quarters: three in the time of a half, 4 divisions each.")
    note(s, "C5", 4, "quarter", tuplet=(3, 2), tuplet_mark="start")
    note(s, "D5", 4, "quarter", tuplet=(3, 2))
    note(s, "E5", 4, "quarter", tuplet=(3, 2), tuplet_mark="stop")
    s.close("measure")

    s.open('measure number="3"')
    note(s, "D5", 12, "half")
    s.comment("Tie across the bar line into bar 4: one note of 18 divisions.")
    note(s, "G5", 12, "half", tie="start")
    s.close("measure")

    s.comment("The last bar completes the pickup: three beats.")
    s.open('measure number="4"')
    note(s, "G5", 6, "quarter", tie="stop")
    note(s, "F#5", 2, "eighth", tuplet=(3, 2), tuplet_mark="start")
    note(s, "E5", 2, "eighth", tuplet=(3, 2))
    note(s, "D5", 2, "eighth", tuplet=(3, 2), tuplet_mark="stop")
    note(s, "G5", 6, "quarter")
    barline(s, "right", style="light-heavy")
    s.close("measure")
    s.close("part")
    return s.render()


def septuplets():
    """AC-S1-03: septuplet 16ths and 8ths, which 480 ticks per quarter cannot hold exactly.

    4/4, C major, divisions 7, so each septuplet 16th is 1 division and each septuplet
    8th is 2. 25 notes; 21 of them (every septuplet note) have an onset or end that
    rounds, and the 4 plain notes are exact.
    """
    s = Score("Septuplets", [("P1", "Melody")])
    s.open('part id="P1"')
    s.open('measure number="1"')
    attributes(s, 7, 0, 4, 4, [("G", 2)])
    tempo(s, 72)
    s.comment("Seven 16ths in the time of four: 1 division each.")
    for i, pitch in enumerate(["C5", "D5", "E5", "F5", "G5", "A5", "B5"]):
        note(s, pitch, 1, "16th", tuplet=(7, 4), tuplet_mark={0: "start", 6: "stop"}.get(i))
    note(s, "C6", 7, "quarter")
    s.comment("Seven 8ths in the time of four (a half note): 2 divisions each.")
    for i, pitch in enumerate(["B5", "A5", "G5", "F5", "E5", "D5", "C5"]):
        note(s, pitch, 2, "eighth", tuplet=(7, 4), tuplet_mark={0: "start", 6: "stop"}.get(i))
    s.close("measure")

    s.open('measure number="2"')
    note(s, "C5", 7, "quarter")
    for i, pitch in enumerate(["E5", "F5", "G5", "A5", "G5", "F5", "E5"]):
        note(s, pitch, 1, "16th", tuplet=(7, 4, "16th"), tuplet_mark={0: "start", 6: "stop"}.get(i))
    note(s, "D5", 14, "half")
    s.close("measure")

    s.open('measure number="3"')
    note(s, "C5", 28, "whole")
    barline(s, "right", style="light-heavy")
    s.close("measure")
    s.close("part")
    return s.render()


def grand_staff():
    """S1-I4: one piano part on two staves; staff 1 is the right hand, staff 2 the left.

    3/4, F major, 96 BPM, divisions 2. Chords, a second voice in the right hand in
    bar 4, and <backup> to move from staff 1 to staff 2. 26 notes: 11 on staff 1,
    15 on staff 2.
    """
    s = Score("Grand staff waltz", [("P1", "Piano")])
    s.open('part id="P1"')

    def left(*chords):
        """Staff 2, voice 5: each item is (duration, type, pitches)."""
        for duration, note_type, pitches in chords:
            for i, pitch in enumerate(pitches):
                note(s, pitch, duration, note_type, voice=5, staff=2, chord=i > 0)

    s.open('measure number="1"')
    attributes(s, 2, -1, 3, 4, [("G", 2), ("F", 4)])
    tempo(s, 96)
    for pitch in ["A4", "C5", "F5"]:
        note(s, pitch, 2, "quarter", staff=1)
    backup(s, 6)
    left((2, "quarter", ["F2"]), (2, "quarter", ["A3", "C4"]), (2, "quarter", ["A3", "C4"]))
    s.close("measure")

    s.open('measure number="2"')
    note(s, "E5", 3, "quarter", staff=1, dot=True)
    note(s, "D5", 1, "eighth", staff=1)
    note(s, "C5", 2, "quarter", staff=1)
    backup(s, 6)
    left((2, "quarter", ["C3"]), (2, "quarter", ["G3", "Bb3"]), (2, "quarter", ["G3", "Bb3"]))
    s.close("measure")

    s.open('measure number="3"')
    note(s, "Bb4", 4, "half", staff=1)
    note(s, "A4", 2, "quarter", staff=1)
    backup(s, 6)
    left((2, "quarter", ["C3"]), (2, "quarter", ["G3", "Bb3"]))
    note(s, None, 2, "quarter", voice=5, staff=2)
    s.close("measure")

    s.open('measure number="4"')
    s.comment("Two voices on staff 1, then the left hand.")
    note(s, "F5", 6, "half", staff=1, dot=True)
    backup(s, 6)
    note(s, "A4", 4, "half", voice=2, staff=1)
    note(s, "C5", 2, "quarter", voice=2, staff=1)
    backup(s, 6)
    left((6, "half", ["F2", "C3"]))
    barline(s, "right", style="light-heavy")
    s.close("measure")
    s.close("part")
    return s.render()


def phrases_repeats():
    """S1-I5: phrase slurs, a nested articulation slur, a beat of rest, repeat signs and endings.

    4/4, C major, divisions 4. Bars 1 to 4 repeat, with first and second endings in
    bars 4 and 5. Phrase slurs (number 1) cover bars 1 to 2, 3 to 4 and 5; a short slur
    (number 2) joins E5 and F5 inside the first phrase. 15 notes and one quarter rest.
    """
    s = Score("Phrases and repeats", [("P1", "Melody")])
    s.open('part id="P1"')
    s.open('measure number="1"')
    barline(s, "left", style="heavy-light", repeat="forward")
    attributes(s, 4, 0, 4, 4, [("G", 2)])
    tempo(s, 100)
    note(s, "C5", 4, "quarter", slur=[(1, "start")])
    note(s, "D5", 4, "quarter")
    note(s, "E5", 4, "quarter", slur=[(2, "start")])
    note(s, "F5", 4, "quarter", slur=[(2, "stop")])
    s.close("measure")

    s.open('measure number="2"')
    note(s, "G5", 8, "half")
    note(s, "E5", 4, "quarter")
    note(s, "C5", 4, "quarter", slur=[(1, "stop")])
    s.close("measure")

    s.open('measure number="3"')
    s.comment("A beat of rest between phrases.")
    note(s, None, 4, "quarter")
    note(s, "D5", 4, "quarter", slur=[(1, "start")])
    note(s, "E5", 4, "quarter")
    note(s, "F5", 4, "quarter")
    s.close("measure")

    s.open('measure number="4"')
    barline(s, "left", ending=(1, "start"))
    note(s, "E5", 8, "half")
    note(s, "D5", 8, "half", slur=[(1, "stop")])
    barline(s, "right", style="light-heavy", repeat="backward", ending=(1, "stop"))
    s.close("measure")

    s.open('measure number="5"')
    barline(s, "left", ending=(2, "start"))
    note(s, "D5", 4, "quarter", slur=[(1, "start")])
    note(s, "B4", 4, "quarter")
    note(s, "C5", 8, "half", slur=[(1, "stop")])
    barline(s, "right", style="light-heavy", ending=(2, "stop"))
    s.close("measure")
    s.close("part")
    return s.render()


CONTAINER = (
    '<?xml version="1.0" encoding="UTF-8"?>\n'
    "<container>\n"
    "  <rootfiles>\n"
    '    <rootfile full-path="score.musicxml" media-type="application/vnd.recordare.musicxml+xml"/>\n'
    "  </rootfiles>\n"
    "</container>\n"
)


def mxl(score_xml):
    """A compressed MusicXML archive: uncompressed mimetype first, then the container and score."""
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        def add(name, data, method):
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = method
            info.external_attr = 0o644 << 16
            archive.writestr(info, data)

        add("mimetype", "application/vnd.recordare.musicxml", zipfile.ZIP_STORED)
        add("META-INF/container.xml", CONTAINER, zipfile.ZIP_DEFLATED)
        add("score.musicxml", score_xml, zipfile.ZIP_DEFLATED)
    return buffer.getvalue()


FIXTURES = {
    "ties-triplets-pickup.musicxml": ties_triplets_pickup,
    "septuplets.musicxml": septuplets,
    "grand-staff.musicxml": grand_staff,
    "phrases-repeats.musicxml": phrases_repeats,
}

if __name__ == "__main__":
    for file_name, make in FIXTURES.items():
        with open(file_name, "w", encoding="utf-8", newline="\n") as f:
            f.write(make())
    with open("grand-staff.mxl", "wb") as f:
        f.write(mxl(grand_staff()))
