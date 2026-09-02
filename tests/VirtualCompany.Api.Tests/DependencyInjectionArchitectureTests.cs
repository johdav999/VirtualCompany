using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Focus;
using VirtualCompany.Infrastructure;
using VirtualCompany.Infrastructure.Activity;

namespace VirtualCompany.Api.Tests;

public sealed class DependencyInjectionArchitectureTests
{
    private static readonly Type[] RequiredServiceTypes =
    [
        typeof(IApprovalRequestService),
        typeof(IAgentStaffOverviewQueryService),
        typeof(IFinanceReadService),
        typeof(IFinanceWorkerOperationsService),
        typeof(IFinanceSupplierPaymentProposalService),
        typeof(IAccountingDimensionService),
        typeof(IAccountingDimensionPostingPolicy),
        typeof(ICurrencyRevaluationService),
        typeof(IFinanceAutonomyWorkflowTemplateService),
        typeof(IFinanceAutonomyWorkflowOutcomeService),
        typeof(ISalesOperationsService),
        typeof(ISupportCaseService),
        typeof(ISupportReplyDraftService),
        typeof(IMailboxConnectionService)
    ];

    private static readonly IReadOnlyDictionary<Type, ServiceLifetime> RequiredLifetimes =
        new Dictionary<Type, ServiceLifetime>
        {
            [typeof(IApprovalRequestService)] = ServiceLifetime.Scoped,
            [typeof(IAgentStaffOverviewQueryService)] = ServiceLifetime.Scoped,
            [typeof(IFinanceReadService)] = ServiceLifetime.Scoped,
            [typeof(IFinanceWorkerOperationsService)] = ServiceLifetime.Scoped,
            [typeof(IFinanceSupplierPaymentProposalService)] = ServiceLifetime.Scoped,
            [typeof(IAccountingDimensionService)] = ServiceLifetime.Scoped,
            [typeof(IAccountingDimensionPostingPolicy)] = ServiceLifetime.Scoped,
            [typeof(ICurrencyRevaluationService)] = ServiceLifetime.Scoped,
            [typeof(IFinanceAutonomyWorkflowTemplateService)] = ServiceLifetime.Scoped,
            [typeof(IFinanceAutonomyWorkflowOutcomeService)] = ServiceLifetime.Scoped,
            [typeof(ISalesOperationsService)] = ServiceLifetime.Scoped,
            [typeof(ISupportCaseService)] = ServiceLifetime.Scoped,
            [typeof(ISupportReplyDraftService)] = ServiceLifetime.Scoped,
            [typeof(IMailboxConnectionService)] = ServiceLifetime.Scoped
        };

    private static readonly string[] ExpectedHostedServiceTypes =
    [
        "AccountingExportBackgroundService",
        "AccountingMigrationBackgroundService",
        "AccountingPolicyPackCatalogStartupValidator",
        "AccountingProviderSwitchAssessmentBackgroundService",
        "AccountingProviderSwitchCutoverBackgroundService",
        "AccountingProviderSwitchMonitoringBackgroundService",
        "AccountingProviderSwitchPreparationBackgroundService",
        "AccountingProviderSwitchRehearsalBackgroundService",
        "AccountingProviderSwitchTargetTransferBackgroundService",
        "AccountingScheduleGenerationBackgroundService",
        "AgentMemoryCandidateExpiryWorker",
        "AgentScheduledTriggerSchedulerBackgroundService",
        "AuditPackageBackgroundService",
        "BankConsentRevocationBackgroundService",
        "BankFeedSynchronizationBackgroundService",
        "BriefingSchedulerBackgroundService",
        "BriefingUpdateJobBackgroundService",
        "CampaignSchedulingBackgroundService",
        "CompanyKnowledgeIndexingBackgroundService",
        "CompanyOperatingCycleScheduler",
        "CompanyOutboxDispatcherBackgroundService",
        "CompanySimulationProgressionBackgroundService",
        "CurrencyRevaluationBackgroundService",
        "CustomerCollectionBackgroundService",
        "CustomerInvoiceRefundExecutionBackgroundService",
        "CustomerInvoiceScheduleGenerationBackgroundService",
        "DataProtectionHostedService",
        "ExchangeRateRefreshBackgroundService",
        "FinanceAnalyticsStartupRefreshBackgroundService",
        "FinanceApprovalTaskBackfillBackgroundService",
        "FinanceAutonomyApprovalBackgroundService",
        "FinanceAutonomyExecutorBackgroundService",
        "FinanceAutonomyTriggerBackgroundService",
        "FinanceBillFortnoxRegistrationReconciliationBackgroundService",
        "FinanceConversationRunBackgroundService",
        "FinanceInsightsSnapshotBackgroundService",
        "FinanceIntegrationStartupSyncBackgroundService",
        "FinanceSeedBackfillBackgroundService",
        "FinanceSeedBackgroundService",
        "FixedAssetMaintenanceBackgroundService",
        "GuidedRealtimeRecoveryWorker",
        "GuidedWorkRetentionWorker",
        "HealthCheckPublisherHostedService",
        "MailboxConnectionStartupRefreshBackgroundService",
        "MarketingChannelDispatchBackgroundService",
        "MarketingEventScannerBackgroundService",
        "MarketingJourneyBackgroundService",
        "OperatingWorkDispatchBackgroundService",
        "PipelineRiskScoringBackgroundService",
        "ProspectingRunBackgroundService",
        "ReportingPeriodRegenerationBackgroundService",
        "RoleAgentCadenceBackgroundService",
        "SequenceExecutionBackgroundService",
        "StandardMailboxInboundSyncBackgroundService",
        "SupportOperationsBackgroundService",
        "TriggerEvaluationBackgroundService",
        "WorkflowProgressionBackgroundService",
        "WorkflowSchedulerBackgroundService"
    ];

