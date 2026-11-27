using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using MotiffLab.MusicCore.Accounts;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Api.Tests;

public sealed class ProjectApiTests : IClassFixture<ProjectApiTests.Api>
{
    private static readonly DateTimeOffset s_created = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);
    private readonly Api _api;

    public ProjectApiTests(Api api) => _api = api;

    [Fact]
    public async Task Without_signing_in_nothing_is_shown()
    {
        HttpResponseMessage response = await _api.CreateClient().GetAsync("/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_without_the_scope_is_refused()
    {
        HttpResponseMessage response = await Client(Player(), scope: "openid").GetAsync("/projects");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Saving_online_is_free_up_to_the_quota_and_a_piece_beyond_it_is_refused()
    {
        // 0025 section 8 (Andrei, 2026-11-11): free sync up to 300 pieces; those online keep syncing, a new one stays on the device.
        string player = Player();
        HttpClient free = Client(player, roles: []);
        HttpResponseMessage first = await free.PutAsync("/projects/p_kept", Json(File("p_kept")));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        for (int index = 1; index < Plans.FreeProjectsOnline; index++)
        {
            await _api.Services.GetRequiredService<IProjectStore>().PutAsync(
                player, Project.Create($"p_{index:D4}", "a.musicxml", Encoding.UTF8.GetBytes("<score-partwise/>"), s_created).Summary, "", null, default);
        }

        HttpResponseMessage refused = await free.PutAsync("/projects/p_new", Json(File("p_new")));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("\"code\":\"quota\"", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await free.SendAsync(Update("p_kept", File("p_kept"), first.Headers.ETag!))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client(player).PutAsync("/projects/p_new", Json(File("p_new")))).StatusCode);

        Assert.Equal(Plans.FreeProjectsOnline + 1, (await free.GetFromJsonAsync<Summary[]>("/projects"))!.Length);
        Assert.Equal(HttpStatusCode.OK, (await free.GetAsync("/projects/p_kept")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await free.DeleteAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task The_account_tells_the_app_what_its_plan_allows()
    {
        Account pro = (await Client(Player()).GetFromJsonAsync<Account>("/account"))!;
        Assert.Equal(("pro", false, null), (pro.Plan, pro.Tester, pro.Chosen));
        Assert.True(pro.Features["scan"] && pro.Features["transcribe"] && pro.Features["saveOnline"] && pro.Features["assistant"]);

        Account testing = (await Client(Player(), roles: ["Tester", "Pro"], choice: "free").GetFromJsonAsync<Account>("/account"))!;
        Assert.Equal(("free", true, "free"), (testing.Plan, testing.Tester, testing.Chosen));
        Assert.Equal(["saveOnline"], testing.Features.Where(feature => feature.Value).Select(feature => feature.Key));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/account")).StatusCode);
    }

    [Fact]
    public async Task A_saved_project_comes_back_as_the_same_bytes_and_is_listed()
    {
        string player = Player();
        HttpClient client = Client(player);
        byte[] file = ProjectFile.Write(Project.Create("p_round", "minuet.musicxml", Encoding.UTF8.GetBytes("<score-partwise/>"), s_created));

        HttpResponseMessage saved = await client.PutAsync("/projects/p_round", Json(file));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.NotNull(saved.Headers.ETag);

        HttpResponseMessage read = await client.GetAsync("/projects/p_round");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(file, await read.Content.ReadAsByteArrayAsync());
        Assert.Equal(saved.Headers.ETag, read.Headers.ETag);

        Summary[] list = (await client.GetFromJsonAsync<Summary[]>("/projects"))!;
        Summary row = Assert.Single(list);
        Assert.Equal(("p_round", "minuet.musicxml"), (row.Id, row.FileName));
    }

    [Fact]
    public async Task One_players_projects_are_invisible_to_another()
    {
        string owner = Player();
        await Client(owner).PutAsync("/projects/p_mine", Json(File("p_mine")));

        HttpClient other = Client(Player());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/projects/p_mine")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<Summary[]>("/projects"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync("/projects/p_mine")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(owner).GetAsync("/projects/p_mine")).StatusCode);
    }

    [Fact]
    public async Task A_file_that_is_not_this_project_is_refused()
    {
        HttpClient client = Client(Player());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/projects/p_one", Json(File("p_two")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/projects/p_one", Json("{}"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/projects/p.one", Json(File("p.one")))).StatusCode);
    }

    [Fact]
    public async Task Only_the_envelope_is_checked_so_a_later_version_and_an_unknown_value_are_kept_byte_for_byte()
    {
        // 0026 S2: the API can be older than the apps, so it never reads the musical content.
        HttpClient client = Client(Player());
        JsonObject later = JsonNode.Parse(File("p_later"))!.AsObject();
        later["version"] = ProjectFile.Version + 1;
        later["workspace"]!["motifs"] = new JsonArray(new JsonObject { ["id"] = "m_1", ["kind"] = "FutureValue" }, new JsonObject { ["id"] = "m_2" });
        later["future"] = "kept";
        byte[] sent = Encoding.UTF8.GetBytes(later.ToJsonString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/projects/p_later", Json(sent))).StatusCode);

        Assert.Equal(sent, await client.GetByteArrayAsync("/projects/p_later"));
        Assert.Equal(2, Assert.Single((await client.GetFromJsonAsync<Summary[]>("/projects"))!).MotifCount);
        JsonObject damaged = JsonNode.Parse(File("p_later"))!.AsObject();
        damaged["lastOpened"] = "yesterday";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/projects/p_later", Json(Encoding.UTF8.GetBytes(damaged.ToJsonString())))).StatusCode);
        damaged["lastOpened"] = later["lastOpened"]!.DeepClone();
        damaged["version"] = 0;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/projects/p_later", Json(Encoding.UTF8.GetBytes(damaged.ToJsonString())))).StatusCode);
    }

    [Fact]
    public async Task A_save_from_a_stale_copy_is_refused()
    {
        HttpClient client = Client(Player());
        EntityTagHeaderValue first = (await client.PutAsync("/projects/p_two_devices", Json(File("p_two_devices")))).Headers.ETag!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Update("p_two_devices", File("p_two_devices"), first))).StatusCode);

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(Update("p_two_devices", File("p_two_devices"), first))).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_first_upload_only_creates_and_never_writes_over_a_copy_online(bool ifNoneMatch)
    {
        // #321, 0026 S4: a save without If-Match is a first upload; with that id online already, it is a conflict (412), not a success.
        HttpClient client = Client(Player());
        byte[] online = ProjectFile.Write(Project.Create("p_first", "online.musicxml", Encoding.UTF8.GetBytes("<score-partwise/>"), s_created));
        HttpResponseMessage created = await client.PutAsync("/projects/p_first", Json(online));
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        using var again = new HttpRequestMessage(HttpMethod.Put, "/projects/p_first") { Content = Json(File("p_first")) };
        if (ifNoneMatch)
        {
            again.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        }

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(again)).StatusCode);
        HttpResponseMessage read = await client.GetAsync("/projects/p_first");
        Assert.Equal(online, await read.Content.ReadAsByteArrayAsync());
        Assert.Equal(created.Headers.ETag, read.Headers.ETag);
    }

    [Fact]
    public async Task An_update_of_a_copy_deleted_online_since_is_a_conflict_and_a_first_upload_creates_it_again()
    {
        HttpClient client = Client(Player());
        EntityTagHeaderValue first = (await client.PutAsync("/projects/p_gone", Json(File("p_gone")))).Headers.ETag!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/projects/p_gone")).StatusCode);

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(Update("p_gone", File("p_gone"), first))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/projects/p_gone", Json(File("p_gone")))).StatusCode);
    }

    [Fact]
    public async Task Deleting_one_project_removes_only_it_and_with_if_match_only_the_copy_last_seen()
    {
        // The 1.6 plan's decision 14: removing a piece on the iPad removes its copy online.
        HttpClient client = Client(Player());
        EntityTagHeaderValue seen = (await client.PutAsync("/projects/p_removed", Json(File("p_removed")))).Headers.ETag!;
        await client.PutAsync("/projects/p_stays", Json(File("p_stays")));
        EntityTagHeaderValue changed = (await client.SendAsync(Update("p_removed", File("p_removed"), seen))).Headers.ETag!;

        using (var stale = new HttpRequestMessage(HttpMethod.Delete, "/projects/p_removed"))
        {
            stale.Headers.IfMatch.Add(seen);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/projects/p_removed")).StatusCode);
        using (var current = new HttpRequestMessage(HttpMethod.Delete, "/projects/p_removed"))
        {
            current.Headers.IfMatch.Add(changed);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(current)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/projects/p_removed")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/projects/p_removed")).StatusCode);
        Assert.Equal("p_stays", Assert.Single((await client.GetFromJsonAsync<Summary[]>("/projects"))!).Id);
    }

    [Fact]
    public async Task A_project_too_large_for_one_document_stays_on_the_device()
    {
        // Random bytes do not compress, so 2 MB of source packs to more than a document holds.
        byte[] noise = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        byte[] file = ProjectFile.Write(Project.Create("p_large", "big.mid", noise, s_created));

        HttpResponseMessage response = await Client(Player()).PutAsync("/projects/p_large", Json(file));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_player_with_nothing_online_answers_none_and_then_a_first_upload_creates_again()
    {
        // App Store 5.1.1(v): «Удалить аккаунт и данные онлайн» works whatever is online, and the account may save again.
        HttpClient client = Client(Player());
        HttpResponseMessage nothing = await client.DeleteAsync("/me");
        Assert.Equal(HttpStatusCode.OK, nothing.StatusCode);
        Assert.Contains("\"deletedProjects\":0", await nothing.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await client.PutAsync("/projects/p_again", Json(File("p_again")));
        Assert.Contains("\"deletedProjects\":1", await (await client.DeleteAsync("/me")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/projects/p_again", Json(File("p_again")))).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_player_removes_all_their_projects_and_no_one_elses()
    {
        string leaving = Player();
        string staying = Player();
        await Client(leaving).PutAsync("/projects/p_a", Json(File("p_a")));
        await Client(leaving).PutAsync("/projects/p_b", Json(File("p_b")));
        await Client(staying).PutAsync("/projects/p_a", Json(File("p_a")));

        HttpResponseMessage response = await Client(leaving).DeleteAsync("/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"deletedProjects\":2", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty((await Client(leaving).GetFromJsonAsync<Summary[]>("/projects"))!);
        Assert.Single((await Client(staying).GetFromJsonAsync<Summary[]>("/projects"))!);
    }

    [Theory]
    [InlineData("https://motiff.example", true)]
    [InlineData("https://motiff-146.example", true)]
    [InlineData("https://elsewhere.example", false)]
    [InlineData("https://motiff-146.example.evil", false)]
    public async Task Browsers_may_call_only_from_the_app_and_its_previews(string origin, bool allowed)
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/projects/p_any");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");

        HttpResponseMessage response = await _api.CreateClient().SendAsync(preflight);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void A_packed_file_unpacks_to_the_same_bytes()
    {
        byte[] file = File("p_pack");
        Assert.Equal(file, ProjectEndpoints.Unpack(ProjectEndpoints.Pack(file)));
    }

    internal static string Player() => Guid.NewGuid().ToString();

    internal static byte[] File(string id) =>
        ProjectFile.Write(Project.Create(id, "minuet.musicxml", Encoding.UTF8.GetBytes("<score-partwise/>"), s_created));

    internal static ByteArrayContent Json(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    /// <summary>A save over the copy online that still has <paramref name="etag"/>.</summary>
    private static HttpRequestMessage Update(string id, byte[] body, EntityTagHeaderValue etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/projects/{id}") { Content = Json(body) };
        request.Headers.IfMatch.Add(etag);
        return request;
    }

    [Fact]
    public async Task A_player_on_any_plan_keeps_a_name_and_a_photo_until_everything_is_erased()
    {
        string player = Player();
        HttpClient free = Client(player, roles: []);
        Assert.Equal(new Profile(null, null), await free.GetFromJsonAsync<Profile>("/profile"));

        string photo = "data:image/jpeg;base64," + Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 0, 16]);
        HttpResponseMessage saved = await free.PutAsJsonAsync("/profile", new Profile("  Andrei K.  ", photo));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(new Profile("Andrei K.", photo), await free.GetFromJsonAsync<Profile>("/profile"));

        // Another player sees only their own; erasing everything takes the profile too.
        Assert.Equal(new Profile(null, null), await Client(Player()).GetFromJsonAsync<Profile>("/profile"));
        await free.DeleteAsync("/me");
        Assert.Equal(new Profile(null, null), await free.GetFromJsonAsync<Profile>("/profile"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/profile")).StatusCode);
    }

    [Theory]
    [InlineData("data:image/gif;base64,R0lGODlh", HttpStatusCode.BadRequest)]
    [InlineData("data:image/png;base64,/9j/4AAQ", HttpStatusCode.BadRequest)]
    [InlineData("data:image/jpeg;base64,not base64!", HttpStatusCode.BadRequest)]
    [InlineData("https://example.com/me.jpg", HttpStatusCode.BadRequest)]
    public async Task A_photo_is_a_small_picture_in_the_profile_itself(string photo, HttpStatusCode expected)
    {
        HttpResponseMessage response = await Client(Player()).PutAsJsonAsync("/profile", new Profile("A", photo));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_name_is_one_line_of_up_to_sixty_characters_and_a_photo_up_to_200_KB()
    {
        HttpClient client = Client(Player());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/profile", new Profile(new string('a', 61), null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/profile", new Profile("two\nlines", null))).StatusCode);
        string large = "data:image/jpeg;base64," + Convert.ToBase64String([0xFF, 0xD8, 0xFF, .. new byte[201 * 1024]]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PutAsJsonAsync("/profile", new Profile("A", large))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/profile", new Profile(new string('a', 60), null))).StatusCode);
    }

    private HttpClient Client(string player, string scope = "Projects.ReadWrite", string[]? roles = null, string? choice = null)
    {
        HttpClient client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Api.Token(player, scope, roles ?? ["Pro"]));
        if (choice is not null)
        {
            client.DefaultRequestHeaders.Add("Motiff-Plan", choice);
        }

        return client;
    }

    private sealed record Account(string Plan, bool Tester, string? Chosen, Dictionary<string, bool> Features);

    private sealed record Profile(string? Name, string? Photo);

    private sealed record Summary(string Id, string FileName, int MotifCount, int ResumeCount);

    /// <summary>The API with an in-memory store and tokens signed by a test key instead of External ID's.</summary>
    public class Api : WebApplicationFactory<Program>
    {
        private const string Issuer = "https://login.test/tenant/v2.0";
        private const string Audience = "api-client-id";
        private static readonly SymmetricSecurityKey s_key = new(RandomNumberGenerator.GetBytes(32));

        public static string Token(string player, string scope, string[] roles) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object> { ["oid"] = player, ["scp"] = scope, ["roles"] = roles },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(s_key, SecurityAlgorithms.HmacSha256),
        });

        /// <summary>The database the API is told it has; the store is in memory whatever it says (TestReset reads its name).</summary>
        protected virtual string? CosmosEndpoint => null;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Cors:Origins", "https://motiff.example");
            builder.UseSetting("Cors:OriginPattern", @"https://motiff-[0-9]+\.example");
            if (CosmosEndpoint is { } endpoint)
            {
                builder.UseSetting("Cosmos:Endpoint", endpoint);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IProjectStore, InMemoryProjectStore>();
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = Issuer,
                        ValidAudience = Audience,
                        IssuerSigningKey = s_key,
                    };
                });
            });
        }
    }
}