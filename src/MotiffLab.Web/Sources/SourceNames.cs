using System.Globalization;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Settings;

namespace MotiffLab.Web.Sources;

/// <summary>
/// The new sources (version 0.6; decision record 0030; sources sheet, 2026-11-14): the switch of all the piece's views and
/// the Sources sheet of Originals and Notes, behind <see cref="Setting"/> (music-core's <see cref="SettingsCatalogue"/>),
/// off by default.
/// </summary>
public static class SourcesExperiment
{
    /// <summary>The experiment's switch, the same key as on the iPad.</summary>
    public const string Setting = "experimental.sources";

    /// <summary>Whether the experiment is on on this device.</summary>
    public static bool IsOn(DeviceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.IsOn(Setting);
    }

    /// <summary>
    /// Whether the web's «N версии» label, which opened Sources too, gives way to the Sources button while the experiment is
    /// on (sources sheet, open question 2: recommended yes; flip it here if Andrei says no).
    /// </summary>
    public const bool ButtonReplacesVersionsLabel = true;
}

/// <summary>A view of the switch as the web shows it: what it is, its word, its pictogram and the file it is grouped under.</summary>
/// <param name="Key">"kind:id", unique among the views (a recording's sound and the notes heard in it share an id).</param>
/// <param name="Line">Under the word in the menu: the files and date of the working notes, or a Notes' engine and date.</param>
public sealed record SwitchView(string Key, string Id, ViewKind Kind, string Word, string Icon, string IconLabel, string? Group, string? GroupName, string? GroupIcon, bool Working, string? Line);

/// <summary>The words and pictograms of the Sources sheet and the switch, from music-core's <see cref="PieceViews"/>.</summary>
public static class SourceNames
{
    /// <summary>The key of a view: its kind and its id.</summary>
    public static string KeyOf(ViewKind kind, string id) => $"{kind.ToString().ToLowerInvariant()}:{id}";

    /// <summary>The pictogram of what made a view (<see cref="SourceView.Operation"/>, <see cref="NotesProvenance.Op"/>).</summary>
    public static string Icon(string operation) => operation switch
    {
        NotesProvenance.PdfRecognition => "fromPdf",
        NotesProvenance.AudioRecognition => "fromRec",
        NotesProvenance.Assembly => "assemble",
        "pdf" => "pages",
        "recording" => "mic",
        _ => "fromFile",
    };

    /// <summary>The pictogram's accessible name: «Распознано с PDF», «Распознано из записи», «Сборка», «Импорт».</summary>
    public static string IconLabel(UiText text, string operation)
    {
        ArgumentNullException.ThrowIfNull(text);
        return operation switch
        {
            NotesProvenance.PdfRecognition => text["sources.opPdf"],
            NotesProvenance.AudioRecognition => text["sources.opRecording"],
            NotesProvenance.Assembly => text["sources.opAssembly"],
            "pdf" => text["pdf.viewPdfShort"],
            "recording" => text["originals.kindAudio"],
            _ => text["sources.opImport"],
        };
    }

    /// <summary>An original's pictogram: pages for a PDF or photos, the microphone for a recording, a file for the rest.</summary>
    public static string OriginalIcon(OriginalKind kind) => kind switch
    {
        OriginalKind.Pdf or OriginalKind.Images => "pages",
        OriginalKind.Audio => "mic",
        _ => "file",
    };

    /// <summary>A day as the sheet says it: «13.11».</summary>
    public static string Day(DateTimeOffset? at) => at is { } when ? when.ToLocalTime().ToString("dd.MM", CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// The title of Notes: the name of the file they were made from, or «A + B» for an assembly; the player's name, else the
    /// piece's, when no file in the piece made them.
    /// </summary>
    public static string NotesTitle(PieceViews views, SourceNotes notes, string piece)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(notes);
        string[] files = [.. notes.Provenance.From
            .Where(static source => source.Project is null)
            .Select(source => views.Originals.FirstOrDefault(original => original.Original.Id == source.Original)?.Original.Shown)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];
        return files.Length > 0 ? string.Join(" + ", files) : notes.Name ?? piece;
    }

