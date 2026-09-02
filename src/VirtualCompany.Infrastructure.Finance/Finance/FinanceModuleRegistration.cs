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
        services.AddSingleton<IAccountingPolicyPack, SwedishDomesticVatCandidatePackV1_1>();
        services.AddSingleton<IAccountingPolicyPack, SwedishCandidateAccountingPolicyPack>();
        services.AddSingleton<IAccountingPolicyPack, SwedishStatutoryDocumentCandidatePack>();
        services.AddSingleton<IAccountingPolicyPack, SwedishStatutoryArchiveCandidatePack>();
        services.AddSingleton<IAccountingPolicyPack, SwedishFoundationAccountingPolicyPack>();
        services.AddSingleton<IAccountingPolicyPackResolver, AccountingPolicyPackResolver>();
        services.AddSingleton<IAccountingChartCatalog, Bas2026AccountingChartCatalog>();
        services.AddSingleton<IAccountingChartCatalogResolver, AccountingChartCatalogResolver>();
        foreach (var evidence in AccountingPolicyPackValidationEvidenceCatalog.All)
            services.AddSingleton(evidence);
        services.AddSingleton<IAccountingPolicyPackValidationRegistry, AccountingPolicyPackValidationRegistry>();
        services.AddHostedService<AccountingPolicyPackCatalogStartupValidator>();
        services.AddScoped<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddScoped<ICompanyStatutoryProfileService, CompanyStatutoryProfileService>();
        services.AddSingleton<CustomerBillingTelemetry>();
        services.AddScoped<ICustomerBillingProfileService, CustomerBillingProfileService>();
        services.AddScoped<ICustomerBillingProviderSyncGuard>(provider =>
            (CustomerBillingProfileService)provider.GetRequiredService<ICustomerBillingProfileService>());
        services.AddScoped<StatutoryDocumentPolicy>();
        services.AddScoped<IStatutoryDocumentPolicy>(provider => provider.GetRequiredService<StatutoryDocumentPolicy>());
        services.AddScoped<IStatutoryDocumentService, StatutoryDocumentService>();
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
        services.AddSingleton<BankConnectionTelemetry>();
        services.AddSingleton<TreasuryMovementTelemetry>();
        services.AddSingleton<PaymentBatchTelemetry>();
        services.AddSingleton<PaymentExecutionTelemetry>();
        services.AddSingleton<TreasuryWorkspaceTelemetry>();
        services.AddSingleton<ExchangeRateTelemetry>();
        services.AddSingleton<AccountingCloseTelemetry>();
        services.AddSingleton<ForeignCurrencySettlementTelemetry>();
        services.AddSingleton<CurrencyRevaluationTelemetry>();
        services.AddSingleton<FixedAssetTelemetry>();
        services.AddSingleton<FinancialReportSuiteTelemetry>();
        services.AddSingleton<ReportDefinitionTelemetry>();
        services.AddSingleton<AuditPackageTelemetry>();
        services.AddOptions<AuditPackageOptions>()
            .Bind(configuration.GetSection(AuditPackageOptions.SectionName))
            .Validate(x => x.PollIntervalSeconds is >= 5 and <= 3600,
                "Audit-package polling must be between 5 seconds and 1 hour.")
            .Validate(x => x.ClaimBatchSize is >= 1 and <= 20 && x.MaximumAttempts is >= 1 and <= 20 && x.LeaseSeconds is >= 30 and <= 1800,
                "Audit-package claim and retry bounds are invalid.")
            .Validate(x => x.RetentionYears is >= 1 and <= 10 && x.DownloadAuthorizationMinutes is >= 1 and <= 60,
                "Audit-package retention or download-authorization bounds are invalid.")
            .Validate(x => x.MaximumGeneralLedgerPages is >= 1 and <= 500 && x.MaximumDocumentCount is >= 1 and <= 5000,
                "Audit-package artifact-count bounds are invalid.")
            .Validate(x => x.MaximumDocumentBytes is >= 1024 and <= 100 * 1024 * 1024 &&
                           x.MaximumPackageBytes >= x.MaximumDocumentBytes && x.MaximumPackageBytes <= 2L * 1024 * 1024 * 1024,
                "Audit-package object-size bounds are invalid.")
            .ValidateOnStart();
        services.AddOptions<FixedAssetMaintenanceOptions>()
            .Bind(configuration.GetSection(FixedAssetMaintenanceOptions.SectionName))
            .Validate(x => x.PollIntervalSeconds is >= 60 and <= 86400,
                "Fixed-asset maintenance polling must be between 1 minute and 1 day.")
            .Validate(x => x.CompanyBatchSize is >= 1 and <= 500,
                "Fixed-asset maintenance company batch size is outside supported bounds.")
            .ValidateOnStart();
        services.AddOptions<CurrencyRevaluationWorkerOptions>()
            .Bind(configuration.GetSection(CurrencyRevaluationWorkerOptions.SectionName))
            .Validate(x => x.PollIntervalSeconds is >= 60 and <= 86400,
                "Currency revaluation polling must be between 1 minute and 1 day.")
            .ValidateOnStart();
        services.AddOptions<ExchangeRateAuthorityOptions>()
            .Bind(configuration.GetSection(ExchangeRateAuthorityOptions.SectionName))
            .Validate(x => x.RawEvidenceRetentionDays is >= 365 and <= 3650,
                "Exchange-rate raw evidence retention must be between 1 and 10 years.")
            .Validate(x => x.MaximumManualObservations is >= 1 and <= 5000,
                "Exchange-rate manual imports must allow between 1 and 5,000 observations.")
            .Validate(x => x.MaximumLookupCandidates is >= 100 and <= 10000,
                "Exchange-rate lookup bounds must be between 100 and 10,000 candidates.")
            .ValidateOnStart();
        services.AddOptions<ExchangeRateRefreshOptions>()
            .Bind(configuration.GetSection(ExchangeRateRefreshOptions.SectionName))
            .Validate(x => x.PollIntervalSeconds is >= 10 and <= 3600,
                "Exchange-rate polling must be between 10 seconds and 1 hour.")
            .Validate(x => x.ClaimBatchSize is >= 1 and <= 100 && x.LeaseSeconds is >= 30 and <= 900,
                "Exchange-rate claim and lease bounds are invalid.")
            .Validate(x => x.MaximumAttempts is >= 1 and <= 20 && x.BaseRetryDelaySeconds is >= 1 and <= 3600 &&
                           x.MaximumRetryDelaySeconds >= x.BaseRetryDelaySeconds && x.MaximumRetryDelaySeconds <= 86400,
                "Exchange-rate retry bounds are invalid.")
            .ValidateOnStart();
        services.AddOptions<RiksbankExchangeRateOptions>()
            .Bind(configuration.GetSection(RiksbankExchangeRateOptions.SectionName))
            .PostConfigure(options =>
            {
                options.SubscriptionKey = configuration["RIKSBANK_API_SUBSCRIPTION_KEY"]
                    ?? Environment.GetEnvironmentVariable("RIKSBANK_API_SUBSCRIPTION_KEY")
                    ?? options.SubscriptionKey;
                options.ApiBaseUrl = string.IsNullOrWhiteSpace(options.ApiBaseUrl)
                    ? "https://api.riksbank.se/swea/v1/" : options.ApiBaseUrl.Trim();
                options.RequestTimeoutSeconds = Math.Clamp(options.RequestTimeoutSeconds, 5, 120);
            })
            .Validate(x => Uri.TryCreate(x.ApiBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "The Sveriges Riksbank API base URL must be an absolute HTTPS URL.")
            .ValidateOnStart();
        services.AddHttpClient(RiksbankExchangeRateOptions.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<RiksbankExchangeRateOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl.EndsWith('/') ? options.ApiBaseUrl : options.ApiBaseUrl + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });
        services.AddScoped<RiksbankExchangeRateProvider>();
        services.AddScoped<IExchangeRateProvider>(provider => provider.GetRequiredService<RiksbankExchangeRateProvider>());
        services.AddScoped<IExchangeRateProviderRegistry, ExchangeRateProviderRegistry>();
        services.AddScoped<ExchangeRateService>();
        services.AddScoped<IExchangeRateService>(provider => provider.GetRequiredService<ExchangeRateService>());
        services.AddScoped<IExchangeRateRefreshRunner, ExchangeRateRefreshRunner>();
        services.AddOptions<ConnectedBankingReadinessOptions>()
            .Bind(configuration.GetSection(ConnectedBankingReadinessOptions.SectionName))
            .Validate(options => options.ConsentExpiryWarningDays is >= 1 and <= 90,
                "Connected-banking consent warning must be between 1 and 90 days.")
            .Validate(options => options.FeedLagWarningMinutes is >= 1 and <= 1440,
                "Connected-banking feed-lag warning must be between 1 minute and 1 day.")
            .Validate(options => options.UnreconciledWarningDays is >= 1 and <= 90 &&
                                 options.StaleApprovalWarningDays is >= 1 and <= 30 &&
                                 options.UnsettledBatchWarningDays is >= 1 and <= 30,
                "Connected-banking aging windows are outside supported bounds.")
            .Validate(options => options.WorkerBacklogWarningMinutes is >= 1 and <= 1440,
                "Connected-banking worker backlog warning must be between 1 minute and 1 day.")
            .ValidateOnStart();
        services.AddOptions<PaymentExecutionOptions>()
            .Bind(configuration.GetSection(PaymentExecutionOptions.SectionName))
            .PostConfigure(options =>
            {
                options.PollIntervalSeconds = Math.Clamp(options.PollIntervalSeconds, 10, 3600);
                options.MaximumStatusPolls = Math.Clamp(options.MaximumStatusPolls, 1, 1000);
                options.MaximumProviderAttempts = Math.Clamp(options.MaximumProviderAttempts, 1, 20);
                options.AuthorizationExpiryMinutes = Math.Clamp(options.AuthorizationExpiryMinutes, 5, 1440);
            });
        services.AddOptions<PaymentBatchPolicyOptions>()
            .Bind(configuration.GetSection(PaymentBatchPolicyOptions.SectionName))
            .PostConfigure(options =>
            {
                options.CutOffHourEuropeStockholm = Math.Clamp(options.CutOffHourEuropeStockholm, 0, 23);
                options.DualApprovalThreshold = Math.Max(0m, options.DualApprovalThreshold);
                options.ApprovalRole = string.IsNullOrWhiteSpace(options.ApprovalRole) ? "owner" : options.ApprovalRole.Trim();
                options.SupportedCurrencies = options.SupportedCurrencies.Length == 0 ? ["SEK", "EUR"] : options.SupportedCurrencies.Select(x => x.Trim().ToUpperInvariant()).Distinct().ToArray();
            });
        services.AddSingleton<IBankConsentStateProtector, DataProtectionBankConsentStateProtector>();
        services.AddOptions<EnableBankingOptions>()
            .Bind(configuration.GetSection(EnableBankingOptions.SectionName))
            .PostConfigure(options =>
            {
                options.ApplicationId = configuration["ENABLE_BANKING_APPLICATION_ID"]
                    ?? Environment.GetEnvironmentVariable("ENABLE_BANKING_APPLICATION_ID")
                    ?? options.ApplicationId;
                options.PrivateKeyPem = configuration["ENABLE_BANKING_PRIVATE_KEY_PEM"]
                    ?? Environment.GetEnvironmentVariable("ENABLE_BANKING_PRIVATE_KEY_PEM")
                    ?? options.PrivateKeyPem;
                options.PrivateKeyPath = configuration["ENABLE_BANKING_PRIVATE_KEY_PATH"]
                    ?? Environment.GetEnvironmentVariable("ENABLE_BANKING_PRIVATE_KEY_PATH")
                    ?? options.PrivateKeyPath;
                options.CountryCode = string.IsNullOrWhiteSpace(options.CountryCode) ? "SE" : options.CountryCode.Trim().ToUpperInvariant();
                options.PsuType = string.Equals(options.PsuType, "personal", StringComparison.OrdinalIgnoreCase) ? "personal" : "business";
                options.ConsentValidityDays = Math.Clamp(options.ConsentValidityDays, 1, 180);
                options.RequestTimeoutSeconds = Math.Clamp(options.RequestTimeoutSeconds, 5, 120);
            });
        services.AddHttpClient(EnableBankingProvider.HttpClientName, client =>
        {
            var section = configuration.GetSection(EnableBankingOptions.SectionName);
            var baseUri = section["BaseUri"] ?? "https://api.enablebanking.com/";
            client.BaseAddress = new Uri(baseUri.EndsWith('/') ? baseUri : baseUri + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(section.GetValue("RequestTimeoutSeconds", 45), 5, 120));
        });
        services.AddScoped<EnableBankingProvider>();
        services.AddScoped<IBankConnectionProvider>(provider => provider.GetRequiredService<EnableBankingProvider>());
        services.AddScoped<IBankFeedProvider>(provider => provider.GetRequiredService<EnableBankingProvider>());
        services.AddScoped<IPaymentInitiationProvider>(provider => provider.GetRequiredService<EnableBankingProvider>());
        services.AddScoped<IBankConnectionProviderRegistry, BankConnectionProviderRegistry>();
        services.AddScoped<IBankFeedProviderRegistry, BankFeedProviderRegistry>();
        services.AddScoped<IPaymentInitiationProviderRegistry, PaymentInitiationProviderRegistry>();
        services.AddScoped<IProtectedBankCredentialStore, ProtectedBankCredentialStore>();
        services.AddScoped<IBankConnectionService, BankConnectionService>();
        services.AddOptions<BankFeedSynchronizationOptions>()
            .Bind(configuration.GetSection(BankFeedSynchronizationOptions.SectionName))
            .Validate(x => x.PollIntervalSeconds is >= 10 and <= 3600, "Bank feed polling must be between 10 seconds and 1 hour.")
            .Validate(x => x.ClaimBatchSize is >= 1 and <= 100, "Bank feed claim batch size must be between 1 and 100.")
            .Validate(x => x.LeaseSeconds is >= 30 and <= 900, "Bank feed lease duration must be between 30 and 900 seconds.")
            .Validate(x => x.SynchronizationIntervalMinutes is >= 1 and <= 1440, "Bank feed synchronization interval must be between 1 minute and 1 day.")
            .Validate(x => x.InitialLookbackDays is >= 1 and <= 366 && x.OverlapDays is >= 1 and <= 30,
                "Bank feed lookback and overlap settings are outside supported bounds.")
            .Validate(x => x.MaximumBackfillDays is >= 1 and <= 1095 && x.MaximumAttempts is >= 1 and <= 20,
                "Bank feed recovery bounds are outside supported limits.")
            .ValidateOnStart();
        services.AddSingleton<BankFeedTelemetry>();
        services.AddScoped<IBankFeedService, BankFeedService>();
        services.AddScoped<IBankFeedSynchronizationRunner, BankFeedSynchronizationRunner>();
        services.AddOptions<BankStatementImportOptions>()
            .Bind(configuration.GetSection(BankStatementImportOptions.SectionName))
            .Validate(x => x.MaximumUploadBytes is >= 1024 and <= 20 * 1024 * 1024,
                "Statement import upload limit must be between 1 KB and 20 MB.")
            .Validate(x => x.MaximumRows is >= 1 and <= 100_000,
                "Statement import row limit must be between 1 and 100,000.")
            .Validate(x => x.CommitBatchSize is >= 1 and <= 1000,
                "Statement import commit batch size must be between 1 and 1,000.")
            .ValidateOnStart();
        services.AddScoped<IBankStatementFileParser, Iso20022BankStatementParser>();
        services.AddScoped<IBankStatementFileParser, CsvBankStatementParser>();
        services.AddOptions<AccountingCapacityOptions>()
            .Bind(configuration.GetSection(AccountingCapacityOptions.SectionName))
            .Validate(x => x.DefaultProfile is AccountingCapacityProfileKeys.Small or AccountingCapacityProfileKeys.Medium,
                "Accounting capacity default profile must be small or medium.")
            .Validate(x => x.DefaultCleanupBatchSize is >= 1 and <= 500,
                "Accounting cleanup default batch size must be between 1 and 500.")
            .Validate(x => x.MaximumCleanupBatchSize is >= 1 and <= 1000 &&
                x.MaximumCleanupBatchSize >= x.DefaultCleanupBatchSize,
                "Accounting cleanup maximum batch size must be between the default size and 1000.")
            .ValidateOnStart();
        services.AddScoped<IAccountingCapacityService, AccountingCapacityService>();
        services.AddScoped<AccountingHistoricalMigrationService>();
        services.AddScoped<IAccountingMigrationService>(provider => provider.GetRequiredService<AccountingHistoricalMigrationService>());
        services.AddScoped<IAccountingMigrationJobRunner>(provider => provider.GetRequiredService<AccountingHistoricalMigrationService>());
        services.AddScoped<IAccountingReadinessService, AccountingReadinessService>();
        services.AddScoped<IAccountingOperationsReadService, AccountingOperationsReadService>();
        services.AddScoped<INativeReceivablesReadinessService, NativeReceivablesReadinessService>();
        services.AddScoped<IAccountingRecoveryVerificationService, AccountingRecoveryVerificationService>();
        services.AddOptions<FinanceWorkerRecoveryOptions>()
            .Bind(configuration.GetSection(FinanceWorkerRecoveryOptions.SectionName))
            .Validate(x => x.BacklogWarningMinutes > 0, "Finance worker backlog warning minutes must be positive.")
            .Validate(x => x.LeaseGraceSeconds >= 0, "Finance worker lease grace seconds cannot be negative.")
            .Validate(x => x.MaximumVisibleItems is >= 1 and <= 1000, "Finance worker maximum visible items must be between 1 and 1000.")
            .ValidateOnStart();
        services.AddSingleton<FinanceWorkerOperationsTelemetry>();
        services.AddScoped<IFinanceWorkerOperationsService, FinanceWorkerOperationsService>();
        services.AddScoped<FinanceBackgroundExecutionAttemptRecorder>();
        services.AddHealthChecks().AddCheck<FinanceWorkerReadinessHealthCheck>("finance-workers", tags: ["ready"]);
        services.AddHealthChecks().AddCheck<SwedishAccountingValidationHealthCheck>("swedish-accounting-validation", tags: ["ready"]);
        services.AddScoped<AccountingAuthorityPolicy>();
        services.AddScoped<IAccountingAuthorityPolicy>(provider => provider.GetRequiredService<AccountingAuthorityPolicy>());
        services.AddScoped<AccountingAuthorityService>();
        services.AddScoped<IAccountingAuthorityService>(provider => provider.GetRequiredService<AccountingAuthorityService>());
        services.AddScoped<AccountingProviderSwitchService>();
        services.AddScoped<IAccountingProviderSwitchService>(provider => provider.GetRequiredService<AccountingProviderSwitchService>());
        services.AddOptions<AccountingProviderSwitchAssessmentWorkerOptions>()
            .Bind(configuration.GetSection(AccountingProviderSwitchAssessmentWorkerOptions.SectionName));
        services.PostConfigure<AccountingProviderSwitchAssessmentWorkerOptions>(options =>
        {
            options.PollIntervalSeconds = Math.Max(1, options.PollIntervalSeconds);
            options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 20);
            options.PageSize = Math.Clamp(options.PageSize, 1, 500);
            options.LeaseSeconds = Math.Max(15, options.LeaseSeconds);
            options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
        });
        services.AddScoped<IAccountingProviderSwitchAdapter, InternalLedgerProviderSwitchAdapter>();
        services.AddScoped<IAccountingProviderSwitchAdapter, FortnoxProviderSwitchAdapter>();
        services.AddScoped<IAccountingProviderSwitchAdapter, UnavailableExternalProviderSwitchAdapter>();
        services.AddScoped<IAccountingProviderSwitchAdapterResolver, AccountingProviderSwitchAdapterResolver>();
        services.AddSingleton<IAccountingProviderSwitchGapPolicy, AccountingProviderSwitchGapPolicy>();
        services.AddScoped<AccountingProviderSwitchAssessmentService>();
        services.AddScoped<IAccountingProviderSwitchAssessmentService>(provider => provider.GetRequiredService<AccountingProviderSwitchAssessmentService>());
        services.AddScoped<IAccountingProviderSwitchAssessmentJobRunner>(provider => provider.GetRequiredService<AccountingProviderSwitchAssessmentService>());
        services.AddScoped<AccountingProviderSwitchStagingService>();
        services.AddScoped<IAccountingProviderSwitchStagingService>(provider => provider.GetRequiredService<AccountingProviderSwitchStagingService>());
        services.AddOptions<AccountingProviderSwitchRehearsalWorkerOptions>()
            .Bind(configuration.GetSection(AccountingProviderSwitchRehearsalWorkerOptions.SectionName));
        services.PostConfigure<AccountingProviderSwitchRehearsalWorkerOptions>(options =>
        {
            options.PollIntervalSeconds = Math.Max(1, options.PollIntervalSeconds);
            options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 20);
            options.LeaseSeconds = Math.Max(15, options.LeaseSeconds);
            options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
            options.SourceFreshnessHours = Math.Clamp(options.SourceFreshnessHours, 1, 168);
        });
        services.AddScoped<IAccountingProviderSwitchRehearsalAdapter, InternalLedgerProviderSwitchRehearsalAdapter>();
        services.AddScoped<IAccountingProviderSwitchRehearsalAdapter, FortnoxProviderSwitchRehearsalAdapter>();
        services.AddScoped<IAccountingProviderSwitchRehearsalAdapter, UnavailableProviderSwitchRehearsalAdapter>();
        services.AddScoped<AccountingProviderSwitchRehearsalService>();
        services.AddScoped<IAccountingProviderSwitchRehearsalService>(provider => provider.GetRequiredService<AccountingProviderSwitchRehearsalService>());
        services.AddScoped<IAccountingProviderSwitchRehearsalJobRunner>(provider => provider.GetRequiredService<AccountingProviderSwitchRehearsalService>());
        services.AddOptions<AccountingProviderSwitchPreparationWorkerOptions>()
            .Bind(configuration.GetSection(AccountingProviderSwitchPreparationWorkerOptions.SectionName));
        services.PostConfigure<AccountingProviderSwitchPreparationWorkerOptions>(options =>
        {
            options.PollIntervalSeconds = Math.Max(1, options.PollIntervalSeconds);
            options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 20);
            options.SaveBatchSize = Math.Clamp(options.SaveBatchSize, 1, 500);
            options.LeaseSeconds = Math.Max(15, options.LeaseSeconds);
            options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
        });
        services.AddScoped<IAccountingProviderSwitchInternalReadinessPolicy, AccountingProviderSwitchInternalReadinessPolicy>();
        services.AddScoped<AccountingProviderSwitchPreparationService>();
        services.AddScoped<IAccountingProviderSwitchPreparationService>(provider => provider.GetRequiredService<AccountingProviderSwitchPreparationService>());
        services.AddScoped<IAccountingProviderSwitchPreparationJobRunner>(provider => provider.GetRequiredService<AccountingProviderSwitchPreparationService>());
        services.AddOptions<AccountingProviderSwitchTargetTransferWorkerOptions>()
            .Bind(configuration.GetSection(AccountingProviderSwitchTargetTransferWorkerOptions.SectionName));
        services.PostConfigure<AccountingProviderSwitchTargetTransferWorkerOptions>(options =>
        {
            options.PollIntervalSeconds = Math.Max(1, options.PollIntervalSeconds);
            options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 20);
            options.LeaseSeconds = Math.Max(15, options.LeaseSeconds);
            options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
        });
        services.AddScoped<IAccountingProviderSwitchTargetPreparationAdapter, FortnoxAccountingProviderSwitchTargetPreparationAdapter>();
        services.AddScoped<AccountingProviderSwitchTargetTransferService>();
        services.AddScoped<IAccountingProviderSwitchTargetTransferService>(provider => provider.GetRequiredService<AccountingProviderSwitchTargetTransferService>());
        services.AddScoped<IAccountingProviderSwitchTargetTransferJobRunner>(provider => provider.GetRequiredService<AccountingProviderSwitchTargetTransferService>());
        services.AddScoped<IAccountingProviderSwitchTargetTransferExecutionTracker>(provider => provider.GetRequiredService<AccountingProviderSwitchTargetTransferService>());
        services.AddOptions<AccountingProviderSwitchCutoverWorkerOptions>()
            .Bind(configuration.GetSection(AccountingProviderSwitchCutoverWorkerOptions.SectionName));
        services.PostConfigure<AccountingProviderSwitchCutoverWorkerOptions>(options =>
        {
            options.PollIntervalSeconds = Math.Max(1, options.PollIntervalSeconds);
            options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 20);
            options.LeaseSeconds = Math.Max(15, options.LeaseSeconds);
            options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
        });
        services.AddSingleton<IAccountingProviderSwitchCutoverPolicy, AccountingProviderSwitchCutoverPolicy>();
        services.AddScoped<IAccountingProviderSwitchFinalTransferExecutor, FortnoxAccountingProviderSwitchFinalTransferExecutor>();
        services.AddScoped<AccountingProviderSwitchCutoverService>();
        services.AddScoped<IAccountingProviderSwitchCutoverService>(provider => provider.GetRequiredService<AccountingProviderSwitchCutoverService>());
        services.AddScoped<IAccountingProviderSwitchCutoverJobRunner>(provider => provider.GetRequiredService<AccountingProviderSwitchCutoverService>());
        services.AddOptions<AccountingProviderSwitchMonitoringOptions>()
            .Bind(configuration.GetSection(AccountingProviderSwitchMonitoringOptions.SectionName));
        services.PostConfigure<AccountingProviderSwitchMonitoringOptions>(options =>
        {
            options.PollIntervalSeconds = Math.Max(1, options.PollIntervalSeconds);
            options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 20);
            options.LeaseSeconds = Math.Max(15, options.LeaseSeconds);
            options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 10);
            options.DefaultWindowDays = Math.Clamp(options.DefaultWindowDays, 7, 30);
            options.CheckIntervalHours = Math.Clamp(options.CheckIntervalHours, 1, 24);
            options.SyncStaleHours = Math.Clamp(options.SyncStaleHours, 1, 168);
            options.StaleFreezeHours = Math.Clamp(options.StaleFreezeHours, 1, 48);
        });
        services.AddScoped<AccountingProviderSwitchMonitoringService>();
        services.AddScoped<IAccountingProviderSwitchMonitoringService>(provider => provider.GetRequiredService<AccountingProviderSwitchMonitoringService>());
        services.AddScoped<IAccountingProviderSwitchMonitoringJobRunner>(provider => provider.GetRequiredService<AccountingProviderSwitchMonitoringService>());
        services.AddScoped<IAccountingProviderSwitchAgentService, AccountingProviderSwitchAgentService>();
        services.AddScoped<IAccountingProviderExportAdapter, FortnoxAccountingProviderExportAdapter>();
        services.AddScoped<AccountingProviderExportService>();
        services.AddScoped<IAccountingProviderExportService>(provider => provider.GetRequiredService<AccountingProviderExportService>());
        services.AddScoped<IAccountingProviderExportExecutionTracker>(provider => provider.GetRequiredService<AccountingProviderExportService>());
        services.AddScoped<IFinanceDocumentActionProviderAdapter, FortnoxFinanceDocumentActionAdapter>();
        services.AddScoped<IFinanceCustomerDocumentProviderAdapter, FortnoxFinanceCustomerDocumentAdapter>();
        services.AddScoped<IFinanceAccountingActionService, FinanceAccountingActionService>();
        services.AddScoped<IAccountingAccountRoleResolver, AccountingAccountRoleResolver>();
        services.AddScoped<IAccountingAdministrationService, AccountingAdministrationService>();
        services.AddSingleton<AccountingDimensionTelemetry>();
        services.AddScoped<IAccountingDimensionPostingPolicy, AccountingDimensionPostingPolicy>();
        services.AddScoped<IAccountingDimensionService, AccountingDimensionService>();
        services.AddScoped<IAccountingPostingService, AccountingPostingService>();
        services.AddScoped<IAccountingJournalReadService, AccountingJournalReadService>();
        services.AddSingleton<AccountingScheduleTelemetry>();
        services.AddScoped<IAccountingScheduleService, AccountingScheduleService>();
        services.AddScoped<IAccountingCloseService, AccountingCloseService>();
        services.AddScoped<IAccountingCloseGovernanceService, AccountingCloseGovernanceService>();
        services.AddSingleton<AccountingCloseWorkspaceTelemetry>();
        services.AddScoped<IAccountingCloseWorkspaceService, AccountingCloseWorkspaceService>();
        services.AddSingleton<CloseComplianceReleaseReadinessTelemetry>();
        services.AddScoped<ICloseComplianceReleaseReadinessService, CloseComplianceReleaseReadinessService>();
        services.AddScoped<IYearEndRolloverService, YearEndRolloverService>();
        services.AddScoped<IFixedAssetService, FixedAssetService>();
        services.AddScoped<FixedAssetMaintenanceRunner>();
        services.AddScoped<IAccountingScheduleGenerationRunner, AccountingScheduleGenerationRunner>();
        services.AddScoped<IManualJournalPolicy, ManualJournalPolicy>();
        services.AddScoped<IManualJournalService, ManualJournalService>();
        services.AddSingleton<CustomerInvoiceDraftTelemetry>();
        services.AddScoped<ICustomerInvoiceDraftCalculationPolicy, CustomerInvoiceDraftCalculationPolicy>();
        services.AddScoped<ICustomerInvoiceDraftReadinessPolicy, CustomerInvoiceDraftReadinessPolicy>();
        services.AddScoped<ICustomerInvoiceDraftService, CustomerInvoiceDraftService>();
        services.AddSingleton<CustomerInvoiceScheduleTelemetry>();
        services.AddScoped<ICustomerInvoiceScheduleOccurrencePolicy, CustomerInvoiceScheduleOccurrencePolicy>();
        services.AddScoped<ICustomerInvoiceScheduleService, CustomerInvoiceScheduleService>();
        services.AddScoped<ICustomerInvoiceScheduleGenerationRunner, CustomerInvoiceScheduleGenerationRunner>();
        services.AddScoped<CustomerInvoiceDeliveryService>();
        services.AddScoped<ICustomerInvoiceDeliveryService>(provider => provider.GetRequiredService<CustomerInvoiceDeliveryService>());
        services.AddScoped<ICustomerInvoiceDeliveryDispatcher>(provider => provider.GetRequiredService<CustomerInvoiceDeliveryService>());
        services.AddScoped<CustomerCollectionsService>();
        services.AddSingleton<CustomerCollectionsTelemetry>();
        services.AddScoped<ICustomerCollectionsService>(provider => provider.GetRequiredService<CustomerCollectionsService>());
        services.AddScoped<ICustomerCollectionWorkerRunner, CustomerCollectionWorkerRunner>();
        services.AddScoped<ICustomerReminderDeliveryDispatcher, CustomerReminderDeliveryService>();
        services.AddScoped<CustomerInvoiceAccountingPolicy>();
        services.AddScoped<ICustomerInvoiceAccountingPolicy>(provider => provider.GetRequiredService<CustomerInvoiceAccountingPolicy>());
        services.AddSingleton<IAccountingTaxDecisionPolicy, AccountingTaxDecisionPolicy>();
        services.AddScoped<ICustomerInvoiceAccountingService, CustomerInvoiceAccountingService>();
        services.AddScoped<ICustomerInvoiceCorrectionPolicy, CustomerInvoiceCorrectionPolicy>();
        services.AddSingleton<CustomerInvoiceCorrectionTelemetry>();
        services.AddScoped<ICustomerInvoiceCorrectionService, CustomerInvoiceCorrectionService>();
        services.AddScoped<ICustomerInvoiceRefundExecutionRunner, CustomerInvoiceRefundExecutionRunner>();
        services.AddHostedService<CustomerInvoiceRefundExecutionBackgroundService>();
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
        services.AddScoped<IFinanceOperatingModeService, FinanceOperatingModeService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FinanceToolProviderOptions>, FinanceToolProviderOptionsValidator>());
        services.AddOptions<FinanceToolProviderOptions>()
            .Bind(configuration.GetSection(FinanceToolProviderOptions.SectionName))
            .PostConfigure(options =>
            {
                options.Provider = string.IsNullOrWhiteSpace(options.Provider)
                    ? FinanceToolProviderOptions.InternalProvider
                    : options.Provider.Trim();
            })
            .ValidateOnStart();
        services.AddOptions<FinanceAnomalyDetectionOptions>()
            .Bind(configuration.GetSection(FinanceAnomalyDetectionOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FortnoxOptions>, FortnoxOptionsValidator>());
        services.AddOptions<FortnoxOptions>()
            .Bind(configuration.GetSection(FortnoxOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<B2BRouterOptions>, B2BRouterOptionsValidator>());
        services.AddOptions<B2BRouterOptions>()
            .Bind(configuration.GetSection(B2BRouterOptions.SectionName))
            .PostConfigure(options =>
            {
                options.Environment = options.Environment?.Trim().ToLowerInvariant() ?? string.Empty;
                options.ApiBaseUrl = options.ApiBaseUrl?.Trim() ?? string.Empty;
                options.ApiVersion = options.ApiVersion?.Trim() ?? string.Empty;
                  options.AccountId = options.AccountId?.Trim() ?? string.Empty;
                  options.CompanyAccountIds = (options.CompanyAccountIds ?? new Dictionary<string, string>())
                      .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty,
                          StringComparer.OrdinalIgnoreCase);
                options.ApiKey = options.ApiKey?.Trim() ?? string.Empty;
                options.PaymentAccountId = options.PaymentAccountId?.Trim();
                options.PaymentAccountName = options.PaymentAccountName?.Trim();
                options.PaymentServiceProviderId = options.PaymentServiceProviderId?.Trim();
                options.WebhookSecret = options.WebhookSecret?.Trim() ?? string.Empty;
            })
            .ValidateOnStart();
        services.AddHttpClient(B2BRouterOptions.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<B2BRouterOptions>>().CurrentValue;
            client.BaseAddress = new Uri(options.ApiBaseUrl, UriKind.Absolute);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-B2B-API-Key", options.ApiKey);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-B2B-API-Version", options.ApiVersion);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<B2BRouterCustomerInvoiceElectronicDeliveryProvider>();
        services.AddScoped<ICustomerInvoiceElectronicDeliveryProvider>(provider => provider.GetRequiredService<B2BRouterCustomerInvoiceElectronicDeliveryProvider>());
        services.AddSingleton<B2BRouterTelemetry>();
        services.AddHealthChecks().AddCheck<B2BRouterHealthCheck>("b2brouter-peppol", tags: ["ready", "finance", "integration"]);
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
        services.AddOptions<AccountingExportWorkerOptions>()
            .Bind(configuration.GetSection(AccountingExportWorkerOptions.SectionName))
            .PostConfigure(options => options.PollIntervalMilliseconds = Math.Max(100, options.PollIntervalMilliseconds));
        services.AddOptions<CustomerInvoiceScheduleGenerationWorkerOptions>()
            .Bind(configuration.GetSection(CustomerInvoiceScheduleGenerationWorkerOptions.SectionName))
            .PostConfigure(options =>
            {
                options.PollIntervalMilliseconds = Math.Clamp(options.PollIntervalMilliseconds, 250, 60000);
                options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 100);
                options.LeaseSeconds = Math.Clamp(options.LeaseSeconds, 30, 900);
                options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 20);
                options.BaseRetryDelaySeconds = Math.Clamp(options.BaseRetryDelaySeconds, 1, 3600);
                options.MaximumRetryDelaySeconds = Math.Clamp(
                    options.MaximumRetryDelaySeconds,
                    options.BaseRetryDelaySeconds,
                    86400);
            });
        services.AddOptions<AccountingScheduleWorkerOptions>()
            .Bind(configuration.GetSection(AccountingScheduleWorkerOptions.SectionName))
            .PostConfigure(options =>
            {
                options.PollIntervalSeconds = Math.Clamp(options.PollIntervalSeconds, 5, 3600);
                options.ClaimBatchSize = Math.Clamp(options.ClaimBatchSize, 1, 100);
                options.LeaseSeconds = Math.Clamp(options.LeaseSeconds, 30, 900);
                options.MaximumAttempts = Math.Clamp(options.MaximumAttempts, 1, 20);
                options.BaseRetryDelaySeconds = Math.Clamp(options.BaseRetryDelaySeconds, 1, 3600);
                options.MaximumRetryDelaySeconds = Math.Clamp(options.MaximumRetryDelaySeconds,
                    options.BaseRetryDelaySeconds, 86400);
            });
        services.AddOptions<FinanceAnalyticsStartupRefreshOptions>()
            .Bind(configuration.GetSection(FinanceAnalyticsStartupRefreshOptions.SectionName))
            .PostConfigure(options => options.CompanyBatchSize = Math.Clamp(options.CompanyBatchSize, 1, 5000));
        services.AddOptions<FinanceBillRegistrationReconciliationOptions>()
            .Bind(configuration.GetSection(FinanceBillRegistrationReconciliationOptions.SectionName))
            .PostConfigure(options => options.BatchSize = Math.Clamp(options.BatchSize, 1, 5000));
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FinanceSeedBackfillWorkerOptions>, FinanceSeedBackfillWorkerOptionsValidator>());
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
            })
            .ValidateOnStart();
        services.AddOptions<CustomerCollectionWorkerOptions>()
            .Bind(configuration.GetSection(CustomerCollectionWorkerOptions.SectionName))
            .Validate(x => x.PollIntervalMilliseconds >= 10000 && x.PollIntervalMilliseconds <= 3600000,
                "Customer collection polling must be between 10 seconds and 1 hour.")
            .Validate(x => x.BatchSize is >= 1 and <= 200,
                "Customer collection batch size must be between 1 and 200.")
            .Validate(x => x.LeaseSeconds is >= 30 and <= 900,
                "Customer collection lease duration must be between 30 and 900 seconds.")
            .Validate(x => x.MaximumAttempts is >= 1 and <= 20,
                "Customer collection maximum attempts must be between 1 and 20.")
            .Validate(x => x.BaseRetryDelaySeconds is >= 1 and <= 3600 &&
                x.MaximumRetryDelaySeconds >= x.BaseRetryDelaySeconds && x.MaximumRetryDelaySeconds <= 86400,
                "Customer collection retry delays are outside the supported bounds.")
            .ValidateOnStart();
        services.AddHostedService<CustomerCollectionBackgroundService>();
        services.AddHttpClient(OpenAiPdfOcrTextExtractor.ClientName);
        services.AddScoped<InternalFinanceToolProvider>();
        if (configuration.GetValue<bool>("FinanceTools:AllowMockProvider"))
        {
            services.AddScoped<MockFinanceToolProvider>();
        }
        services.AddScoped<IFinanceCommandService, CompanyFinanceCommandService>();
        services.AddScoped<IFinanceAgentInsightRepository, FinanceAgentInsightRepository>();
        services.AddScoped<IFinanceInsightPersistenceService, FinanceInsightPersistenceService>();
        services.AddScoped<IFinancePaymentCommandService, CompanyFinanceCommandService>();
        services.AddScoped<CompanyCashSettlementPostingService>();
        services.AddScoped<IFinanceCashSettlementPostingService>(provider => provider.GetRequiredService<CompanyCashSettlementPostingService>());
        services.AddScoped<FinancePaymentAllocationService>(provider => new FinancePaymentAllocationService(
            provider.GetRequiredService<VirtualCompany.Infrastructure.Persistence.VirtualCompanyDbContext>(),
            provider.GetRequiredService<IFinanceCashSettlementPostingService>(),
            provider.GetRequiredService<IExchangeRateService>(),
            provider.GetRequiredService<IAccountingPostingService>(),
            provider.GetRequiredService<ForeignCurrencySettlementTelemetry>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IFinanceApprovalTaskService, CompanyFinanceApprovalTaskService>();
        services.AddScoped<ICashPostingTraceabilityBackfillService, CompanyCashPostingTraceabilityBackfillService>();
        services.AddScoped<CompanyBankTransactionService>();
        services.AddScoped<IBankTransactionReadService>(provider => provider.GetRequiredService<CompanyBankTransactionService>());
        services.AddScoped<IBankTransactionCommandService>(provider => provider.GetRequiredService<CompanyBankTransactionService>());
        services.AddScoped<IBankStatementImportCenterService, BankStatementImportCenterService>();
        services.AddScoped<IFinancePolicyConfigurationService, CompanyFinanceCommandService>();
        services.AddScoped<IFinancialStatementMappingService, CompanyFinancialStatementMappingService>();
        services.AddScoped<IExecutiveCockpitFinanceAdapter, CompanyExecutiveCockpitFinanceAdapter>();
        services.AddScoped<ITodayWorkspaceContributor, FinanceTodayWorkspaceContributor>();
        services.AddScoped<IMonthlyWorkspaceContributor, FinanceMonthlyWorkspaceContributor>();
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
        services.AddScoped<ICurrencyRevaluationService, CurrencyRevaluationService>();
        services.AddScoped<IAccountingReportingService, AccountingReportingService>();
        services.AddScoped<IFinancialReportSuiteService, FinancialReportSuiteService>();
        services.AddScoped<IReportDefinitionService, ReportDefinitionService>();
        services.AddScoped<IFinanceLedgerAgentReadService, FinanceLedgerAgentReadService>();
        services.AddScoped<IFinanceCloseComplianceAgentService, FinanceCloseComplianceAgentService>();
        services.AddScoped<IFinanceAdvancedAccountingAgentService, FinanceAdvancedAccountingAgentService>();
        services.AddScoped<IFinanceAccountingDraftAgentService, FinanceAccountingDraftAgentService>();
        services.AddScoped<IFinanceOperationalProposalAgentService, FinanceOperationalProposalAgentService>();
        services.AddScoped<IFinanceGuardedCommandService, FinanceGuardedCommandService>();
        services.AddScoped<IVatReturnService, VatReturnService>();
        services.AddSingleton<ComplianceObligationTelemetry>();
        services.AddScoped<IComplianceObligationService, ComplianceObligationService>();
        services.AddScoped<IAuditPackageService, AuditPackageService>();
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
        services.AddScoped<IFinancePlanningEntityResolver, FinancePlanningEntityResolver>();
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
        services.AddSingleton<ITreasuryWorkspacePolicy, TreasuryWorkspacePolicy>();
        services.AddScoped<ITreasuryWorkspaceQueryService, TreasuryWorkspaceQueryService>();
        services.AddScoped<IConnectedBankingReadinessService, ConnectedBankingReadinessService>();
        services.AddScoped<IConnectedBankingRecoveryVerificationService, ConnectedBankingRecoveryVerificationService>();
        services.AddScoped<IReconciliationScoringSettingsProvider, CompanyReconciliationScoringSettingsProvider>();
        services.AddScoped<IReconciliationScoringService, CompanyReconciliationScoringService>();
        services.AddScoped<IReconciliationSuggestionReadService, CompanyReconciliationSuggestionService>();
        services.AddScoped<IReconciliationSuggestionCommandService, CompanyReconciliationSuggestionService>();
        services.AddScoped<AdvancedReconciliationService>();
        services.AddScoped<IAdvancedReconciliationReadService>(provider => provider.GetRequiredService<AdvancedReconciliationService>());
        services.AddScoped<IAdvancedReconciliationCommandService>(provider => provider.GetRequiredService<AdvancedReconciliationService>());
        services.AddScoped<TreasuryMovementService>();
        services.AddScoped<ITreasuryMovementReadService>(provider => provider.GetRequiredService<TreasuryMovementService>());
        services.AddScoped<ITreasuryMovementCommandService>(provider => provider.GetRequiredService<TreasuryMovementService>());
        services.AddScoped<IPaymentBatchEligibilityPolicy, PaymentBatchEligibilityPolicy>();
        services.AddScoped<IPaymentBatchService, PaymentBatchService>();
        services.AddScoped<PaymentExecutionAuthorityValidator>();
        services.AddScoped<IPaymentBatchExecutionService, PaymentBatchExecutionService>();
        services.AddScoped<IPaymentBatchExecutionDispatcher, PaymentBatchExecutionDispatcher>();
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
        services.AddHostedService<AccountingProviderSwitchAssessmentBackgroundService>();
        services.AddHostedService<AccountingProviderSwitchRehearsalBackgroundService>();
        services.AddHostedService<AccountingProviderSwitchPreparationBackgroundService>();
        services.AddHostedService<AccountingProviderSwitchTargetTransferBackgroundService>();
        services.AddHostedService<AccountingProviderSwitchCutoverBackgroundService>();
        services.AddHostedService<AccountingProviderSwitchMonitoringBackgroundService>();
        services.AddHostedService<FinanceApprovalTaskBackfillBackgroundService>();
        services.AddHostedService<CustomerInvoiceScheduleGenerationBackgroundService>();
        services.AddHostedService<AccountingScheduleGenerationBackgroundService>();
        services.AddHostedService<FinanceInsightsSnapshotBackgroundService>();
        services.AddHostedService<FinanceAnalyticsStartupRefreshBackgroundService>();
        services.AddHostedService<FinanceIntegrationStartupSyncBackgroundService>();
        services.AddHostedService<BankConsentRevocationBackgroundService>();
        services.AddHostedService<BankFeedSynchronizationBackgroundService>();
        services.AddHostedService<ExchangeRateRefreshBackgroundService>();
        services.AddHostedService<CurrencyRevaluationBackgroundService>();
        services.AddHostedService<FixedAssetMaintenanceBackgroundService>();
        services.AddHostedService<AuditPackageBackgroundService>();
        services.AddHostedService<FinanceBillFortnoxRegistrationReconciliationBackgroundService>();
        services.AddHostedService<FinanceSeedBackgroundService>();
        return services;
    }
}



