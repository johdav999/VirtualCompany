using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchAgentToolIds
{
    public const string ReadBriefing = "finance.migration.read_briefing";
    public const string ReadStatus = "finance.migration.read_status";
    public const string ReadCapabilities = "finance.migration.read_capabilities";
    public const string ReadInventory = "finance.migration.read_inventory";
    public const string ReadGaps = "finance.migration.read_gaps";
    public const string ReadMappings = "finance.migration.read_mappings";
    public const string ReadRehearsal = "finance.migration.read_rehearsal";
    public const string ReadReconciliation = "finance.migration.read_reconciliation";
    public const string ReadApprovals = "finance.migration.read_approvals";
    public const string ReadTransferProgress = "finance.migration.read_transfer_progress";
    public const string ReadMonitoring = "finance.migration.read_monitoring";
    public const string ReadAuditEvidence = "finance.migration.read_audit_evidence";

    public const string RecommendEffectivePeriod = "finance.migration.recommend_effective_period";
    public const string RecommendStrategy = "finance.migration.recommend_strategy";
    public const string RecommendMapping = "finance.migration.recommend_mapping";
    public const string RecommendGapResolution = "finance.migration.recommend_gap_resolution";
    public const string RecommendRequiredEvidence = "finance.migration.recommend_required_evidence";
    public const string RecommendCutoverPlan = "finance.migration.recommend_cutover_plan";
    public const string RecommendFreezeWindow = "finance.migration.recommend_freeze_window";
    public const string RecommendMonitoringPeriod = "finance.migration.recommend_monitoring_period";
    public const string ExplainReadiness = "finance.migration.explain_readiness";

    public const string StartAssessment = "finance.migration.start_assessment";
    public const string StartRehearsal = "finance.migration.start_rehearsal";
    public const string StartPreparation = "finance.migration.start_preparation";
    public const string ApplyApprovedMapping = "finance.migration.apply_approved_mapping";
    public const string CreateFollowUpTask = "finance.migration.create_follow_up_task";
    public const string RequestPlanApproval = "finance.migration.request_plan_approval";
    public const string StartApprovedFreeze = "finance.migration.start_approved_freeze";
    public const string RequestActivationApproval = "finance.migration.request_activation_approval";
    public const string ResumeRecovery = "finance.migration.resume_recovery";

    public static IReadOnlyList<string> ReadTools { get; } =
    [
        ReadBriefing, ReadStatus, ReadCapabilities, ReadInventory, ReadGaps, ReadMappings,
        ReadRehearsal, ReadReconciliation, ReadApprovals, ReadTransferProgress, ReadMonitoring,
        ReadAuditEvidence
    ];

    public static IReadOnlyList<string> RecommendationTools { get; } =
    [
        RecommendEffectivePeriod, RecommendStrategy, RecommendMapping, RecommendGapResolution,
        RecommendRequiredEvidence, RecommendCutoverPlan, RecommendFreezeWindow,
        RecommendMonitoringPeriod, ExplainReadiness
    ];

    public static IReadOnlyList<string> ExecuteTools { get; } =
    [
        StartAssessment, StartRehearsal, StartPreparation, ApplyApprovedMapping,
        CreateFollowUpTask, RequestPlanApproval, StartApprovedFreeze,
        RequestActivationApproval, ResumeRecovery
    ];

    public static IReadOnlyList<string> All { get; } =
        ReadTools.Concat(RecommendationTools).Concat(ExecuteTools).ToArray();
}

public static class AccountingProviderSwitchAgentEvidenceViews
{
    public const string Status = "status";
    public const string Capabilities = "capabilities";
    public const string Inventory = "inventory";
    public const string Gaps = "gaps";
    public const string Mappings = "mappings";
    public const string Rehearsal = "rehearsal";
    public const string Reconciliation = "reconciliation";
    public const string Approvals = "approvals";
    public const string TransferProgress = "transfer_progress";
    public const string Monitoring = "monitoring";
    public const string Audit = "audit";
}

public sealed record GetAccountingProviderSwitchAgentBriefingQuery(
    Guid CompanyId,
    Guid SwitchId,
    int MaxItems = 20);

public sealed record GetAccountingProviderSwitchAgentEvidenceQuery(
    Guid CompanyId,
    Guid SwitchId,
    string View,
    int MaxItems = 20);

public sealed record RecommendAccountingProviderSwitchActionQuery(
    Guid CompanyId,
    Guid? SwitchId,
    string RecommendationType,
    string? SourceKind = null,
    string? SourceProviderKey = null,
    string? TargetKind = null,
    string? TargetProviderKey = null,
    string? RequestedStrategy = null,
    int MaxItems = 20);

public sealed record AccountingProviderSwitchAgentBriefingDto(
    Guid SwitchId,
    long SwitchVersion,
    string CurrentStep,
    string WhyItMatters,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> AllowedActions,
    string ResponsibleParty,
    string NextCheckpoint,
    IReadOnlyList<string> DataSources,
    DateTime GeneratedUtc);

public sealed record AccountingProviderSwitchAgentEvidenceDto(
    Guid SwitchId,
    long SwitchVersion,
    string View,
    string Summary,
    IReadOnlyList<AccountingProviderSwitchAgentEvidenceItemDto> Items,
    IReadOnlyList<string> DataSources,
    DateTime AsOfUtc);

public sealed record AccountingProviderSwitchAgentEvidenceItemDto(
    string Label,
    string Status,
    string Explanation,
    string? Reference = null,
    bool NeedsAttention = false);

public sealed record AccountingProviderSwitchAgentRecommendationDto(
    Guid? SwitchId,
    long? SwitchVersion,
    string RecommendationType,
    string Recommendation,
    string Rationale,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> DataSources,
    decimal Confidence,
    DateTime GeneratedUtc);

public sealed record AccountingProviderSwitchAgentCommandContext(
    Guid CompanyId,
    Guid SwitchId,
    long ExpectedSwitchVersion,
    Guid ActorUserId,
    Guid AgentId,
    string CorrelationId,
    string IdempotencyKey);

public sealed record AccountingProviderSwitchAgentCommandResultDto(
    Guid SwitchId,
    long SwitchVersion,
    string Operation,
    string Status,
    string Summary,
    string NextCheckpoint,
    IReadOnlyList<string> DataSources,
    JsonObject FinanceResult);

public interface IAccountingProviderSwitchAgentService
{
    Task<AccountingProviderSwitchAgentBriefingDto> GetBriefingAsync(
        GetAccountingProviderSwitchAgentBriefingQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentEvidenceDto> GetEvidenceAsync(
        GetAccountingProviderSwitchAgentEvidenceQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentRecommendationDto> RecommendAsync(
        RecommendAccountingProviderSwitchActionQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> StartAssessmentAsync(
        AccountingProviderSwitchAgentCommandContext context, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> StartRehearsalAsync(
        AccountingProviderSwitchAgentCommandContext context, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> StartPreparationAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid planId, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> ApplyApprovedMappingAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid stagedRecordId, Guid mappingDecisionId,
        long expectedRecordVersion, string disposition, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> RequestPlanApprovalAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid planId, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> StartApprovedFreezeAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId,
        long expectedExecutionVersion, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> RequestActivationApprovalAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId,
        long expectedExecutionVersion, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAgentCommandResultDto> ResumeRecoveryAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId,
        long expectedExecutionVersion, CancellationToken cancellationToken);
}
