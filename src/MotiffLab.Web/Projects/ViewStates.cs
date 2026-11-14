using System.Globalization;
using System.Text.Json;

namespace MotiffLab.Web.Projects;

/// <summary>
/// A place a view is scrolled to, which a new layout keeps (Scripts/score/view-anchor.ts): on the notes the system at the top
/// of the view, by the tick of its first bar, on a PDF the page there, and how far into its height the top of the view is.
/// </summary>
public sealed record ViewPlace(double Key, double Fraction);

/// <summary>The roll's zoom and view as it drew them last (Scripts/roll/roll-view.ts <c>ViewFrame</c>).</summary>
public sealed record RollFrame(double Zoom, double RowScale, double Scroll, double PitchTop);

/// <summary>
/// The "Notes" view of a piece as the player left it for its PDF (PDF | Notes, #511 section 10): the selection, also a bar
/// with no notes, as its span in ticks; where the score was scrolled to; where the music stood and whether Цикл looped it;
/// and the roll's drawer with its zoom and view. Kept on this device by the piece, never in the project.
/// </summary>
public sealed record NotesView(long? SelectionOnset, long? SelectionEnd, ViewPlace? Place, long Position, bool Loop, bool RollOpen, RollFrame? Roll);

/// <summary>
/// The views of a piece kept on this device while the other one shows (PDF | Notes, #511 section 10), each by the piece's
/// id. The "Notes" view is brought back when the player comes back to it from the PDF; the PDF's place whenever it opens
/// at no page asked for, as its page always was.
/// </summary>
public static class ViewStates
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>The "view" of the workspace's address that brings the "Notes" view back as it was left.</summary>
    public const string NotesAsLeft = "notes";

    /// <summary>The "Notes" view kept for a piece, or null for none or one that cannot be read.</summary>
    public static async Task<NotesView?> NotesViewAsync(this Preferences preferences, string projectId)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return Read<NotesView>(await preferences.GetAsync(NotesKey(projectId)));
    }

    public static Task KeepNotesViewAsync(this Preferences preferences, string projectId, NotesView view)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.SetAsync(NotesKey(projectId), JsonSerializer.Serialize(view, s_json));
    }

    /// <summary>
    /// Where a PDF was left: the place kept with its page, while that is still the page last under the bar (the page alone
    /// is kept as the pages scroll, so a PDF scrolled on and left another way opens at the top of its page); else null.
    /// </summary>
    public static async Task<ViewPlace?> PdfPlaceAsync(this Preferences preferences, string projectId)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ViewPlace? place = Read<ViewPlace>(await preferences.GetAsync(PdfKey(projectId)));
        string? page = await preferences.GetAsync(PdfPageKey(projectId));
        return place is not null && (page is null || page == PageText((int)place.Key)) ? place : null;
    }

    /// <summary>Keeps the place a PDF is left at, with its page.</summary>
    public static async Task KeepPdfPlaceAsync(this Preferences preferences, string projectId, ViewPlace place)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(place);
        await preferences.SetAsync(PdfKey(projectId), JsonSerializer.Serialize(place, s_json));
        await KeepPdfPageAsync(preferences, projectId, (int)place.Key);
    }

    /// <summary>Keeps the page under the bar as the PDF scrolls.</summary>
    public static Task KeepPdfPageAsync(this Preferences preferences, string projectId, int page)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.SetAsync(PdfPageKey(projectId), PageText(page));
    }

    private static string PageText(int page) => page.ToString(CultureInfo.InvariantCulture);

    /// <summary>The page last under the bar, as the PDF view always kept it.</summary>
    private static string PdfPageKey(string id) => $"pdf.page.{id}";

    private static string PdfKey(string id) => $"pdf.view.{id}";

    private static string NotesKey(string id) => $"notes.view.{id}";

    private static T? Read<T>(string? text)
        where T : class
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, s_json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}