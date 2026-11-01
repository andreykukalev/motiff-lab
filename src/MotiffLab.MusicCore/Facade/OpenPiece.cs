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
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// One open piece (0020): its project, the imported piece, the notes with the player's corrections,
/// the phrases, the one primary selection (0013) with what was understood about it and the
/// experiments offered for it, and the variation chain. The orchestration the web app's page does
/// today, kept in one place for both apps.
/// </summary>
internal sealed partial class OpenPiece
{
    private static readonly KeepProperty[] s_defaultKeep = [KeepProperty.Pitches, KeepProperty.PitchOrder];

    private InvitationContext? _understood;
    private IReadOnlyList<Experiment>? _offered;
    private (Experiment Experiment, int Alternative)? _trial;
    private TryChip? _chip;
    private MotifSelectionKind _selectionKind = MotifSelectionKind.Range;

    private OpenPiece(Project project, ImportedPiece piece, IReadOnlyList<Note> notes, Words words)
    {
        Project = project;
        Piece = piece;
        Notes = notes;
        Words = words;
        Levels = PhraseLevels.Suggest(notes, piece.Meter, piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, piece.End, KeySignature.InForce(piece.Keys, Tick.Zero), words, project.PhraseLevels);
        Level = PhraseLevels.Parse(project.SettingOverrides.GetValueOrDefault(GranularitySetting)) ?? PhraseLevels.Parse(SettingsCatalogue.Get(GranularitySetting).Default) ?? PhraseLevel.Phrase;
        Keep = [.. s_defaultKeep];
    }

    /// <summary>The piece setting that remembers the level shown (0018, section 3).</summary>
    public const string GranularitySetting = "phrases.granularity";

    public Project Project { get; private set; }

    public ImportedPiece Piece { get; }

    /// <summary>The imported notes with the project's corrections applied.</summary>
    public IReadOnlyList<Note> Notes { get; }

    public Words Words { get; private set; }

