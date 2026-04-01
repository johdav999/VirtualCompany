using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVirtualCompanyInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("VirtualCompanyDb")
            ?? "Host=localhost;Port=5432;Database=virtualcompany;Username=postgres;Password=postgres;Include Error Detail=true";

        services.AddDbContext<VirtualCompanyDbContext>(options =>
        {
            if (LooksLikeSqliteConnectionString(connectionString))
            {
                options.UseSqlite(connectionString);
                return;
            }

            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());
        });

        services.AddOptions<CompanyOutboxDispatcherOptions>()
            .Bind(configuration.GetSection(CompanyOutboxDispatcherOptions.SectionName));

        services.AddHostedService<CompanyOutboxDispatcherBackgroundService>();
        services.AddVirtualCompanyObservability(configuration);

        services.AddSingleton<IBackgroundJobFailureClassifier, DefaultBackgroundJobFailureClassifier>();
        services.AddSingleton<IBackgroundJobExecutor, BackgroundJobExecutor>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICompanyContextAccessor, RequestCompanyContextAccessor>();
        services.AddScoped<ClaimsPrincipalExternalUserIdentityFactory>();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        services.AddScoped<IExternalUserIdentityAccessor, ClaimsExternalUserIdentityAccessor>();
        services.AddScoped<IExternalUserIdentityResolver, ExternalUserIdentityResolver>();
        services.AddScoped<CompanyQueryService>();
        services.AddScoped<ICompanyOutboxEnqueuer, CompanyOutboxEnqueuer>();
        services.AddScoped<ICompanyOutboxProcessor, CompanyOutboxProcessor>();
        services.AddScoped<ICompanyInvitationDeliveryDispatcher, CompanyInvitationDeliveryDispatcher>();
        services.AddScoped<ICompanyInvitationSender, LoggingCompanyInvitationSender>();
        services.AddScoped<IAuditEventWriter, AuditEventWriter>();
        services.AddScoped<ICurrentUserCompanyService>(provider => provider.GetRequiredService<CompanyQueryService>());
        services.AddScoped<ICompanyNoteService>(provider => provider.GetRequiredService<CompanyQueryService>());
        services.AddScoped<ICompanyMembershipAdministrationService, CompanyMembershipAdministrationService>();
        services.AddScoped<CompanySetupTemplateSeeder>();
        services.AddScoped<ICompanyOnboardingService, CompanyOnboardingService>();
        services.AddTransient<IClaimsTransformation, UserClaimsTransformation>();
        services.AddScoped<IAgentRuntimeProfileResolver, PersistedAgentRuntimeProfileResolver>();
        services.AddScoped<ICompanyAgentService, CompanyAgentService>();
        services.AddScoped<IAgentAssignmentGuard, CompanyAgentAssignmentGuard>();
        services.AddScoped<IAgentToolExecutionService, CompanyAgentToolExecutionService>();
        services.AddScoped<IPolicyGuardrailEngine, PolicyGuardrailEngine>();
        services.AddScoped<ICompanyToolExecutor, NoOpCompanyToolExecutor>();
        services.AddScoped<CompanyContextResolutionMiddleware>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipResourceAuthorizationHandler>();
        services.AddScoped<ICompanyMembershipContextResolver, CompanyMembershipContextResolver>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipRoleAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipRoleResourceAuthorizationHandler>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = DevHeaderAuthenticationDefaults.Scheme;
                options.DefaultChallengeScheme = DevHeaderAuthenticationDefaults.Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(
                DevHeaderAuthenticationDefaults.Scheme,
                _ => { });

        return services;
    }

    private static bool LooksLikeSqliteConnectionString(string connectionString) =>
        connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains(".sqlite", StringComparison.OrdinalIgnoreCase);
}
