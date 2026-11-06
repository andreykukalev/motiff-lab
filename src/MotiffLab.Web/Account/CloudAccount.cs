using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

using MotiffLab.MusicCore.Accounts;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Account;

/// <summary>
/// Where players sign in (decision record 0014): the Microsoft Entra External ID tenant, the web app's registration
/// and the Motiff Lab API's scopes, from <c>wwwroot/appsettings.json</c>. All empty in a build without sign-in,
/// and then nothing about accounts shows.
/// </summary>
public sealed record AccountOptions
{
    /// <summary>https://&lt;domain&gt;.ciamlogin.com/&lt;tenant id&gt;</summary>
    public string Authority { get; init; } = "";

    public string ClientId { get; init; } = "";

    /// <summary>api://&lt;API client id&gt;/Projects.ReadWrite</summary>
    public string ApiScope { get; init; } = "";

    /// <summary>api://&lt;API client id&gt;/Scan</summary>
    public string ScanScope { get; init; } = "";

    /// <summary>The Motiff Lab API's address.</summary>
    public string Api { get; init; } = "";

    /// <summary>The hosted scan server's address. Only this address gets the player's Scan token.</summary>
    public string Scan { get; init; } = "";

    public bool IsConfigured =>
        Authority.Length > 0 && ClientId.Length > 0 && ApiScope.Length > 0 && Uri.TryCreate(Api, UriKind.Absolute, out _);
}

/// <summary>
/// The player's account, when the build has sign-in: who is signed in, and tokens for the API and the hosted
/// scan server. Signing in and out go through the External ID pages (<c>Pages/Authentication.razor</c>).
/// Its plan (Pro or not, 0018 section 12) comes from the API, which reads it from the sign-in; a tester's choice of
/// Pro or no Pro is kept on this device and sent with every request to the API and the hosted scan server
/// (<see cref="Plans.Header"/>), which trust it only from a tester.
/// </summary>
public sealed class CloudAccount(AccountOptions options, NavigationManager navigation, UiText text, Preferences preferences, IServiceProvider services) : IDisposable
{
    private const string ChoiceKey = "ml-test-plan";

    private readonly HttpClient _http = new();
    private AccountPlan? _plan;
    private string? _choice;
    private bool _choiceLoaded;

    public AccountOptions Options => options;

    /// <summary>Raised when the plan or a tester's choice changes.</summary>
    public event Action? PlanChanged;

    /// <summary>False in a build without sign-in; then every other member answers "signed out".</summary>
    public bool IsAvailable => options.IsConfigured;

    /// <summary>The signed-in player's name or email, or null when nobody is signed in.</summary>
    public async Task<string?> PlayerAsync()
    {
        if (!IsAvailable || services.GetService<AuthenticationStateProvider>() is not { } provider)
        {
            return null;
        }

        AuthenticationState state = await provider.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return PlayerName.Of(state.User.Claims.Select(claim => (claim.Type, claim.Value)), state.User.Identity.Name);
    }

