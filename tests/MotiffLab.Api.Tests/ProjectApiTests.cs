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

    private HttpClient Client(string player, string scope = "Projects.ReadWrite")
    {
        HttpClient client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Api.Token(player, scope));
        return client;
    }

    private sealed record Summary(string Id, string FileName, int MotifCount, int ResumeCount);

    /// <summary>The API with an in-memory store and tokens signed by a test key instead of External ID's.</summary>
    public sealed class Api : WebApplicationFactory<Program>
    {
        private const string Issuer = "https://login.test/tenant/v2.0";
        private const string Audience = "api-client-id";
        private static readonly SymmetricSecurityKey s_key = new(RandomNumberGenerator.GetBytes(32));

        public static string Token(string player, string scope) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object> { ["oid"] = player, ["scp"] = scope },
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