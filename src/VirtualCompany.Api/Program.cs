using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualCompany.Infrastructure;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Api;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Activity;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Api.Hubs;
using VirtualCompany.Application.Sales;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "VirtualCompanyTeamsMedia");
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMinutes(2));
builder.Configuration.AddJsonFile("appsettings.MediaHost.json", optional: true, reloadOnChange: false);
const string DevelopmentCorsPolicy = "DevelopmentWebClient";

var keyVaultUriValue = builder.Configuration["AzureKeyVault:Uri"] ?? builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUriValue))
{
    if (!Uri.TryCreate(keyVaultUriValue, UriKind.Absolute, out var keyVaultUri))
    {
        throw new InvalidOperationException("Azure Key Vault URI configuration value is invalid.");
    }

    builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = builder.Configuration["AzureKeyVault:ManagedIdentityClientId"]
    }));
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var monitoringConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ??
    builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(monitoringConnection))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options => options.ConnectionString = monitoringConnection);
    builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(
        "VirtualCompany.Sales.TeamsPresenter",
        "VirtualCompany.Teams.CallControl",
        "VirtualCompany.Sales.TeamsMedia",
        "VirtualCompany.Sales.TeamsMediaHost"));
}

var dataProtectionKeyRing = DataProtectionKeyRingConfiguration.Configure(
    builder.Services,
    builder.Configuration,
    builder.Environment);

builder.Services
    .AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    });
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevelopmentCorsPolicy, policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IActivityEventPublisher, SignalRActivityEventPublisher>();
builder.Services.AddVirtualCompanyInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ISalesPresentationEventPublisher, SignalRSalesPresentationEventPublisher>();
builder.Services.AddCompanyAuthorization(builder.Environment);
builder.Services.AddVirtualCompanyRateLimiting(builder.Configuration);
builder.Services.Configure<DatabaseInitializationOptions>(builder.Configuration.GetSection(DatabaseInitializationOptions.SectionName));
builder.Services.AddScoped<DatabaseInitializationService>();

var app = builder.Build();

app.Logger.LogInformation(
    "ASP.NET Core Data Protection keys are persisted to {KeyRingPath}. Preserve this directory across restarts and deployments.",
    dataProtectionKeyRing.FullName);

var teamsPackageExitCode = await TeamsPresenterPackageCommand.TryExecuteAsync(
    args,
    app.Services,
    Console.Out,
    Console.Error,
    app.Lifetime.ApplicationStopping);
if (teamsPackageExitCode is not null)
{
    Environment.ExitCode = teamsPackageExitCode.Value;
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseWhen(context => !(context.Connection.LocalPort == 8080 &&
    context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)),
    branch => branch.UseHttpsRedirection());
app.UseRouting();
app.UseCors(DevelopmentCorsPolicy);
app.UseAuthentication();
app.UseMiddleware<CompanyContextResolutionMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>()
        .InitializeAsync(app.Lifetime.ApplicationStopping);
}

if (TryParseFinanceSeedCliCommand(args, out var seedCommand, out var seedCommandError))
{
    if (seedCommandError is not null)
    {
        Console.Error.WriteLine(seedCommandError);
        Environment.ExitCode = 2;
        return;
    }

    using var scope = app.Services.CreateScope();
    var bootstrapService = scope.ServiceProvider.GetRequiredService<IFinanceSeedBootstrapService>();
    var result = await bootstrapService.GenerateAsync(seedCommand!, app.Lifetime.ApplicationStopping);
    Console.WriteLine(JsonSerializer.Serialize(
        result,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    Environment.ExitCode = result.ValidationErrors.Count == 0 ? 0 : 1;
    return;
}

app.MapVirtualCompanyHealthEndpoints();
app.MapControllers();
app.MapHub<ActivityFeedHub>(ActivityFeedHub.Route).RequireAuthorization(CompanyPolicies.AuthenticatedUser);
// The hub authenticates each connection itself: members use normal authentication while the
// customer-visible stage uses a short-lived, session/deck-scoped capability.
app.MapHub<SalesMeetingHub>(SalesMeetingHub.Route);
app.Run();

static bool TryParseFinanceSeedCliCommand(string[] args, out FinanceSeedBootstrapCommand? command, out string? error)
{
    command = null;
    error = null;
    if (args.Length == 0 ||
        !args[0].Equals("seed-finance", StringComparison.OrdinalIgnoreCase) &&
        !args[0].Equals("finance-seed", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    Guid? companyId = null;
    int? seedValue = null;
    DateTime? seedAnchorUtc = null;
    var replaceExisting = true;
    for (var index = 1; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--company-id":
                if (!TryReadNext(args, ref index, out var companyIdValue) || !Guid.TryParse(companyIdValue, out var parsedCompanyId) || parsedCompanyId == Guid.Empty)
                {
                    error = "seed-finance requires --company-id <guid>.";
                    return true;
                }
                companyId = parsedCompanyId;
                break;
            case "--seed":
            case "--seed-value":
                if (!TryReadNext(args, ref index, out var seedValueText) || !int.TryParse(seedValueText, out var parsedSeedValue))
                {
                    error = "seed-finance requires --seed <integer>.";
                    return true;
                }
                seedValue = parsedSeedValue;
                break;
            case "--anchor-utc":
            case "--seed-anchor-utc":
                if (!TryReadNext(args, ref index, out var anchorText) ||
                    !DateTime.TryParse(anchorText, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedAnchor))
                {
                    error = "seed-finance requires --anchor-utc <datetime> when an anchor is supplied.";
                    return true;
                }
                seedAnchorUtc = parsedAnchor.Kind == DateTimeKind.Utc ? parsedAnchor : parsedAnchor.ToUniversalTime();
                break;
            case "--append":
                replaceExisting = false;
                break;
            case "--replace":
            case "--replace-existing":
                replaceExisting = true;
                break;
            default:
                error = $"Unknown seed-finance option '{args[index]}'. Usage: seed-finance --company-id <guid> --seed <integer> [--anchor-utc <datetime>] [--replace|--append]";
                return true;
        }
    }

    if (companyId is null || seedValue is null)
    {
        error = companyId is null ? "seed-finance requires --company-id <guid>." : "seed-finance requires --seed <integer>.";
        return true;
    }

    command = new FinanceSeedBootstrapCommand(companyId.Value, seedValue.Value, seedAnchorUtc, replaceExisting);
    return true;
}

static bool TryReadNext(string[] args, ref int index, out string value)
{
    value = string.Empty;
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return false;
    value = args[++index];
    return true;
}

public partial class Program;
