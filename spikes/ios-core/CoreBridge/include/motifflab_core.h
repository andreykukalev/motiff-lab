// music-core as a native library (NativeAOT). The functions are exported by
// CoreBridge/Exports.cs. Every returned string is UTF-8 JSON owned by the caller:
// free it with ml_free. A failure returns {"error": "...", "message": "..."}.
#ifndef MOTIFFLAB_CORE_H
#define MOTIFFLAB_CORE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// The runtime the library was built with, for example ".NET 10.0.0; ios-arm64; NativeAOT".
char *ml_version(void);

// The facade (docs/decisions/0020-music-core-facade.md): one contract with JSON in and out.
// Opens a MIDI or MusicXML file as a new project, or a project file (.motiff.json), and
// returns {"piece": n, "project": {...}, "summary": {...}}; `request` is an optional JSON
// object ({"language": "ru", "id": "...", "now": "..."}) or NULL.
char *ml_open(const char *file_name, const uint8_t *data, int32_t length, const char *request);

// Runs one call ("phrases", "select", "understand", "experiments", "variation.propose",
// "notation", "playback", "project.save", ...) on an open piece; piece 0 is for "version"
// and "catalogue". `request` is the call's JSON object or NULL.
char *ml_call(int32_t piece, const char *name, const char *request);

// Forgets an open piece; 1 when it was open, 0 when it was not.
int32_t ml_close(int32_t piece);

// Spike benchmarks. Runs import, phrases, analysis, experiments, notation and a project file
// round trip `iterations` times on the file and returns the counts and timings per stage.
// `file_name` decides the format (.musicxml, .xml, .mxl, .mid). `russian` != 0 for Russian facts.
char *ml_benchmark(const char *file_name, const uint8_t *data, int32_t length, int32_t iterations, int32_t russian);

// The suggested phrases of the file: number, first and last bar, onset and end in ticks.
char *ml_phrases(const char *file_name, const uint8_t *data, int32_t length);

// The notation of the whole piece as MEI for Verovio: {"notes": n, "ms": t, "mei": "<mei …>"}.
char *ml_mei(const char *file_name, const uint8_t *data, int32_t length);

// Frees a string returned by any function above.
void ml_free(char *text);

#ifdef __cplusplus
}
#endif

#endif