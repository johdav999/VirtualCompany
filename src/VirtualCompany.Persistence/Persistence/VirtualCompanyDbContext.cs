using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class VirtualCompanyDbContext : DbContext
{
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IExecutiveCockpitDashboardCacheInvalidator? _dashboardCacheInvalidator;

    public VirtualCompanyDbContext(
        DbContextOptions<VirtualCompanyDbContext> options,
        ICompanyContextAccessor? companyContextAccessor = null,
        IExecutiveCockpitDashboardCacheInvalidator? dashboardCacheInvalidator = null)
        : base(options)
    {
        _companyContextAccessor = companyContextAccessor;
        _dashboardCacheInvalidator = dashboardCacheInvalidator;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserPreferenceChange> UserPreferenceChanges => Set<UserPreferenceChange>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<CompanyOutboxMessage> CompanyOutboxMessages => Set<CompanyOutboxMessage>();
    public DbSet<BackgroundExecution> BackgroundExecutions => Set<BackgroundExecution>();
    public DbSet<BackgroundExecutionAttempt> BackgroundExecutionAttempts => Set<BackgroundExecutionAttempt>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<ExecutionExceptionRecord> ExecutionExceptionRecords => Set<ExecutionExceptionRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CompanyOwnedNote> CompanyNotes => Set<CompanyOwnedNote>();
    public DbSet<CompanySetupTemplate> CompanySetupTemplates => Set<CompanySetupTemplate>();
    public DbSet<CompanyKnowledgeDocument> CompanyKnowledgeDocuments => Set<CompanyKnowledgeDocument>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<CompanyKnowledgeChunk> CompanyKnowledgeChunks => Set<CompanyKnowledgeChunk>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<CompanyGoal> CompanyGoals => Set<CompanyGoal>();
    public DbSet<CompanyOperatingConfiguration> CompanyOperatingConfigurations => Set<CompanyOperatingConfiguration>();
    public DbSet<OperatingCycle> OperatingCycles => Set<OperatingCycle>();
    public DbSet<OperatingPlan> OperatingPlans => Set<OperatingPlan>();
    public DbSet<OperatingInitiative> OperatingInitiatives => Set<OperatingInitiative>();
    public DbSet<OperatingPlanDependency> OperatingPlanDependencies => Set<OperatingPlanDependency>();
    public DbSet<OperatingDecision> OperatingDecisions => Set<OperatingDecision>();
    public DbSet<OperatingValidationResult> OperatingValidationResults => Set<OperatingValidationResult>();
    public DbSet<OperatingReview> OperatingReviews => Set<OperatingReview>();
    public DbSet<OperatingDispatch> OperatingDispatches => Set<OperatingDispatch>();
    public DbSet<OperatingInitiativeCollaborator> OperatingInitiativeCollaborators => Set<OperatingInitiativeCollaborator>();
    public DbSet<OperatingEvent> OperatingEvents => Set<OperatingEvent>();
    public DbSet<OperatingCycleRequest> OperatingCycleRequests => Set<OperatingCycleRequest>();
    public DbSet<CompanyOperatingLease> CompanyOperatingLeases => Set<CompanyOperatingLease>();
    public DbSet<OperatingSnapshot> OperatingSnapshots => Set<OperatingSnapshot>();
    public DbSet<ToolExecutionAttempt> ToolExecutionAttempts => Set<ToolExecutionAttempt>();
    public DbSet<AgentScheduledTrigger> AgentScheduledTriggers => Set<AgentScheduledTrigger>();
    public DbSet<TriggerExecutionAttempt> TriggerExecutionAttempts => Set<TriggerExecutionAttempt>();
    public DbSet<AgentScheduledTriggerEnqueueWindow> AgentScheduledTriggerEnqueueWindows => Set<AgentScheduledTriggerEnqueueWindow>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<AgentTaskCreationDedupeRecord> AgentTaskCreationDedupeRecords => Set<AgentTaskCreationDedupeRecord>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ConversationTaskLink> ConversationTaskLinks => Set<ConversationTaskLink>();
    public DbSet<GuidedWorkSession> GuidedWorkSessions => Set<GuidedWorkSession>();
    public DbSet<GuidedDraftField> GuidedDraftFields => Set<GuidedDraftField>();
    public DbSet<GuidedSessionOperation> GuidedSessionOperations => Set<GuidedSessionOperation>();
    public DbSet<GuidedVoiceBinding> GuidedVoiceBindings => Set<GuidedVoiceBinding>();
    public DbSet<CompanyBriefing> CompanyBriefings => Set<CompanyBriefing>();
    public DbSet<CompanyBriefingSection> CompanyBriefingSections => Set<CompanyBriefingSection>();
    public DbSet<CompanyBriefingContribution> CompanyBriefingContributions => Set<CompanyBriefingContribution>();
    public DbSet<CompanyBriefingDeliveryPreference> CompanyBriefingDeliveryPreferences => Set<CompanyBriefingDeliveryPreference>();
    public DbSet<CompanyBriefingSeverityRule> CompanyBriefingSeverityRules => Set<CompanyBriefingSeverityRule>();
    public DbSet<UserBriefingPreference> UserBriefingPreferences => Set<UserBriefingPreference>();
    public DbSet<TenantBriefingDefault> TenantBriefingDefaults => Set<TenantBriefingDefault>();
    public DbSet<CompanyBriefingUpdateJob> CompanyBriefingUpdateJobs => Set<CompanyBriefingUpdateJob>();
    public DbSet<CompanyNotification> CompanyNotifications => Set<CompanyNotification>();
    public DbSet<ProactiveMessage> ProactiveMessages => Set<ProactiveMessage>();
    public DbSet<ProactiveMessagePolicyDecision> ProactiveMessagePolicyDecisions => Set<ProactiveMessagePolicyDecision>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowTrigger> WorkflowTriggers => Set<WorkflowTrigger>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<ProcessedWorkflowTriggerEvent> ProcessedWorkflowTriggerEvents => Set<ProcessedWorkflowTriggerEvent>();
    public DbSet<WorkflowException> WorkflowExceptions => Set<WorkflowException>();
    public DbSet<ConditionTriggerEvaluation> ConditionTriggerEvaluations => Set<ConditionTriggerEvaluation>();
    public DbSet<MemoryItem> MemoryItems => Set<MemoryItem>();
    public DbSet<CustomerMemoryProfile> CustomerMemoryProfiles => Set<CustomerMemoryProfile>();
    public DbSet<CustomerMemoryProfileConversation> CustomerMemoryProfileConversations => Set<CustomerMemoryProfileConversation>();
    public DbSet<CustomerMemoryProfileDeal> CustomerMemoryProfileDeals => Set<CustomerMemoryProfileDeal>();
    public DbSet<CustomerMemoryProfileEngagementAttribute> CustomerMemoryProfileEngagementAttributes => Set<CustomerMemoryProfileEngagementAttribute>();
    public DbSet<CustomerMemoryProfilePreference> CustomerMemoryProfilePreferences => Set<CustomerMemoryProfilePreference>();
    public DbSet<CustomerMemoryProfilePriceSignal> CustomerMemoryProfilePriceSignals => Set<CustomerMemoryProfilePriceSignal>();
    public DbSet<CustomerMemoryProfileIndustrySignal> CustomerMemoryProfileIndustrySignals => Set<CustomerMemoryProfileIndustrySignal>();
    public DbSet<Company> CompanyOnboardingDrafts => Set<Company>();
    public DbSet<ContextRetrieval> ContextRetrievals => Set<ContextRetrieval>();
    public DbSet<ContextRetrievalSource> ContextRetrievalSources => Set<ContextRetrievalSource>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Escalation> Escalations => Set<Escalation>();
    public DbSet<InsightAcknowledgment> InsightAcknowledgments => Set<InsightAcknowledgment>();
    public DbSet<DashboardDepartmentConfig> DashboardDepartmentConfigs => Set<DashboardDepartmentConfig>();
    public DbSet<DashboardWidgetConfig> DashboardWidgetConfigs => Set<DashboardWidgetConfig>();
    public DbSet<FinanceAccount> FinanceAccounts => Set<FinanceAccount>();
    public DbSet<AccountingConfiguration> AccountingConfigurations => Set<AccountingConfiguration>();
    public DbSet<AccountingConfigurationAccountRole> AccountingConfigurationAccountRoles => Set<AccountingConfigurationAccountRole>();
    public DbSet<AccountingPolicyPackSelection> AccountingPolicyPackSelections => Set<AccountingPolicyPackSelection>();
    public DbSet<AccountingAuthorityPeriod> AccountingAuthorityPeriods => Set<AccountingAuthorityPeriod>();
    public DbSet<AccountingProviderSwitch> AccountingProviderSwitches => Set<AccountingProviderSwitch>();
    public DbSet<AccountingProviderSwitchAssessment> AccountingProviderSwitchAssessments => Set<AccountingProviderSwitchAssessment>();
    public DbSet<AccountingProviderSwitchCapability> AccountingProviderSwitchCapabilities => Set<AccountingProviderSwitchCapability>();
    public DbSet<AccountingProviderSwitchDataset> AccountingProviderSwitchDatasets => Set<AccountingProviderSwitchDataset>();
    public DbSet<AccountingProviderSwitchGap> AccountingProviderSwitchGaps => Set<AccountingProviderSwitchGap>();
    public DbSet<AccountingProviderSwitchStagedRecord> AccountingProviderSwitchStagedRecords => Set<AccountingProviderSwitchStagedRecord>();
    public DbSet<AccountingProviderSwitchMappingSet> AccountingProviderSwitchMappingSets => Set<AccountingProviderSwitchMappingSet>();
    public DbSet<AccountingProviderSwitchMappingDecision> AccountingProviderSwitchMappingDecisions => Set<AccountingProviderSwitchMappingDecision>();
    public DbSet<AccountingProviderSwitchMappingRecord> AccountingProviderSwitchMappingRecords => Set<AccountingProviderSwitchMappingRecord>();
    public DbSet<AccountingProviderSwitchRehearsal> AccountingProviderSwitchRehearsals => Set<AccountingProviderSwitchRehearsal>();
    public DbSet<AccountingProviderSwitchRehearsalInput> AccountingProviderSwitchRehearsalInputs => Set<AccountingProviderSwitchRehearsalInput>();
    public DbSet<AccountingProviderSwitchRehearsalDatasetResult> AccountingProviderSwitchRehearsalDatasetResults => Set<AccountingProviderSwitchRehearsalDatasetResult>();
    public DbSet<AccountingProviderSwitchReconciliationCheck> AccountingProviderSwitchReconciliationChecks => Set<AccountingProviderSwitchReconciliationCheck>();
    public DbSet<AccountingProviderSwitchManualEvidence> AccountingProviderSwitchManualEvidence => Set<AccountingProviderSwitchManualEvidence>();
    public DbSet<AccountingProviderSwitchCutoverPlan> AccountingProviderSwitchCutoverPlans => Set<AccountingProviderSwitchCutoverPlan>();
    public DbSet<AccountingProviderSwitchPlanApproval> AccountingProviderSwitchPlanApprovals => Set<AccountingProviderSwitchPlanApproval>();
    public DbSet<AccountingProviderSwitchPreparation> AccountingProviderSwitchPreparations => Set<AccountingProviderSwitchPreparation>();
    public DbSet<AccountingProviderSwitchReadinessCheck> AccountingProviderSwitchReadinessChecks => Set<AccountingProviderSwitchReadinessCheck>();
    public DbSet<AccountingProviderSwitchNativeCandidate> AccountingProviderSwitchNativeCandidates => Set<AccountingProviderSwitchNativeCandidate>();
    public DbSet<AccountingProviderSwitchCandidateValidation> AccountingProviderSwitchCandidateValidations => Set<AccountingProviderSwitchCandidateValidation>();
    public DbSet<AccountingProviderSwitchArchiveDependency> AccountingProviderSwitchArchiveDependencies => Set<AccountingProviderSwitchArchiveDependency>();
    public DbSet<AccountingProviderSwitchTargetTransferBatch> AccountingProviderSwitchTargetTransferBatches => Set<AccountingProviderSwitchTargetTransferBatch>();
    public DbSet<AccountingProviderSwitchTargetTransferItem> AccountingProviderSwitchTargetTransferItems => Set<AccountingProviderSwitchTargetTransferItem>();
    public DbSet<AccountingProviderSwitchTargetTransferAttempt> AccountingProviderSwitchTargetTransferAttempts => Set<AccountingProviderSwitchTargetTransferAttempt>();
    public DbSet<AccountingProviderSwitchTargetAcknowledgement> AccountingProviderSwitchTargetAcknowledgements => Set<AccountingProviderSwitchTargetAcknowledgement>();
    public DbSet<AccountingProviderSwitchCutoverExecution> AccountingProviderSwitchCutoverExecutions => Set<AccountingProviderSwitchCutoverExecution>();
    public DbSet<AccountingProviderSwitchFinalSnapshot> AccountingProviderSwitchFinalSnapshots => Set<AccountingProviderSwitchFinalSnapshot>();
    public DbSet<AccountingProviderSwitchFinalCheck> AccountingProviderSwitchFinalChecks => Set<AccountingProviderSwitchFinalCheck>();
    public DbSet<AccountingProviderSwitchActivationApproval> AccountingProviderSwitchActivationApprovals => Set<AccountingProviderSwitchActivationApproval>();
    public DbSet<AccountingProviderSwitchNativeMaterialization> AccountingProviderSwitchNativeMaterializations => Set<AccountingProviderSwitchNativeMaterialization>();
    public DbSet<AccountingProviderSwitchMonitoringRun> AccountingProviderSwitchMonitoringRuns => Set<AccountingProviderSwitchMonitoringRun>();
    public DbSet<AccountingProviderSwitchMonitoringCheck> AccountingProviderSwitchMonitoringChecks => Set<AccountingProviderSwitchMonitoringCheck>();
    public DbSet<AccountingProviderSwitchMonitoringIncident> AccountingProviderSwitchMonitoringIncidents => Set<AccountingProviderSwitchMonitoringIncident>();
    public DbSet<AccountingProviderExport> AccountingProviderExports => Set<AccountingProviderExport>();
    public DbSet<AccountingMigrationRun> AccountingMigrationRuns => Set<AccountingMigrationRun>();
    public DbSet<AccountingMigrationConflict> AccountingMigrationConflicts => Set<AccountingMigrationConflict>();
    public DbSet<AccountingCutoverReport> AccountingCutoverReports => Set<AccountingCutoverReport>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CompanyBankAccount> CompanyBankAccounts => Set<CompanyBankAccount>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Forecast> Forecasts => Set<Forecast>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<BankTransactionPaymentLink> BankTransactionPaymentLinks => Set<BankTransactionPaymentLink>();
    public DbSet<BankTransactionPostingStateRecord> BankTransactionPostingStateRecords => Set<BankTransactionPostingStateRecord>();
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    public DbSet<BankStatementImportRow> BankStatementImportRows => Set<BankStatementImportRow>();
    public DbSet<BankReconciliationFollowUp> BankReconciliationFollowUps => Set<BankReconciliationFollowUp>();
    public DbSet<BankTransactionCashLedgerLink> BankTransactionCashLedgerLinks => Set<BankTransactionCashLedgerLink>();
    public DbSet<ReconciliationSuggestionRecord> ReconciliationSuggestionRecords => Set<ReconciliationSuggestionRecord>();
    public DbSet<PaymentCashLedgerLink> PaymentCashLedgerLinks => Set<PaymentCashLedgerLink>();
    public DbSet<ReconciliationResultRecord> ReconciliationResultRecords => Set<ReconciliationResultRecord>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<FinanceTransaction> FinanceTransactions => Set<FinanceTransaction>();
    public DbSet<FinanceInvoice> FinanceInvoices => Set<FinanceInvoice>();
    public DbSet<FinanceCounterparty> FinanceCounterparties => Set<FinanceCounterparty>();
    public DbSet<FinanceBill> FinanceBills => Set<FinanceBill>();
    public DbSet<SupplierSubscription> SupplierSubscriptions => Set<SupplierSubscription>();
    public DbSet<SupplierSubscriptionBillMatch> SupplierSubscriptionBillMatches => Set<SupplierSubscriptionBillMatch>();
    public DbSet<SupplierSubscriptionIntakeProposal> SupplierSubscriptionIntakeProposals => Set<SupplierSubscriptionIntakeProposal>();
    public DbSet<FinanceAsset> FinanceAssets => Set<FinanceAsset>();
    public DbSet<FinanceBalance> FinanceBalances => Set<FinanceBalance>();
    public DbSet<FinancePolicyConfiguration> FinancePolicyConfigurations => Set<FinancePolicyConfiguration>();
    public DbSet<FinanceSeedAnomaly> FinanceSeedAnomalies => Set<FinanceSeedAnomaly>();
    public DbSet<FinanceSimulationStepLog> FinanceSimulationStepLogs => Set<FinanceSimulationStepLog>();
    public DbSet<FinanceWorkflowTriggerExecution> FinanceWorkflowTriggerExecutions => Set<FinanceWorkflowTriggerExecution>();
    public DbSet<FinanceWorkflowTriggerCheckExecution> FinanceWorkflowTriggerCheckExecutions => Set<FinanceWorkflowTriggerCheckExecution>();
    public DbSet<FinanceSeedBackfillRun> FinanceSeedBackfillRuns => Set<FinanceSeedBackfillRun>();
    public DbSet<FinanceSeedBackfillAttempt> FinanceSeedBackfillAttempts => Set<FinanceSeedBackfillAttempt>();
    public DbSet<FinanceAgentInsight> FinanceAgentInsights => Set<FinanceAgentInsight>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<LedgerEntrySourceMapping> LedgerEntrySourceMappings => Set<LedgerEntrySourceMapping>();
    public DbSet<LedgerEntryLine> LedgerEntryLines => Set<LedgerEntryLine>();
    public DbSet<VoucherSeries> VoucherSeries => Set<VoucherSeries>();
    public DbSet<VoucherSequence> VoucherSequences => Set<VoucherSequence>();
    public DbSet<LedgerPostingIdentity> LedgerPostingIdentities => Set<LedgerPostingIdentity>();
    public DbSet<ManualJournalDraft> ManualJournalDrafts => Set<ManualJournalDraft>();
    public DbSet<ManualJournalDraftLine> ManualJournalDraftLines => Set<ManualJournalDraftLine>();
    public DbSet<ManualJournalEvidenceLink> ManualJournalEvidenceLinks => Set<ManualJournalEvidenceLink>();
    public DbSet<ManualJournalOperation> ManualJournalOperations => Set<ManualJournalOperation>();
    public DbSet<LedgerEntryEvidenceLink> LedgerEntryEvidenceLinks => Set<LedgerEntryEvidenceLink>();
    public DbSet<CustomerInvoiceAccountingProfile> CustomerInvoiceAccountingProfiles => Set<CustomerInvoiceAccountingProfile>();
    public DbSet<CustomerInvoiceAccountingLine> CustomerInvoiceAccountingLines => Set<CustomerInvoiceAccountingLine>();
    public DbSet<SupplierBillAccountingProfile> SupplierBillAccountingProfiles => Set<SupplierBillAccountingProfile>();
    public DbSet<SupplierBillAccountingLine> SupplierBillAccountingLines => Set<SupplierBillAccountingLine>();
    public DbSet<TrialBalanceSnapshot> TrialBalanceSnapshots => Set<TrialBalanceSnapshot>();
    public DbSet<FinancialStatementSnapshot> FinancialStatementSnapshots => Set<FinancialStatementSnapshot>();
    public DbSet<FinancialStatementSnapshotLine> FinancialStatementSnapshotLines => Set<FinancialStatementSnapshotLine>();
    public DbSet<AccountingTaxReview> AccountingTaxReviews => Set<AccountingTaxReview>();
    public DbSet<AccountingPeriodHistory> AccountingPeriodHistory => Set<AccountingPeriodHistory>();
    public DbSet<AccountingExportJob> AccountingExportJobs => Set<AccountingExportJob>();
    public DbSet<FinancialStatementMapping> FinancialStatementMappings => Set<FinancialStatementMapping>();
    public DbSet<CompanySimulationState> CompanySimulationStates => Set<CompanySimulationState>();
    public DbSet<CompanySimulationRunHistory> CompanySimulationRunHistories => Set<CompanySimulationRunHistory>();
    public DbSet<MailboxConnection> MailboxConnections => Set<MailboxConnection>();
    public DbSet<ExternalAccountConnection> ExternalAccountConnections => Set<ExternalAccountConnection>();
    public DbSet<CalendarConnection> CalendarConnections => Set<CalendarConnection>();
    public DbSet<MailboxFolderSyncCursor> MailboxFolderSyncCursors => Set<MailboxFolderSyncCursor>();
    public DbSet<MailboxOAuthAuthorizationState> MailboxOAuthAuthorizationStates => Set<MailboxOAuthAuthorizationState>();
    public DbSet<FortnoxConnection> FortnoxConnections => Set<FortnoxConnection>();
    public DbSet<FortnoxOAuthState> FortnoxOAuthStates => Set<FortnoxOAuthState>();
    public DbSet<FortnoxSyncHistory> FortnoxSyncHistories => Set<FortnoxSyncHistory>();
    public DbSet<FortnoxExternalReference> FortnoxExternalReferences => Set<FortnoxExternalReference>();
    public DbSet<EmailIngestionRun> EmailIngestionRuns => Set<EmailIngestionRun>();
    public DbSet<EmailMessageSnapshot> EmailMessageSnapshots => Set<EmailMessageSnapshot>();
    public DbSet<EmailAttachmentSnapshot> EmailAttachmentSnapshots => Set<EmailAttachmentSnapshot>();
    public DbSet<BillDuplicateCheck> BillDuplicateChecks => Set<BillDuplicateCheck>();
    public DbSet<NormalizedBillExtraction> NormalizedBillExtractions => Set<NormalizedBillExtraction>();
    public DbSet<DetectedBill> DetectedBills => Set<DetectedBill>();
    public DbSet<DetectedBillField> DetectedBillFields => Set<DetectedBillField>();
    public DbSet<FinanceBillReviewState> FinanceBillReviewStates => Set<FinanceBillReviewState>();
    public DbSet<FinanceBillReviewAction> FinanceBillReviewActions => Set<FinanceBillReviewAction>();
    public DbSet<BillApprovalProposal> BillApprovalProposals => Set<BillApprovalProposal>();
    public DbSet<SupplierInvoicePaymentProposal> SupplierInvoicePaymentProposals => Set<SupplierInvoicePaymentProposal>();
    public DbSet<SupplierInvoiceSourceDocumentAttachment> SupplierInvoiceSourceDocumentAttachments => Set<SupplierInvoiceSourceDocumentAttachment>();
    public DbSet<SupplierInvoiceDraftAction> SupplierInvoiceDraftActions => Set<SupplierInvoiceDraftAction>();
    public DbSet<SupplierInvoiceCorrectionAction> SupplierInvoiceCorrectionActions => Set<SupplierInvoiceCorrectionAction>();
    public DbSet<SupplierInvoiceEnrichmentAction> SupplierInvoiceEnrichmentActions => Set<SupplierInvoiceEnrichmentAction>();
    public DbSet<CompanySimulationRunTransition> CompanySimulationRunTransitions => Set<CompanySimulationRunTransition>();
    public DbSet<CompanySimulationRunDayLog> CompanySimulationRunDayLogs => Set<CompanySimulationRunDayLog>();
    public DbSet<SimulationCashDeltaRecord> SimulationCashDeltaRecords => Set<SimulationCashDeltaRecord>();
    public DbSet<SimulationEventRecord> SimulationEventRecords => Set<SimulationEventRecord>();
    public DbSet<FinanceIntegrationConnection> FinanceIntegrationConnections => Set<FinanceIntegrationConnection>();
    public DbSet<FinanceIntegrationToken> FinanceIntegrationTokens => Set<FinanceIntegrationToken>();
    public DbSet<FinanceIntegrationSyncState> FinanceIntegrationSyncStates => Set<FinanceIntegrationSyncState>();
    public DbSet<FinanceExternalReference> FinanceExternalReferences => Set<FinanceExternalReference>();
    public DbSet<FinanceIntegrationAuditEvent> FinanceIntegrationAuditEvents => Set<FinanceIntegrationAuditEvent>();
    public DbSet<FinanceIntegrationWriteCommandRecord> FinanceIntegrationWriteCommands => Set<FinanceIntegrationWriteCommandRecord>();
    public DbSet<FinanceIntegrationProviderConfiguration> FinanceIntegrationProviderConfigurations => Set<FinanceIntegrationProviderConfiguration>();
    public DbSet<FinanceIntegrationProviderConfigurationAudit> FinanceIntegrationProviderConfigurationAudits => Set<FinanceIntegrationProviderConfigurationAudit>();
    public DbSet<SupplierApprovalAutomationRule> SupplierApprovalAutomationRules => Set<SupplierApprovalAutomationRule>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<CustomerCompany> CustomerCompanies => Set<CustomerCompany>();
    public DbSet<SalesActivity> SalesActivities => Set<SalesActivity>();
    public DbSet<SalesPipelineStage> SalesPipelineStages => Set<SalesPipelineStage>();
    public DbSet<SalesAgentRecommendation> SalesAgentRecommendations => Set<SalesAgentRecommendation>();
    public DbSet<SalesMeetingInvitation> SalesMeetingInvitations => Set<SalesMeetingInvitation>();
    public DbSet<SalesMeetingChangeRequest> SalesMeetingChangeRequests => Set<SalesMeetingChangeRequest>();
    public DbSet<SalesActionApproval> SalesActionApprovals => Set<SalesActionApproval>();
    public DbSet<SalesEmailLink> SalesEmailLinks => Set<SalesEmailLink>();
    public DbSet<SalesSequence> SalesSequences => Set<SalesSequence>();
    public DbSet<SalesSequenceStep> SalesSequenceSteps => Set<SalesSequenceStep>();
    public DbSet<SalesCampaign> SalesCampaigns => Set<SalesCampaign>();
    public DbSet<SalesCampaignContact> SalesCampaignContacts => Set<SalesCampaignContact>();
    public DbSet<SalesCampaignObjective> SalesCampaignObjectives => Set<SalesCampaignObjective>();
    public DbSet<SalesCampaignOffer> SalesCampaignOffers => Set<SalesCampaignOffer>();
    public DbSet<SalesCampaignAudienceSegment> SalesCampaignAudienceSegments => Set<SalesCampaignAudienceSegment>();
    public DbSet<SalesCampaignAudienceSnapshot> SalesCampaignAudienceSnapshots => Set<SalesCampaignAudienceSnapshot>();
    public DbSet<SalesCampaignAudienceMember> SalesCampaignAudienceMembers => Set<SalesCampaignAudienceMember>();
    public DbSet<SalesCampaignMilestone> SalesCampaignMilestones => Set<SalesCampaignMilestone>();
    public DbSet<SalesCampaignActivity> SalesCampaignActivities => Set<SalesCampaignActivity>();
    public DbSet<SalesCampaignKpiDefinition> SalesCampaignKpiDefinitions => Set<SalesCampaignKpiDefinition>();
    public DbSet<SalesCampaignKpiSnapshot> SalesCampaignKpiSnapshots => Set<SalesCampaignKpiSnapshot>();
    public DbSet<SalesCampaignCost> SalesCampaignCosts => Set<SalesCampaignCost>();
    public DbSet<SalesSequenceExecution> SalesSequenceExecutions => Set<SalesSequenceExecution>();
    public DbSet<SalesSequenceExecutionStep> SalesSequenceExecutionSteps => Set<SalesSequenceExecutionStep>();
    public DbSet<SalesAutomationPolicy> SalesAutomationPolicies => Set<SalesAutomationPolicy>();
    public DbSet<OutboundMessageReview> OutboundMessageReviews => Set<OutboundMessageReview>();
    public DbSet<WebsiteLeadSubmission> WebsiteLeadSubmissions => Set<WebsiteLeadSubmission>();
    public DbSet<SalesMessagePerformance> SalesMessagePerformances => Set<SalesMessagePerformance>();
    public DbSet<SalesFinanceHandoff> SalesFinanceHandoffs => Set<SalesFinanceHandoff>();
    public DbSet<RevenueForecastSnapshot> RevenueForecastSnapshots => Set<RevenueForecastSnapshot>();
    public DbSet<DealRiskScoreSnapshot> DealRiskScoreSnapshots => Set<DealRiskScoreSnapshot>();
    public DbSet<DealIntelligenceSignal> DealIntelligenceSignals => Set<DealIntelligenceSignal>();
    public DbSet<IdealCustomerProfile> IdealCustomerProfiles => Set<IdealCustomerProfile>();
    public DbSet<ProspectSourcePolicy> ProspectSourcePolicies => Set<ProspectSourcePolicy>();
    public DbSet<ProspectingRun> ProspectingRuns => Set<ProspectingRun>();
    public DbSet<ProspectAccount> ProspectAccounts => Set<ProspectAccount>();
    public DbSet<ProspectContact> ProspectContacts => Set<ProspectContact>();
    public DbSet<ProspectSignal> ProspectSignals => Set<ProspectSignal>();
    public DbSet<SalesSuppression> SalesSuppressions => Set<SalesSuppression>();
    public DbSet<SalesAcquisitionCampaign> SalesAcquisitionCampaigns => Set<SalesAcquisitionCampaign>();
    public DbSet<SalesSourceTouch> SalesSourceTouches => Set<SalesSourceTouch>();
    public DbSet<SalesSourceAttribution> SalesSourceAttributions => Set<SalesSourceAttribution>();
    public DbSet<SalesContactPermission> SalesContactPermissions => Set<SalesContactPermission>();
    public DbSet<MarketingObjective> MarketingObjectives => Set<MarketingObjective>();
    public DbSet<MarketingPlan> MarketingPlans => Set<MarketingPlan>();
    public DbSet<MarketingPlanObjective> MarketingPlanObjectives => Set<MarketingPlanObjective>();
    public DbSet<MarketingPlanSegment> MarketingPlanSegments => Set<MarketingPlanSegment>();
    public DbSet<MarketingPlanCampaign> MarketingPlanCampaigns => Set<MarketingPlanCampaign>();
    public DbSet<MarketingPlanCampaignSegment> MarketingPlanCampaignSegments => Set<MarketingPlanCampaignSegment>();
    public DbSet<MarketingContentBrief> MarketingContentBriefs => Set<MarketingContentBrief>();
    public DbSet<MarketingContentVariant> MarketingContentVariants => Set<MarketingContentVariant>();
    public DbSet<MarketingSalesHandoff> MarketingSalesHandoffs => Set<MarketingSalesHandoff>();
    public DbSet<MarketingChannelObservation> MarketingChannelObservations => Set<MarketingChannelObservation>();
    public DbSet<MarketingExperiment> MarketingExperiments => Set<MarketingExperiment>();
    public DbSet<MarketingQualificationDefinition> MarketingQualificationDefinitions => Set<MarketingQualificationDefinition>();
    public DbSet<MarketingQualificationEvaluation> MarketingQualificationEvaluations => Set<MarketingQualificationEvaluation>();
    public DbSet<MarketingQualificationFeedback> MarketingQualificationFeedback => Set<MarketingQualificationFeedback>();
    public DbSet<MarketingStrategy> MarketingStrategies => Set<MarketingStrategy>();
    public DbSet<MarketingStrategySegment> MarketingStrategySegments => Set<MarketingStrategySegment>();
    public DbSet<MarketingStrategyCampaignLink> MarketingStrategyCampaignLinks => Set<MarketingStrategyCampaignLink>();
    public DbSet<MarketingIntelligenceRecord> MarketingIntelligenceRecords => Set<MarketingIntelligenceRecord>();
    public DbSet<MarketingIntelligenceReview> MarketingIntelligenceReviews => Set<MarketingIntelligenceReview>();
    public DbSet<MarketingCustomerSegment> MarketingCustomerSegments => Set<MarketingCustomerSegment>();
    public DbSet<MarketingCustomerSegmentVersion> MarketingCustomerSegmentVersions => Set<MarketingCustomerSegmentVersion>();
    public DbSet<MarketingSegmentDimension> MarketingSegmentDimensions => Set<MarketingSegmentDimension>();
    public DbSet<MarketingSegmentSizeEstimate> MarketingSegmentSizeEstimates => Set<MarketingSegmentSizeEstimate>();
    public DbSet<MarketingSegmentEconomicEstimate> MarketingSegmentEconomicEstimates => Set<MarketingSegmentEconomicEstimate>();
    public DbSet<MarketingSegmentScorePolicy> MarketingSegmentScorePolicies => Set<MarketingSegmentScorePolicy>();
    public DbSet<MarketingSegmentScoreDimension> MarketingSegmentScoreDimensions => Set<MarketingSegmentScoreDimension>();
    public DbSet<MarketingSegmentTargetDecision> MarketingSegmentTargetDecisions => Set<MarketingSegmentTargetDecision>();
    public DbSet<MarketingSegmentArtifactMapping> MarketingSegmentArtifactMappings => Set<MarketingSegmentArtifactMapping>();
    public DbSet<MarketingOperatingRun> MarketingOperatingRuns => Set<MarketingOperatingRun>();
    public DbSet<MarketingOperatingAction> MarketingOperatingActions => Set<MarketingOperatingAction>();
    public DbSet<MarketingWorkEvidence> MarketingWorkEvidence => Set<MarketingWorkEvidence>();
    public DbSet<MarketingCompanySignal> MarketingCompanySignals => Set<MarketingCompanySignal>();
    public DbSet<MarketingCreativeAsset> MarketingCreativeAssets => Set<MarketingCreativeAsset>();
    public DbSet<MarketingCreativeAssetScan> MarketingCreativeAssetScans => Set<MarketingCreativeAssetScan>();
    public DbSet<MarketingChannelConnection> MarketingChannelConnections => Set<MarketingChannelConnection>();
    public DbSet<MarketingChannelOAuthSession> MarketingChannelOAuthSessions => Set<MarketingChannelOAuthSession>();
    public DbSet<MarketingChannelDestination> MarketingChannelDestinations => Set<MarketingChannelDestination>();
    public DbSet<MarketingChannelAction> MarketingChannelActions => Set<MarketingChannelAction>();
    public DbSet<MarketingLifecycleJourney> MarketingLifecycleJourneys => Set<MarketingLifecycleJourney>();
    public DbSet<MarketingJourneyEnrollment> MarketingJourneyEnrollments => Set<MarketingJourneyEnrollment>();
    public DbSet<MarketingJourneyInboundEvent> MarketingJourneyInboundEvents => Set<MarketingJourneyInboundEvent>();
    public DbSet<MarketingJourneyStepAttempt> MarketingJourneyStepAttempts => Set<MarketingJourneyStepAttempt>();
    public DbSet<MarketingAttributionResult> MarketingAttributionResults => Set<MarketingAttributionResult>();
    public DbSet<MarketingAttributionTouch> MarketingAttributionTouches => Set<MarketingAttributionTouch>();
    public DbSet<MarketingAttributionModelDefinition> MarketingAttributionModels => Set<MarketingAttributionModelDefinition>();
    public DbSet<MarketingAttributionAllocation> MarketingAttributionAllocations => Set<MarketingAttributionAllocation>();
    public DbSet<MarketingExperimentExposure> MarketingExperimentExposures => Set<MarketingExperimentExposure>();
    public DbSet<MarketingExperimentDecisionRecord> MarketingExperimentDecisions => Set<MarketingExperimentDecisionRecord>();
    public DbSet<MarketingSegmentLearningProposal> MarketingSegmentLearningProposals => Set<MarketingSegmentLearningProposal>();
    public DbSet<MarketingEventTrigger> MarketingEventTriggers => Set<MarketingEventTrigger>();
    public DbSet<SupportCase> SupportCases => Set<SupportCase>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<SupportCaseEvent> SupportCaseEvents => Set<SupportCaseEvent>();
    public DbSet<SupportCaseAssignment> SupportCaseAssignments => Set<SupportCaseAssignment>();
    public DbSet<SupportSlaPolicy> SupportSlaPolicies => Set<SupportSlaPolicy>();
    public DbSet<SupportCaseResolution> SupportCaseResolutions => Set<SupportCaseResolution>();
    public DbSet<SupportReplyDraft> SupportReplyDrafts => Set<SupportReplyDraft>();
    public DbSet<SupportRefundRequest> SupportRefundRequests => Set<SupportRefundRequest>();
    public DbSet<SupportKnowledgeGap> SupportKnowledgeGaps => Set<SupportKnowledgeGap>();
    public DbSet<SupportMemoryUpdateJob> SupportMemoryUpdateJobs => Set<SupportMemoryUpdateJob>();
    public DbSet<SupportMemoryObservation> SupportMemoryObservations => Set<SupportMemoryObservation>();
    public DbSet<SupportAgentExecution> SupportAgentExecutions => Set<SupportAgentExecution>();
    public DbSet<AgentOrchestrationRun> AgentOrchestrationRuns => Set<AgentOrchestrationRun>();
    public DbSet<AgentHandoff> AgentHandoffs => Set<AgentHandoff>();
    public DbSet<AgentMemoryCandidate> AgentMemoryCandidates => Set<AgentMemoryCandidate>();
    public DbSet<AgentAiQualityEvent> AgentAiQualityEvents => Set<AgentAiQualityEvent>();

    internal Guid? CurrentCompanyId => _companyContextAccessor?.CompanyId;

    public override int SaveChanges()
    {
        ValidateCompanyOwnedMutations();
        EnsurePostedLedgerIsImmutable();
        ApplyFinanceSourceTrackingDefaults();
        EnsureBankTransactionPostingStates();

        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateCompanyOwnedMutations();
        EnsurePostedLedgerIsImmutable();
        ApplyFinanceSourceTrackingDefaults();
        EnsureBankTransactionPostingStates();
        var companiesToInvalidate = CaptureDashboardInvalidationCompanies();
        var result = await base.SaveChangesAsync(cancellationToken);

        if (_dashboardCacheInvalidator is not null)
        {
            foreach (var companyId in companiesToInvalidate)
            {
                await _dashboardCacheInvalidator.InvalidateAsync(companyId, cancellationToken);
            }
        }

        return result;
    }

    private void EnsureBankTransactionPostingStates()
    {
        var trackedStates = ChangeTracker.Entries<BankTransactionPostingStateRecord>()
            .Where(entry => entry.State != EntityState.Deleted)
            .Select(entry => (entry.Entity.CompanyId, entry.Entity.BankTransactionId))
            .ToHashSet();

        var addedTransactions = ChangeTracker.Entries<BankTransaction>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        foreach (var transaction in addedTransactions)
        {
            var key = (transaction.CompanyId, transaction.Id);
            if (trackedStates.Contains(key))
            {
                continue;
            }

            BankTransactionPostingStateRecords.Add(new BankTransactionPostingStateRecord(
                Guid.NewGuid(),
                transaction.CompanyId,
                transaction.Id,
                BankTransactionMatchingStatuses.Unmatched,
                BankTransactionPostingStates.SkippedUnmatched,
                0,
                transaction.CreatedUtc,
                "created_without_payment_match"));
        }
    }

    private void ApplyFinanceSourceTrackingDefaults()
    {
        foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
        {
            var sourceMetadata = entry.Metadata.FindProperty("SourceType");
            if (sourceMetadata is null || sourceMetadata.ClrType != typeof(string))
            {
                continue;
            }

            var sourceProperty = entry.Property("SourceType");
            var currentSource = sourceProperty.CurrentValue as string;
            if (!string.IsNullOrWhiteSpace(currentSource) &&
                !currentSource.Equals(FinanceRecordSourceTypes.Manual, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var simulationEventProperty = entry.Metadata.FindProperty("SourceSimulationEventRecordId");
            if (simulationEventProperty is not null &&
                entry.Property("SourceSimulationEventRecordId").CurrentValue is Guid simulationEventId &&
                simulationEventId != Guid.Empty)
            {
                // Simulation-created records must stay distinguishable from manual and Fortnox-synced records.
                sourceProperty.CurrentValue = FinanceRecordSourceTypes.Simulation;
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentSource))
            {
                sourceProperty.CurrentValue = FinanceRecordSourceTypes.Manual;
            }
        }
    }

    private void ValidateCompanyOwnedMutations()
    {
        var currentCompanyId = CurrentCompanyId;
        if (!currentCompanyId.HasValue)
        {
            return;
        }

        var invalidEntry = ChangeTracker.Entries<ICompanyOwnedEntity>()
            .FirstOrDefault(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                entry.Entity.CompanyId != currentCompanyId.Value);

        if (invalidEntry is not null)
        {
            throw new InvalidOperationException("Tenant-scoped records cannot be changed from a different company context.");
        }
    }

    private void EnsurePostedLedgerIsImmutable()
    {
        var changedEntryIds = ChangeTracker.Entries<LedgerEntry>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.Id)
            .Concat(ChangeTracker.Entries<LedgerEntryLine>()
                .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Entity.LedgerEntryId))
            .Concat(ChangeTracker.Entries<LedgerEntrySourceMapping>()
                .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Entity.LedgerEntryId))
            .Concat(ChangeTracker.Entries<LedgerEntryEvidenceLink>()
                .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Entity.LedgerEntryId))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (changedEntryIds.Length == 0)
        {
            return;
        }

        var trackedPosted = ChangeTracker.Entries<LedgerEntry>()
            .Any(entry => changedEntryIds.Contains(entry.Entity.Id) &&
                !string.IsNullOrWhiteSpace(entry.Entity.IdempotencyKey) &&
                (LedgerEntryStatuses.IsPosted(entry.OriginalValues.GetValue<string>(nameof(LedgerEntry.Status))) ||
                 LedgerEntryStatuses.IsPosted(entry.Entity.Status)));
        var persistedPosted = LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Any(entry => changedEntryIds.Contains(entry.Id) &&
                entry.Status == LedgerEntryStatuses.Posted &&
                entry.IdempotencyKey != null);

        if (trackedPosted || persistedPosted)
        {
            throw new InvalidOperationException("Posted journal entries and their lines cannot be changed or deleted. Create a correction instead.");
        }
    }

    private IReadOnlyList<Guid> CaptureDashboardInvalidationCompanies() =>
        ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry =>
                entry.Entity is ApprovalTask ||
                entry.Entity is WorkTask ||
                entry.Entity is ApprovalRequest ||
                entry.Entity is Agent ||
                entry.Entity is ActivityEvent ||
                entry.Entity is ToolExecutionAttempt ||
                entry.Entity is TriggerExecutionAttempt ||
                entry.Entity is WorkflowDefinition ||
                entry.Entity is WorkflowInstance ||
                entry.Entity is WorkflowException ||
                entry.Entity is CompanyBriefing ||
                entry.Entity is CompanyBriefingSection ||
                entry.Entity is CompanyBriefingContribution ||
                entry.Entity is CompanyBriefingSeverityRule ||
                entry.Entity is CompanyBriefingUpdateJob ||
                entry.Entity is UserBriefingPreference ||
                entry.Entity is TenantBriefingDefault ||
                entry.Entity is DashboardDepartmentConfig ||
                entry.Entity is DashboardWidgetConfig ||
                entry.Entity is Alert ||
                entry.Entity is FinanceAccount ||
                entry.Entity is AccountingConfiguration ||
                entry.Entity is AccountingConfigurationAccountRole ||
                entry.Entity is AccountingPolicyPackSelection ||
                entry.Entity is AccountingAuthorityPeriod ||
                entry.Entity is AccountingProviderSwitch ||
                entry.Entity is AccountingProviderSwitchAssessment ||
                entry.Entity is AccountingProviderSwitchCapability ||
                entry.Entity is AccountingProviderSwitchDataset ||
                entry.Entity is AccountingProviderSwitchGap ||
                entry.Entity is AccountingProviderSwitchStagedRecord ||
                entry.Entity is AccountingProviderSwitchMappingSet ||
                entry.Entity is AccountingProviderSwitchMappingDecision ||
                entry.Entity is AccountingProviderSwitchMappingRecord ||
                entry.Entity is AccountingProviderSwitchRehearsal ||
                entry.Entity is AccountingProviderSwitchRehearsalInput ||
                entry.Entity is AccountingProviderSwitchRehearsalDatasetResult ||
                entry.Entity is AccountingProviderSwitchReconciliationCheck ||
                entry.Entity is AccountingProviderSwitchManualEvidence ||
                entry.Entity is AccountingProviderSwitchCutoverPlan ||
                entry.Entity is AccountingProviderSwitchPlanApproval ||
                entry.Entity is AccountingProviderSwitchPreparation ||
                entry.Entity is AccountingProviderSwitchReadinessCheck ||
                entry.Entity is AccountingProviderSwitchNativeCandidate ||
                entry.Entity is AccountingProviderSwitchCandidateValidation ||
                entry.Entity is AccountingProviderSwitchArchiveDependency ||
                entry.Entity is AccountingProviderSwitchTargetTransferBatch ||
                entry.Entity is AccountingProviderSwitchTargetTransferItem ||
                entry.Entity is AccountingProviderSwitchTargetTransferAttempt ||
                entry.Entity is AccountingProviderSwitchTargetAcknowledgement ||
                entry.Entity is AccountingProviderSwitchCutoverExecution ||
                entry.Entity is AccountingProviderSwitchFinalSnapshot ||
                entry.Entity is AccountingProviderSwitchFinalCheck ||
                entry.Entity is AccountingProviderSwitchActivationApproval ||
                entry.Entity is AccountingProviderSwitchNativeMaterialization ||
                entry.Entity is AccountingProviderSwitchMonitoringRun ||
                entry.Entity is AccountingProviderSwitchMonitoringCheck ||
                entry.Entity is AccountingProviderSwitchMonitoringIncident ||
                entry.Entity is AccountingProviderExport ||
                entry.Entity is Payment ||
                entry.Entity is Budget ||
                entry.Entity is Forecast ||
                entry.Entity is CompanyBankAccount ||
                entry.Entity is BankTransaction ||
                entry.Entity is BankTransactionPaymentLink ||
                entry.Entity is BankTransactionPostingStateRecord ||
                entry.Entity is BankStatementImport ||
                entry.Entity is BankStatementImportRow ||
                entry.Entity is BankReconciliationFollowUp ||
                entry.Entity is PaymentCashLedgerLink ||
                entry.Entity is BankTransactionCashLedgerLink ||
                entry.Entity is PaymentAllocation ||
                entry.Entity is FinanceTransaction ||
                entry.Entity is FinanceInvoice ||
                entry.Entity is FinanceBill ||
                entry.Entity is FinanceAsset ||
                entry.Entity is FinanceBalance ||
                entry.Entity is FinanceCounterparty ||
                entry.Entity is FinancePolicyConfiguration ||
                entry.Entity is FinanceSeedAnomaly ||
                entry.Entity is FinanceSimulationStepLog ||
                entry.Entity is FiscalPeriod ||
                entry.Entity is FinanceAgentInsight ||
                entry.Entity is LedgerEntry ||
                entry.Entity is LedgerEntrySourceMapping ||
                entry.Entity is LedgerEntryLine ||
                entry.Entity is VoucherSeries ||
                entry.Entity is VoucherSequence ||
                entry.Entity is LedgerPostingIdentity ||
                entry.Entity is ManualJournalDraft ||
                entry.Entity is ManualJournalDraftLine ||
                entry.Entity is ManualJournalEvidenceLink ||
                entry.Entity is ManualJournalOperation ||
                entry.Entity is LedgerEntryEvidenceLink ||
                entry.Entity is CustomerInvoiceAccountingProfile ||
                entry.Entity is CustomerInvoiceAccountingLine ||
                entry.Entity is SupplierBillAccountingProfile ||
                entry.Entity is SupplierBillAccountingLine ||
                entry.Entity is TrialBalanceSnapshot ||
                entry.Entity is FinancialStatementSnapshot ||
                entry.Entity is FinancialStatementSnapshotLine ||
                entry.Entity is AccountingTaxReview ||
                entry.Entity is AccountingPeriodHistory ||
                entry.Entity is AccountingExportJob ||
                entry.Entity is MailboxConnection ||
                entry.Entity is MailboxOAuthAuthorizationState ||
                entry.Entity is FortnoxConnection ||
                entry.Entity is FortnoxOAuthState ||
                entry.Entity is FortnoxSyncHistory ||
                entry.Entity is FortnoxExternalReference ||
                entry.Entity is EmailIngestionRun ||
                entry.Entity is EmailMessageSnapshot ||
                entry.Entity is BillDuplicateCheck ||
                entry.Entity is NormalizedBillExtraction ||
                entry.Entity is DetectedBill ||
                entry.Entity is DetectedBillField ||
                entry.Entity is FinanceBillReviewState ||
                entry.Entity is FinanceBillReviewAction ||
                entry.Entity is BillApprovalProposal ||
                entry.Entity is SupplierInvoicePaymentProposal ||
                entry.Entity is SupplierInvoiceSourceDocumentAttachment ||
                entry.Entity is SupplierInvoiceDraftAction ||
                entry.Entity is SupplierInvoiceCorrectionAction ||
                entry.Entity is SupplierInvoiceEnrichmentAction ||
                entry.Entity is EmailAttachmentSnapshot ||
                entry.Entity is SimulationCashDeltaRecord ||
                entry.Entity is SimulationEventRecord ||
                entry.Entity is FinanceIntegrationConnection ||
                entry.Entity is FinanceIntegrationToken ||
                entry.Entity is FinanceIntegrationSyncState ||
                entry.Entity is FinanceIntegrationWriteCommandRecord ||
                entry.Entity is FinanceExternalReference ||
                entry.Entity is FinanceIntegrationAuditEvent ||
                entry.Entity is FinanceIntegrationWriteCommandRecord ||
                entry.Entity is Lead ||
                entry.Entity is Deal ||
                entry.Entity is Contact ||
                entry.Entity is CustomerCompany ||
                entry.Entity is SalesActivity ||
                entry.Entity is SalesPipelineStage ||
                entry.Entity is SalesAgentRecommendation ||
                entry.Entity is SalesMeetingInvitation ||
                entry.Entity is SalesMeetingChangeRequest ||
                entry.Entity is SalesAutomationPolicy ||
                entry.Entity is SalesSequence ||
                entry.Entity is SalesSequenceStep ||
                entry.Entity is SalesCampaign ||
                entry.Entity is SalesCampaignContact ||
                entry.Entity is SalesCampaignObjective ||
                entry.Entity is SalesCampaignOffer ||
                entry.Entity is SalesCampaignAudienceSegment ||
                entry.Entity is SalesCampaignAudienceSnapshot ||
                entry.Entity is SalesCampaignAudienceMember ||
                entry.Entity is SalesCampaignMilestone ||
                entry.Entity is SalesCampaignActivity ||
                entry.Entity is SalesCampaignKpiDefinition ||
                entry.Entity is SalesCampaignKpiSnapshot ||
                entry.Entity is SalesCampaignCost ||
                entry.Entity is SalesSequenceExecution ||
                entry.Entity is SalesSequenceExecutionStep ||
                entry.Entity is SalesMessagePerformance ||
                entry.Entity is OutboundMessageReview ||
                entry.Entity is WebsiteLeadSubmission ||
                entry.Entity is SalesActionApproval ||
                entry.Entity is SalesFinanceHandoff ||
                entry.Entity is SalesEmailLink ||
                entry.Entity is DealIntelligenceSignal ||
                entry.Entity is MarketingObjective ||
                entry.Entity is MarketingPlan ||
                entry.Entity is MarketingPlanObjective ||
                entry.Entity is MarketingPlanSegment ||
                entry.Entity is MarketingPlanCampaign ||
                entry.Entity is MarketingPlanCampaignSegment ||
                entry.Entity is MarketingContentBrief ||
                entry.Entity is MarketingContentVariant ||
                entry.Entity is MarketingSalesHandoff ||
                entry.Entity is MarketingChannelObservation ||
                entry.Entity is MarketingExperiment ||
                entry.Entity is MarketingQualificationDefinition ||
                entry.Entity is MarketingQualificationEvaluation ||
                entry.Entity is MarketingQualificationFeedback ||
                entry.Entity is SupportCase ||
                entry.Entity is SupportMessage ||
                entry.Entity is SupportCaseEvent ||
                entry.Entity is SupportCaseAssignment ||
                entry.Entity is SupportSlaPolicy ||
                entry.Entity is SupportCaseResolution ||
                entry.Entity is SupportReplyDraft ||
                entry.Entity is SupportRefundRequest ||
                entry.Entity is SupportKnowledgeGap)
            .Select(entry =>
            {
                var property = entry.Properties.FirstOrDefault(x => x.Metadata.Name == nameof(ICompanyOwnedEntity.CompanyId));
                return property?.CurrentValue is Guid companyId ? companyId : Guid.Empty;
            })
            .Where(companyId => companyId != Guid.Empty)
            .Distinct()
            .ToArray();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VirtualCompanyDbContext).Assembly);
        ApplySqliteCompatibilityMappings(modelBuilder);
        modelBuilder.Entity<CompanyOwnedNote>()
            .HasQueryFilter(note =>
                CurrentCompanyId != null && note.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BackgroundExecution>()
            .HasQueryFilter(execution =>
                CurrentCompanyId != null && execution.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BackgroundExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId != null && attempt.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ExecutionExceptionRecord>()
            .HasQueryFilter(executionException =>
                CurrentCompanyId != null && executionException.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(auditEvent =>
                CurrentCompanyId != null && auditEvent.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ActivityEvent>()
            .HasQueryFilter(activityEvent =>
                CurrentCompanyId != null && activityEvent.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Agent>()
            .HasQueryFilter(agent =>
                CurrentCompanyId != null && agent.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyGoal>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyOperatingConfiguration>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingCycle>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingPlan>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingInitiative>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingPlanDependency>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingDecision>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingValidationResult>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingReview>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<OperatingSnapshot>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierApprovalAutomationRule>()
            .HasQueryFilter(rule =>
                CurrentCompanyId != null && rule.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierSubscription>()
            .HasQueryFilter(subscription =>
                CurrentCompanyId != null && subscription.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierSubscriptionBillMatch>()
            .HasQueryFilter(match =>
                CurrentCompanyId != null && match.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierSubscriptionIntakeProposal>()
            .HasQueryFilter(proposal =>
                CurrentCompanyId != null && proposal.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesMeetingInvitation>()
            .HasQueryFilter(invitation =>
                CurrentCompanyId != null && invitation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesMeetingChangeRequest>()
            .HasQueryFilter(changeRequest =>
                CurrentCompanyId != null && changeRequest.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AgentOrchestrationRun>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AgentHandoff>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AgentMemoryCandidate>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AgentAiQualityEvent>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesAcquisitionCampaign>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesSourceTouch>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesSourceAttribution>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesContactPermission>().HasQueryFilter(x => CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ToolExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId != null && attempt.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AgentScheduledTrigger>()
            .HasQueryFilter(trigger =>
                CurrentCompanyId != null && trigger.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<TriggerExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId != null && attempt.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AgentScheduledTriggerEnqueueWindow>()
            .HasQueryFilter(window =>
                CurrentCompanyId != null && window.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<WorkTask>()
            .HasQueryFilter(task =>
                CurrentCompanyId != null && task.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AgentTaskCreationDedupeRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId != null && record.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ApprovalRequest>()
            .HasQueryFilter(request =>
                CurrentCompanyId != null && request.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ApprovalStep>()
            .HasQueryFilter(step =>
                CurrentCompanyId != null && step.Approval.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ApprovalTask>()
            .HasQueryFilter(task =>
                CurrentCompanyId != null && task.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Conversation>()
            .HasQueryFilter(conversation =>
                CurrentCompanyId != null && conversation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Message>()
            .HasQueryFilter(message =>
                CurrentCompanyId != null && message.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ConversationTaskLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<GuidedWorkSession>()
            .HasQueryFilter(session =>
                CurrentCompanyId != null && session.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<GuidedDraftField>()
            .HasQueryFilter(field =>
                CurrentCompanyId != null && field.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<GuidedSessionOperation>()
            .HasQueryFilter(operation =>
                CurrentCompanyId != null && operation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<GuidedVoiceBinding>()
            .HasQueryFilter(binding =>
                CurrentCompanyId != null && binding.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyBriefing>()
            .HasQueryFilter(briefing =>
                CurrentCompanyId != null && briefing.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyBriefingSection>()
            .HasQueryFilter(section =>
                CurrentCompanyId != null && section.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyBriefingContribution>()
            .HasQueryFilter(contribution =>
                CurrentCompanyId != null && contribution.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyBriefingUpdateJob>()
            .HasQueryFilter(job =>
                CurrentCompanyId != null && job.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyBriefingDeliveryPreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId != null && preference.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyBriefingSeverityRule>()
            .HasQueryFilter(rule =>
                CurrentCompanyId != null && rule.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<UserBriefingPreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId != null && preference.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<TenantBriefingDefault>()
            .HasQueryFilter(defaults =>
                CurrentCompanyId != null && defaults.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyNotification>()
            .HasQueryFilter(notification =>
                CurrentCompanyId != null && notification.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ProactiveMessage>()
            .HasQueryFilter(message =>
                CurrentCompanyId != null && message.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ProactiveMessagePolicyDecision>()
            .HasQueryFilter(decision =>
                CurrentCompanyId != null && decision.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<WorkflowDefinition>()
            .HasQueryFilter(definition =>
                CurrentCompanyId != null && (definition.CompanyId == CurrentCompanyId || definition.CompanyId == null));
        modelBuilder.Entity<WorkflowTrigger>()
            .HasQueryFilter(trigger =>
                CurrentCompanyId != null && trigger.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<WorkflowInstance>()
            .HasQueryFilter(instance =>
                CurrentCompanyId != null && instance.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ProcessedWorkflowTriggerEvent>()
            .HasQueryFilter(processedEvent =>
                CurrentCompanyId != null && processedEvent.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<WorkflowException>()
            .HasQueryFilter(workflowException =>
                CurrentCompanyId != null && workflowException.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ConditionTriggerEvaluation>()
            .HasQueryFilter(evaluation =>
                CurrentCompanyId != null && evaluation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyKnowledgeDocument>()
            .HasQueryFilter(document =>
                CurrentCompanyId != null && document.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyKnowledgeChunk>()
            .HasQueryFilter(chunk =>
                CurrentCompanyId != null && chunk.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MemoryItem>()
            .HasQueryFilter(memoryItem =>
                CurrentCompanyId != null && memoryItem.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerMemoryProfile>()
            .HasQueryFilter(profile =>
                CurrentCompanyId != null && profile.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerMemoryProfileConversation>()
            .HasQueryFilter(link =>
                CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerMemoryProfileDeal>()
            .HasQueryFilter(link =>
                CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerMemoryProfileEngagementAttribute>()
            .HasQueryFilter(attribute =>
                CurrentCompanyId != null && attribute.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerMemoryProfilePreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId != null && preference.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerMemoryProfilePriceSignal>()
            .HasQueryFilter(signal =>
                CurrentCompanyId != null && signal.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerMemoryProfileIndustrySignal>()
            .HasQueryFilter(signal =>
                CurrentCompanyId != null && signal.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<DealIntelligenceSignal>()
            .HasQueryFilter(signal =>
                CurrentCompanyId != null && signal.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ContextRetrieval>()
            .HasQueryFilter(retrieval =>
                CurrentCompanyId != null && retrieval.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ContextRetrievalSource>()
            .HasQueryFilter(source =>
                CurrentCompanyId != null && source.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Alert>()
            .HasQueryFilter(alert =>
                CurrentCompanyId != null && alert.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Escalation>()
            .HasQueryFilter(escalation =>
                CurrentCompanyId != null && escalation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<DashboardDepartmentConfig>()
            .HasQueryFilter(config =>
                CurrentCompanyId != null && config.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<DashboardWidgetConfig>()
            .HasQueryFilter(config =>
                CurrentCompanyId != null && config.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<InsightAcknowledgment>()
            .HasQueryFilter(acknowledgment =>
                CurrentCompanyId != null && acknowledgment.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceAccount>()
            .HasQueryFilter(account =>
                CurrentCompanyId != null && account.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingConfiguration>()
            .HasQueryFilter(configuration =>
                CurrentCompanyId != null && configuration.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingConfigurationAccountRole>()
            .HasQueryFilter(role =>
                CurrentCompanyId != null && role.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingPolicyPackSelection>()
            .HasQueryFilter(selection =>
                CurrentCompanyId != null && selection.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingAuthorityPeriod>()
            .HasQueryFilter(period =>
                CurrentCompanyId != null && period.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitch>()
            .HasQueryFilter(providerSwitch =>
                CurrentCompanyId != null && providerSwitch.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchAssessment>()
            .HasQueryFilter(assessment =>
                CurrentCompanyId != null && assessment.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchCapability>()
            .HasQueryFilter(capability =>
                CurrentCompanyId != null && capability.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchDataset>()
            .HasQueryFilter(dataset =>
                CurrentCompanyId != null && dataset.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchGap>()
            .HasQueryFilter(gap =>
                CurrentCompanyId != null && gap.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchStagedRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId != null && record.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchMappingSet>()
            .HasQueryFilter(mappingSet =>
                CurrentCompanyId != null && mappingSet.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchMappingDecision>()
            .HasQueryFilter(decision =>
                CurrentCompanyId != null && decision.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchMappingRecord>()
            .HasQueryFilter(mappingRecord =>
                CurrentCompanyId != null && mappingRecord.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchRehearsal>()
            .HasQueryFilter(run => CurrentCompanyId != null && run.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchRehearsalInput>()
            .HasQueryFilter(input => CurrentCompanyId != null && input.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchRehearsalDatasetResult>()
            .HasQueryFilter(result => CurrentCompanyId != null && result.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchReconciliationCheck>()
            .HasQueryFilter(check => CurrentCompanyId != null && check.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchManualEvidence>()
            .HasQueryFilter(evidence => CurrentCompanyId != null && evidence.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchCutoverPlan>()
            .HasQueryFilter(plan => CurrentCompanyId != null && plan.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchPlanApproval>()
            .HasQueryFilter(approval => CurrentCompanyId != null && approval.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchPreparation>()
            .HasQueryFilter(preparation => CurrentCompanyId != null && preparation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchReadinessCheck>()
            .HasQueryFilter(check => CurrentCompanyId != null && check.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchNativeCandidate>()
            .HasQueryFilter(candidate => CurrentCompanyId != null && candidate.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchCandidateValidation>()
            .HasQueryFilter(validation => CurrentCompanyId != null && validation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchArchiveDependency>()
            .HasQueryFilter(dependency => CurrentCompanyId != null && dependency.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchTargetTransferBatch>()
            .HasQueryFilter(batch => CurrentCompanyId != null && batch.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchTargetTransferItem>()
            .HasQueryFilter(item => CurrentCompanyId != null && item.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchTargetTransferAttempt>()
            .HasQueryFilter(attempt => CurrentCompanyId != null && attempt.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchTargetAcknowledgement>()
            .HasQueryFilter(acknowledgement => CurrentCompanyId != null && acknowledgement.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchCutoverExecution>()
            .HasQueryFilter(execution => CurrentCompanyId != null && execution.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchFinalSnapshot>()
            .HasQueryFilter(snapshot => CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchFinalCheck>()
            .HasQueryFilter(check => CurrentCompanyId != null && check.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchActivationApproval>()
            .HasQueryFilter(approval => CurrentCompanyId != null && approval.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchNativeMaterialization>()
            .HasQueryFilter(materialization => CurrentCompanyId != null && materialization.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchMonitoringRun>()
            .HasQueryFilter(run => CurrentCompanyId != null && run.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchMonitoringCheck>()
            .HasQueryFilter(check => CurrentCompanyId != null && check.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderSwitchMonitoringIncident>()
            .HasQueryFilter(incident => CurrentCompanyId != null && incident.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingProviderExport>()
            .HasQueryFilter(export =>
                CurrentCompanyId != null && export.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingMigrationRun>()
            .HasQueryFilter(run =>
                CurrentCompanyId != null && run.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingMigrationConflict>()
            .HasQueryFilter(conflict =>
                CurrentCompanyId != null && conflict.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingCutoverReport>()
            .HasQueryFilter(report =>
                CurrentCompanyId != null && report.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinancialStatementMapping>()
            .HasQueryFilter(mapping =>
                CurrentCompanyId != null && mapping.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Budget>()
            .HasQueryFilter(budget =>
                CurrentCompanyId != null && budget.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Forecast>()
            .HasQueryFilter(forecast =>
                CurrentCompanyId != null && forecast.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Payment>()
            .HasQueryFilter(payment =>
                CurrentCompanyId != null && payment.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanyBankAccount>()
            .HasQueryFilter(bankAccount =>
                CurrentCompanyId != null && bankAccount.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BankTransaction>()
            .HasQueryFilter(bankTransaction =>
                CurrentCompanyId != null && bankTransaction.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BankTransactionPaymentLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BankTransactionPostingStateRecord>()
            .HasQueryFilter(state =>
                CurrentCompanyId != null && state.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<PaymentCashLedgerLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BankTransactionCashLedgerLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<PaymentAllocation>()
            .HasQueryFilter(allocation =>
                CurrentCompanyId != null && allocation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ReconciliationSuggestionRecord>()
            .HasQueryFilter(suggestion =>
                CurrentCompanyId != null && suggestion.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ReconciliationResultRecord>()
            .HasQueryFilter(result =>
                CurrentCompanyId != null && result.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceTransaction>()
            .HasQueryFilter(transaction =>
                CurrentCompanyId != null && transaction.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceInvoice>()
            .HasQueryFilter(invoice =>
                CurrentCompanyId != null && invoice.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceBill>()
            .HasQueryFilter(bill =>
                CurrentCompanyId != null && bill.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceAsset>()
            .HasQueryFilter(asset =>
                CurrentCompanyId != null && asset.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceBalance>()
            .HasQueryFilter(balance =>
                CurrentCompanyId != null && balance.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceCounterparty>()
            .HasQueryFilter(counterparty =>
                CurrentCompanyId != null && counterparty.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinancePolicyConfiguration>()
            .HasQueryFilter(policy =>
                CurrentCompanyId != null && policy.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceSeedAnomaly>()
            .HasQueryFilter(anomaly =>
                CurrentCompanyId != null && anomaly.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceSimulationStepLog>()
            .HasQueryFilter(log =>
                CurrentCompanyId != null && log.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceAgentInsight>()
            .HasQueryFilter(insight =>
                CurrentCompanyId != null && insight.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceWorkflowTriggerExecution>()
            .HasQueryFilter(execution =>
                CurrentCompanyId != null && execution.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceWorkflowTriggerCheckExecution>()
            .HasQueryFilter(execution =>
                CurrentCompanyId != null && execution.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FiscalPeriod>()
            .HasQueryFilter(period =>
                CurrentCompanyId != null && period.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<LedgerEntry>()
            .HasQueryFilter(entry =>
                CurrentCompanyId != null && entry.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<LedgerEntrySourceMapping>()
            .HasQueryFilter(mapping =>
                CurrentCompanyId != null && mapping.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<LedgerEntryLine>()
            .HasQueryFilter(line =>
                CurrentCompanyId != null && line.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<VoucherSeries>()
            .HasQueryFilter(series =>
                CurrentCompanyId != null && series.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<VoucherSequence>()
            .HasQueryFilter(sequence =>
                CurrentCompanyId != null && sequence.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<LedgerPostingIdentity>()
            .HasQueryFilter(identity =>
                CurrentCompanyId != null && identity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ManualJournalDraft>()
            .HasQueryFilter(draft => CurrentCompanyId != null && draft.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ManualJournalDraftLine>()
            .HasQueryFilter(line => CurrentCompanyId != null && line.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ManualJournalEvidenceLink>()
            .HasQueryFilter(link => CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<ManualJournalOperation>()
            .HasQueryFilter(operation => CurrentCompanyId != null && operation.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<LedgerEntryEvidenceLink>()
            .HasQueryFilter(link => CurrentCompanyId != null && link.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerInvoiceAccountingProfile>()
            .HasQueryFilter(profile => CurrentCompanyId != null && profile.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CustomerInvoiceAccountingLine>()
            .HasQueryFilter(line => CurrentCompanyId != null && line.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierBillAccountingProfile>()
            .HasQueryFilter(profile => CurrentCompanyId != null && profile.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierBillAccountingLine>()
            .HasQueryFilter(line => CurrentCompanyId != null && line.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<TrialBalanceSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinancialStatementSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinancialStatementSnapshotLine>()
            .HasQueryFilter(line =>
                CurrentCompanyId != null && line.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingTaxReview>()
            .HasQueryFilter(review => CurrentCompanyId != null && review.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingPeriodHistory>()
            .HasQueryFilter(history => CurrentCompanyId != null && history.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<AccountingExportJob>()
            .HasQueryFilter(job => CurrentCompanyId != null && job.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MailboxConnection>()
            .HasQueryFilter(connection =>
                CurrentCompanyId != null && connection.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MailboxFolderSyncCursor>()
            .HasQueryFilter(cursor =>
                CurrentCompanyId != null && cursor.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MailboxOAuthAuthorizationState>()
            .HasQueryFilter(state =>
                CurrentCompanyId != null && state.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FortnoxConnection>()
            .HasQueryFilter(connection =>
                CurrentCompanyId != null && connection.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FortnoxOAuthState>()
            .HasQueryFilter(state =>
                CurrentCompanyId != null && state.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FortnoxSyncHistory>()
            .HasQueryFilter(history =>
                CurrentCompanyId != null && history.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FortnoxExternalReference>()
            .HasQueryFilter(reference =>
                CurrentCompanyId != null && reference.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<EmailIngestionRun>()
            .HasQueryFilter(run =>
                CurrentCompanyId != null && run.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<EmailMessageSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<EmailAttachmentSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BillDuplicateCheck>()
            .HasQueryFilter(check =>
                CurrentCompanyId != null && check.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<NormalizedBillExtraction>()
            .HasQueryFilter(extraction =>
                CurrentCompanyId != null && extraction.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<DetectedBill>()
            .HasQueryFilter(bill =>
                CurrentCompanyId != null && bill.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<DetectedBillField>()
            .HasQueryFilter(field =>
                CurrentCompanyId != null && field.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceBillReviewState>()
            .HasQueryFilter(state =>
                CurrentCompanyId != null && state.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceBillReviewAction>()
            .HasQueryFilter(action =>
                CurrentCompanyId != null && action.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<BillApprovalProposal>()
            .HasQueryFilter(proposal =>
                CurrentCompanyId != null && proposal.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierInvoicePaymentProposal>()
            .HasQueryFilter(proposal =>
                CurrentCompanyId != null && proposal.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierInvoiceSourceDocumentAttachment>()
            .HasQueryFilter(attachment =>
                CurrentCompanyId != null && attachment.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierInvoiceDraftAction>()
            .HasQueryFilter(action =>
                CurrentCompanyId != null && action.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierInvoiceCorrectionAction>()
            .HasQueryFilter(action =>
                CurrentCompanyId != null && action.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierInvoiceEnrichmentAction>()
            .HasQueryFilter(action =>
                CurrentCompanyId != null && action.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanySimulationRunHistory>()
            .HasQueryFilter(history =>
                CurrentCompanyId != null && history.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanySimulationState>()
            .HasQueryFilter(state =>
                CurrentCompanyId != null && state.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanySimulationRunTransition>()
            .HasQueryFilter(transition =>
                CurrentCompanyId != null && transition.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<CompanySimulationRunDayLog>()
            .HasQueryFilter(log =>
                CurrentCompanyId != null && log.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SimulationCashDeltaRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId != null && record.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SimulationEventRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId != null && record.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceIntegrationConnection>()
            .HasQueryFilter(connection =>
                CurrentCompanyId != null && connection.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceIntegrationToken>()
            .HasQueryFilter(token =>
                CurrentCompanyId != null && token.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceIntegrationSyncState>()
            .HasQueryFilter(state =>
                CurrentCompanyId != null && state.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceExternalReference>()
            .HasQueryFilter(reference =>
                CurrentCompanyId != null && reference.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceIntegrationAuditEvent>()
            .HasQueryFilter(auditEvent =>
                CurrentCompanyId != null && auditEvent.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinanceIntegrationWriteCommandRecord>()
            .HasQueryFilter(command =>
                CurrentCompanyId != null && command.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesPipelineStage>()
            .HasQueryFilter(stage =>
                !stage.IsDeleted &&
                (stage.CompanyId == SalesPipelineStage.SystemCompanyId ||
                 (CurrentCompanyId != null && stage.CompanyId == CurrentCompanyId)));
        modelBuilder.Entity<Lead>()
            .HasQueryFilter(lead =>
                CurrentCompanyId != null && lead.CompanyId == CurrentCompanyId && !lead.IsDeleted);
        modelBuilder.Entity<Deal>()
            .HasQueryFilter(deal =>
                CurrentCompanyId != null && deal.CompanyId == CurrentCompanyId && !deal.IsDeleted);
        modelBuilder.Entity<Contact>()
            .HasQueryFilter(contact =>
                CurrentCompanyId != null && contact.CompanyId == CurrentCompanyId && !contact.IsDeleted);
        modelBuilder.Entity<CustomerCompany>()
            .HasQueryFilter(customer =>
                CurrentCompanyId != null && customer.CompanyId == CurrentCompanyId && !customer.IsDeleted);
        modelBuilder.Entity<SalesActivity>()
            .HasQueryFilter(activity =>
                CurrentCompanyId != null && activity.CompanyId == CurrentCompanyId && !activity.IsDeleted);
        modelBuilder.Entity<SalesAgentRecommendation>()
            .HasQueryFilter(recommendation =>
                CurrentCompanyId != null && recommendation.CompanyId == CurrentCompanyId && !recommendation.IsDeleted);
        modelBuilder.Entity<SalesSequence>()
            .HasQueryFilter(sequence =>
                CurrentCompanyId != null && sequence.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesSequenceStep>()
            .HasQueryFilter(step =>
                CurrentCompanyId != null && step.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaign>()
            .HasQueryFilter(campaign =>
                CurrentCompanyId != null && campaign.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignContact>()
            .HasQueryFilter(contact =>
                CurrentCompanyId != null && contact.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignObjective>()
            .HasQueryFilter(objective =>
                CurrentCompanyId != null && objective.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignOffer>()
            .HasQueryFilter(offer =>
                CurrentCompanyId != null && offer.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignAudienceSegment>()
            .HasQueryFilter(segment =>
                CurrentCompanyId != null && segment.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignAudienceSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignAudienceMember>()
            .HasQueryFilter(member =>
                CurrentCompanyId != null && member.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignMilestone>()
            .HasQueryFilter(milestone =>
                CurrentCompanyId != null && milestone.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignActivity>()
            .HasQueryFilter(activity =>
                CurrentCompanyId != null && activity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignKpiDefinition>()
            .HasQueryFilter(definition =>
                CurrentCompanyId != null && definition.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignKpiSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesCampaignCost>()
            .HasQueryFilter(cost =>
                CurrentCompanyId != null && cost.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesSequenceExecution>()
            .HasQueryFilter(execution =>
                CurrentCompanyId != null && execution.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesSequenceExecutionStep>()
            .HasQueryFilter(step =>
                CurrentCompanyId != null && step.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesAutomationPolicy>()
            .HasQueryFilter(policy =>
                CurrentCompanyId != null && policy.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesActionApproval>()
            .HasQueryFilter(approval =>
                CurrentCompanyId != null && approval.CompanyId == CurrentCompanyId && !approval.IsDeleted);
        modelBuilder.Entity<SalesEmailLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId != null && link.CompanyId == CurrentCompanyId && !link.IsDeleted);
        modelBuilder.Entity<OutboundMessageReview>()
            .HasQueryFilter(review =>
                CurrentCompanyId != null && review.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesMessagePerformance>()
            .HasQueryFilter(performance =>
                CurrentCompanyId != null && performance.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<WebsiteLeadSubmission>()
            .HasQueryFilter(submission =>
                CurrentCompanyId != null && submission.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<RevenueForecastSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<DealRiskScoreSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SalesFinanceHandoff>()
            .HasQueryFilter(handoff =>
                CurrentCompanyId != null &&
                handoff.CompanyId == CurrentCompanyId &&
                !handoff.Deal.IsDeleted);
        modelBuilder.Entity<MarketingObjective>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingPlan>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingPlanObjective>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingContentBrief>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingContentVariant>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSalesHandoff>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingChannelObservation>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingExperiment>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingQualificationDefinition>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingQualificationEvaluation>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingQualificationFeedback>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSegmentSizeEstimate>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSegmentEconomicEstimate>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSegmentScorePolicy>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSegmentScoreDimension>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSegmentTargetDecision>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSegmentArtifactMapping>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingJourneyInboundEvent>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingJourneyStepAttempt>()
            .HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingAttributionTouch>().HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingAttributionModelDefinition>().HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingAttributionAllocation>().HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingExperimentExposure>().HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingExperimentDecisionRecord>().HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<MarketingSegmentLearningProposal>().HasQueryFilter(entity => CurrentCompanyId != null && entity.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportCase>()
            .HasQueryFilter(supportCase =>
                CurrentCompanyId != null && supportCase.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportMessage>()
            .HasQueryFilter(message =>
                CurrentCompanyId != null && message.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportCaseEvent>()
            .HasQueryFilter(supportEvent =>
                CurrentCompanyId != null && supportEvent.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportCaseAssignment>()
            .HasQueryFilter(assignment =>
                CurrentCompanyId != null && assignment.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportSlaPolicy>()
            .HasQueryFilter(policy =>
                CurrentCompanyId != null && policy.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportCaseResolution>()
            .HasQueryFilter(resolution =>
                CurrentCompanyId != null && resolution.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportReplyDraft>()
            .HasQueryFilter(draft =>
                CurrentCompanyId != null && draft.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportRefundRequest>()
            .HasQueryFilter(refund =>
                CurrentCompanyId != null && refund.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupportKnowledgeGap>()
            .HasQueryFilter(gap =>
                CurrentCompanyId != null && gap.CompanyId == CurrentCompanyId);
    }

    private void ApplySqliteCompatibilityMappings(ModelBuilder modelBuilder)
    {
        if (!string.Equals(Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (string.Equals(property.GetColumnType(), "nvarchar(max)", StringComparison.OrdinalIgnoreCase))
            {
                property.SetColumnType("TEXT");
            }

            if (string.Equals(property.GetColumnType(), "varbinary(max)", StringComparison.OrdinalIgnoreCase))
            {
                property.SetColumnType("BLOB");
            }

            var defaultValueSql = property.GetDefaultValueSql();
            if (defaultValueSql?.StartsWith("N'", StringComparison.OrdinalIgnoreCase) == true)
            {
                property.SetDefaultValueSql(defaultValueSql[1..]);
            }
        }

        modelBuilder.Entity<VoucherSequence>().Property(sequence => sequence.RowVersion)
            .HasColumnType("BLOB")
            .ValueGeneratedNever()
            .IsConcurrencyToken();
        modelBuilder.Entity<LedgerEntry>().Property(entry => entry.RowVersion)
            .HasColumnType("BLOB")
            .ValueGeneratedNever()
            .IsConcurrencyToken();
        modelBuilder.Entity<FiscalPeriod>().Property(period => period.RowVersion)
            .HasColumnType("BLOB")
            .ValueGeneratedNever()
            .IsConcurrencyToken();
        modelBuilder.Entity<AccountingExportJob>().Property(job => job.RowVersion)
            .HasColumnType("BLOB")
            .ValueGeneratedNever()
            .IsConcurrencyToken();

        modelBuilder.Entity<DealIntelligenceSignal>().ToTable(table =>
            table.HasCheckConstraint(
                "CK_deal_intelligence_signals_explanation_required",
                "LENGTH(TRIM(explanation)) > 0"));
    }
}

