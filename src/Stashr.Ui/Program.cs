using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Stashr.Ui;
using Stashr.Ui.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The UI is hosted under /ui, but the API lives at the site root (/v1). Point the
// HttpClient at the origin so API paths like "v1/sys/health" resolve correctly.
var origin = new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(origin) });

builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<StashrApi>();
builder.Services.AddScoped<AppState>();

await builder.Build().RunAsync();
