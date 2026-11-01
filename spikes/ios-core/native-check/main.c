// Calls the native music-core library through its C header, the way the Swift app does.
//   clang -I ../CoreBridge/include main.c -L <publish dir> -l:MotiffLabCore.so -Wl,-rpath,<publish dir>
//   ./a.out ../assets/fur-elise-97-bars.musicxml
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "motifflab_core.h"

int main(int argc, char **argv)
{
    if (argc < 2)
    {
        fprintf(stderr, "usage: %s <file> [iterations]\n", argv[0]);
        return 2;
    }

    FILE *file = fopen(argv[1], "rb");
    if (file == NULL)
    {
        perror(argv[1]);
        return 1;
    }

    fseek(file, 0, SEEK_END);
    long length = ftell(file);
    fseek(file, 0, SEEK_SET);
    uint8_t *data = malloc((size_t)length);
    if (data == NULL || fread(data, 1, (size_t)length, file) != (size_t)length)
    {
        fprintf(stderr, "could not read %s\n", argv[1]);
        return 1;
    }

    fclose(file);
    const char *name = strrchr(argv[1], '/') ? strrchr(argv[1], '/') + 1 : argv[1];
    int iterations = argc > 2 ? atoi(argv[2]) : 10;

    char *version = ml_version();
    printf("%s\n", version);
    ml_free(version);

    char *phrases = ml_phrases(name, data, (int32_t)length);
    printf("%s\n", phrases);
    ml_free(phrases);

    char *report = ml_benchmark(name, data, (int32_t)length, iterations, 1);
    printf("%s\n", report);
    ml_free(report);

    char *error = ml_phrases("not-music.txt", data, 4);
    printf("%s\n", error);
    ml_free(error);

    free(data);
    return 0;
}
