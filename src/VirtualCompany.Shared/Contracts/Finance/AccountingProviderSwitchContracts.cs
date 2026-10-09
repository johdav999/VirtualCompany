namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchEndpointDto(string Kind, string? ProviderKey, string DisplayName)
{
    public string Kind { get; set; } = Kind;
    public string? ProviderKey { get; set; } = ProviderKey;
    public string DisplayName { get; set; } = DisplayName;

    public AccountingProviderSwitchEndpointDto() : this(string.Empty, default !, string.Empty)
    {
    }
}

public sealed record AccountingProviderSwitchDto(Guid Id, Guid CompanyId, AccountingProviderSwitchEndpointDto Source, AccountingProviderSwitchEndpointDto Target, string Direction, Guid EffectiveFiscalPeriodId, DateOnly EffectiveFrom, DateOnly EffectiveTo, string MigrationStrategy, string MigrationStrategyLabel, string Reason, Guid ResponsibleUserId, Guid? ResponsibleAgentId, string Status, string StatusLabel, string? BlockedFromStatus, string? FailureCode, string? FailureSummary, Guid CreatedByUserId, Guid UpdatedByUserId, Guid? CancelledByUserId, string? CancellationReason, string CorrelationId, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime StatusChangedUtc, DateTime? BlockedUtc, DateTime? CancelledUtc, DateTime? CompletedUtc, long Version)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public AccountingProviderSwitchEndpointDto Source { get; set; } = Source;
    public AccountingProviderSwitchEndpointDto Target { get; set; } = Target;
    public string Direction { get; set; } = Direction;
    public Guid EffectiveFiscalPeriodId { get; set; } = EffectiveFiscalPeriodId;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly EffectiveTo { get; set; } = EffectiveTo;
    public string MigrationStrategy { get; set; } = MigrationStrategy;
    public string MigrationStrategyLabel { get; set; } = MigrationStrategyLabel;
    public string Reason { get; set; } = Reason;
    public Guid ResponsibleUserId { get; set; } = ResponsibleUserId;
    public Guid? ResponsibleAgentId { get; set; } = ResponsibleAgentId;
    public string Status { get; set; } = Status;
    public string StatusLabel { get; set; } = StatusLabel;
    public string? BlockedFromStatus { get; set; } = BlockedFromStatus;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public Guid CreatedByUserId { get; set; } = CreatedByUserId;
    public Guid UpdatedByUserId { get; set; } = UpdatedByUserId;
    public Guid? CancelledByUserId { get; set; } = CancelledByUserId;
    public string? CancellationReason { get; set; } = CancellationReason;
    public string CorrelationId { get; set; } = CorrelationId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime StatusChangedUtc { get; set; } = StatusChangedUtc;
    public DateTime? BlockedUtc { get; set; } = BlockedUtc;
    public DateTime? CancelledUtc { get; set; } = CancelledUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public long Version { get; set; } = Version;

    public AccountingProviderSwitchDto() : this(default !, default !, new(), new(), string.Empty, default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchAllowedActionsDto(Guid SwitchId, long Version, string Status, bool IsTerminal, bool CanUpdatePlan, bool CanCancel, bool IsReadyForNextStep, IReadOnlyList<string> AllowedTransitions, string Explanation, string? BlockingReasonCode, string? BlockingSummary)
{
    public Guid SwitchId { get; set; } = SwitchId;
    public long Version { get; set; } = Version;
    public string Status { get; set; } = Status;
    public bool IsTerminal { get; set; } = IsTerminal;
    public bool CanUpdatePlan { get; set; } = CanUpdatePlan;
    public bool CanCancel { get; set; } = CanCancel;
    public bool IsReadyForNextStep { get; set; } = IsReadyForNextStep;
    public IReadOnlyList<string> AllowedTransitions { get; set; } = AllowedTransitions;
    public string Explanation { get; set; } = Explanation;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string? BlockingSummary { get; set; } = BlockingSummary;

    public AccountingProviderSwitchAllowedActionsDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, [], string.Empty, default !, default !)
    {
    }
}
