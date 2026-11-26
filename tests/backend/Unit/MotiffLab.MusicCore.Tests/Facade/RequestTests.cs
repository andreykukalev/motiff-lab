using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The time of a call (audit D15): <c>Request.Now</c> and <c>Request.SentNow</c> are where the facade reads it, the adapters
/// hand it to the sessions and the rules; sync.online reads its own (<c>SyncRequest.Time</c>).
/// </summary>
public class RequestTests
{
    [Fact]
    public void The_time_of_a_call_is_its_now_else_the_clock_and_a_save_keeps_it_only_when_sent()
    {
        using Request sent = Request.Parse("""{"now": "2026-11-26T12:30:00+03:00"}""");
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 12, 30, 0, TimeSpan.FromHours(3)), sent.Now());
        Assert.Equal(sent.Now(), sent.SentNow());

        using Request wrong = Request.Parse("""{"now": "yesterday"}""");
        Assert.Equal("\"now\" must be an ISO 8601 time.", Assert.Throws<FacadeException>(() => wrong.Now()).Message);
        Assert.Throws<FacadeException>(() => wrong.SentNow());

        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset clock = Request.Empty.Now();
        Assert.InRange(clock, before, DateTimeOffset.UtcNow);
        Assert.Null(Request.Empty.SentNow());
    }
}