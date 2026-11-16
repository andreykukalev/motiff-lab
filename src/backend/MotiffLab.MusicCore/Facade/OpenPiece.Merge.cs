using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// Merging a version through a draft (testing items 32 and 34; contract 7): open a draft for a version, choose a bar or a
/// phrase from either side or write it in the roll, undo, redo and reset, see and hear the three views (mine, the version,
/// the result), apply it, open it again, or undo the merge. Nothing changes in the score until the draft is applied, and
/// a removed version can be put back while the piece is open.
/// </summary>
internal sealed partial class OpenPiece
{
    /// <summary>The draft open, for one version; null when none is.</summary>
    private MergeDraft? _merge;

    /// <summary>The version removed last, where it was and the Check flags performances had: "Версия убрана · Вернуть".</summary>
    private (SavedPerformance Performance, int Index, IReadOnlyList<CheckFlag> Flags)? _removedVersion;

    private string CallOnMerge(string name, Request request) => name switch
    {
        "performance.merge" => MergeAnswer(),
        "performance.merge.open" => OpenMerge(request),
        "performance.merge.choose" => ChangeMerge(draft => draft.Choose(BarsOf(request), ChoiceOf(request))),
        "performance.merge.own" => ChangeMerge(draft => draft.WriteOwn(OwnBarOf(draft, request))),
        "performance.merge.undecide" => ChangeMerge(draft => draft.Undecide(BarsOf(request))),
        "performance.merge.undo" => ChangeMerge(draft => draft.Undo()),
        "performance.merge.redo" => ChangeMerge(draft => draft.Redo()),
        "performance.merge.reset" => ChangeMerge(draft => draft.Reset()),
        "performance.merge.cancel" => CancelMerge(),
        "performance.merge.apply" => ApplyMerge(request),
        "performance.merge.undoApplied" => UndoMerge(request),
        "performance.merge.notation" => MergeNotation(request),
        "performance.merge.playback" => MergePlayback(request),
        "performance.putBack" => PutBackVersion(),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

    private MergeDraft Draft => _merge ?? throw FacadeException.Unavailable("No merge draft is open: send \"performance.merge.open\" first.");

    private SavedPerformance DraftVersion => Project.FindPerformance(Draft.PerformanceId) ?? throw FacadeException.Unavailable("The draft's version is no longer in the piece.");

    /// <summary>"id" and "start": "mine", "sure", "all" or "saved" (as last applied, the default).</summary>
    private string OpenMerge(Request request)
    {
        SavedPerformance performance = Performance(request);
        _merge = PerformanceMerge.Open(performance, Compare(performance), request.Enum<MergeStart>("start") ?? MergeStart.Saved);
        return MergeAnswer();
    }

    private string ChangeMerge(Func<MergeDraft, MergeDraft> change)
    {
        _merge = change(Draft);
        return MergeAnswer();
    }

    private string CancelMerge()
    {
        _merge = null;
        return Answer.Write(json => json.WriteNull("merge"));
    }

    /// <summary>"at", the time it is applied (ISO 8601), so the version page can say when; the score takes the draft's bars.</summary>
    private string ApplyMerge(Request request)
    {
        MergeDraft draft = Draft;
        SavedPerformance performance = DraftVersion;
        DateTimeOffset at = request.Text("at") is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"at\" must be an ISO 8601 time.")
            : DateTimeOffset.UnixEpoch;
        Project = Project.ChangePerformance(performance.Id, kept => PerformanceMerge.Apply(draft, kept, Compare(kept), Piece.Meter, Printed, at));
        _merge = null;
        RefreshNotes();
        return PerformanceAnswer(Project.FindPerformance(performance.Id)!);
    }

    /// <summary>"Отменить слияние": the player's notes back in every bar taken from the version; the choices stay to open again.</summary>
    private string UndoMerge(Request request) =>
        ChangePerformance(request, PerformanceMerge.Undo, notesChange: true);

    /// <summary>A version removed last put back where it was, with its taken bars and Check flags.</summary>
    private string PutBackVersion()
    {
        if (_removedVersion is not { } removed)
        {
            throw FacadeException.Unavailable("No version was removed while the piece is open.");
        }

        Project = Project.PutBackPerformance(removed.Performance, removed.Index, removed.Flags);
        _removedVersion = null;
        if (removed.Performance.Taken.Count > 0)
        {
            RefreshNotes();
        }

        return PerformanceAnswer(removed.Performance);
    }

    private static MergeChoice ChoiceOf(Request request) =>
        request.Enum<MergeChoice>("choice") is { } choice and not MergeChoice.Own
            ? choice
            : throw FacadeException.BadRequest("\"choice\" is \"mine\", \"version\" or \"notADifference\"; a bar the player writes goes through \"performance.merge.own\".");

