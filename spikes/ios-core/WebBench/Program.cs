using System.Runtime.InteropServices.JavaScript;

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using MotiffLab.CoreBridge;
using MotiffLab.MusicCore;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
foreach (string file in new[] { "fur-elise-97-bars.musicxml", "large-5000-notes.mid" })
{
    byte[] data = await http.GetByteArrayAsync($"assets/{file}");
    string report = CoreBenchmark.Run(file, data, 20, Words.Russian);
    Page.Show(report);
}

Page.Show("DONE");

internal static partial class Page
{
    [JSImport("globalThis.showReport")]
    public static partial void Show(string text);
}