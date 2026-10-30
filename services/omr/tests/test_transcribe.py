from pathlib import Path

from fastapi.testclient import TestClient

from app import main
from app.main import MIDI_TYPE, create_app
from app.transcribe import INPUT_FORMATS, TranscribeError, Transcription, duration_command, extract_command
from tests.test_auth import token, verifier
from tests.test_merge import page

MIDI = b"MThd\x00\x00\x00\x06\x00\x00\x00\x01\x01\xe0MTrk\x00\x00\x00\x04\x00\xff\x2f\x00"
RECORDING = b"\x00\x00\x00\x18ftypmp42" + bytes(64)


def client(transcribe=lambda path, seconds: Transcription(MIDI, 12.0, False), **options) -> TestClient:
    return TestClient(create_app(lambda path, number: page("1"), transcribe, **options))


def test_health_names_the_transcription_engine_and_its_limits():
    body = client().get("/health").json()["transcribe"]
    assert body["engine"] == "transkun"
    assert body["maxSeconds"] == main.MAX_RECORDING_SECONDS


def test_a_recording_comes_back_as_midi_and_its_folder_is_gone():
    seen: list[Path] = []

    def transcribe(path: Path, max_seconds: int) -> Transcription:
        assert path.name == "upload" and path.read_bytes() == RECORDING
        assert max_seconds == main.MAX_RECORDING_SECONDS
        seen.append(path)
        return Transcription(MIDI, 61.25, True)

    response = client(transcribe).post("/transcribe", files={"file": ("Für Elise.mov", RECORDING, "video/quicktime")})
    assert response.status_code == 200
    assert response.headers["content-type"] == MIDI_TYPE
    assert response.content == MIDI
    assert response.headers["x-engine"].startswith("transkun ")
    assert response.headers["x-seconds"] == "61.2"
    assert response.headers["x-cut"] == "true"
    assert not seen[0].parent.exists()


def test_empty_recordings_are_refused():
    response = client().post("/transcribe", files={"file": ("take.m4a", b"", "audio/mp4")})
    assert response.status_code == 400


def test_recordings_over_the_limit_are_refused_before_they_are_read(monkeypatch):
    monkeypatch.setattr(main, "MAX_RECORDING_BYTES", 16)
    heard: list[Path] = []

    def transcribe(path: Path, max_seconds: int) -> Transcription:
        heard.append(path)
        return Transcription(MIDI, 1.0, False)

    response = client(transcribe).post("/transcribe", files={"file": ("take.m4a", RECORDING, "audio/mp4")})
    assert response.status_code == 413
    assert heard == []


def test_a_recording_with_no_notes_gives_its_reason():
    def transcribe(path: Path, max_seconds: int) -> Transcription:
        raise TranscribeError("No notes were heard in this recording.")

    response = client(transcribe).post("/transcribe", files={"file": ("take.m4a", RECORDING, "audio/mp4")})
    assert response.status_code == 422
    assert response.json()["message"] == "No notes were heard in this recording."


def test_a_hosted_server_transcribes_nothing_without_a_sign_in():
    heard: list[Path] = []

    def transcribe(path: Path, max_seconds: int) -> Transcription:
        heard.append(path)
        return Transcription(MIDI, 1.0, False)

    hosted = client(transcribe, verify=verifier().verify)
    refused = hosted.post("/transcribe", files={"file": ("take.m4a", RECORDING, "audio/mp4")})
    assert refused.status_code == 401
    assert heard == []

    allowed = hosted.post(
        "/transcribe",
        files={"file": ("take.m4a", RECORDING, "audio/mp4")},
        headers={"Authorization": f"Bearer {token()}"},
    )
    assert allowed.status_code == 200
    assert len(heard) == 1


def test_the_app_may_read_the_transcription_headers():
    response = client().options(
        "/transcribe",
        headers={"Origin": "https://motiff.example", "Access-Control-Request-Method": "POST"},
    )
    assert response.status_code == 200
    response = client().post(
        "/transcribe",
        files={"file": ("take.m4a", RECORDING, "audio/mp4")},
        headers={"Origin": "https://motiff.example"},
    )
    exposed = response.headers["access-control-expose-headers"]
    assert "X-Seconds" in exposed and "X-Cut" in exposed


def test_ffmpeg_reads_only_the_upload_as_a_known_format():
    for command in (extract_command(Path("/w/upload"), Path("/w/sound.wav"), 600), duration_command(Path("/w/upload"))):
        assert command[command.index("-protocol_whitelist") + 1] == "file"
        assert command[command.index("-format_whitelist") + 1] == INPUT_FORMATS
        assert "hls" not in INPUT_FORMATS.split(",") and "concat" not in INPUT_FORMATS.split(",")


def test_ffmpeg_keeps_only_the_first_minutes_of_sound_in_transkuns_rate():
    command = extract_command(Path("/w/upload"), Path("/w/sound.wav"), 300)
    assert command[command.index("-t") + 1] == "300"
    assert command[command.index("-ar") + 1] == "44100"
    assert command[command.index("-ac") + 1] == "1"
    assert command[-1] == "/w/sound.wav"
    assert command.index("-i") < command.index("-t")
