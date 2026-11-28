using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MotiffLab.Api.Tests;

/// <summary>
/// The player's playlists online (version 1.7, p-3): one document, the playlists file kept as sent, saved over the copy
/// last seen only (412 otherwise, so the device merges and sends again), checked by its envelope and size only, on every
/// plan, and erased with everything else.
/// </summary>
public sealed class PlaylistsApiTests : IClassFixture<ProjectApiTests.Api>, IClassFixture<TestResetTests.DevelopApi>
{
    private readonly ProjectApiTests.Api _api;
    private readonly TestResetTests.DevelopApi _develop;

    public PlaylistsApiTests(ProjectApiTests.Api api, TestResetTests.DevelopApi develop)
    {
        _api = api;
        _develop = develop;
    }

    [Fact]
    public async Task A_free_player_with_none_online_gets_none_then_keeps_them_and_gets_the_same_bytes_with_the_etag_and_time()
    {
        HttpClient client = Client(_api, ProjectApiTests.Player(), roles: []);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/playlists")).StatusCode);

        byte[] file = Playlists("Бах");
        HttpResponseMessage saved = await client.PutAsync("/playlists", ProjectApiTests.Json(file));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.NotNull(saved.Headers.ETag);

        HttpResponseMessage read = await client.GetAsync("/playlists");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(file, await read.Content.ReadAsByteArrayAsync());
        Assert.Equal(saved.Headers.ETag, read.Headers.ETag);
        // When they were saved, for the merge's "copy saved last" (to the second, as HTTP dates go).
        DateTimeOffset when = Assert.NotNull(read.Content.Headers.LastModified);
        Assert.InRange(DateTimeOffset.UtcNow - when, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        // They are no project, and another player sees none.
        Assert.Equal("[]", await client.GetStringAsync("/projects"));
        Assert.Equal(HttpStatusCode.NotFound, (await Client(_api, ProjectApiTests.Player()).GetAsync("/playlists")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/playlists")).StatusCode);
    }

    [Fact]
    public async Task A_save_from_a_stale_copy_or_a_first_save_over_one_kept_is_a_conflict_and_the_copy_kept_stays()
    {
        HttpClient client = Client(_api, ProjectApiTests.Player());
        EntityTagHeaderValue first = (await client.PutAsync("/playlists", ProjectApiTests.Json(Playlists("Бах")))).Headers.ETag!;
        HttpResponseMessage second = await client.SendAsync(Save(Playlists("Шопен"), first));
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        // Another device that read the first copy, and one that never read any, are both refused.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(Save(Playlists("Лист"), first))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutAsync("/playlists", ProjectApiTests.Json(Playlists("Лист")))).StatusCode);

        HttpResponseMessage read = await client.GetAsync("/playlists");
        Assert.Equal(Playlists("Шопен"), await read.Content.ReadAsByteArrayAsync());
        Assert.Equal(second.Headers.ETag, read.Headers.ETag);
    }

    [Fact]
    public async Task If_match_any_replaces_a_copy_kept_and_never_creates_one()
    {
        HttpClient client = Client(_api, ProjectApiTests.Player());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(Save(Playlists("Бах"), EntityTagHeaderValue.Any))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/playlists")).StatusCode);

        await client.PutAsync("/playlists", ProjectApiTests.Json(Playlists("Бах")));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Save(Playlists("Шопен"), EntityTagHeaderValue.Any))).StatusCode);
        Assert.Equal(Playlists("Шопен"), await client.GetByteArrayAsync("/playlists"));
    }

