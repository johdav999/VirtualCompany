using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Support;
using VirtualCompany.Application.Orchestration;

namespace VirtualCompany.Infrastructure.Support;

public static class SupportModuleRegistration
{
    public static IServiceCollection AddSupportInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SupportOperationsWorkerOptions>(
            configuration.GetSection(SupportOperationsWorkerOptions.SectionName));
        services.AddHostedService<SupportOperationsBackgroundService>();
        services.AddScoped<ISupportCaseService, SupportCaseService>();
        services.AddScoped<ISupportMailboxIngestionService, SupportMailboxIngestionService>();
        services.AddScoped<ISupportContextResolutionService, SupportContextResolutionService>();
        services.AddScoped<ISupportTriageService, SupportTriageService>();
        services.AddScoped<ISupportOutboundEmailSender, SupportMailboxOutboundEmailSender>();
        services.AddScoped<ISupportReplyDeliveryDispatcher, SupportReplyDeliveryDispatcher>();
        services.AddScoped<ISupportKnowledgeContextProvider, SupportKnowledgeContextProvider>();
        services.AddScoped<ISupportMailboxRoutingService, SupportMailboxRoutingService>();
        services.AddScoped<ISupportReplySafetyPolicy, DeterministicSupportReplySafetyPolicy>();
        services.AddScoped<ISupportReplyDraftService, SupportReplyDraftService>();
        services.AddScoped<ISupportToolActionService, SupportToolActionService>();
        services.AddScoped<ISupportAgentOrchestrationService, SupportAgentOrchestrationService>();
        services.AddScoped<ISupportRefundWorkflowService, SupportRefundWorkflowService>();
        services.AddScoped<ISupportRefundApprovalOutcomeHandler, SupportRefundApprovalOutcomeHandler>();
        services.AddScoped<ISupportRefundFinanceService, SupportRefundFinanceService>();
        services.AddScoped<ISupportSlaMonitor, SupportSlaMonitor>();
        services.AddScoped<ISupportSlaPolicyService, SupportSlaPolicyService>();
        services.AddScoped<ISupportKnowledgeGapService, SupportKnowledgeGapService>();
        services.AddScoped<ISupportAnalyticsService, SupportAnalyticsService>();
        services.AddScoped<ISupportMemoryUpdateService, SupportMemoryUpdateService>();
        services.AddScoped<ISupportMemoryReviewService, SupportMemoryReviewService>();
        services.AddScoped<ISupportAgentAnalysisService, SupportAgentAnalysisService>();
        services.AddScoped<ISupportAgentDecisionService, SupportAgentDecisionService>();
        services.AddScoped<ICompanyOperatingSnapshotContributor, SupportOperatingSnapshotContributor>();
        return services;
    }
}
