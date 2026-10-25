using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using MotiffLab.Web;
using MotiffLab.Web.Analytics;
using MotiffLab.Web.Import;
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

await builder.Build().RunAsync();