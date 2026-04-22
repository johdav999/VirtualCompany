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
    public DbSet<FinanceAsset> FinanceAssets => Set<FinanceAsset>();
    public DbSet<FinanceBalance> FinanceBalances => Set<FinanceBalance>();
    public DbSet<FinancePolicyConfiguration> FinancePolicyConfigurations => Set<FinancePolicyConfiguration>();
    public DbSet<FinanceSeedAnomaly> FinanceSeedAnomalies => Set<FinanceSeedAnomaly>();
    public DbSet<FinanceSimulationStepLog> FinanceSimulationStepLogs => Set<FinanceSimulationStepLog>();
    public DbSet<FinanceSeedBackfillRun> FinanceSeedBackfillRuns => Set<FinanceSeedBackfillRun>();
    public DbSet<FinanceSeedBackfillAttempt> FinanceSeedBackfillAttempts => Set<FinanceSeedBackfillAttempt>();
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
    public DbSet<CompanySimulationRunTransition> CompanySimulationRunTransitions => Set<CompanySimulationRunTransition>();
    public DbSet<CompanySimulationRunDayLog> CompanySimulationRunDayLogs => Set<CompanySimulationRunDayLog>();
    public DbSet<SimulationCashDeltaRecord> SimulationCashDeltaRecords => Set<SimulationCashDeltaRecord>();
    public DbSet<SimulationEventRecord> SimulationEventRecords => Set<SimulationEventRecord>();

    internal Guid? CurrentCompanyId => _companyContextAccessor?.CompanyId;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateCompanyOwnedMutations();
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
                entry.Entity is LedgerEntry ||
                entry.Entity is LedgerEntrySourceMapping ||
                entry.Entity is LedgerEntryLine ||
                entry.Entity is TrialBalanceSnapshot ||
                entry.Entity is FinancialStatementSnapshot ||
                entry.Entity is FinancialStatementSnapshotLine ||
                entry.Entity is SimulationCashDeltaRecord ||
                entry.Entity is SimulationEventRecord)
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
        modelBuilder.Entity<CompanyOwnedNote>()
            .HasQueryFilter(note =>
                CurrentCompanyId.HasValue && note.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<BackgroundExecution>()
            .HasQueryFilter(execution =>
                CurrentCompanyId.HasValue && execution.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ExecutionExceptionRecord>()
            .HasQueryFilter(executionException =>
                CurrentCompanyId.HasValue && executionException.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(auditEvent =>
                CurrentCompanyId.HasValue && auditEvent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ActivityEvent>()
            .HasQueryFilter(activityEvent =>
                CurrentCompanyId.HasValue && activityEvent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Agent>()
            .HasQueryFilter(agent =>
                CurrentCompanyId.HasValue && agent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ToolExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId.HasValue && attempt.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AgentScheduledTrigger>()
            .HasQueryFilter(trigger =>
                CurrentCompanyId.HasValue && trigger.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<TriggerExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId.HasValue && attempt.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AgentScheduledTriggerEnqueueWindow>()
            .HasQueryFilter(window =>
                CurrentCompanyId.HasValue && window.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkTask>()
            .HasQueryFilter(task =>
                CurrentCompanyId.HasValue && task.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AgentTaskCreationDedupeRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId.HasValue && record.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ApprovalRequest>()
            .HasQueryFilter(request =>
                CurrentCompanyId.HasValue && request.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ApprovalTask>()
            .HasQueryFilter(task =>
                CurrentCompanyId.HasValue && task.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Conversation>()
            .HasQueryFilter(conversation =>
                CurrentCompanyId.HasValue && conversation.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Message>()
            .HasQueryFilter(message =>
                CurrentCompanyId.HasValue && message.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ConversationTaskLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId.HasValue && link.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefing>()
            .HasQueryFilter(briefing =>
                CurrentCompanyId.HasValue && briefing.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingSection>()
            .HasQueryFilter(section =>
                CurrentCompanyId.HasValue && section.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingContribution>()
            .HasQueryFilter(contribution =>
                CurrentCompanyId.HasValue && contribution.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingUpdateJob>()
            .HasQueryFilter(job =>
                CurrentCompanyId.HasValue && job.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingDeliveryPreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId.HasValue && preference.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingSeverityRule>()
            .HasQueryFilter(rule =>
                CurrentCompanyId.HasValue && rule.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<UserBriefingPreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId.HasValue && preference.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<TenantBriefingDefault>()
            .HasQueryFilter(defaults =>
                CurrentCompanyId.HasValue && defaults.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyNotification>()
            .HasQueryFilter(notification =>
                CurrentCompanyId.HasValue && notification.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ProactiveMessage>()
            .HasQueryFilter(message =>
                CurrentCompanyId.HasValue && message.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ProactiveMessagePolicyDecision>()
            .HasQueryFilter(decision =>
                CurrentCompanyId.HasValue && decision.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowDefinition>()
            .HasQueryFilter(definition =>
                CurrentCompanyId.HasValue && (definition.CompanyId == CurrentCompanyId.Value || definition.CompanyId == null));
        modelBuilder.Entity<WorkflowTrigger>()
            .HasQueryFilter(trigger =>
                CurrentCompanyId.HasValue && trigger.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowInstance>()
            .HasQueryFilter(instance =>
                CurrentCompanyId.HasValue && instance.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ProcessedWorkflowTriggerEvent>()
            .HasQueryFilter(processedEvent =>
                CurrentCompanyId.HasValue && processedEvent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowException>()
            .HasQueryFilter(workflowException =>
                CurrentCompanyId.HasValue && workflowException.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ConditionTriggerEvaluation>()
            .HasQueryFilter(evaluation =>
                CurrentCompanyId.HasValue && evaluation.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyKnowledgeDocument>()
            .HasQueryFilter(document =>
                CurrentCompanyId.HasValue && document.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyKnowledgeChunk>()
            .HasQueryFilter(chunk =>
                CurrentCompanyId.HasValue && chunk.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<MemoryItem>()
            .HasQueryFilter(memoryItem =>
                CurrentCompanyId.HasValue && memoryItem.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ContextRetrieval>()
            .HasQueryFilter(retrieval =>
                CurrentCompanyId.HasValue && retrieval.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ContextRetrievalSource>()
            .HasQueryFilter(source =>
                CurrentCompanyId.HasValue && source.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Alert>()
            .HasQueryFilter(alert =>
                CurrentCompanyId.HasValue && alert.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Escalation>()
            .HasQueryFilter(escalation =>
                CurrentCompanyId.HasValue && escalation.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<DashboardDepartmentConfig>()
            .HasQueryFilter(config =>
                CurrentCompanyId.HasValue && config.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<DashboardWidgetConfig>()
            .HasQueryFilter(config =>
                CurrentCompanyId.HasValue && config.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<InsightAcknowledgment>()
            .HasQueryFilter(acknowledgment =>
                CurrentCompanyId.HasValue && acknowledgment.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceAccount>()
            .HasQueryFilter(account =>
                CurrentCompanyId.HasValue && account.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Budget>()
            .HasQueryFilter(budget =>
                CurrentCompanyId.HasValue && budget.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Forecast>()
            .HasQueryFilter(forecast =>
                CurrentCompanyId.HasValue && forecast.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Payment>()
            .HasQueryFilter(payment =>
                CurrentCompanyId.HasValue && payment.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBankAccount>()
            .HasQueryFilter(bankAccount =>
                CurrentCompanyId.HasValue && bankAccount.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<BankTransaction>()
            .HasQueryFilter(bankTransaction =>
                CurrentCompanyId.HasValue && bankTransaction.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<BankTransactionPaymentLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId.HasValue && link.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<BankTransactionPostingStateRecord>()
            .HasQueryFilter(state =>
                CurrentCompanyId.HasValue && state.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<PaymentCashLedgerLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId.HasValue && link.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<BankTransactionCashLedgerLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId.HasValue && link.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<PaymentAllocation>()
            .HasQueryFilter(allocation =>
                CurrentCompanyId.HasValue && allocation.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ReconciliationSuggestionRecord>()
            .HasQueryFilter(suggestion =>
                CurrentCompanyId.HasValue && suggestion.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ReconciliationResultRecord>()
            .HasQueryFilter(result =>
                CurrentCompanyId.HasValue && result.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceTransaction>()
            .HasQueryFilter(transaction =>
                CurrentCompanyId.HasValue && transaction.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceInvoice>()
            .HasQueryFilter(invoice =>
                CurrentCompanyId.HasValue && invoice.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceBill>()
            .HasQueryFilter(bill =>
                CurrentCompanyId.HasValue && bill.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceAsset>()
            .HasQueryFilter(asset =>
                CurrentCompanyId.HasValue && asset.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceBalance>()
            .HasQueryFilter(balance =>
                CurrentCompanyId.HasValue && balance.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceCounterparty>()
            .HasQueryFilter(counterparty =>
                CurrentCompanyId.HasValue && counterparty.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinancePolicyConfiguration>()
            .HasQueryFilter(policy =>
                CurrentCompanyId.HasValue && policy.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceSeedAnomaly>()
            .HasQueryFilter(anomaly =>
                CurrentCompanyId.HasValue && anomaly.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinanceSimulationStepLog>()
            .HasQueryFilter(log =>
                CurrentCompanyId.HasValue && log.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FiscalPeriod>()
            .HasQueryFilter(period =>
                CurrentCompanyId.HasValue && period.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<LedgerEntry>()
            .HasQueryFilter(entry =>
                CurrentCompanyId.HasValue && entry.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<LedgerEntrySourceMapping>()
            .HasQueryFilter(mapping =>
                CurrentCompanyId.HasValue && mapping.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<LedgerEntryLine>()
            .HasQueryFilter(line =>
                CurrentCompanyId.HasValue && line.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<TrialBalanceSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId.HasValue && snapshot.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinancialStatementSnapshot>()
            .HasQueryFilter(snapshot =>
                CurrentCompanyId.HasValue && snapshot.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<FinancialStatementSnapshotLine>()
            .HasQueryFilter(line =>
                CurrentCompanyId.HasValue && line.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanySimulationState>()
            .HasQueryFilter(state =>
                CurrentCompanyId.HasValue && state.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanySimulationRunHistory>()
            .HasQueryFilter(history =>
                CurrentCompanyId.HasValue && history.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanySimulationRunTransition>()
            .HasQueryFilter(transition =>
                CurrentCompanyId.HasValue && transition.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanySimulationRunDayLog>()
            .HasQueryFilter(log =>
                CurrentCompanyId.HasValue && log.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<SimulationCashDeltaRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId.HasValue && record.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<SimulationEventRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId.HasValue && record.CompanyId == CurrentCompanyId.Value);
    }
}
