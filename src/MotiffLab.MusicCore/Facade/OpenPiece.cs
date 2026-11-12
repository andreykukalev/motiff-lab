using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// One open piece (0020): its project, the imported piece, the notes with the player's corrections,
/// the phrases, the one primary selection (0013) with what was understood about it, and the
/// variation chain a Try chip starts. The iPad app's orchestration of music-core, kept in one place.
/// </summary>
internal sealed partial class OpenPiece
{
    private static readonly KeepProperty[] s_defaultKeep = [KeepProperty.Pitches, KeepProperty.PitchOrder];

    private InvitationContext? _understood;

    /// <summary>The Check flags and notes <see cref="_understood"/> was read from.</summary>
    private (IReadOnlyList<CheckFlag>? Check, IReadOnlyList<Note> Notes)? _understoodFor;
    private TryChip? _chip;
    private MotifSelectionKind _selectionKind = MotifSelectionKind.Range;

    private OpenPiece(Project project, ImportedPiece piece, IReadOnlyList<Note> notes, Words words)
    {
        Project = project;
        Piece = piece;
        Notes = notes;
        Words = words;
        ReadStructure(project.PhraseLevels);
        Level = PhraseLevels.Parse(project.SettingOverrides.GetValueOrDefault(GranularitySetting)) ?? PhraseLevels.Parse(SettingsCatalogue.Get(GranularitySetting).Default) ?? PhraseLevel.Phrase;
        Keep = [.. s_defaultKeep];
    }

    /// <summary>The piece setting that remembers the level shown (0018, section 3).</summary>
    public const string GranularitySetting = "phrases.granularity";

    public Project Project { get; private set; }

    public ImportedPiece Piece { get; }

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

    /// <summary>The level the app shows, remembered per piece through <see cref="GranularitySetting"/>.</summary>
    public PhraseLevel Level { get; private set; }

    public PhraseMap Phrases => Levels.Phrases;

    public MotifSelection? Selection { get; private set; }

    public VariationChain? Chain { get; private set; }

    public HashSet<KeepProperty> Keep { get; }

    public string MotifId { get; private set; } = "m_0001";

    /// <summary>A new project for a file the player just imported.</summary>
    public static OpenPiece Import(string id, string fileName, byte[] data, Words words, DateTimeOffset now)
    {
        PieceImportResult result = PieceImporter.Import(fileName, data);
        if (result.Piece is not { } piece)
        {
            ImportRejection rejection = result.Rejection!;
            throw new FacadeException("import", $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim()) { Data = { ["reason"] = Answer.Name(rejection.Reason), ["tip"] = rejection.TipIn(words) } };
        }

        return new OpenPiece(Project.Create(id, fileName, data, now), piece, piece.Notes, words);
    }

    /// <summary>
    /// A new project from a recording heard in parts (0014, 0016), as the web app makes one: the parts' notes joined
    /// ("milliseconds", "parts" and "levels" as for performance.add), marked detected, written in the bars worked out from them when
    /// some fit, with the transcription's engine ("engine"), its seams and the Check flags it raises.
    /// </summary>
    public static OpenPiece Transcribe(string id, string fileName, Request heard, Words words, DateTimeOffset now)
    {
        byte[] midi = JoinedParts(heard);
        string name = Path.ChangeExtension(string.IsNullOrWhiteSpace(fileName) ? "recording" : fileName, ".mid");
        PieceImportResult result = PieceImporter.Import(name, midi);
        if (result.Piece is not { } imported || imported.Midi is null)
        {
            ImportRejection? rejection = result.Rejection;
            throw new FacadeException("import", rejection is null ? "The notes heard could not be read." : $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim());
        }

        long milliseconds = heard.Long("milliseconds")!.Value;
        IReadOnlyList<RecordingPart> plan = PlanOf(heard, milliseconds);
        BarGrid? bars = BarGrid.Find(imported.Midi);
        var origin = new TranscriptionOrigin(heard.Text("engine") ?? "unknown engine", bars)
        {
            Seams = plan.Count > 1 ? RecordingParts.Seams(plan) : [],
        };
        ImportedPiece piece = Transcribed.Mark(bars is { } grid ? grid.Apply(imported) : imported);
        Project project = Project.Create(id, name, midi, now) with { Transcription = origin };
        project = project with { Check = CheckFlag.FromTranscription(origin, midi, piece) };
        return Open(ProjectFile.Write(project), words);
    }

