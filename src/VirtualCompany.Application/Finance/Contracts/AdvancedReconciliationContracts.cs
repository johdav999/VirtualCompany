namespace VirtualCompany.Application.Finance;

public static class AdvancedReconciliationReasonCodes
{
    public const string GroupVersionConflict = "advanced_reconciliation_group_version_conflict";
    public const string RuleVersionConflict = "advanced_reconciliation_rule_version_conflict";
    public const string RecordVersionConflict = "advanced_reconciliation_record_version_conflict";
    public const string UnbalancedGroup = "advanced_reconciliation_group_unbalanced";
    public const string ApprovalRequired = "advanced_reconciliation_approval_required";
    public const string UnsupportedGraph = "advanced_reconciliation_graph_unsupported";
    public const string IdempotencyConflict = "advanced_reconciliation_idempotency_conflict";
}

public sealed record AdvancedReconciliationNodeInputDto(
    Guid NodeId,
    string NodeType,
    Guid? RecordId,
    decimal Amount = 0m,
    string? AdjustmentKind = null,
    decimal DebitAmount = 0m,
    decimal CreditAmount = 0m,
    string? Label = null,
    string? Reference = null,
    int Sequence = 0);

public sealed record AdvancedReconciliationEdgeInputDto(
    Guid EdgeId,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string EdgeType,
    decimal Amount);

public sealed record CreateAdvancedReconciliationGroupCommand(
    Guid CompanyId,
    string Reference,
    string Counterparty,
    string Currency,
    int? RuleVersion,
    Guid? CorrectionOfGroupId,
    IReadOnlyList<AdvancedReconciliationNodeInputDto> Nodes,
    IReadOnlyList<AdvancedReconciliationEdgeInputDto> Edges,
    Guid ActorUserId,
    string? CorrelationId = null,
    string? IdempotencyKey = null);

public sealed record AcceptAdvancedReconciliationGroupCommand(
    Guid CompanyId,
    Guid GroupId,
    long ExpectedVersion,
    int ExpectedRuleVersion,
    string DecisionReason,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record RejectAdvancedReconciliationGroupCommand(
    Guid CompanyId,
    Guid GroupId,
    long ExpectedVersion,
    string DecisionReason,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record ReverseAdvancedReconciliationGroupCommand(
    Guid CompanyId,
    Guid GroupId,
    long ExpectedVersion,
    Guid FiscalPeriodId,
    DateOnly PostingDate,
    string Reason,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record CreateAdvancedReconciliationRuleCommand(
    Guid CompanyId,
    string Name,
    string ReferenceNormalizationPattern,
    string CounterpartyNormalizationPattern,
    string ProviderPattern,
    decimal AmountTolerance,
    int TimingWindowDays,
    decimal RecommendationThreshold,
    decimal LowConfidenceThreshold,
    decimal MaterialityThreshold,
    Guid ActorUserId);

public sealed record ListAdvancedReconciliationGroupsQuery(
    Guid CompanyId,
    string? Status = null,
    string? Search = null,
    decimal? MaximumConfidence = null,
    int Limit = 100);

public sealed record GetAdvancedReconciliationGroupQuery(Guid CompanyId, Guid GroupId);

public interface IAdvancedReconciliationReadService
{
    Task<AdvancedReconciliationWorkspaceDto> ListAsync(ListAdvancedReconciliationGroupsQuery query, CancellationToken cancellationToken);
    Task<AdvancedReconciliationGroupDetailDto?> GetAsync(GetAdvancedReconciliationGroupQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdvancedReconciliationRuleDto>> ListRulesAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface IAdvancedReconciliationCommandService
{
    Task<AdvancedReconciliationRuleDto> CreateRuleVersionAsync(CreateAdvancedReconciliationRuleCommand command, CancellationToken cancellationToken);
    Task<AdvancedReconciliationGroupDetailDto> CreateGroupAsync(CreateAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken);
    Task<AdvancedReconciliationGroupDetailDto> AcceptAsync(AcceptAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken);
    Task<AdvancedReconciliationGroupDetailDto> RejectAsync(RejectAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken);
    Task<AdvancedReconciliationGroupDetailDto> ReverseAsync(ReverseAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken);
}
