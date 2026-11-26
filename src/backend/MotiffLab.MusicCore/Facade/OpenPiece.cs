using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// One open piece (0020): its project, the imported piece, the notes with the player's corrections and
/// the phrases; the one primary selection (0013) with what was understood about it and the variation chain
/// a Try chip starts are its <see cref="SelectionSession"/>, the corrections' draft and layer its
/// <see cref="CorrectionsSession"/>, the drafts kept for drafts.undo and the rules of its sources its
/// <see cref="SourcesSession"/>, the rules of the versions compared with it <see cref="PerformancesSession"/> (audit W1.7). The iPad app's orchestration of music-core, kept in one place.
/// </summary>
internal sealed partial class OpenPiece
{
    private OpenPiece(Project project, ImportedPiece piece, IReadOnlyList<Note> notes, Words words)
    {
        Project = project;
        Piece = piece;
        Notes = notes;
        Words = words;
        ReadStructure(project.PhraseLevels);
    }

    public Project Project { get; private set; }

    public ImportedPiece Piece { get; private set; }

    /// <summary>The imported notes with the project's corrections and the bars taken from performances applied.</summary>
    public IReadOnlyList<Note> Notes { get; private set; }

    public Words Words { get; private set; }

    /// <summary>
    /// The structure of the piece (#347): the phrase levels, the sections of the form and the landmarks beside them, read
    /// once; the brackets (<c>phrases</c>), the Contents (<c>dna</c>'s form) and Structure bookmarks all read it.
    /// </summary>
    public PieceStructure Structure { get; private set; }

    /// <summary>The piece in three levels (0018, section 3) with the player's edits at each.</summary>
    public PhraseLevels Levels => Structure.Levels;

    /// <summary>
    /// The level the phrases answer shows: the phrases (the phrases spec: the iPad shows phrases and the sections of the
    /// form, no level).
    /// </summary>
    public PhraseLevel Level { get; } = PhraseLevel.Phrase;

    public PhraseMap Phrases => Levels.Phrases;

    /// <summary>The selection and what is understood about it, the variation chain and the working motif (<see cref="SelectionSession"/>).</summary>
    internal SelectionSession Selection { get; } = new();

    /// <summary>The corrections' draft, the draft accepted last and the layer they are laid with (<see cref="CorrectionsSession"/>).</summary>
    internal CorrectionsSession Corrections { get; } = new();

    /// <summary>
    /// For Notes or a draft opened only to be shown (sources.notes.open, drafts.open): the piece they were opened from and
    /// what is shown; null for a piece open to work on.
    /// </summary>
    internal ShownView? ShownFrom { get; private set; }

    /// <summary>
    /// True for Notes or a draft opened only to be shown: the facade answers the calls that show them and refuses every
    /// other (<see cref="MusicCoreFacade"/>).
    /// </summary>
    internal bool ReadOnly => ShownFrom is not null;

    /// <summary>The piece as the sessions read it now.</summary>
    internal SessionPiece State => new(Project, Piece, Notes, Structure, Words);

    /// <summary>This piece, open only to be shown from now on, as <paramref name="shown"/> says.</summary>
    internal OpenPiece AsReadOnly(ShownView shown)
    {
        ShownFrom = shown;
        return this;
    }

    /// <summary>A new project for a file the player just imported.</summary>
    public static OpenPiece Import(string id, string fileName, byte[] data, Words words, DateTimeOffset now)
    {
        PieceImportResult result = PieceImporter.Import(fileName, data);
        if (result.Piece is not { } piece)
        {
            ImportRejection rejection = result.Rejection!;
            throw new FacadeException("import", $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim()) { Data = { ["reason"] = JsonWrite.Name(rejection.Reason), ["tip"] = rejection.TipIn(words) } };
        }

        return new OpenPiece(Project.Create(id, fileName, data, now), piece, piece.Notes, words);
    }

    /// <summary>
    /// A new project from a recording heard in parts (0014, 0016), as the web app makes one: the parts' notes joined
    /// ("milliseconds", "parts" and "levels" as for performance.add), marked detected, written in the bars worked out from them when
    /// some fit ("tempo": "live" finds the beat afresh in each section, <see cref="LiveTempoRequest"/>), with the transcription's
    /// engine ("engine"), its seams and the Check flags it raises.
    /// </summary>
    public static OpenPiece Transcribe(string id, string fileName, Request heard, Words words, DateTimeOffset now)
    {
        string name = Path.ChangeExtension(string.IsNullOrWhiteSpace(fileName) ? "recording" : fileName, ".mid");
        (byte[] midi, TranscriptionOrigin origin, IReadOnlyList<CheckFlag> check) = Heard(name, heard, words);
        Project project = Project.Create(id, name, midi, now) with { Transcription = origin, Check = check };
        return Open(ProjectFile.Write(project), words);
    }