    /// <summary>
    /// A saved project, with its first motif back as the selection and that motif's variation; the corrections laid over
    /// the notes as <paramref name="layer"/> says (version 0.5), strictly by default.
    /// </summary>
    public static OpenPiece Open(byte[] projectFile, Words words, EditsLayer layer = EditsLayer.Strict)
    {
        Project project;
        OpenedProject opened;
        try
        {
            project = ProjectFile.Read(projectFile);
            opened = OpenedProject.Open(project, layer);
        }
        catch (ProjectFileException exception)
        {
            throw new FacadeException("import", exception.Message) { Data = { ["reason"] = "damaged" } };
        }

        var result = new OpenPiece(opened.Project, opened.Piece, opened.Notes, words);
        if (opened.Motifs.Count > 0)
        {
            Motif motif = opened.Motifs[0];
            result.MotifId = motif.Id;
            result.Selection = motif.Selection;
            result._selectionKind = motif.Kind;
            if (opened.Variations.FirstOrDefault(variation => variation.Saved.MotifId == motif.Id).Chain is { } chain)
            {
                result.Keep.Clear();
                result.Keep.UnionWith(chain.Context.Keep);
                result.Chain = chain;
            }
        }

        result.StartLayer(opened);
        return result;
    }

    public string Call(string name, Request request) => name switch
    {
        "piece" => Answer.Write(json => Answer.Piece(json, Piece, Notes, withNotes: true, Words)),
        "language" => Language(request),
        "phrases" => Answer.Write(json => Answer.Phrases(json, Levels, LevelOf(request) ?? Level, Words)),
        "phrases.level" => ChangeLevel(request),
        "phrases.edit" => EditPhrases(request),
        "select" => Select(request),
        "understand" => Answer.Write(json => Answer.Understanding(json, Understood(), SentenceFacts(), fact => TryChips.Offer(fact.Id, Understood()))),
        "circle" => Answer.Write(json => Answer.Circle(json, Circle(Understood()), Understood().Selection.Span.Onset, Piece.End)),
        "try" => TryChip(request),
        "variation" => ChainAnswer(Chain ?? throw FacadeException.NoVariation()),
        "variation.clear" => Clear(),
        "notation" => Notation(request),
        "playback" => Playback(request),
        "roll" => Roll(request),
        "project.save" => Save(request),
        "reference.key" => ReferenceRequest.Key(request, PieceDna.OpeningKey(Notes, Piece.Meter, Piece.Keys, Piece.End)),
        "dna" => Answer.Write(json => Answer.Dna(json, Doubt().On(PieceDna.Build(Notes, Piece.Meter, Piece.Keys, Piece.End, Selection, Selection is null ? null : Understood().Structure, Words, Structure)), Words, Piece.End, Piece.Meter)),
        _ => CallOnProject(name, request),
    };