    /// <summary>The caption under Notes: «homr 0.7.0 · 13.11 · 14 правок», or for an assembly «14.11 · 3 переноса».</summary>
    public static string NotesLine(UiText text, SourceNotes notes, int carries)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(notes);
        List<string> parts = [];
        if (notes.Provenance.Engine is { Length: > 0 } engine)
        {
            parts.Add(engine);
        }

        if (Day(notes.Provenance.At) is { Length: > 0 } day)
        {
            parts.Add(day);
        }

        if (notes.Provenance.Op == NotesProvenance.Assembly && carries > 0)
        {
            parts.Add(text.Count("assembly.carries", carries));
        }
        else if (notes.Edits > 0)
        {
            parts.Add(text.Count("sources.edits", notes.Edits));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The caption under an original: «PDF · 4 страницы», «Запись · 2:41», or what it is.</summary>
    public static string OriginalLine(UiText text, PieceOriginal original, int? pages)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(original);
        return original.Kind switch
        {
            OriginalKind.Pdf when pages is > 0 => text.Count("sources.pdfPages", pages.Value),
            OriginalKind.Images when pages is > 0 => text.Count("sources.photoPages", pages.Value),
            OriginalKind.Audio when original.DurationMs is { } ms => text.Format("sources.recordingLine", Clock(ms)),
            OriginalKind.Pdf => text["originals.kindPdf"],
            OriginalKind.Images => text["originals.kindImages"],
            OriginalKind.Audio => text["originals.kindAudio"],
            OriginalKind.Midi => "MIDI",
            _ => "MusicXML",
        };
    }

    /// <summary>A length as «2:41».</summary>
    public static string Clock(long milliseconds)
    {
        long seconds = Math.Max(0, milliseconds) / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>The word of a view in the switch: «Ноты», «PDF» (or «Фото»), «Исполнение».</summary>
    public static string ViewWord(UiText text, SourceView view, PieceViews views)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(views);
        return view.Kind switch
        {
            ViewKind.Notes => text["sources.roleNotes"],
            ViewKind.Performance => text["sources.rolePlaying"],
            _ => views.Originals.FirstOrDefault(original => original.Original.Id == view.Id)?.Original.Kind == OriginalKind.Images ? text["originals.kindImages"] : text["pdf.viewPdfShort"],
        };
    }

    /// <summary>
    /// The views of the switch the web can show, in music-core's order, with their words: <paramref name="canShow"/> leaves
    /// out a view this browser cannot open (a recording's sound with no notes heard in it, a PDF whose bytes are elsewhere)
    /// and the hidden ones go too.
    /// </summary>
    public static IReadOnlyList<SwitchView> Switch(UiText text, PieceViews views, string piece, int carries, Func<SourceView, bool> canShow)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(canShow);
        List<SwitchView> shown = [];
        foreach (SourceView view in views.Views.Where(view => !view.Hidden && canShow(view)))
        {
            SourceOriginal? group = view.Group is { } id ? views.Originals.FirstOrDefault(original => original.Original.Id == id) : null;
            SourceNotes? notes = view.Kind == ViewKind.Notes ? views.Notes.FirstOrDefault(each => each.Id == view.Id) : null;
            string? line = notes is null ? null
                : view.Working ? Joined(NotesTitle(views, notes, piece), Day(notes.Provenance.At))
                : Joined(notes.Provenance.Engine ?? (group is null ? NotesTitle(views, notes, piece) : null), Day(notes.Provenance.At));
            shown.Add(new SwitchView(KeyOf(view.Kind, view.Id), view.Id, view.Kind, ViewWord(text, view, views), Icon(view.Operation), IconLabel(text, view.Operation),
                view.Group, group?.Original.Shown, group is null ? null : OriginalIcon(group.Original.Kind), view.Working, line));
        }

        return shown;
    }

    private static string Joined(params string?[] parts) => string.Join(" · ", parts.Where(static part => !string.IsNullOrEmpty(part)));
}