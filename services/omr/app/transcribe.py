"""Transcribes a recording of piano music, audio or video, into MIDI notes (decision record 0014).

ffmpeg takes the sound out of the upload as mono 22,050 Hz audio, and Basic Pitch (Apache-2.0, from
Spotify) turns it into notes on the CPU through ONNX Runtime. Both run unmodified, each recording in
the request's own temporary folder. Basic Pitch is cheap rather than exact: it hears any instrument,
not the piano's pedal, so its notes are a first draft for the player to check.
"""

from __future__ import annotations

import io
import subprocess
from dataclasses import dataclass
from importlib import metadata
from pathlib import Path

# Basic Pitch's own rate: the model is trained on audio at this rate.
SAMPLE_RATE = 22050

# Containers and codecs a phone, a camera or a recording app writes. ffmpeg opens nothing else, so an
# upload cannot make it read a playlist, another file or the network.
INPUT_FORMATS = "mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,wav,mp3,ogg,flac,aac,aiff,caf"

# The keys of an 88-key piano, A0 to C8, in hertz: anything outside is noise, not a note.
LOWEST_PIANO_HZ = 27.5
HIGHEST_PIANO_HZ = 4186.0

# Extracting a few minutes of sound takes seconds; this only stops a file that never ends.
EXTRACT_TIMEOUT_SECONDS = 120


class TranscribeError(RuntimeError):
    """The recording could not be transcribed. The message is plain words for the player."""


@dataclass(frozen=True)
class Transcription:
    """The notes as a MIDI file, how many seconds were heard, and whether the recording was longer."""

    midi: bytes
    seconds: float
    cut: bool


def engine_version() -> str:
    try:
        return metadata.version("basic-pitch")
    except metadata.PackageNotFoundError:
        return "not installed"


def extract_command(source: Path, target: Path, max_seconds: int) -> list[str]:
    """The ffmpeg command that writes the first max_seconds of the upload's sound as a WAV file."""
    return [
        "ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error",
        "-protocol_whitelist", "file",
        "-format_whitelist", INPUT_FORMATS,
        "-i", str(source),
        "-t", str(max_seconds),
        "-map", "0:a:0", "-vn", "-sn", "-dn",
        "-ac", "1", "-ar", str(SAMPLE_RATE), "-c:a", "pcm_s16le",
        "-f", "wav", "-y", str(target),
    ]  # fmt: skip


def duration_command(source: Path) -> list[str]:
    """The ffprobe command that prints the length of the upload's first sound track, in seconds."""
    return [
        "ffprobe", "-hide_banner", "-loglevel", "error",
        "-protocol_whitelist", "file",
        "-format_whitelist", INPUT_FORMATS,
        "-select_streams", "a:0", "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1", str(source),
    ]  # fmt: skip


def extract_audio(source: Path, folder: Path, max_seconds: int) -> tuple[Path, float, bool]:
    """The upload's sound as a WAV file in the folder, its length in seconds as heard, and whether it was cut."""
    target = folder / "sound.wav"
    try:
        result = subprocess.run(extract_command(source, target, max_seconds), capture_output=True, timeout=EXTRACT_TIMEOUT_SECONDS, check=False)
        probe = subprocess.run(duration_command(source), capture_output=True, timeout=EXTRACT_TIMEOUT_SECONDS, check=False)
    except subprocess.TimeoutExpired as error:
        raise TranscribeError("Taking the sound out of this file took too long.") from error

    if result.returncode != 0 or not target.exists() or target.stat().st_size <= 44:
        raise TranscribeError("This file has no sound Motiff Lab can read. Record audio or video as MP4, M4A, MOV, MP3, WAV or WebM.")

    try:
        length = float(probe.stdout.decode("ascii", "replace").strip())
    except ValueError:
        length = 0.0
    heard = (target.stat().st_size - 44) / (2 * SAMPLE_RATE)
    return target, heard, length > max_seconds + 0.5


def transcribe_audio(audio: Path) -> bytes:
    """The notes Basic Pitch hears in a mono WAV file, as a MIDI file."""
    # Imported here, so the service starts and its tests run without the model and its libraries.
    from basic_pitch import ICASSP_2022_MODEL_PATH, note_creation
    from basic_pitch.inference import Model, run_inference

    output = run_inference(audio, Model(ICASSP_2022_MODEL_PATH))
    midi, notes = note_creation.model_output_to_notes(
        output,
        onset_thresh=0.5,
        frame_thresh=0.3,
        min_note_len=11,
        min_freq=LOWEST_PIANO_HZ,
        max_freq=HIGHEST_PIANO_HZ,
        include_pitch_bends=False,
        melodia_trick=True,
    )
    if not notes:
        raise TranscribeError("No notes were heard in this recording. Record closer to the piano, with less noise around it.")

    buffer = io.BytesIO()
    midi.write(buffer)
    return buffer.getvalue()


def transcribe(source: Path, max_seconds: int) -> Transcription:
    """Transcribes the first max_seconds of an audio or video file, in the folder it lies in."""
    audio, seconds, cut = extract_audio(source, source.parent, max_seconds)
    return Transcription(transcribe_audio(audio), seconds, cut)
