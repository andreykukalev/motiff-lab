"""Times the iPad's score pipeline outside the app: music-core (NativeAOT) writes MEI, Verovio lays it
out on the web app's A4 pages in Leland and draws every page as SVG. Same C calls as Engraver.swift.

    python3 engrave.py <MotiffLabCore .so/.dylib> <libverovio .so/.dylib> <verovio data dir> <file> [runs]

Prints one JSON line per piece: the first run and the median of the rest, in ms. Each run uses a new
toolkit, as the app does. With MOTIFF_ENGRAVE_LOOP=<seconds> it only repeats the layout for that long,
so a profiler can attach (the iOS core spike workflow samples it on macOS).
"""
import ctypes
import json
import os
import statistics
import sys
import time

core_path, vrv_path, data_dir, path = sys.argv[1:5]
runs = int(sys.argv[5]) if len(sys.argv) > 5 else 5

core = ctypes.CDLL(core_path)
core.ml_mei.restype = ctypes.c_void_p
core.ml_mei.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int]
core.ml_free.argtypes = [ctypes.c_void_p]

vrv = ctypes.CDLL(vrv_path)
vrv.vrvToolkit_constructorResourcePath.restype = ctypes.c_void_p
vrv.vrvToolkit_constructorResourcePath.argtypes = [ctypes.c_char_p]
vrv.vrvToolkit_destructor.argtypes = [ctypes.c_void_p]
for name in ["vrvToolkit_getVersion", "vrvToolkit_getPageCount", "vrvToolkit_getLog"]:
    getattr(vrv, name).argtypes = [ctypes.c_void_p]
for name in ["vrvToolkit_getVersion", "vrvToolkit_getLog", "vrvToolkit_renderToSVG", "vrvToolkit_renderToTimemap"]:
    getattr(vrv, name).restype = ctypes.c_char_p
vrv.vrvToolkit_setOptions.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
vrv.vrvToolkit_setOptions.restype = ctypes.c_bool
vrv.vrvToolkit_loadData.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
vrv.vrvToolkit_loadData.restype = ctypes.c_bool
vrv.vrvToolkit_renderToSVG.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_bool]
vrv.vrvToolkit_renderToTimemap.argtypes = [ctypes.c_void_p, ctypes.c_char_p]

# The web app's A4 page (Scripts/score/score-page.ts) in Leland, as in Engraver.swift.
OPTIONS = json.dumps({
    "scale": 100, "pageWidth": 2100, "pageHeight": 2970, "pageMarginLeft": 150, "pageMarginRight": 150,
    "pageMarginTop": 120, "pageMarginBottom": 150, "adjustPageHeight": False, "justifyVertically": True,
    "breaks": "auto", "header": "auto", "footer": "none", "svgViewBox": True, "font": "Leland",
}).encode()


def ms(start):
    return (time.perf_counter() - start) * 1000


raw = open(path, "rb").read()
file_name = os.path.basename(path)
start = time.perf_counter()
pointer = core.ml_mei(file_name.encode(), raw, len(raw))
result = json.loads(ctypes.string_at(pointer).decode())
core.ml_free(pointer)
mei_ms = ms(start)
if "mei" not in result:
    sys.exit(f"music-core: {result}")
mei = result["mei"].encode()


def run():
    timings = {}
    start = time.perf_counter()
    toolkit = vrv.vrvToolkit_constructorResourcePath(data_dir.encode())
    timings["toolkit"] = ms(start)
    start = time.perf_counter()
    vrv.vrvToolkit_setOptions(toolkit, OPTIONS)
    timings["options"] = ms(start)
    start = time.perf_counter()
    if not vrv.vrvToolkit_loadData(toolkit, mei):
        sys.exit("Verovio: " + vrv.vrvToolkit_getLog(toolkit).decode())
    timings["layout"] = ms(start)
    pages = vrv.vrvToolkit_getPageCount(toolkit)
    start = time.perf_counter()
    size = sum(len(vrv.vrvToolkit_renderToSVG(toolkit, page, False)) for page in range(1, pages + 1))
    timings["allPages"] = ms(start)
    start = time.perf_counter()
    vrv.vrvToolkit_renderToTimemap(toolkit, b"{}")
    timings["timemap"] = ms(start)
    version = vrv.vrvToolkit_getVersion(toolkit).decode()
    vrv.vrvToolkit_destructor(toolkit)
    return timings, pages, size, version


loop = float(os.environ.get("MOTIFF_ENGRAVE_LOOP", "0"))
if loop:
    print(f"pid {os.getpid()}: engraving {file_name} for {loop:.0f} s", flush=True)
    end = time.perf_counter() + loop
    while time.perf_counter() < end:
        run()
    sys.exit(0)

first, pages, size, version = run()
rest = [run()[0] for _ in range(max(runs - 1, 1))]
print(json.dumps({
    "file": file_name, "notes": result["notes"], "verovio": version, "pages": pages, "svgKB": size // 1024,
    "meiMs": round(mei_ms, 1),
    "first": {step: round(value, 1) for step, value in first.items()},
    "median": {step: round(statistics.median(r[step] for r in rest), 1) for step in first},
}))
