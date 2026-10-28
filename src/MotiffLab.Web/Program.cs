using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using MotiffLab.Web;
using MotiffLab.Web.Account;
using MotiffLab.Web.Analytics;
using MotiffLab.Web.Import;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Projects;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<Player>();
builder.Services.AddScoped<CurrentPiece>();
builder.Services.AddScoped<ProjectStore>();
builder.Services.AddScoped<UsageAnalytics>();
builder.Services.AddScoped<ScanService>();
builder.Services.AddScoped<UiText>();
builder.Services.AddScoped<Preferences>();

// Sign-in and saving online (0014) exist only in a build whose appsettings.json names the External ID tenant.
AccountOptions account = builder.Configuration.GetSection("Account").Get<AccountOptions>() ?? new AccountOptions();
builder.Services.AddSingleton(account);
builder.Services.AddScoped<CloudAccount>();
builder.Services.AddScoped<OnlineProjects>();
if (account.IsConfigured)
{
    builder.Services.AddMsalAuthentication(options =>
    {
        options.ProviderOptions.Authentication.Authority = account.Authority;
        options.ProviderOptions.Authentication.ClientId = account.ClientId;
        options.ProviderOptions.Authentication.ValidateAuthority = false;
        options.ProviderOptions.LoginMode = "redirect";
        options.ProviderOptions.DefaultAccessTokenScopes.Add(account.ApiScope);
        if (account.ScanScope.Length > 0)
        {
            options.ProviderOptions.AdditionalScopesToConsent.Add(account.ScanScope);
        }
    });
}

WebAssemblyHost host = builder.Build();
// The interface's words load before the first page renders, so nothing shows in a key or the wrong language (0012).
await host.Services.GetRequiredService<UiText>().InitializeAsync();
await host.RunAsync();