    /// <summary>An access token for the scope without asking the player anything, or null when there is none.</summary>
    public async Task<string?> TokenAsync(string scope)
    {
        if (!IsAvailable || scope.Length == 0 || services.GetService<IAccessTokenProvider>() is not { } tokens)
        {
            return null;
        }

        try
        {
            AccessTokenResult result = await tokens.RequestAccessToken(new AccessTokenRequestOptions { Scopes = [scope] });
            return result.TryGetToken(out AccessToken? token) ? token.Value : null;
        }
        catch (AccessTokenNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the sign-in page in the app's language, and comes back to this page. <paramref name="create"/> opens it on
    /// the sign-up form (<c>prompt=create</c>), so a new player does not first meet "we couldn't find an account".
    /// </summary>
    public void SignIn(bool create = false)
    {
        var request = new InteractiveRequestOptions { Interaction = InteractionType.SignIn, ReturnUrl = navigation.Uri };
        if (create)
        {
            request.TryAddAdditionalParameter("prompt", "create");
        }

        // External ID takes both: ui_locales picks the page's language, mkt its market strings. A JsonElement, because the
        // request is serialized with a source-generated context that knows strings, string lists and JsonElement only.
        string locale = text.Language == "ru" ? "ru-RU" : "en-US";
        using JsonDocument query = JsonDocument.Parse($$"""{"ui_locales":"{{locale}}","mkt":"{{locale}}"}""");
        request.TryAddAdditionalParameter("extraQueryParameters", query.RootElement.Clone());
        navigation.NavigateToLogin("authentication/login", request);
    }

    public void SignOut() => navigation.NavigateToLogout("authentication/logout", "settings");

    /// <summary>
    /// The signed-in player's plan as the API answers it, asked once and kept; null when nobody is signed in or the API
    /// cannot be reached, and then the app offers no Pro feature.
    /// </summary>
    public async Task<AccountPlan?> PlanAsync()
    {
        if (_plan is not null || await PlayerAsync() is null || await TokenAsync(options.ApiScope) is not { } token)
        {
            return _plan;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(options.Api.TrimEnd('/') + "/"), "account"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await AddPlanChoiceAsync(request);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                using JsonDocument answer = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
                _plan = AccountPlan.Read(answer.RootElement);
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException)
        {
            // Unreachable for now: asked again next time.
        }

        return _plan;
    }

    /// <summary>Whether the signed-in player may use the feature; false when signed out.</summary>
    public async Task<bool> AllowsAsync(ProFeature feature) => (await PlanAsync())?.Allows(feature) == true;

    /// <summary>A tester's choice on this device: Pro, no Pro, or null for the plan of the account.</summary>
    public async Task<Plan?> PlanChoiceAsync()
    {
        if (!_choiceLoaded)
        {
            _choice = await preferences.GetAsync(ChoiceKey);
            _choiceLoaded = true;
        }

        return Plans.Parse(_choice);
    }

    /// <summary>Switches a tester between Pro and no Pro (null: the account's own plan), and asks the API again.</summary>
    public async Task SetPlanChoiceAsync(Plan? choice)
    {
        _choice = choice is { } plan ? Plans.Id(plan) : null;
        _choiceLoaded = true;
        await preferences.SetAsync(ChoiceKey, _choice);
        _plan = null;
        await PlanAsync();
        PlanChanged?.Invoke();
    }

    /// <summary>Adds a tester's choice to a request for the API or the hosted scan server; nothing when there is none.</summary>
    public async Task AddPlanChoiceAsync(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await PlanChoiceAsync() is { } choice)
        {
            request.Headers.Add(Plans.Header, Plans.Id(choice));
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>What the API says the player's plan is (<c>GET /account</c>).</summary>
public sealed record AccountPlan(Plan Plan, bool IsTester, Plan? Chosen, IReadOnlySet<ProFeature> Features)
{
    public bool IsPro => Plan == Plan.Pro;

    public bool Allows(ProFeature feature) => Features.Contains(feature);

    internal static AccountPlan Read(JsonElement answer)
    {
        Plan plan = Plans.Parse(answer.GetProperty("plan").GetString()) ?? Plan.Free;
        bool tester = answer.TryGetProperty("tester", out JsonElement t) && t.ValueKind == JsonValueKind.True;
        Plan? chosen = answer.TryGetProperty("chosen", out JsonElement c) && c.ValueKind == JsonValueKind.String ? Plans.Parse(c.GetString()) : null;
        var features = new HashSet<ProFeature>();
        if (answer.TryGetProperty("features", out JsonElement allowed))
        {
            foreach (ProFeature feature in Plans.Features)
            {
                if (allowed.TryGetProperty(Plans.Id(feature), out JsonElement value) && value.ValueKind == JsonValueKind.True)
                {
                    features.Add(feature);
                }
            }
        }

        return new AccountPlan(plan, tester, chosen, features);
    }
}