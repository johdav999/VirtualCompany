using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAnomalyDetectionOptions
{
    public const string SectionName = "FinanceAnomalyDetection";

    public int HistoricalLookbackDays { get; set; } = 180;
    public int MinimumHistoricalSampleSize { get; set; } = 3;
    public decimal HistoricalAverageMultiplier { get; set; } = 3m;
    public int DeduplicationWindowHours { get; set; } = 24;
}

public sealed class CompanyFinanceTransactionAnomalyDetectionService : IFinanceTransactionAnomalyDetectionService
{
    private const string FollowUpTaskType = "finance_transaction_anomaly_follow_up";
    private const string CorrelationPrefix = "finance-transaction-anomaly";
    private const string FinanceWorkflowQueue = "finance_workflow_queue";
    private const string SourceWorkflow = "transaction_anomaly_detection";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinancePolicyConfigurationService _policyConfigurationService;
    private readonly FinanceAnomalyDetectionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyOutboxEnqueuer? _outboxEnqueuer;

    public CompanyFinanceTransactionAnomalyDetectionService(
        VirtualCompanyDbContext dbContext,
        IFinancePolicyConfigurationService policyConfigurationService,
        IOptions<FinanceAnomalyDetectionOptions> options,
        TimeProvider timeProvider)
        : this(dbContext, policyConfigurationService, options, timeProvider, null)
    {
    }

    public CompanyFinanceTransactionAnomalyDetectionService(
        VirtualCompanyDbContext dbContext,
        IFinancePolicyConfigurationService policyConfigurationService,
        IOptions<FinanceAnomalyDetectionOptions> options,
        TimeProvider timeProvider,
        ICompanyOutboxEnqueuer? outboxEnqueuer)
    {
        _dbContext = dbContext;
        _policyConfigurationService = policyConfigurationService;
        _options = options.Value;
        _timeProvider = timeProvider;
        _outboxEnqueuer = outboxEnqueuer;
    }

    public async Task<FinanceTransactionAnomalyEvaluationDto> EvaluateAsync(
        EvaluateFinanceTransactionAnomalyCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var evaluatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var transaction = await LoadTransactionAsync(command.CompanyId, command.TransactionId, cancellationToken);
        var policy = await _policyConfigurationService.GetPolicyConfigurationAsync(
            new GetFinancePolicyConfigurationQuery(command.CompanyId),
            cancellationToken);
        var baseline = await LoadHistoricalBaselineAsync(command.CompanyId, transaction, cancellationToken);
        var decisions = BuildDecisions(transaction, policy, baseline);
        var sourceAgentId = command.AgentId ?? await ResolveLauraAgentIdAsync(command.CompanyId, cancellationToken);
        var dedupeWindow = BuildDedupeWindow(evaluatedUtc);
        var anomalies = new List<FinanceTransactionAnomalyDto>();

        foreach (var decision in decisions)
        {
            var alert = await CreateOrRefreshAlertAsync(
                command,
                transaction,
                policy,
                baseline,
                decision,
                sourceAgentId,
                evaluatedUtc,
                dedupeWindow,
                cancellationToken);
            var task = await CreateOrReuseFollowUpTaskAsync(
                command,
                transaction,
                policy,
                baseline,
                decision,
                alert.Alert.Id,
                sourceAgentId,
                dedupeWindow,
                cancellationToken);

            var workflowOutput = BuildWorkflowOutput(decision);
            var correlationId = BuildCorrelationId(command.CompanyId, command.TransactionId, decision.AnomalyType, dedupeWindow);
            anomalies.Add(new FinanceTransactionAnomalyDto(
                decision.AnomalyType,
                decision.Explanation,
                decision.Confidence,
                decision.RecommendedAction,
                alert.Alert.Id,
                task.Id,
                alert.Created,
                alert.Deduplicated)
            {
                WorkflowOutput = workflowOutput
            });

            FinanceDomainEvents.EnqueueThresholdBreached(
                _outboxEnqueuer,
                command.CompanyId,
                decision.AnomalyType,
                "finance_transaction",
                transaction.Id,
                evaluatedUtc,
                BuildThresholdEvaluationDetails(transaction, policy, baseline, decision, workflowOutput, alert.Alert.Id, task.Id, dedupeWindow),
                correlationId,
                dedupeWindow.StartUtc.ToString("yyyyMMddHH"));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceTransactionAnomalyEvaluationDto(
            command.CompanyId,
            command.TransactionId,
            evaluatedUtc,
            anomalies.Count > 0,
            anomalies);
    }

    private async Task<TransactionRow> LoadTransactionAsync(
        Guid companyId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == transactionId)
            .Select(x => new TransactionRow(
                x.Id,
                x.CompanyId,
                x.AccountId,
                x.Account.Name,
                x.CounterpartyId,
                x.Counterparty == null ? null : x.Counterparty.Name,
                x.InvoiceId,
                x.BillId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference))
            .SingleOrDefaultAsync(cancellationToken);

        return row ?? throw new KeyNotFoundException("Finance transaction was not found.");
    }