    [Fact]
    public void Production_composition_has_one_effective_registration_for_required_services()
    {
        var services = CreateProductionServices();

        foreach (var serviceType in RequiredServiceTypes)
        {
            var descriptors = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
            Assert.True(
                descriptors.Length == 1,
                $"{serviceType.FullName} must have exactly one effective registration, but found {descriptors.Length}.");
            Assert.Equal(RequiredLifetimes[serviceType], descriptors[0].Lifetime);
        }
    }

    [Fact]
    public void Production_composition_resolves_required_services_with_valid_lifetimes()
    {
        var services = CreateProductionServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        foreach (var serviceType in RequiredServiceTypes)
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
        }
    }

    [Fact]
    public void Every_hosted_service_is_registered_once()
    {
        var services = CreateProductionServices();
        var hostedDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ToArray();
        var duplicateTypes = hostedDescriptors
            .Where(descriptor => descriptor.ImplementationType is not null)
            .GroupBy(descriptor => descriptor.ImplementationType!)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.FullName} ({group.Count()} registrations)")
            .ToArray();

        Assert.NotEmpty(hostedDescriptors);
        Assert.True(
            duplicateTypes.Length == 0,
            $"Hosted services must be registered once. Duplicates: {string.Join(", ", duplicateTypes)}.");

        var actualTypes = hostedDescriptors
            .Select(descriptor => descriptor.ImplementationType?.Name ?? "<factory>")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedHostedServiceTypes, actualTypes);
    }

    [Fact]
    public void Provider_collections_preserve_membership_and_order()
    {
        var services = CreateProductionServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        AssertTypeNames(
            scope.ServiceProvider.GetServices<IMailboxProviderClient>(),
            "GmailMailboxProviderClient",
            "Microsoft365MailboxProviderClient",
            "StandardMailboxProviderClient");
        AssertTypeNames(
            scope.ServiceProvider.GetServices<IMailboxAuthenticationStrategy>(),
            "ApplicationPasswordMailboxAuthenticationStrategy",
            "OAuthMailboxAuthenticationStrategy");
        AssertTypeNames(
            scope.ServiceProvider.GetServices<IDocumentTextExtractor>(),
            "PdfDocumentTextExtractor",
            "OpenAiPdfOcrTextExtractor",
            "DocxDocumentTextExtractor",
            "EmailBodyTextExtractor");
        AssertTypeNames(
            scope.ServiceProvider.GetServices<IFocusCandidateSource>(),
            "ApprovalFocusCandidateSource",
            "TaskFocusCandidateSource",
            "AlertAnomalyFocusCandidateSource",
            "FinanceAlertFocusCandidateSource");
    }

    [Fact]
    public void Effective_factory_registrations_preserve_selected_implementations()
    {
        var services = CreateProductionServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Equal("InternalFinanceToolProvider", scope.ServiceProvider.GetRequiredService<IFinanceToolProvider>().GetType().Name);
        Assert.Equal("FortnoxFinanceIntegrationProvider", scope.ServiceProvider.GetRequiredService<IFinanceIntegrationProvider>().GetType().Name);
    }

    private static void AssertTypeNames<T>(IEnumerable<T> services, params string[] expectedNames)
    {
        var actualNames = services.Select(service => service!.GetType().Name).ToArray();
        Assert.Equal(expectedNames, actualNames);
    }

    private static IServiceCollection CreateProductionServices()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:VirtualCompanyDb"] = "Server=localhost;Database=virtualcompany_architecture_tests;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False",
                ["Database:Provider"] = "SqlServer",
                ["MailboxIntegration:PublicBaseUrl"] = "https://localhost",
                ["MailboxIntegration:Gmail:ClientId"] = "architecture-test-client",
                ["MailboxIntegration:Gmail:ClientSecret"] = "architecture-test-secret",
                ["MailboxIntegration:Microsoft365:ClientId"] = "architecture-test-client",
                ["MailboxIntegration:Microsoft365:ClientSecret"] = "architecture-test-secret",
                ["SimulationFeatures:DisabledMessage"] = "Simulation is disabled for architecture tests."
            });

        builder.Services.AddControllers();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IActivityEventPublisher, SignalRActivityEventPublisher>();
        builder.Services.AddVirtualCompanyInfrastructure(builder.Configuration);
        return builder.Services;
    }
}
