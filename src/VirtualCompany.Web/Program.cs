using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Net.Sockets;
using System.Text.Json;
using VirtualCompany.Web;
using VirtualCompany.Web.Localization;
using VirtualCompany.Web.Services;
using VirtualCompany.Web.Localization.Formatting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
// Marker types already include the Localization namespace used by embedded resources.
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(SupportedWebCultures.Default);
    options.SupportedCultures = SupportedWebCultures.CultureInfos;
    options.SupportedUICultures = SupportedWebCultures.CultureInfos;
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    ];
});
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<FinanceSimulationControlPanelOptions>(builder.Configuration.GetSection(FinanceSimulationControlPanelOptions.SectionName));
builder.Services.AddScoped<IFinanceSandboxAdminService, FinanceSandboxAdminService>();
builder.Services.AddScoped<FinanceAccessResolver>();
builder.Services.AddScoped<IDashboardInteractionService, DashboardInteractionService>();
builder.Services.AddScoped<ICompanyPresentationContext, CompanyPresentationContext>();
builder.Services.AddScoped<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
builder.Services.AddScoped<INumberFormatter, NumberFormatter>();
builder.Services.AddScoped<IMoneyFormatter, MoneyFormatter>();
builder.Services.AddScoped<IApiProblemMessageResolver, ApiProblemMessageResolver>();

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
        BaseAddress = ResolveApiBaseAddress(configuredApiBaseUrl, useOfflineMode, httpContext),
        // A text workshop can perform checkpoint -> permitted research -> checkpoint.
        // Keep the client timeout above the bounded aggregate provider timeout.
        Timeout = TimeSpan.FromMinutes(3)
    };

    httpClient.ApplyRequestHeaders(httpContext);
    httpClient.ApplyDevelopmentAuth(developmentAuth);
    return httpClient;
});
// Capability clients share the configured transport and authentication headers.
builder.Services.AddVirtualCompanyApiClients(builder.Configuration);

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseStaticFiles();
var sharedImagesPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "images"));
if (Directory.Exists(sharedImagesPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(sharedImagesPath),
        RequestPath = "/images"
    });
}
app.UseAntiforgery();

app.MapGuidedRealtimeProxyEndpoints();

app.MapPost("/localization/apply", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    if (!SupportedWebCultures.TryNormalize(form["culture"], out var culture))
    {
        return Results.BadRequest("Unsupported culture.");
    }

    var returnUrl = LocalizationRequestPolicy.NormalizeLocalReturnUrl(form["returnUrl"]);
    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        LocalizationRequestPolicy.CreateCookieValue(culture),
        new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps
        });

    return Results.LocalRedirect(returnUrl);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static Uri ResolveApiBaseAddress(string? configuredValue, bool useOfflineMode, HttpContext? httpContext)
{
    if (TryResolveReachableApiBaseAddress(configuredValue, httpContext, out var resolvedUri))
    {
        return resolvedUri;
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

static bool TryResolveReachableApiBaseAddress(string? configuredValue, HttpContext? httpContext, out Uri resolvedUri)
{
    var candidates = GetApiBaseAddressCandidates(configuredValue, httpContext);
    foreach (var candidate in candidates)
    {
        if (IsReachable(candidate))
        {
            resolvedUri = candidate;
            return true;
        }
    }

    if (Uri.TryCreate(configuredValue, UriKind.Absolute, out var configuredUri))
    {
        resolvedUri = configuredUri;
        return true;
    }

    resolvedUri = default!;
    return false;
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

static IReadOnlyList<Uri> GetApiBaseAddressCandidates(string? configuredValue, HttpContext? httpContext)
{
    var results = new List<Uri>();
    AddCandidate(results, configuredValue);
    AddCandidate(results, Environment.GetEnvironmentVariable("VC_API_BASE_URL"));
    AddCandidate(results, Environment.GetEnvironmentVariable("ApiBaseUrl"));

    foreach (var candidate in ReadApiLaunchProfileUrls())
    {
        AddCandidate(results, candidate);
    }

    var hostUri = ResolveHostUri(httpContext);
    if (string.Equals(hostUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || hostUri.IsLoopback)
    {
        AddCandidate(results, "http://localhost:5301/");
        AddCandidate(results, "https://localhost:7120/");
        AddCandidate(results, "http://127.0.0.1:5123/");
    }

    return results;
}

static void AddCandidate(List<Uri> results, string? value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
    {
        return;
    }

    if (results.Any(existing => Uri.Compare(existing, uri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0))
    {
        return;
    }

    results.Add(uri);
}

static IReadOnlyList<string> ReadApiLaunchProfileUrls()
{
    var urls = new List<string>();
    var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "VirtualCompany.Api", "Properties", "launchSettings.json");
    if (!File.Exists(path))
    {
        return urls;
    }

    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("profiles", out var profiles))
        {
            return urls;
        }

        foreach (var profile in profiles.EnumerateObject())
        {
            if (!profile.Value.TryGetProperty("applicationUrl", out var applicationUrl))
            {
                continue;
            }

            var value = applicationUrl.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var url in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                urls.Add(url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/");
            }
        }
    }
    catch
    {
        return urls;
    }

    return urls;
}


static bool IsReachable(Uri uri)
{
    try
    {
        var port = uri.IsDefaultPort
            ? string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(uri.Host, port);
        return connectTask.Wait(TimeSpan.FromMilliseconds(250)) && client.Connected;
    }
    catch
    {
        return false;
    }
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
