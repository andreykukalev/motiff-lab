"""Calls the native music-core through its C functions, as the iPad app does (decision records 0019, 0020).

Usage: python3 ipad/Core/check.py <MotiffLabCore.so or .dylib> [piece.musicxml]
Publish the library first: dotnet publish ipad/Core -c Release -r linux-x64 (or osx-arm64) -o <folder>.
Prints one line per call and exits with 1 when an answer is not what the facade promises.
"""

from __future__ import annotations

import ctypes
import json
import sys
import time
from pathlib import Path


def main() -> int:
    library = ctypes.CDLL(sys.argv[1])
    piece_path = Path(sys.argv[2] if len(sys.argv) > 2 else Path(__file__).parents[2] / "design/kit/elise.musicxml")
    for name in ("ml_version", "ml_open", "ml_call"):
        getattr(library, name).restype = ctypes.c_void_p
    library.ml_open.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int32, ctypes.c_char_p]
    library.ml_call.argtypes = [ctypes.c_int32, ctypes.c_char_p, ctypes.c_char_p]
    library.ml_close.argtypes = [ctypes.c_int32]
    library.ml_close.restype = ctypes.c_int32
    library.ml_free.argtypes = [ctypes.c_void_p]

    def take(pointer: int | None) -> dict:
        assert pointer, "the library returned NULL"
        try:
            return json.loads(ctypes.string_at(pointer).decode("utf-8"))
        finally:
            library.ml_free(pointer)

    failures = 0

    def check(label: str, condition: bool, answer: dict, started: float) -> None:
        nonlocal failures
        ms = (time.perf_counter() - started) * 1000
        print(f"{'ok  ' if condition else 'FAIL'} {label:<22} {ms:7.1f} ms")
        if not condition:
            failures += 1
            print("     " + json.dumps(answer, ensure_ascii=False)[:400])

    started = time.perf_counter()
    version = take(library.ml_version())
    check("version", version.get("contract", 0) >= 3 and version.get("projectFile", 0) >= 12, version, started)

    data = piece_path.read_bytes()
    started = time.perf_counter()
    opened = take(library.ml_open(piece_path.name.encode(), data, len(data), b'{"language": "ru", "id": "p_check"}'))
    piece = opened.get("piece", 0)
    check("open", piece > 0 and opened.get("summary", {}).get("noteCount", 0) > 0, opened, started)

    def call(name: str, request: str | None = None) -> dict:
        return take(library.ml_call(piece, name.encode(), request.encode() if request else None))

    started = time.perf_counter()
    phrases = call("phrases")
    check("phrases", [level.get("id") for level in phrases.get("levels", [])] == ["short", "phrase", "sentence"], phrases, started)
    started = time.perf_counter()
    selected = call("select", '{"bars": {"first": 1, "last": 2}}')
    check("select", "selection" in selected, selected, started)
    started = time.perf_counter()
    understood = call("understand")
    check("understand", "error" not in understood, understood, started)
    started = time.perf_counter()
    notation = call("notation", '{"title": "Für Elise"}')
    check("notation", "<mei" in notation.get("mei", ""), notation, started)
    started = time.perf_counter()
    playback = call("playback")
    check("playback", "error" not in playback, playback, started)
    started = time.perf_counter()
    saved = call("project.save", '{"now": "2026-11-04T12:00:00Z"}')
    check("project.save", saved.get("format") == "motiff-lab-project" and saved.get("version", 0) >= 12, saved, started)
    started = time.perf_counter()
    unknown = call("dance")
    check("error code", unknown.get("error") == "unknownCall", unknown, started)
    check("close", library.ml_close(piece) == 1 and library.ml_close(piece) == 0, {}, time.perf_counter())
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
