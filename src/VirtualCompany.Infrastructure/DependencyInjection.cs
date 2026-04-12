using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Memory;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Context;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Memory;
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
            ?? "Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        services.AddDbContext<VirtualCompanyDbContext>(options =>
            options
                .UseSqlServer(
                    connectionString,
                    sqlServerOptions => sqlServerOptions.EnableRetryOnFailure())
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddOptions<CompanyDocumentOptions>()
            .Bind(configuration.GetSection(CompanyDocumentOptions.SectionName));

        services.AddOptions<CompanyOutboxDispatcherOptions>()
            .Bind(configuration.GetSection(CompanyOutboxDispatcherOptions.SectionName));

        services.AddOptions<KnowledgeChunkingOptions>()
            .Bind(configuration.GetSection(KnowledgeChunkingOptions.SectionName));

        services.AddOptions<KnowledgeEmbeddingOptions>()
            .Bind(configuration.GetSection(KnowledgeEmbeddingOptions.SectionName));

        services.AddOptions<KnowledgeIndexingOptions>()
            .Bind(configuration.GetSection(KnowledgeIndexingOptions.SectionName));

        services.AddOptions<GroundedContextRetrievalCacheOptions>()
            .Bind(configuration.GetSection(GroundedContextRetrievalCacheOptions.SectionName));

        var redisConnectionString = configuration[$"{ObservabilityOptions.SectionName}:Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "virtual-company:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<GroundedContextRetrievalCacheKeyBuilder>();
        services.AddSingleton<IGroundedContextRetrievalSectionCache, GroundedContextRetrievalSectionCache>();
        services.AddHttpClient(OpenAiCompatibleEmbeddingGenerator.ClientName);
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
        services.AddScoped<ICompanyDocumentService, CompanyDocumentService>();
        services.AddScoped<ICompanyDocumentIngestionStatusService, CompanyDocumentIngestionStatusService>();
        services.AddScoped<IDocumentIngestionOrchestrator, InlineCompanyDocumentIngestionOrchestrator>();
        services.AddScoped<ICompanyDocumentVirusScanner, NoOpCompanyDocumentVirusScanner>();
        services.AddScoped<ICompanyDocumentStorage, LocalCompanyDocumentStorage>();
        services.AddScoped<ICompanyDocumentTextExtractor, CompanyDocumentTextExtractor>();
        services.AddScoped<IKnowledgeChunker, DefaultKnowledgeChunker>();
        services.AddScoped<IEmbeddingGenerator, OpenAiCompatibleEmbeddingGenerator>();
        services.AddScoped<IKnowledgeAccessPolicyEvaluator, KnowledgeAccessPolicyEvaluator>();
        services.AddScoped<ICompanyKnowledgeIndexingProcessor, CompanyKnowledgeIndexingProcessor>();
        services.AddScoped<ICompanyKnowledgeSearchService, CompanyKnowledgeSearchService>();
        services.AddScoped<IRetrievalScopeEvaluator, RetrievalScopeEvaluator>();
        services.AddScoped<IGroundedContextPromptBuilder, GroundedContextPromptBuilder>();
        services.AddScoped<IGroundedPromptContextService, GroundedPromptContextService>();
        services.AddScoped<IGroundedContextRetrievalService, GroundedContextRetrievalService>();
        services.AddHostedService<CompanyKnowledgeIndexingBackgroundService>();
        services.AddTransient<IClaimsTransformation, UserClaimsTransformation>();
        services.AddScoped<IAgentRuntimeProfileResolver, PersistedAgentRuntimeProfileResolver>();
        services.AddScoped<ICompanyAgentService, CompanyAgentService>();
        services.AddScoped<CompanyMemoryService>();
        services.AddScoped<CompanyTaskService>();
        services.AddScoped<ICompanyTaskService, CompanyTaskService>();
        services.AddScoped<ICompanyTaskCommandService, CompanyTaskCommandService>();
        services.AddScoped<ICompanyTaskQueryService>(provider => provider.GetRequiredService<CompanyTaskService>());
        services.AddScoped<ICompanyMemoryService>(provider => provider.GetRequiredService<CompanyMemoryService>());
        services.AddScoped<IMemoryRetrievalService>(provider => provider.GetRequiredService<CompanyMemoryService>());
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
}
