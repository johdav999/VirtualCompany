using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VirtualCompany.Web;
using VirtualCompany.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredApiBaseUrl = builder.Configuration["ApiBaseUrl"];
var useOfflineMode = ShouldUseOfflineMode(builder, configuredApiBaseUrl);

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = ResolveApiBaseAddress(builder, configuredApiBaseUrl, useOfflineMode)
});
builder.Services.AddScoped(sp => new OnboardingApiClient(
    sp.GetRequiredService<HttpClient>(),
    useOfflineMode));

await builder.Build().RunAsync();

static Uri ResolveApiBaseAddress(WebAssemblyHostBuilder builder, string? configuredValue, bool useOfflineMode)
{
    if (Uri.TryCreate(configuredValue, UriKind.Absolute, out var configuredUri))
    {
        return configuredUri;
    }

    var hostUri = new Uri(builder.HostEnvironment.BaseAddress);
    if (useOfflineMode)
    {
        return hostUri;
    }

    if (string.Equals(hostUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return new Uri("https://localhost:7120/");
    }

    return hostUri;
}

static bool ShouldUseOfflineMode(WebAssemblyHostBuilder builder, string? configuredValue)
{
    if (!string.IsNullOrWhiteSpace(configuredValue))
    {
        return false;
    }

    var hostUri = new Uri(builder.HostEnvironment.BaseAddress);
    return string.Equals(hostUri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}