    public void Summary(Utf8JsonWriter json)
    {
        Answer.Project(json, "project", Project);
        json.WriteStartObject("summary");
        Answer.Piece(json, Piece, Notes, withNotes: false, Words);
        json.WriteEndObject();
        json.WriteString("language", Words.IsRussian ? "ru" : "en");
        if (Selection is { } selection)
        {
            Answer.Selection(json, "selection", selection, KeySignature.InForce(Piece.Keys, selection.Span.Onset), Piece.Meter);
        }
        else
        {
            json.WriteNull("selection");
        }

        json.WriteBoolean("hasVariation", Chain is not null);
        if (_layer != EditsLayer.Strict)
        {
            json.WriteStartObject("edits");
            json.WriteNumber("count", Project.Edits.Edits.Count);
            LayerFields(json);
            json.WriteEndObject();
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
        _understood = null;
        return Answer.Write(json => json.WriteString("language", Words.IsRussian ? "ru" : "en"));
    }

    /// <summary>The level a request names in "level", or null when it names none.</summary>
    private static PhraseLevel? LevelOf(Request request) =>
        request.Text("level") is { } name
            ? PhraseLevels.Parse(name) ?? throw FacadeException.BadRequest("\"level\" is \"short\", \"phrase\" or \"sentence\".")
            : null;

    /// <summary>
    /// Shows another level and remembers it with the piece (0018, section 3), unless "remember" is false: the level
    /// the player's device opens pieces in is shown without becoming the piece's own. The selection moves to the unit
    /// that is, encloses or starts it at the new level; the answer is the phrases with the selection as it now is.
    /// </summary>
    private string ChangeLevel(Request request)
    {
        PhraseLevel level = LevelOf(request) ?? throw FacadeException.BadRequest("Send \"level\": \"short\", \"phrase\" or \"sentence\".");
        Level = level;
        if (request.Bool("remember") ?? true)
        {
            Project = Project.OverrideSetting(GranularitySetting, PhraseLevels.SettingValue(level));
        }

        if (Selection is { } selection && _selectionKind == MotifSelectionKind.Range)
        {
            TickSpan mapped = Levels.Selectable(Levels.Map(selection.Span, level));
            if (mapped != selection.Span)
            {
                MotifSelection moved = MotifSelector.Select(Notes, Piece.Meter, mapped, selection.Filter, MotifSelection.MaxStructureBars);
                if (moved.NoteIds.Count > 0)
                {
                    Selection = moved;
                    _understood = null;
                    _chip = null;
                    Chain = null;
                }
            }
        }

        return Answer.Write(json =>
        {
            Answer.Phrases(json, Levels, Level, Words);
            if (Selection is { } now)
            {
                Answer.Selection(json, "selection", now, KeySignature.InForce(Piece.Keys, now.Span.Onset), Piece.Meter);
            }
            else
            {
                json.WriteNull("selection");
            }
        });
    }

    /// <summary>Make phrase, Merge with next, Split here or Reset to detected at a level (the one shown unless "level" says otherwise).</summary>
    private string EditPhrases(Request request)
    {
        PhraseLevel level = LevelOf(request) ?? Level;
        LevelEdits edits;
        if (request.Span("makePhrase") is { } span)
        {
            edits = Levels.MakeUnit(level, span);
        }
        else if (request.Int("joinWithNext") is int number)
        {
            PhraseUnit unit = Levels.Units(level).FirstOrDefault(unit => unit.Number == number) ?? throw FacadeException.BadRequest($"There is no {PhraseLevels.Name(level)} {number}.");
            edits = Levels.JoinWithNext(level, unit);
        }
        else if (request.Long("split") is long at)
        {
            edits = Levels.SplitAt(level, new Tick(at));
        }
        else if (request.Bool("reset") == true)
        {
            edits = Levels.Reset(level);
        }
        else
        {
            throw FacadeException.BadRequest("Send \"makePhrase\" (a span), \"joinWithNext\" (a number), \"split\" (a tick) or \"reset\", with an optional \"level\".");
        }

        Structure = Structure.With(Levels.With(edits));
        // Bookmarks made from a phrase follow it through the merge or split (0018, section 5).
        Project = Project.WithPhraseLevels(Levels.Edits).FollowPhrases(Levels.Phrases.Phrases.Select(phrase => phrase.Span));
        _understood = null;
        return Answer.Write(json => Answer.Phrases(json, Levels, Level, Words));
    }

    /// <summary>The sentence map's facts about the selection: about the phrase it is, or the sentence it is; none for another range.</summary>
    private IReadOnlyList<MotifFact> SentenceFacts()
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        IReadOnlyList<MotifFact> facts = Levels.Phrases.Matching(selection.Span) is { } phrase ? Levels.Sentences.Facts(phrase)
            : Levels.Sentences.Matching(selection.Span) is { } sentence ? Levels.Sentences.Facts(sentence)
            : [];
        return Doubt().On(facts, selection);
    }

    /// <summary>Sounds a Try chip (0018, section 7): one accepted step over the selection with the chip's Keep preset.</summary>
    private string TryChip(Request request)
    {
        string id = request.Required("chip");
        TryChip chip = TryChips.Find(id) ?? throw FacadeException.BadRequest($"There is no chip \"{id}\".");
        VariationChain chain = TryChips.Start(chip, Understood(), out string? reason) ?? throw FacadeException.Unavailable(reason ?? "The chip cannot run on this selection.");
        Keep.Clear();
        Keep.UnionWith(chain.Context.Keep);
        Chain = chain;
        _chip = chip;
        _hiddenVariation = null;
        return ChainAnswer(chain);
    }

