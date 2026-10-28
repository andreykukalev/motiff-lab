using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

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
/// </summary>
public sealed class CloudAccount(AccountOptions options, NavigationManager navigation, IServiceProvider services)
{
    public AccountOptions Options => options;

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

        return state.User.FindFirst("name")?.Value
            ?? state.User.FindFirst("email")?.Value
            ?? state.User.FindFirst("preferred_username")?.Value
            ?? state.User.Identity.Name
            ?? "";
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

    public void SignIn() => navigation.NavigateToLogin("authentication/login");

    public void SignOut() => navigation.NavigateToLogout("authentication/logout", "settings");
}