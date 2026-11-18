using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>
/// The full assistant's conversation as the device keeps it (#582 item 17.1, the lead's decision 4): the record reads back
/// whole, so the conversation goes on from where it was left, and is named by its first question.
/// </summary>
public class ConversationRecordTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void A_kept_conversation_reads_back_whole_and_goes_on_from_its_session()
    {
        var place = new FactPlace("Bar 3", TickSpan.FromBounds(new Tick(2880), new Tick(4320)), ["n1", "n2"]);
        AssistantSession session = AssistantSession.Start(AssistantRole.Proposer).Ask("What happens in bar 3?").Answered("It cadences [[cad.1]].");
        ConversationRecord record = ConversationRecord.Start("c_1", AssistantRole.Proposer, At)
            .With(new SavedExchange("What happens in bar 3?", "It cadences [[cad.1]].", false, null, [new CitedFact("cad.1", "A cadence in C", [place])]), session, At.AddMinutes(1))
            .With(new SavedExchange("And then?", "The app's facts.", true, null, []), session.Ask("And then?").Answered("The app's facts."), At.AddMinutes(2));

        ConversationRecord read = ConversationRecord.Read(record.ToJson())!;

        Assert.Equal(("c_1", "What happens in bar 3?", At, At.AddMinutes(2)), (read.Id, read.Title, read.Started, read.Updated));
        Assert.Equal(AssistantRole.Proposer, read.Session.Role);
        Assert.Equal(record.ToJson(), read.ToJson());
        Assert.Equal(record.Session.History.Count, read.Session.History.Count);
        Assert.Equal(2, read.Exchanges.Count);
        SavedExchange first = read.Exchanges[0];
        Assert.Equal(("What happens in bar 3?", "It cadences [[cad.1]].", false, (string?)null), (first.Question, first.Answer, first.Fallback, first.Note));
        CitedFact cited = Assert.Single(first.Cited);
        Assert.Equal(("cad.1", "A cadence in C"), (cited.Id, cited.Text));
        FactPlace readPlace = Assert.Single(cited.Places);
        Assert.Equal((place.Label, place.Span), (readPlace.Label, readPlace.Span));
        Assert.Equal(place.NoteIds, readPlace.NoteIds);
        Assert.True(read.Exchanges[1].Fallback);
    }

    [Fact]
    public void A_record_of_another_version_or_not_a_record_is_not_read()
    {
        string json = ConversationRecord.Start("c_1", AssistantRole.Mentor, At).ToJson();

        Assert.NotNull(ConversationRecord.Read(json));
        Assert.Null(ConversationRecord.Read(json.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)));
        Assert.Null(ConversationRecord.Read("{\"version\":1}"));
        Assert.Null(ConversationRecord.Read("not json"));
        Assert.Null(ConversationRecord.Read(null));
    }

    [Fact]
    public void The_first_question_names_the_conversation_cut_at_a_word()
    {
        Assert.Equal("Why does it end in G?", ConversationRecord.TitleOf("  Why does\nit end   in G? "));

        string title = ConversationRecord.TitleOf(string.Join(' ', Enumerable.Repeat("chord", 20)));
        Assert.True(title.Length <= ConversationRecord.MaxTitle);
        Assert.EndsWith("chord…", title, StringComparison.Ordinal);

        ConversationRecord record = ConversationRecord.Start("c_1", AssistantRole.Mentor, At);
        AssistantSession session = record.Session.Ask("First?").Answered("Yes.");
        ConversationRecord named = record.With(new SavedExchange("First?", "Yes.", false, null, []), session, At)
            .With(new SavedExchange("Second?", "No.", false, null, []), session.Ask("Second?").Answered("No."), At);
        Assert.Equal("First?", named.Title);
    }
}