using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>The conversation's format, its limits and the check of answers (0028 section 4; 0003 section 5).</summary>
public class ConversationTests
{
    private static readonly string s_full = Note("G", 4, 6);

    private static AssistantPiece Piece() => Open(Score(Note("E", 5, 2) + Note("D", 5, 2) + Note("C", 5, 2), s_full));

    private static ToolCall Call(string id, string name, string arguments) => new(id, name, Json(arguments));

    private static TurnRequest RoundTrip(TurnRequest request)
    {
        TurnRequest? read = TurnRequest.Read(Json(Encoding.UTF8.GetString(request.ToJson())), out string? refusal);
        Assert.Null(refusal);
        return read!;
    }

    private static string? Refusal(string json)
    {
        TurnRequest.Read(Json(json), out string? refusal);
        return refusal;
    }

    [Fact]
    public void The_mentor_may_only_read()
    {
        Assert.Equal(["piece.describe", "facts.get", "notes.list", "harmony.chords", "check.flags"], AssistantRoles.ToolsOf(AssistantRole.Mentor).Select(tool => tool.Name));
        Assert.False(AssistantRoles.Allows(AssistantRole.Mentor, "notes.edit"));
        Assert.Equal(AssistantRole.Mentor, AssistantRoles.Parse("mentor"));
        Assert.Null(AssistantRoles.Parse("composer"));
    }

    [Fact]
    public void A_question_goes_through_tool_calls_run_on_the_device_to_a_checked_answer()
    {
        AssistantPiece piece = Piece();
        JsonElement context = AssistantContext.OfRange(piece, 1, 1);
        AssistantSession session = AssistantSession.Start(AssistantRole.Mentor).Ask("What happens in bar 1?");

        TurnRequest first = RoundTrip(session.Request("en", context));
        Assert.Equal("What happens in bar 1?", Assert.Single(first.History).Text);
        Assert.Equal(3, context.GetProperty("notes").GetProperty("notes").GetArrayLength());

        // The server answers with a call; the device runs it and the next turn carries its result.
        TurnReply reply = TurnReply.Read(Json(Written(TurnReply.Called([Call("c1", "harmony.chords", """{"bars": {"from": 1, "to": 2}}""")]).Write)))!;
        session = session.Called(reply.Calls);
        Assert.True(session.AwaitsResults);
        IReadOnlyList<ToolOutcome> results = TurnRunner.Run(piece, session.Role, reply.Calls, session.ToolCallsInQuestion - reply.Calls.Count);
        Assert.True(results[0].Content.GetProperty("ok").GetBoolean());
        session = session.Resulted(results);

        TurnRequest second = RoundTrip(session.Request("en", context));
        Assert.Equal(1, second.ToolCallsInQuestion);
        string known = context.GetProperty("digest").GetProperty("ids")[0].GetString()!;
        Assert.Contains(known, second.KnownIds);

        CheckedAnswer answer = AnswerCheck.Check($"Bar 1 steps down [[{known}]].", second.KnownIds, context, Words.English);
        Assert.False(answer.FellBack);
        Assert.Equal(["Bar 1 steps down ", $"[[{known}]]", "."], answer.Parts.Select(part => part.Text));
        Assert.Equal(known, answer.Parts[1].Cites);
        session = session.Answered(answer.Text);
        Assert.False(session.Waiting);
        Assert.Equal([TurnEntryKind.Question, TurnEntryKind.Answer], session.Exchanges.Select(entry => entry.Kind));
    }

