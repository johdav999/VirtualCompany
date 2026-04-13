using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Chat;
using StackExchange.Redis;
using VirtualCompany.Application.ExecutionExceptions;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Memory;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Notifications;
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

        services.AddOptions<RedisExecutionCoordinationOptions>()
            .Bind(configuration.GetSection(RedisExecutionCoordinationOptions.SectionName))
            .Validate(
                options => options.DefaultLockLeaseSeconds > 0 && options.DefaultExecutionStateTtlSeconds > 0,
                "Redis execution coordination TTL values must be positive.")
            .PostConfigure(options =>
            {
                options.KeyPrefix = string.IsNullOrWhiteSpace(options.KeyPrefix) ? "vc" : options.KeyPrefix.Trim();
            });

        services.AddOptions<BackgroundExecutionOptions>()
            .Bind(configuration.GetSection(BackgroundExecutionOptions.SectionName))
            .Configure(options => options.BaseRetryDelaySeconds = Math.Max(options.BaseRetryDelaySeconds, 0));

        services.AddOptions<KnowledgeChunkingOptions>()
            .Bind(configuration.GetSection(KnowledgeChunkingOptions.SectionName));

        services.AddOptions<KnowledgeEmbeddingOptions>()
            .Bind(configuration.GetSection(KnowledgeEmbeddingOptions.SectionName));

        services.AddOptions<KnowledgeIndexingOptions>()
            .Bind(configuration.GetSection(KnowledgeIndexingOptions.SectionName));

        services.AddOptions<GroundedContextRetrievalCacheOptions>()
            .Bind(configuration.GetSection(GroundedContextRetrievalCacheOptions.SectionName));

        services.AddOptions<ExecutiveCockpitDashboardCacheOptions>()
            .Bind(configuration.GetSection(ExecutiveCockpitDashboardCacheOptions.SectionName))
            .Validate(options => options.TtlSeconds > 0, "Executive cockpit dashboard cache TTL must be positive.")
            .PostConfigure(options =>
            {
                options.KeyPrefix = string.IsNullOrWhiteSpace(options.KeyPrefix)
                    ? "vc:executive-cockpit"
                    : options.KeyPrefix.Trim().TrimEnd(':');
                options.KeyVersion = string.IsNullOrWhiteSpace(options.KeyVersion)
                    ? "v1"
                    : options.KeyVersion.Trim().ToLowerInvariant();
            });

        services.AddOptions<MultiAgentCollaborationOptions>()
            .Bind(configuration.GetSection(MultiAgentCollaborationOptions.SectionName))
            .PostConfigure(options =>
            {
                options.MaxWorkers = options.MaxWorkers > 0 ? options.MaxWorkers : 3;
                options.MaxDepth = 1;
                options.MaxRuntimeSeconds = options.MaxRuntimeSeconds > 0 ? options.MaxRuntimeSeconds : 45;
                options.MaxTotalSteps = options.MaxTotalSteps > 0 ? options.MaxTotalSteps : 6;
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
            // Local/test fallback keeps the worker path runnable when Redis is intentionally absent.
            services.AddSingleton<InMemoryExecutionCoordinationService>();
            services.AddSingleton<IExecutionCoordinationStore>(provider => provider.GetRequiredService<InMemoryExecutionCoordinationService>());
            services.AddSingleton<IExecutionCoordinationKeyBuilder>(provider => provider.GetRequiredService<InMemoryExecutionCoordinationService>());
            services.AddSingleton<IDistributedLockProvider>(provider => provider.GetRequiredService<InMemoryExecutionCoordinationService>());
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<GroundedContextRetrievalCacheKeyBuilder>();
        services.AddSingleton<IGroundedContextRetrievalSectionCache, GroundedContextRetrievalSectionCache>();
        services.AddHttpClient(OpenAiCompatibleEmbeddingGenerator.ClientName);
        services.AddHostedService<CompanyOutboxDispatcherBackgroundService>();
        services.AddVirtualCompanyObservability(configuration);
        services.AddOptions<WorkflowSchedulerOptions>()
            .Bind(configuration.GetSection(WorkflowSchedulerOptions.SectionName));
        services.AddOptions<WorkflowProgressionOptions>()
            .Bind(configuration.GetSection(WorkflowProgressionOptions.SectionName));
        services.AddScoped<IWorkflowSchedulerCoordinator, WorkflowSchedulerCoordinator>();
        services.AddHostedService<WorkflowSchedulerBackgroundService>();
        services.AddScoped<IWorkflowProgressionCoordinator, WorkflowProgressionCoordinator>();
        services.AddScoped<IWorkflowProgressionService, WorkflowProgressionService>();
        services.AddOptions<BriefingSchedulerOptions>().Bind(configuration.GetSection(BriefingSchedulerOptions.SectionName));
        services.AddScoped<BriefingSchedulerCoordinator>();
        services.AddHostedService<BriefingSchedulerBackgroundService>();
        services.AddHostedService<WorkflowProgressionBackgroundService>();

        services.AddSingleton<IBackgroundJobFailureClassifier, DefaultBackgroundJobFailureClassifier>();
        services.AddSingleton<IBackgroundJobExecutor, BackgroundJobExecutor>();
        services.AddSingleton<IBackgroundExecutionRetryPolicy, ExponentialBackgroundExecutionRetryPolicy>();
        services.AddSingleton<IBackgroundExecutionIdentityFactory, DefaultBackgroundExecutionIdentityFactory>();
        services.AddScoped<IBackgroundExecutionRecorder, BackgroundExecutionRecorder>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICompanyContextAccessor, RequestCompanyContextAccessor>();
        services.AddScoped<ICompanyExecutionScopeFactory, CompanyExecutionScopeFactory>();
        services.AddScoped<ClaimsPrincipalExternalUserIdentityFactory>();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        services.AddScoped<IExternalUserIdentityAccessor, ClaimsExternalUserIdentityAccessor>();
        services.AddScoped<IExternalUserIdentityResolver, ExternalUserIdentityResolver>();
        services.AddScoped<CompanyQueryService>();
        services.AddScoped<ICompanyOutboxEnqueuer, CompanyOutboxEnqueuer>();
        services.AddScoped<ICompanyOutboxProcessor, CompanyOutboxProcessor>();
        services.AddScoped<ICompanyInvitationDeliveryDispatcher, CompanyInvitationDeliveryDispatcher>();
        services.AddScoped<ICompanyNotificationDispatcher, CompanyNotificationDispatcher>();
        services.AddScoped<ICompanyInvitationSender, LoggingCompanyInvitationSender>();
        services.AddScoped<IAuditEventWriter, AuditEventWriter>();
        services.AddScoped<IAuditQueryService, CompanyAuditQueryService>();
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
        // Direct chat uses this facade for compatibility, but execution routes through ISingleAgentOrchestrationService.
        services.AddScoped<IDirectAgentChatOrchestrator, DirectAgentChatOrchestrator>();
        services.AddScoped<ICompanyDirectChatService, CompanyDirectChatService>();
        services.AddScoped<IPromptBuilder, StructuredPromptBuilder>();
        services.AddScoped<IToolExecutor, AgentToolOrchestrationExecutor>();
        services.AddScoped<IOrchestrationAuditWriter, OrchestrationAuditWriter>();
        services.AddScoped<ISingleAgentOrchestrationResolver, SingleAgentOrchestrationResolver>();
        services.AddScoped<ISingleAgentOrchestrationService, SingleAgentOrchestrationService>();
        services.AddScoped<IMultiAgentCoordinator, MultiAgentCoordinator>();
        services.AddScoped<CompanyWorkflowDefinitionSeeder>();
        services.AddScoped<IApprovalRequestService, CompanyApprovalRequestService>();
        services.AddScoped<INotificationInboxService, CompanyNotificationService>();
        services.AddScoped<IExecutiveDashboardAggregateCache, ExecutiveDashboardAggregateCache>();
        services.AddScoped<ICompanyBriefingService, CompanyBriefingService>();
        services.AddScoped<CompanyWorkflowService>();
        services.AddScoped<ICompanyWorkflowService>(provider => provider.GetRequiredService<CompanyWorkflowService>());
        services.AddSingleton<IExecutiveCockpitDashboardCache, ExecutiveCockpitDashboardCache>();
        services.AddScoped<IExecutiveCockpitDashboardService, CompanyExecutiveCockpitDashboardService>();
        services.AddScoped<IWorkflowScheduleTriggerService>(provider => provider.GetRequiredService<CompanyWorkflowService>());
        services.AddScoped<ExecutionExceptionService>();
        services.AddScoped<IExecutionExceptionRecorder>(provider => provider.GetRequiredService<ExecutionExceptionService>());
        services.AddScoped<IExecutionExceptionQueryService>(provider => provider.GetRequiredService<ExecutionExceptionService>());
        services.AddScoped<IInternalWorkflowEventTriggerService>(provider => provider.GetRequiredService<CompanyWorkflowService>());
        services.AddScoped<IWorkflowSchedulePollingService, WorkflowSchedulePollingService>();
        services.AddScoped<ICompanyTaskQueryService>(provider => provider.GetRequiredService<CompanyTaskService>());
        services.AddScoped<ICompanyMemoryService>(provider => provider.GetRequiredService<CompanyMemoryService>());
        services.AddScoped<IMemoryRetrievalService>(provider => provider.GetRequiredService<CompanyMemoryService>());
        services.AddScoped<IAgentAssignmentGuard, CompanyAgentAssignmentGuard>();
        services.AddScoped<IAgentToolExecutionService, CompanyAgentToolExecutionService>();
        services.AddScoped<IPolicyGuardrailEngine, PolicyGuardrailEngine>();
        services.AddSingleton<ICompanyToolRegistry, StaticCompanyToolRegistry>();
        services.AddScoped<IInternalCompanyToolContract, InternalCompanyToolContract>();
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
