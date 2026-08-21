using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Finance;

public static class FinanceModuleRegistration
{
    public static IServiceCollection AddFinanceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IAccountingPolicyPack, CountryNeutralAccountingPolicyPack>();
        services.AddSingleton<IAccountingPolicyPack, CountryNeutralBankingAccountingPolicyPack>();
        services.AddSingleton<IAccountingPolicyPackResolver, AccountingPolicyPackResolver>();
        services.AddHostedService<AccountingPolicyPackCatalogStartupValidator>();
        services.AddScoped<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddOptions<AccountingMigrationWorkerOptions>()
            .Bind(configuration.GetSection(AccountingMigrationWorkerOptions.SectionName));
        services.PostConfigure<AccountingMigrationWorkerOptions>(options =>
        {
            options.PollIntervalSeconds = Math.Max(1, options.PollIntervalSeconds);
            options.BatchSize = Math.Clamp(options.BatchSize, 1, 500);
            options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 50);
            options.LeaseSeconds = Math.Max(15, options.LeaseSeconds);
            options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
        });
        services.AddSingleton<AccountingOperationsTelemetry>();
        services.AddScoped<AccountingHistoricalMigrationService>();
        services.AddScoped<IAccountingMigrationService>(provider => provider.GetRequiredService<AccountingHistoricalMigrationService>());
        services.AddScoped<IAccountingMigrationJobRunner>(provider => provider.GetRequiredService<AccountingHistoricalMigrationService>());
        services.AddScoped<IAccountingReadinessService, AccountingReadinessService>();
        services.AddScoped<IAccountingOperationsReadService, AccountingOperationsReadService>();
        services.AddScoped<IAccountingRecoveryVerificationService, AccountingRecoveryVerificationService>();
        services.AddScoped<AccountingAuthorityPolicy>();
        services.AddScoped<IAccountingAuthorityPolicy>(provider => provider.GetRequiredService<AccountingAuthorityPolicy>());
        services.AddScoped<AccountingAuthorityService>();
        services.AddScoped<IAccountingAuthorityService>(provider => provider.GetRequiredService<AccountingAuthorityService>());
        services.AddScoped<IAccountingProviderExportAdapter, FortnoxAccountingProviderExportAdapter>();
        services.AddScoped<AccountingProviderExportService>();
        services.AddScoped<IAccountingProviderExportService>(provider => provider.GetRequiredService<AccountingProviderExportService>());
        services.AddScoped<IAccountingProviderExportExecutionTracker>(provider => provider.GetRequiredService<AccountingProviderExportService>());
        services.AddScoped<IFinanceDocumentActionProviderAdapter, FortnoxFinanceDocumentActionAdapter>();
        services.AddScoped<IFinanceCustomerDocumentProviderAdapter, FortnoxFinanceCustomerDocumentAdapter>();
        services.AddScoped<IFinanceAccountingActionService, FinanceAccountingActionService>();
        services.AddScoped<IAccountingAccountRoleResolver, AccountingAccountRoleResolver>();
        services.AddScoped<IAccountingAdministrationService, AccountingAdministrationService>();
        services.AddScoped<IAccountingPostingService, AccountingPostingService>();
        services.AddScoped<IAccountingJournalReadService, AccountingJournalReadService>();
        services.AddScoped<IManualJournalPolicy, ManualJournalPolicy>();
        services.AddScoped<IManualJournalService, ManualJournalService>();
        services.AddScoped<CustomerInvoiceAccountingPolicy>();
        services.AddScoped<ICustomerInvoiceAccountingPolicy>(provider => provider.GetRequiredService<CustomerInvoiceAccountingPolicy>());
        services.AddScoped<ICustomerInvoiceAccountingService, CustomerInvoiceAccountingService>();
        services.AddScoped<SupplierBillAccountingPolicy>();
        services.AddScoped<ISupplierBillAccountingPolicy>(provider => provider.GetRequiredService<SupplierBillAccountingPolicy>());
        services.AddScoped<ISupplierBillAccountingService, SupplierBillAccountingService>();
        services.AddScoped<IGuidedArtifactDefinition, FinanceBudgetGuidedArtifactDefinition>();
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
            .PostConfigure(options => options.DisabledMessage = options.DisabledMessage.Trim());
        services.AddOptions<SupplierApprovalAutomationOptions>()
            .Bind(configuration.GetSection(SupplierApprovalAutomationOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DisabledMessage),
                "SupplierApprovalAutomation:DisabledMessage is required.")
            .PostConfigure(options => options.DisabledMessage = options.DisabledMessage.Trim());
        services.AddOptions<CompanySimulationProgressionWorkerOptions>()
            .Bind(configuration.GetSection(CompanySimulationProgressionWorkerOptions.SectionName))
            .PostConfigure(options =>
            {
                options.PollIntervalMilliseconds = Math.Max(100, options.PollIntervalMilliseconds);
                options.BatchSize = Math.Max(1, options.BatchSize);
            });
        services.AddHostedService<CompanySimulationProgressionBackgroundService>();
        services.AddScoped<IDashboardFinanceSnapshotService, CompanyDashboardFinanceSnapshotService>();
        services.AddScoped<ICompanyOperatingSnapshotContributor, FinanceOperatingSnapshotContributor>();
        services.AddScoped<IBillDuplicateCheckRepository, BillDuplicateCheckRepository>();
        services.AddScoped<IBillExtractionPersistenceRepository, BillExtractionPersistenceRepository>();
        services.AddScoped<IDocumentTextExtractor, PdfDocumentTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, OpenAiPdfOcrTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, DocxDocumentTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, EmailBodyTextExtractor>();
        services.AddScoped<ICompanySimulationService, CompanySimulationService>();
        services.AddScoped<ICompanySimulationStateRepository, EfCompanySimulationStateRepository>();
        services.AddSingleton<ISimulationFeatureGate, ConfigurationSimulationFeatureGate>();
        services.AddScoped<CompanySimulationStateService>();
        services.AddScoped<ICompanySimulationStateService>(provider => provider.GetRequiredService<CompanySimulationStateService>());
        services.AddScoped<ICompanySimulationProgressionRunner, CompanySimulationProgressionRunner>();

        services.AddOptions<FinanceToolProviderOptions>()
            .Bind(configuration.GetSection(FinanceToolProviderOptions.SectionName))
            .PostConfigure(options =>
            {
                options.Provider = string.IsNullOrWhiteSpace(options.Provider)
                    ? FinanceToolProviderOptions.InternalProvider
                    : options.Provider.Trim();
            });
        services.AddOptions<FinanceAnomalyDetectionOptions>()
            .Bind(configuration.GetSection(FinanceAnomalyDetectionOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FortnoxOptions>, FortnoxOptionsValidator>());
        services.AddOptions<FortnoxOptions>()
            .Bind(configuration.GetSection(FortnoxOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IFinanceIntegrationApplicationDefinition, FortnoxFinanceIntegrationApplicationDefinition>();
        services.AddScoped<FinanceIntegrationApplicationManagementService>();
        services.AddScoped<IFinanceIntegrationApplicationManagementService>(
            provider => provider.GetRequiredService<FinanceIntegrationApplicationManagementService>());
        services.AddScoped<IFinanceIntegrationRuntimeSettingsProvider>(
            provider => provider.GetRequiredService<FinanceIntegrationApplicationManagementService>());
        services.AddHttpClient(FortnoxOAuthClient.ClientName);
        services.AddHttpClient<IFortnoxApiClient, FortnoxApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<FortnoxOptions>>().CurrentValue;
            client.BaseAddress = FortnoxApiClient.NormalizeBaseAddress(options.ApiBaseUrl);
        });
        services.AddScoped<IFortnoxOAuthStateProtector, DataProtectionFortnoxOAuthStateProtector>();
        services.AddScoped<IFortnoxOAuthSessionStore, EfFortnoxOAuthSessionStore>();
        services.AddScoped<FortnoxOAuthService>();
        services.AddScoped<IFortnoxOAuthService>(provider => provider.GetRequiredService<FortnoxOAuthService>());
        services.AddScoped<FortnoxFinanceIntegrationOAuthService>();
        services.AddScoped<IFinanceIntegrationOAuthService>(provider => provider.GetRequiredService<FortnoxFinanceIntegrationOAuthService>());
        services.TryAddSingleton<IFortnoxIntegrationDiagnostics, FortnoxIntegrationDiagnostics>();
        services.TryAddSingleton<IFortnoxErrorTranslator, DefaultFortnoxErrorTranslator>();
        services.AddScoped<IFortnoxTokenStore, FortnoxTokenStore>();
        services.AddScoped<FortnoxOAuthClient>();
        services.AddScoped<FortnoxMappingService>();
        services.AddScoped<IFortnoxMappingService>(provider => provider.GetRequiredService<FortnoxMappingService>());
        services.AddScoped<FortnoxFinanceIntegrationMapper>();
        services.AddScoped<IFinanceIntegrationMapper>(provider => provider.GetRequiredService<FortnoxFinanceIntegrationMapper>());
        services.AddScoped<FinanceIntegrationWriteApprovalService>();
        services.AddScoped<IFinanceIntegrationWriteApprovalService>(provider => provider.GetRequiredService<FinanceIntegrationWriteApprovalService>());
        services.AddScoped<IFinanceIntegrationWriteCommandService>(provider => provider.GetRequiredService<FinanceIntegrationWriteApprovalService>());
        services.AddScoped<FinanceBillFortnoxRegistrationCompletionService>();
        services.AddScoped<IFortnoxOutboundActionExecutor, FortnoxOutboundActionExecutor>();
        services.AddScoped<FortnoxSyncService>();
        services.AddScoped<IFortnoxSyncService>(provider => provider.GetRequiredService<FortnoxSyncService>());
        services.AddScoped<FortnoxFinanceIntegrationSyncService>();
        services.AddScoped<IFinanceIntegrationSyncService>(provider => provider.GetRequiredService<FortnoxFinanceIntegrationSyncService>());

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
                options.MissingDatasetBehavior = FinanceMissingDatasetBehaviorValues.Normalize(options.MissingDatasetBehavior));
        services.AddOptions<FinanceTransactionCreationOptions>()
            .Bind(configuration.GetSection(FinanceTransactionCreationOptions.SectionName));
        services.AddOptions<OpenAiPdfOcrTextExtractor.FinanceDocumentOcrOptions>()
            .Bind(configuration.GetSection(OpenAiPdfOcrTextExtractor.FinanceDocumentOcrOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = configuration["OPENAI_API_KEY"]
                        ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                        ?? string.Empty;
                }
            });
        services.AddOptions<FinanceIntegrationStartupSyncOptions>()
            .Bind(configuration.GetSection(FinanceIntegrationStartupSyncOptions.SectionName))
            .PostConfigure(options =>
            {
                options.StartupDelaySeconds = Math.Max(0, options.StartupDelaySeconds);
                options.SyncTimeoutSeconds = Math.Max(1, options.SyncTimeoutSeconds);
                options.LockTtlSeconds = Math.Max(options.SyncTimeoutSeconds, options.LockTtlSeconds);
                options.ProviderKeys = options.ProviderKeys?
                    .Where(providerKey => !string.IsNullOrWhiteSpace(providerKey))
                    .Select(providerKey => providerKey.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? [];
            });
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
        services.AddHttpClient(OpenAiPdfOcrTextExtractor.ClientName);
        services.AddScoped<InternalFinanceToolProvider>();
        services.AddScoped<MockFinanceToolProvider>();
        services.AddScoped<IFinanceCommandService, CompanyFinanceCommandService>();
        services.AddScoped<IFinanceAgentInsightRepository, FinanceAgentInsightRepository>();
        services.AddScoped<IFinanceInsightPersistenceService, FinanceInsightPersistenceService>();
        services.AddScoped<IFinancePaymentCommandService, CompanyFinanceCommandService>();
        services.AddScoped<CompanyCashSettlementPostingService>();
        services.AddScoped<IFinanceCashSettlementPostingService>(provider => provider.GetRequiredService<CompanyCashSettlementPostingService>());
        services.AddScoped<IFinanceApprovalTaskService, CompanyFinanceApprovalTaskService>();
        services.AddScoped<ICashPostingTraceabilityBackfillService, CompanyCashPostingTraceabilityBackfillService>();
        services.AddScoped<CompanyBankTransactionService>();
        services.AddScoped<IBankTransactionReadService>(provider => provider.GetRequiredService<CompanyBankTransactionService>());
        services.AddScoped<IBankTransactionCommandService>(provider => provider.GetRequiredService<CompanyBankTransactionService>());
        services.AddScoped<IFinancePolicyConfigurationService, CompanyFinanceCommandService>();
        services.AddScoped<IFinancialStatementMappingService, CompanyFinancialStatementMappingService>();
        services.AddScoped<IExecutiveCockpitFinanceAdapter, CompanyExecutiveCockpitFinanceAdapter>();
        services.AddScoped<FortnoxFinanceIntegrationProvider>();
        services.AddScoped<IFinanceIntegrationProvider>(provider => provider.GetRequiredService<FortnoxFinanceIntegrationProvider>());
        services.AddScoped<IFinanceIntegrationProviderRegistry, FinanceIntegrationProviderRegistry>();
        services.AddScoped<IFinanceIntegrationProviderResolver>(provider => provider.GetRequiredService<IFinanceIntegrationProviderRegistry>());
        services.AddScoped<IFinanceBootstrapRerunService, CompanyFinanceBootstrapRerunService>();
        services.AddScoped<IFinanceSeedingStateService, CompanyFinanceSeedingStateResolver>();
        services.AddScoped<IFinanceSeedBackfillOrchestrator, FinanceSeedBackfillOrchestrator>();
        services.AddScoped<FinanceSummaryConsistencyChecker>();
        services.AddScoped<IFinanceSummaryQueryService, CompanyFinanceSummaryQueryService>();
        services.AddScoped<IFinanceSeedBackfillQueryService, FinanceSeedBackfillQueryService>();
        services.AddScoped<IPlanningBaselineService, PlanningBaselineService>();
        services.AddScoped<IFinanceSeedTelemetry, FinanceSeedTelemetry>();
        services.AddScoped<IFinanceSeedBootstrapService, CompanyFinanceSeedBootstrapService>();
        services.AddScoped<IFinanceEntryService, CompanyFinanceEntryService>();
        services.AddScoped<IFinanceSeedJobRunner, CompanyFinanceSeedJobRunner>();
        services.AddScoped<IReportingPeriodCloseService, CompanyReportingPeriodCloseService>();
        services.AddScoped<IAccountingReportingService, AccountingReportingService>();
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
        services.AddSingleton<IFinanceWorkflowTriggerRegistry, StaticFinanceWorkflowTriggerRegistry>();
        services.AddScoped<IFinanceWorkflowTriggerService, FinanceWorkflowTriggerService>();
        services.AddScoped<IFinanceBillInboxService, CompanyFinanceBillInboxService>();
        services.AddScoped<ISupplierSubscriptionService, SupplierSubscriptionService>();
        services.AddScoped<ISupplierSubscriptionIntakeProposalService, SupplierSubscriptionIntakeProposalService>();
        services.AddScoped<ISupplierSubscriptionDocumentClassifier, SupplierSubscriptionDocumentClassifier>();
        services.AddScoped<ISupplierInvoicePaymentExportProvider, FortnoxSupplierInvoicePaymentExportProvider>();
        services.AddScoped<IFinanceSupplierPaymentProposalService, SupplierInvoicePaymentProposalService>();
        services.AddScoped<ISupplierInvoiceSourceDocumentAttachmentProvider, FortnoxSupplierInvoiceSourceDocumentAttachmentProvider>();
        services.AddScoped<IFinanceSupplierInvoiceSourceDocumentAttachmentService, SupplierInvoiceSourceDocumentAttachmentService>();
        services.AddScoped<ISupplierInvoiceDraftActionProvider, FortnoxSupplierInvoiceDraftActionProvider>();
        services.AddScoped<IFinanceSupplierInvoiceDraftActionService, SupplierInvoiceDraftActionService>();
        services.AddScoped<IPaidSupplierBillExpensePostingService, PaidSupplierBillExpensePostingService>();
        services.AddScoped<IFinanceCustomerInvoiceFortnoxActionService, CustomerInvoiceFortnoxActionService>();
        services.AddScoped<ISupplierInvoiceCorrectionProvider, FortnoxSupplierInvoiceCorrectionProvider>();
        services.AddScoped<IFinanceSupplierInvoiceCorrectionService, SupplierInvoiceCorrectionService>();
        services.AddScoped<ISupplierInvoiceEnrichmentProvider, FortnoxSupplierInvoiceEnrichmentProvider>();
        services.AddScoped<IFinanceSupplierInvoiceEnrichmentService, SupplierInvoiceEnrichmentService>();
        services.AddScoped<IFinanceReadService, CompanyFinanceReadService>();
        services.AddScoped<IFinancePaymentReadService, CompanyFinanceReadService>();
        services.AddScoped<IReconciliationScoringSettingsProvider, CompanyReconciliationScoringSettingsProvider>();
        services.AddScoped<IReconciliationScoringService, CompanyReconciliationScoringService>();
        services.AddScoped<IReconciliationSuggestionReadService, CompanyReconciliationSuggestionService>();
        services.AddScoped<IReconciliationSuggestionCommandService, CompanyReconciliationSuggestionService>();
        services.AddScoped<IFinanceAgentAnalysisService, FinanceAgentAnalysisService>();
        services.AddScoped<IFinanceAgentDecisionService, FinanceAgentDecisionService>();
        services.AddScoped<IFinanceToolProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FinanceToolProviderOptions>>().Value;
            return options.Provider.Equals(FinanceToolProviderOptions.MockProvider, StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<MockFinanceToolProvider>()
                : provider.GetRequiredService<InternalFinanceToolProvider>();
        });
        services.AddSingleton<IFinanceDeterministicValueSource, Sha256FinanceDeterministicValueSource>();
        services.AddSingleton<IFinanceScenarioFactory, DefaultFinanceScenarioFactory>();
        services.AddSingleton<IFinanceAnomalyScheduleFactory, PeriodicFinanceAnomalyScheduleFactory>();
        services.AddScoped<IFinanceGenerationPolicy, CompanySimulationFinanceGenerationService>();
        services.AddHostedService<FinanceSeedBackfillBackgroundService>();
        services.AddHostedService<ReportingPeriodRegenerationBackgroundService>();
        services.AddHostedService<AccountingExportBackgroundService>();
        services.AddHostedService<AccountingMigrationBackgroundService>();
        services.AddHostedService<FinanceApprovalTaskBackfillBackgroundService>();
        services.AddHostedService<FinanceInsightsSnapshotBackgroundService>();
        services.AddHostedService<FinanceAnalyticsStartupRefreshBackgroundService>();
        services.AddHostedService<FinanceIntegrationStartupSyncBackgroundService>();
        services.AddHostedService<FinanceBillFortnoxRegistrationReconciliationBackgroundService>();
        services.AddHostedService<FinanceSeedBackgroundService>();
        return services;
    }
}