    /// <summary>
    /// The notes heard in a recording sent in parts ("milliseconds", "parts", "levels", "engine", "sentFromMs", "tempo"),
    /// joined, with the bars worked out from them, the transcription's origin and the Check flags it raises.
    /// </summary>
    internal static (byte[] Midi, TranscriptionOrigin Origin, IReadOnlyList<CheckFlag> Check) Heard(string name, Request heard, Words words)
    {
        byte[] midi = JoinedParts(heard);
        PieceImportResult result = PieceImporter.Import(name, midi);
        if (result.Piece is not { } imported || imported.Midi is null)
        {
            ImportRejection? rejection = result.Rejection;
            throw new FacadeException("import", rejection is null ? "The notes heard could not be read." : $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim());
        }

        long milliseconds = heard.Long("milliseconds")!.Value;
        IReadOnlyList<RecordingPart> plan = PlanOf(heard, milliseconds);
        (BarGrid? bars, ImportedPiece piece) = RecordingBars.Heard(imported, LiveTempoRequest.Of(heard));
        long? sentFrom = heard.Long("sentFromMs");
        if (sentFrom is < 0)
        {
            throw FacadeException.BadRequest("\"sentFromMs\" is where the stretch sent starts in the recording file, 0 or later.");
        }

        var origin = new TranscriptionOrigin(heard.Text("engine") ?? "unknown engine", bars)
        {
            Seams = plan.Count > 1 ? RecordingParts.Seams(plan) : [],
            SentFromMilliseconds = sentFrom,
        };
        return (midi, origin, CheckFlag.FromTranscription(origin, midi, piece));
    }

    /// <summary>
    /// A saved project, with its first motif back as the selection and that motif's variation; the corrections laid over
    /// the notes as <paramref name="layer"/> says (version 0.5), strictly by default.
    /// </summary>
    /// <param name="asTheFileSays">The corrections are left out when the file says the player turned them off ("editsOff"), whatever <paramref name="layer"/> says.</param>
    public static OpenPiece Open(byte[] projectFile, Words words, EditsLayer layer = EditsLayer.Strict, bool asTheFileSays = false)
    {
        Project project;
        OpenedProject opened;
        try
        {
            project = ProjectFile.Read(projectFile);
            opened = OpenedProject.Open(project, asTheFileSays && project.EditsOff ? EditsLayer.Off : layer);
        }
        catch (ProjectFileException exception)
        {
            throw new FacadeException("import", exception.Message) { Data = { ["reason"] = "damaged" } };
        }

        var result = new OpenPiece(opened.Project, opened.Piece, opened.Notes, words);
        if (opened.Motifs.Count > 0)
        {
            Motif motif = opened.Motifs[0];
            result.Selection.Start(motif, opened.Variations.FirstOrDefault(variation => variation.Saved.MotifId == motif.Id).Chain);
        }

        result.StartLayer(opened);
        return result;
    }

    /// <summary>What the assistant's tools read of the piece (<see cref="AssistantRequest.Calls"/>, corrections.acceptDraft).</summary>
    internal AssistantPiece Assistant => new(Piece, Notes, Project.Check ?? [], Words, Structure);

    public void Summary(Utf8JsonWriter json)
    {
        Answer.Project(json, "project", Project);
        json.WriteStartObject("summary");
        Answer.Piece(json, Piece, Notes, withNotes: false, Words);
        json.WriteEndObject();
        json.WriteString("language", Words.IsRussian ? "ru" : "en");
        if (Selection.Current is { } selection)
        {
            SelectionAnswer(json, selection);
        }
        else
        {
            json.WriteNull("selection");
        }

        json.WriteBoolean("hasVariation", Selection.Chain is not null);
        if (Corrections.Laid(State) is { } laid)
        {
            json.WriteStartObject("edits");
            json.WriteNumber("count", Project.Edits.Edits.Count);
            Answer.Layer(json, laid, Words);
            json.WriteEndObject();
        }
    }

    /// <summary>The printed notes with the player's corrections, as the corrections' layer lays them: what every performance is compared with.</summary>
    private IReadOnlyList<Note> Printed => Corrections.Corrected(State);

