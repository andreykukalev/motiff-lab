using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>
/// The assistant as an action interface (#582 item 17.3, the lead's decision 3): the model chooses a catalogue operation
/// with in-range parameters, music-core makes the draft, and the device makes it again from the call when the player
/// accepts. A tie the reading lost is joined; a call out of range never runs.
/// </summary>
public class ActionToolsTests
{
    /// <summary>Bar 1 of 3/4: E5 C5 C5 (the two Cs a tie the reading lost); bar 2: C5 for two beats, then D5.</summary>
    private static AssistantPiece Piece() => Open(Score(Note("E", 5, 2) + Note("C", 5, 2) + Note("C", 5, 2), Note("C", 5, 4) + Note("D", 5, 2)));

    private static System.Text.Json.JsonElement Join(string address, int to = 2) => Json($$"""{"bars": {"from": 1, "to": {{to}}}, "address": "{{address}}"}""");

    [Fact]
    public void Joining_a_tie_holds_the_first_note_through_the_second_and_removes_it()
    {
        AssistantPiece piece = Piece();

        ToolResult result = AssistantTools.Call(piece, "notes.join", Join("1.R.2", to: 1));

        AssistantDraft draft = Assert.Single(result.Drafts);
        Assert.Equal([DraftEditKind.Remove, DraftEditKind.Change], draft.Edits.Select(edit => edit.Kind));
        DraftEdit held = draft.Edits[1];
        Assert.Equal(held.Before!.Value.Span.Onset, held.After!.Value.Span.Onset);
        Assert.Equal(2 * held.Before.Value.Span.Duration, held.After.Value.Span.Duration);
        Assert.Equal(piece.Addresses.Resolve("1.R.3", Words.English, out _)!.Id, draft.Edits[0].NoteId);

        Assert.Equal(piece.Meter.GetBar(1).Span, draft.Measure(piece.Meter));

        // Across the bar line too: the last C of bar 1 and the C that starts bar 2.
        AssistantDraft across = Assert.Single(AssistantTools.Call(piece, "notes.join", Join("1.R.3")).Drafts);
        Assert.Equal(TickSpan.FromBounds(piece.Meter.GetBar(1).Span.Onset, piece.Meter.GetBar(2).Span.End), across.Measure(piece.Meter));
        Assert.Equal(5, piece.Notes.Count);
    }

