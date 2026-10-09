namespace VirtualCompany.Application.Finance;
public sealed record AdvancedReconciliationGroupDetailDto(AdvancedReconciliationGroupSummaryDto Summary, decimal AllocatedAmount, decimal FeeAmount, decimal RoundingAmount, decimal ResidualAmount, decimal Variance, bool IsBalanced, string? BlockingReason, IReadOnlyList<AdvancedReconciliationNodeDto> Nodes, IReadOnlyList<AdvancedReconciliationEdgeDto> Edges, IReadOnlyList<AdvancedReconciliationReasonContributionDto> ReasonContributions, IReadOnlyList<AdvancedReconciliationResultDto> Results, IReadOnlyList<AdvancedReconciliationEventDto> History)
{
    public AdvancedReconciliationGroupSummaryDto Summary { get; set; } = Summary;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public decimal FeeAmount { get; set; } = FeeAmount;
    public decimal RoundingAmount { get; set; } = RoundingAmount;
    public decimal ResidualAmount { get; set; } = ResidualAmount;
    public decimal Variance { get; set; } = Variance;
    public bool IsBalanced { get; set; } = IsBalanced;
    public string? BlockingReason { get; set; } = BlockingReason;
    public IReadOnlyList<AdvancedReconciliationNodeDto> Nodes { get; set; } = Nodes;
    public IReadOnlyList<AdvancedReconciliationEdgeDto> Edges { get; set; } = Edges;
    public IReadOnlyList<AdvancedReconciliationReasonContributionDto> ReasonContributions { get; set; } = ReasonContributions;
    public IReadOnlyList<AdvancedReconciliationResultDto> Results { get; set; } = Results;
    public IReadOnlyList<AdvancedReconciliationEventDto> History { get; set; } = History;

    public AdvancedReconciliationGroupDetailDto() : this(new(), default !, default !, default !, default !, default !, default !, default !, [], [], [], [], [])
    {
    }
}

public sealed record AdvancedReconciliationNodeDto(Guid Id, string NodeType, Guid? RecordId, string Label, string Reference, string Currency, decimal Amount, string? Direction, string? AdjustmentKind, decimal DebitAmount, decimal CreditAmount, string? ExpectedRecordVersion, int Sequence)
{
    public Guid Id { get; set; } = Id;
    public string NodeType { get; set; } = NodeType;
    public Guid? RecordId { get; set; } = RecordId;
    public string Label { get; set; } = Label;
    public string Reference { get; set; } = Reference;
    public string Currency { get; set; } = Currency;
    public decimal Amount { get; set; } = Amount;
    public string? Direction { get; set; } = Direction;
    public string? AdjustmentKind { get; set; } = AdjustmentKind;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string? ExpectedRecordVersion { get; set; } = ExpectedRecordVersion;
    public int Sequence { get; set; } = Sequence;

    public AdvancedReconciliationNodeDto() : this(default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AdvancedReconciliationResultDto(Guid Id, Guid? ParentResultId, string Outcome, long GroupVersion, int RuleVersion, decimal ExpectedBankTotal, decimal AllocatedAmount, decimal FeeAmount, decimal RoundingAmount, decimal ResidualAmount, IReadOnlyList<Guid> LedgerEntryIds, Guid CreatedByUserId, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid? ParentResultId { get; set; } = ParentResultId;
    public string Outcome { get; set; } = Outcome;
    public long GroupVersion { get; set; } = GroupVersion;
    public int RuleVersion { get; set; } = RuleVersion;
    public decimal ExpectedBankTotal { get; set; } = ExpectedBankTotal;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public decimal FeeAmount { get; set; } = FeeAmount;
    public decimal RoundingAmount { get; set; } = RoundingAmount;
    public decimal ResidualAmount { get; set; } = ResidualAmount;
    public IReadOnlyList<Guid> LedgerEntryIds { get; set; } = LedgerEntryIds;
    public Guid CreatedByUserId { get; set; } = CreatedByUserId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public AdvancedReconciliationResultDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, [], default !, default !)
    {
    }
}

public sealed record AdvancedReconciliationQualityMetricsDto(int NeedsReviewCount, int LowConfidenceCount, int ConflictCount, int StaleCount, decimal AverageConfidence, decimal AcceptedValue)
{
    public int NeedsReviewCount { get; set; } = NeedsReviewCount;
    public int LowConfidenceCount { get; set; } = LowConfidenceCount;
    public int ConflictCount { get; set; } = ConflictCount;
    public int StaleCount { get; set; } = StaleCount;
    public decimal AverageConfidence { get; set; } = AverageConfidence;
    public decimal AcceptedValue { get; set; } = AcceptedValue;

