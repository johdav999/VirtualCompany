using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Infrastructure.Marketing;
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
        services.AddScoped<ISalesCampaignDraftService, SalesCampaignDraftService>();
        services.AddScoped<IGuidedArtifactDefinition, SalesCampaignGuidedArtifactDefinition>();
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
        services.AddScoped<IMarketingStrategyService, MarketingStrategyService>();
        services.AddScoped<IGuidedArtifactDefinition, MarketingStrategyGuidedArtifactDefinition>();
        services.AddScoped<IGuidedArtifactDefinition, MarketingSegmentGuidedArtifactDefinition>();
        services.AddScoped<IGuidedArtifactDefinition, MarketingPlanGuidedArtifactDefinition>();
        services.AddScoped<IMarketingOperatingLoopService, MarketingOperatingLoopService>();
        services.AddScoped<IMarketingWorkNeedAssessment, MarketingWorkNeedAssessment>();
        services.AddScoped<IMarketingDeliveryService, MarketingDeliveryService>();
        services.AddScoped<IMarketingPolicyService, MarketingPolicyService>();
        services.AddSingleton<IMarketingChannelOAuthStateProtector, DataProtectionMarketingChannelOAuthStateProtector>();
        services.AddScoped<IMarketingChannelConnectionService, MarketingChannelConnectionService>();
        services.AddOptions<MarketingChannelOAuthOptions>()
            .Bind(configuration.GetSection(MarketingChannelOAuthOptions.SectionName));
        services.AddHttpClient(nameof(LinkedInMarketingOAuthAdapter), client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(nameof(MetaMarketingOAuthAdapter), client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(nameof(XMarketingOAuthAdapter), client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IMarketingChannelOAuthAdapter, LinkedInMarketingOAuthAdapter>();
        services.AddScoped<IMarketingChannelOAuthAdapter, MetaMarketingOAuthAdapter>();
        services.AddScoped<IMarketingChannelOAuthAdapter, XMarketingOAuthAdapter>();
        services.AddOptions<MarketingCreativeImageOptions>()
            .Bind(configuration.GetSection(MarketingCreativeImageOptions.SectionName))
            .PostConfigure(options =>
            {
                options.TimeoutSeconds = Math.Clamp(options.TimeoutSeconds, 30, 300);
                options.MaximumBytes = Math.Clamp(options.MaximumBytes, 1_000_000, 25 * 1024 * 1024);
            });
        services.AddHttpClient(OpenAiMarketingCreativeImageGenerator.ClientName);
        services.AddScoped<IMarketingCreativeImageGenerator, OpenAiMarketingCreativeImageGenerator>();
        services.AddOptions<MarketingAssetSafetyOptions>()
            .Bind(configuration.GetSection(MarketingAssetSafetyOptions.SectionName))
            .PostConfigure(options => options.TimeoutSeconds = Math.Clamp(options.TimeoutSeconds, 10, 180));
        services.AddHttpClient(HttpMarketingAssetSafetyScanner.ClientName, (provider, client) =>
        {
            var configured = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketingAssetSafetyOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(configured.TimeoutSeconds);
        });
        services.AddScoped<IMarketingAssetSafetyScanner, HttpMarketingAssetSafetyScanner>();
        services.AddOptions<MarketingChannelDeliveryOptions>()
            .Bind(configuration.GetSection(MarketingChannelDeliveryOptions.SectionName))
            .PostConfigure(options =>
            {
                options.PollSeconds = Math.Clamp(options.PollSeconds, 5, 300);
                options.BatchSize = Math.Clamp(options.BatchSize, 1, 100);
                options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
            });
        services.AddScoped<IMarketingChannelDispatchService, MarketingChannelDispatchService>();
        services.AddHttpClient(nameof(LinkedInMarketingChannelPublisher), client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(nameof(MetaMarketingChannelPublisher), client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddHttpClient(nameof(XMarketingChannelPublisher), client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IMarketingChannelPublisher, LinkedInMarketingChannelPublisher>();
        services.AddScoped<IMarketingChannelPublisher, MetaMarketingChannelPublisher>();
        services.AddScoped<IMarketingChannelPublisher, XMarketingChannelPublisher>();
        services.AddHostedService<MarketingChannelDispatchBackgroundService>();
        services.AddOptions<MarketingJourneyWorkerOptions>()
            .Bind(configuration.GetSection(MarketingJourneyWorkerOptions.SectionName))
            .PostConfigure(options => { options.PollSeconds = Math.Clamp(options.PollSeconds, 5, 300); options.BatchSize = Math.Clamp(options.BatchSize, 1, 100); });
        services.AddScoped<IMarketingJourneyExecutionService, MarketingJourneyExecutionService>();
        services.AddSingleton<IMarketingJourneyRuleEvaluator, MarketingJourneyRuleEvaluator>();
        services.AddScoped<IMarketingJourneyInboundEventService, MarketingJourneyInboundEventService>();
        services.AddScoped<IMarketingMeasurementService, MarketingMeasurementService>();
        services.AddScoped<IMarketingEventPublisher, MarketingEventPublisher>();
        services.AddScoped<IMarketingBriefingService, MarketingBriefingService>();
        services.AddScoped<MarketingEventScanner>();
        services.AddHostedService<MarketingEventScannerBackgroundService>();
        services.AddHostedService<MarketingJourneyBackgroundService>();
        services.AddSingleton<IMarketingChannelAdapter, LinkedInMarketingChannelAdapter>();
        services.AddSingleton<IMarketingChannelAdapter, MetaMarketingChannelAdapter>();
        services.AddSingleton<IMarketingChannelAdapter, XMarketingChannelAdapter>();
        services.AddScoped<IMarketingAgentAccessGuard, MarketingAgentAccessGuard>();
        services.AddScoped<IMarketingAgentAnalysisService, MarketingAgentAnalysisService>();
        services.AddScoped<IMarketingCompanyOrchestrationService, MarketingCompanyOrchestrationService>();
        services.AddScoped<ICompanyOperatingSnapshotContributor, SalesOperatingSnapshotContributor>();
        services.AddScoped<ICompanyOperatingSnapshotContributor, MarketingOperatingSnapshotContributor>();
        return services;
    }
}
