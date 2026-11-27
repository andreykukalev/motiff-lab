using System.Text.Json;

using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The phrases of the open piece (0018, section 3; the phrases spec): the level shown, and the player's edits of the
/// boundaries, kept in the project, which a recalculation of the notes never moves (<see cref="ReadStructure"/> reads the
/// structure again with them). An edit never changes the selection; its answer says which phrases it changed.
/// </summary>
internal sealed partial class OpenPiece
{
    /// <summary>The level a request names in "level", or null when it names none.</summary>
    private static PhraseLevel? LevelOf(Request request) =>
        request.Text("level") is { } name
            ? PhraseLevels.Parse(name) ?? throw FacadeException.BadRequest("\"level\" is \"short\", \"phrase\" or \"sentence\".")
            : null;

    /// <summary>
    /// An edit of the boundaries, one step for Undo: at a level (the one shown unless "level" says otherwise) Make phrase,
    /// Merge with next, Split here or Reset to detected; or, of the phrases, a mark dragged ("move" {from, to}, the target
    /// snapped to the nearest note), «Начать фразу здесь» ("start", at the first note from that tick), «Убрать границу»
    /// ("remove", a phrase start), «Вернуть найденную» ("putBack", a start of the player's), «Вернуть найденные границы»
    /// ("resetAll": the phrases as found, the other levels as they are, answering "replaced", how many of the player's
    /// boundaries went) or Undo and Redo ("restore", the edits a "previous" gave, with "expected", the "edits" it left, and
    /// "step", the step's number its answer gave: its anchors are put back as they were before it, for an Undo, or after
    /// it, for a Redo; with no "step", or one this opening no longer keeps, they follow the edits as any other's).
    /// The answer is the phrases with what the edit changed (<see cref="Answer.PhraseEdit"/>) and "step", the step's number
    /// (a new one for an edit, the step undone or redone, else null). What is attached to a phrase
    /// goes with it (#318, <see cref="ProjectAnchors.AnchorsFollowed"/>); bookmarks stay where they are (the phrases spec: a bookmark
    /// never moves with a phrase boundary).
    /// </summary>
    private string EditPhrases(Request request)
    {
        PhraseLevels before = Levels;
        AnchorState anchors = Project.AnchorState();
        LevelEdits edits;
        try
        {
            edits = PhraseEditOf(request) ?? LevelEditOf(request, LevelOf(request) ?? Level);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        LevelEdits now = Normal(before.Edits);
        PhraseStep? undone = request.Has("restore") && request.Int("step") is int sent ? _steps.FirstOrDefault(step => step.Number == sent) : null;
        Structure = Structure.With(Levels.With(edits));
        Project = Project.WithPhraseLevels(Levels.Edits).AnchorsFollowed(before.Phrases, Phrases);
        int? number = request.Has("restore") ? RestoreAnchors(undone, now) : KeepStep(now, Normal(Levels.Edits), anchors);

        Selection.ForgetUnderstanding();
        return Answer.Write(json =>
        {
            PhrasesAnswer(json, Level);
            Answer.PhraseEdit(json, before, Levels);
            WriteStep(json, number);
            if (request.Bool("resetAll") == true)
            {
                json.WriteNumber("replaced", before.Phrases.OwnCount);
            }
        });
    }

    /// <summary>The most phrases steps whose anchors an Undo or Redo puts back (<see cref="_steps"/>); an older step is undone by its edits alone.</summary>
    internal const int KeptSteps = 100;

    /// <summary>
    /// The phrases steps of this opening, oldest first, each with its number, its edits and the anchors before and after it
    /// (#318), so an Undo or Redo ("restore" with the step's "step") puts back exactly what was attached where: Undo of a
    /// join brings the second anchor back with what was on it. Written only by <see cref="EditPhrases"/>.
    /// </summary>
    private readonly List<PhraseStep> _steps = [];

    /// <summary>The number of the latest phrases step of this opening; the first is 1.</summary>
    private int _lastStep;

    /// <summary>A phrases step: its number, the edits before and after it, as <see cref="Normal(LevelEdits)"/> writes them, and the anchors then.</summary>
    private sealed record PhraseStep(int Number, LevelEdits Before, LevelEdits After, AnchorState Was, AnchorState Made);

    /// <summary>
    /// Keeps a new phrases step from the edits <paramref name="before"/> and the anchors <paramref name="was"/> to the edits
    /// <paramref name="after"/> and the anchors now, the oldest dropped past <see cref="KeptSteps"/>; its number.
    /// </summary>
    private int KeepStep(LevelEdits before, LevelEdits after, AnchorState was)
    {
        _steps.Add(new PhraseStep(++_lastStep, before, after, was, Project.AnchorState()));
        if (_steps.Count > KeptSteps)
        {
            _steps.RemoveAt(0);
        }

        return _lastStep;
    }

    /// <summary>
    /// Puts back the anchors of <paramref name="step"/>, undone or redone from the edits <paramref name="now"/>: as they were
    /// before it when <paramref name="now"/> are the edits it left (Undo), as it left them when they are those it found (Redo);
    /// nothing otherwise, the anchors having followed the edits. The step's number, or null with no step.
    /// </summary>
    private int? RestoreAnchors(PhraseStep? step, LevelEdits now)
    {
        if ((step is null ? null : step.After.Equals(now) ? step.Was : step.Before.Equals(now) ? step.Made : null) is { } state)
        {
            Project = Project.WithAnchorState(state, Phrases);
        }

        return step?.Number;
    }

    private static void WriteStep(Utf8JsonWriter json, int? number)
    {
        if (number is int step)
        {
            json.WriteNumber("step", step);
        }
        else
        {
            json.WriteNull("step");
        }
    }

    /// <summary>The phrases answer (<see cref="Answer.Phrases"/>) at <paramref name="level"/>, with the project's phrase anchors.</summary>
    private void PhrasesAnswer(Utf8JsonWriter json, PhraseLevel level) => Answer.Phrases(json, Levels, Structure.Sections, level, Words, Piece.Meter, Project.Anchors);

    /// <summary>
    /// phrases.reattach (#318, 0026 C1): every phrase anchor that no longer matches the phrases, after the notes or their
    /// analysis changed, attached again with what is attached to it, as one group (<see cref="ProjectAnchors.Reattached"/>); the
    /// answer is the phrases with "reattached", how many anchors were. Nothing else changes, the selection neither.
    /// </summary>
    private string ReattachPhrases()
    {
        (Project, int reattached) = Project.Reattached(Phrases);
        return Answer.Write(json =>
        {
            PhrasesAnswer(json, Level);
            json.WriteNumber("reattached", reattached);
        });
    }

    /// <summary>The edits an edit of the phrases asks for, with the other levels as they are; null for an edit at a level.</summary>
    private LevelEdits? PhraseEditOf(Request request)
    {
        if (request.Has("move"))
        {
            Request move = request.Object("move");
            Tick from = new(move.Long("from") ?? throw FacadeException.BadRequest("\"move\" needs \"from\", the tick a phrase starts at."));
            Tick to = new(move.Long("to") ?? throw FacadeException.BadRequest("\"move\" needs \"to\", a tick."));
            return Levels.Edits.With(PhraseLevel.Phrase, Phrases.Move(from, Levels.NoteAt(to)));
        }

        return request.Long("start") is long start ? Levels.Edits.With(PhraseLevel.Phrase, Phrases.StartAt(Levels.NoteFrom(new Tick(start))))
            : request.Long("remove") is long remove ? Levels.Edits.With(PhraseLevel.Phrase, Phrases.Remove(new Tick(remove)))
            : request.Long("putBack") is long putBack ? Levels.Edits.With(PhraseLevel.Phrase, Phrases.PutBack(new Tick(putBack)))
            : request.Bool("resetAll") == true ? Levels.Reset(PhraseLevel.Phrase)
            : request.Has("restore") ? Restore(request)
            : null;
    }

    /// <summary>
    /// The edits "restore" puts back (Undo or Redo of a step), once the edits now are those "expected" (the edits the step
    /// left, its answer's "edits"); otherwise, the phrases having changed since, refused with the edits as they are.
    /// </summary>
    private LevelEdits Restore(Request request)
    {
        if (!request.Has("expected"))
        {
            throw FacadeException.BadRequest("\"restore\" needs \"expected\", the edits the step being undone or redone left (its answer's \"edits\").");
        }

        if (!Restored(request.Object("expected")).Equals(Normal(Levels.Edits)))
        {
            throw FacadeException.Unavailable(Words.T(
                "The phrases have changed since, so this step can no longer be undone.",
                "Фразы с тех пор изменились, этот шаг уже не отменить."));
        }

        return Restored(request.Object("restore"));
    }

    /// <summary>Edits with each list in time order, each tick once, as <see cref="Restored"/> reads them, so edits sent back compare with the edits kept.</summary>
    private static LevelEdits Normal(LevelEdits edits) =>
        new(Normal(edits.Short), Normal(edits.Phrase), Normal(edits.Sentence));

    private static PhraseEdits Normal(PhraseEdits edits) => new(Ordered(edits.Added), Ordered(edits.Removed));

    private static Tick[] Ordered(IEnumerable<Tick> ticks) => [.. ticks.Distinct().Order()];

    /// <summary>The edits at a level an edit asks for: Make phrase, Merge with next, Split here or Reset to detected.</summary>
    private LevelEdits LevelEditOf(Request request, PhraseLevel level)
    {
        if (request.Span("makePhrase") is { } span)
        {
            return Levels.MakeUnit(level, span);
        }

        if (request.Int("joinWithNext") is int number)
        {
            PhraseUnit unit = Levels.Units(level).FirstOrDefault(unit => unit.Number == number) ?? throw FacadeException.BadRequest($"There is no {PhraseLevels.Name(level)} {number}.");
            return Levels.JoinWithNext(level, unit);
        }

        return request.Long("split") is long at ? Levels.SplitAt(level, new Tick(at))
            : request.Bool("reset") == true ? Levels.Reset(level)
            : throw FacadeException.BadRequest("Send \"makePhrase\" (a span), \"joinWithNext\" (a number), \"split\" (a tick) or \"reset\", with an optional \"level\"; or \"move\" {from, to}, \"start\", \"remove\" or \"putBack\" (a tick), \"resetAll\" or \"restore\" (the edits) with \"expected\".");
    }

    /// <summary>The edits "restore" sends, {short, phrase, sentence} each {added, removed} as the answers write them; a level left out has none.</summary>
    private static LevelEdits Restored(Request restore) =>
        new(EditsOf(restore, PhraseLevel.Short), EditsOf(restore, PhraseLevel.Phrase), EditsOf(restore, PhraseLevel.Sentence));

    private static PhraseEdits EditsOf(Request restore, PhraseLevel level)
    {
        Request edits = restore.Object(PhraseLevels.Name(level));
        return new PhraseEdits(TicksOf(edits, "added"), TicksOf(edits, "removed"));
    }

    private static Tick[] TicksOf(Request edits, string name) =>
        Ordered(edits.Elements(name).Select(tick => tick.ValueKind == JsonValueKind.Number && tick.TryGetInt64(out long value) && value >= 0 ? new Tick(value) : throw FacadeException.BadRequest($"\"{name}\" holds ticks.")));
}