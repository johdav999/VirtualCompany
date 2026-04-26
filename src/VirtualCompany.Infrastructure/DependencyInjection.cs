using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Alerts;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Chat;
using StackExchange.Redis;
using VirtualCompany.Application.Focus;
using VirtualCompany.Application.ExecutionExceptions;
using VirtualCompany.Application.Escalations;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Insights;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Memory;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mobile;
using VirtualCompany.Application.Notifications;
using VirtualCompany.Application.ProactiveMessaging;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Activity;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Context;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Finance;
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
            ConfigureDatabase(options, connectionString, configuration["Database:Provider"]));

        services.AddOptions<CompanyDocumentOptions>()
            .Bind(configuration.GetSection(CompanyDocumentOptions.SectionName));

        services.AddOptions<CompanyOutboxDispatcherOptions>()
            .Bind(configuration.GetSection(CompanyOutboxDispatcherOptions.SectionName));

        services.AddOptions<FinanceToolProviderOptions>()
            .Bind(configuration.GetSection(FinanceToolProviderOptions.SectionName))
            .PostConfigure(options =>
            {
                options.Provider = string.IsNullOrWhiteSpace(options.Provider) ? FinanceToolProviderOptions.InternalProvider : options.Provider.Trim();
            });

        services.AddOptions<FinanceAnomalyDetectionOptions>()
            .Bind(configuration.GetSection(FinanceAnomalyDetectionOptions.SectionName));

        services.AddOptions<CompanySimulationOptions>()
            .Bind(configuration.GetSection(CompanySimulationOptions.SectionName))
            .PostConfigure(options =>
            {
                options.DefaultStepHours = Math.Clamp(options.DefaultStepHours, 1, 168);
                options.DefaultAutoAdvanceIntervalSeconds = Math.Max(0, options.DefaultAutoAdvanceIntervalSeconds);
            });
        services.AddOptions<SimulationFeatureOptions>()
            .Bind(configuration.GetSection(SimulationFeatureOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.DisabledMessage), "SimulationFeatures:DisabledMessage is required.")
            .PostConfigure(options =>
            {
                options.DisabledMessage = options.DisabledMessage.Trim();
            });
        services.AddOptions<CompanySimulationProgressionWorkerOptions>()
            .Bind(configuration.GetSection(CompanySimulationProgressionWorkerOptions.SectionName))
            .PostConfigure(options =>
            {
                options.PollIntervalMilliseconds = Math.Max(100, options.PollIntervalMilliseconds);
                options.BatchSize = Math.Max(1, options.BatchSize);
            });

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
        services.AddOptions<FinanceSeedWorkerOptions>()
            .Bind(configuration.GetSection(FinanceSeedWorkerOptions.SectionName))
            .PostConfigure(options => options.BatchSize = Math.Max(1, options.BatchSize));
        services.AddOptions<ReportingPeriodRegenerationWorkerOptions>()
            .Bind(configuration.GetSection(ReportingPeriodRegenerationWorkerOptions.SectionName))
            .PostConfigure(options => options.BatchSize = Math.Max(1, options.BatchSize))
            .PostConfigure(options => options.PollIntervalMilliseconds = Math.Max(100, options.PollIntervalMilliseconds));
        services.AddOptions<FinanceApprovalTaskBackfillWorkerOptions>()
            .Bind(configuration.GetSection(FinanceApprovalTaskBackfillWorkerOptions.SectionName))
            .PostConfigure(options => options.BatchSize = Math.Max(1, options.BatchSize))
            .PostConfigure(options => options.BackfillBatchSize = Math.Max(1, options.BackfillBatchSize))
            .PostConfigure(options => options.PollIntervalMilliseconds = Math.Max(100, options.PollIntervalMilliseconds));
        services.AddOptions<FinanceInsightsSnapshotWorkerOptions>()
            .Bind(configuration.GetSection(FinanceInsightsSnapshotWorkerOptions.SectionName))
            .PostConfigure(options =>
            {
                options.BatchSize = Math.Max(1, options.BatchSize);
                options.PollIntervalMilliseconds = Math.Max(100, options.PollIntervalMilliseconds);
            });
        services.AddOptions<FinanceInitializationOptions>()
            .Bind(configuration.GetSection(FinanceInitializationOptions.SectionName))
            .PostConfigure(options =>
            {
                options.MissingDatasetBehavior = FinanceMissingDatasetBehaviorValues.Normalize(options.MissingDatasetBehavior);
            });
        services.AddOptions<FinanceTransactionCreationOptions>()
            .Bind(configuration.GetSection(FinanceTransactionCreationOptions.SectionName));
        services.AddOptions<FinanceSeedBackfillWorkerOptions>()
            .Bind(configuration.GetSection(FinanceSeedBackfillWorkerOptions.SectionName))
            .PostConfigure(options =>
            {
                options.ScanPageSize = Math.Max(1, options.ScanPageSize);
                options.EnqueueBatchSize = Math.Max(1, options.EnqueueBatchSize);
                options.MaxConcurrentEnqueues = Math.Max(1, options.MaxConcurrentEnqueues);
                options.RateLimitCount = Math.Max(0, options.RateLimitCount);
                options.RateLimitWindowSeconds = Math.Max(0, options.RateLimitWindowSeconds);
                options.MaxRetries = Math.Max(0, options.MaxRetries);
                options.BaseRetryDelaySeconds = Math.Max(0, options.BaseRetryDelaySeconds);
                options.RetryBackoffMultiplier = options.RetryBackoffMultiplier < 1d ? 1d : options.RetryBackoffMultiplier;
                options.MaxRetryDelaySeconds = Math.Max(options.BaseRetryDelaySeconds, options.MaxRetryDelaySeconds);
            });

        services.AddOptions<KnowledgeChunkingOptions>()
            .Bind(configuration.GetSection(KnowledgeChunkingOptions.SectionName));

        services.AddOptions<KnowledgeEmbeddingOptions>()
            .Bind(configuration.GetSection(KnowledgeEmbeddingOptions.SectionName));

        services.AddOptions<KnowledgeIndexingOptions>()
            .Bind(configuration.GetSection(KnowledgeIndexingOptions.SectionName));

        services.AddOptions<GroundedContextRetrievalCacheOptions>()
            .Bind(configuration.GetSection(GroundedContextRetrievalCacheOptions.SectionName));

        services.AddOptions<ProactiveTaskCreationOptions>()
            .Bind(configuration.GetSection(ProactiveTaskCreationOptions.SectionName))
            .PostConfigure(options =>
                options.DeduplicationWindowSeconds = Math.Max(1, options.DeduplicationWindowSeconds));

        services.AddOptions<ExecutiveCockpitDashboardCacheOptions>()
            .Bind(configuration.GetSection(ExecutiveCockpitDashboardCacheOptions.SectionName))
            .Validate(options => options.TtlSeconds > 0 && options.WidgetTtlSeconds > 0, "Executive cockpit cache TTL values must be positive.")
            .PostConfigure(options =>
            {
                options.KeyPrefix = string.IsNullOrWhiteSpace(options.KeyPrefix)
                    ? "vc:executive-cockpit"
                    : options.KeyPrefix.Trim().TrimEnd(':');
                options.KeyVersion = string.IsNullOrWhiteSpace(options.KeyVersion)
                    ? "v1"
                    : options.KeyVersion.Trim().ToLowerInvariant();
            });
        services.AddOptions<CompanyDashboardBriefingSummaryService.DashboardBriefingSummaryOptions>()
            .Bind(configuration.GetSection(CompanyDashboardBriefingSummaryService.DashboardBriefingSummaryOptions.SectionName));

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
        services.AddSingleton<ExecutiveCockpitCacheKeyBuilder>();
        services.AddHttpClient(OpenAiCompatibleEmbeddingGenerator.ClientName);
        services.AddHttpClient(CompanyDashboardBriefingSummaryService.ClientName);
        services.AddHostedService<CompanyOutboxDispatcherBackgroundService>();
        services.AddVirtualCompanyObservability(configuration);
        services.AddOptions<WorkflowSchedulerOptions>()
            .Bind(configuration.GetSection(WorkflowSchedulerOptions.SectionName));
        services.AddOptions<WorkflowProgressionOptions>()
            .Bind(configuration.GetSection(WorkflowProgressionOptions.SectionName));
        services.AddOptions<AgentScheduledTriggerSchedulerOptions>()
            .Bind(configuration.GetSection(AgentScheduledTriggerSchedulerOptions.SectionName));
        services.AddOptions<TriggerWorkerOptions>()
            .Bind(configuration.GetSection(TriggerWorkerOptions.SectionName));
        services.AddScoped<IWorkflowSchedulerCoordinator, WorkflowSchedulerCoordinator>();
        services.AddHostedService<WorkflowSchedulerBackgroundService>();
        services.AddScoped<IWorkflowProgressionCoordinator, WorkflowProgressionCoordinator>();
        services.AddScoped<IWorkflowProgressionService, WorkflowProgressionService>();
        services.AddOptions<BriefingSchedulerOptions>().Bind(configuration.GetSection(BriefingSchedulerOptions.SectionName));
        services.AddOptions<BriefingUpdateJobWorkerOptions>().Bind(configuration.GetSection(BriefingUpdateJobWorkerOptions.SectionName));
        services.AddScoped<BriefingSchedulerCoordinator>();
        services.AddHostedService<BriefingSchedulerBackgroundService>();
        services.AddHostedService<BriefingUpdateJobBackgroundService>();
        services.AddHostedService<WorkflowProgressionBackgroundService>();
        services.AddHostedService<TriggerEvaluationBackgroundService>();
        services.AddHostedService<FinanceSeedBackfillBackgroundService>();
        services.AddHostedService<ReportingPeriodRegenerationBackgroundService>();
        services.AddHostedService<FinanceApprovalTaskBackfillBackgroundService>();
        services.AddHostedService<CompanySimulationProgressionBackgroundService>();
        services.AddHostedService<FinanceInsightsSnapshotBackgroundService>();
        services.AddHostedService<FinanceAnalyticsStartupRefreshBackgroundService>();
        services.AddHostedService<FinanceSeedBackgroundService>();

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
        services.AddScoped<ISignalEngine, CompanySignalEngine>();
        services.AddScoped<IDashboardFinanceSnapshotService, CompanyDashboardFinanceSnapshotService>();
        services.AddScoped<ICompanyInvitationDeliveryDispatcher, CompanyInvitationDeliveryDispatcher>();
        services.AddScoped<ICompanyNotificationDispatcher, CompanyNotificationDispatcher>();
        services.AddScoped<ICompanyInvitationSender, LoggingCompanyInvitationSender>();
        services.AddSingleton<IActivityEventSummaryFormatter, DefaultActivityEventSummaryFormatter>();
        services.AddScoped<IActivityEventStore, EfActivityEventStore>();
        services.AddScoped<IEntityLinkResolutionService, EfEntityLinkResolutionService>();
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
        services.AddSingleton<IDefaultAgentCommunicationProfileProvider, DefaultAgentCommunicationProfileProvider>();
        services.AddScoped<IAgentCommunicationProfileResolver, AgentCommunicationProfileResolver>();
        services.AddScoped<IAgentRuntimeProfileResolver, PersistedAgentRuntimeProfileResolver>();
        services.AddScoped<ICompanyAgentService, CompanyAgentService>();
        services.AddScoped<IAgentStatusAggregationService, AgentStatusAggregationService>();
        services.AddScoped<CompanyMemoryService>();
        services.AddScoped<CompanyTaskService>();
        services.AddScoped<ICompanyTaskService, CompanyTaskService>();
        services.AddScoped<IFocusEngine, CompanyFocusEngine>();
        services.AddScoped<IFocusCandidateSource, ApprovalFocusCandidateSource>();
        services.AddScoped<IFocusCandidateSource, TaskFocusCandidateSource>();
        services.AddScoped<IFocusCandidateSource, AlertAnomalyFocusCandidateSource>();
        services.AddScoped<IFocusCandidateSource, FinanceAlertFocusCandidateSource>();
        services.AddScoped<ITriggerToTaskMappingService, DefaultTriggerToTaskMappingService>();
        services.AddScoped<IProactiveTaskDuplicateDetector, EfProactiveTaskDuplicateDetector>();
        services.AddScoped<IProactiveTaskCreationService, ProactiveTaskCreationService>();
        services.AddScoped<ICompanyTaskCommandService, CompanyTaskCommandService>();
        services.AddSingleton<IScheduleExpressionValidator, CronosScheduleExpressionValidator>();
        services.AddSingleton<IScheduledTriggerNextRunCalculator, CronosScheduledTriggerNextRunCalculator>();
        services.AddSingleton<ISupportedPlatformEventTypeRegistry>(SupportedPlatformEventTypeRegistry.Instance);
        services.AddScoped<IAgentScheduledTriggerRepository, EfAgentScheduledTriggerRepository>();
        services.AddScoped<IAgentScheduledTriggerService, AgentScheduledTriggerService>();
        services.AddScoped<IAgentScheduledTriggerPollingService, AgentScheduledTriggerPollingService>();
        services.AddScoped<IAgentScheduledTriggerSchedulerCoordinator, AgentScheduledTriggerSchedulerCoordinator>();
        services.AddScoped<ITriggerExecutionAttemptRepository, EfTriggerExecutionAttemptRepository>();
        services.AddScoped<ITriggerExecutionPolicyChecker, AgentTriggerExecutionPolicyChecker>();
        services.AddScoped<ITriggerOrchestrationDispatcher, SingleAgentTriggerOrchestrationDispatcher>();
        services.AddScoped<ITriggerAuditEventWriter, TriggerAuditEventWriter>();
        services.AddScoped<ITriggerExecutionService, TriggerExecutionService>();
        services.AddScoped<ITriggerInitiatedOrchestrationService>(provider => provider.GetRequiredService<ITriggerExecutionService>());
        services.AddScoped<ITriggerEvaluationWorker, TriggerEvaluationWorker>();
        services.AddHostedService<AgentScheduledTriggerSchedulerBackgroundService>();
        services.AddSingleton<IConditionTriggerEvaluator, ConditionTriggerEvaluator>();
        services.AddScoped<IConditionTriggerEvaluationRepository, EfConditionTriggerEvaluationRepository>();
        services.AddScoped<IConditionMetricValueResolver, MissingConditionMetricValueResolver>();
        services.AddScoped<IConditionEntityFieldValueResolver, MissingConditionEntityFieldValueResolver>();
        services.AddScoped<IConditionTriggerEvaluationService, ConditionTriggerEvaluationService>();
        // Direct chat uses this facade for compatibility, but execution routes through ISingleAgentOrchestrationService.
        services.AddScoped<IDirectAgentChatOrchestrator, DirectAgentChatOrchestrator>();
        services.AddScoped<ICompanyDirectChatService, CompanyDirectChatService>();
        services.AddScoped<IPromptBuilder, StructuredPromptBuilder>();
        services.AddSingleton<ICommunicationStyleRuleChecker, CommunicationStyleRuleChecker>();
        services.AddScoped<IToolExecutor, AgentToolOrchestrationExecutor>();
        services.AddSingleton<IResponsibilityPolicyEvaluator, ResponsibilityPolicyEvaluator>();
        services.AddSingleton<IRequestedDomainClassifier, RequestedDomainClassifier>();
        services.AddSingleton<IResponsibilityPolicyEvaluator, ResponsibilityPolicyEvaluator>();
        services.AddSingleton<IRequestedDomainClassifier, RequestedDomainClassifier>();
        services.AddScoped<IOrchestrationAuditWriter, OrchestrationAuditWriter>();
        services.AddScoped<ISingleAgentOrchestrationResolver, SingleAgentOrchestrationResolver>();
        services.AddScoped<ISingleAgentOrchestrationService, SingleAgentOrchestrationService>();
        services.AddScoped<IMultiAgentCoordinator, MultiAgentCoordinator>();
        services.AddScoped<CompanyWorkflowDefinitionSeeder>();
        services.AddScoped<IApprovalRequestService, CompanyApprovalRequestService>();
        services.AddScoped<INotificationInboxService, CompanyNotificationService>();
        services.AddScoped<IExecutiveDashboardAggregateCache, ExecutiveDashboardAggregateCache>();
        services.AddScoped<IProactiveMessageService, CompanyProactiveMessageService>();
        services.AddScoped<IBriefingUpdateJobProducer, BriefingUpdateJobProducer>();
        services.AddScoped<IBriefingInsightAggregationService, BriefingInsightAggregationService>();
        services.AddScoped<IBriefingGenerationPipeline, CompanyBriefingGenerationPipeline>();
        services.AddScoped<IBriefingUpdateJobRunner, CompanyBriefingUpdateJobRunner>();
        services.AddScoped<ICompanyBriefingService, CompanyBriefingService>();
        services.AddScoped<IDashboardBriefingSummaryService, CompanyDashboardBriefingSummaryService>();
        services.AddScoped<CompanyWorkflowService>();
        services.AddScoped<ICompanyWorkflowService>(provider => provider.GetRequiredService<CompanyWorkflowService>());
        services.AddScoped<IMobileSummaryService, CompanyMobileSummaryService>();
        services.AddSingleton<IExecutiveCockpitDashboardCache, ExecutiveCockpitDashboardCache>();
        services.AddSingleton<IExecutiveCockpitDashboardCacheInvalidator>(provider => (IExecutiveCockpitDashboardCacheInvalidator)provider.GetRequiredService<IExecutiveCockpitDashboardCache>());
        services.AddScoped<InternalFinanceToolProvider>();
        services.AddScoped<MockFinanceToolProvider>();
        services.AddScoped<IFinanceCommandService, CompanyFinanceCommandService>();
        services.AddScoped<IFinanceAgentInsightRepository, FinanceAgentInsightRepository>();
        services.AddScoped<IFinanceInsightPersistenceService, FinanceInsightPersistenceService>();
        services.AddScoped<IFinancePaymentCommandService, CompanyFinanceCommandService>();
        services.AddScoped<IFinanceCashSettlementPostingService, CompanyCashSettlementPostingService>();
        services.AddScoped<IFinanceApprovalTaskService, CompanyFinanceApprovalTaskService>();
        services.AddScoped<ICashPostingTraceabilityBackfillService, CompanyCashPostingTraceabilityBackfillService>();
        services.AddScoped<IBankTransactionReadService, CompanyBankTransactionService>();
        services.AddScoped<IBankTransactionCommandService, CompanyBankTransactionService>();
        services.AddScoped<IFinancePolicyConfigurationService, CompanyFinanceCommandService>();
        services.AddScoped<IExecutiveCockpitDashboardService, CompanyExecutiveCockpitDashboardService>();
        services.AddScoped<ISignalEngine, CompanySignalEngine>();
        services.AddScoped<IExecutiveCockpitFinanceAdapter, CompanyExecutiveCockpitFinanceAdapter>();
        services.AddScoped<IFinanceSeedBootstrapService, CompanyFinanceSeedBootstrapService>();
        services.AddSingleton<IFinanceSeedTelemetry, FinanceSeedTelemetry>();
        services.AddScoped<IDashboardFinanceSnapshotService, CompanyDashboardFinanceSnapshotService>();
        services.AddScoped<IFinanceBootstrapRerunService, CompanyFinanceBootstrapRerunService>();
        services.AddScoped<IFinanceSeedingStateService, CompanyFinanceSeedingStateResolver>();
        services.AddScoped<IFinanceSeedBackfillOrchestrator, FinanceSeedBackfillOrchestrator>();
        services.AddScoped<FinanceSummaryConsistencyChecker>();
        services.AddScoped<IFinanceSummaryQueryService, CompanyFinanceSummaryQueryService>();
        services.AddScoped<IFinanceSeedBackfillQueryService, FinanceSeedBackfillQueryService>();
        services.AddScoped<IPlanningBaselineService, PlanningBaselineService>();
        services.AddScoped<IFinanceEntryService, CompanyFinanceEntryService>();
        services.AddScoped<IFinanceSeedJobRunner, CompanyFinanceSeedJobRunner>();
        services.AddScoped<IReportingPeriodCloseService, CompanyReportingPeriodCloseService>();
        services.AddScoped<IReportingPeriodRegenerationJobRunner, ReportingPeriodRegenerationJobRunner>();
        services.AddScoped<IFinanceApprovalTaskBackfillJobRunner, FinanceApprovalTaskBackfillJobRunner>();
        services.AddScoped<IFinanceInsightsSnapshotJobRunner, FinanceInsightsSnapshotJobRunner>();
        services.AddScoped<IFinanceMaintenanceService, CompanyFinanceMaintenanceService>();
        services.AddSingleton<IFinanceSeedBackfillExecutionScheduler, FinanceSeedBackfillExecutionScheduler>();
        services.AddSingleton<IFinanceSeedBackfillDelayStrategy, SystemFinanceSeedBackfillDelayStrategy>();
        services.AddScoped<IFinanceSeedingStateResolver, CompanyFinanceSeedingStateResolver>();
        services.AddScoped<IInvoiceReviewWorkflowService, CompanyInvoiceReviewWorkflowService>();
        services.AddScoped<IFinanceTransactionAnomalyDetectionService, CompanyFinanceTransactionAnomalyDetectionService>();
        services.AddScoped<IFinanceCashPositionWorkflowService, CompanyFinanceCashPositionWorkflowService>();
        services.AddScoped<IFinanceToolProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FinanceToolProviderOptions>>().Value;
            return options.Provider.Equals(FinanceToolProviderOptions.MockProvider, StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<MockFinanceToolProvider>()
                : provider.GetRequiredService<InternalFinanceToolProvider>();
        });
        services.AddSingleton<IFinanceWorkflowTriggerRegistry, StaticFinanceWorkflowTriggerRegistry>();
        services.AddScoped<IFinanceWorkflowTriggerService, FinanceWorkflowTriggerService>();
        services.AddScoped<IDepartmentDashboardConfigurationService, CompanyDepartmentDashboardConfigurationService>();
        services.AddScoped<IExecutiveCockpitKpiQueryService, CompanyExecutiveCockpitKpiQueryService>();
        services.AddScoped<IFinanceReadService, CompanyFinanceReadService>();
        services.AddScoped<IFinancePaymentReadService, CompanyFinanceReadService>();
        services.AddScoped<IWorkflowScheduleTriggerService>(provider => provider.GetRequiredService<CompanyWorkflowService>());
        services.AddScoped<IReconciliationScoringSettingsProvider, CompanyReconciliationScoringSettingsProvider>();
        services.AddScoped<IReconciliationScoringService, CompanyReconciliationScoringService>();
        services.AddScoped<IReconciliationSuggestionReadService, CompanyReconciliationSuggestionService>();
        services.AddScoped<IReconciliationSuggestionCommandService, CompanyReconciliationSuggestionService>();
        services.AddScoped<ExecutionExceptionService>();
        services.AddScoped<ICompanySimulationService, CompanySimulationService>();
        services.AddScoped<IExecutionExceptionRecorder>(provider => provider.GetRequiredService<ExecutionExceptionService>());
        services.AddScoped<ICompanySimulationStateRepository, EfCompanySimulationStateRepository>();
        services.AddSingleton<IFinanceDeterministicValueSource, Sha256FinanceDeterministicValueSource>();
        services.AddSingleton<IFinanceScenarioFactory, DefaultFinanceScenarioFactory>();
        services.AddSingleton<IFinanceAnomalyScheduleFactory, PeriodicFinanceAnomalyScheduleFactory>();
        services.AddScoped<IFinanceGenerationPolicy, CompanySimulationFinanceGenerationService>();
        services.AddScoped<IExecutionExceptionQueryService>(provider => provider.GetRequiredService<ExecutionExceptionService>());
        services.AddScoped<CompanyAlertService>();
        services.AddScoped<ICompanyAlertService>(provider => provider.GetRequiredService<CompanyAlertService>());
        services.AddScoped<IEscalationRepository, EfEscalationRepository>();
        services.AddScoped<IEscalationPolicyEvaluationService, EscalationPolicyEvaluationService>();
        services.AddScoped<IEscalationQueryService, EfEscalationQueryService>();
        services.AddScoped<IInternalWorkflowEventTriggerService>(provider => provider.GetRequiredService<CompanyWorkflowService>());
        services.AddScoped<IWorkflowSchedulePollingService, WorkflowSchedulePollingService>();
        services.AddScoped<ICompanyTaskQueryService>(provider => provider.GetRequiredService<CompanyTaskService>());
        services.AddScoped<ICompanyMemoryService>(provider => provider.GetRequiredService<CompanyMemoryService>());
        services.AddSingleton<ISimulationFeatureGate, ConfigurationSimulationFeatureGate>();
        services.AddScoped<CompanySimulationStateService>();
        services.AddScoped<IMemoryRetrievalService>(provider => provider.GetRequiredService<CompanyMemoryService>());
        services.AddScoped<IAgentAssignmentGuard, CompanyAgentAssignmentGuard>();
        services.AddScoped<ICompanySimulationStateService>(provider => provider.GetRequiredService<CompanySimulationStateService>());
        services.AddScoped<ICompanySimulationProgressionRunner, CompanySimulationProgressionRunner>();
        services.AddScoped<IAgentToolExecutionService, CompanyAgentToolExecutionService>();
        services.AddScoped<IPolicyGuardrailEngine, PolicyGuardrailEngine>();
        services.AddSingleton<ICompanyToolRegistry, StaticCompanyToolRegistry>();
        services.AddSingleton<IInsightScoringService, DefaultInsightScoringService>();
        services.AddSingleton<IActionDeepLinkResolver, DefaultActionDeepLinkResolver>();
        services.AddScoped<IActionInsightService, CompanyActionInsightService>();
        services.AddScoped<IInternalCompanyToolContract, InternalCompanyToolContract>();
        services.AddScoped<ICompanyToolExecutor, NoOpCompanyToolExecutor>();
        services.AddScoped<CompanyContextResolutionMiddleware>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipResourceAuthorizationHandler>();
        services.AddScoped<ICompanyMembershipContextResolver, CompanyMembershipContextResolver>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipRoleAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyMembershipRoleResourceAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyPermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyPermissionResourceAuthorizationHandler>();

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
                    sqlServerOptions => sqlServerOptions.EnableRetryOnFailure());
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

    private enum DatabaseProvider { SqlServer, PostgreSql, Sqlite }
}
