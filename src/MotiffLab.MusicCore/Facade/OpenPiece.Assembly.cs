using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// Assembling the player's own version from the piece's sources (decision record 0024; contract 9), as the web app's
/// comparison does it, for the iPad app: the sources and their chips, each source engraved and heard in its own bars,
/// a selection's card in words, the carry sheet with its ghost, the carry, a transposition, undo and redo, «Вернуть что
/// было», «Начать заново», «Принять совпавшее», the journal, and «Сделать нотами», the one rule that changes the piece.
/// The selection and the source shown stay with the app; the journal is kept in the project as it changes.
/// </summary>
internal sealed partial class OpenPiece
{
    /// <summary>The player's notes as the journal builds them («Ноты»).</summary>
    private const string NotesSource = "notes";

    /// <summary>The notes before the assembly («Как было», «Ноты до сборки» once applied).</summary>
    private const string BeforeSource = "before";

    private AssemblyWork? _work;

    /// <summary>What the work was built over: it is built again, with its history, when the versions, the notes before or the piece's notes change.</summary>
    private (IReadOnlyList<SavedPerformance> Versions, SourceMusic? Before, IReadOnlyList<Note>? Notes)? _workOver;

    private readonly Dictionary<string, (byte[] Midi, SourceMusic? Music)> _versionMusic = new(StringComparer.Ordinal);

    /// <summary>The carry sheet worked out last, by the draft sent, the journal and the language.</summary>
    private (string Draft, string Stamp, bool Russian, CarrySheet Sheet, CarryDraft Carry)? _sheet;

    /// <summary>The project file as it was before «Сделать нотами», for «Отменить» while the piece is open.</summary>
    private byte[]? _beforeApply;

    /// <summary>The piece that takes this one's place after «Сделать нотами» or its undo; the facade opens it under the same number.</summary>
    public OpenPiece? Replacement { get; private set; }

    /// <summary>A carry as the app sends it: where from, where to and how.</summary>
    private sealed record CarryDraft(string SourceId, TickSpan From, AssemblyPlace? Replace, Tick Playhead, bool Insert, int Transpose, bool Keep);

