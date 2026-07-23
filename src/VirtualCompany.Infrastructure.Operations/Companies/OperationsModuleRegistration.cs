using VirtualCompany.Application.CustomerMemory;
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
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Memory;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Communication;
using VirtualCompany.Application.Mobile;
using VirtualCompany.Application.Notifications;
using VirtualCompany.Application.ProactiveMessaging;
using VirtualCompany.Infrastructure.Security;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Activity;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Context;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Communication;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Memory;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public static class OperationsModuleRegistration
{
    public static IServiceCollection AddOperationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ICommunicationLanguageService, CommunicationLanguageService>();

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
        services.AddOptions<AgentBriefDraftOptions>()
            .Bind(configuration.GetSection(AgentBriefDraftOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
                }
            });
        services.AddHttpClient(OpenAiAgentBriefDraftService.ClientName);
        services.AddOptions<SharedAgentAiOptions>()
            .Bind(configuration.GetSection(SharedAgentAiOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
                }
            });
        services.AddHttpClient(SharedAgentReasoningGateway.ClientName);
        services.AddOptions<AgentMemoryCandidateExpiryOptions>()
            .Bind(configuration.GetSection(AgentMemoryCandidateExpiryOptions.SectionName));


        services.AddOptions<MultiAgentCollaborationOptions>()
            .Bind(configuration.GetSection(MultiAgentCollaborationOptions.SectionName))
            .PostConfigure(options =>
            {
                options.MaxWorkers = options.MaxWorkers > 0 ? options.MaxWorkers : 3;
                options.MaxDepth = 1;
                options.MaxRuntimeSeconds = options.MaxRuntimeSeconds > 0 ? options.MaxRuntimeSeconds : 45;
                options.MaxTotalSteps = options.MaxTotalSteps > 0 ? options.MaxTotalSteps : 6;
            });

        services.AddSingleton<GroundedContextRetrievalCacheKeyBuilder>();
        services.AddSingleton<IGroundedContextRetrievalSectionCache, GroundedContextRetrievalSectionCache>();
        services.AddSingleton<ExecutiveCockpitCacheKeyBuilder>();
        services.AddHttpClient(OpenAiCompatibleEmbeddingGenerator.ClientName);
        services.AddHttpClient(CompanyDashboardBriefingSummaryService.ClientName);
        services.AddHostedService<CompanyOutboxDispatcherBackgroundService>();
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
        services.AddOptions<RoleAgentCadenceOptions>().Bind(configuration.GetSection(RoleAgentCadenceOptions.SectionName));
        services.AddHostedService<RoleAgentCadenceBackgroundService>();
        services.AddHostedService<BriefingUpdateJobBackgroundService>();
        services.AddHostedService<WorkflowProgressionBackgroundService>();
        services.AddHostedService<TriggerEvaluationBackgroundService>();

        services.AddScoped<CompanyQueryService>();
        services.AddScoped<ICompanyOutboxEnqueuer, CompanyOutboxEnqueuer>();
        services.AddScoped<ICompanyOutboxProcessor, CompanyOutboxProcessor>();
        services.AddScoped<ISignalEngine, CompanySignalEngine>();
        services.AddScoped<ICompanyInvitationDeliveryDispatcher, CompanyInvitationDeliveryDispatcher>();
        services.AddScoped<ICompanyNotificationDispatcher, CompanyNotificationDispatcher>();
        services.AddScoped<ICompanyInvitationSender, LoggingCompanyInvitationSender>();
        services.AddSingleton<IActivityEventSummaryFormatter, DefaultActivityEventSummaryFormatter>();
        services.AddScoped<IActivityEventStore, EfActivityEventStore>();
        services.AddScoped<IEntityLinkResolutionService, EfEntityLinkResolutionService>();
        services.AddScoped<ICurrentUserCompanyService>(provider => provider.GetRequiredService<CompanyQueryService>());
        services.AddScoped<ICompanyNoteService>(provider => provider.GetRequiredService<CompanyQueryService>());
        services.AddScoped<ICompanyMembershipAdministrationService, CompanyMembershipAdministrationService>();
        services.AddScoped<CompanySetupTemplateSeeder>();
        services.AddScoped<ICoreCompanyAgentSeeder, CoreCompanyAgentSeeder>();
        services.AddScoped<ICompanyOnboardingService, CompanyOnboardingService>();
        services.AddScoped<ICompanyDocumentService, CompanyDocumentService>();
        services.AddScoped<ICompanyDocumentIngestionStatusService, CompanyDocumentIngestionStatusService>();
        services.AddScoped<IDocumentIngestionOrchestrator, InlineCompanyDocumentIngestionOrchestrator>();
        services.AddScoped<ICompanyDocumentVirusScanner, NoOpCompanyDocumentVirusScanner>();
        services.AddScoped<ICompanyDocumentStorage, LocalCompanyDocumentStorage>();
        services.AddScoped<ICompanyDocumentTextExtractor, CompanyDocumentTextExtractor>();
        services.AddScoped<IKnowledgeChunker, DefaultKnowledgeChunker>();
        services.AddScoped<IDocumentExtractionService, DocumentExtractionService>();
        services.AddScoped<IBillInformationExtractor, BillInformationExtractor>();
        services.AddScoped<IEmbeddingGenerator, OpenAiCompatibleEmbeddingGenerator>();
        services.AddScoped<IKnowledgeAccessPolicyEvaluator, KnowledgeAccessPolicyEvaluator>();
        services.AddScoped<ICompanyKnowledgeIndexingProcessor, CompanyKnowledgeIndexingProcessor>();
        services.AddScoped<ICompanyKnowledgeSearchService, CompanyKnowledgeSearchService>();
        services.AddScoped<IRetrievalScopeEvaluator, RetrievalScopeEvaluator>();
        services.AddScoped<IGroundedContextPromptBuilder, GroundedContextPromptBuilder>();
        services.AddScoped<IGroundedPromptContextService, GroundedPromptContextService>();
        services.AddScoped<IGroundedContextRetrievalService, GroundedContextRetrievalService>();
        services.AddHostedService<CompanyKnowledgeIndexingBackgroundService>();
        services.AddSingleton<IDefaultAgentCommunicationProfileProvider, DefaultAgentCommunicationProfileProvider>();
        services.AddScoped<IAgentCommunicationProfileResolver, AgentCommunicationProfileResolver>();
        services.AddScoped<IAgentRuntimeProfileResolver, PersistedAgentRuntimeProfileResolver>();
        services.AddScoped<ICompanyAgentService, CompanyAgentService>();
        services.AddScoped<IAgentCapabilityCatalog, AgentCapabilityCatalog>();
        services.AddScoped<IAgentReasoningGateway, SharedAgentReasoningGateway>();
        services.AddScoped<IAgentQuestionAnsweringService, AgentQuestionAnsweringService>();
        services.AddScoped<IAgentRoleBriefingService, AgentRoleBriefingService>();
        services.AddScoped<IAgentWorkPrioritizationService, AgentWorkPrioritizationService>();
        services.AddScoped<IAgentPlanningService, AgentPlanningService>();
        services.AddScoped<IAgentExceptionInterpretationService, AgentExceptionInterpretationService>();
        services.AddScoped<IAgentHandoffService, AgentHandoffService>();
        services.AddScoped<IAgentMemoryCandidateService, AgentMemoryCandidateService>();
        services.AddScoped<IAgentAiQualityService, AgentAiQualityService>();
        services.AddHostedService<AgentMemoryCandidateExpiryWorker>();
        services.AddScoped<IAgentBriefDraftService, OpenAiAgentBriefDraftService>();
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
        services.AddScoped<IExecutiveCockpitDashboardService, CompanyExecutiveCockpitDashboardService>();
        services.AddScoped<IAgentStaffOverviewQueryService, CompanyAgentStaffOverviewQueryService>();

        services.AddScoped<IDepartmentDashboardConfigurationService, CompanyDepartmentDashboardConfigurationService>();
        services.AddScoped<IExecutiveCockpitKpiQueryService, CompanyExecutiveCockpitKpiQueryService>();
        services.AddScoped<IWorkflowScheduleTriggerService>(provider => provider.GetRequiredService<CompanyWorkflowService>());
        services.AddScoped<ExecutionExceptionService>();
        services.AddScoped<IExecutionExceptionRecorder>(provider => provider.GetRequiredService<ExecutionExceptionService>());
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
        services.AddScoped<IMemoryRetrievalService>(provider => provider.GetRequiredService<CompanyMemoryService>());
        services.AddScoped<IAgentAssignmentGuard, CompanyAgentAssignmentGuard>();
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

}

