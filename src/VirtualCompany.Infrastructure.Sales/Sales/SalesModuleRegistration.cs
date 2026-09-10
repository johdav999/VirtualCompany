using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Cockpit;
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
        services.AddSalesRoomMedia(configuration);
        services.AddOptions<SalesNarrationOptions>().Bind(configuration.GetSection("SalesNarration"));
        services.AddScoped<SalesNarrationService>();
        services.AddScoped<ISalesNarrationService>(p => p.GetRequiredService<SalesNarrationService>());
        services.AddScoped<SalesNarrationWorker>();
        services.AddHostedService<SalesNarrationBackgroundService>();
        services.AddOptions<SalesRoomAgentOptions>().Bind(configuration.GetSection(SalesRoomAgentOptions.SectionName))
            .Validate(x => x.LeaseSeconds is >= 15 and <= 120 && x.RenewalSeconds is >= 5 and <= 60 &&
                x.RenewalSeconds < x.LeaseSeconds && x.PreRollMilliseconds is >= 200 and <= 300 &&
                x.TrailingSilenceMilliseconds is >= 500 and <= 800 && x.MinimumSpeechMilliseconds is >= 40 and <= 300 &&
                x.MaximumUtteranceSeconds is >= 5 and <= 60 && x.MaximumInputAudioSeconds is >= 60 and <= 7200 &&
                x.MaximumOutputAudioSeconds is >= 60 and <= 7200 && x.MaximumSessionMinutes is >= 1 and <= 120 &&
                x.PlaybackStopAcknowledgementTimeoutMilliseconds is >= 100 and <= 5000 &&
                x.OrganizerDisconnectGraceSeconds is >= 0 and <= 60 &&
                x.ReconciliationIntervalSeconds is >= 2 and <= 60 &&
                x.SpeechRmsThreshold is >= 50 and <= 5000 && x.SpeechPeakThreshold is >= 100 and <= 10000 &&
                SalesRoomOperationsPolicy.ConfigurationProblem(x, DateTime.UtcNow) is null,
                "Browser room agent limits are outside supported safety boundaries.").ValidateOnStart();
        services.AddScoped<SalesRoomAgentWorker>();
        services.AddScoped<ISalesRoomAgentService, SalesRoomAgentService>();
        services.AddSingleton<SalesRoomAgentCoordinator>();
        services.AddSingleton<ISalesRoomAgentCommandSink>(p => p.GetRequiredService<SalesRoomAgentCoordinator>());
        services.AddHostedService(p => p.GetRequiredService<SalesRoomAgentCoordinator>());
        services.AddHealthChecks().AddCheck<SalesBrowserRoomHealthCheck>("sales-browser-room", tags: ["ready"]);
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
        services.AddOptions<DemoScenarioOptions>()
            .Bind(configuration.GetSection(DemoScenarioOptions.SectionName));
        services.AddSingleton<IDemoScenarioCatalog, DemoScenarioCatalog>();
        services.AddScoped<IDemoTenantExternalSideEffectPolicy, DemoTenantExternalSideEffectPolicy>();
        services.AddScoped<IDemoScenarioService, DemoScenarioService>();
        services.AddHttpClient(GoogleCalendarProviderClient.ClientName);
        services.AddHttpClient(Microsoft365CalendarProviderClient.ClientName);
        services.AddScoped<GoogleCalendarProviderClient>();
        services.AddScoped<Microsoft365CalendarProviderClient>();
        services.AddScoped<ICalendarProviderClient>(provider => provider.GetRequiredService<GoogleCalendarProviderClient>());
        services.AddScoped<ICalendarProviderClient>(provider => provider.GetRequiredService<Microsoft365CalendarProviderClient>());
        services.AddScoped<ICalendarProviderRegistry, CalendarProviderRegistry>();
        services.AddScoped<ISalesMeetingSchedulingService, SalesMeetingSchedulingService>();
        services.AddScoped<ISalesMeetingSessionService, SalesMeetingSessionService>();
        services.AddScoped<ISalesMeetingPresentationPreparationQuery, SalesMeetingPresentationPreparationQuery>();
        services.AddOptions<SalesPresentationOptions>()
            .Bind(configuration.GetSection(SalesPresentationOptions.SectionName))
            .Validate(options => options.MaximumUploadBytes is >= 1_000_000 and <= 100_000_000 &&
                                 options.MaximumSlides is >= 1 and <= 500 &&
                                 options.RenderWidthPixels is >= 640 and <= 4096 &&
                                 options.RenderHeightPixels is >= 360 and <= 2160 &&
                                 options.BatchSize is >= 1 and <= 20 &&
                                 options.PollIntervalSeconds is >= 2 and <= 300 &&
                                 options.ClaimTimeoutSeconds is >= 30 and <= 3600 &&
                                 options.MaximumAttempts is >= 1 and <= 10 &&
                                 options.ReasoningBatchSize is >= 1 and <= 40,
                "Sales presentation processing limits are outside supported safety boundaries.")
            .ValidateOnStart();
        services.AddScoped<ISalesPresentationDeckService, SalesPresentationDeckService>();
        services.AddScoped<ISalesPresentationDeckProcessor, SalesPresentationDeckProcessor>();
        services.AddScoped<ISalesPresentationRuntimeService, SalesPresentationRuntimeService>();
        services.AddScoped<ISalesBrowserPresentationService, SalesBrowserPresentationService>();
        var conductorSection = configuration.GetSection(SalesPresentationConductorOptions.SectionName);
        var conductorOptions = services.AddOptions<SalesPresentationConductorOptions>()
            .Bind(conductorSection)
            .Validate(options => options.RenderTimeoutMilliseconds is >= 100 and <= 30_000 &&
                                 options.MaximumConsecutiveSlideTransitions is >= 1 and <= 20 &&
                                 options.MinimumSlideDwellSeconds is >= 0 and <= 300,
                "Sales presentation conductor limits are outside supported safety boundaries.")
            .ValidateOnStart();
        if (!conductorSection.Exists())
            conductorOptions.Configure<IOptions<TeamsPresenterOptions>>((options, teams) =>
            {
                options.RenderTimeoutMilliseconds = teams.Value.StageRenderTimeoutMilliseconds;
                options.MaximumConsecutiveSlideTransitions = teams.Value.MaximumConsecutiveSlideTransitions;
                options.MinimumSlideDwellSeconds = teams.Value.MinimumSlideDwellSeconds;
            });
        services.AddSingleton<ISalesPresentationNarrationPreemption, SalesPresentationNarrationPreemption>();
        services.AddSingleton<ISalesPresentationStagePresenceService, SalesPresentationStagePresenceService>();
        services.AddScoped<ISalesMeetingPresentationConductor, SalesMeetingPresentationConductor>();
        services.AddScoped<ISalesMeetingCaptureService, SalesMeetingCaptureService>();
        services.AddScoped<ISalesMeetingQuestionAnsweringService, SalesMeetingQuestionAnsweringService>();
        services.AddOptions<SalesMeetingVoiceOptions>()
            .Bind(configuration.GetSection(SalesMeetingVoiceOptions.SectionName))
            .Validate(x => x.MaximumSessionMinutes is >= 1 and <= 120 &&
                           x.MaximumReconnects is >= 0 and <= 10 &&
                           x.MaximumAudioSeconds is >= 60 and <= 7200 &&
                           x.MaximumInputTokens is >= 1000 and <= 1_000_000 &&
                           x.MaximumOutputTokens is >= 1000 and <= 250_000 &&
                           !string.IsNullOrWhiteSpace(x.MediaRoute) && x.MediaRoute.Length <= 64,
                "Sales meeting voice limits are outside supported safety boundaries.")
            .ValidateOnStart();
        services.AddSingleton<IMeetingMediaAdapter, ConfiguredMeetingMediaAdapter>();
        services.AddScoped<ISalesMeetingRealtimeService, SalesMeetingRealtimeService>();
        services.AddHealthChecks().AddCheck<SalesMeetingVoiceHealthCheck>("sales-meeting-voice", tags: ["ready"]);
        services.AddOptions<TeamsPresenterOptions>()
            .Bind(configuration.GetSection(TeamsPresenterOptions.SectionName))
            .Validate(x => x.MaxActiveCallsPerCompany is >= 1 and <= 100 &&
                           x.MaxActiveCallsPerHost is >= 1 and <= 1000 &&
                           x.ProviderRequestTimeoutSeconds is >= 2 and <= 120 &&
                           x.CallbackMaxBytes is >= 1024 and <= 1_000_000 &&
                           x.ReconciliationDelaySeconds is >= 1 and <= 300 &&
                           x.MediaBufferFrames is >= 5 and <= 500 &&
                           x.MaximumMediaFrameBytes is >= 640 and <= 4096 &&
                           x.MaximumMediaReconnects is >= 0 and <= 10 &&
                           x.StageRenderTimeoutMilliseconds is >= 250 and <= 10_000 &&
                           x.StageAccessMinutes is >= 5 and <= 720 &&
                           x.MaximumConsecutiveSlideTransitions is >= 1 and <= 50 &&
                           x.MinimumSlideDwellSeconds is >= 0 and <= 60 &&
                           x.MaximumSlideDwellSeconds is >= 30 and <= 3600 &&
                           x.MaximumMediaMinutes is >= 1 and <= 120 &&
                           x.MediaSdkMaximumAgeDays is >= 30 and <= 120 &&
                           x.MediaHostDrainMinutes is >= 1 and <= 120 &&
                           x.MediaHostReconciliationSeconds is >= 1 and <= 30 &&
                           x.MediaHostInternalPort is >= 1 and <= 65535 &&
                           x.MediaHostPublicPort is >= 1 and <= 65535 &&
                           x.MaxActiveCallsGlobal is >= 1 and <= 10_000 &&
                           x.MonthlyCostUsed >= 0 && x.MonthlyCostLimit >= 0 &&
                           !string.IsNullOrWhiteSpace(x.CostCurrency) && x.CostCurrency.Length <= 8,
                "Teams call-control limits are outside supported safety boundaries.")
            .ValidateOnStart();
        services.AddSingleton<MicrosoftTeamsAudioSocketPlatform>();
        services.AddHttpClient(AzureTeamsVmssInstanceProtection.ClientName, client =>
        {
            client.BaseAddress = new Uri("https://management.azure.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("azure-instance-metadata", client =>
        {
            client.BaseAddress = new Uri("http://169.254.169.254/");
            client.Timeout = TimeSpan.FromSeconds(2);
        });
        services.AddSingleton<ITeamsVmssInstanceProtection, AzureTeamsVmssInstanceProtection>();
        services.AddSingleton<ITeamsMediaHostPrerequisiteProbe, TeamsMediaHostPrerequisiteProbe>();
        services.AddSingleton<ITeamsMediaHostRuntime, TeamsMediaHostRuntime>();
        services.AddScoped<ITeamsMediaHostDrainExecutor, TeamsMediaHostDrainExecutor>();
        services.AddHostedService<TeamsMediaHostLifecycleService>();
        services.AddSingleton<ITeamsAudioSocketPlatform>(provider => provider.GetRequiredService<MicrosoftTeamsAudioSocketPlatform>());
        services.AddSingleton<ITeamsCallMediaPreparationProvider>(provider => provider.GetRequiredService<MicrosoftTeamsAudioSocketPlatform>());
        services.AddScoped<ITeamsMeetingMediaBindingAuthorizer, TeamsMeetingMediaBindingAuthorizer>();
        services.AddSingleton<TeamsApplicationHostedMediaAdapter>();
        services.AddSingleton<ITeamsMeetingMediaAdapter>(provider => provider.GetRequiredService<TeamsApplicationHostedMediaAdapter>());
        services.AddSingleton<ITeamsMeetingMediaIngress>(provider => provider.GetRequiredService<TeamsApplicationHostedMediaAdapter>());
        services.AddSingleton<ITeamsRealtimeAudioBridge, TeamsRealtimeAudioBridge>();
        services.AddSingleton<ITeamsMeetingMediaCoordinator, TeamsMeetingMediaCoordinator>();
        services.AddSingleton<ITeamsPresenterPackageBuilder, TeamsPresenterPackageBuilder>();
        services.AddSingleton<ITeamsCredentialTokenSource, AzureTeamsCredentialTokenSource>();
        services.AddSingleton<ITeamsAppOnlyTokenProvider, AzureTeamsAppOnlyTokenProvider>();
        services.AddSingleton<ITeamsCallbackTokenValidator, TeamsCallbackTokenValidator>();
        services.AddScoped<ITeamsCallbackAuthenticator, TeamsCallbackAuthenticator>();
        services.AddScoped<ITeamsTenantRegistrationService, TeamsTenantRegistrationService>();
        services.AddScoped<ITeamsPresenterRolloutPolicy, TeamsPresenterRolloutPolicy>();
        services.AddScoped<ITeamsMeetingPresenterService, TeamsMeetingPresenterService>();
        services.AddScoped<IFirstTeamsUatService, FirstTeamsUatService>();
        services.AddScoped<ITeamsMediaHostUpgradeReadiness, TeamsMediaHostUpgradeReadiness>();
        services.AddScoped<ITeamsOrganizerExperienceService, TeamsOrganizerExperienceService>();
        services.AddScoped<ITeamsCallControlService, TeamsCallControlService>();
        services.AddScoped<ITeamsCallControlDispatcher, TeamsCallControlDispatcher>();
        services.AddScoped<ITeamsCallCallbackReceiver, TeamsCallCallbackReceiver>();
        services.AddScoped<ISalesPresentationStageAccessService, SalesPresentationStageAccessService>();
        services.AddHttpClient(MicrosoftGraphTeamsCallControlAdapter.ClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TeamsPresenterOptions>>().Value;
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            client.Timeout = TimeSpan.FromSeconds(options.ProviderRequestTimeoutSeconds);
        });
        services.AddScoped<ITeamsCallControlAdapter, MicrosoftGraphTeamsCallControlAdapter>();
        services.AddScoped<ITeamsPresenterReadinessService, TeamsPresenterReadinessService>();
        services.AddHostedService<TeamsPresenterStartupValidationService>();
        services.AddHealthChecks().AddCheck<TeamsPresenterHealthCheck>("teams-presenter");
        services.AddHealthChecks().AddCheck<TeamsCallControlHealthCheck>("teams-call-control", tags: ["ready"]);
        services.AddHealthChecks().AddCheck<TeamsMeetingMediaHealthCheck>("teams-meeting-media", tags: ["ready"]);
        services.AddHealthChecks().AddCheck<TeamsMediaHostHealthCheck>("teams-media-host", tags: ["ready"]);
        services.AddScoped<ISalesMeetingClosingService, SalesMeetingClosingService>();
        services.AddSingleton<ISalesMeetingChangePolicy, SalesMeetingChangePolicy>();
        services.AddScoped<ISalesMeetingCanonicalChangeCommandHandler, SalesMeetingCanonicalChangeCommandHandler>();
        services.AddScoped<ISalesMeetingChangeProposalService, SalesMeetingChangeProposalService>();
        services.AddScoped<ISalesMeetingCustomerMinutesDeliveryDispatcher, SalesMeetingCustomerMinutesDeliveryDispatcher>();
        services.AddOptions<SalesMeetingTranscriptOptions>()
            .Bind(configuration.GetSection(SalesMeetingTranscriptOptions.SectionName))
            .Validate(x => !x.Enabled || x.SubscriptionLifetimeHours is >= 1 and <= 71 &&
                           x.RenewalLeadMinutes is >= 30 and <= 1440 &&
                           x.RenewalPollMinutes is >= 5 and <= 180 &&
                           x.MaximumNotificationsPerRequest is >= 1 and <= 500 &&
                           x.MaximumWebhookBytes is >= 1024 and <= 1_000_000 &&
                           x.ClientState.Length is >= 32 and <= 128 &&
                           Uri.TryCreate(x.NotificationUrl, UriKind.Absolute, out var notificationUri) &&
                           notificationUri.Scheme == Uri.UriSchemeHttps &&
                           Uri.TryCreate(x.LifecycleNotificationUrl, UriKind.Absolute, out var lifecycleUri) &&
                           lifecycleUri.Scheme == Uri.UriSchemeHttps,
                "Microsoft Graph transcript limits are outside supported boundaries.")
            .ValidateOnStart();
        services.AddHttpClient(MicrosoftGraphMeetingTranscriptAdapter.ClientName,
            client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IMeetingTranscriptProviderAdapter, MicrosoftGraphMeetingTranscriptAdapter>();
        services.AddScoped<ISalesMeetingTranscriptSubscriptionService, SalesMeetingTranscriptSubscriptionService>();
        services.AddScoped<IMicrosoftGraphTranscriptWebhookService, MicrosoftGraphTranscriptWebhookService>();
        services.AddScoped<ISalesMeetingTranscriptIngestionDispatcher, SalesMeetingTranscriptIngestionDispatcher>();
        services.AddScoped<ISalesMeetingTranscriptReconciliationQuery, SalesMeetingTranscriptReconciliationQuery>();
        services.AddHostedService<SalesMeetingTranscriptSubscriptionBackgroundService>();
        services.AddHealthChecks().AddCheck<SalesMeetingTranscriptHealthCheck>("microsoft-graph-meeting-transcripts", tags: ["ready"]);
        services.AddSingleton<ISalesPresentationDeckExtractor, OpenXmlSalesPresentationDeckExtractor>();
        services.AddSingleton<ISalesPresentationSlideRenderer, DeterministicSvgSalesPresentationSlideRenderer>();
        services.AddHostedService<SalesPresentationDeckBackgroundService>();
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
        services.AddScoped<ITodayWorkspaceContributor, SalesTodayWorkspaceContributor>();
        services.AddScoped<ITodayWorkspaceContributor, MarketingTodayWorkspaceContributor>();
        services.AddScoped<IMonthlyWorkspaceContributor, SalesMonthlyWorkspaceContributor>();
        services.AddScoped<IMonthlyWorkspaceContributor, MarketingMonthlyWorkspaceContributor>();
        services.AddScoped<ISalesRoomCaptureService, SalesRoomCaptureService>();
        services.AddHostedService<SalesRoomCaptureRetentionWorker>();
        return services;
    }
}
