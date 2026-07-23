using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Data;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceMaintenanceService : IFinanceMaintenanceService
{
    private const string InvoiceReviewTaskType = "invoice_approval_review";
    private const string BillReviewTaskType = "bill_approval_review";
    private const string AnomalyTaskType = "finance_transaction_anomaly_follow_up";
    private const string InvoiceReviewCorrelationPrefix = "invoice-review";
    private const string FinanceSimulationCorrelationPrefix = "finance-sim:";
    private const string FinanceAnomalyCorrelationPrefix = "fin-anom:";
    private const string InvoiceReviewApprovalType = "invoice_review";
    private const string BillReviewApprovalType = "bill_review";
    private const string PaymentReviewApprovalType = "payment_review";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IExecutiveCockpitDashboardCacheInvalidator? _dashboardCacheInvalidator;
    private readonly ILogger<CompanyFinanceMaintenanceService> _logger;

    public CompanyFinanceMaintenanceService(
        VirtualCompanyDbContext dbContext,
        IExecutiveCockpitDashboardCacheInvalidator? dashboardCacheInvalidator,
        ILogger<CompanyFinanceMaintenanceService> logger)
    {
        _dbContext = dbContext;
        _dashboardCacheInvalidator = dashboardCacheInvalidator;
        _logger = logger;
    }

    public async Task<FinanceDataResetResultDto> ResetFinancialDataAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        _logger.LogInformation("Resetting financial data for company {CompanyId}.", companyId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var companyExists = await _dbContext.Companies
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == companyId, cancellationToken);
            if (!companyExists)
            {
                throw new KeyNotFoundException($"Company '{companyId:D}' was not found.");
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            await DeleteWorkflowAndOperationalStateAsync(companyId, counts, cancellationToken);
            await DeleteFinanceTransactionalStateAsync(companyId, counts, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var totalDeleted = counts.Values.Sum();
            return new FinanceDataResetResultDto(companyId, totalDeleted, counts);
        });

        if (_dashboardCacheInvalidator is not null)
        {
            await _dashboardCacheInvalidator.InvalidateAsync(companyId, cancellationToken);
        }

        _logger.LogInformation(
            "Deleted {InvoiceCount} invoices, {BillCount} bills, {TransactionCount} finance transactions, {PaymentCount} payments, {LedgerEntryCount} ledger entries, and {InsightCount} finance insights for company {CompanyId}.",
            GetCount(result, "finance_invoices"),
            GetCount(result, "finance_bills"),
            GetCount(result, "finance_transactions"),
            GetCount(result, "payments"),
            GetCount(result, "ledger_entries"),
            GetCount(result, "finance_agent_insights"),
            companyId);
        _logger.LogInformation(
            "Financial data reset completed for company {CompanyId}. Deleted {TotalDeleted} record(s).",
            companyId,
            result.TotalDeleted);

        return result;
    }

    private async Task DeleteWorkflowAndOperationalStateAsync(
        Guid companyId,
        Dictionary<string, int> counts,
        CancellationToken cancellationToken)
    {
        var financeWorkflowInstances = _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                ((x.TriggerRef != null && x.TriggerRef.StartsWith("finance.")) ||
                 (x.TriggerRef != null && x.TriggerRef.StartsWith("finance-")) ||
                 _dbContext.WorkflowDefinitions
                     .IgnoreQueryFilters()
                     .Any(definition =>
                         definition.Id == x.DefinitionId &&
                         (definition.Department == "finance" || definition.Code.StartsWith("FINANCE")))));

        var financeTasks = _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.Type == InvoiceReviewTaskType ||
                 x.Type == BillReviewTaskType ||
                 x.Type == AnomalyTaskType ||
                 x.Type.StartsWith("finance_") ||
                 x.Title.StartsWith("Review invoice ") ||
                 x.Title.StartsWith("Review bill ") ||
                 x.Title.StartsWith("Invoice requires approval") ||
                 x.Title.StartsWith("Bill requires approval") ||
                 x.Title.StartsWith("Payment requires approval") ||
                 x.Title.Contains("SIM-INV-") ||
                 x.Title.Contains("SIM-BILL-") ||
                 (x.CorrelationId != null && x.CorrelationId.StartsWith(InvoiceReviewCorrelationPrefix)) ||
                 (x.CorrelationId != null && x.CorrelationId.StartsWith(FinanceSimulationCorrelationPrefix)) ||
                 (x.CorrelationId != null && x.CorrelationId.StartsWith(FinanceAnomalyCorrelationPrefix)) ||
                 (x.TriggerEventId != null && x.TriggerEventId.StartsWith("finance.")) ||
                 (x.WorkflowInstanceId.HasValue && financeWorkflowInstances.Any(workflow => workflow.Id == x.WorkflowInstanceId.Value))));

        var financeApprovalRequests = _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                ((x.TargetEntityType == "task" && financeTasks.Any(task => task.Id == x.TargetEntityId)) ||
                 (x.TargetEntityType == "workflow" && financeWorkflowInstances.Any(workflow => workflow.Id == x.TargetEntityId)) ||
                 x.ApprovalType == InvoiceReviewApprovalType ||
                 x.ApprovalType == BillReviewApprovalType ||
                 x.ApprovalType == PaymentReviewApprovalType ||
                 x.ApprovalType.StartsWith("finance_") ||
                 (x.ApprovalTarget != null && x.ApprovalTarget.StartsWith("finance"))));

        await DeleteAsync(
            counts,
            "conversation_task_links",
            _dbContext.ConversationTaskLinks
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && financeTasks.Any(task => task.Id == x.TaskId)),
            cancellationToken);

        await DeleteAsync(
            counts,
            "finance_approval_notifications",
            _dbContext.CompanyNotifications
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.RelatedEntityType == "approval_request" &&
                    x.RelatedEntityId.HasValue &&
                    financeApprovalRequests.Any(approval => approval.Id == x.RelatedEntityId.Value)),
            cancellationToken);

        await DeleteAsync(
            counts,
            "approval_requests",
            financeApprovalRequests,
            cancellationToken);

        await DeleteAsync(
            counts,
            "approval_tasks",
            _dbContext.ApprovalTasks
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && (x.TargetType == ApprovalTargetType.Bill || x.TargetType == ApprovalTargetType.Payment)),
            cancellationToken);

        await DeleteAsync(
            counts,
            "workflow_exceptions",
            _dbContext.WorkflowExceptions
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && financeWorkflowInstances.Any(workflow => workflow.Id == x.WorkflowInstanceId)),
            cancellationToken);

        await DeleteAsync(
            counts,
            "finance_child_tasks",
            _dbContext.WorkTasks
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.ParentTaskId.HasValue && financeTasks.Any(parent => parent.Id == x.ParentTaskId.Value)),
            cancellationToken);

        await DeleteAsync(counts, "finance_tasks", financeTasks, cancellationToken);

        await DeleteAsync(
            counts,
            "workflow_instances",
            financeWorkflowInstances,
            cancellationToken);

        await DeleteAsync(
            counts,
            "processed_workflow_trigger_events",
            _dbContext.ProcessedWorkflowTriggerEvents
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.EventId.StartsWith("finance.")),
            cancellationToken);

        await DeleteAsync(
            counts,
            "condition_trigger_evaluations",
            _dbContext.ConditionTriggerEvaluations
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == companyId &&
                    (x.ConditionDefinitionId.StartsWith("finance") ||
                     (x.EntityType != null && x.EntityType.StartsWith("finance")))),
            cancellationToken);

        await DeleteAsync(
            counts,
            "finance_outbox_messages",
            _dbContext.CompanyOutboxMessages
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Topic.StartsWith("finance.")),
            cancellationToken);

        await DeleteAsync(
            counts,
            "finance_alerts",
            _dbContext.Alerts
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == companyId &&
                    (x.Fingerprint.StartsWith("finance-") ||
                     x.Fingerprint.StartsWith("finance:") ||
                     x.CorrelationId.StartsWith("finance-") ||
                     x.CorrelationId.StartsWith("finance:"))),
            cancellationToken);

        await DeleteAsync(
            counts,
            "finance_activity_events",
            _dbContext.ActivityEvents
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == companyId &&
                    (x.EventType.StartsWith("finance.") ||
                     (x.CorrelationId != null && x.CorrelationId.StartsWith("finance-")) ||
                     x.Department == "finance")),
            cancellationToken);

        await DeleteAsync(
            counts,
            "finance_background_exceptions",
            _dbContext.ExecutionExceptionRecords
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == companyId &&
                    ((x.RelatedEntityType != null && x.RelatedEntityType.StartsWith("finance")) ||
                     x.IncidentKey.StartsWith("finance"))),
            cancellationToken);

        await DeleteAsync(
            counts,
            "finance_background_executions",
            _dbContext.BackgroundExecutions
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == companyId &&
                    (x.RelatedEntityType.StartsWith("finance") ||
                     x.IdempotencyKey.StartsWith("finance-") ||
                     x.IdempotencyKey.StartsWith("finance:") ||
                     x.CorrelationId.StartsWith("finance-"))),
            cancellationToken);
    }

    private async Task DeleteFinanceTransactionalStateAsync(
        Guid companyId,
        Dictionary<string, int> counts,
        CancellationToken cancellationToken)
    {
        await DeleteAsync(counts, "finance_workflow_trigger_check_executions", _dbContext.FinanceWorkflowTriggerCheckExecutions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_workflow_trigger_executions", _dbContext.FinanceWorkflowTriggerExecutions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_agent_insights", _dbContext.FinanceAgentInsights.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);

        await DeleteAsync(counts, "financial_statement_snapshot_lines", _dbContext.FinancialStatementSnapshotLines.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "financial_statement_snapshots", _dbContext.FinancialStatementSnapshots.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "trial_balance_snapshots", _dbContext.TrialBalanceSnapshots.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);

        await DeleteAsync(counts, "payment_allocations", _dbContext.PaymentAllocations.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "bank_transaction_payment_links", _dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "payment_cash_ledger_links", _dbContext.PaymentCashLedgerLinks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "bank_transaction_cash_ledger_links", _dbContext.BankTransactionCashLedgerLinks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "bank_transaction_posting_states", _dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "reconciliation_results", _dbContext.ReconciliationResultRecords.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "reconciliation_suggestions", _dbContext.ReconciliationSuggestionRecords.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);

        await DeleteAsync(counts, "ledger_entry_source_mappings", _dbContext.LedgerEntrySourceMappings.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "ledger_entry_lines", _dbContext.LedgerEntryLines.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "ledger_entries", _dbContext.LedgerEntries.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);

        await DeleteAsync(counts, "finance_transactions", _dbContext.FinanceTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "payments", _dbContext.Payments.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "bank_transactions", _dbContext.BankTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_seed_anomalies", _dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_assets", _dbContext.FinanceAssets.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_balances", _dbContext.FinanceBalances.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_invoices", _dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_bills", _dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "budgets", _dbContext.Budgets.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "forecasts", _dbContext.Forecasts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);

        await DeleteAsync(counts, "simulation_cash_delta_records", _dbContext.SimulationCashDeltaRecords.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "simulation_event_records", _dbContext.SimulationEventRecords.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_simulation_step_logs", _dbContext.FinanceSimulationStepLogs.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
        await DeleteAsync(counts, "finance_seed_backfill_attempts", _dbContext.FinanceSeedBackfillAttempts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId), cancellationToken);
    }

    private async Task DeleteAsync<TEntity>(
        Dictionary<string, int> counts,
        string key,
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(TEntity));
        var tableName = entityType?.GetTableName();
        var schema = entityType?.GetSchema();
        if (string.IsNullOrWhiteSpace(tableName) ||
            !await TableExistsAsync(tableName, schema, cancellationToken))
        {
            counts[key] = 0;
            _logger.LogWarning(
                "Skipped deleting finance reset scope {ResetScope} because mapped table {TableName} does not exist in the current database schema.",
                key,
                tableName ?? typeof(TEntity).Name);
            return;
        }

        var deleted = await query.ExecuteDeleteAsync(cancellationToken);
        counts[key] = deleted;
    }

    private async Task<bool> TableExistsAsync(string tableName, string? schema, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();

            if (string.Equals(_dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
                var sqliteParameter = command.CreateParameter();
                sqliteParameter.ParameterName = "$tableName";
                sqliteParameter.Value = tableName;
                command.Parameters.Add(sqliteParameter);

                var sqliteResult = await command.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt64(sqliteResult) > 0;
            }

            if (string.Equals(_dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                command.CommandText = """
                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = COALESCE(@schema, current_schema())
                          AND table_name = @tableName
                    );
                    """;
                var schemaParameter = command.CreateParameter();
                schemaParameter.ParameterName = "@schema";
                schemaParameter.Value = string.IsNullOrWhiteSpace(schema) ? DBNull.Value : schema;
                command.Parameters.Add(schemaParameter);

                var postgresParameter = command.CreateParameter();
                postgresParameter.ParameterName = "@tableName";
                postgresParameter.Value = tableName;
                command.Parameters.Add(postgresParameter);

                return await command.ExecuteScalarAsync(cancellationToken) is true;
            }

            command.CommandText = "SELECT OBJECT_ID(@tableName, 'U');";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = string.IsNullOrWhiteSpace(schema) ? tableName : $"{schema}.{tableName}";
            command.Parameters.Add(parameter);

            return await command.ExecuteScalarAsync(cancellationToken) is not null and not DBNull;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static int GetCount(FinanceDataResetResultDto result, string key) =>
        result.DeletedCounts.TryGetValue(key, out var count) ? count : 0;
}