    [Fact]
    public void An_answer_that_cites_nothing_or_a_fact_it_was_not_given_becomes_the_engines_template()
    {
        JsonElement context = AssistantContext.OfRange(Piece(), 1, 2);
        IReadOnlySet<string> known = AnswerCheck.Known(context, []);

        CheckedAnswer invented = AnswerCheck.Check("It modulates to F [[key.modulation]].", known, context, Words.Russian);
        CheckedAnswer bare = AnswerCheck.Check("It is lovely.", known, context, Words.English);
        CheckedAnswer longer = AnswerCheck.Check(new string('a', AnswerCheck.MaxLength + 1) + $"[[{known.First()}]]", known, context, Words.English);

        Assert.True(invented.FellBack);
        Assert.Equal("unknownFact", invented.Reason);
        Assert.StartsWith("Ответ помощника не удалось сверить", invented.Text, StringComparison.Ordinal);
        Assert.All(AnswerCheck.Cited(invented.Text), id => Assert.Contains(id, known));
        Assert.NotEmpty(AnswerCheck.Cited(invented.Text));
        Assert.Equal("noCitation", bare.Reason);
        Assert.Equal("tooLong", longer.Reason);
    }

    [Fact]
    public void A_turn_out_of_order_beyond_the_cap_or_with_a_tool_the_role_lacks_is_refused()
    {
        string context = """{"digest": {"ids": ["a"]}}""";
        string question = """{"kind": "question", "text": "Why?"}""";
        string Calls(int count, string tool = "check.flags") => $$"""{"kind": "toolCalls", "calls": [{{string.Join(",", Enumerable.Range(1, count).Select(i => "{\"id\": \"c" + i + "\", \"name\": \"" + tool + "\", \"arguments\": {}}"))}}]}""";
        string Results(int count) => $$"""{"kind": "toolResults", "results": [{{string.Join(",", Enumerable.Range(1, count).Select(i => "{\"id\": \"c" + i + "\", \"name\": \"check.flags\", \"content\": {}}"))}}]}""";
        string Turn(params string[] history) => $$"""{"version": 1, "role": "mentor", "language": "en", "context": {{context}}, "history": [{{string.Join(",", history)}}]}""";

        Assert.Null(Refusal(Turn(question, Calls(2), Results(2))));
        Assert.Contains("at most 8", Refusal(Turn(question, Calls(9), Results(9))), StringComparison.Ordinal);
        Assert.Contains("no tool \"notes.edit\"", Refusal(Turn(question, Calls(1, "notes.edit"), Results(1))), StringComparison.Ordinal);
        Assert.Contains("out of order", Refusal(Turn(question, Results(1))), StringComparison.Ordinal);
        Assert.Contains("ends on a question", Refusal(Turn(question, Calls(1))), StringComparison.Ordinal);
        Assert.Contains("one result with its id", Refusal(Turn(question, Calls(2), Results(1))), StringComparison.Ordinal);
        Assert.Contains("1 to 1000", Refusal(Turn($$"""{"kind": "question", "text": "{{new string('x', 1001)}}"}""")), StringComparison.Ordinal);
        Assert.Contains("version 1", Refusal(Turn(question).Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("digest", Refusal(Turn(question).Replace(context, "{}", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("en or ru", Refusal(Turn(question).Replace("\"en\"", "\"lt\"", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_device_runs_no_more_calls_than_the_cap_and_only_the_roles_tools()
    {
        AssistantPiece piece = Piece();
        ToolCall[] calls = [Call("a", "check.flags", """{"bars": {"from": 1, "to": 1}}"""), Call("b", "notes.edit", "{}"), Call("c", "check.flags", """{"bars": {"from": 1, "to": 1}}""")];

        IReadOnlyList<ToolOutcome> results = TurnRunner.Run(piece, AssistantRole.Mentor, calls, AssistantRoles.MostToolCalls - 2);

        Assert.Equal(["a", "b", "c"], results.Select(result => result.Id));
        Assert.True(results[0].Content.GetProperty("ok").GetBoolean());
        Assert.Contains("no tool", results[1].Content.GetProperty("refusal").GetString(), StringComparison.Ordinal);
        Assert.Contains("at most 8", results[2].Content.GetProperty("refusal").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_is_kept_by_the_device_and_comes_back_whole()
    {
        AssistantPiece piece = Piece();
        AssistantSession session = AssistantSession.Start(AssistantRole.Mentor).Ask("One?");
        Assert.Throws<InvalidOperationException>(() => session.Ask("Two?"));
        Assert.Empty(session.Withdrawn().History);

        ToolCall call = Call("c1", "notes.edit", """{"bars": {"from": 1, "to": 1}, "edits": [{"kind": "remove", "address": "1.R.2", "bar": null, "hand": null, "start": null, "length": null, "pitch": null, "velocity": null}]}""");
        session = session.Called([call]);
        session = session.Resulted([.. TurnRunner.Run(piece, AssistantRole.Mentor, [call], 0).Select(outcome => outcome with { Content = Answer(piece, call.Name, call.Arguments.GetRawText()) })]);
        session = session.Answered("Done [[a]].").Decided("d1", DraftState.Accepted);

        SessionDraft draft = Assert.Single(session.Drafts);
        Assert.Equal(("d1", "notes.edit", 0, DraftState.Accepted), (draft.Id, draft.Tool, draft.Index, draft.State));
        AssistantSession read = AssistantSession.Read(Json(Written(session.Write)))!;
        Assert.Equal(Written(session.Write), Written(read.Write));
        Assert.Null(AssistantSession.Read(Json("""{"role": "mentor"}""")));
    }

    [Fact]
    public void A_long_conversation_sends_its_latest_exchanges_only()
    {
        AssistantSession session = AssistantSession.Start(AssistantRole.Mentor);
        for (int index = 0; index < TurnRequest.MaxEntries; index++)
        {
            session = session.Ask($"Question {index}?").Answered("[[a]]");
        }

        session = session.Ask("Last?");
        TurnRequest request = session.Request("ru", Json("""{"digest": {"ids": ["a"]}}"""));

        Assert.True(request.History.Count <= TurnRequest.MaxEntries);
        Assert.Equal("Last?", request.History[^1].Text);
        Assert.Equal(TurnEntryKind.Question, request.History[0].Kind);
        Assert.NotNull(TurnRequest.Read(Json(Encoding.UTF8.GetString(request.ToJson())), out _));
    }

    [Fact]
    public void Draft_diff_says_what_edits_would_change_without_a_draft()
    {
        AssistantPiece piece = Piece();

        JsonElement answer = Answer(piece, "draft.diff", """
            {"bars": {"from": 1, "to": 1},
             "edits": [{"kind": "change", "address": "1.R.1", "bar": null, "hand": null, "start": null, "length": null, "pitch": 77, "velocity": null}]}
            """);

        Assert.True(answer.GetProperty("ok").GetBoolean());
        Assert.False(answer.TryGetProperty("drafts", out _));
        JsonElement result = answer.GetProperty("result");
        Assert.Equal("draft.diff", result.GetProperty("tool").GetString());
        JsonElement edit = Assert.Single(result.GetProperty("edits").EnumerateArray());
        Assert.Equal(("1.R.1", "E5", "F5"), (edit.GetProperty("address").GetString(), edit.GetProperty("before").GetProperty("pitch").GetString(), edit.GetProperty("after").GetProperty("pitch").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("explanation").GetString()));
    }

    [Fact]
    public void A_reply_is_written_and_read_back()
    {
        CheckedAnswer answer = AnswerCheck.Template(Json("""{"digest": {"items": [{"id": "x.1", "text": "A fact"}], "ids": ["x.1"]}}"""), Words.English, "unknownFact");

        TurnReply read = TurnReply.Read(Json(Written(TurnReply.Answered(answer).Write)))!;

        Assert.Equal((TurnReplyKind.Answer, true), (read.Kind, read.Fallback));
        Assert.EndsWith("· A fact [[x.1]]", read.Text, StringComparison.Ordinal);
        Assert.Equal(TurnReplyKind.NotConfigured, TurnReply.Read(Json("""{"kind": "notConfigured"}"""))!.Kind);
        Assert.Null(TurnReply.Read(Json("""{"kind": "toolCalls"}""")));
    }

    [Fact]
    public void A_citation_leads_to_its_notes_or_its_bars_in_the_score()
    {
        AssistantPiece piece = Open(FurElise());
        JsonElement context = AssistantContext.OfRange(piece, 1, 8);
        JsonElement items = context.GetProperty("digest").GetProperty("items");
        JsonElement withNotes = items.EnumerateArray().First(item => item.TryGetProperty("places", out _));
        JsonElement chord = items.EnumerateArray().First(item => item.GetProperty("kind").GetString() == "chord");
        Assert.True(chord.TryGetProperty("bars", out _));

        CitedFact fact = CitedFacts.Of(piece, withNotes.GetProperty("id").GetString()!, context, [])!;
        Assert.Equal(withNotes.GetProperty("text").GetString(), fact.Text);
        Assert.All(fact.Places, place => Assert.NotEmpty(place.NoteIds));
        Assert.All(fact.Places.SelectMany(place => place.NoteIds), id => Assert.Contains(piece.Notes, note => note.Id == id));

        // A tool result's fact counts too; an id the turn never gave is left out.
        JsonElement result = Json("""{"ok": true, "items": [{"id": "cadence.2", "kind": "cadence", "text": "full close in bar 2", "bars": {"from": 2, "to": 2}}]}""");
        IReadOnlyList<AnswerPart> parts = AnswerCheck.Parts("Ends [[cadence.2]], not [[made.up]].");
        CitedFact close = Assert.Single(CitedFacts.In(piece, parts, context, [new ToolOutcome("c1", "facts.get", result)]));
        Assert.Equal(piece.Meter.GetBar(2).Span, Assert.Single(close.Places).Span);
        Assert.Empty(close.Places[0].NoteIds);
    }

    [Fact]
    public void The_scope_follows_the_selection_unless_the_whole_piece_was_chosen_for_it()
    {
        AssistantPiece piece = Open(FurElise());
        TickSpan bars3To4 = TickSpan.FromBounds(piece.Meter.GetBar(3).Span.Onset, piece.Meter.GetBar(4).Span.End);
        TickSpan bar7 = piece.Meter.GetBar(7).Span;

        // The assistant opened with nothing selected: the whole piece. Bars selected afterwards are what it looks at (#582).
        AskScope opened = AskScope.Choose(false, null);
        Assert.True(opened.IsWholePiece(null));
        Assert.False(opened.IsWholePiece(bars3To4));
        Assert.False(AskScope.Choose(true, null).IsWholePiece(bars3To4));

        // The whole piece chosen while bars 3-4 stand holds for them, and gives way to the next selection.
        AskScope whole = AskScope.Choose(true, bars3To4);
        Assert.True(whole.IsWholePiece(bars3To4));
        Assert.False(whole.IsWholePiece(bar7));

        // The context of a question about the selection is its bars, read whole: their digest and their notes.
        JsonElement context = AssistantContext.OfQuestion(piece, TickSpan.FromBounds(new Tick(bars3To4.Onset.Value + 120), new Tick(bars3To4.End.Value - 120)), opened)!.Value;
        JsonElement digest = context.GetProperty("digest");
        Assert.Equal("selection", digest.GetProperty("scope").GetString());
        Assert.Equal((3, 4), (digest.GetProperty("bars").GetProperty("from").GetInt32(), digest.GetProperty("bars").GetProperty("to").GetInt32()));
        string[] addresses = [.. context.GetProperty("notes").GetProperty("notes").EnumerateArray().Select(note => note.GetProperty("address").GetString()!)];
        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.Matches(@"^[34]\.", address));
        Assert.Equal("piece", AssistantContext.OfQuestion(piece, bars3To4, whole)!.Value.GetProperty("digest").GetProperty("scope").GetString());

        // More bars than a range holds give no context, rather than another one.
        TickSpan tooLong = TickSpan.FromBounds(piece.Meter.GetBar(1).Span.Onset, piece.Meter.GetBar(1 + MusicCore.Motifs.MotifSelection.MaxBars).Span.End);
        Assert.Null(AssistantContext.OfQuestion(piece, tooLong, opened));
    }

    private static string Written(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            write(json);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}