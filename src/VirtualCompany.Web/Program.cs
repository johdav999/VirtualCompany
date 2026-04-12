using Microsoft.AspNetCore.Http;
using VirtualCompany.Web;
using VirtualCompany.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var environment = sp.GetRequiredService<IWebHostEnvironment>();
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var httpContext = httpContextAccessor.HttpContext;
    var configuredApiBaseUrl = builder.Configuration["ApiBaseUrl"];
    var useOfflineMode = ShouldUseOfflineMode(configuredApiBaseUrl, httpContext);
    var developmentAuth = ResolveDevelopmentAuth(configuration, environment, httpContext);

    var httpClient = new HttpClient
    {
        BaseAddress = ResolveApiBaseAddress(configuredApiBaseUrl, useOfflineMode, httpContext)
    };

    httpClient.ApplyRequestHeaders(httpContext);
    httpClient.ApplyDevelopmentAuth(developmentAuth);
    return httpClient;
});
builder.Services.AddScoped(sp => new OnboardingApiClient(
    sp.GetRequiredService<HttpClient>(),
    ShouldUseOfflineMode(
        sp.GetRequiredService<IConfiguration>()["ApiBaseUrl"],
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext)));
builder.Services.AddScoped(sp => new AgentApiClient(
    sp.GetRequiredService<HttpClient>(),
    ShouldUseOfflineMode(
        sp.GetRequiredService<IConfiguration>()["ApiBaseUrl"],
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext)));
builder.Services.AddScoped(sp => new WorkflowApiClient(
    sp.GetRequiredService<HttpClient>(),
    ShouldUseOfflineMode(
        sp.GetRequiredService<IConfiguration>()["ApiBaseUrl"],
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext)));

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static Uri ResolveApiBaseAddress(string? configuredValue, bool useOfflineMode, HttpContext? httpContext)
{
    if (Uri.TryCreate(configuredValue, UriKind.Absolute, out var configuredUri))
    {
        return configuredUri;
    }

    var hostUri = ResolveHostUri(httpContext);
    if (useOfflineMode)
    {
        return hostUri;
    }

    if (string.Equals(hostUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return new Uri("http://localhost:5301/");
    }

    return hostUri;
}

static bool ShouldUseOfflineMode(string? configuredValue, HttpContext? httpContext)
{
    if (!string.IsNullOrWhiteSpace(configuredValue))
    {
        return false;
    }

    var hostUri = ResolveHostUri(httpContext);
    return string.Equals(hostUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || hostUri.IsLoopback;
}

static DevelopmentAuthSettings? ResolveDevelopmentAuth(IConfiguration configuration, IWebHostEnvironment environment, HttpContext? httpContext)
{
    if (!environment.IsDevelopment())
    {
        return null;
    }

    var subject = configuration["DevelopmentAuth:Subject"]?.Trim();
    var email = configuration["DevelopmentAuth:Email"]?.Trim();
    var displayName = configuration["DevelopmentAuth:DisplayName"]?.Trim();
    var provider = configuration["DevelopmentAuth:Provider"]?.Trim();

    if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(email))
    {
        var hostUri = ResolveHostUri(httpContext);
        return string.Equals(hostUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || hostUri.IsLoopback
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

static Uri ResolveHostUri(HttpContext? httpContext)
{
    if (httpContext is not null)
    {
        return new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}/");
    }

    return new Uri("http://localhost/");
}

static class HttpClientDevelopmentAuthExtensions
{
    public static HttpClient ApplyRequestHeaders(this HttpClient httpClient, HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return httpClient;
        }

        CopyHeader(httpClient, httpContext, "Authorization");
        CopyHeader(httpClient, httpContext, "Cookie");
        CopyHeader(httpClient, httpContext, "X-Dev-Auth-Subject");
        CopyHeader(httpClient, httpContext, "X-Dev-Auth-Email");
        CopyHeader(httpClient, httpContext, "X-Dev-Auth-DisplayName");
        CopyHeader(httpClient, httpContext, "X-Dev-Auth-Provider");
        return httpClient;
    }

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

    private static void CopyHeader(HttpClient httpClient, HttpContext httpContext, string headerName)
    {
        if (httpContext.Request.Headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(headerName, value.ToString());
        }
    }
}

sealed record DevelopmentAuthSettings(
    string Subject,
    string? Email,
    string? DisplayName,
    string? Provider);
