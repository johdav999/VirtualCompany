using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VirtualCompany.Web;
using VirtualCompany.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredApiBaseUrl = builder.Configuration["ApiBaseUrl"];
var useOfflineMode = ShouldUseOfflineMode(builder, configuredApiBaseUrl);
var developmentAuth = ResolveDevelopmentAuth(builder);

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = ResolveApiBaseAddress(builder, configuredApiBaseUrl, useOfflineMode)
}.ApplyDevelopmentAuth(developmentAuth));
builder.Services.AddScoped(sp => new OnboardingApiClient(
    sp.GetRequiredService<HttpClient>(),
    useOfflineMode));
builder.Services.AddScoped(sp => new AgentApiClient(
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

static DevelopmentAuthSettings? ResolveDevelopmentAuth(WebAssemblyHostBuilder builder)
{
    if (!builder.HostEnvironment.IsDevelopment())
    {
        return null;
    }

    var subject = builder.Configuration["DevelopmentAuth:Subject"]?.Trim();
    var email = builder.Configuration["DevelopmentAuth:Email"]?.Trim();
    var displayName = builder.Configuration["DevelopmentAuth:DisplayName"]?.Trim();
    var provider = builder.Configuration["DevelopmentAuth:Provider"]?.Trim();

    if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(email))
    {
        var hostUri = new Uri(builder.HostEnvironment.BaseAddress);
        return string.Equals(hostUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? new DevelopmentAuthSettings(
                "alice",
                "alice@example.com",
                "Alice Admin",
                "dev-header")
            : null;
    }

    return new DevelopmentAuthSettings(
        subject ?? email!,
        email,
        displayName,
        string.IsNullOrWhiteSpace(provider) ? "dev-header" : provider);
}

static class HttpClientDevelopmentAuthExtensions
{
    public static HttpClient ApplyDevelopmentAuth(this HttpClient httpClient, DevelopmentAuthSettings? auth)
    {
        if (auth is null)
        {
            return httpClient;
        }

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-Subject", auth.Subject);

        if (!string.IsNullOrWhiteSpace(auth.Email))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-Email", auth.Email);
        }

        if (!string.IsNullOrWhiteSpace(auth.DisplayName))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-DisplayName", auth.DisplayName);
        }

        if (!string.IsNullOrWhiteSpace(auth.Provider))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-Provider", auth.Provider);
        }

        return httpClient;
    }
}

sealed record DevelopmentAuthSettings(
    string Subject,
    string? Email,
    string? DisplayName,
    string? Provider);
