"""Calls the facade (docs/decisions/0020-music-core-facade.md) through the C functions of the
native build, as the iPad app does, and prints each answer's size and time:

    python3 facade.py <MotiffLabCore .so/.dylib> <file>

Opens the file, then runs phrases, select, understand, experiments, a variation step, the
notation and playback of the variation and the project file, and checks the answers' shape.
"""
import ctypes
import json
import os
import sys
import time

core_path, path = sys.argv[1:3]
core = ctypes.CDLL(core_path)
core.ml_open.restype = ctypes.c_void_p
core.ml_open.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p]
core.ml_call.restype = ctypes.c_void_p
core.ml_call.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_char_p]
core.ml_close.restype = ctypes.c_int
core.ml_close.argtypes = [ctypes.c_int]
core.ml_free.argtypes = [ctypes.c_void_p]


def take(pointer):
    text = ctypes.string_at(pointer).decode()
    core.ml_free(pointer)
    return text


def call(piece, name, request=None):
    start = time.perf_counter()
    text = take(core.ml_call(piece, name.encode(), request.encode() if request else None))
    ms = (time.perf_counter() - start) * 1000
    answer = json.loads(text)
    print(f"{name:22} {len(text):>9} bytes {ms:8.2f} ms" + (f"  error: {answer['error']}: {answer['message']}" if "error" in answer else ""))
    return answer


raw = open(path, "rb").read()
start = time.perf_counter()
opened = json.loads(take(core.ml_open(os.path.basename(path).encode(), raw, len(raw), b'{"language": "ru", "id": "p_check"}')))
print(f"{'open':22} {'':>9}       {(time.perf_counter() - start) * 1000:8.2f} ms  piece {opened.get('piece')}, {opened.get('summary', {}).get('noteCount')} notes")
if "error" in opened:
    sys.exit(f"open failed: {opened}")
piece = opened["piece"]

assert call(0, "version")["contract"] == 1
assert call(0, "catalogue", '{"language": "ru"}')["operations"]
phrases = call(piece, "phrases")
assert phrases["levels"][0]["id"] == "phrase"
selection = call(piece, "select", '{"bars": {"first": 1, "last": 2}}')["selection"]
assert selection["noteIds"]
understood = call(piece, "understand")
assert understood["motif"]["facts"]
experiments = call(piece, "experiments")["experiments"]
if experiments:
    assert call(piece, "experiments.start", json.dumps({"id": experiments[0]["id"], "alternative": 0}))["result"]
proposed = call(piece, "variation.propose", '{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}')
assert proposed["pending"]["operation"] == "V1"
assert call(piece, "variation.accept")["steps"]
assert "<mei" in call(piece, "notation", '{"of": "variation"}')["mei"]
assert call(piece, "playback", '{"of": "inPlace"}')["notes"]
saved = call(piece, "project.save", '{"now": "2026-11-01T12:00:00Z"}')
assert saved["format"] == "motiff-lab-project"
assert call(piece, "dna")["status"] == "notYet"
assert call(piece, "understand", "not json")["error"] == "badRequest"
assert core.ml_close(piece) == 1 and core.ml_close(piece) == 0
print("facade: every answer has its shape")