    /// <summary>The piece in three levels (0018, section 3) with the player's edits at each.</summary>
    public PhraseLevels Levels { get; private set; }

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
            throw new FacadeException("import", $"{rejection.Message} {rejection.Tip}".Trim()) { Data = { ["reason"] = Answer.Name(rejection.Reason), ["tip"] = rejection.Tip } };
        }

        return new OpenPiece(Project.Create(id, fileName, data, now), piece, piece.Notes, words);
    }

    /// <summary>A saved project, with its first motif back as the selection and that motif's variation.</summary>
    public static OpenPiece Open(byte[] projectFile, Words words)
    {
        Project project;
        OpenedProject opened;
        try
        {
            project = ProjectFile.Read(projectFile);
            opened = OpenedProject.Open(project);
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

        return result;
    }

    public string Call(string name, Request request) => name switch
    {
        "piece" => Answer.Write(json => Answer.Piece(json, Piece, Notes, withNotes: true)),
        "language" => Language(request),
        "phrases" => Answer.Write(json => Answer.Phrases(json, Levels, LevelOf(request) ?? Level, Words)),
        "phrases.level" => ChangeLevel(request),
        "phrases.edit" => EditPhrases(request),
        "select" => Select(request),
        "understand" => Answer.Write(json => Answer.Understanding(json, Understood(), SentenceFacts(), fact => TryChips.Offer(fact.Id, Understood()))),
        "experiments" => Answer.Write(json => Answer.Experiments(json, "experiments", Offered())),
        "experiments.start" => StartExperiment(request),
        "experiments.surprise" => Surprise(request),
        "try" => TryChip(request),
        "variation" => ChainAnswer(Chain ?? throw FacadeException.NoVariation()),
        "variation.start" => StartChain(request),
        "variation.propose" => Propose(request),
        "variation.check" => Check(request),
        "variation.accept" => Step(chain => chain.AcceptDisabledReason is { } reason ? throw FacadeException.Unavailable(reason) : chain.Accept()),
        "variation.reject" => Step(chain => chain.Reject()),
        "variation.undo" => Step(chain => chain.CanUndo ? chain.Undo() : throw FacadeException.Unavailable("There is no step to undo.")),
        "variation.redo" => Step(chain => chain.CanRedo ? chain.Redo() : throw FacadeException.Unavailable("There is no step to redo.")),
        "variation.clear" => Clear(),
        "notation" => Notation(request),
        "playback" => Playback(request),
        "project.save" => Save(request),
        "dna" => Answer.Write(json => json.WriteString("status", "notYet")),
        _ => CallOnProject(name, request),
    };

    public void Summary(Utf8JsonWriter json)
    {
        Answer.Project(json, "project", Project);
        json.WriteStartObject("summary");
        Answer.Piece(json, Piece, Notes, withNotes: false);
        json.WriteEndObject();
        json.WriteString("language", Words.IsRussian ? "ru" : "en");
        if (Selection is { } selection)
        {
            Answer.Selection(json, "selection", selection, KeySignature.InForce(Piece.Keys, selection.Span.Onset));
        }
        else
        {
            json.WriteNull("selection");
        }

        json.WriteBoolean("hasVariation", Chain is not null);
    }

    private string Language(Request request)
    {
        Words = Words.For(request.Required("language"));
        Levels = PhraseLevels.Suggest(Notes, Piece.Meter, Piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, Piece.End, KeySignature.InForce(Piece.Keys, Tick.Zero), Words, Levels.Edits);
        _understood = null;
        _offered = null;
        return Answer.Write(json => json.WriteString("language", Words.IsRussian ? "ru" : "en"));
    }

    /// <summary>The level a request names in "level", or null when it names none.</summary>
    private static PhraseLevel? LevelOf(Request request) =>
        request.Text("level") is { } name
            ? PhraseLevels.Parse(name) ?? throw FacadeException.BadRequest("\"level\" is \"short\", \"phrase\" or \"sentence\".")
            : null;

    /// <summary>
    /// Shows another level and remembers it with the piece (0018, section 3). The selection moves to the unit that
    /// is, encloses or starts it at the new level; the answer is the phrases with the selection as it now is.
    /// </summary>
    private string ChangeLevel(Request request)
    {
        PhraseLevel level = LevelOf(request) ?? throw FacadeException.BadRequest("Send \"level\": \"short\", \"phrase\" or \"sentence\".");
        Level = level;
        Project = Project.OverrideSetting(GranularitySetting, PhraseLevels.SettingValue(level));
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
                    _offered = null;
                    _trial = null;
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
                Answer.Selection(json, "selection", now, KeySignature.InForce(Piece.Keys, now.Span.Onset));
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

        Levels = Levels.With(edits);
        // Bookmarks made from a phrase follow it through the merge or split (0018, section 5).
        Project = Project.WithPhraseLevels(Levels.Edits).FollowPhrases(Levels.Phrases.Phrases.Select(phrase => phrase.Span));
        _understood = null;
        _offered = null;
        return Answer.Write(json => Answer.Phrases(json, Levels, Level, Words));
    }

    /// <summary>The sentence map's facts about the selection: about the phrase it is, or the sentence it is; none for another range.</summary>
    private IReadOnlyList<MotifFact> SentenceFacts()
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        if (Levels.Phrases.Matching(selection.Span) is { } phrase)
        {
            return Levels.Sentences.Facts(phrase);
        }

        return Levels.Sentences.Matching(selection.Span) is { } sentence ? Levels.Sentences.Facts(sentence) : [];
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
        _trial = null;
        _chip = chip;
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
            selection = MotifSelector.Select(Notes, Piece.Meter, span, filter);
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
        _offered = null;
        _trial = null;
        _chip = null;
        Chain = null;
        return Answer.Write(json => Answer.Selection(json, "selection", selection, KeySignature.InForce(Piece.Keys, selection.Span.Onset)));
    }

    private InvitationContext Understood()
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        if (_understood is { } understood)
        {
            return understood;
        }

        KeySignature? key = KeySignature.InForce(Piece.Keys, selection.Span.Onset);
        MotifAnalysis motif = MotifAnalyzer.Analyze(Notes, Piece.Meter, selection, key, Piece.MusicXml?.AccentMarks, Phrases.Boundaries, Words);
        StructureAnalysis structure = StructureAnalyzer.Analyze(Notes, Piece.Meter, selection, Phrases.Boundaries, Words);
        IReadOnlyList<MotifFact> character = MotifAnalyzer.AnalyzeCharacter(Notes, Piece.Meter, selection, key, CharacterContext.FromPiece(Piece), Words);
        return _understood = new InvitationContext(Notes, Piece.Meter, selection, motif, structure, character, Words);
    }

    private IReadOnlyList<Experiment> Offered() => _offered ??= ExperimentCatalogue.Offer(Understood());

    /// <summary>The key the variation operations work in: the one read from the motif unless it is uncertain.</summary>
    private MusicalKey? Key => Understood().Motif.Key is { Certainty: not FactCertainty.Uncertain } reading ? reading.Key : null;

    private string StartExperiment(Request request)
    {
        string id = request.Required("id");
        int alternative = request.Int("alternative") ?? 0;
        Experiment experiment = Offered().FirstOrDefault(offered => offered.Id == id) ?? throw FacadeException.BadRequest($"Experiment \"{id}\" is not offered for this passage.");
        if (alternative < 0 || alternative >= experiment.Alternatives.Count)
        {
            throw FacadeException.BadRequest($"Experiment \"{id}\" has {experiment.Alternatives.Count} alternatives.");
        }

        return Try(experiment, alternative);
    }

    private string Surprise(Request request)
    {
        var tried = request.Texts("tried").ToHashSet(StringComparer.Ordinal);
        SurprisePick? pick = ExperimentCatalogue.Surprise(Understood(), request.Int("seed") ?? 0, tried);
        return pick is null ? Answer.Write(json => json.WriteBoolean("none", true)) : Try(pick.Experiment, pick.Alternative);
    }

    private string Try(Experiment experiment, int alternative)
    {
        VariationChain chain = ExperimentCatalogue.Start(experiment, alternative, Understood());
        Keep.Clear();
        Keep.UnionWith(chain.Context.Keep);
        Chain = chain;
        _trial = (experiment, alternative);
        _chip = null;
        return ChainAnswer(chain);
    }

    private string StartChain(Request request)
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        if (request.Has("keep"))
        {
            Keep.Clear();
            Keep.UnionWith(request.Enums<KeepProperty>("keep"));
        }

        _trial = null;
        _chip = null;
        Chain = NewChain(selection, request.Int("seed") ?? 0);
        return ChainAnswer(Chain);
    }

    private VariationChain NewChain(MotifSelection selection, int seed) =>
        VariationChain.Start(VariationFacts.MotifNotes(Notes, selection), VariationContext.For(selection, Keep.ToHashSet(), seed, Key, Piece.Meter, Words));

    private string Propose(Request request)
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        (IVariationOperation operation, OperationParameters parameters) = ReadStep(request);
        VariationChain chain = Chain ?? NewChain(selection, 0);
        if (parameters is HandEditParameters edit)
        {
            if (chain.EditUnavailableReason(edit) is { } editReason)
            {
                throw FacadeException.Unavailable(editReason);
            }

            Chain = chain.Edit(edit);
            return ChainAnswer(Chain);
        }

        if (chain.UnavailableReason(operation, parameters) is { } reason)
        {
            throw FacadeException.Unavailable(reason);
        }

        Chain = chain.Propose(operation, parameters);
        return ChainAnswer(Chain);
    }

    private string Check(Request request)
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        (IVariationOperation operation, OperationParameters parameters) = ReadStep(request);
        VariationChain chain = Chain ?? NewChain(selection, 0);
        string? reason = parameters is HandEditParameters edit ? chain.EditUnavailableReason(edit) : chain.UnavailableReason(operation, parameters);
        return Answer.Write(json => json.WriteString("reason", reason));
    }

    /// <summary>A step in the project file's shape: {"operation": id, "parameters": {…}}.</summary>
    private static (IVariationOperation Operation, OperationParameters Parameters) ReadStep(Request request)
    {
        string id = request.Required("operation");
        IVariationOperation operation = id == HandEdit.OperationId ? HandEdit.Instance : VariationOperations.Find(id) ?? throw FacadeException.BadRequest($"There is no operation \"{id}\".");
        if (!request.Has("parameters"))
        {
            throw FacadeException.BadRequest("The step needs \"parameters\".");
        }

        try
        {
            return (operation, ProjectFile.ReadStep(request.Root).Parameters);
        }
        catch (ProjectFileException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    private string Step(Func<VariationChain, VariationChain> step)
    {
        Chain = step(Chain ?? throw FacadeException.NoVariation());
        return ChainAnswer(Chain);
    }

    private string Clear()
    {
        Chain = null;
        _trial = null;
        _chip = null;
        return Answer.Write(json => json.WriteBoolean("cleared", true));
    }

    private string ChainAnswer(VariationChain chain) => Answer.Write(json =>
    {
        Answer.Chain(json, chain, Words);
        if (_trial is { } trial)
        {
            json.WriteStartObject("experiment");
            json.WriteString("id", trial.Experiment.Id);
            json.WriteNumber("alternative", trial.Alternative);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("experiment");
        }

        json.WriteString("chip", _chip?.Id);
    });

    private string Notation(Request request)
    {
        string of = request.Text("of") ?? "piece";
        string title = request.Text("title") ?? Path.GetFileNameWithoutExtension(Piece.FileName);
        bool readable = request.Bool("readable") ?? true;
        bool heard = Transcribed.IsHeard(Piece);
        GeneratedNotation notation;
        if (of == "piece")
        {
            notation = heard || !Project.Edits.IsEmpty
                ? Generate(Notes, Piece.End, title, heard, readable)
                : Piece.MusicXml is { } xml ? NotationGenerator.FromMusicXml(xml, title) : NotationGenerator.FromMidi(Piece.Midi!, title);
        }
        else if (of == "variation")
        {
            Note[] notes = InPlace(Shown());
            Tick end = notes.Length == 0 ? Piece.End : notes.Max(note => note.Span.End);
            notation = Generate(notes, end > Piece.End ? end : Piece.End, title, heard, readable);
        }
        else
        {
            throw FacadeException.BadRequest("\"of\" is \"piece\" or \"variation\".");
        }

        return Answer.Write(json => Answer.Notation(json, notation));
    }

    private GeneratedNotation Generate(IEnumerable<Note> notes, Tick end, string title, bool heard, bool readable)
    {
        IEnumerable<KeyChange> keys = heard && Piece.Keys.Count == 0 && Transcribed.KeyOf(Notes) is { } heardKey
            ? [new KeyChange(Tick.Zero, heardKey.Fifths)]
            : Piece.Keys.Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)));
        if (!heard)
        {
            return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title);
        }

        if (readable)
        {
            return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, asHeard: true);
        }

        PlaybackScore played = PlaybackScore.FromPiece(Piece);
        return NotationGenerator.Generate(notes, Piece.Meter, keys, end, title, playedTempos: played.Tempos, pedal: played.Pedal.Select(held => held.Span));
    }

    private string Playback(Request request)
    {
        PlaybackScore score = PlaybackScore.FromPiece(Piece);
        if (!Project.Edits.IsEmpty)
        {
            score = score.WithNotes(Notes);
        }

        score = (request.Text("of") ?? "piece") switch
        {
            "piece" => score,
            "heard" => score.WithNotes((Chain ?? throw FacadeException.NoVariation()).Heard),
            "inPlace" => score.WithNotes(InPlace(Shown())),
            _ => throw FacadeException.BadRequest("\"of\" is \"piece\", \"heard\" or \"inPlace\"."),
        };
        return Answer.Write(json => Answer.Playback(json, score));
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
        if (Selection is { NoteIds.Count: > 0 } selection)
        {
            motifs.Insert(0, SavedMotif.From(Motif.Create(MotifId, 1, selection, _selectionKind, MotifMode.Discovery, Notes)));
            if (Chain is { Result: not null } chain)
            {
                variations.Insert(0, SavedVariation.Create(MotifId.Replace("m_", "v_", StringComparison.Ordinal), MotifId, chain));
            }
        }

        Project = Project.WithPhraseLevels(Levels.Edits) with
        {
            Motifs = motifs,
            Variations = variations,
            LastOpened = now ?? Project.LastOpened,
        };
        return System.Text.Encoding.UTF8.GetString(ProjectFile.Write(Project));
    }
}