    [Fact]
    public async Task Only_the_envelope_is_checked_so_a_later_version_is_kept_byte_for_byte()
    {
        // 0026 S2: the API can be older than the apps; even a version this build may not read is kept as sent.
        HttpClient client = Client(_api, ProjectApiTests.Player());
        byte[] later = Encoding.UTF8.GetBytes("""{"format":"motiff-lab-playlists","version":9,"minReader":4,"shelves":[{"x":1}]}""");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/playlists", ProjectApiTests.Json(later))).StatusCode);
        Assert.Equal(later, await client.GetByteArrayAsync("/playlists"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"format":"motiff-lab-project","version":19,"minReader":18,"playlists":[]}""")]
    [InlineData("""{"format":"motiff-lab-playlists","version":"1","minReader":1,"playlists":[]}""")]
    [InlineData("""{"format":"motiff-lab-playlists","version":1,"playlists":[]}""")]
    public async Task What_is_not_a_playlists_file_is_refused_and_nothing_is_kept(string text)
    {
        HttpClient client = Client(_api, ProjectApiTests.Player());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/playlists", ProjectApiTests.Json(Encoding.UTF8.GetBytes(text)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/playlists")).StatusCode);
    }

    [Fact]
    public async Task Playlists_over_a_megabyte_are_refused()
    {
        HttpClient client = Client(_api, ProjectApiTests.Player());
        string pieces = string.Join(",", Enumerable.Range(0, 30_000).Select(index => $"\"p_{index:D32}\""));
        byte[] large = Encoding.UTF8.GetBytes($$"""{"format":"motiff-lab-playlists","version":1,"minReader":1,"playlists":[{"id":"list_1","name":"Все","pieces":[{{pieces}}]}]}""");
        Assert.True(large.Length > PlaylistsEndpoints.MaxFileBytes);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PutAsync("/playlists", ProjectApiTests.Json(large))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/playlists")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_player_erases_their_playlists_with_their_projects_and_no_one_elses()
    {
        HttpClient leaving = Client(_api, ProjectApiTests.Player());
        HttpClient staying = Client(_api, ProjectApiTests.Player());
        await leaving.PutAsync("/projects/p_a", ProjectApiTests.Json(ProjectApiTests.File("p_a")));
        await leaving.PutAsync("/playlists", ProjectApiTests.Json(Playlists("Бах")));
        await staying.PutAsync("/playlists", ProjectApiTests.Json(Playlists("Шопен")));

        HttpResponseMessage deleted = await leaving.DeleteAsync("/me");
        Assert.Contains("\"deletedProjects\":1", await deleted.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await leaving.GetAsync("/playlists")).StatusCode);
        Assert.Equal(Playlists("Шопен"), await staying.GetByteArrayAsync("/playlists"));
        // The account may save again, as a first save.
        Assert.Equal(HttpStatusCode.NoContent, (await leaving.PutAsync("/playlists", ProjectApiTests.Json(Playlists("Бах")))).StatusCode);
    }

    [Fact]
    public async Task A_test_reset_erases_the_playlists_and_a_device_that_has_not_cleared_sends_none_from_before_it()
    {
        HttpClient client = Client(_develop, ProjectApiTests.Player());
        await client.PutAsync("/playlists", ProjectApiTests.Json(Playlists("Бах")));
        HttpResponseMessage reset = await client.PostAsync("/me/reset", null);
        string stamp = Assert.Single(reset.Headers.GetValues(TestReset.Header));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/playlists")).StatusCode);
        using var stale = new HttpRequestMessage(HttpMethod.Put, "/playlists") { Content = ProjectApiTests.Json(Playlists("Бах")) };
        stale.Headers.Add(TestReset.SeenHeader, "0");
        HttpResponseMessage refused = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("\"code\":\"reset\"", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var cleared = new HttpRequestMessage(HttpMethod.Put, "/playlists") { Content = ProjectApiTests.Json(Playlists("Шопен")) };
        cleared.Headers.Add(TestReset.SeenHeader, stamp);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(cleared)).StatusCode);
    }

    /// <summary>A playlists file of version 1 with one playlist of this name, as music-core writes it.</summary>
    private static byte[] Playlists(string name) => Encoding.UTF8.GetBytes(
        $$"""{"format":"motiff-lab-playlists","version":1,"minReader":1,"playlists":[{"id":"list_1","name":"{{name}}","pieces":["p_a"]}]}""");

    /// <summary>A save over the copy online that still has <paramref name="etag"/>.</summary>
    private static HttpRequestMessage Save(byte[] file, EntityTagHeaderValue etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/playlists") { Content = ProjectApiTests.Json(file) };
        request.Headers.IfMatch.Add(etag);
        return request;
    }

    private static HttpClient Client(ProjectApiTests.Api api, string player, string[]? roles = null)
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProjectApiTests.Api.Token(player, "Projects.ReadWrite", roles ?? ["Pro"]));
        return client;
    }
}