    /// <summary>The notes read again after the corrections or the bars taken changed (<see cref="PerformancesSession.NotesWithTakes"/>), and what was worked out from them.</summary>
    private void RefreshNotes()
    {
        Notes = PerformancesSession.NotesWithTakes(Printed, Project);
        ReadStructure();
        Selection.ForgetUnderstanding();
    }

    /// <summary>
    /// The notes read again (<see cref="RefreshNotes"/>) and, when they changed, the selection kept over them and the
    /// variation made from the old ones gone (<see cref="SelectionSession.FollowNotes"/>, E-score-1): after corrections
    /// dropped, bars taken or restored, or a version with bars taken from it taken away. Notes that stay the same (bars
    /// restored that were never taken) keep the variation in progress.
    /// </summary>
    private void RefreshNotesFollowingSelection()
    {
        IReadOnlyList<Note> before = Notes;
        RefreshNotes();
        if (!before.SequenceEqual(Notes))
        {
            Selection.FollowNotes(State);
        }
    }

    /// <summary>The structure read again from the notes as they are now, in the language set, with the player's <paramref name="edits"/> (by default those kept).</summary>
    [MemberNotNull(nameof(Structure))]
    private void ReadStructure(LevelEdits? edits = null) =>
        Structure = PieceStructure.Of(Notes, Piece.Meter, PhraseLevels.Suggest(Notes, Piece.Meter, Piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, Piece.End, KeySignature.InForce(Piece.Keys, Tick.Zero), Words, edits ?? Levels.Edits), Words);

    private string Language(Request request)
    {
        Words = Words.For(request.Required("language"));
        ReadStructure();
        Selection.ForgetUnderstanding();
        return Answer.Write(json => json.WriteString("language", Words.IsRussian ? "ru" : "en"));
    }

    /// <summary>
    /// Notation as MEI (0008): the piece, or the variation in place; and an excerpt of bars engraved on
    /// its own, with ossias above the original bar for bar (0018, section 4; preparation item 10). An ossia may be
    /// another version by its id, as the comparison layer over the whole piece or above an excerpt (0022, sections 4 and 5).
    /// </summary>
    private string Notation(Request request)
    {
        string of = request.Text("of") ?? "piece";
        string title = request.Text("title") ?? Path.GetFileNameWithoutExtension(Piece.FileName);
        bool readable = request.Bool("readable") ?? true;
        // «Для чтения» (version 0.5): the score for reading with the marks of the playing that matter, "reading": "marks".
        bool reading = request.Text("reading") switch
        {
            null => false,
            "marks" => true,
            _ => throw FacadeException.BadRequest("\"reading\" is \"marks\": the score for reading with the marks of the playing that matter."),
        };
        bool heard = Transcribed.IsHeard(Piece);
        BarRange? bars = ExcerptOf(request);
        // Another version above (0022, section 4) is engraved as the comparison layer has it, likely artefacts only when asked.
        bool artefacts = request.Bool("artefacts") ?? false;
        SavedPerformance?[] versions = [.. OssiaSources(request).Select(Project.FindPerformance)];
        PerformanceLayerNotes?[] layers = [.. versions.Select(version => version is null ? null : PerformancesSession.Layer(State, Printed, version, artefacts))];
        NotationOssia[] ossias = [.. OssiaSources(request).Select((source, index) => layers[index] is { } layer
            ? new NotationOssia(versions[index]!.Name, layer.Notes, AsHeard: true)
            : Ossia(source, heard, readable))];
        bool generated = heard || Project.ChangesNotes || bars is not null || ossias.Length > 0;
        GeneratedNotation notation;
        if (of == "piece")
        {
            notation = generated
                ? Generate(Notes, Piece.End, title, heard, readable, bars, ossias, reading)
                : Piece.MusicXml is { } xml ? NotationGenerator.FromMusicXml(xml, title, PlaybackScore.FromPiece(Piece).Tempos) : NotationGenerator.FromMidi(Piece.Midi!, title, tempos: PlaybackScore.FromPiece(Piece).Tempos);
        }
        else if (of == "variation")
        {
            Note[] notes = InPlace(Selection.Shown());
            Tick end = notes.Length == 0 ? Piece.End : notes.Max(note => note.Span.End);
            notation = Generate(notes, end > Piece.End ? end : Piece.End, title, heard, readable, bars, ossias, reading);
        }
        else
        {
            throw FacadeException.BadRequest("\"of\" is \"piece\" or \"variation\".");
        }

        return Answer.Write(json => Answer.Notation(json, notation, layers));
    }

