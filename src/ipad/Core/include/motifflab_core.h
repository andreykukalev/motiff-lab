// music-core as a native library (NativeAOT) for the iPad app (decision record 0019). The functions are the facade of
// decision record 0020, exported by ../Exports.cs. Every returned string is UTF-8 JSON owned by the caller: free it with
// ml_free. A failure returns {"error": "...", "message": "..."}. Call them from one thread at a time.
#ifndef MOTIFFLAB_CORE_H
#define MOTIFFLAB_CORE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// The facade's version: {"contract": 3, "projectFile": 12, "runtime": ".NET 10.0.x; ios-arm64; NativeAOT"}.
char *ml_version(void);

// Opens a MIDI or MusicXML file as a new project, or a project file (.motiff.json), and returns
// {"piece": n, "project": {...}, "summary": {...}}; `request` is an optional JSON object
// ({"language": "ru", "id": "...", "now": "..."}) or NULL.
char *ml_open(const char *file_name, const uint8_t *data, int32_t length, const char *request);

// Runs one call ("phrases", "select", "understand", "try", "variation.propose", "notation", "playback",
// "project.save", ...) on an open piece; piece 0 is for "version", "catalogue", "settings" and "settings.state".
// `request` is the call's JSON object or NULL.
char *ml_call(int32_t piece, const char *name, const char *request);

// Forgets an open piece; 1 when it was open, 0 when it was not.
int32_t ml_close(int32_t piece);

// Frees a string returned by any function above.
void ml_free(char *text);

#ifdef __cplusplus
}
#endif

#endif
