using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Automatic bookmarks, part of the experiment Several pieces in a score or recording (Settings · Experimental, v0.5;
/// first its own switch, Andrei, 2026-11-11): in a long score or a recording
/// music-core proposes where the pieces, movements and large sections begin (<see cref="SectionFinder"/>), and the
/// Contents list them, marked as found automatically, above the player's bookmarks. A proposal is never saved: Confirm
/// saves it as an ordinary Structure bookmark through the same path as any other; Move and Dismiss change the proposals
/// for as long as the piece is open.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The experiment's switch in Settings · Experimental, off by default and kept on the device: the one for several pieces.</summary>
    private const string AutoBookmarksSetting = PartPlace.Setting;

    /// <summary>The piece as imported, which the proposals read its bar lines, texts, keys and tempo from.</summary>
    private ImportedPiece? _sectionSource;

    /// <summary>The proposals for the notes as they are, with the player's moves and dismissals; read again when the notes change.</summary>
    private (IReadOnlyList<Note> Notes, FoundSections Found)? _found;

    private bool AutoBookmarksOn => SettingsStore.IsOn(AutoBookmarksSetting);

    /// <summary>The proposals while the experiment is on; none without a piece.</summary>
    private FoundSections Found
    {
        get
        {
            if (!AutoBookmarksOn || _sectionSource is null || _notes is null || _score is null)
            {
                return FoundSections.None;
            }

            if (_found is { } kept && ReferenceEquals(kept.Notes, _notes))
            {
                return kept.Found;
            }

            FoundSections found = SectionFinder.Find(SectionEvidence.Of(_sectionSource, _notes, _score.Tempos));
            _found = (_notes, found);
            return found;
        }
    }

    /// <summary>The proposals no bookmark starts at yet.</summary>
    private IReadOnlyList<FoundSection> PendingSections => Found.Pending(Bookmarks);

    /// <summary>A new piece: its own proposals, read when the Contents first ask for them.</summary>
    private void ReadSectionsOf(ImportedPiece piece)
    {
        _sectionSource = piece;
        _found = null;
    }

    private void ChangeFound(Func<FoundSections, FoundSections> change)
    {
        if (_found is { } kept)
        {
            _found = (kept.Notes, change(kept.Found));
        }
    }

    /// <summary>A proposal tapped: the score goes there and selects it, as a bookmark does.</summary>
    private Task GoToFoundAsync(FoundSection section)
    {
        _bookmarkDraft = null;
        return SelectAsync(section.Span, note: null);
    }

    /// <summary>Confirm: the proposal becomes an ordinary bookmark in Structure, titled and spanned as music-core proposed it.</summary>
    private Task ConfirmFoundAsync(FoundSection section)
    {
        string title = section.TitleIn(Words);
        return SaveMarksAsync(
            project => project.AddBookmark(new Bookmark(project.NextBookmarkId(), title, BookmarkCategory.StructureId, section.Span, FromPhrase: false, DateTimeOffset.Now)),
            Text.Format("found.confirmed", title));
    }

    /// <summary>Dismiss: the proposal goes, its bars join the section before it, for as long as the piece is open.</summary>
    private void DismissFound(FoundSection section)
    {
        ChangeFound(found => found.Dismiss(section.Number));
        _marksStatus = Text.Format("found.dismissed", section.NameIn(Words));
    }

    private bool CanMoveFound(int number, int bar) => Found.CanMove(number, bar);

    /// <summary>Move: the section starts at another bar, and the one before it ends there.</summary>
    private void MoveFound((int Number, int Bar) move)
    {
        if (!Found.CanMove(move.Number, move.Bar))
        {
            return;
        }

        ChangeFound(found => found.Move(move.Number, move.Bar));
        _marksStatus = Text.Format("found.moved", move.Bar);
    }
}