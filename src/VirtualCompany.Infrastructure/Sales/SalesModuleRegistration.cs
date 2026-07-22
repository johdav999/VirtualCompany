using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.CustomerMemory;

namespace VirtualCompany.Infrastructure.Sales;

internal static class SalesModuleRegistration
{
    public static IServiceCollection AddSalesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SequenceExecutionWorkerOptions>()
            .Bind(configuration.GetSection(SequenceExecutionWorkerOptions.SectionName));
        services.AddHostedService<SequenceExecutionBackgroundService>();
        services.AddHostedService<ProspectingRunBackgroundService>();
        services.AddOptions<CustomerMemoryOptions>()
            .Bind(configuration.GetSection(CustomerMemoryOptions.SectionName));
        services.AddScoped<ICustomerMemoryService, CustomerMemoryService>();
        services.AddScoped<IOutboundCampaignService, OutboundCampaignService>();
        services.AddScoped<ISequenceExecutionService, SequenceExecutionService>();
        services.AddScoped<IOutboundAutomationPolicyService, OutboundAutomationPolicyService>();
        services.AddScoped<IConversionAnalyticsService, ConversionAnalyticsService>();
        services.AddScoped<RevenueForecastService>();
        services.AddScoped<IRevenueForecastService>(provider => provider.GetRequiredService<RevenueForecastService>());
        services.AddScoped<IPipelineRiskScoringJobRunner>(provider => provider.GetRequiredService<RevenueForecastService>());
        services.AddScoped<IOutboundAutomationEnforcementService, OutboundAutomationEnforcementService>();
        services.AddScoped<IOutboundReviewQueueService, OutboundReviewQueueService>();
        services.AddScoped<IWebsiteLeadCaptureService, WebsiteLeadCaptureService>();
        services.AddScoped<IOutboundEmailSender, MailboxOutboundEmailSender>();
        services.AddScoped<ISalesEmailIngestionService, SalesEmailIngestionService>();
        services.AddScoped<ISalesEmailIntentExtractionService, SharedSalesEmailIntentExtractionService>();
        services.AddScoped<IReplySignalDetectionService, DeterministicReplySignalDetectionService>();
        services.AddScoped<IReplySignalDetectionPipeline, ReplySignalDetectionPipeline>();
        services.AddScoped<IDealIntelligenceSignalRepository, DealIntelligenceSignalRepository>();
        services.AddScoped<ISalesOperationsService, SalesOperationsService>();
        services.AddScoped<ILeadGenerationService, LeadGenerationService>();
        services.AddScoped<ISalesSourceService, SalesSourceService>();
        services.AddScoped<IProspectDataProvider, FirstPartyProspectDataProvider>();
        services.AddScoped<IProspectDataProviderRegistry, ProspectDataProviderRegistry>();
        services.AddScoped<ICrmLeadAdapterRegistry, CrmLeadAdapterRegistry>();
        services.AddSingleton<ISalesAutomationPolicyEvaluator, SalesAutomationPolicyEvaluator>();
        services.AddScoped<ISalesAgentAnalysisService, SalesAgentAnalysisService>();
        services.AddScoped<ISalesAgentDecisionService, SalesAgentDecisionService>();
        return services;
    }
}
