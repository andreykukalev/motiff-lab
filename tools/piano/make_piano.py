"""Builds the playback piano in src/MotiffLab.Web/wwwroot/audio/piano from Salamander Grand Piano V3.

Usage:
    curl -LO https://archive.org/download/SalamanderGrandPianoV3/SalamanderGrandPianoV3_OggVorbis.tar.bz2
    tar xjf SalamanderGrandPianoV3_OggVorbis.tar.bz2
    python3 tools/piano/make_piano.py SalamanderGrandPianoV3_OggVorbis

Needs ffmpeg with libmp3lame. Takes one velocity layer (v10 of 16) of the 30 samples recorded in
minor thirds from A0 to C8, with the tuning corrections from the retuned .sfz, mixes each to
mono, shortens it (6 s below C3, 5 s below C5, 3.5 s above) with a 1 s fade-out, and encodes it
as 96 kbit/s MP3. Files are named by MIDI note number. See README.md for the licence.
"""

import json
import pathlib
import re
import subprocess
import sys

LAYER = 10
NAMES = {"C": 0, "D#": 3, "F#": 6, "A": 9}
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "src" / "MotiffLab.Web" / "wwwroot" / "audio" / "piano"


def regions(sfz: pathlib.Path):
    for line in sfz.read_text().splitlines():
        sample = re.search(rf"sample=ogg\\(\w#?)(\d)v{LAYER}\.ogg", line)
        if not sample:
            continue
        name, octave = sample.groups()
        pitch = 12 * (int(octave) + 1) + NAMES[name]
        keycenter = re.search(r"pitch_keycenter=(\d+)", line)
        # sfz defaults pitch_keycenter to 60; the C4 region relies on it.
        assert (int(keycenter.group(1)) if keycenter else 60) == pitch, line
        tune = re.search(r"tune=(-?\d+)", line)
        yield pitch, f"{name}{octave}v{LAYER}.ogg", int(tune.group(1)) if tune else 0


def seconds(pitch: int) -> float:
    return 6.0 if pitch < 48 else 5.0 if pitch < 72 else 3.5


def main(source: pathlib.Path) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    samples = []
    for pitch, name, tune in sorted(regions(source / "SalamanderGrandPianoV3Retuned.sfz")):
        length = seconds(pitch)
        file = f"{pitch:03d}.mp3"
        subprocess.run(
            [
                "ffmpeg", "-v", "error", "-y", "-i", str(source / "ogg" / name),
                "-ac", "1", "-t", str(length), "-af", f"afade=t=out:st={length - 1}:d=1",
                "-c:a", "libmp3lame", "-b:a", "96k", str(OUT / file),
            ],
            check=True,
        )
        samples.append({"pitch": pitch, "file": file, "tuneCents": tune})

    manifest = {
        "name": "Salamander Grand Piano V3 (Motiff Lab cut)",
        "author": "Alexander Holm",
        "license": "CC BY 3.0",
        "licenseUrl": "https://creativecommons.org/licenses/by/3.0/",
        "source": "https://archive.org/details/SalamanderGrandPianoV3",
        "changes": "Velocity layer 10 of 16, retuned values from SalamanderGrandPianoV3Retuned.sfz; mixed to mono, "
        "shortened with a 1 s fade-out, encoded as 96 kbit/s MP3. See tools/piano/README.md.",
        "samples": samples,
    }
    (OUT / "piano.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"{len(samples)} samples written to {OUT}")


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]))
