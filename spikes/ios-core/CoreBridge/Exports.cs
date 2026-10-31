using System.Runtime.InteropServices;
using System.Text;

using MotiffLab.MusicCore;

namespace MotiffLab.CoreBridge;

/// <summary>
/// The C functions the native build of music-core exports (include/motifflab_core.h). Every
/// function takes bytes and returns a UTF-8 JSON string the caller frees with ml_free; no
/// exception crosses the boundary, so a failure comes back as {"error": …, "message": …}.
/// </summary>
public static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "ml_version")]
    public static byte* Version() => Utf8(CoreBenchmark.RuntimeDescription());

    [UnmanagedCallersOnly(EntryPoint = "ml_benchmark")]
    public static byte* Benchmark(byte* fileName, byte* data, int length, int iterations, int russian) =>
        Guard(() => CoreBenchmark.Run(Text(fileName), Bytes(data, length), iterations, russian != 0 ? Words.Russian : Words.English));

    [UnmanagedCallersOnly(EntryPoint = "ml_phrases")]
    public static byte* Phrases(byte* fileName, byte* data, int length) =>
        Guard(() => CoreBenchmark.Phrases(Text(fileName), Bytes(data, length)));

    [UnmanagedCallersOnly(EntryPoint = "ml_free")]
    public static void Free(byte* text) => NativeMemory.Free(text);

    private static byte* Guard(Func<string> call)
    {
        try
        {
            return Utf8(call());
        }
        catch (Exception error)
        {
            return Utf8(CoreBenchmark.Error(error));
        }
    }

    private static string Text(byte* text) => text is null ? string.Empty : Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;

    private static byte[] Bytes(byte* data, int length) => data is null || length <= 0 ? [] : new ReadOnlySpan<byte>(data, length).ToArray();

    private static byte* Utf8(string text)
    {
        int count = Encoding.UTF8.GetByteCount(text);
        byte* result = (byte*)NativeMemory.Alloc((nuint)count + 1);
        Encoding.UTF8.GetBytes(text, new Span<byte>(result, count));
        result[count] = 0;
        return result;
    }
}