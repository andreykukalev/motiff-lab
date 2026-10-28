using System.Text.RegularExpressions;

using Azure.Identity;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Cosmos;

using MotiffLab.Api;

// The Motiff Lab API (decision record 0014): a signed-in player's projects, with their sessions and sources,
// saved in Cosmos DB. Players sign in through Microsoft Entra External ID; every document is the player's own.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IConfiguration config = builder.Configuration;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // The External ID tenant's authority, https://<tenant>.ciamlogin.com/<tenant id>/v2.0, and the API's client id.
        options.Authority = config["Auth:Authority"];
        options.Audience = config["Auth:Audience"];
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Player.Policy, policy => policy.RequireAuthenticatedUser().RequireClaim("oid").RequireAssertion(context => Player.HasScope(context.User)));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(AllowedOrigins(config))
    .AllowAnyHeader()
    .WithMethods("GET", "PUT", "DELETE")
    .WithExposedHeaders("ETag")
    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

// Created on first use, so the tests can put their own store in its place.
builder.Services.AddSingleton<IProjectStore>(_ =>
{
    string endpoint = config["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Cosmos:Endpoint is not set.");
    // Signs in as the app's managed identity (AZURE_CLIENT_ID); no account key is used.
    var client = new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions
    {
        UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
        ApplicationName = "motiff-lab-api",
    });
    return new CosmosProjectStore(client.GetContainer(config["Cosmos:Database"] ?? "motifflab", config["Cosmos:Container"] ?? "projects"));
});

WebApplication app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { service = "motiff-lab-api" }));
app.MapProjects();
app.Run();

// The web app, and its pull request previews by pattern; a native app sends no origin.
static Func<string, bool> AllowedOrigins(IConfiguration config)
{
    string[] origins = (config["Cors:Origins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    Regex? pattern = string.IsNullOrEmpty(config["Cors:OriginPattern"])
        ? null
        : new Regex($"^(?:{config["Cors:OriginPattern"]})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    return origin => origins.Contains(origin, StringComparer.Ordinal) || (pattern?.IsMatch(origin) ?? false);
}

public partial class Program;