using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class AdvancedReconciliationRule : ICompanyOwnedEntity
{
    private AdvancedReconciliationRule() { }

    public AdvancedReconciliationRule(Guid id, Guid companyId, int version, string name,
        string referenceNormalizationPattern, string counterpartyNormalizationPattern, string providerPattern,
        decimal amountTolerance, int timingWindowDays, decimal recommendationThreshold,
        decimal lowConfidenceThreshold, decimal materialityThreshold, Guid createdByUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Require(companyId, nameof(companyId));
        Version = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version));
        Name = Text(name, nameof(name), 160);
        ReferenceNormalizationPattern = Text(referenceNormalizationPattern, nameof(referenceNormalizationPattern), 500);
        CounterpartyNormalizationPattern = Text(counterpartyNormalizationPattern, nameof(counterpartyNormalizationPattern), 500);
        ProviderPattern = Text(providerPattern, nameof(providerPattern), 500);
        AmountTolerance = MoneyNonNegative(amountTolerance, nameof(amountTolerance));
        TimingWindowDays = timingWindowDays is >= 0 and <= 366 ? timingWindowDays : throw new ArgumentOutOfRangeException(nameof(timingWindowDays));
        RecommendationThreshold = Score(recommendationThreshold, nameof(recommendationThreshold));
        LowConfidenceThreshold = Score(lowConfidenceThreshold, nameof(lowConfidenceThreshold));
        MaterialityThreshold = MoneyNonNegative(materialityThreshold, nameof(materialityThreshold));
        CreatedByUserId = Require(createdByUserId, nameof(createdByUserId));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; } = null!;
    public string ReferenceNormalizationPattern { get; private set; } = null!;
    public string CounterpartyNormalizationPattern { get; private set; } = null!;
    public string ProviderPattern { get; private set; } = null!;
    public decimal AmountTolerance { get; private set; }
    public int TimingWindowDays { get; private set; }
    public decimal RecommendationThreshold { get; private set; }
    public decimal LowConfidenceThreshold { get; private set; }
    public decimal MaterialityThreshold { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? SupersededUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Supersede(DateTime supersededUtc) => SupersededUtc ??= EntityTimestampNormalizer.NormalizeUtc(supersededUtc, nameof(supersededUtc));

    private static Guid Require(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static string Text(string value, string name, int maxLength) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static decimal MoneyNonNegative(decimal value, string name) => value < 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal Score(decimal value, string name) => value is < 0m or > 1m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}

public sealed class AdvancedReconciliationGroup : ICompanyOwnedEntity
{
    private AdvancedReconciliationGroup() { }

    public AdvancedReconciliationGroup(Guid id, Guid companyId, Guid ruleId, int ruleVersion,
        Guid? correctionOfGroupId, string reference, string counterparty, string currency,
        decimal expectedBankTotal, decimal confidenceScore, bool requiresApproval,
        Guid createdByUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Require(companyId, nameof(companyId));
        RuleId = Require(ruleId, nameof(ruleId));
        RuleVersion = ruleVersion > 0 ? ruleVersion : throw new ArgumentOutOfRangeException(nameof(ruleVersion));
        CorrectionOfGroupId = correctionOfGroupId == Guid.Empty ? throw new ArgumentException("CorrectionOfGroupId cannot be empty.", nameof(correctionOfGroupId)) : correctionOfGroupId;
        Reference = Text(reference, nameof(reference), 200);
        Counterparty = Text(counterparty, nameof(counterparty), 200);
        Currency = CurrencyCode(currency);
        ExpectedBankTotal = Money(expectedBankTotal, nameof(expectedBankTotal));
        ConfidenceScore = Score(confidenceScore);
        RequiresApproval = requiresApproval;
        Status = AdvancedReconciliationGroupStatuses.Proposed;
        CreatedByUserId = UpdatedByUserId = Require(createdByUserId, nameof(createdByUserId));
        CreatedUtc = UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RuleId { get; private set; }
    public int RuleVersion { get; private set; }
    public Guid? CorrectionOfGroupId { get; private set; }
    public string Reference { get; private set; } = null!;
    public string Counterparty { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal ExpectedBankTotal { get; private set; }
    public decimal ConfidenceScore { get; private set; }
    public bool RequiresApproval { get; private set; }
    public string Status { get; private set; } = null!;
    public long Version { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? AcceptedUtc { get; private set; }
    public DateTime? RejectedUtc { get; private set; }
    public DateTime? ReversedUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public Company Company { get; private set; } = null!;
    public AdvancedReconciliationRule Rule { get; private set; } = null!;
    public ICollection<AdvancedReconciliationNode> Nodes { get; } = new List<AdvancedReconciliationNode>();
    public ICollection<AdvancedReconciliationEdge> Edges { get; } = new List<AdvancedReconciliationEdge>();
    public ICollection<AdvancedReconciliationReasonContribution> ReasonContributions { get; } = new List<AdvancedReconciliationReasonContribution>();
    public ICollection<AdvancedReconciliationResult> Results { get; } = new List<AdvancedReconciliationResult>();
    public ICollection<AdvancedReconciliationEvent> Events { get; } = new List<AdvancedReconciliationEvent>();

    public void Accept(long expectedVersion, Guid actorUserId, string reason, DateTime acceptedUtc) => Transition(expectedVersion,
        AdvancedReconciliationGroupStatuses.Accepted, actorUserId, reason, acceptedUtc, value => AcceptedUtc = value);
    public void Reject(long expectedVersion, Guid actorUserId, string reason, DateTime rejectedUtc) => Transition(expectedVersion,
        AdvancedReconciliationGroupStatuses.Rejected, actorUserId, reason, rejectedUtc, value => RejectedUtc = value);

    public void Reverse(long expectedVersion, Guid actorUserId, string reason, DateTime reversedUtc)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The reconciliation group changed after it was opened.");
        if (Status != AdvancedReconciliationGroupStatuses.Accepted) throw new InvalidOperationException("Only an accepted reconciliation group can be reversed.");
        UpdatedByUserId = Require(actorUserId, nameof(actorUserId));
        DecisionReason = Text(reason, nameof(reason), 1000);
        ReversedUtc = UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(reversedUtc, nameof(reversedUtc));
        Status = AdvancedReconciliationGroupStatuses.Reversed;
        Version++;
    }

    private void Transition(long expectedVersion, string status, Guid actorUserId, string reason, DateTime occurredUtc, Action<DateTime> stamp)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The reconciliation group changed after it was opened.");
        if (!AdvancedReconciliationGroupStatuses.IsActionable(Status)) throw new InvalidOperationException("The reconciliation group is no longer awaiting a decision.");
        UpdatedByUserId = Require(actorUserId, nameof(actorUserId));
        DecisionReason = Text(reason, nameof(reason), 1000);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        stamp(UpdatedUtc);
        Status = status;
        Version++;
    }

    private static Guid Require(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static string Text(string value, string name, int maxLength) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string CurrencyCode(string value) => string.IsNullOrWhiteSpace(value) || value.Trim().Length != 3 ? throw new ArgumentException("Currency must be a three-letter code.", nameof(value)) : value.Trim().ToUpperInvariant();
    private static decimal Money(decimal value, string name) => value <= 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal Score(decimal value) => value is < 0m or > 1m ? throw new ArgumentOutOfRangeException(nameof(value)) : decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}

public sealed class AdvancedReconciliationNode : ICompanyOwnedEntity
{
    private AdvancedReconciliationNode() { }
    public AdvancedReconciliationNode(Guid id, Guid companyId, Guid groupId, string nodeType, Guid? recordId,
        string label, string reference, string currency, decimal amount, string? direction, string? adjustmentKind,
        decimal debitAmount, decimal creditAmount, string? expectedRecordVersion, int sequence)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Required(companyId); GroupId = Required(groupId);
        NodeType = AdvancedReconciliationNodeTypes.Normalize(nodeType);
        if (!AdvancedReconciliationNodeTypes.IsSupported(NodeType)) throw new ArgumentOutOfRangeException(nameof(nodeType));
        if (AdvancedReconciliationNodeTypes.IsRecordBacked(NodeType) != recordId.HasValue) throw new ArgumentException("Record-backed nodes must reference a record.", nameof(recordId));
        RecordId = recordId; Label = Text(label, 200); Reference = Text(reference, 300); Currency = CurrencyCode(currency);
        Amount = NonNegative(amount); Direction = string.IsNullOrWhiteSpace(direction) ? null : AdvancedReconciliationDirections.Normalize(direction);
        AdjustmentKind = string.IsNullOrWhiteSpace(adjustmentKind) ? null : adjustmentKind.Trim().ToLowerInvariant();
        DebitAmount = NonNegative(debitAmount); CreditAmount = NonNegative(creditAmount);
        ExpectedRecordVersion = string.IsNullOrWhiteSpace(expectedRecordVersion) ? null : Text(expectedRecordVersion, 200);
        Sequence = sequence >= 0 ? sequence : throw new ArgumentOutOfRangeException(nameof(sequence));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid GroupId { get; private set; }
    public string NodeType { get; private set; } = null!; public Guid? RecordId { get; private set; }
    public string Label { get; private set; } = null!; public string Reference { get; private set; } = null!; public string Currency { get; private set; } = null!;
    public decimal Amount { get; private set; } public string? Direction { get; private set; } public string? AdjustmentKind { get; private set; }
    public decimal DebitAmount { get; private set; } public decimal CreditAmount { get; private set; } public string? ExpectedRecordVersion { get; private set; }
    public int Sequence { get; private set; } public AdvancedReconciliationGroup Group { get; private set; } = null!;
    private static Guid Required(Guid value) => value == Guid.Empty ? throw new ArgumentException("Identity is required.") : value;
    private static string Text(string value, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Text is required.") : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
    private static string CurrencyCode(string value) => string.IsNullOrWhiteSpace(value) || value.Trim().Length != 3 ? throw new ArgumentException("Currency must be a three-letter code.") : value.Trim().ToUpperInvariant();
    private static decimal NonNegative(decimal value) => value < 0m ? throw new ArgumentOutOfRangeException(nameof(value)) : decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class AdvancedReconciliationEdge : ICompanyOwnedEntity
{
    private AdvancedReconciliationEdge() { }
    public AdvancedReconciliationEdge(Guid id, Guid companyId, Guid groupId, Guid sourceNodeId, Guid targetNodeId, string edgeType, decimal amount)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Required(companyId); GroupId = Required(groupId); SourceNodeId = Required(sourceNodeId); TargetNodeId = Required(targetNodeId); EdgeType = AdvancedReconciliationEdgeTypes.Normalize(edgeType); if (!AdvancedReconciliationEdgeTypes.IsSupported(EdgeType)) throw new ArgumentOutOfRangeException(nameof(edgeType)); Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero); if (EdgeType != AdvancedReconciliationEdgeTypes.BankAdjustment && Amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount)); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid GroupId { get; private set; } public Guid SourceNodeId { get; private set; } public Guid TargetNodeId { get; private set; } public string EdgeType { get; private set; } = null!; public decimal Amount { get; private set; }
    public AdvancedReconciliationGroup Group { get; private set; } = null!; public AdvancedReconciliationNode SourceNode { get; private set; } = null!; public AdvancedReconciliationNode TargetNode { get; private set; } = null!;
    private static Guid Required(Guid value) => value == Guid.Empty ? throw new ArgumentException("Identity is required.") : value;
}

public sealed class AdvancedReconciliationReasonContribution : ICompanyOwnedEntity
{
    private AdvancedReconciliationReasonContribution() { }
    public AdvancedReconciliationReasonContribution(Guid id, Guid companyId, Guid groupId, string featureKey, decimal contribution, string explanation, string evidence)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Required(companyId); GroupId = Required(groupId); FeatureKey = Text(featureKey, 80); Contribution = contribution is < 0m or > 1m ? throw new ArgumentOutOfRangeException(nameof(contribution)) : decimal.Round(contribution, 4, MidpointRounding.AwayFromZero); Explanation = Text(explanation, 500); Evidence = Text(evidence, 1000); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid GroupId { get; private set; } public string FeatureKey { get; private set; } = null!; public decimal Contribution { get; private set; } public string Explanation { get; private set; } = null!; public string Evidence { get; private set; } = null!; public AdvancedReconciliationGroup Group { get; private set; } = null!;
    private static Guid Required(Guid value) => value == Guid.Empty ? throw new ArgumentException("Identity is required.") : value;
    private static string Text(string value, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Text is required.") : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed class AdvancedReconciliationResult : ICompanyOwnedEntity
{
    private AdvancedReconciliationResult() { }
    public AdvancedReconciliationResult(Guid id, Guid companyId, Guid groupId, Guid? parentResultId, string outcome, long groupVersion, int ruleVersion, decimal expectedBankTotal, decimal allocatedAmount, decimal feeAmount, decimal roundingAmount, decimal residualAmount, string evidenceJson, Guid createdByUserId, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Required(companyId); GroupId = Required(groupId); ParentResultId = parentResultId; Outcome = string.IsNullOrWhiteSpace(outcome) ? throw new ArgumentException("Outcome is required.") : outcome.Trim().ToLowerInvariant(); GroupVersion = groupVersion; RuleVersion = ruleVersion; ExpectedBankTotal = Money(expectedBankTotal); AllocatedAmount = Money(allocatedAmount); FeeAmount = Money(feeAmount); RoundingAmount = Money(roundingAmount); ResidualAmount = Money(residualAmount); EvidenceJson = string.IsNullOrWhiteSpace(evidenceJson) ? "{}" : evidenceJson; CreatedByUserId = Required(createdByUserId); CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc)); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid GroupId { get; private set; } public Guid? ParentResultId { get; private set; } public string Outcome { get; private set; } = null!; public long GroupVersion { get; private set; } public int RuleVersion { get; private set; } public decimal ExpectedBankTotal { get; private set; } public decimal AllocatedAmount { get; private set; } public decimal FeeAmount { get; private set; } public decimal RoundingAmount { get; private set; } public decimal ResidualAmount { get; private set; } public string EvidenceJson { get; private set; } = null!; public Guid CreatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; } public AdvancedReconciliationGroup Group { get; private set; } = null!;
    private static Guid Required(Guid value) => value == Guid.Empty ? throw new ArgumentException("Identity is required.") : value;
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class AdvancedReconciliationEvent : ICompanyOwnedEntity
{
    private AdvancedReconciliationEvent() { }
    public AdvancedReconciliationEvent(Guid id, Guid companyId, Guid groupId, string action, Guid actorUserId, string beforeJson, string afterJson, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Required(companyId); GroupId = Required(groupId); Action = string.IsNullOrWhiteSpace(action) ? throw new ArgumentException("Action is required.") : action.Trim(); ActorUserId = Required(actorUserId); BeforeJson = string.IsNullOrWhiteSpace(beforeJson) ? "{}" : beforeJson; AfterJson = string.IsNullOrWhiteSpace(afterJson) ? "{}" : afterJson; CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc)); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid GroupId { get; private set; } public string Action { get; private set; } = null!; public Guid ActorUserId { get; private set; } public string BeforeJson { get; private set; } = null!; public string AfterJson { get; private set; } = null!; public DateTime CreatedUtc { get; private set; } public AdvancedReconciliationGroup Group { get; private set; } = null!;
    private static Guid Required(Guid value) => value == Guid.Empty ? throw new ArgumentException("Identity is required.") : value;
}

