using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Infrastructure;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);
const string DevelopmentCorsPolicy = "DevelopmentWebClient";

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".data-protection")))
        .SetApplicationName("VirtualCompany.Api");
}

builder.Services
    .AddControllers()
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
builder.Services.AddVirtualCompanyInfrastructure(builder.Configuration);
builder.Services.AddCompanyAuthorization(builder.Environment);
builder.Services.AddVirtualCompanyRateLimiting(builder.Configuration);

var app = builder.Build();
var applyMigrationsOnStartup =
    app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("DatabaseInitialization:ApplyMigrationsOnStartup");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(DevelopmentCorsPolicy);
app.UseAuthentication();
app.UseMiddleware<CompanyContextResolutionMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
    var templateSeeder = scope.ServiceProvider.GetRequiredService<CompanySetupTemplateSeeder>();
    var providerName = dbContext.Database.ProviderName;
    var isPostgreSql = string.Equals(providerName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);
    var isSqlite = string.Equals(providerName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);

    if (isPostgreSql)
    {
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE EXTENSION IF NOT EXISTS vector;""");
        // The repo still carries historical SQL Server migrations; PostgreSQL environments
        // bootstrap from the current model until the provider history is rebased.
        await dbContext.Database.EnsureCreatedAsync();
    }
    else if (isSqlite)
    {
        await dbContext.Database.EnsureCreatedAsync();
    }
    else if (dbContext.Database.IsRelational())
    {
        if (applyMigrationsOnStartup)
        {
            await dbContext.Database.MigrateAsync();
        }
    } 
    else if (app.Environment.IsDevelopment())
    {
        await dbContext.Database.EnsureCreatedAsync();
    }

    await templateSeeder.SeedAsync();
}

app.MapVirtualCompanyHealthEndpoints();
app.MapControllers();

app.Run();

public partial class Program;
