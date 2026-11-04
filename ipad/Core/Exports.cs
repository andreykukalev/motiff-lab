using System.Runtime.InteropServices;
using System.Text;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.NativeCore;

/// <summary>
/// The C functions of the native music-core (include/motifflab_core.h): the facade of decision record 0020 and nothing
/// else. Every function takes bytes and returns a UTF-8 JSON string the caller frees with ml_free; no exception crosses
/// the boundary, so a failure comes back as {"error": …, "message": …}. The iPad app calls them from one thread at a
/// time (0019), as the facade's open pieces are not shared between threads.
/// </summary>
public static unsafe class Exports
{
    private static readonly MusicCoreFacade s_facade = new();

    [UnmanagedCallersOnly(EntryPoint = "ml_version")]
    public static byte* Version() => Guard(MusicCoreFacade.Version);

    [UnmanagedCallersOnly(EntryPoint = "ml_open")]
    public static byte* Open(byte* fileName, byte* data, int length, byte* request) =>
        Guard(() => s_facade.Open(Text(fileName), Bytes(data, length), OptionalText(request)));

    [UnmanagedCallersOnly(EntryPoint = "ml_call")]
    public static byte* Call(int piece, byte* name, byte* request) =>
        Guard(() => s_facade.Call(piece, Text(name), OptionalText(request)));

    [UnmanagedCallersOnly(EntryPoint = "ml_close")]
    public static int Close(int piece) => s_facade.Close(piece) ? 1 : 0;

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
            // The facade answers its own failures; this is for one it did not expect.
            return Utf8($$"""{"error":"internal","message":"{{System.Text.Json.JsonEncodedText.Encode(error.Message)}}"}""");
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