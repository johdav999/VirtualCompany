using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.CustomerMemory;

namespace VirtualCompany.Infrastructure.Sales;

public static class SalesModuleRegistration
{
    public static IServiceCollection AddSalesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SequenceExecutionWorkerOptions>()
            .Bind(configuration.GetSection(SequenceExecutionWorkerOptions.SectionName));
        services.AddOptions<CampaignSchedulingWorkerOptions>()
            .Bind(configuration.GetSection(CampaignSchedulingWorkerOptions.SectionName));
        services.AddOptions<PipelineRiskScoringWorkerOptions>()
            .Bind(configuration.GetSection(PipelineRiskScoringWorkerOptions.SectionName))
            .PostConfigure(options => options.RunIntervalHours = Math.Max(1, options.RunIntervalHours));
        services.AddHostedService<SequenceExecutionBackgroundService>();
        services.AddHostedService<CampaignSchedulingBackgroundService>();
        services.AddHostedService<ProspectingRunBackgroundService>();
        services.AddHostedService<PipelineRiskScoringBackgroundService>();
        services.AddOptions<CustomerMemoryOptions>()
            .Bind(configuration.GetSection(CustomerMemoryOptions.SectionName));
        services.AddScoped<ICustomerMemoryService, CustomerMemoryService>();
        services.AddScoped<IOutboundCampaignService, OutboundCampaignService>();
        services.AddScoped<ICampaignPlanningService, CampaignPlanningService>();
        services.AddScoped<ICampaignSchedulingCoordinator, CampaignSchedulingCoordinator>();
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
        services.AddScoped<ISalesLeadEmailEvidenceService, SalesLeadEmailEvidenceService>();
        services.AddScoped<ISalesEmailIntentExtractionService, SharedSalesEmailIntentExtractionService>();
        services.AddScoped<IReplySignalDetectionService, DeterministicReplySignalDetectionService>();
        services.AddScoped<IReplySignalDetectionPipeline, ReplySignalDetectionPipeline>();
        services.AddScoped<IDealIntelligenceSignalRepository, DealIntelligenceSignalRepository>();
        services.AddScoped<ISalesPersistenceRepository, SalesPersistenceRepository>();
        services.AddScoped<ISalesOperationsService, SalesOperationsService>();
        services.AddHttpClient(GoogleCalendarProviderClient.ClientName);
        services.AddHttpClient(Microsoft365CalendarProviderClient.ClientName);
        services.AddScoped<GoogleCalendarProviderClient>();
        services.AddScoped<Microsoft365CalendarProviderClient>();
        services.AddScoped<ICalendarProviderClient>(provider => provider.GetRequiredService<GoogleCalendarProviderClient>());
        services.AddScoped<ICalendarProviderClient>(provider => provider.GetRequiredService<Microsoft365CalendarProviderClient>());
        services.AddScoped<ICalendarProviderRegistry, CalendarProviderRegistry>();
        services.AddScoped<ISalesMeetingSchedulingService, SalesMeetingSchedulingService>();
        services.AddScoped<ISalesMeetingInvitationDeliveryDispatcher, SalesMeetingInvitationDeliveryDispatcher>();
        services.AddScoped<ISalesMeetingChangeDeliveryDispatcher, SalesMeetingChangeDeliveryDispatcher>();
        services.AddScoped<ISalesMeetingConfirmationDeliveryDispatcher, SalesMeetingConfirmationDeliveryDispatcher>();
        services.AddScoped<ILeadGenerationService, LeadGenerationService>();
        services.AddScoped<IIcpSuggestionService, IcpSuggestionService>();
        services.AddScoped<ISalesSourceService, SalesSourceService>();
        services.AddScoped<IProspectDataProvider, FirstPartyProspectDataProvider>();
        services.AddScoped<IProspectDataProviderRegistry, ProspectDataProviderRegistry>();
        services.AddScoped<ICrmLeadAdapterRegistry, CrmLeadAdapterRegistry>();
        services.AddSingleton<ISalesAutomationPolicyEvaluator, SalesAutomationPolicyEvaluator>();
        services.AddScoped<ISalesAgentAnalysisService, SalesAgentAnalysisService>();
        services.AddScoped<ISalesAgentDecisionService, SalesAgentDecisionService>();
        services.AddScoped<IMarketingOperationsService, MarketingOperationsService>();
        services.AddScoped<IMarketingAgentAnalysisService, MarketingAgentAnalysisService>();
        return services;
    }
}
