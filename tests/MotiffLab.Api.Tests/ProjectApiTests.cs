using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

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
        Assert.Equal(HttpStatusCode.NoContent, (await free.PutAsync("/projects/p_kept", Json(File("p_kept")))).StatusCode);
        for (int index = 1; index < Plans.FreeProjectsOnline; index++)
        {
            await _api.Services.GetRequiredService<IProjectStore>().PutAsync(
                player, Project.Create($"p_{index:D4}", "a.musicxml", Encoding.UTF8.GetBytes("<score-partwise/>"), s_created).Summary, "", null, default);
        }

        HttpResponseMessage refused = await free.PutAsync("/projects/p_new", Json(File("p_new")));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("\"code\":\"quota\"", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await free.PutAsync("/projects/p_kept", Json(File("p_kept")))).StatusCode);
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
        Assert.DoesNotContain(true, testing.Features.Values);

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
    public async Task A_save_from_a_stale_copy_is_refused()
    {
        HttpClient client = Client(Player());
        EntityTagHeaderValue first = (await client.PutAsync("/projects/p_two_devices", Json(File("p_two_devices")))).Headers.ETag!;
        await client.PutAsync("/projects/p_two_devices", Json(File("p_two_devices")));

        using var stale = new HttpRequestMessage(HttpMethod.Put, "/projects/p_two_devices") { Content = Json(File("p_two_devices")) };
        stale.Headers.IfMatch.Add(first);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);
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

    private static string Player() => Guid.NewGuid().ToString();

    private static byte[] File(string id) =>
        ProjectFile.Write(Project.Create(id, "minuet.musicxml", Encoding.UTF8.GetBytes("<score-partwise/>"), s_created));

    private static ByteArrayContent Json(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
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
    public sealed class Api : WebApplicationFactory<Program>
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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Cors:Origins", "https://motiff.example");
            builder.UseSetting("Cors:OriginPattern", @"https://motiff-[0-9]+\.example");
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