    [Fact]
    public void A_note_with_nothing_to_tie_to_or_outside_the_bars_is_refused_with_the_reason()
    {
        AssistantPiece piece = Piece();

        Assert.Contains("No note of the same pitch starts where 1.R.1 ends", AssistantTools.Call(piece, "notes.join", Join("1.R.1")).Refusal, StringComparison.Ordinal);
        Assert.Contains("No note of the same pitch", AssistantTools.Call(piece, "notes.join", Join("1.R.3", to: 1)).Refusal, StringComparison.Ordinal);
        Assert.Contains("text matching", AssistantTools.Call(piece, "notes.join", Json("""{"bars": {"from": 1, "to": 2}, "address": "C5"}""")).Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_out_of_range_or_of_a_tool_the_role_lacks_is_refused_before_it_runs()
    {
        var inRange = new ToolCall("c1", "notes.join", Join("1.R.2"));
        Assert.Null(AssistantRoles.CallRefusal(AssistantRole.Proposer, inRange, Words.English));
        Assert.Contains("no tool", AssistantRoles.CallRefusal(AssistantRole.Mentor, inRange, Words.English), StringComparison.Ordinal);
        var outOfRange = new ToolCall("c2", "repair.candidates", Json("""{"bar": 100000, "reason": "barTooLong"}"""));
        Assert.Contains("\"bar\" must be a whole number from 0 to 9999", AssistantRoles.CallRefusal(AssistantRole.Proposer, outOfRange, Words.English), StringComparison.Ordinal);
    }

    [Fact]
    public void The_device_makes_the_draft_again_from_the_call_and_not_once_the_notes_changed()
    {
        AssistantPiece piece = Piece();
        AssistantSession session = AssistantSession.Start(AssistantRole.Proposer).Ask("Tie the two Cs.");
        ToolCall call = new("c1", "notes.join", Join("1.R.2"));
        session = session.Called([call]).Resulted(TurnRunner.Run(piece, AssistantRole.Proposer, [call], 0));

        SessionDraft kept = Assert.Single(session.Drafts);
        AssistantDraft made = kept.Make(piece)!;
        Assert.Equal(AssistantTools.Call(piece, "notes.join", Join("1.R.2")).Drafts[0].Edits, made.Edits);

        // The notes changed under it (both Cs after 1.R.2 gone): the call makes no draft any more, so none can be accepted.
        string[] gone = [piece.Addresses.Resolve("1.R.3", Words.English, out _)!.Id, piece.Addresses.Resolve("2.R.1", Words.English, out _)!.Id];
        AssistantPiece changed = new(piece.Piece, [.. piece.Notes.Where(note => !gone.Contains(note.Id))], []);
        Assert.Null(kept.Make(changed));
    }

    [Fact]
    public void An_answer_about_a_draft_may_cite_no_fact_but_never_an_unknown_one()
    {
        System.Text.Json.JsonElement context = Json("""{"digest": {"items": [{"id": "x.1", "text": "A fact"}], "ids": ["x.1"]}}""");
        var known = new HashSet<string>(["x.1"], StringComparer.Ordinal);
        Assert.True(AnswerCheck.Check("I tied the two Cs.", known, context, Words.English).FellBack);
        Assert.False(AnswerCheck.Check("I tied the two Cs.", known, context, Words.English, drafted: true).FellBack);
        Assert.True(AnswerCheck.Check("I tied them [[made.up]].", known, context, Words.English, drafted: true).FellBack);

        AssistantPiece piece = Piece();
        ToolCall call = new("c1", "notes.join", Join("1.R.2"));
        TurnRequest request = AssistantSession.Start(AssistantRole.Proposer).Ask("Tie the two Cs.").Called([call]).Resulted(TurnRunner.Run(piece, AssistantRole.Proposer, [call], 0)).Request("en", context);
        Assert.True(request.Drafted);
        Assert.False(AssistantSession.Start(AssistantRole.Proposer).Ask("Why?").Request("en", context).Drafted);
    }

    [Fact]
    public void The_proposer_suggests_a_practice_loop_in_range_that_only_the_player_takes()
    {
        AssistantPiece piece = Open(FurElise(), words: Words.Russian);
        static System.Text.Json.JsonElement Loop(int from, int to, int tempo) => Json($$"""{"bars": {"from": {{from}}, "to": {{to}}}, "hand": "right", "tempo": {{tempo}}, "loop": true}""");
        Assert.True(AssistantRoles.Allows(AssistantRole.Proposer, "practice.suggest"));
        Assert.False(AssistantRoles.Allows(AssistantRole.Mentor, "practice.suggest"));
        Assert.Contains("\"tempo\" must be a whole number from 25 to 150", AssistantRoles.CallRefusal(AssistantRole.Proposer, new ToolCall("c0", "practice.suggest", Loop(5, 8, 151)), Words.English), StringComparison.Ordinal);

        ToolCall call = new("c1", "practice.suggest", Loop(32, 35, 60));
        AssistantSession session = AssistantSession.Start(AssistantRole.Proposer).Ask("Как учить такты 32–35?").Called([call]);
        IReadOnlyList<ToolOutcome> results = TurnRunner.Run(piece, AssistantRole.Proposer, [call, new ToolCall("c2", "practice.suggest", Loop(100, 120, 60)), new ToolCall("c3", "practice.suggest", Loop(1, 40, 60))], 0);

        // A proposal: an action and its button's words; no draft, nothing applied, and the session keeps no draft of it.
        System.Text.Json.JsonElement offered = results[0].Content;
        Assert.Equal("suggestAction", offered.GetProperty("kind").GetString());
        Assert.False(offered.TryGetProperty("drafts", out _));
        Assert.Equal("Зациклить такты 32–35 в темпе 60 % · правая рука", offered.GetProperty("result").GetProperty("label").GetString());
        Assert.Equal(60, offered.GetProperty("result").GetProperty("action").GetProperty("tempo").GetInt32());
        Assert.Empty(session.Resulted([results[0]]).Drafts);
        Assert.False(session.Resulted([results[0]]).Request("ru", AssistantContext.OfPiece(piece)).Drafted);

        // Bars the piece has not, or more than a range holds, are refused with the reason.
        Assert.Equal("В пьесе такты с 0 по 105.", results[1].Content.GetProperty("refusal").GetString());
        Assert.Equal("Диапазон — не больше 32 тактов.", results[2].Content.GetProperty("refusal").GetString());
    }
}