    private string Select(Request request)
    {
        SelectionFilter filter = request.Filter("filter");
        MotifSelection selection;
        MotifSelectionKind kind = MotifSelectionKind.Range;
        if (request.Has("notes"))
        {
            selection = MotifSelector.SelectNotes(Notes, Piece.Meter, request.Texts("notes"));
            kind = MotifSelectionKind.Notes;
        }
        else if (request.Span("span") is { } span)
        {
            // "snap": "beat" (#349, within contract 12) takes the span out to whole beats, so it can start and end mid-bar.
            TickSpan chosen = request.Text("snap") switch
            {
                null => span,
                "beat" => MotifSelector.ToBeats(Piece.Meter, span),
                _ => throw FacadeException.BadRequest("\"snap\" is \"beat\"."),
            };
            selection = MotifSelector.Select(Notes, Piece.Meter, chosen, filter);
        }
        else
        {
            Request bars = request.Object("bars");
            int first = bars.Int("first") ?? throw FacadeException.BadRequest("Send \"bars\" {first, last}, \"span\" {onset, end} or \"notes\" [ids].");
            int last = bars.Int("last") ?? first;
            selection = MotifSelector.Select(Notes, Piece.Meter, MotifSelector.Bars(Piece.Meter, first, last, Piece.End), filter);
        }

        if (selection.NoteIds.Count == 0)
        {
            throw FacadeException.Unavailable("There are no notes in that passage.");
        }

        Selection = selection;
        _selectionKind = kind;
        _understood = null;
        _chip = null;
        Chain = null;
        _hiddenVariation = null;
        return Answer.Write(json => Answer.Selection(json, "selection", selection, KeySignature.InForce(Piece.Keys, selection.Span.Onset), Piece.Meter));
    }

    /// <summary>The circle of fifths for the selection, from the key and chords music-core read for it.</summary>
    private static KeyCircleView? Circle(InvitationContext context) =>
        KeyCircle.For(context.Selection, context.Notes, context.Meter, context.Motif.Key, context.Harmony, context.Words);

    private InvitationContext Understood()
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        // Fixing a note or resolving a Check flag changes the doubt, so the understanding is read again (#352).
        if (_understood is { } understood && _understoodFor is { } from && ReferenceEquals(from.Check, Project.Check) && ReferenceEquals(from.Notes, Notes))
        {
            return understood;
        }