    private async Task<HistoricalBaseline> LoadHistoricalBaselineAsync(
        Guid companyId,
        TransactionRow transaction,
        CancellationToken cancellationToken)
    {
        var lookbackDays = Math.Max(1, _options.HistoricalLookbackDays);
        var startUtc = transaction.TransactionUtc.AddDays(-lookbackDays);
        var sameDirectionIsDebit = transaction.Amount < 0;

        var candidates = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Id != transaction.Id &&
                x.Currency == transaction.Currency &&
                x.TransactionUtc >= startUtc &&
                x.TransactionUtc < transaction.TransactionUtc &&
                x.Amount < 0 == sameDirectionIsDebit)
            .Where(x =>
                x.AccountId == transaction.AccountId ||
                x.CounterpartyId == transaction.CounterpartyId ||
                x.TransactionType == transaction.TransactionType)
            .Select(x => new HistoricalTransactionRow(x.TransactionUtc, Math.Abs(x.Amount)))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new HistoricalBaseline(0, 0m, 0m, null, null);
        }

        return new HistoricalBaseline(
            candidates.Count,
            candidates.Average(x => x.AbsoluteAmount),
            candidates.Max(x => x.AbsoluteAmount),
            candidates.Min(x => x.TransactionUtc),
            candidates.Max(x => x.TransactionUtc));
    }

    private IReadOnlyList<AnomalyDecision> BuildDecisions(
        TransactionRow transaction,
        FinancePolicyConfigurationDto policy,
        HistoricalBaseline baseline)
    {
        var decisions = new List<AnomalyDecision>();
        var amount = transaction.Amount;
        var absoluteAmount = Math.Abs(amount);

        if (amount < policy.AnomalyDetectionLowerBound || amount > policy.AnomalyDetectionUpperBound)
        {
            var bound = amount < policy.AnomalyDetectionLowerBound
                ? policy.AnomalyDetectionLowerBound
                : policy.AnomalyDetectionUpperBound;
            var direction = amount < policy.AnomalyDetectionLowerBound ? "below" : "above";
            decisions.Add(new AnomalyDecision(
                "threshold_breach",
                $"Transaction {transaction.ExternalReference} is {amount:0.##} {transaction.Currency}, which is {direction} the configured anomaly threshold of {bound:0.##} {policy.ApprovalCurrency}.",
                absoluteAmount >= Math.Abs(bound) * 2m ? 0.93m : 0.86m,
                "Open a finance review, verify the supporting document and counterparty, and hold any related payment action until reviewed.",
                amount < policy.AnomalyDetectionLowerBound || amount > policy.AnomalyDetectionUpperBound * 2m
                    ? AlertSeverity.High
                    : AlertSeverity.Medium));
        }

        var minimumSampleSize = Math.Max(1, _options.MinimumHistoricalSampleSize);
        var multiplier = Math.Max(1m, _options.HistoricalAverageMultiplier);
        if (baseline.SampleSize >= minimumSampleSize &&
            baseline.AverageAbsoluteAmount > 0m &&
            absoluteAmount >= baseline.AverageAbsoluteAmount * multiplier)
        {
            decisions.Add(new AnomalyDecision(
                "historical_baseline_deviation",
                $"Transaction {transaction.ExternalReference} is {absoluteAmount:0.##} {transaction.Currency}, compared with a historical average of {baseline.AverageAbsoluteAmount:0.##} {transaction.Currency} across {baseline.SampleSize} similar transaction(s).",
                absoluteAmount >= baseline.AverageAbsoluteAmount * (multiplier + 1m) ? 0.9m : 0.82m,
                "Compare against recent similar transactions, confirm the business reason with the owner, and attach evidence before closing the follow-up.",
                absoluteAmount >= baseline.AverageAbsoluteAmount * (multiplier + 1m)
                    ? AlertSeverity.High
                    : AlertSeverity.Medium));
        }

        if (policy.RequireCounterpartyForTransactions && !transaction.CounterpartyId.HasValue)
        {
            decisions.Add(new AnomalyDecision(
                "missing_counterparty",
                $"Transaction {transaction.ExternalReference} has no counterparty, but the active finance policy requires counterparties for transactions.",
                0.78m,
                "Assign or verify the counterparty before the transaction is reconciled.",
                AlertSeverity.Medium));
        }

        return decisions
            .GroupBy(x => x.AnomalyType, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private async Task<AlertMutation> CreateOrRefreshAlertAsync(
        EvaluateFinanceTransactionAnomalyCommand command,
        TransactionRow transaction,
        FinancePolicyConfigurationDto policy,
        HistoricalBaseline baseline,
        AnomalyDecision decision,
        Guid? sourceAgentId,
        DateTime evaluatedUtc,
        DedupeWindow dedupeWindow,
        CancellationToken cancellationToken)
    {
        var baseFingerprint = BuildDedupeKey(command.CompanyId, command.TransactionId, decision.AnomalyType);
        var windowedFingerprint = BuildWindowedFingerprint(baseFingerprint, dedupeWindow);
        var windowedFingerprintPrefix = $"{baseFingerprint}:window:";
        var correlationId = BuildCorrelationId(command.CompanyId, command.TransactionId, decision.AnomalyType, dedupeWindow);
        var evidence = BuildEvidence(transaction, policy, baseline, decision, evaluatedUtc, dedupeWindow);
        var metadata = BuildMetadata(command, transaction, baseline, decision, dedupeWindow);
        var dedupeCutoffUtc = evaluatedUtc.AddHours(-dedupeWindow.Hours);

        var existing = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == command.CompanyId &&
                (x.Fingerprint == baseFingerprint || x.Fingerprint.StartsWith(windowedFingerprintPrefix)) &&
                (x.Status == AlertStatus.Open || x.Status == AlertStatus.Acknowledged) &&
                (x.LastDetectedUtc ?? x.CreatedUtc) >= dedupeCutoffUtc)
            .OrderByDescending(x => x.LastDetectedUtc ?? x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            existing.RefreshFromDuplicateDetection(
                decision.Severity,
                BuildAlertTitle(transaction, decision),
                decision.Explanation,
                evidence,
                correlationId,
                sourceAgentId,
                metadata);
            return new AlertMutation(existing, Created: false, Deduplicated: true);
        }

        var alert = new Alert(
            Guid.NewGuid(),
            command.CompanyId,
            AlertType.Anomaly,
            decision.Severity,
            BuildAlertTitle(transaction, decision),
            decision.Explanation,
            evidence,
            correlationId,
            windowedFingerprint,
            AlertStatus.Open,
            sourceAgentId,
            metadata);
        _dbContext.Alerts.Add(alert);
        return new AlertMutation(alert, Created: true, Deduplicated: false);
    }

    private async Task<WorkTask> CreateOrReuseFollowUpTaskAsync(
        EvaluateFinanceTransactionAnomalyCommand command,
        TransactionRow transaction,
        FinancePolicyConfigurationDto policy,
        HistoricalBaseline baseline,
        AnomalyDecision decision,
        Guid alertId,
        Guid? sourceAgentId,
        DedupeWindow dedupeWindow,
        CancellationToken cancellationToken)
    {
        var correlationId = BuildCorrelationId(command.CompanyId, command.TransactionId, decision.AnomalyType, dedupeWindow);
        var existingTask = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == command.CompanyId &&
                x.Type == FollowUpTaskType &&
                x.CorrelationId == correlationId &&
                x.Status != WorkTaskStatus.Completed &&
                x.Status != WorkTaskStatus.Failed)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingTask is not null)
        {
            return existingTask;
        }

        var payload = BuildTaskPayload(command, transaction, policy, baseline, decision, alertId, dedupeWindow);
        var task = new WorkTask(
            Guid.NewGuid(),
            command.CompanyId,
            FollowUpTaskType,
            $"Review anomalous transaction {transaction.ExternalReference}",
            decision.Explanation,
            decision.Severity is AlertSeverity.Critical or AlertSeverity.High ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            sourceAgentId,
            null,
            "agent",
            sourceAgentId,
            payload,
            command.WorkflowInstanceId,
            null,
            decision.RecommendedAction,
            decision.Confidence,
            correlationId,
            WorkTaskSourceTypes.Agent,
            sourceAgentId,
            FinanceWorkflowQueue,
            "Laura detected a finance transaction anomaly that requires queue follow-up.",
            $"{command.TransactionId:N}:{decision.AnomalyType}:{dedupeWindow.StartUtc:yyyyMMddHH}",
            WorkTaskStatus.New);

        task.SetDueDate(DateTime.UtcNow.AddBusinessDays(2));
        _dbContext.WorkTasks.Add(task);
        return task;
    }

    private async Task<Guid?> ResolveLauraAgentIdAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.CanReceiveAssignments &&
                (x.TemplateId == "laura-finance" ||
                 x.DisplayName.Contains("Laura") ||
                 x.Department == "Finance"))
            .OrderByDescending(x => x.TemplateId == "laura-finance")
            .ThenByDescending(x => x.DisplayName.Contains("Laura"))
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static Dictionary<string, JsonNode?> BuildEvidence(
        TransactionRow transaction,
        FinancePolicyConfigurationDto policy,
        HistoricalBaseline baseline,
        AnomalyDecision decision,
        DateTime evaluatedUtc,
        DedupeWindow dedupeWindow) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["transactionId"] = JsonValue.Create(transaction.Id),
            ["transactionExternalReference"] = JsonValue.Create(transaction.ExternalReference),
            ["transactionAmount"] = JsonValue.Create(transaction.Amount),
            ["transactionCurrency"] = JsonValue.Create(transaction.Currency),
            ["transactionType"] = JsonValue.Create(transaction.TransactionType),
            ["transactionUtc"] = JsonValue.Create(transaction.TransactionUtc),
            ["accountId"] = JsonValue.Create(transaction.AccountId),
            ["accountName"] = JsonValue.Create(transaction.AccountName),
            ["counterpartyId"] = transaction.CounterpartyId.HasValue ? JsonValue.Create(transaction.CounterpartyId.Value) : null,
            ["counterpartyName"] = string.IsNullOrWhiteSpace(transaction.CounterpartyName) ? null : JsonValue.Create(transaction.CounterpartyName),
            ["policyLowerBound"] = JsonValue.Create(policy.AnomalyDetectionLowerBound),
            ["policyUpperBound"] = JsonValue.Create(policy.AnomalyDetectionUpperBound),
            ["policyCurrency"] = JsonValue.Create(policy.ApprovalCurrency),
            ["historicalBaseline"] = BuildBaselineNode(baseline),
            ["anomalyType"] = JsonValue.Create(decision.AnomalyType),
            ["confidence"] = JsonValue.Create(decision.Confidence),
            ["classification"] = JsonValue.Create(decision.AnomalyType),
            ["riskLevel"] = JsonValue.Create(decision.Severity.ToStorageValue()),
            ["rationale"] = JsonValue.Create(decision.Explanation),
            ["recommendedAction"] = JsonValue.Create(decision.RecommendedAction),
            ["sourceWorkflow"] = JsonValue.Create(SourceWorkflow),
            ["workflowOutput"] = FinanceWorkflowOutputSchemas.ToJsonObject(BuildWorkflowOutput(decision)),
            ["evaluatedUtc"] = JsonValue.Create(evaluatedUtc),
            ["deduplicationWindowStartUtc"] = JsonValue.Create(dedupeWindow.StartUtc),
            ["deduplicationWindowEndUtc"] = JsonValue.Create(dedupeWindow.EndUtc),
            ["deduplicationWindowHours"] = JsonValue.Create(dedupeWindow.Hours)
        };

    private static Dictionary<string, JsonNode?> BuildMetadata(
        EvaluateFinanceTransactionAnomalyCommand command,
        TransactionRow transaction,
        HistoricalBaseline baseline,
        AnomalyDecision decision,
        DedupeWindow dedupeWindow) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = JsonValue.Create(SourceWorkflow),
            ["assignedQueue"] = JsonValue.Create(FinanceWorkflowQueue),
            ["workflowInstanceId"] = command.WorkflowInstanceId.HasValue ? JsonValue.Create(command.WorkflowInstanceId.Value) : null,
            ["transactionDescription"] = JsonValue.Create(transaction.Description),
            ["baselineSampleSize"] = JsonValue.Create(baseline.SampleSize),
            ["anomalyType"] = JsonValue.Create(decision.AnomalyType),
            ["classification"] = JsonValue.Create(decision.AnomalyType),
            ["riskLevel"] = JsonValue.Create(decision.Severity.ToStorageValue()),
            ["recommendedAction"] = JsonValue.Create(decision.RecommendedAction),
            ["rationale"] = JsonValue.Create(decision.Explanation),
            ["confidence"] = JsonValue.Create(decision.Confidence),
            ["sourceWorkflow"] = JsonValue.Create(SourceWorkflow),
            ["dedupeKey"] = JsonValue.Create(BuildDedupeKey(command.CompanyId, command.TransactionId, decision.AnomalyType)),
            ["workflowOutput"] = FinanceWorkflowOutputSchemas.ToJsonObject(BuildWorkflowOutput(decision)),
            ["deduplicationWindowStartUtc"] = JsonValue.Create(dedupeWindow.StartUtc),
            ["deduplicationWindowEndUtc"] = JsonValue.Create(dedupeWindow.EndUtc),
            ["deduplicationWindowHours"] = JsonValue.Create(dedupeWindow.Hours)
        };

    private static Dictionary<string, JsonNode?> BuildTaskPayload(
        EvaluateFinanceTransactionAnomalyCommand command,
        TransactionRow transaction,
        FinancePolicyConfigurationDto policy,
        HistoricalBaseline baseline,
        AnomalyDecision decision,
        Guid alertId,
        DedupeWindow dedupeWindow) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["companyId"] = JsonValue.Create(command.CompanyId),
            ["transactionId"] = JsonValue.Create(transaction.Id),
            ["alertId"] = JsonValue.Create(alertId),
            ["workflowInstanceId"] = command.WorkflowInstanceId.HasValue ? JsonValue.Create(command.WorkflowInstanceId.Value) : null,
            ["anomalyType"] = JsonValue.Create(decision.AnomalyType),
            ["classification"] = JsonValue.Create(decision.AnomalyType),
            ["riskLevel"] = JsonValue.Create(decision.Severity.ToStorageValue()),
            ["explanation"] = JsonValue.Create(decision.Explanation),
            ["rationale"] = JsonValue.Create(decision.Explanation),
            ["confidence"] = JsonValue.Create(decision.Confidence),
            ["recommendedAction"] = JsonValue.Create(decision.RecommendedAction),
            ["sourceWorkflow"] = JsonValue.Create(SourceWorkflow),
            ["workflowOutput"] = FinanceWorkflowOutputSchemas.ToJsonObject(BuildWorkflowOutput(decision)),
            ["transactionAmount"] = JsonValue.Create(transaction.Amount),
            ["transactionCurrency"] = JsonValue.Create(transaction.Currency),
            ["policyLowerBound"] = JsonValue.Create(policy.AnomalyDetectionLowerBound),
            ["policyUpperBound"] = JsonValue.Create(policy.AnomalyDetectionUpperBound),
            ["historicalBaseline"] = BuildBaselineNode(baseline),
            ["assignedQueue"] = JsonValue.Create(FinanceWorkflowQueue),
            ["deduplicationWindowStartUtc"] = JsonValue.Create(dedupeWindow.StartUtc),
            ["deduplicationWindowEndUtc"] = JsonValue.Create(dedupeWindow.EndUtc),
            ["deduplicationWindowHours"] = JsonValue.Create(dedupeWindow.Hours)
        };

    private static Dictionary<string, JsonNode?> BuildThresholdEvaluationDetails(
        TransactionRow transaction,
        FinancePolicyConfigurationDto policy,
        HistoricalBaseline baseline,
        AnomalyDecision decision,
        FinanceWorkflowOutputSchemaDto workflowOutput,
        Guid alertId,
        Guid taskId,
        DedupeWindow dedupeWindow) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["transactionId"] = JsonValue.Create(transaction.Id),
            ["transactionExternalReference"] = JsonValue.Create(transaction.ExternalReference),
            ["transactionAmount"] = JsonValue.Create(transaction.Amount),
            ["transactionCurrency"] = JsonValue.Create(transaction.Currency),
            ["transactionType"] = JsonValue.Create(transaction.TransactionType),
            ["transactionUtc"] = JsonValue.Create(transaction.TransactionUtc),
            ["policyLowerBound"] = JsonValue.Create(policy.AnomalyDetectionLowerBound),
            ["policyUpperBound"] = JsonValue.Create(policy.AnomalyDetectionUpperBound),
            ["historicalBaseline"] = BuildBaselineNode(baseline),
            ["explanation"] = JsonValue.Create(decision.Explanation),
            ["recommendedAction"] = JsonValue.Create(decision.RecommendedAction),
            ["confidence"] = JsonValue.Create(decision.Confidence),
            ["riskLevel"] = JsonValue.Create(decision.Severity.ToStorageValue()),
            ["alertId"] = JsonValue.Create(alertId),
            ["followUpTaskId"] = JsonValue.Create(taskId),
            ["workflowOutput"] = FinanceWorkflowOutputSchemas.ToJsonObject(workflowOutput),
            ["deduplicationWindowStartUtc"] = JsonValue.Create(dedupeWindow.StartUtc),
            ["deduplicationWindowEndUtc"] = JsonValue.Create(dedupeWindow.EndUtc),
            ["deduplicationWindowHours"] = JsonValue.Create(dedupeWindow.Hours)
        };

    private static JsonObject BuildBaselineNode(HistoricalBaseline baseline) =>
        new()
        {
            ["sampleSize"] = JsonValue.Create(baseline.SampleSize),
            ["averageAbsoluteAmount"] = JsonValue.Create(baseline.AverageAbsoluteAmount),
            ["maximumAbsoluteAmount"] = JsonValue.Create(baseline.MaximumAbsoluteAmount),
            ["earliestTransactionUtc"] = baseline.EarliestTransactionUtc.HasValue ? JsonValue.Create(baseline.EarliestTransactionUtc.Value) : null,
            ["latestTransactionUtc"] = baseline.LatestTransactionUtc.HasValue ? JsonValue.Create(baseline.LatestTransactionUtc.Value) : null
        };

    private static FinanceWorkflowOutputSchemaDto BuildWorkflowOutput(AnomalyDecision decision) =>
        FinanceWorkflowOutputSchemas.Create(
            decision.AnomalyType,
            decision.Severity.ToStorageValue(),
            decision.RecommendedAction,
            decision.Explanation,
            decision.Confidence,
            SourceWorkflow);

    private static string BuildAlertTitle(TransactionRow transaction, AnomalyDecision decision) =>
        $"Finance anomaly: {decision.AnomalyType.Replace('_', ' ')} on {transaction.ExternalReference}";

    private DedupeWindow BuildDedupeWindow(DateTime evaluatedUtc)
    {
        var hours = Math.Max(1, _options.DeduplicationWindowHours);
        var windowTicks = TimeSpan.FromHours(hours).Ticks;
        var windowStartTicks = evaluatedUtc.Ticks - evaluatedUtc.Ticks % windowTicks;
        var windowStartUtc = new DateTime(windowStartTicks, DateTimeKind.Utc);
        return new DedupeWindow(windowStartUtc, windowStartUtc.AddHours(hours), hours);
    }

    private static string BuildDedupeKey(Guid companyId, Guid transactionId, string anomalyType) =>
        $"{CorrelationPrefix}:{companyId:N}:{transactionId:N}:{anomalyType}".ToLowerInvariant();

    private static string BuildWindowedFingerprint(string baseFingerprint, DedupeWindow dedupeWindow) =>
        $"{baseFingerprint}:window:{dedupeWindow.StartUtc:yyyyMMddHH}".ToLowerInvariant();

    private static string BuildCorrelationId(
        Guid companyId,
        Guid transactionId,
        string anomalyType,
        DedupeWindow dedupeWindow) =>
        $"fin-anom:{companyId:N}:{transactionId:N}:{anomalyType.ToLowerInvariant()}:{dedupeWindow.StartUtc:yyyyMMddHH}";

    private static void Validate(EvaluateFinanceTransactionAnomalyCommand command)
    {
        if (command.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(command));
        }

        if (command.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction id is required.", nameof(command));
        }

        if (command.AgentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id cannot be empty.", nameof(command));
        }

        if (command.WorkflowInstanceId == Guid.Empty)
        {
            throw new ArgumentException("Workflow instance id cannot be empty.", nameof(command));
        }
    }

    private sealed record TransactionRow(
        Guid Id,
        Guid CompanyId,
        Guid AccountId,
        string AccountName,
        Guid? CounterpartyId,
        string? CounterpartyName,
        Guid? InvoiceId,
        Guid? BillId,
        DateTime TransactionUtc,
        string TransactionType,
        decimal Amount,
        string Currency,
        string Description,
        string ExternalReference);

    private sealed record HistoricalTransactionRow(DateTime TransactionUtc, decimal AbsoluteAmount);

    private sealed record HistoricalBaseline(
        int SampleSize,
        decimal AverageAbsoluteAmount,
        decimal MaximumAbsoluteAmount,
        DateTime? EarliestTransactionUtc,
        DateTime? LatestTransactionUtc);

    private sealed record AnomalyDecision(
        string AnomalyType,
        string Explanation,
        decimal Confidence,
        string RecommendedAction,
        AlertSeverity Severity);

    private sealed record DedupeWindow(DateTime StartUtc, DateTime EndUtc, int Hours);

    private sealed record AlertMutation(Alert Alert, bool Created, bool Deduplicated);
}

internal static class FinanceAnomalyDateTimeExtensions
{
    public static DateTime AddBusinessDays(this DateTime value, int days)
    {
        var date = value;
        var remaining = Math.Max(0, days);
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                remaining--;
            }
        }

        return date;
    }
}
