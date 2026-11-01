using System.Runtime.InteropServices;
using System.Text;

using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Facade;

namespace MotiffLab.CoreBridge;

/// <summary>
/// The C functions the native build of music-core exports (include/motifflab_core.h). Every
/// function takes bytes and returns a UTF-8 JSON string the caller frees with ml_free; no
/// exception crosses the boundary, so a failure comes back as {"error": …, "message": …}.
/// ml_open, ml_call and ml_close are the facade (0020); the rest are the spike's benchmarks.
/// </summary>
public static unsafe class Exports
{
    private static readonly MusicCoreFacade s_facade = new();

    [UnmanagedCallersOnly(EntryPoint = "ml_version")]
    public static byte* Version() => Utf8(CoreBenchmark.RuntimeDescription());

    [UnmanagedCallersOnly(EntryPoint = "ml_open")]
    public static byte* Open(byte* fileName, byte* data, int length, byte* request) =>
        Guard(() => s_facade.Open(Text(fileName), Bytes(data, length), OptionalText(request)));

    [UnmanagedCallersOnly(EntryPoint = "ml_call")]
    public static byte* Call(int piece, byte* name, byte* request) =>
        Guard(() => s_facade.Call(piece, Text(name), OptionalText(request)));

    [UnmanagedCallersOnly(EntryPoint = "ml_close")]
    public static int Close(int piece) => s_facade.Close(piece) ? 1 : 0;

    [UnmanagedCallersOnly(EntryPoint = "ml_benchmark")]
    public static byte* Benchmark(byte* fileName, byte* data, int length, int iterations, int russian) =>
        Guard(() => CoreBenchmark.Run(Text(fileName), Bytes(data, length), iterations, russian != 0 ? Words.Russian : Words.English));

    [UnmanagedCallersOnly(EntryPoint = "ml_phrases")]
    public static byte* Phrases(byte* fileName, byte* data, int length) =>
        Guard(() => CoreBenchmark.Phrases(Text(fileName), Bytes(data, length)));

    [UnmanagedCallersOnly(EntryPoint = "ml_mei")]
    public static byte* Mei(byte* fileName, byte* data, int length) =>
        Guard(() => CoreBenchmark.Mei(Text(fileName), Bytes(data, length)));

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

    private static string? OptionalText(byte* text) => text is null ? null : Marshal.PtrToStringUTF8((nint)text);

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