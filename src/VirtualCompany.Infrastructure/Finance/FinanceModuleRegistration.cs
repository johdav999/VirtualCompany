using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

internal static class FinanceModuleRegistration
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
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
        return services;
    }
}
