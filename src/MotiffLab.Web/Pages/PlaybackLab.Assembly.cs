using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Versions;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Roll;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Assembling the player's own version from the piece's sources (decision record 0024; the mockups «Сборка своей версии»).
/// The score editor stays as it is while the piece has one source. Once a version is added, one quiet card offers to
/// compare; comparing shows each source in its own bars on the same page, chosen by a chip in the pill («Ноты», each
/// version, «Как было»), with a selection from note to note, a card at the selection, the carry sheet with its ghost, the
/// lines of «Происхождение», the journal with «Вернуть что было», and «Сделать нотами», the one rule that changes the piece.
/// Everything runs on the device.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The chip of the player's notes as the journal builds them («Ноты»).</summary>
    private const string NotesSource = "notes";

    /// <summary>The chip of the notes before the assembly («Как было», «Ноты до сборки» once applied).</summary>
    private const string BeforeSource = "before";

    /// <summary>The colours of the versions' chips and lines, in the order they were added; the notes have none.</summary>
    private static readonly string[] s_tones = ["plum", "teal", "amber", "green"];

    /// <summary>How long the card after adding a version stays before it goes by itself.</summary>
    private static readonly TimeSpan s_offerFor = TimeSpan.FromSeconds(10);

    private AssemblyWork? _work;

    /// <summary>What the work was built over: the project, its versions and the base; it is built again when they change.</summary>
    private (string ProjectId, IReadOnlyList<SavedPerformance> Versions, SourceMusic? Before, IReadOnlyList<Note>? Notes)? _workOver;

    /// <summary>Each version's music, read once from its MIDI.</summary>
    private readonly Dictionary<string, (byte[] Midi, SourceMusic? Music)> _versionMusic = new(StringComparer.Ordinal);

    /// <summary>Whether the comparison is open: the chips in the pill, the card at the selection, the carry sheet.</summary>
    private bool _assembling;

    /// <summary>The source the page shows while comparing.</summary>
    private string _assemblyShown = NotesSource;

    /// <summary>The selection in each source, in that source's own time.</summary>
    private readonly Dictionary<string, TickSpan> _picks = new(StringComparer.Ordinal);

    /// <summary>The note a selection from note to note started at: the next note tapped ends it.</summary>
    private (string Source, Note Note)? _pickFrom;

    /// <summary>The carry being made in the sheet; null while the sheet is shut.</summary>
    private CarryDraft? _carry;

    private (CarryDraft Draft, AssemblyWork Work, string Language, CarrySheet Sheet)? _carrySheet;

    /// <summary>Whether «Происхождение» draws the lines under the notes carries put in.</summary>
    private bool _origins;

    /// <summary>Whether the journal sheet is open, and the step whose return it shows.</summary>
    private bool _journalOpen;

    private int? _revertShown;

    private bool _applyOpen;

    /// <summary>The card after a version was added: the version and what it says; it goes after ten seconds or on «Позже».</summary>
    private (string VersionId, string Text, DateTimeOffset Until)? _offer;

    private CancellationTokenSource? _offerTimer;

    /// <summary>The engraving and the sound of each source's music, kept while the music is the same.</summary>
    private readonly Dictionary<SourceMusic, (ScoreSource Score, PlaybackScore Sound)> _assemblyScores = new(ReferenceEqualityComparer.Instance);

    private ScoreView? _assemblyView;

    /// <summary>A carry being made: where from, where to and how.</summary>
    /// <param name="Replace">The notes it can replace; null when it can only be inserted.</param>
    /// <param name="Playhead">Where an insert goes.</param>
    /// <param name="ReturnTo">The source shown before the sheet opened, shown again on «Отмена».</param>
    private sealed record CarryDraft(string SourceId, TickSpan From, AssemblyPlace? Replace, Tick Playhead, bool Insert, int Transpose, string ReturnTo, bool Keep = false);

    /// <summary>
    /// The work of the assembly over the piece as it is now: built again, with its history, when the versions, the notes
    /// before the assembly or the piece's notes change. Null without a project with notes.
    /// </summary>
    private AssemblyWork? Work
    {
        get
        {
            if (_project is not { } project || _notes is null || _meter is null || _score is null)
            {
                return null;
            }

            PieceAssembly kept = project.Assembly;
            IReadOnlyList<Note>? notes = kept.Before is null ? _notes : null;
            if (_work is not null && _workOver is { } over && over.ProjectId == project.Id && ReferenceEquals(over.Versions, project.Performances)
                && ReferenceEquals(over.Before, kept.Before) && ReferenceEquals(over.Notes, notes))
            {
                return _work;
            }

            SourceMusic baseMusic = kept.Before ?? SourceMusic.Of(_notes, _meter, _score.End, _score.Tempos, _keys);
            Dictionary<string, SourceMusic> sources = new(StringComparer.Ordinal);
            Dictionary<string, string> names = new(StringComparer.Ordinal);
            foreach (SavedPerformance version in project.Performances)
            {
                if (VersionMusic(version) is { } music)
                {
                    sources[version.Id] = music;
                    names[version.Id] = version.Name;
                }
            }

            _work = _work is not null && _workOver?.ProjectId == project.Id
                ? _work.Over(baseMusic, sources, names)
                : new AssemblyWork(baseMusic, sources, names, kept.Steps, kept.Next);
            _workOver = (project.Id, project.Performances, kept.Before, notes);
            return _work;
        }
    }

    private SourceMusic? VersionMusic(SavedPerformance version)
    {
        if (_versionMusic.TryGetValue(version.Id, out var read) && ReferenceEquals(read.Midi, version.Midi))
        {
            return read.Music;
        }

        SourceMusic? music = SourceMusic.OfVersion(version);
        _versionMusic[version.Id] = (version.Midi, music);
        return music;
    }

    /// <summary>The steps the journal holds, for the label by the title.</summary>
    private int AssemblySteps => _project?.Assembly.Steps.Count ?? 0;

    /// <summary>«+1 версия · 3 переноса» by the title: the only trace of the assembly in everyday work.</summary>
    private string? VersionsLabel
    {
        get
        {
            if (_project is not { Performances.Count: > 0 } project)
            {
                return null;
            }

            string label = Text.Count("assembly.plusVersions", project.Performances.Count);
            int carries = project.Assembly.Steps.Count(step => step is CarryStep);
            return carries > 0 ? $"{label} · {Text.Count("assembly.carries", carries)}" : label;
        }
    }

    private bool HasBefore => _project is { } project && (project.Assembly.Before is not null || project.Assembly.Steps.Count > 0);

    private string BeforeName => Text[_project?.Assembly.Before is not null ? "assembly.beforeApplied" : "assembly.before"];

    /// <summary>The name of a source on its chip and in the card.</summary>
    private string SourceName(string id) => id switch
    {
        NotesSource => Text["assembly.notes"],
        BeforeSource => BeforeName,
        _ => _project?.FindPerformance(id)?.Name ?? id,
    };

    /// <summary>A version's tone, by the order it was added; the notes and «Как было» are graphite.</summary>
    private string ToneOf(string id)
    {
        int index = _project?.Performances.ToList().FindIndex(version => version.Id == id) ?? -1;
        return index < 0 ? "graphite" : s_tones[index % s_tones.Length];
    }

    /// <summary>The chips of the pill while comparing: «Ноты», each version, and «Как было» once the journal holds a step.</summary>
    private IReadOnlyList<AssemblyChip> AssemblyChips
    {
        get
        {
            List<string> ids = [NotesSource, .. (_project?.Performances ?? []).Where(version => Work?.Source(version.Id) is not null).Select(version => version.Id)];
            if (HasBefore)
            {
                ids.Add(BeforeSource);
            }

            bool playing = Player.State != PlaybackState.Stopped;
            return [.. ids.Select((id, index) => new AssemblyChip(
                id,
                SourceName(id),
                ToneOf(id),
                _picks.TryGetValue(id, out TickSpan pick) && AssemblyMusic(id) is { } music ? Text.Count("assembly.notesCount", music.NotesIn(pick.Onset, pick.End).Count) : null,
                index + 1,
                id == _assemblyShown,
                playing && id == _assemblyShown))];
        }
    }

    /// <summary>A source's music: the notes as the journal builds them (or the ghost while the sheet is open), the notes before, or a version.</summary>
    private SourceMusic? AssemblyMusic(string id) => id switch
    {
        NotesSource => CarrySheetNow?.Ghost.Music ?? Work?.Result.Music,
        BeforeSource => Work?.Base,
        _ => Work?.Source(id),
    };

    private SourceMusic? ShownMusic => AssemblyMusic(_assemblyShown);

    /// <summary>The source shown, engraved in its own bars and keys.</summary>
    private ScoreSource? AssemblyScore => ShownMusic is { } music ? EngravedAndSounded(music, _assemblyShown).Score : null;

    private (ScoreSource Score, PlaybackScore Sound) EngravedAndSounded(SourceMusic music, string id)
    {
        if (_assemblyScores.TryGetValue(music, out var made))
        {
            return made;
        }

        if (_assemblyScores.Count > 12)
        {
            _assemblyScores.Clear();
        }

        IEnumerable<KeyChange> keys = music.Keys.Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)));
        // A version is headed with its own name, the notes with the piece's.
        string? title = id is NotesSource or BeforeSource ? ScoreTitle.Of(_title) : SourceName(id);
        GeneratedNotation notation = NotationGenerator.Generate(music.Notes, music.Meter, keys, music.End, title);
        made = (ScoreSource.FromNotes(notation), PlaybackScore.Create(music.Notes, music.Tempos, music.Meter, music.End));
        _assemblyScores[music] = made;
        return made;
    }

    /// <summary>The selection in the source shown.</summary>
    private TickSpan? AssemblyPick => _carry is null && _picks.TryGetValue(_assemblyShown, out TickSpan pick) ? pick : null;

    /// <summary>The notes of the selection in the source shown.</summary>
    private IReadOnlyList<Note> PickedNotes => AssemblyPick is { } pick && ShownMusic is { } music ? music.NotesIn(pick.Onset, pick.End) : [];

    /// <summary>The lines under the notes carries put in, in «Происхождение», over the notes only.</summary>
    private IReadOnlyList<ScoreOrigin>? AssemblyOrigins
    {
        get
        {
            if (!_origins || _assemblyShown != NotesSource || _carry is not null || Work is not { } work)
            {
                return null;
            }

            AssemblyNames names = work.NamesIn(Words);
            return [.. work.Steps.OfType<CarryStep>()
                .Select(carry => (carry, outcome: work.Result.OutcomeOf(carry.Number)))
                .Where(entry => entry.outcome is { Applied: true, Span: not null })
                .Select(entry => new ScoreOrigin(entry.outcome!.Span!.Value.Onset.Value, entry.outcome.Span.Value.End.Value, ToneOf(entry.carry.SourceId),
                    Text.Format("assembly.originLine", entry.carry.Number, names.GenitiveOf(entry.carry.SourceId))))];
        }
    }

    /// <summary>The ghost while the sheet is open: where the fragment will lie in the notes.</summary>
    private ScoreGhost? AssemblyGhost => CarrySheetNow is { Outcome.Span: { } span } sheet
        ? new ScoreGhost(span.Onset.Value, span.End.Value, $"{Text["assembly.ghost"]} · {sheet.Title}")
        : null;

    /// <summary>The notes a carry would put in, plum on the ghost.</summary>
    private IReadOnlyList<string>? AssemblyGhostNotes => CarrySheetNow?.Outcome.Put;

    /// <summary>The carry sheet for the draft, worked out again only when the draft, the journal or the language change.</summary>
    private CarrySheet? CarrySheetNow
    {
        get
        {
            if (_carry is not { } draft || _work is not { } work)
            {
                return null;
            }

            if (_carrySheet is { } cached && ReferenceEquals(cached.Draft, draft) && ReferenceEquals(cached.Work, work) && cached.Language == Text.Language)
            {
                return cached.Sheet;
            }

            SourceMusic notes = work.Result.Music;
            AssemblyPlace to = draft.Insert || draft.Replace is null ? AssemblyPlace.InsertAt(notes.Notes, draft.Playhead) : draft.Replace;
            CarryStep step = work.Carry(draft.SourceId, draft.From, to, draft.Transpose, DateTimeOffset.Now, draft.Keep);
            CarrySheet sheet = work.Sheet(step, draft.Replace, draft.Playhead, Words, heard: RecordingVersion(draft.SourceId) is not null);
            _carrySheet = (draft, work, Text.Language, sheet);
            return sheet;
        }
    }

    /// <summary>The title of the bar while comparing: the piece and the source shown.</summary>
    private string AssemblyShownLabel => _carry is not null && _assemblyShown == NotesSource ? Text["assembly.notesGhost"] : SourceName(_assemblyShown);

    /// <summary>Compare: the chips, the card and the sheet come; the page shows <paramref name="show"/>, or the notes.</summary>
    private async Task OpenAssemblyAsync(string? show = null)
    {
        if (Work is null)
        {
            return;
        }

        if (!_assembling && Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
        }

        _assembling = true;
        _offer = null;
        _performanceOpen = false;
        _popover = Popover.None;
        _pillMenu = PillMenu.None;
        _selection = null;
        _rollOpen = false;
        _insightsOpen = false;
        // The versions play as they did before the playback model (v0.5.2): it lets go until the work is done.
        await SyncModelAsync();
        await ShowSourceAsync(show ?? _assemblyShown);
    }

    /// <summary>The work is done: the comparison folds away and the piece plays as it was.</summary>
    private async Task CloseAssemblyAsync()
    {
        if (!_assembling)
        {
            return;
        }

        _assembling = false;
        _carry = null;
        _journalOpen = false;
        _revertShown = null;
        _applyOpen = false;
        _origins = false;
        _pickFrom = null;
        _recordingShown = null;
        _recordingMissing = null;
        ForgetBeside();
        if (_score is not null)
        {
            await Player.LoadAsync(_score);
            await ApplyFilterAsync();
            await SetVariationSourcesAsync();
        }

        await SyncModelAsync();
    }

    /// <summary>
    /// A chip's name: the page shows that source in its own bars, with its own selection; the sound follows the page, and
    /// the same bar of the new source is where playing goes on.
    /// </summary>
    private async Task ShowSourceAsync(string id)
    {
        if (AssemblyMusic(id) is not { } music)
        {
            return;
        }

        SourceMusic? was = ShownMusic;
        Tick position = Player.Position;
        bool playing = Player.State != PlaybackState.Stopped;
        _assemblyShown = id;
        _pickFrom = null;
        (_, PlaybackScore sound) = EngravedAndSounded(music, id);
        await Player.LoadAsync(sound);
        await SoundShownAsync();
        Tick at = _picks.TryGetValue(id, out TickSpan pick) ? pick.Onset : was is null ? Tick.Zero : SameBar(was, music, position);
        await Player.SetLoopAsync(_assemblyLoop && _picks.TryGetValue(id, out TickSpan loop) ? loop : null);
        await Player.SeekAsync(at);
        if (playing)
        {
            await Player.PlayAsync();
        }

        if (_besideSource == id)
        {
            _besideSource = null;
        }

        await FollowBesideAsync();
    }

    /// <summary>The start of the bar with the same number in another source.</summary>
    private static Tick SameBar(SourceMusic from, SourceMusic to, Tick position)
    {
        if (from.Bars.Count == 0 || to.Bars.Count == 0)
        {
            return Tick.Zero;
        }

        int index = Math.Max(0, from.Bars.ToList().FindLastIndex(bar => bar.Span.Onset <= position));
        return to.Bars[Math.Min(index, to.Bars.Count - 1)].Span.Onset;
    }

    /// <summary>A chip's ▶: that source is heard, shown with it, from its selection; pressed again, it stops.</summary>
    private async Task HearSourceAsync(string id)
    {
        if (id == _assemblyShown && Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
            return;
        }

        if (id != _assemblyShown)
        {
            await ShowSourceAsync(id);
        }

        if (_picks.TryGetValue(id, out TickSpan pick) && !pick.Contains(Player.Position))
        {
            await Player.SeekAsync(pick.Onset);
        }

        await Player.PlayAsync();
    }

    private bool _assemblyLoop;

    /// <summary>«Цикл»: the selection in the source shown plays round, or the whole source on.</summary>
    private async Task ToggleAssemblyLoopAsync()
    {
        _assemblyLoop = !_assemblyLoop;
        await Player.SetLoopAsync(_assemblyLoop ? AssemblyPick : null);
    }

    /// <summary>«Слушать» on the card: the selection plays round, from its start.</summary>
    private async Task HearPickAsync()
    {
        if (AssemblyPick is not { } pick)
        {
            return;
        }

        _assemblyLoop = true;
        await Player.SetLoopAsync(pick);
        await Player.SeekAsync(pick.Onset);
        await Player.PlayAsync();
    }

    private async Task SetPickAsync(string id, TickSpan? pick)
    {
        if (pick is { } span)
        {
            _picks[id] = span;
        }
        else
        {
            _picks.Remove(id);
        }

        if (id == _assemblyShown && _assemblyLoop)
        {
            await Player.SetLoopAsync(pick);
        }

        await FollowBesideAsync();
    }

    /// <summary>
    /// A note tapped: the selection starts at it; the next note tapped ends it there, from note to note, whichever comes
    /// first. In «Происхождение» a note a carry put in selects the whole carry.
    /// </summary>
    private async Task OnAssemblyNotePickedAsync(NotePoint point)
    {
        if (_carry is not null || ShownMusic is not { } music)
        {
            return;
        }

        Note? note = music.Notes
            .Where(candidate => candidate.Span.Onset.Value <= point.Tick && point.Tick < candidate.Span.End.Value)
            .OrderBy(candidate => Math.Abs(candidate.Pitch.MidiNumber - point.Pitch))
            .ThenBy(candidate => Math.Abs(candidate.Span.Onset.Value - point.Tick))
            .FirstOrDefault();
        if (note is null)
        {
            return;
        }

        if (_origins && _assemblyShown == NotesSource && Work is { } work && work.Result.OriginOf(note.Id) is > 0 and int number
            && work.Result.OutcomeOf(number) is { Applied: true, Span: { } carried } && work.Steps.First(step => step.Number == number) is CarryStep)
        {
            _pickFrom = null;
            await SetPickAsync(NotesSource, carried);
            return;
        }

        if (_pickFrom is { } from && from.Source == _assemblyShown && from.Note.Id != note.Id)
        {
            Note first = from.Note.Span.Onset <= note.Span.Onset ? from.Note : note;
            Note last = ReferenceEquals(first, note) ? from.Note : note;
            long end = Math.Max(last.Span.End.Value, first.Span.End.Value);
            await SetPickAsync(_assemblyShown, TickSpan.FromBounds(first.Span.Onset, new Tick(end)));
            _pickFrom = null;
            return;
        }

        _pickFrom = (_assemblyShown, note);
        await SetPickAsync(_assemblyShown, note.Span);
    }

    /// <summary>A bar tapped away from its notes: the bar, or with Shift from the selection to it.</summary>
    private async Task OnAssemblyBarPickedAsync((Tick Start, bool Extend) picked)
    {
        if (_carry is not null || ShownMusic is not { } music)
        {
            return;
        }

        TickSpan bar = music.BarAt(picked.Start).Bar.Span;
        TickSpan span = picked.Extend && AssemblyPick is { } pick
            ? TickSpan.FromBounds(pick.Onset < bar.Onset ? pick.Onset : bar.Onset, pick.End > bar.End ? pick.End : bar.End)
            : bar;
        _pickFrom = null;
        await SetPickAsync(_assemblyShown, span);
    }

    /// <summary>Bars chosen by a drag or a handle: from the first to the end of the last.</summary>
    private async Task OnAssemblyBarsChosenAsync((Tick First, Tick Last, bool ByHandle) chosen)
    {
        if (_carry is not null || ShownMusic is not { } music)
        {
            return;
        }

        Tick first = chosen.First < chosen.Last ? chosen.First : chosen.Last;
        Tick last = chosen.First < chosen.Last ? chosen.Last : chosen.First;
        _pickFrom = null;
        await SetPickAsync(_assemblyShown, TickSpan.FromBounds(music.BarAt(first).Bar.Span.Onset, music.BarAt(last).Bar.Span.End));
    }

    private async Task OnAssemblyPaperTappedAsync()
    {
        if (_carry is null)
        {
            _pickFrom = null;
            await SetPickAsync(_assemblyShown, null);
        }
    }

    /// <summary>«9 нот · такты 15–16 Кавера»: what the selection holds, for the card's head.</summary>
    private string PickTitle
    {
        get
        {
            if (AssemblyPick is not { } pick || ShownMusic is not { } music)
            {
                return "";
            }

            string bars = CarrySheet.BarsText(music, pick.Onset, pick.End, Words);
            string of = _assemblyShown switch
            {
                NotesSource => Text["assembly.ofNotes"],
                BeforeSource => Text["assembly.ofBefore"],
                _ => Words.T($"of {SourceName(_assemblyShown)}", AssemblyWords.Genitive(SourceName(_assemblyShown))),
            };
            string title = $"{Text.Count("assembly.notesCount", PickedNotes.Count)} · {bars} {of}";
            return PickTimes is { } times ? $"{Text.Count("assembly.notesCount", PickedNotes.Count)} · {times} · {bars} {of}" : title;
        }
    }

    /// <summary>«от ми² · такт 15, доля 1 — до соль¹ · такт 16, доля 3»: the edges of a selection from note to note.</summary>
    private string? PickEdges
    {
        get
        {
            if (PickedNotes is not { Count: > 0 } notes || ShownMusic is not { } music)
            {
                return null;
            }

            NoteNaming naming = NoteNames.For(SettingsStore[NoteNames.Setting], Words);
            string Edge(Note note)
            {
                int fifths = music.Keys.LastOrDefault(key => key.At <= note.Span.Onset)?.Fifths ?? 0;
                (int bar, int beat, _) = music.PositionOf(note.Span.Onset);
                return Text.Format("assembly.edge", NoteNames.Name(note.Pitch.MidiNumber, Math.Clamp(fifths, -7, 7), naming), bar, beat);
            }

            Note first = notes.MinBy(note => (note.Span.Onset.Value, -note.Pitch.MidiNumber))!;
            Note last = notes.MaxBy(note => (note.Span.Onset.Value, note.Pitch.MidiNumber))!;
            return ReferenceEquals(first, last) ? Edge(first) : Text.Format("assembly.edges", Edge(first), Edge(last));
        }
    }

    /// <summary>«Похоже на такты 15–16 ваших нот · частично»: where a version's selection lies in the notes, before deciding.</summary>
    private (TickSpan Span, string Text)? PickSimilar
    {
        get
        {
            if (_assemblyShown is NotesSource or BeforeSource || AssemblyPick is not { } pick || Work is not { } work || work.Source(_assemblyShown) is null)
            {
                return null;
            }

            if (work.Match(_assemblyShown).Similar(pick) is not { } similar)
            {
                return (default, Text["assembly.similarNone"]);
            }

            string bars = CarrySheet.BarsText(work.Result.Music, similar.Span.Onset, similar.Span.End, Words);
            return (similar.Span, Text.Format("assembly.similar", bars, AssemblyMatch.Word(similar.Kind, Words)));
        }
    }

    /// <summary>On the notes: the carry that put the selected notes in, if one did.</summary>
    private CarryStep? PickCarry
    {
        get
        {
            if (_assemblyShown != NotesSource || Work is not { } work)
            {
                return null;
            }

            int number = PickedNotes.Select(note => work.Result.OriginOf(note.Id)).FirstOrDefault(origin => origin > 0);
            return number > 0 ? work.Steps.FirstOrDefault(step => step.Number == number) as CarryStep : null;
        }
    }

    /// <summary>The card of the carry under the selection in «Происхождение»: the same lines as the carry sheet.</summary>
    private IReadOnlyList<JournalLine> PickCarryLines => PickCarry is { } carry && Work is { } work ? AssemblyJournal.Card(work, carry.Number, Words) : [];

    /// <summary>«Показать»: the notes are shown with the place the version's selection is like.</summary>
    private async Task ShowSimilarAsync()
    {
        if (PickSimilar is not { Span.Duration: > 0 } similar)
        {
            return;
        }

        _picks[NotesSource] = similar.Span;
        await ShowSourceAsync(NotesSource);
    }

    /// <summary>«Показать в Кавере»: the version is shown with the fragment the carry took.</summary>
    private async Task ShowCarryInSourceAsync(CarryStep carry)
    {
        _picks[carry.SourceId] = carry.From;
        _journalOpen = false;
        await ShowSourceAsync(carry.SourceId);
    }

    /// <summary>
    /// «В ноты…»: the sheet opens on what the carry will do, and the page shows the notes as they will be. It replaces the
    /// notes selected in the notes, or else the place the fragment is like; or it goes in at the playhead.
    /// </summary>
    private async Task OpenCarryAsync()
    {
        if (Work is not { } work || AssemblyPick is not { } pick || work.Source(_assemblyShown) is null || PickedNotes.Count == 0)
        {
            return;
        }

        SourceMusic notes = work.Result.Music;
        TickSpan? target = _picks.TryGetValue(NotesSource, out TickSpan inNotes) ? inNotes : PickSimilar is { Span.Duration: > 0 } similar ? similar.Span : null;
        AssemblyPlace? replace = target is { } span && notes.NotesIn(span.Onset, span.End).Count > 0 ? AssemblyPlace.Over(notes.Notes, span) : null;
        Tick playhead = target?.Onset ?? Tick.Zero;
        string from = _assemblyShown;
        // From a recording, the notes stay and what the performer plays otherwise is added, unless the player chooses the notes as heard.
        _carry = new CarryDraft(from, pick, replace, playhead, replace is null, 0, from, replace is not null && RecordingVersion(from) is not null);
        _journalOpen = false;
        await ShowSourceAsync(NotesSource);
    }

    private void ChooseCarryPlace(bool insert)
    {
        if (_carry is { } draft && (insert || draft.Replace is not null))
        {
            _carry = draft with { Insert = insert, Keep = !insert && draft.Keep };
        }
    }

    private void ChooseCarryKeep(bool keep)
    {
        if (_carry is { } draft)
        {
            _carry = draft with { Keep = keep };
        }
    }

    private void ChooseCarryTranspose(int semitones)
    {
        if (_carry is { } draft)
        {
            _carry = draft with { Transpose = semitones };
        }
    }

    /// <summary>The ghost or the fragment itself, round, until stopped.</summary>
    private async Task HearGhostAsync()
    {
        if (CarrySheetNow is not { Outcome.Span: { } span })
        {
            return;
        }

        if (Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
            return;
        }

        await ShowSourceAsync(NotesSource);
        await Player.SetLoopAsync(span);
        await Player.SeekAsync(span.Onset);
        await Player.PlayAsync();
    }

    private async Task CancelCarryAsync()
    {
        if (_carry is not { } draft)
        {
            return;
        }

        _carry = null;
        await ShowSourceAsync(draft.ReturnTo);
    }

    /// <summary>«Перенести»: the carry joins the journal, the notes show where it went, and the toast can take it back.</summary>
    private async Task MakeCarryAsync()
    {
        if (_carry is not { } draft || Work is not { } work)
        {
            return;
        }

        SourceMusic notes = work.Result.Music;
        AssemblyPlace to = draft.Insert || draft.Replace is null ? AssemblyPlace.InsertAt(notes.Notes, draft.Playhead) : draft.Replace;
        CarryStep step = work.Carry(draft.SourceId, draft.From, to, draft.Transpose, DateTimeOffset.Now, draft.Keep);
        _carry = null;
        await ChangeWorkAsync(work.With(step));
        string title = AssemblyJournal.NotesFrom(_work!, step, _work!.NamesIn(Words), Words);
        if (_work!.Result.OutcomeOf(step.Number) is { Span: { } placed })
        {
            _picks[NotesSource] = placed;
        }

        _picks.Remove(draft.SourceId);
        await ShowSourceAsync(NotesSource);
        _mergeToast = (Text.Format("assembly.carried", step.Number, title), UndoAssemblyAsync);
    }

    /// <summary>The journal changed: kept with the piece at once, and the page shows the notes it builds.</summary>
    private async Task ChangeWorkAsync(AssemblyWork work)
    {
        if (_project is not { } project)
        {
            return;
        }

        _work = work;
        _project = project.WithJournal(work.Steps, work.Next);
        _workOver = _workOver is { } over ? over with { Versions = _project.Performances } : null;
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
        if (_assembling && _assemblyShown is NotesSource or BeforeSource)
        {
            await ShowSourceAsync(_assemblyShown);
        }
    }

    private async Task UndoAssemblyAsync()
    {
        if (Work is { CanUndo: true } work)
        {
            _mergeToast = null;
            await ChangeWorkAsync(work.Undo());
        }
    }

    private async Task RedoAssemblyAsync()
    {
        if (Work is { CanRedo: true } work)
        {
            await ChangeWorkAsync(work.Redo());
        }
    }

    /// <summary>⌘Z and ⇧⌘Z (Ctrl+Z, Ctrl+Y) step through the journal while comparing; Escape shuts the sheet or the card.</summary>
    /// <summary>The keys of the assembly, listened to only while comparing: a handler on the page would render it on every key.</summary>
    private EventCallback<KeyboardEventArgs> WorkspaceKeys => _assembling ? EventCallback.Factory.Create<KeyboardEventArgs>(this, OnWorkspaceKeyAsync) : default;

    private async Task OnWorkspaceKeyAsync(KeyboardEventArgs args)
    {
        if (!_assembling)
        {
            return;
        }

        bool command = args.CtrlKey || args.MetaKey;
        switch (args.Key)
        {
            case "z" or "Z" or "я" or "Я" when command && args.ShiftKey:
            case "y" or "Y" or "н" or "Н" when command:
                await RedoAssemblyAsync();
                break;
            case "z" or "Z" or "я" or "Я" when command:
                await UndoAssemblyAsync();
                break;
            case "Escape" when _carry is not null:
                await CancelCarryAsync();
                break;
            case "Escape" when _applyOpen || _journalOpen:
                _applyOpen = false;
                _journalOpen = false;
                _revertShown = null;
                break;
            case "1" or "2" or "3" or "4" or "5" or "6" when !command && _carry is null:
                IReadOnlyList<AssemblyChip> chips = AssemblyChips;
                int index = args.Key[0] - '1';
                if (index < chips.Count)
                {
                    await ShowSourceAsync(chips[index].Id);
                }

                break;
        }
    }

    private void ToggleOrigins()
    {
        _origins = !_origins;
        _journalOpen = false;
        if (_origins && _assemblyShown != NotesSource)
        {
            _ = ShowSourceAsync(NotesSource);
        }
    }

    private void ToggleJournal()
    {
        _journalOpen = !_journalOpen;
        _revertShown = null;
        _carry = null;
    }

    /// <summary>«Вернуть что было» on a step: the journal shows what goes with it and what stays, before anything is done.</summary>
    private void PlanRevert(int number)
    {
        _journalOpen = true;
        _revertShown = number;
        _carry = null;
    }

    private RevertPlan? RevertPlanShown => _revertShown is { } number && Work is { } work && work.Steps.Any(step => step.Number == number) ? work.PlanRevert(number) : null;

    /// <summary>«Вернуть»: the step and every step that leans on it leave the journal, as one change ⌘Z brings back.</summary>
    private async Task RevertAsync(int number)
    {
        if (Work is not { } work || work.Steps.All(step => step.Number != number))
        {
            return;
        }

        RevertPlan plan = work.PlanRevert(number);
        _revertShown = null;
        _picks.Remove(NotesSource);
        await ChangeWorkAsync(work.Revert(number));
        _mergeToast = (Text.Format("assembly.reverted", number, plan.Dependents.Count), UndoAssemblyAsync);
    }

    /// <summary>«Начать заново»: the notes back to the notes before the assembly, as one change ⌘Z brings back.</summary>
    private async Task RestartAssemblyAsync()
    {
        if (Work is not { Steps.Count: > 0 } work)
        {
            return;
        }

        _picks.Remove(NotesSource);
        await ChangeWorkAsync(work.Restart());
        _mergeToast = (Text["assembly.restarted"], UndoAssemblyAsync);
    }

    /// <summary>
    /// «Принять совпавшее»: every bar of a version that matches the notes but is not identical, bar for bar, carried in one
    /// change; bars that only partly match or differ are left to the player.
    /// </summary>
    private async Task AcceptMatchedAsync(string versionId)
    {
        if (Work is not { } work || work.Source(versionId) is null)
        {
            return;
        }

        AssemblyMatch match = work.Match(versionId);
        if (work.Accept(match, DateTimeOffset.Now) is not { } accepted || accepted.Steps.Count == work.Steps.Count)
        {
            _mergeToast = (Text["assembly.acceptNone"], null);
            return;
        }

        int count = accepted.Steps.Count - work.Steps.Count;
        await ChangeWorkAsync(accepted);
        _mergeToast = (Text.Count("assembly.accepted", count), UndoAssemblyAsync);
    }

    /// <summary>The corrections «Принять совпавшее» would carry from a version.</summary>
    private int CorrectionsIn(string versionId) =>
        Work is { } work && work.Source(versionId) is { } source ? work.Match(versionId).CorrectionCount(source, work.Result.Music) : 0;

    /// <summary>A version's card in words: where it matches the notes and where it differs.</summary>
    private string MatchSummary(string versionId) =>
        Work is { } work && work.Source(versionId) is { } source ? work.Match(versionId).Summary(source, work.Result.Music, Words) : Text["assembly.unreadable"];

    /// <summary>«Сделать нотами…»: the dialog says what will change before anything is.</summary>
    private void OpenApply()
    {
        _applyOpen = true;
        _journalOpen = false;
        _carry = null;
    }

    /// <summary>
    /// «Сделать нотами», the one rule that changes the piece (0024, section 7): the notes the journal builds become the
    /// piece's notes, the notes before stay as «Ноты до сборки», and the toast can take it back.
    /// </summary>
    private async Task ApplyAssemblyAsync()
    {
        if (_project is not { } project || Work is not { } work)
        {
            return;
        }

        int places = work.Result.Steps.Count(step => step.Applied);
        Project changed = project.ApplyAssembly(work.Result.Music, work.Base, DateTimeOffset.Now);
        _applyOpen = false;
        await CloseAssemblyAsync();
        await ReopenAsync(changed);
        _mergeToast = (Text.Count("assembly.applied", places), () => ReopenAsync(project));
    }

    /// <summary>The piece opened again as <paramref name="project"/> has it, kept on the device first.</summary>
    private async Task ReopenAsync(Project project)
    {
        await Projects.SaveAsync(project);
        OpenedProject opened = OpenedProject.Open(project);
        _work = null;
        _workOver = null;
        _picks.Clear();
        CurrentPiece.Open(opened.Piece, _filter, project, opened);
    }

    /// <summary>After a version is added: one quiet card for ten seconds, with where it matches the notes.</summary>
    private void OfferComparison(SavedPerformance version)
    {
        string text = Work?.Source(version.Id) is not null ? MatchSummary(version.Id) : Text["assembly.unreadable"];
        _offer = (version.Id, text, DateTimeOffset.Now + s_offerFor);
        _offerTimer?.Cancel();
        _offerTimer = new CancellationTokenSource();
        CancellationToken token = _offerTimer.Token;
        _ = Task.Delay(s_offerFor, token).ContinueWith(
            _ => InvokeAsync(() =>
            {
                if (_offer?.VersionId == version.Id)
                {
                    _offer = null;
                    StateHasChanged();
                }
            }),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void Later()
    {
        _offerTimer?.Cancel();
        _offer = null;
    }

    /// <summary>«Послушать версию»: compared, with the version shown and playing.</summary>
    private async Task HearOfferedAsync()
    {
        if (_offer is not { } offer)
        {
            return;
        }

        await OpenAssemblyAsync(offer.VersionId);
        await HearSourceAsync(offer.VersionId);
    }

    private SavedPerformance? OfferedVersion => _offer is { } offer ? _project?.FindPerformance(offer.VersionId) : null;

    /// <summary>The bars of a source, for its card: «105 тактов».</summary>
    private string BarsOfSource(string id) => AssemblyMusic(id) is { } music ? Text.Count("assembly.bars", music.Bars.Count) : "";

    /// <summary>A step's time in the journal: «7 ноя, 14:02».</summary>
    private string StepTime(DateTimeOffset at) => at.ToLocalTime().ToString(Text.Language == "ru" ? "d MMM, HH:mm" : "d MMM, HH:mm", Text.Culture);

    /// <summary>The journal's rows.</summary>
    private IReadOnlyList<JournalRow> JournalRows => Work is { } work ? AssemblyJournal.Rows(work, Words, NoteNames.For(SettingsStore[NoteNames.Setting], Words)) : [];

    /// <summary>The bars «Сделать нотами» will change.</summary>
    private string ApplyBars => Work is { } work ? AssemblyJournal.ChangedBars(work, Words) : "";

    private IReadOnlyList<JournalLine> ApplyLines => Work is { } work ? AssemblyJournal.Apply(work, Words) : [];

    /// <summary>Forget everything about the assembly of the piece that was open: a new piece starts with none of it shown.</summary>
    private void ForgetAssembly()
    {
        _assembling = false;
        ForgetBeside();
        _assemblyShown = NotesSource;
        _picks.Clear();
        _pickFrom = null;
        _carry = null;
        _carrySheet = null;
        _origins = false;
        _journalOpen = false;
        _revertShown = null;
        _applyOpen = false;
        _assemblyLoop = false;
        _work = null;
        _workOver = null;
        _versionMusic.Clear();
        _assemblyScores.Clear();
    }

    private Task ShowRevertAsync(int? number)
    {
        if (number is { } shown)
        {
            PlanRevert(shown);
        }
        else
        {
            _revertShown = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>«Показать» on a journal row: the notes, with the step's music selected.</summary>
    private async Task ShowStepAsync(int number)
    {
        if (Work?.Result.OutcomeOf(number) is not { Span: { } span })
        {
            return;
        }

        _picks[NotesSource] = span;
        await ShowSourceAsync(NotesSource);
        await Player.SeekAsync(span.Onset);
    }

    /// <summary>«Послушать» in the apply dialog: the notes as the journal builds them, from the first change.</summary>
    private async Task HearNotesAsync()
    {
        if (Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
            return;
        }

        await ShowSourceAsync(NotesSource);
        TickSpan? first = Work?.Result.Steps.Where(step => step.Applied && step.Span is not null).Select(step => step.Span!.Value).OrderBy(span => span.Onset).FirstOrDefault();
        await Player.SetLoopAsync(null);
        _assemblyLoop = false;
        await Player.SeekAsync(first?.Onset ?? Tick.Zero);
        await Player.PlayAsync();
    }

    private async Task ToggleAssemblyPlayAsync()
    {
        if (Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
        }
        else
        {
            await Player.PlayAsync();
        }
    }

    /// <summary>«Добавить другую версию…» in More: a MIDI or MusicXML file read on the device, then the quiet card.</summary>
    private async Task AddVersionFromMoreAsync(Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs args)
    {
        _popover = Popover.None;
        await AddPerformanceAsync(args);
        if (_performanceStatus is { } problem && _offer is null)
        {
            _mergeToast = (problem, null);
        }
    }

    /// <summary>«На полтона ниже / выше» on the notes: the selected notes transposed, as a step of the journal.</summary>
    private async Task TransposePickAsync(int semitones)
    {
        if (Work is not { } work || _assemblyShown != NotesSource || AssemblyPick is not { } pick || PickedNotes.Count == 0)
        {
            return;
        }

        await ChangeWorkAsync(work.Transpose(AssemblyPlace.Over(work.Result.Music.Notes, pick), semitones, DateTimeOffset.Now));
        _mergeToast = (Text["assembly.transposed"], UndoAssemblyAsync);
    }

    /// <summary>What the last step of the assembly did, with the step that undoes it: a toast over the score.</summary>
    private (string Text, Func<Task>? Undo)? _mergeToast;

    /// <summary>The version removed last, where it was and the Check flags performances had, to put back while the piece is open.</summary>
    private (SavedPerformance Performance, int Index, IReadOnlyList<CheckFlag> Flags)? _removedVersion;

    private async Task UndoToastAsync()
    {
        if (_mergeToast is { Undo: { } undo })
        {
            _mergeToast = null;
            await undo();
        }
    }

    private void CloseToast() => _mergeToast = null;

    /// <summary>«Убрать версию»: the version leaves the piece; the toast puts it back. Carries from it are kept and named as lost.</summary>
    private async Task RemoveVersionAsync(string id)
    {
        if (_project is not { } project || project.FindPerformance(id) is not { } version)
        {
            return;
        }

        _removedVersion = (version, project.Performances.ToList().FindIndex(kept => kept.Id == version.Id),
            [.. (project.Check ?? []).Where(flag => flag.Source == CheckSource.Performance)]);
        if (_assembling && _assemblyShown == id)
        {
            await ShowSourceAsync(NotesSource);
        }

        _project = project.RemovePerformance(version.Id);
        _picks.Remove(id);
        _performanceId = null;
        _scoreChecks = null;
        _mergeToast = (Text.Format("merge.removed", version.Name), PutBackVersionAsync);
        await ApplyEditsAsync(_edits);
    }

    private async Task PutBackVersionAsync()
    {
        if (_project is not { } project || _removedVersion is not { } removed || project.FindPerformance(removed.Performance.Id) is not null)
        {
            return;
        }

        _project = project.PutBackPerformance(removed.Performance, removed.Index, removed.Flags);
        _removedVersion = null;
        _scoreChecks = null;
        _performanceId = removed.Performance.Id;
        _mergeToast = (Text.Format("merge.putBack", removed.Performance.Name), null);
        await ApplyEditsAsync(_edits);
    }

    /// <summary>The version whose «Подробнее» is open in Sources.</summary>
    private string? _sourceDetails;

    /// <summary>«Подробнее»: how many of the version's bars match, partly match, differ and were not found.</summary>
    private string MatchDetails(string versionId)
    {
        if (Work is not { } work || work.Source(versionId) is null)
        {
            return Text["assembly.unreadable"];
        }

        IReadOnlyList<MatchBar> bars = work.Match(versionId).Bars;
        int Count(MatchKind kind) => bars.Count(bar => bar.Kind == kind);
        return Text.Format("assembly.detailsLine", Count(MatchKind.Same), Count(MatchKind.Partly), Count(MatchKind.Differs), Count(MatchKind.NotFound), bars.Count);
    }

    private async Task RenameVersionAsync(string id, Microsoft.AspNetCore.Components.ChangeEventArgs args)
    {
        if (_project is not { } project || project.FindPerformance(id) is not { } version || args.Value is not string name || string.IsNullOrWhiteSpace(name) || name.Trim() == version.Name)
        {
            return;
        }

        _project = project.ChangePerformance(id, kept => kept with { Name = name.Trim() });
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
    }

    /// <summary>«Происхождение» on the notes' card: the comparison opens on the notes with the lines under the carries.</summary>
    private async Task OpenOriginsAsync()
    {
        await OpenAssemblyAsync(NotesSource);
        _origins = true;
    }

    /// <summary>«Начать сборку заново с них»: the journal empties, as one change ⌘Z brings back, and the comparison opens on the notes.</summary>
    private async Task RestartFromSheetAsync()
    {
        await OpenAssemblyAsync(NotesSource);
        await RestartAssemblyAsync();
    }
}