using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed record CreateAdvancedReconciliationGroupRequest(
    string Reference,
    string Counterparty,
    string Currency,
    int? RuleVersion,
    Guid? CorrectionOfGroupId,
    IReadOnlyList<AdvancedReconciliationNodeInputDto> Nodes,
    IReadOnlyList<AdvancedReconciliationEdgeInputDto> Edges);

public sealed record AcceptAdvancedReconciliationGroupRequest(long ExpectedVersion, int ExpectedRuleVersion, string DecisionReason);
public sealed record RejectAdvancedReconciliationGroupRequest(long ExpectedVersion, string DecisionReason);
public sealed record ReverseAdvancedReconciliationGroupRequest(long ExpectedVersion, Guid FiscalPeriodId, DateOnly PostingDate, string Reason);

public sealed record CreateAdvancedReconciliationRuleRequest(
    string Name,
    string ReferenceNormalizationPattern,
    string CounterpartyNormalizationPattern,
    string ProviderPattern,
    decimal AmountTolerance,
    int TimingWindowDays,
    decimal RecommendationThreshold,
    decimal LowConfidenceThreshold,
    decimal MaterialityThreshold);

