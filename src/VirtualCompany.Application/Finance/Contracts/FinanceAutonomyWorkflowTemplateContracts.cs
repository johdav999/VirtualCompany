using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public static class FinanceAutonomyWorkflowTemplateVersions
{
    public const string V1 = "2026-09-01.prompt7.v1";
}

public static class FinanceAutonomyWorkflowTemplateCodes
{
    public const string StaleCashEvidence = "stale_cash_evidence_monitoring";
    public const string UncategorizedTransactions = "uncategorized_transaction_review";
    public const string OverdueReceivables = "overdue_receivables_plan_refresh";
    public const string DuePayables = "due_payables_cash_reserve_review";
    public const string CloseBlockers = "close_blocker_refresh";
    public const string ReconciliationExceptions = "reconciliation_import_exception_review";
    public const string ExpiringComplianceEvidence = "expiring_compliance_evidence_reminder";
    public const string FailedBackgroundWork = "failed_background_finance_work_escalation";
}

public static class FinanceAutonomyWorkflowOutcomeStates
{
    public const string Healthy = "healthy";
    public const string Exception = "exception";
    public const string Stale = "stale";
    public const string Missing = "missing";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    { Healthy, Exception, Stale, Missing };
}

public sealed record FinanceAutonomyLocalizedText(string En, string Sv);

public sealed record FinanceAutonomyWorkflowLimits(
    int MaximumRecordsPerRun,
    int MaximumActionsPerRun,
    int MaximumRunsPerWindow,
    int MinimumIntervalMinutes,
    int DebounceMinutes,
    int EvidenceFreshnessMinutes,
    int ReviewWindowMinutes);

public sealed record FinanceAutonomyWorkflowTemplate(
    string Code,
    string Version,
    FinanceAutonomyLocalizedText Name,
    FinanceAutonomyLocalizedText Description,
    string CapabilityId,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> EventTypes,
    string? DefaultScheduleExpression,
    IReadOnlyList<string> Evidence,
    string ActionClass,
    string ToolName,
    IReadOnlyDictionary<string, JsonNode?> RequestPayload,
    FinanceAutonomyWorkflowLimits Limits,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> StopConditions,
    IReadOnlyList<string> ExpectedTasksOrDrafts,
    string OwnerRole,
    string ApprovalBehavior,
    IReadOnlyList<string> UnsupportedEffects,
    FinanceAutonomyLocalizedText TaskTitle,
    FinanceAutonomyLocalizedText NextHumanAction);

public sealed record PreviewFinanceAutonomyWorkflowTemplateCommand(
    string TemplateCode,
    Guid AgentId,
    string Timezone = "Europe/Stockholm",
    IReadOnlyList<string>? RequestedEffects = null);

public sealed record CreateFinanceAutonomyWorkflowTemplateDraftCommand(
    string TemplateCode,
    Guid AgentId,
    string Timezone = "Europe/Stockholm",
    IReadOnlyList<string>? RequestedEffects = null,
    string? Rationale = null);

public sealed record FinanceAutonomyWorkflowActivationPreview(
    FinanceAutonomyWorkflowTemplate Template,
    bool IsReady,
    IReadOnlyList<string> BlockingReasons,
    FinanceAutonomyGrantDefinition ProspectiveGrant,
    bool IsActivated,
    bool IncludesElevatedAuthority,
    string AuthorityNotice);

public sealed record FinanceAutonomyWorkflowDraftResult(
    FinanceAutonomyWorkflowActivationPreview Preview,
    FinanceAutonomyGrantDto Grant,
    Guid ProspectiveVersionId,
    bool CreatedNewGrant,
    bool ReusedProspectiveVersion = false);

public sealed record MaterializeFinanceAutonomyWorkflowOutcomeCommand(
    Guid RunId,
    Guid StepId,
    string TemplateCode,
    string Outcome,
    string SafeSummary);

public sealed record FinanceAutonomyWorkflowOutcomeResult(
    string Outcome,
    Guid? TaskId,
    bool Created,
    bool Duplicate,
    bool Resolved,
    bool Reopened,
    int SupersededCount,
    string DedupeKey);

public interface IFinanceAutonomyWorkflowTemplateService
{
    Task<IReadOnlyList<FinanceAutonomyWorkflowTemplate>> ListAsync(
        Guid companyId, string? locale, CancellationToken cancellationToken);
    Task<FinanceAutonomyWorkflowActivationPreview> PreviewAsync(
        Guid companyId, PreviewFinanceAutonomyWorkflowTemplateCommand command,
        CancellationToken cancellationToken);
    Task<FinanceAutonomyWorkflowDraftResult> CreateDraftAsync(
        Guid companyId, CreateFinanceAutonomyWorkflowTemplateDraftCommand command,
        CancellationToken cancellationToken);
}

public interface IFinanceAutonomyWorkflowOutcomeService
{
    Task<FinanceAutonomyWorkflowOutcomeResult> MaterializeAsync(
        Guid companyId, MaterializeFinanceAutonomyWorkflowOutcomeCommand command,
        CancellationToken cancellationToken);
}

public sealed class FinanceAutonomyWorkflowTemplateValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Finance autonomy workflow template validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