    public AdvancedReconciliationQualityMetricsDto() : this(default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AdvancedReconciliationGroupSummaryDto(Guid Id, string Reference, string Counterparty, string Currency, decimal ExpectedBankTotal, decimal ConfidenceScore, string Status, string Cardinality, int BankRowCount, int PaymentCount, int DocumentCount, int RuleVersion, long Version, bool RequiresApproval, bool IsStale, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Reference { get; set; } = Reference;
    public string Counterparty { get; set; } = Counterparty;
    public string Currency { get; set; } = Currency;
    public decimal ExpectedBankTotal { get; set; } = ExpectedBankTotal;
    public decimal ConfidenceScore { get; set; } = ConfidenceScore;
    public string Status { get; set; } = Status;
    public string Cardinality { get; set; } = Cardinality;
    public int BankRowCount { get; set; } = BankRowCount;
    public int PaymentCount { get; set; } = PaymentCount;
    public int DocumentCount { get; set; } = DocumentCount;
    public int RuleVersion { get; set; } = RuleVersion;
    public long Version { get; set; } = Version;
    public bool RequiresApproval { get; set; } = RequiresApproval;
    public bool IsStale { get; set; } = IsStale;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public AdvancedReconciliationGroupSummaryDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AdvancedReconciliationReasonContributionDto(string FeatureKey, decimal Contribution, string Explanation, string Evidence)
{
    public string FeatureKey { get; set; } = FeatureKey;
    public decimal Contribution { get; set; } = Contribution;
    public string Explanation { get; set; } = Explanation;
    public string Evidence { get; set; } = Evidence;

    public AdvancedReconciliationReasonContributionDto() : this(string.Empty, default !, string.Empty, string.Empty)
    {
    }
}

public sealed record AdvancedReconciliationRuleDto(Guid Id, int Version, string Name, string ReferenceNormalizationPattern, string CounterpartyNormalizationPattern, string ProviderPattern, decimal AmountTolerance, int TimingWindowDays, decimal RecommendationThreshold, decimal LowConfidenceThreshold, decimal MaterialityThreshold, DateTime CreatedUtc, DateTime? SupersededUtc)
{
    public Guid Id { get; set; } = Id;
    public int Version { get; set; } = Version;
    public string Name { get; set; } = Name;
    public string ReferenceNormalizationPattern { get; set; } = ReferenceNormalizationPattern;
    public string CounterpartyNormalizationPattern { get; set; } = CounterpartyNormalizationPattern;
    public string ProviderPattern { get; set; } = ProviderPattern;
    public decimal AmountTolerance { get; set; } = AmountTolerance;
    public int TimingWindowDays { get; set; } = TimingWindowDays;
    public decimal RecommendationThreshold { get; set; } = RecommendationThreshold;
    public decimal LowConfidenceThreshold { get; set; } = LowConfidenceThreshold;
    public decimal MaterialityThreshold { get; set; } = MaterialityThreshold;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? SupersededUtc { get; set; } = SupersededUtc;

    public AdvancedReconciliationRuleDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AdvancedReconciliationEventDto(Guid Id, string Action, Guid ActorUserId, string BeforeJson, string AfterJson, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public string BeforeJson { get; set; } = BeforeJson;
    public string AfterJson { get; set; } = AfterJson;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public AdvancedReconciliationEventDto() : this(default !, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record AdvancedReconciliationWorkspaceDto(IReadOnlyList<AdvancedReconciliationGroupSummaryDto> Groups, AdvancedReconciliationQualityMetricsDto Metrics, AdvancedReconciliationRuleDto? CurrentRule)
{
    public IReadOnlyList<AdvancedReconciliationGroupSummaryDto> Groups { get; set; } = Groups;
    public AdvancedReconciliationQualityMetricsDto Metrics { get; set; } = Metrics;
    public AdvancedReconciliationRuleDto? CurrentRule { get; set; } = CurrentRule;

    public AdvancedReconciliationWorkspaceDto() : this([], new(), default !)
    {
    }
}

public sealed record AdvancedReconciliationEdgeDto(Guid Id, Guid SourceNodeId, Guid TargetNodeId, string EdgeType, decimal Amount)
{
    public Guid Id { get; set; } = Id;
    public Guid SourceNodeId { get; set; } = SourceNodeId;
    public Guid TargetNodeId { get; set; } = TargetNodeId;
    public string EdgeType { get; set; } = EdgeType;
    public decimal Amount { get; set; } = Amount;

    public AdvancedReconciliationEdgeDto() : this(default !, default !, default !, string.Empty, default !)
    {
    }
}
