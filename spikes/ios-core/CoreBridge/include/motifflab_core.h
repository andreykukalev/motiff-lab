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

// Runs import, phrases, analysis, experiments, notation and a project file round trip
// `iterations` times on the file and returns the counts and timings per stage.
// `file_name` decides the format (.musicxml, .xml, .mxl, .mid). `russian` != 0 for Russian facts.
char *ml_benchmark(const char *file_name, const uint8_t *data, int32_t length, int32_t iterations, int32_t russian);

// The suggested phrases of the file: number, first and last bar, onset and end in ticks.
char *ml_phrases(const char *file_name, const uint8_t *data, int32_t length);

// Frees a string returned by any function above.
void ml_free(char *text);

#ifdef __cplusplus
}
#endif

#endif
