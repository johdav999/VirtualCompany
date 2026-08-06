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
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<ExecutionExceptionRecord> ExecutionExceptionRecords => Set<ExecutionExceptionRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CompanyOwnedNote> CompanyNotes => Set<CompanyOwnedNote>();
    public DbSet<CompanySetupTemplate> CompanySetupTemplates => Set<CompanySetupTemplate>();
    public DbSet<CompanyKnowledgeDocument> CompanyKnowledgeDocuments => Set<CompanyKnowledgeDocument>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<CompanyKnowledgeChunk> CompanyKnowledgeChunks => Set<CompanyKnowledgeChunk>();
    public DbSet<Agent> Agents => Set<Agent>();
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
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CompanyBankAccount> CompanyBankAccounts => Set<CompanyBankAccount>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Forecast> Forecasts => Set<Forecast>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<BankTransactionPaymentLink> BankTransactionPaymentLinks => Set<BankTransactionPaymentLink>();
    public DbSet<BankTransactionPostingStateRecord> BankTransactionPostingStateRecords => Set<BankTransactionPostingStateRecord>();
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
    public DbSet<TrialBalanceSnapshot> TrialBalanceSnapshots => Set<TrialBalanceSnapshot>();
    public DbSet<FinancialStatementSnapshot> FinancialStatementSnapshots => Set<FinancialStatementSnapshot>();
    public DbSet<FinancialStatementSnapshotLine> FinancialStatementSnapshotLines => Set<FinancialStatementSnapshotLine>();
    public DbSet<FinancialStatementMapping> FinancialStatementMappings => Set<FinancialStatementMapping>();
    public DbSet<CompanySimulationState> CompanySimulationStates => Set<CompanySimulationState>();
    public DbSet<CompanySimulationRunHistory> CompanySimulationRunHistories => Set<CompanySimulationRunHistory>();
    public DbSet<MailboxConnection> MailboxConnections => Set<MailboxConnection>();
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
    public DbSet<MarketingContentBrief> MarketingContentBriefs => Set<MarketingContentBrief>();
    public DbSet<MarketingContentVariant> MarketingContentVariants => Set<MarketingContentVariant>();
    public DbSet<MarketingSalesHandoff> MarketingSalesHandoffs => Set<MarketingSalesHandoff>();
    public DbSet<MarketingChannelObservation> MarketingChannelObservations => Set<MarketingChannelObservation>();
    public DbSet<MarketingExperiment> MarketingExperiments => Set<MarketingExperiment>();
    public DbSet<MarketingQualificationDefinition> MarketingQualificationDefinitions => Set<MarketingQualificationDefinition>();
    public DbSet<MarketingQualificationEvaluation> MarketingQualificationEvaluations => Set<MarketingQualificationEvaluation>();
    public DbSet<MarketingQualificationFeedback> MarketingQualificationFeedback => Set<MarketingQualificationFeedback>();
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
        ApplyFinanceSourceTrackingDefaults();
        EnsureBankTransactionPostingStates();

        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateCompanyOwnedMutations();
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
                entry.Entity is Payment ||
                entry.Entity is Budget ||
                entry.Entity is Forecast ||
                entry.Entity is CompanyBankAccount ||
                entry.Entity is BankTransaction ||
                entry.Entity is BankTransactionPaymentLink ||
                entry.Entity is BankTransactionPostingStateRecord ||
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
                entry.Entity is TrialBalanceSnapshot ||
                entry.Entity is FinancialStatementSnapshot ||
                entry.Entity is FinancialStatementSnapshotLine ||
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
        modelBuilder.Entity<SupplierApprovalAutomationRule>()
            .HasQueryFilter(rule =>
                CurrentCompanyId != null && rule.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierSubscription>()
            .HasQueryFilter(subscription =>
                CurrentCompanyId != null && subscription.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<SupplierSubscriptionBillMatch>()
            .HasQueryFilter(match =>
                CurrentCompanyId != null && match.CompanyId == CurrentCompanyId);
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
        modelBuilder.Entity<TrialBalanceSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinancialStatementSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId != null && snapshot.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<FinancialStatementSnapshotLine>()
            .HasQueryFilter(line =>
                CurrentCompanyId != null && line.CompanyId == CurrentCompanyId);
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

            var defaultValueSql = property.GetDefaultValueSql();
            if (defaultValueSql?.StartsWith("N'", StringComparison.OrdinalIgnoreCase) == true)
            {
                property.SetDefaultValueSql(defaultValueSql[1..]);
            }
        }

        modelBuilder.Entity<DealIntelligenceSignal>().ToTable(table =>
            table.HasCheckConstraint(
                "CK_deal_intelligence_signals_explanation_required",
                "LENGTH(TRIM(explanation)) > 0"));
    }
}