    /// <summary>The bars a request names in "bars" {first, last}, or "bars": "selection" for the selection's; null for the whole piece.</summary>
    private BarRange? ExcerptOf(Request request)
    {
        if (!request.Has("bars"))
        {
            return null;
        }

        if (request.Element("bars") is { ValueKind: JsonValueKind.String } named)
        {
            if (named.GetString() != "selection")
            {
                throw FacadeException.BadRequest("Send \"bars\" {first, last} or \"selection\".");
            }

            return BarRange.Of(Piece.Meter, Selection.Required().Span);
        }

        Request range = request.Object("bars");
        int first = range.Int("first") ?? throw FacadeException.BadRequest("Send \"bars\" {first, last} or \"selection\".");
        int last = range.Int("last") ?? first;
        int lowest = Piece.Meter.FirstBarNumber;
        int highest = Piece.Meter.BarAt(new Tick(Math.Max(0, Piece.End.Value - 1))).Number;
        if (first < lowest || last < first || last > highest)
        {
            throw FacadeException.BadRequest($"\"bars\" runs from {lowest} to {highest}, first before last.");
        }

        return new BarRange(first, last);
    }

    /// <summary>What a request asks to engrave above the original: "ossia" as one source or a list of up to two.</summary>
    private static IReadOnlyList<string> OssiaSources(Request request)
    {
        if (!request.Has("ossia"))
        {
            return [];
        }

        IReadOnlyList<string> sources = request.Element("ossia") is { ValueKind: JsonValueKind.String } one ? [one.GetString()!] : request.Texts("ossia");
        if (sources.Count is 0 or > MaxOssias)
        {
            throw FacadeException.BadRequest($"\"ossia\" names one or {MaxOssias} sources: \"variation\", \"asPlayed\", an idea's id or another version's id.");
        }

        return sources;
    }

    /// <summary>The most ossias above one excerpt: two variations side by side for comparison (0018, section 4).</summary>
    public const int MaxOssias = 2;

    /// <summary>
    /// An ossia's notes and label: the variation heard now (only the passage it changed), the piece written as played
    /// for a transcription, or a kept idea's variation.
    /// </summary>
    private NotationOssia Ossia(string source, bool heard, bool readable)
    {
        if (source == "variation")
        {
            return new NotationOssia(Words.T("Variation", "Вариация"), Selection.Shown().Notes, AsHeard: heard && readable);
        }

        if (source == "asPlayed")
        {
            if (!heard)
            {
                throw FacadeException.Unavailable(Words.T("Only notes heard in a recording can be written as played.", "Как сыграно можно записать только ноты, услышанные в записи."));
            }

            PlaybackScore played = PlaybackScore.FromPiece(Piece);
            return new NotationOssia(Words.T("As played", "Как сыграно"), Notes, PlayedTempos: played.Tempos, Pedal: played.Pedal.Select(held => held.Span));
        }

        Observation idea = Project.Observations.FirstOrDefault(observation => observation.Id == source)
            ?? throw FacadeException.BadRequest($"\"ossia\" is \"variation\", \"asPlayed\", an idea's id or another version's id; there is no idea or version \"{source}\".");
        VariationChain? chain;
        try
        {
            chain = idea.Restore(Notes, Piece.Meter);
        }
        catch (ProjectFileException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        Variation variation = chain?.Result ?? throw FacadeException.Unavailable(Words.T("This idea keeps a note, not a variation.", "В этой идее только заметка, вариации нет."));
        return new NotationOssia(idea.Name, variation.Notes, AsHeard: heard && readable);
    }

    /// <param name="reading">For notes heard, written for reading: with the marks of the playing that matter («Для чтения», <see cref="PlayedMarks.Significant"/>).</param>
    private GeneratedNotation Generate(IEnumerable<Note> notes, Tick end, string title, bool heard, bool readable, BarRange? bars = null, IReadOnlyList<NotationOssia>? ossias = null, bool reading = false)
    {
        IEnumerable<KeyChange> keys = NotationKeys(heard);
        // The tempo is marked as music-core plays it (version 0.5.4): notated or the player's "♩ = 92", inferred "♩ ≈ 92".
        PlaybackScore played = PlaybackScore.FromPiece(Piece);
        if (!heard)
        {
            // Notes with a source record are written as the score prints them («Как напечатано», step 1).
            PrintedNotation? printed = Piece.MusicXml is { } source ? PrintedNotation.Of(source) : null;
            return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, bars: bars, ossias: ossias, tempos: played.Tempos, printed: printed);
        }

        if (readable && !reading)
        {
            return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, asHeard: true, bars: bars, ossias: ossias, tempos: played.Tempos);
        }

