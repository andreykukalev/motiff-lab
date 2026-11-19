"""Transcribes a recording of piano music, audio or video, into MIDI notes (decision record 0014).

ffmpeg takes the sound out of the upload as mono 44,100 Hz audio, and Transkun V2 (MIT, Yujia Yan) turns
it into notes on the CPU through PyTorch. Both run unmodified, each recording in the request's own
temporary folder. Transkun is trained on the piano alone: it hears overtones as overtones rather than as
notes of their own, and it hears the sustain pedal. It takes about a second of two cores for a second
of sound, so the server hears the first few minutes of a recording.
"""

from __future__ import annotations

import io
import subprocess
from dataclasses import dataclass
import wave
from importlib import metadata, resources
from pathlib import Path

ENGINE = "transkun"

# Transkun's own rate: the model is trained on audio at this rate. It listens up to 8 kHz, so the
# 22,050 Hz sound the web app sends loses nothing it hears.
SAMPLE_RATE = 44100

# Containers and codecs a phone, a camera or a recording app writes. ffmpeg opens nothing else, so an
# upload cannot make it read a playlist, another file or the network.
INPUT_FORMATS = "mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,wav,mp3,ogg,flac,aac,aiff,caf"

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
        return metadata.version(ENGINE)
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
    """The notes Transkun hears in a mono 16-bit WAV file, as a MIDI file."""
    # Imported here, so the service starts and its tests run without the model and its libraries.
    import moduleconf
    import numpy
    import torch
    from transkun.Data import writeMidi

    # The model is loaded for each recording rather than kept: it takes a second or two, against a
    # minute or more of hearing, and the memory is free for homr between recordings.
    pretrained = resources.files(ENGINE) / "pretrained"
    configuration = moduleconf.parseFromFile(str(pretrained / "2.0.conf"))["Model"]
    model = configuration.module.TransKun(conf=configuration.config)
    checkpoint = torch.load(str(pretrained / "2.0.pt"), map_location="cpu")
    model.load_state_dict(checkpoint.get("best_state_dict", checkpoint.get("state_dict")), strict=False)
    model.eval()

    with wave.open(str(audio), "rb") as sound:
        samples = numpy.frombuffer(sound.readframes(sound.getnframes()), dtype=numpy.int16)
    signal = torch.from_numpy(samples.astype(numpy.float32).reshape(-1, 1) / 32768)

    with torch.inference_mode():
        notes = model.transcribe(signal, discardSecondHalf=False)
    if not any(note.pitch > 0 for note in notes):
        raise TranscribeError("No notes were heard in this recording. Record closer to the piano, with less noise around it.")

    buffer = io.BytesIO()
    writeMidi(notes).write(buffer)
    return buffer.getvalue()


def transcribe(source: Path, max_seconds: int) -> Transcription:
    """Transcribes the first max_seconds of an audio or video file, in the folder it lies in."""
    audio, seconds, cut = extract_audio(source, source.parent, max_seconds)
    return Transcription(transcribe_audio(audio), seconds, cut)
