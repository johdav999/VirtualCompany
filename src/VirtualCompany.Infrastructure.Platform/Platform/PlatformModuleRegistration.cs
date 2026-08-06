using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Security;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Platform;

public static class PlatformModuleRegistration
{
    public static IServiceCollection AddPlatformInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("VirtualCompanyDb")
            ?? "Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=False";

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddDataProtection();
        services.TryAddSingleton<IFieldEncryptionService, DataProtectionFieldEncryptionService>();
        services.AddPlatformSecretStore(configuration);
        services.AddDbContext<VirtualCompanyDbContext>(options =>
            ConfigureDatabase(options, connectionString, configuration["Database:Provider"]));

        services.AddOptions<BackgroundExecutionOptions>()
            .Bind(configuration.GetSection(BackgroundExecutionOptions.SectionName))
            .Configure(options => options.BaseRetryDelaySeconds = Math.Max(options.BaseRetryDelaySeconds, 0));
        services.AddSingleton<IBackgroundJobFailureClassifier, DefaultBackgroundJobFailureClassifier>();
        services.AddSingleton<IBackgroundJobExecutor, BackgroundJobExecutor>();
        services.AddSingleton<IBackgroundExecutionRetryPolicy, ExponentialBackgroundExecutionRetryPolicy>();
        services.AddSingleton<IBackgroundExecutionIdentityFactory, DefaultBackgroundExecutionIdentityFactory>();
        services.AddScoped<IBackgroundExecutionRecorder, BackgroundExecutionRecorder>();

        services.AddHttpContextAccessor();
        services.AddOptions<PlatformAdministrationOptions>()
            .Bind(configuration.GetSection(PlatformAdministrationOptions.SectionName));
        services.AddScoped<ICompanyContextAccessor, RequestCompanyContextAccessor>();
        services.AddScoped<ICompanyExecutionScopeFactory, CompanyExecutionScopeFactory>();
        services.AddScoped<ClaimsPrincipalExternalUserIdentityFactory>();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        services.AddScoped<IUserPreferenceService, UserPreferenceService>();
        services.AddScoped<IExternalUserIdentityAccessor, ClaimsExternalUserIdentityAccessor>();
        services.AddScoped<IExternalUserIdentityResolver, ExternalUserIdentityResolver>();
        services.AddScoped<IAuditEventWriter, AuditEventWriter>();
        services.AddScoped<IAuditQueryService, CompanyAuditQueryService>();
        services.AddTransient<IClaimsTransformation, UserClaimsTransformation>();

        services.AddOptions<RedisExecutionCoordinationOptions>()
            .Bind(configuration.GetSection(RedisExecutionCoordinationOptions.SectionName))
            .Validate(
                options => options.DefaultLockLeaseSeconds > 0 && options.DefaultExecutionStateTtlSeconds > 0,
                "Redis execution coordination TTL values must be positive.")
            .PostConfigure(options =>
            {
                options.KeyPrefix = string.IsNullOrWhiteSpace(options.KeyPrefix) ? "vc" : options.KeyPrefix.Trim();
            });

        var redisConnectionString = configuration[$"{ObservabilityOptions.SectionName}:Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redisConnectionString);
                options.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<RedisExecutionCoordinationService>();
            services.AddSingleton<IExecutionCoordinationStore>(provider => provider.GetRequiredService<RedisExecutionCoordinationService>());
            services.AddSingleton<IExecutionCoordinationKeyBuilder>(provider => provider.GetRequiredService<RedisExecutionCoordinationService>());
            services.AddSingleton<IDistributedLockProvider>(provider => provider.GetRequiredService<RedisExecutionCoordinationService>());
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "virtual-company:";
            });
        }
        else
        {
            services.AddSingleton<InMemoryExecutionCoordinationService>();
            services.AddSingleton<IExecutionCoordinationStore>(provider => provider.GetRequiredService<InMemoryExecutionCoordinationService>());
            services.AddSingleton<IExecutionCoordinationKeyBuilder>(provider => provider.GetRequiredService<InMemoryExecutionCoordinationService>());
            services.AddSingleton<IDistributedLockProvider>(provider => provider.GetRequiredService<InMemoryExecutionCoordinationService>());
            services.AddDistributedMemoryCache();
        }

        services.AddVirtualCompanyObservability(configuration);
        return services;
    }

    private static void ConfigureDatabase(
        DbContextOptionsBuilder options,
        string connectionString,
        string? configuredProvider)
    {
        switch (ResolveDatabaseProvider(configuredProvider, connectionString))
        {
            case DatabaseProvider.PostgreSql:
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());
                break;
            case DatabaseProvider.Sqlite:
                options.UseSqlite(connectionString);
                break;
            default:
                options.UseSqlServer(
                    connectionString,
                    sqlServerOptions => sqlServerOptions
                        .MigrationsAssembly("VirtualCompany.Persistence.Migrations")
                        .EnableRetryOnFailure());
                break;
        }

        options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    private static DatabaseProvider ResolveDatabaseProvider(string? configuredProvider, string connectionString)
    {
        var normalizedProvider = string.IsNullOrWhiteSpace(configuredProvider)
            ? string.Empty
            : configuredProvider.Trim().ToLowerInvariant();

        return normalizedProvider switch
        {
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.PostgreSql,
            "sqlite" => DatabaseProvider.Sqlite,
            "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
            _ when connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
                   connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase) => DatabaseProvider.PostgreSql,
            _ when connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
                   connectionString.Contains("Filename=", StringComparison.OrdinalIgnoreCase) => DatabaseProvider.Sqlite,
            _ => DatabaseProvider.SqlServer
        };
    }

    private enum DatabaseProvider
    {
        SqlServer,
        PostgreSql,
        Sqlite
    }
}