    /// <summary>"bar" and "notes", the bar as the player left it in the roll: each {pitch, onset, length, velocity, track, hand, id}; an id the bar had keeps that note.</summary>
    private TakenBar OwnBarOf(MergeDraft draft, Request request)
    {
        int bar = BarOf(request);
        List<Note> notes = [];
        int index = 0;
        foreach (JsonElement element in request.Elements("notes"))
        {
            Request note = Request.Of(element);
            int pitch = note.Int("pitch") ?? throw FacadeException.BadRequest("Each note needs \"pitch\".");
            long onset = note.Long("onset") ?? throw FacadeException.BadRequest("Each note needs \"onset\", in ticks.");
            long length = note.Long("length") ?? throw FacadeException.BadRequest("Each note needs \"length\", in ticks.");
            if (pitch is < 0 or > Pitch.MaxMidiNumber || onset < 0 || length <= 0)
            {
                throw FacadeException.BadRequest("\"pitch\" runs from 0 to 127, \"onset\" from 0 and \"length\" from 1.");
            }

            Hand? hand = note.Text("hand") switch
            {
                "left" => Hand.Left,
                "right" => Hand.Right,
                _ => null,
            };
            notes.Add(new Note
            {
                Id = note.Text("id") ?? $"own-{index++}",
                Pitch = new Pitch(pitch),
                Span = new TickSpan(new Tick(onset), length),
                Velocity = Math.Clamp(note.Int("velocity") ?? 80, Note.MinVelocity, Note.MaxVelocity),
                Track = note.Int("track") ?? 0,
                Hand = hand is { } held ? new Attributed<Hand>(held, Attribution.UserEdited) : null,
                Attribution = Attribution.UserEdited,
            });
        }

        try
        {
            return PerformanceMerge.OwnBar(draft.PerformanceId, bar, Piece.Meter, Printed, notes);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw FacadeException.BadRequest($"Bar {bar} is not in the piece.");
        }
    }

    /// <summary>The notes of one of the draft's three views, with the ids to tint: "mine", "version" or "result".</summary>
    private (IReadOnlyList<Note> Notes, IReadOnlyList<string> Version, IReadOnlyList<string> Own) MergeView(Request request)
    {
        MergeDraft draft = Draft;
        SavedPerformance performance = DraftVersion;
        switch (request.Text("view") ?? "result")
        {
            case "mine":
                return (Printed, [], []);
            case "version":
                PerformanceLayerNotes layer = VersionLayer(performance, artefacts: false);
                return (layer.Notes, layer.Added, []);
            case "result":
                IReadOnlyList<Note> result = PerformanceMerge.Result(draft, performance, Compare(performance), Piece.Meter, Printed);
                string own = PerformanceMerge.OwnPrefix(performance.Id);
                string taken = $"{PerformanceTake.IdPrefix}{performance.Id}-";
                return (result,
                    [.. result.Select(note => note.Id).Where(id => id.StartsWith(taken, StringComparison.Ordinal) && !id.StartsWith(own, StringComparison.Ordinal))],
                    [.. result.Select(note => note.Id).Where(id => id.StartsWith(own, StringComparison.Ordinal))]);
            default:
                throw FacadeException.BadRequest("\"view\" is \"mine\", \"version\" or \"result\".");
        }
    }

    /// <summary>
    /// A view's bars engraved on their own ("bars" {first, last}), or the whole piece on its pages without "bars", as the
    /// score shows the view while the draft is open; with "version" (plum) and "own" (amber) ids engraved.
    /// </summary>
    private string MergeNotation(Request request)
    {
        (IReadOnlyList<Note> notes, IReadOnlyList<string> version, IReadOnlyList<string> own) = MergeView(request);
        BarRange? bars = ExcerptOf(request);
        bool heard = Transcribed.IsHeard(Piece);
        GeneratedNotation notation = Generate(notes, Piece.End, request.Text("title") ?? "", heard, request.Bool("readable") ?? true, bars);
        return Answer.Write(json =>
        {
            Answer.Notation(json, notation);
            WriteIds(json, "version", version);
            WriteIds(json, "own", own);
        });
    }

    /// <summary>A view's notes to hear: the whole piece with the view's notes, so the app loops the bars it shows.</summary>
    private string MergePlayback(Request request)
    {
        (IReadOnlyList<Note> notes, _, _) = MergeView(request);
        PlaybackScore score = PlaybackScore.FromPiece(Piece).WithNotes(notes);
        return Answer.Write(json => Answer.Playback(json, score));
    }

    private string MergeAnswer() => Answer.Write(json =>
    {
        if (_merge is not { } draft || Project.FindPerformance(draft.PerformanceId) is not { } performance)
        {
            json.WriteNull("merge");
            return;
        }

        PerformanceComparison comparison = Compare(performance);
        Answer.Merge(json, draft, performance, comparison, PerformanceMerge.Taken(draft, performance, comparison, Piece.Meter, Printed), Words);
    });

    private static void WriteIds(Utf8JsonWriter json, string name, IReadOnlyList<string> ids)
    {
        json.WriteStartArray(name);
        foreach (string id in ids)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }
}