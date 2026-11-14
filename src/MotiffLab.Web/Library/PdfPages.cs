using Microsoft.JSInterop;

namespace MotiffLab.Web.Library;

/// <summary>
/// A PDF piece's pages drawn one at a time as images for the scan server (version 0.5, batch recognition): a thin handle on
/// <c>Scripts/import/pdf-pages.ts</c>, which draws them with pdf.js at 300 dpi as the scan server would. One PDF at a time.
/// </summary>
public sealed class PdfPages(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>Opens the PDF; its number of pages, or 0 when it cannot be read.</summary>
    public async Task<int> OpenAsync(byte[] pdf) => await (await ModuleAsync()).InvokeAsync<int>("openPages", pdf);

    /// <summary>One page, from 1, as a PNG image; null when it cannot be drawn.</summary>
    public async Task<byte[]?> ImageAsync(int page) => await (await ModuleAsync()).InvokeAsync<byte[]?>("pageImage", page);

    public async Task CloseAsync() => await (await ModuleAsync()).InvokeVoidAsync("closePages");

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/import/pdf-pages.js");
}