    private string CallOnAssembly(string name, Request request) => name switch
    {
        "assembly" => AssemblyAnswer(),
        "assembly.source" => SourceAnswer(request),
        "assembly.notation" => AssemblyNotation(request),
        "assembly.playback" => AssemblyPlayback(request),
        "assembly.pick" => PickAnswer(request),
        "assembly.sheet" => SheetAnswer(request),
        "assembly.carry" => MakeCarry(request),
        "assembly.transpose" => TransposePick(request),
        "assembly.undo" => ChangeWork(Work is { CanUndo: true } work ? work.Undo() : throw FacadeException.Unavailable("There is no step to undo.")),
        "assembly.redo" => ChangeWork(Work is { CanRedo: true } work ? work.Redo() : throw FacadeException.Unavailable("There is no step to redo.")),
        "assembly.restart" => ChangeWork(Work is { Steps.Count: > 0 } work ? work.Restart() : throw FacadeException.Unavailable("The journal holds no step.")),
        "assembly.accept" => AcceptMatched(request),
        "assembly.journal" => JournalAnswer(request),
        "assembly.revert" => Revert(request),
        "assembly.apply" => ApplyAssembly(request),
        "assembly.unapply" => UnapplyAssembly(),
        "assembly.recording" => RecordingAnswer(request),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

    /// <summary>The work of the assembly over the piece as it is now; built again, keeping its history, when what it is built over changes.</summary>
    private AssemblyWork Work
    {
        get
        {
            PieceAssembly kept = Project.Assembly;
            IReadOnlyList<Note>? notes = kept.Before is null ? Notes : null;
            if (_work is not null && _workOver is { } over && ReferenceEquals(over.Versions, Project.Performances)
                && ReferenceEquals(over.Before, kept.Before) && ReferenceEquals(over.Notes, notes))
            {
                return _work;
            }

            SourceMusic baseMusic = kept.Before ?? BaseMusic();
            Dictionary<string, SourceMusic> sources = new(StringComparer.Ordinal);
            Dictionary<string, string> names = new(StringComparer.Ordinal);
            foreach (SavedPerformance version in Project.Performances)
            {
                if (VersionMusic(version) is { } music)
                {
                    sources[version.Id] = music;
                    names[version.Id] = version.Name;
                }
            }

            _work = _work is not null ? _work.Over(baseMusic, sources, names) : new AssemblyWork(baseMusic, sources, names, kept.Steps, kept.Next);
            _workOver = (Project.Performances, kept.Before, notes);
            return _work;
        }
    }

    /// <summary>The piece's notes as music, in the key worked out from them when they were heard without one.</summary>
    private SourceMusic BaseMusic()
    {
        SourceMusic music = SourceMusic.OfPiece(Piece, Notes);
        return Transcribed.IsHeard(Piece) && music.Keys.Count == 0 && Transcribed.KeyOf(Notes) is { } key
            ? music with { Keys = [new KeySignature(Tick.Zero, key.Fifths, key.Mode)] }
            : music;
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

    /// <summary>A source's music: the notes as the journal builds them, the notes before, or a version.</summary>
    private SourceMusic Music(string id) => id switch
    {
        NotesSource => Work.Result.Music,
        BeforeSource when HasBefore => Work.Base,
        _ => Work.Source(id) ?? throw FacadeException.BadRequest($"\"source\" is \"notes\", \"before\" or a version whose notes read; there is no source \"{id}\"."),
    };

    private bool HasBefore => Project.Assembly.Before is not null || Project.Assembly.Steps.Count > 0;

    private string SourceName(string id) => id switch
    {
        NotesSource => Words.T("Notes", "Ноты"),
        BeforeSource => Project.Assembly.Before is not null ? Words.T("Notes before the assembly", "Ноты до сборки") : Words.T("As it was", "Как было"),
        _ => Project.FindPerformance(id)?.Name ?? id,
    };

    /// <summary>«of Cover» / «Кавера»: a source in the genitive, after the bars.</summary>
    private string SourceOf(string id) => id switch
    {
        NotesSource => Words.T("of the notes", "нот"),
        BeforeSource => Words.T("as it was", "как было"),
        _ => Words.T($"of {SourceName(id)}", AssemblyWords.Genitive(SourceName(id))),
    };

    private string SourceId(Request request) => request.Text("source") ?? NotesSource;

    /// <summary>
    /// The sources and what the journal holds: «Ноты», each version that reads (its tone by the order it was added, its
    /// match with the notes in words and in bars, the corrections «Принять совпавшее» would carry), and «Как было» once
    /// the journal holds a step or the assembly was applied.
    /// </summary>
    private string AssemblyAnswer(Action<Utf8JsonWriter>? more = null) => Answer.Write(json =>
    {
        AssemblyWork work = Work;
        json.WriteStartObject("assembly");
        json.WriteNumber("versions", Project.Performances.Count);
        json.WriteNumber("steps", work.Steps.Count);
        json.WriteNumber("carries", work.Steps.Count(step => step is CarryStep));
        json.WriteBoolean("canUndo", work.CanUndo);
        json.WriteBoolean("canRedo", work.CanRedo);
        json.WriteBoolean("changed", work.Changed);
        json.WriteBoolean("applied", Project.Assembly.Applied is not null);
        json.WriteBoolean("canUnapply", _beforeApply is not null);
        json.WriteStartArray("sources");
        WriteSource(json, NotesSource, "notes", work.Result.Music);
        int index = 0;
        foreach (SavedPerformance version in Project.Performances)
        {
            int tone = index++;
            json.WriteStartObject();
            json.WriteString("id", version.Id);
            json.WriteString("kind", "version");
            json.WriteString("name", version.Name);
            json.WriteNumber("tone", tone);
            json.WriteString("from", Answer.Name(version.Source));
            if (work.Source(version.Id) is not { } music)
            {
                json.WriteBoolean("reads", false);
                json.WriteString("summary", Words.T("The version's notes could not be read.", "Ноты версии не читаются."));
                json.WriteEndObject();
                continue;
            }

            AssemblyMatch match = work.Match(version.Id);
            json.WriteBoolean("reads", true);
            json.WriteNumber("bars", music.Bars.Count);
            json.WriteString("summary", match.Summary(music, work.Result.Music, Words));
            json.WriteNumber("corrections", match.CorrectionCount(music, work.Result.Music));
            json.WriteStartObject("match");
            json.WriteNumber("same", match.Bars.Count(bar => bar.Kind == MatchKind.Same));
            json.WriteNumber("partly", match.Bars.Count(bar => bar.Kind == MatchKind.Partly));
            json.WriteNumber("differs", match.Bars.Count(bar => bar.Kind == MatchKind.Differs));
            json.WriteNumber("notFound", match.Bars.Count(bar => bar.Kind == MatchKind.NotFound));
            json.WriteNumber("total", match.Bars.Count);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        if (HasBefore)
        {
            WriteSource(json, BeforeSource, "before", work.Base);
        }

        json.WriteEndArray();
        json.WriteEndObject();
        more?.Invoke(json);
    });

    private void WriteSource(Utf8JsonWriter json, string id, string kind, SourceMusic music)
    {
        json.WriteStartObject();
        json.WriteString("id", id);
        json.WriteString("kind", kind);
        json.WriteString("name", SourceName(id));
        json.WriteBoolean("reads", true);
        json.WriteNumber("bars", music.Bars.Count);
        json.WriteEndObject();
    }

    /// <summary>
    /// A source's bars and notes, for picking on its page: each bar with its number (0 for a pickup) and span, each note
    /// with its id, pitch and span, and whether it is a recording whose sound plays in place of its notes.
    /// </summary>
    private string SourceAnswer(Request request)
    {
        string id = SourceId(request);
        SourceMusic music = Music(id);
        return Answer.Write(json =>
        {
            json.WriteString("source", id);
            json.WriteNumber("end", music.End.Value);
            json.WriteBoolean("recording", Project.FindPerformance(id) is { Source: PerformanceSource.Recording });
            json.WriteStartArray("bars");
            for (int index = 0; index < music.Bars.Count; index++)
            {
                json.WriteStartObject();
                json.WriteNumber("number", music.NumberOf(index));
                json.WriteNumber("onset", music.Bars[index].Span.Onset.Value);
                json.WriteNumber("end", music.Bars[index].Span.End.Value);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("notes");
            foreach (Note note in music.Notes)
            {
                json.WriteStartObject();
                json.WriteString("id", note.Id);
                json.WriteNumber("pitch", note.Pitch.MidiNumber);
                json.WriteNumber("onset", note.Span.Onset.Value);
                json.WriteNumber("end", note.Span.End.Value);
                json.WriteNumber("origin", id == NotesSource ? Work.Result.OriginOf(note.Id) : 0);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// A source engraved in its own bars and keys, headed with its name (the notes with the piece's title, "title"); with
    /// "carry" the notes as that carry will leave them (the ghost), with "put" the ids of the notes it puts in and
    /// "ghost" the span they take; with "origins" true the lines under the notes the carries put in.
    /// </summary>
    private string AssemblyNotation(Request request)
    {
        string id = SourceId(request);
        CarrySheet? sheet = id == NotesSource && request.Has("carry") ? Sheet(request) : null;
        SourceMusic music = sheet?.Ghost.Music ?? Music(id);
        IEnumerable<KeyChange> keys = music.Keys.Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)));
        string title = id is NotesSource or BeforeSource ? request.Text("title") ?? Path.GetFileNameWithoutExtension(Piece.FileName) : SourceName(id);
        GeneratedNotation notation = NotationGenerator.Generate(music.Notes, music.Meter, keys, music.End, title);
        bool origins = id == NotesSource && sheet is null && (request.Bool("origins") ?? false);
        return Answer.Write(json =>
        {
            Answer.Notation(json, notation);
            WriteIds(json, "put", sheet?.Outcome.Put ?? []);
            if (sheet?.Outcome.Span is { } ghost)
            {
                Answer.Span(json, "ghost", ghost);
            }
            else
            {
                json.WriteNull("ghost");
            }

            json.WriteStartArray("origins");
            if (origins)
            {
                AssemblyWork work = Work;
                AssemblyNames names = work.NamesIn(Words);
                foreach (CarryStep carry in work.Steps.OfType<CarryStep>())
                {
                    if (work.Result.OutcomeOf(carry.Number) is { Applied: true, Span: { } span })
                    {
                        json.WriteStartObject();
                        json.WriteNumber("number", carry.Number);
                        json.WriteString("source", carry.SourceId);
                        Answer.Span(json, "span", span);
                        json.WriteString("label", Words.T($"Carry No. {carry.Number} from {names.Of(carry.SourceId)}", $"Перенос №{carry.Number} из {names.GenitiveOf(carry.SourceId)}"));
                        json.WriteEndObject();
                    }
                }
            }

            json.WriteEndArray();
        });
    }

    /// <summary>A source's notes to hear, at its own tempo; with "carry", the ghost.</summary>
    private string AssemblyPlayback(Request request)
    {
        string id = SourceId(request);
        SourceMusic music = (id == NotesSource && request.Has("carry") ? Sheet(request).Ghost.Music : null) ?? Music(id);
        PlaybackScore score = PlaybackScore.Create(music.Notes, music.Tempos, music.Meter, music.End);
        return Answer.Write(json => Answer.Playback(json, score));
    }

    /// <summary>
    /// What a selection ("span" in the source's own time) holds, for its card: «9 нот · такты 15–16 Кавера», the edges of
    /// a selection from note to note, a recording's stretch in time, where a version's selection is like the notes, and on
    /// the notes the carry that put them in with its lines. "naming" spells the note names, as for roll.
    /// </summary>
    private string PickAnswer(Request request)
    {
        string id = SourceId(request);
        SourceMusic music = Music(id);
        TickSpan pick = request.Span("span") ?? throw FacadeException.BadRequest("Send \"span\", the selection in the source's own time.");
        IReadOnlyList<Note> notes = music.NotesIn(pick.Onset, pick.End);
        AssemblyWork work = Work;
        string count = Words.Count(notes.Count, "note", "notes", "нота", "ноты", "нот");
        string bars = CarrySheet.BarsText(music, pick.Onset, pick.End, Words);
        string? times = Times(id, music, pick);
        string title = times is null ? $"{count} · {bars} {SourceOf(id)}" : $"{count} · {times} · {bars} {SourceOf(id)}";
        NoteNaming naming = NoteNames.For(request.Text("naming"), Words);
        return Answer.Write(json =>
        {
            json.WriteString("title", title);
            json.WriteNumber("notes", notes.Count);
            json.WriteString("edges", Edges(music, notes, naming));
            json.WriteString("times", times);
            json.WriteBoolean("canCarry", id is not NotesSource and not BeforeSource && notes.Count > 0);
            json.WriteBoolean("canTranspose", id == NotesSource && notes.Count > 0);
            if (id is not NotesSource and not BeforeSource)
            {
                json.WriteStartObject("similar");
                if (work.Match(id).Similar(pick) is { } similar)
                {
                    Answer.Span(json, "span", similar.Span);
                    json.WriteString("text", Words.T(
                        $"Like {CarrySheet.BarsText(work.Result.Music, similar.Span.Onset, similar.Span.End, Words)} of your notes · {AssemblyMatch.Word(similar.Kind, Words)}",
                        $"Похоже на {CarrySheet.BarsText(work.Result.Music, similar.Span.Onset, similar.Span.End, Words)} ваших нот · {AssemblyMatch.Word(similar.Kind, Words)}"));
                }
                else
                {
                    json.WriteNull("span");
                    json.WriteString("text", Words.T("Nothing like it in your notes", "В ваших нотах похожего не найдено"));
                }

                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("similar");
            }

            int number = id == NotesSource ? notes.Select(note => work.Result.OriginOf(note.Id)).FirstOrDefault(origin => origin > 0) : 0;
            if (number > 0 && work.Steps.FirstOrDefault(step => step.Number == number) is CarryStep carry)
            {
                json.WriteStartObject("carry");
                json.WriteNumber("number", carry.Number);
                json.WriteString("source", carry.SourceId);
                Answer.Span(json, "from", carry.From);
                if (work.Result.OutcomeOf(carry.Number) is { Applied: true, Span: { } span })
                {
                    Answer.Span(json, "span", span);
                }

                WriteLines(json, "lines", AssemblyJournal.Card(work, carry.Number, Words));
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("carry");
            }
        });
    }

    /// <summary>«0:41–0:45»: a recording's stretch of a selection; null for any other source.</summary>
    private string? Times(string id, SourceMusic music, TickSpan pick)
    {
        if (Project.FindPerformance(id) is not { Source: PerformanceSource.Recording } version || RecordingClock.Of(version, music) is not { } clock)
        {
            return null;
        }

        static string Clock(long milliseconds)
        {
            long seconds = Math.Max(0, milliseconds) / 1000;
            return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
        }

        return $"{Clock(clock.MillisecondsAt(pick.Onset))}–{Clock(clock.MillisecondsAt(pick.End))}";
    }

    /// <summary>«от ми² · такт 15, доля 1 — до соль¹ · такт 16, доля 3»; null without notes.</summary>
    private string? Edges(SourceMusic music, IReadOnlyList<Note> notes, NoteNaming naming)
    {
        if (notes.Count == 0)
        {
            return null;
        }

        string Edge(Note note)
        {
            int fifths = music.Keys.LastOrDefault(key => key.At <= note.Span.Onset)?.Fifths ?? 0;
            (int bar, int beat, _) = music.PositionOf(note.Span.Onset);
            string name = NoteNames.Name(note.Pitch.MidiNumber, Math.Clamp(fifths, -7, 7), naming);
            return Words.T($"{name} · bar {bar}, beat {beat}", $"{name} · такт {bar}, доля {beat}");
        }

        Note first = notes.MinBy(note => (note.Span.Onset.Value, -note.Pitch.MidiNumber))!;
        Note last = notes.MaxBy(note => (note.Span.Onset.Value, note.Pitch.MidiNumber))!;
        return ReferenceEquals(first, last) ? Edge(first) : Words.T($"from {Edge(first)} to {Edge(last)}", $"от {Edge(first)} до {Edge(last)}");
    }

    /// <summary>
    /// "carry" {source, span, target, insert, transpose, keep}: a fragment of a version (its "span" in the version's own
    /// time) to carry into the notes. It replaces the notes in "target" (the selection in the notes), or else the place
    /// the fragment is like, or goes in at the target's start (the playhead) with "insert" true or when nothing is there to
    /// replace. "keep" (from a recording, the default when replacing) keeps the notes and adds what the performer plays otherwise.
    /// </summary>
    private CarryDraft DraftOf(Request request)
    {
        Request carry = request.Object("carry");
        if (carry.IsEmpty)
        {
            throw FacadeException.BadRequest("Send \"carry\" {source, span, target, insert, transpose, keep}.");
        }

        string source = carry.Required("source");
        AssemblyWork work = Work;
        if (work.Source(source) is not { } music)
        {
            throw FacadeException.BadRequest($"\"source\" is a version whose notes read; there is no version \"{source}\".");
        }

        TickSpan from = carry.Span("span") ?? throw FacadeException.BadRequest("The carry needs \"span\", the fragment in the version's own time.");
        if (music.NotesIn(from.Onset, from.End).Count == 0)
        {
            throw FacadeException.BadRequest("The fragment holds no notes.");
        }

        SourceMusic notes = work.Result.Music;
        TickSpan? target = carry.Span("target") ?? (work.Match(source).Similar(from) is { } similar && similar.Span.Duration > 0 ? similar.Span : null);
        AssemblyPlace? replace = target is { } span && notes.NotesIn(span.Onset, span.End).Count > 0 ? AssemblyPlace.Over(notes.Notes, span) : null;
        bool insert = replace is null || (carry.Bool("insert") ?? false);
        bool heard = Project.FindPerformance(source) is { Source: PerformanceSource.Recording };
        int transpose = carry.Int("transpose") ?? 0;
        if (transpose is < -24 or > 24)
        {
            throw FacadeException.BadRequest("\"transpose\" runs from -24 to 24 semitones.");
        }

        return new CarryDraft(source, from, replace, target?.Onset ?? Tick.Zero, insert, transpose, !insert && (carry.Bool("keep") ?? heard));
    }

    private CarryStep StepOf(CarryDraft draft, DateTimeOffset at)
    {
        AssemblyWork work = Work;
        AssemblyPlace to = draft.Insert || draft.Replace is null ? AssemblyPlace.InsertAt(work.Result.Music.Notes, draft.Playhead) : draft.Replace;
        return work.Carry(draft.SourceId, draft.From, to, draft.Transpose, at, draft.Keep);
    }

    /// <summary>The carry sheet for the draft sent, worked out again only when the draft, the journal or the language change.</summary>
    private CarrySheet Sheet(Request request) => SheetAndDraft(request).Sheet;

    private (CarrySheet Sheet, CarryDraft Draft) SheetAndDraft(Request request)
    {
        string raw = request.Element("carry")?.GetRawText() ?? "";
        AssemblyWork work = Work;
        if (_sheet is { } cached && cached.Draft == raw && cached.Stamp == work.Stamp && cached.Russian == Words.IsRussian)
        {
            return (cached.Sheet, cached.Carry);
        }

        CarryDraft draft = DraftOf(request);
        CarryStep step = StepOf(draft, DateTimeOffset.UnixEpoch);
        bool heard = Project.FindPerformance(draft.SourceId) is { Source: PerformanceSource.Recording };
        CarrySheet sheet = work.Sheet(step, draft.Replace, draft.Playhead, Words, heard);
        _sheet = (raw, work.Stamp, Words.IsRussian, sheet, draft);
        return (sheet, draft);
    }

    /// <summary>«В ноты…»: what the carry will do, in the same lines every time, before it is made.</summary>
    private string SheetAnswer(Request request)
    {
        (CarrySheet sheet, CarryDraft draft) = SheetAndDraft(request);
        return Answer.Write(json =>
        {
            json.WriteStartObject("sheet");
            json.WriteString("title", sheet.Title);
            json.WriteString("route", sheet.Route);
            json.WriteString("replaceText", sheet.ReplaceText);
            json.WriteString("insertText", sheet.InsertText);
            json.WriteString("keyText", sheet.KeyText);
            json.WriteNumber("suggestedTranspose", sheet.SuggestedTranspose);
            json.WriteString("keepText", sheet.KeepText);
            json.WriteString("heardText", sheet.HeardText);
            json.WriteBoolean("canReplace", draft.Replace is not null);
            json.WriteBoolean("insert", draft.Insert);
            json.WriteBoolean("keep", draft.Keep);
            json.WriteNumber("transpose", draft.Transpose);
            json.WriteStartArray("lines");
            foreach (SheetLine line in sheet.Lines)
            {
                json.WriteStartObject();
                json.WriteString("name", line.Name);
                json.WriteString("mark", Answer.Name(line.Mark));
                json.WriteString("label", line.Label);
                json.WriteString("text", line.Text);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (sheet.Outcome.Span is { } span)
            {
                Answer.Span(json, "ghost", span);
            }
            else
            {
                json.WriteNull("ghost");
            }

            WriteIds(json, "put", sheet.Outcome.Put);
            json.WriteEndObject();
        });
    }

    /// <summary>«Перенести»: the carry joins the journal; the answer says what went in and where, for the toast and the selection.</summary>
    private string MakeCarry(Request request)
    {
        CarryDraft draft = DraftOf(request);
        CarryStep step = StepOf(draft, Now(request));
        KeepJournal(Work.With(step));
        AssemblyWork work = Work;
        string title = AssemblyJournal.NotesFrom(work, step, work.NamesIn(Words), Words);
        return AssemblyAnswer(json =>
        {
            json.WriteStartObject("carried");
            json.WriteNumber("number", step.Number);
            json.WriteString("title", title);
            if (work.Result.OutcomeOf(step.Number) is { Span: { } placed })
            {
                Answer.Span(json, "span", placed);
            }
            else
            {
                json.WriteNull("span");
            }

            json.WriteEndObject();
        });
    }

    /// <summary>«На полтона ниже / выше» on the notes: the notes of "span" moved by "semitones", as a step of the journal.</summary>
    private string TransposePick(Request request)
    {
        TickSpan span = request.Span("span") ?? throw FacadeException.BadRequest("Send \"span\", the selection in the notes.");
        int semitones = request.Int("semitones") is { } moved and >= -24 and <= 24 and not 0 ? moved : throw FacadeException.BadRequest("\"semitones\" runs from -24 to 24, not 0.");
        AssemblyWork work = Work;
        if (work.Result.Music.NotesIn(span.Onset, span.End).Count == 0)
        {
            throw FacadeException.Unavailable("The selection holds no notes.");
        }

        return ChangeWork(work.Transpose(AssemblyPlace.Over(work.Result.Music.Notes, span), semitones, Now(request)));
    }

    /// <summary>
    /// «Принять совпавшее»: every bar of version "id" that matches the notes but is not identical, carried in one change;
    /// "accepted" is how many bars were carried, 0 when none was.
    /// </summary>
    private string AcceptMatched(Request request)
    {
        string id = request.Required("id");
        AssemblyWork work = Work;
        if (work.Source(id) is null)
        {
            throw FacadeException.BadRequest($"There is no version \"{id}\" whose notes read.");
        }

        int accepted = 0;
        if (work.Accept(work.Match(id), Now(request)) is { } changed && changed.Steps.Count > work.Steps.Count)
        {
            accepted = changed.Steps.Count - work.Steps.Count;
            KeepJournal(changed);
        }

        return AssemblyAnswer(json => json.WriteNumber("accepted", accepted));
    }

    /// <summary>
    /// The journal's rows, newest last; with "revert" (a step's number) what «Вернуть что было» on it takes with it and
    /// leaves, before anything is done; and what «Сделать нотами» will change.
    /// </summary>
    private string JournalAnswer(Request request)
    {
        AssemblyWork work = Work;
        NoteNaming naming = NoteNames.For(request.Text("naming"), Words);
        return Answer.Write(json =>
        {
            json.WriteStartArray("rows");
            foreach (JournalRow row in AssemblyJournal.Rows(work, Words, naming))
            {
                json.WriteStartObject();
                json.WriteNumber("number", row.Number);
                json.WriteString("source", row.SourceId);
                json.WriteString("title", row.Title);
                json.WriteString("detail", row.Detail);
                json.WriteBoolean("applied", row.Applied);
                if (row.Span is { } span)
                {
                    Answer.Span(json, "span", span);
                }
                else
                {
                    json.WriteNull("span");
                }

                if (work.Steps.FirstOrDefault(step => step.Number == row.Number) is { } step)
                {
                    json.WriteString("at", step.At.ToString("O", CultureInfo.InvariantCulture));
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (request.Int("revert") is { } number && work.Steps.Any(step => step.Number == number))
            {
                RevertPlan plan = work.PlanRevert(number);
                json.WriteStartObject("revert");
                json.WriteNumber("number", plan.Number);
                WriteNumbers(json, "dependents", plan.Dependents);
                WriteNumbers(json, "staying", plan.Staying);
                WriteLines(json, "lines", AssemblyJournal.Revert(work, plan, Words));
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("revert");
            }

            json.WriteStartObject("apply");
            json.WriteNumber("places", work.Result.Steps.Count(step => step.Applied));
            json.WriteString("bars", AssemblyJournal.ChangedBars(work, Words));
            WriteLines(json, "lines", AssemblyJournal.Apply(work, Words));
            json.WriteEndObject();
        });
    }

    /// <summary>«Вернуть»: step "number" and every step that leans on it leave the journal, as one change undo brings back.</summary>
    private string Revert(Request request)
    {
        int number = request.Int("number") ?? throw FacadeException.BadRequest("Send \"number\", the step's number.");
        AssemblyWork work = Work;
        if (work.Steps.All(step => step.Number != number))
        {
            throw FacadeException.BadRequest($"The journal has no step {number}.");
        }

        RevertPlan plan = work.PlanRevert(number);
        KeepJournal(work.Revert(number));
        return AssemblyAnswer(json =>
        {
            json.WriteStartObject("reverted");
            json.WriteNumber("number", number);
            WriteNumbers(json, "dependents", plan.Dependents);
            json.WriteEndObject();
        });
    }

    /// <summary>
    /// «Сделать нотами», the one rule that changes the piece (0024, section 7): the notes the journal builds become the
    /// piece's notes and the notes before stay as «Ноты до сборки». The piece opens again as it now is, under the same
    /// number: the answer is the open piece's summary with "places", how many places changed; "assembly.unapply" takes it back.
    /// </summary>
    private string ApplyAssembly(Request request)
    {
        AssemblyWork work = Work;
        int places = work.Result.Steps.Count(step => step.Applied);
        Project changed = Project.ApplyAssembly(work.Result.Music, work.Base, Now(request));
        OpenPiece next = Open(ProjectFile.Write(changed), Words);
        next._beforeApply = ProjectFile.Write(Project);
        Replacement = next;
        return Answer.Write(json =>
        {
            next.Summary(json);
            json.WriteNumber("places", places);
        });
    }

    /// <summary>The piece back as it was before «Сделать нотами», while it is open.</summary>
    private string UnapplyAssembly()
    {
        byte[] before = _beforeApply ?? throw FacadeException.Unavailable("«Сделать нотами» was not pressed while the piece is open.");
        OpenPiece next = Open(before, Words);
        Replacement = next;
        return Answer.Write(next.Summary);
    }

    /// <summary>
    /// Where the notes of recording version "id" are heard in the recording (0024, scenario 2): "ticks" paired with
    /// "milliseconds", for an app that plays the recording's own sound in place of its notes; "clock" null otherwise.
    /// </summary>
    private string RecordingAnswer(Request request)
    {
        string id = request.Required("id");
        RecordingClock? clock = Project.FindPerformance(id) is { } version && Work.Source(id) is { } music ? RecordingClock.Of(version, music) : null;
        return Answer.Write(json =>
        {
            if (clock is null)
            {
                json.WriteNull("clock");
                return;
            }

            json.WriteStartObject("clock");
            json.WriteStartArray("ticks");
            foreach (long tick in clock.Ticks)
            {
                json.WriteNumberValue(tick);
            }

            json.WriteEndArray();
            json.WriteStartArray("milliseconds");
            foreach (long milliseconds in clock.Milliseconds)
            {
                json.WriteNumberValue(milliseconds);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        });
    }

    /// <summary>The journal changed: kept in the project, so the next save writes it.</summary>
    private void KeepJournal(AssemblyWork work)
    {
        _work = work;
        Project = Project.WithJournal(work.Steps, work.Next);
        _workOver = _workOver is { } over ? over with { Versions = Project.Performances } : null;
    }

    private string ChangeWork(AssemblyWork work)
    {
        KeepJournal(work);
        return AssemblyAnswer();
    }

    private static void WriteLines(Utf8JsonWriter json, string name, IReadOnlyList<JournalLine> lines)
    {
        json.WriteStartArray(name);
        foreach (JournalLine line in lines)
        {
            json.WriteStartObject();
            json.WriteString("mark", Answer.Name(line.Mark));
            json.WriteString("label", line.Label);
            json.WriteString("text", line.Text);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteIds(Utf8JsonWriter json, string name, IEnumerable<string> ids)
    {
        json.WriteStartArray(name);
        foreach (string id in ids)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }

    private static void WriteNumbers(Utf8JsonWriter json, string name, IEnumerable<int> numbers)
    {
        json.WriteStartArray(name);
        foreach (int number in numbers)
        {
            json.WriteNumberValue(number);
        }

        json.WriteEndArray();
    }
}