using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.Configuration;

namespace MotiffLab.Api.Tests;

/// <summary>«Очистить всё» (TestReset): only on develop's API, and every device of the player hears of it at its next sync.</summary>
public sealed class TestResetTests : IClassFixture<TestResetTests.DevelopApi>, IClassFixture<TestResetTests.MainApi>
{
    private readonly DevelopApi _develop;
    private readonly MainApi _main;

    public TestResetTests(DevelopApi develop, MainApi main)
    {
        _develop = develop;
        _main = main;
    }

    [Theory]
    [InlineData("https://cosmos-motifflab-dev-ab12cd34.documents.azure.com:443/", true)]
    [InlineData("https://COSMOS-MOTIFFLAB-DEV-AB12CD34.documents.azure.com/", true)]
    [InlineData("https://cosmos-motifflab-1a2b3c4d.documents.azure.com:443/", false)]
    [InlineData("https://cosmos-motifflab-dev-ab12cd34.documents.azure.com.evil/", false)]
    [InlineData("https://cosmos-motifflab-dev-ab12cd345.documents.azure.com/", false)]
    [InlineData("http://cosmos-motifflab-dev-ab12cd34.documents.azure.com/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Resets_are_on_only_with_develops_own_database(string? endpoint, bool enabled)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection([new("Cosmos:Endpoint", endpoint)]).Build();
        Assert.Equal(enabled, TestReset.IsEnabled(config));
    }

    [Fact]
    public void A_new_stamp_is_the_servers_time_and_always_later_than_the_last()
    {
        var now = new DateTimeOffset(2026, 11, 13, 14, 19, 0, TimeSpan.Zero);
        Assert.Equal(now.ToUnixTimeMilliseconds(), TestReset.Next(null, now));
        Assert.Equal(now.ToUnixTimeMilliseconds() + 1, TestReset.Next(now.ToUnixTimeMilliseconds(), now));
        Assert.Equal((long?)42, TestReset.Parse("42"));
        Assert.Null(TestReset.Parse("-1"));
        Assert.Null(TestReset.Parse("4.2"));
        Assert.Null(TestReset.Parse(""));
    }

    [Fact]
    public async Task On_the_players_api_there_is_no_reset_and_no_stamp()
    {
        HttpClient client = Client(_main, Player());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/projects/p_main", Json(File("p_main")))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/me/reset", null)).StatusCode);
        HttpResponseMessage list = await client.GetAsync("/projects");
        Assert.False(list.Headers.Contains(TestReset.Header));
        Assert.Single((await list.Content.ReadFromJsonAsync<Row[]>())!);
    }

    [Fact]
    public async Task A_reset_empties_the_players_test_data_and_the_list_tells_every_device()
    {
        string player = Player();
        HttpClient here = Client(_develop, player);
        HttpClient other = Client(_develop, Player());
        await here.PutAsync("/projects/p_a", Json(File("p_a")));
        await here.PutAsync("/projects/p_b", Json(File("p_b")));
        await other.PutAsync("/projects/p_a", Json(File("p_a")));

        // Before any reset the list has no stamp, and a save that says it saw none goes through.
        HttpResponseMessage before = await here.GetAsync("/projects");
        Assert.False(before.Headers.Contains(TestReset.Header));

        HttpResponseMessage reset = await here.PostAsync("/me/reset", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Answer answer = (await reset.Content.ReadFromJsonAsync<Answer>())!;
        Assert.Equal(2, answer.DeletedProjects);
        Assert.Equal(answer.Reset.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(reset.Headers.GetValues(TestReset.Header)));

        // The list stays the same JSON array, empty, with the stamp beside it; the marker is no project.
        HttpResponseMessage list = await here.GetAsync("/projects");
        Assert.Empty((await list.Content.ReadFromJsonAsync<Row[]>())!);
        Assert.Equal(answer.Reset, long.Parse(Assert.Single(list.Headers.GetValues(TestReset.Header)), System.Globalization.CultureInfo.InvariantCulture));

        // Another player keeps everything and hears of no reset.
        HttpResponseMessage theirs = await other.GetAsync("/projects");
        Assert.Single((await theirs.Content.ReadFromJsonAsync<Row[]>())!);
        Assert.False(theirs.Headers.Contains(TestReset.Header));

        // A second reset is later still.
        Answer again = (await (await here.PostAsync("/me/reset", null)).Content.ReadFromJsonAsync<Answer>())!;
        Assert.True(again.Reset > answer.Reset);
    }

    [Fact]
    public async Task A_device_that_has_not_cleared_yet_sends_nothing_from_before_the_reset()
    {
        string player = Player();
        HttpClient client = Client(_develop, player);
        long stamp = (await (await client.PostAsync("/me/reset", null)).Content.ReadFromJsonAsync<Answer>())!.Reset;

        HttpResponseMessage stale = await client.SendAsync(Save("p_old", seen: "0"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("\"code\":\"reset\"", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(stamp.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(stale.Headers.GetValues(TestReset.Header)));
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(Save("p_old", seen: (stamp - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))).StatusCode);

        // Once it applied the stamp it saves again; an app without the header (before «Очистить всё») is never refused.
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Save("p_new", seen: stamp.ToString(System.Globalization.CultureInfo.InvariantCulture)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Save("p_older_app", seen: null))).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<Row[]>("/projects"))!.Length);
    }

    [Fact]
    public async Task Erasing_everything_also_forgets_the_stamp()
    {
        HttpClient client = Client(_develop, Player());
        await client.PostAsync("/me/reset", null);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/me")).StatusCode);
        Assert.False((await client.GetAsync("/projects")).Headers.Contains(TestReset.Header));
    }

    [Fact]
    public async Task Browsers_on_the_test_site_may_reset_and_read_the_stamp()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/me/reset");
        preflight.Headers.Add("Origin", "https://motiff.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        HttpResponseMessage allowed = await _develop.CreateClient().SendAsync(preflight);
        Assert.Contains("POST", allowed.Headers.GetValues("Access-Control-Allow-Methods").SelectMany(value => value.Split(',')).Select(value => value.Trim()));

        HttpClient client = Client(_develop, Player());
        client.DefaultRequestHeaders.Add("Origin", "https://motiff.example");
        HttpResponseMessage list = await client.GetAsync("/projects");
        Assert.Contains(TestReset.Header, list.Headers.GetValues("Access-Control-Expose-Headers").SelectMany(value => value.Split(',')).Select(value => value.Trim()));
    }

    private static HttpRequestMessage Save(string id, string? seen)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/projects/{id}") { Content = Json(File(id)) };
        if (seen is not null)
        {
            request.Headers.Add(TestReset.SeenHeader, seen);
        }

        return request;
    }

    private static HttpClient Client(ProjectApiTests.Api api, string player)
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProjectApiTests.Api.Token(player, "Projects.ReadWrite", ["Pro"]));
        return client;
    }

    private static string Player() => ProjectApiTests.Player();

    private static byte[] File(string id) => ProjectApiTests.File(id);

    private static ByteArrayContent Json(byte[] body) => ProjectApiTests.Json(body);

    private sealed record Answer(int DeletedProjects, long Reset);

    private sealed record Row(string Id, string FileName);

    /// <summary>The API as develop deploys it: its database is cosmos-motifflab-dev-….</summary>
    public sealed class DevelopApi : ProjectApiTests.Api
    {
        protected override string? CosmosEndpoint => "https://cosmos-motifflab-dev-ab12cd34.documents.azure.com:443/";
    }

    /// <summary>The players' API: its database is main's.</summary>
    public sealed class MainApi : ProjectApiTests.Api
    {
        protected override string? CosmosEndpoint => "https://cosmos-motifflab-1a2b3c4d.documents.azure.com:443/";
    }
}