using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Projects;

/// <summary>
/// Where a piece's originals are, in words (version 0.3, local-first): the badge on an original in the Sources sheet, and
/// the one on a library line whose PDF or recording is not in this browser, so a line never looks whole when its
/// original is only on another device (Andrei, 2026-11-11 21:16 UTC).
/// </summary>
public static class OriginalPlaces
{
    /// <summary>«В файле пьесы», «На этом устройстве», «Только на iPad», «Только в другом браузере», «Нет ни на одном устройстве».</summary>
    public static string Text(UiText text, OriginalWhere where)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text[where switch
        {
            OriginalWhere.InFile => "originals.inFile",
            OriginalWhere.Here => "originals.here",
            OriginalWhere.OnIpad => "originals.onIpad",
            OriginalWhere.InBrowser => "originals.inBrowser",
            _ => "originals.nowhere",
        }];
    }

    /// <summary>
    /// The library's badge for a piece: «PDF: только на iPad», «Запись: только в другом браузере», for its first PDF, photos
    /// or recording whose bytes are not here; null when every one is here, or the row was saved before originals were kept.
    /// </summary>
    /// <param name="heldHere">The recordings this browser holds, as "&lt;project&gt;/&lt;original&gt;".</param>
    public static string? Badge(UiText text, ProjectSummary summary, IReadOnlySet<string> heldHere)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(heldHere);
        foreach (OriginalSummary original in summary.Originals.Where(static original => original.Kind is OriginalKind.Pdf or OriginalKind.Images or OriginalKind.Audio))
        {
            OriginalWhere where = original.Where(HoldingDevice.Web, heldHere.Contains($"{summary.Id}/{original.Id}"));
            if (where is OriginalWhere.InFile or OriginalWhere.Here)
            {
                continue;
            }

            string kind = text[original.Kind switch
            {
                OriginalKind.Pdf => "originals.kindPdf",
                OriginalKind.Images => "originals.kindImages",
                _ => "originals.kindAudio",
            }];
            string place = Text(text, where);
            return $"{kind}: {char.ToLower(place[0], text.Culture)}{place[1..]}";
        }

        return null;
    }
}