        if (readable)
        {
            return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, asHeard: true, playedTempos: played.Tempos, bars: bars, ossias: ossias, marks: PlayedMarks.Significant, tempos: played.Tempos);
        }

        return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, playedTempos: played.Tempos, pedal: played.Pedal.Select(held => held.Span), bars: bars, ossias: ossias, tempos: played.Tempos);
    }

    /// <summary>The key signatures the score is written in: the piece's, or for notes heard without one the key worked out from them.</summary>
    private IEnumerable<KeyChange> NotationKeys(bool heard) =>
        heard && Piece.Keys.Count == 0 && MusicalKey.OfHeard(Notes) is { } heardKey
            ? [new KeyChange(Tick.Zero, heardKey.Fifths)]
            : Piece.Keys.Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)));

    /// <summary>
    /// The piano roll's data (0018 section 4, within contract 4): the notes and bars packed as the roll reads them, and the
    /// names it writes, spelled for the keys the score is written in. "naming" is "letters", "syllables" or, by default,
    /// the piece's language's.
    /// </summary>
    private string Roll(Request request)
    {
        PlaybackScore score = PlaybackScore.FromPiece(Piece);
        if (Project.ChangesNotes)
        {
            score = score.WithNotes(Notes);
        }

        NoteNaming naming = NoteNames.For(request.Text("naming"), Words);
        RollPayload payload = PianoRollData.Create(Notes, score);
        RollNames names = PianoRollData.Names(Notes, [.. NotationKeys(Transcribed.IsHeard(Piece))], naming, Words);
        return Answer.Write(json => Answer.Roll(json, payload, names));
    }

    /// <summary>
    /// What plays: the piece, the variation heard, the variation in place or another version, with "hands" ("right" or
    /// "left", #328 within contract 12) only that hand's notes, as the web app plays a hand alone. The answer says which
    /// hands the piece's notes carry and whether any was inferred ("hands").
    /// </summary>
    private string Playback(Request request)
    {
        Hand? hand = request.Text("hands") switch
        {
            null or "both" => null,
            "right" => Hand.Right,
            "left" => Hand.Left,
            _ => throw FacadeException.BadRequest("\"hands\" is \"both\", \"right\" or \"left\"."),
        };
        IEnumerable<Note>? notes = (request.Text("of") ?? "piece") switch
        {
            "piece" => Project.ChangesNotes || hand is not null ? Notes : null,
            "heard" => Selection.Variation().Heard,
            "inPlace" => InPlace(Selection.Shown()),
            "version" => PerformancesSession.Layer(State, Printed, Performance(request), artefacts: false).Notes,
            _ => throw FacadeException.BadRequest("\"of\" is \"piece\", \"heard\", \"inPlace\" or \"version\"."),
        };
        if (hand is { } alone)
        {
            notes = notes!.Where(SelectionFilter.ForHand(alone).Matches);
        }

        PlaybackScore score = PlaybackScore.FromPiece(Piece);
        if (notes is not null)
        {
            score = score.WithNotes(notes);
        }

        PlaybackHands hands = PlaybackHands.Of(Notes);
        return Answer.Write(json =>
        {
            Answer.Playback(json, score);
            Answer.Hands(json, hands);
        });
    }

    /// <summary>The piece's notes with the variation's in place of the ones it changed or removed.</summary>
    private Note[] InPlace(Variation variation)
    {
        var changed = variation.Changes.Select(change => change.Before.Id).Concat(variation.Removed.Select(note => note.Id)).ToHashSet(StringComparer.Ordinal);
        return [.. Notes.Where(note => !changed.Contains(note.Id)), .. variation.Notes];
    }

    /// <summary>
    /// The project file with the selection as the working motif and the chain as its variation, as
    /// the web app autosaves (S1-P1). The answer is the project file itself.
    /// </summary>
    private string Save(Request request)
    {
        DateTimeOffset? now = request.Text("now") is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.")
            : null;
        (IReadOnlyList<SavedMotif> motifs, IReadOnlyList<SavedVariation> variations) = Selection.Saved(State, layerOff: Corrections.Layer == EditsLayer.Off);
        Project = Project.WithPhraseLevels(Levels.Edits) with
        {
            Motifs = motifs,
            Variations = variations,
            LastOpened = now ?? Project.LastOpened,
        };
        // Corrections made in the note editor are kept only once the player saves them (testing item 15).
        return System.Text.Encoding.UTF8.GetString(ProjectFile.Write(Project with { Edits = Corrections.Kept(State) }));
    }
}