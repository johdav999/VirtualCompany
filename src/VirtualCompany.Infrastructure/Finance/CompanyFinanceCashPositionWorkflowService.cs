using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceCashPositionWorkflowService : IFinanceCashPositionWorkflowService
{
    private const string SourceWorkflow = "cash_position_monitoring";
    private const string CorrelationPrefix = "finance-cash-position";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceReadService _financeReadService;

    public CompanyFinanceCashPositionWorkflowService(
        VirtualCompanyDbContext dbContext,
        IFinanceReadService financeReadService)
    {
        _dbContext = dbContext;
        _financeReadService = financeReadService;
    }

    public async Task<FinanceCashPositionDto> EvaluateAsync(
        EvaluateFinanceCashPositionWorkflowCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var position = await _financeReadService.GetCashPositionAsync(
            new GetFinanceCashPositionQuery(command.CompanyId),
            cancellationToken);
        if (!position.AlertState.IsLowCash)
        {
            return position;
        }

        var sourceAgentId = command.AgentId ?? await ResolveLauraAgentIdAsync(command.CompanyId, cancellationToken);
        var severity = ParseSeverity(position.RiskLevel);
        var fingerprint = BuildFingerprint(command.CompanyId);
        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? $"{CorrelationPrefix}:{command.CompanyId:N}"
            : command.CorrelationId.Trim();
        var evidence = BuildEvidence(command, position);
        var metadata = BuildMetadata(command, position);
        var title = position.EstimatedRunwayDays.HasValue
            ? $"Low cash runway: {position.EstimatedRunwayDays.Value} days"
            : "Low cash position";

        var existing = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == command.CompanyId &&
                x.Fingerprint == fingerprint &&
                (x.Status == AlertStatus.Open || x.Status == AlertStatus.Acknowledged))
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        Alert alert;
        var created = false;
        var deduplicated = false;
        if (existing is null)
        {
            alert = new Alert(
                Guid.NewGuid(),
                command.CompanyId,
                AlertType.Risk,
                severity,
                title,
                position.Rationale,
                evidence,
                correlationId,
                fingerprint,
                AlertStatus.Open,
                sourceAgentId,
                metadata);
            _dbContext.Alerts.Add(alert);
            created = true;
        }
        else
        {
            alert = existing;
            alert.RefreshFromDuplicateDetection(
                severity,
                title,
                position.Rationale,
                evidence,
                correlationId,
                sourceAgentId,
                metadata);
            deduplicated = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var alertState = position.AlertState with
        {
            AlertCreated = created,
            AlertDeduplicated = deduplicated,
            AlertId = alert.Id,
            AlertStatus = alert.Status.ToStorageValue()
        };

        return position with { AlertState = alertState };
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
        EvaluateFinanceCashPositionWorkflowCommand command,
        FinanceCashPositionDto position) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["companyId"] = JsonValue.Create(command.CompanyId),
            ["correlationId"] = JsonValue.Create(command.CorrelationId),
            ["triggerEventId"] = JsonValue.Create(command.TriggerEventId),
            ["sourceEntityId"] = JsonValue.Create(command.SourceEntityId),
            ["sourceEntityVersion"] = JsonValue.Create(command.SourceEntityVersion),
            ["workflowInstanceId"] = command.WorkflowInstanceId.HasValue ? JsonValue.Create(command.WorkflowInstanceId.Value) : null,
            ["availableBalance"] = JsonValue.Create(position.AvailableBalance),
            ["currency"] = JsonValue.Create(position.Currency),
            ["averageMonthlyBurn"] = JsonValue.Create(position.AverageMonthlyBurn),
            ["estimatedRunwayDays"] = position.EstimatedRunwayDays.HasValue ? JsonValue.Create(position.EstimatedRunwayDays.Value) : null,
            ["warningRunwayDays"] = JsonValue.Create(position.Thresholds.WarningRunwayDays),
            ["criticalRunwayDays"] = JsonValue.Create(position.Thresholds.CriticalRunwayDays),
            ["warningCashAmount"] = position.Thresholds.WarningCashAmount.HasValue ? JsonValue.Create(position.Thresholds.WarningCashAmount.Value) : null,
            ["criticalCashAmount"] = position.Thresholds.CriticalCashAmount.HasValue ? JsonValue.Create(position.Thresholds.CriticalCashAmount.Value) : null,
            ["classification"] = JsonValue.Create(position.Classification),
            ["riskLevel"] = JsonValue.Create(position.RiskLevel),
            ["recommendedAction"] = JsonValue.Create(position.RecommendedAction),
            ["rationale"] = JsonValue.Create(position.Rationale),
            ["confidence"] = JsonValue.Create(position.Confidence),
            ["sourceWorkflow"] = JsonValue.Create(position.SourceWorkflow),
            ["workflowOutput"] = FinanceWorkflowOutputSchemas.ToJsonObject(position.WorkflowOutput)
        };

    private static Dictionary<string, JsonNode?> BuildMetadata(
        EvaluateFinanceCashPositionWorkflowCommand command,
        FinanceCashPositionDto position) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = JsonValue.Create(SourceWorkflow),
            ["correlationId"] = JsonValue.Create(command.CorrelationId),
            ["triggerEventId"] = JsonValue.Create(command.TriggerEventId),
            ["sourceEntityId"] = JsonValue.Create(command.SourceEntityId),
            ["sourceEntityVersion"] = JsonValue.Create(command.SourceEntityVersion),
            ["workflowInstanceId"] = command.WorkflowInstanceId.HasValue ? JsonValue.Create(command.WorkflowInstanceId.Value) : null,
            ["classification"] = JsonValue.Create(position.Classification),
            ["riskLevel"] = JsonValue.Create(position.RiskLevel),
            ["recommendedAction"] = JsonValue.Create(position.RecommendedAction),
            ["rationale"] = JsonValue.Create(position.Rationale),
            ["confidence"] = JsonValue.Create(position.Confidence),
            ["workflowOutput"] = FinanceWorkflowOutputSchemas.ToJsonObject(position.WorkflowOutput)
        };

    private static AlertSeverity ParseSeverity(string riskLevel) =>
        riskLevel.Trim().ToLowerInvariant() switch
        {
            "critical" => AlertSeverity.Critical,
            "high" => AlertSeverity.High,
            "medium" => AlertSeverity.Medium,
            _ => AlertSeverity.Low
        };

    private static string BuildFingerprint(Guid companyId) =>
        $"{CorrelationPrefix}:{companyId:N}:low-cash".ToLowerInvariant();

    private static void Validate(EvaluateFinanceCashPositionWorkflowCommand command)
    {
        if (command.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(command));
        }

        if (command.AgentId.HasValue && command.AgentId.Value == Guid.Empty)
        {
            throw new ArgumentException("Agent id cannot be empty.", nameof(command));
        }

        if (command.WorkflowInstanceId.HasValue && command.WorkflowInstanceId.Value == Guid.Empty)
        {
            throw new ArgumentException("Workflow instance id cannot be empty.", nameof(command));
        }
    }
}