        NoteDoubt doubt = Doubt();
        KeySignature? key = KeySignature.InForce(Piece.Keys, selection.Span.Onset);
        MotifAnalysis read = MotifAnalyzer.Analyze(Notes, Piece.Meter, selection, key, Piece.MusicXml?.AccentMarks, Phrases.Boundaries, Words);
        HarmonyAnalysis harmony = doubt.On(HarmonyAnalyzer.Analyze(Notes, Piece.Meter, selection, read.Key?.Key, Words), selection);
        MotifAnalysis motif = doubt.On(read, selection);
        StructureAnalysis structure = doubt.On(StructureAnalyzer.Analyze(Notes, Piece.Meter, selection, Phrases.Boundaries, Words), selection);
        IReadOnlyList<MotifFact> character = doubt.On(MotifAnalyzer.AnalyzeCharacter(Notes, Piece.Meter, selection, key, CharacterContext.FromPiece(Piece), Words), selection);
        _understoodFor = (Project.Check, Notes);
        return _understood = new InvitationContext(Notes, Piece.Meter, selection, motif, structure, character, Words) { Harmony = harmony };
    }

    /// <summary>The recognised notes still to check: in the bars with an open Check flag (#352).</summary>
    private NoteDoubt Doubt() => NoteDoubt.Of(Notes, Piece.Meter, Project.OpenChecks.Select(static flag => flag.Bar), Words);

    private string Clear()
    {
        Chain = null;
        _chip = null;
        return Answer.Write(json => json.WriteBoolean("cleared", true));
    }

    private string ChainAnswer(VariationChain chain) => Answer.Write(json =>
    {
        Answer.Chain(json, chain, Words);
        // No call starts an experiment since contract 10; the field stays so that "try" and "variation" answer as before.
        json.WriteNull("experiment");

        json.WriteString("chip", _chip?.Id);
    });

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
        bool heard = Transcribed.IsHeard(Piece);
        BarRange? bars = ExcerptOf(request);
        // Another version above (0022, section 4) is engraved as the comparison layer has it, likely artefacts only when asked.
        bool artefacts = request.Bool("artefacts") ?? false;
        SavedPerformance?[] versions = [.. OssiaSources(request).Select(Project.FindPerformance)];
        PerformanceLayerNotes?[] layers = [.. versions.Select(version => version is null ? null : VersionLayer(version, artefacts))];
        NotationOssia[] ossias = [.. OssiaSources(request).Select((source, index) => layers[index] is { } layer
            ? new NotationOssia(versions[index]!.Name, layer.Notes, AsHeard: true)
            : Ossia(source, heard, readable))];
        bool generated = heard || Project.ChangesNotes || bars is not null || ossias.Length > 0;
        GeneratedNotation notation;
        if (of == "piece")
        {
            notation = generated
                ? Generate(Notes, Piece.End, title, heard, readable, bars, ossias)
                : Piece.MusicXml is { } xml ? NotationGenerator.FromMusicXml(xml, title) : NotationGenerator.FromMidi(Piece.Midi!, title);
        }
        else if (of == "variation")
        {
            Note[] notes = InPlace(Shown());
            Tick end = notes.Length == 0 ? Piece.End : notes.Max(note => note.Span.End);
            notation = Generate(notes, end > Piece.End ? end : Piece.End, title, heard, readable, bars, ossias);
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

            MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
            return BarRange.Of(Piece.Meter, selection.Span);
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
            return new NotationOssia(Words.T("Variation", "Вариация"), Shown().Notes, AsHeard: heard && readable);
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

    private GeneratedNotation Generate(IEnumerable<Note> notes, Tick end, string title, bool heard, bool readable, BarRange? bars = null, IReadOnlyList<NotationOssia>? ossias = null)
    {
        IEnumerable<KeyChange> keys = NotationKeys(heard);
        if (!heard)
        {
            return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, bars: bars, ossias: ossias);
        }

        if (readable)
        {
            return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, asHeard: true, bars: bars, ossias: ossias);
        }

        PlaybackScore played = PlaybackScore.FromPiece(Piece);
        return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, playedTempos: played.Tempos, pedal: played.Pedal.Select(held => held.Span), bars: bars, ossias: ossias);
    }

    /// <summary>The key signatures the score is written in: the piece's, or for notes heard without one the key worked out from them.</summary>
    private IEnumerable<KeyChange> NotationKeys(bool heard) =>
        heard && Piece.Keys.Count == 0 && Transcribed.KeyOf(Notes) is { } heardKey
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
            "heard" => (Chain ?? throw FacadeException.NoVariation()).Heard,
            "inPlace" => InPlace(Shown()),
            "version" => VersionLayer(Performance(request), artefacts: false).Notes,
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

    /// <summary>The variation the player sees and hears: the pending step's result, else the accepted chain's.</summary>
    private Variation Shown()
    {
        VariationChain chain = Chain ?? throw FacadeException.NoVariation();
        return chain.Pending?.Result ?? chain.Result ?? throw FacadeException.Unavailable("The variation has no step yet.");
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
        var motifs = Project.Motifs.Where(motif => motif.Id != MotifId).ToList();
        var variations = Project.Variations.Where(variation => variation.MotifId != MotifId).ToList();
        if (_layer == EditsLayer.Off)
        {
            // With the corrections off, the selection is over notes they would change: the motifs stay as they were (version 0.5).
            motifs = [.. Project.Motifs];
            variations = [.. Project.Variations];
        }
        else if (Selection is { NoteIds.Count: > 0 } selection)
        {
            motifs.Insert(0, SavedMotif.From(Motif.Create(MotifId, 1, selection, _selectionKind, MotifMode.Discovery, Notes)));
            if (Chain is { Result: not null } chain)
            {
                variations.Insert(0, SavedVariation.Create(MotifId.Replace("m_", "v_", StringComparison.Ordinal), MotifId, chain));
            }
            else if (_hiddenVariation is { } hidden)
            {
                variations.Insert(0, hidden);
            }
        }

        Project = Project.WithPhraseLevels(Levels.Edits) with
        {
            Motifs = motifs,
            Variations = variations,
            LastOpened = now ?? Project.LastOpened,
        };
        // Corrections made in the note editor are kept only once the player saves them (testing item 15).
        return System.Text.Encoding.UTF8.GetString(ProjectFile.Write(_draftFrom is { } saved ? Project with { Edits = saved } : Project));
    }
}