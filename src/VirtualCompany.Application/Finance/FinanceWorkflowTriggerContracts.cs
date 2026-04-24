using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public sealed record ProcessFinanceWorkflowTriggerCommand(
    Guid CompanyId,
    string TriggerType,
    string SourceEntityType,
    string SourceEntityId,
    string SourceEntityVersion,
    DateTime OccurredAtUtc,
    string? CorrelationId = null,
    string? EventId = null,
    string? CausationId = null,
    string? TriggerMessageId = null,
    IReadOnlyDictionary<string, JsonNode?>? Metadata = null);

public sealed record FinanceWorkflowTriggerExecutionDto(
    Guid Id,
    Guid CompanyId,
    string TriggerType,
    string SourceEntityType,
    string SourceEntityId,
    string SourceEntityVersion,
    IReadOnlyList<string> ExecutedChecks,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string Outcome,
    string? ErrorDetails);

public interface IFinanceWorkflowTriggerService
{
    Task<FinanceWorkflowTriggerExecutionDto> ProcessAsync(
        ProcessFinanceWorkflowTriggerCommand command,
        CancellationToken cancellationToken);
}

public interface IFinanceWorkflowTriggerRegistry
{
    IReadOnlyList<string> GetChecks(string triggerType);
    IReadOnlyList<FinanceWorkflowTriggerCheckRegistration> ListRegistrations();
    Task<FinanceWorkflowTriggerExecutionDto> ProcessAsync(
        ProcessFinanceWorkflowTriggerCommand command,
        CancellationToken cancellationToken);
}

public static class FinanceWorkflowTriggerTypes
{
    public const string Invoice = "invoice";
    public const string Bill = "bill";
    public const string Payment = "payment";
    public const string Cash = "cash";
    public const string SimulationDayAdvanced = "simulation_day_advanced";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Trigger type is required.", nameof(value))
            : value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
}

public static class FinanceWorkflowExecutedChecks
{
    public const string RefreshInsightsSnapshot = "refresh_insights_snapshot";
    public const string EvaluateCashPosition = "evaluate_cash_position";
    public const string EnsureApprovalTask = "ensure_approval_task";
}

public static class FinanceWorkflowTriggerOutcomes
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string NoOp = "no_op";
    public const string DuplicateSkipped = "duplicate_skipped";
}

public sealed record FinanceWorkflowTriggerCheckRegistration(
    string TriggerType,
    IReadOnlyList<string> CheckCodes);