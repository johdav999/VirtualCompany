namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderEndpointKinds
{
    public const string Internal = "internal";
    public const string External = "external";

    public static string Normalize(string value) => NormalizeToken(value, nameof(value)) switch
    {
        Internal => Internal,
        External => External,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Accounting endpoint must be Virtual Company or an external provider.")
    };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Accounting endpoint kind is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public readonly record struct AccountingProviderEndpoint
{
    public AccountingProviderEndpoint(string kind, string? providerKey)
    {
        Kind = AccountingProviderEndpointKinds.Normalize(kind);
        ProviderKey = NormalizeProviderKey(providerKey);
        if (Kind == AccountingProviderEndpointKinds.Internal && ProviderKey is not null)
            throw new ArgumentException("Virtual Company endpoints cannot have an external provider key.", nameof(providerKey));
        if (Kind == AccountingProviderEndpointKinds.External && ProviderKey is null)
            throw new ArgumentException("External accounting endpoints require a provider key.", nameof(providerKey));
    }

    public string Kind { get; }
    public string? ProviderKey { get; }

    public bool IsSameAs(AccountingProviderEndpoint other) =>
        Kind == other.Kind && string.Equals(ProviderKey, other.ProviderKey, StringComparison.Ordinal);

    private static string? NormalizeProviderKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(value), "Provider key must be 64 characters or fewer.");
        if (normalized.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-')))
            throw new ArgumentException("Provider key may contain only letters, numbers, hyphens, and underscores.", nameof(value));
        return normalized;
    }
}

public static class AccountingProviderSwitchStrategies
{
    public const string OpeningBalancesAndOpenItems = "opening_balances_and_open_items";
    public const string CurrentFiscalYear = "current_fiscal_year";
    public const string FullHistory = "full_history";

    public static string Normalize(string value) => NormalizeToken(value, nameof(value)) switch
    {
        OpeningBalancesAndOpenItems => OpeningBalancesAndOpenItems,
        CurrentFiscalYear => CurrentFiscalYear,
        FullHistory => FullHistory,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Migration strategy is not supported.")
    };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Migration strategy is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public static class AccountingProviderSwitchStatuses
{
    public const string Draft = "draft";
    public const string Assessing = "assessing";
    public const string ReadyForPlanning = "ready_for_planning";
    public const string PlanAwaitingApproval = "plan_awaiting_approval";
    public const string PreparingTarget = "preparing_target";
    public const string RehearsalPassed = "rehearsal_passed";
    public const string Scheduled = "scheduled";
    public const string SourceFrozen = "source_frozen";
    public const string Reconciling = "reconciling";
    public const string ActivationAwaitingApproval = "activation_awaiting_approval";
    public const string TargetAuthoritative = "target_authoritative";
    public const string Monitoring = "monitoring";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
    public const string Recovery = "recovery";

    private static readonly IReadOnlyDictionary<string, string[]> NormalTransitions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Draft] = [Assessing],
            [Assessing] = [ReadyForPlanning],
            [ReadyForPlanning] = [PlanAwaitingApproval],
            [PlanAwaitingApproval] = [PreparingTarget],
            [PreparingTarget] = [RehearsalPassed],
            [RehearsalPassed] = [Scheduled],
            [Scheduled] = [SourceFrozen],
            [SourceFrozen] = [Reconciling],
            [Reconciling] = [ActivationAwaitingApproval],
            [ActivationAwaitingApproval] = [TargetAuthoritative],
            [TargetAuthoritative] = [Monitoring, Recovery],
            [Monitoring] = [Completed, Recovery]
        };

    public static bool IsTerminal(string status) => status is Completed or Cancelled;

    public static bool IsPreActivation(string status, string? blockedFromStatus = null) => status switch
    {
        Draft or Assessing or ReadyForPlanning or PlanAwaitingApproval or PreparingTarget or RehearsalPassed or
            Scheduled or SourceFrozen or Reconciling or ActivationAwaitingApproval => true,
        Blocked or Recovery when blockedFromStatus is not null => IsPreActivation(blockedFromStatus),
        _ => false
    };

    public static IReadOnlyList<string> AllowedTransitions(string status, string? blockedFromStatus = null)
    {
        var normalized = Normalize(status);
        var allowed = NormalTransitions.TryGetValue(normalized, out var configured)
            ? configured.ToList()
            : [];

        if (!IsTerminal(normalized) && normalized is not Blocked)
            allowed.Add(Blocked);
        if (IsPreActivation(normalized, blockedFromStatus))
            allowed.Add(Cancelled);
        if (normalized == Blocked)
            allowed.Add(Recovery);
        if (normalized == Recovery && blockedFromStatus is not null)
            allowed.Add(blockedFromStatus);

        return allowed.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string Normalize(string value) => NormalizeToken(value, nameof(value)) switch
    {
        Draft => Draft,
        Assessing => Assessing,
        ReadyForPlanning => ReadyForPlanning,
        PlanAwaitingApproval => PlanAwaitingApproval,
        PreparingTarget => PreparingTarget,
        RehearsalPassed => RehearsalPassed,
        Scheduled => Scheduled,
        SourceFrozen => SourceFrozen,
        Reconciling => Reconciling,
        ActivationAwaitingApproval => ActivationAwaitingApproval,
        TargetAuthoritative => TargetAuthoritative,
        Monitoring => Monitoring,
        Completed => Completed,
        Blocked => Blocked,
        Cancelled => Cancelled,
        Recovery => Recovery,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Accounting provider switch status is not supported.")
    };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Accounting provider switch status is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public sealed class AccountingProviderSwitch : ICompanyOwnedEntity
{
    private AccountingProviderSwitch() { }

    public AccountingProviderSwitch(
        Guid id,
        Guid companyId,
        AccountingProviderEndpoint source,
        AccountingProviderEndpoint target,
        Guid effectiveFiscalPeriodId,
        string migrationStrategy,
        string reason,
        Guid responsibleUserId,
        Guid? responsibleAgentId,
        Guid createdByUserId,
        string correlationId,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (effectiveFiscalPeriodId == Guid.Empty) throw new ArgumentException("EffectiveFiscalPeriodId is required.", nameof(effectiveFiscalPeriodId));
        if (responsibleUserId == Guid.Empty) throw new ArgumentException("ResponsibleUserId is required.", nameof(responsibleUserId));
        if (responsibleAgentId == Guid.Empty) throw new ArgumentException("ResponsibleAgentId cannot be empty.", nameof(responsibleAgentId));
        if (createdByUserId == Guid.Empty) throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        if (source.IsSameAs(target)) throw new ArgumentException("Source and target accounting systems must be different.", nameof(target));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ApplyEndpoints(source, target);
        EffectiveFiscalPeriodId = effectiveFiscalPeriodId;
        MigrationStrategy = AccountingProviderSwitchStrategies.Normalize(migrationStrategy);
        Reason = RequiredText(reason, nameof(reason), 1000);
        ResponsibleUserId = responsibleUserId;
        ResponsibleAgentId = responsibleAgentId;
        Status = AccountingProviderSwitchStatuses.Draft;
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        CorrelationId = RequiredText(correlationId, nameof(correlationId), 128);
        CreatedUtc = NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        StatusChangedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string SourceKind { get; private set; } = null!;
    public string? SourceProviderKey { get; private set; }
    public string TargetKind { get; private set; } = null!;
    public string? TargetProviderKey { get; private set; }
    public Guid EffectiveFiscalPeriodId { get; private set; }
    public string MigrationStrategy { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public Guid ResponsibleUserId { get; private set; }
    public Guid? ResponsibleAgentId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? BlockedFromStatus { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime StatusChangedUtc { get; private set; }
    public DateTime? BlockedUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod EffectiveFiscalPeriod { get; private set; } = null!;
    public Agent? ResponsibleAgent { get; private set; }

    public AccountingProviderEndpoint Source => new(SourceKind, SourceProviderKey);
    public AccountingProviderEndpoint Target => new(TargetKind, TargetProviderKey);
    public bool IsTerminal => AccountingProviderSwitchStatuses.IsTerminal(Status);
    public bool CanUpdatePlan => Status == AccountingProviderSwitchStatuses.Draft;
    public bool CanCancel => AccountingProviderSwitchStatuses.IsPreActivation(Status, BlockedFromStatus);

    public void UpdatePlan(
        AccountingProviderEndpoint source,
        AccountingProviderEndpoint target,
        Guid effectiveFiscalPeriodId,
        string migrationStrategy,
        string reason,
        Guid responsibleUserId,
        Guid? responsibleAgentId,
        Guid actorUserId,
        string correlationId,
        DateTime updatedUtc)
    {
        if (!CanUpdatePlan) throw new InvalidOperationException("Only a draft accounting-system switch can be edited.");
        if (source.IsSameAs(target)) throw new ArgumentException("Source and target accounting systems must be different.", nameof(target));
        if (effectiveFiscalPeriodId == Guid.Empty) throw new ArgumentException("EffectiveFiscalPeriodId is required.", nameof(effectiveFiscalPeriodId));
        if (responsibleUserId == Guid.Empty) throw new ArgumentException("ResponsibleUserId is required.", nameof(responsibleUserId));
        if (responsibleAgentId == Guid.Empty) throw new ArgumentException("ResponsibleAgentId cannot be empty.", nameof(responsibleAgentId));

        ApplyEndpoints(source, target);
        EffectiveFiscalPeriodId = effectiveFiscalPeriodId;
        MigrationStrategy = AccountingProviderSwitchStrategies.Normalize(migrationStrategy);
        Reason = RequiredText(reason, nameof(reason), 1000);
        ResponsibleUserId = responsibleUserId;
        ResponsibleAgentId = responsibleAgentId;
        Touch(actorUserId, correlationId, updatedUtc);
    }

    public void TransitionTo(string nextStatus, Guid actorUserId, string correlationId, DateTime updatedUtc)
    {
        var normalized = AccountingProviderSwitchStatuses.Normalize(nextStatus);
        if (normalized is AccountingProviderSwitchStatuses.Blocked or AccountingProviderSwitchStatuses.Cancelled)
            throw new InvalidOperationException("Use the dedicated block or cancel operation for this transition.");
        if (!AccountingProviderSwitchStatuses.AllowedTransitions(Status, BlockedFromStatus).Contains(normalized, StringComparer.Ordinal))
            throw new InvalidOperationException($"The accounting-system switch cannot move from '{Status}' to '{normalized}'.");

        var currentStatus = Status;
        if (normalized == AccountingProviderSwitchStatuses.Recovery && currentStatus != AccountingProviderSwitchStatuses.Blocked)
            BlockedFromStatus = currentStatus;
        Status = normalized;
        if (normalized != AccountingProviderSwitchStatuses.Recovery)
        {
            FailureCode = null;
            FailureSummary = null;
            BlockedUtc = null;
            if (BlockedFromStatus == normalized) BlockedFromStatus = null;
        }
        if (normalized == AccountingProviderSwitchStatuses.Completed)
            CompletedUtc = NormalizeUtc(updatedUtc, nameof(updatedUtc));
        StatusChangedUtc = NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Touch(actorUserId, correlationId, updatedUtc);
    }

    public void Block(string failureCode, string failureSummary, Guid actorUserId, string correlationId, DateTime blockedUtc)
    {
        if (!AccountingProviderSwitchStatuses.AllowedTransitions(Status, BlockedFromStatus)
                .Contains(AccountingProviderSwitchStatuses.Blocked, StringComparer.Ordinal))
            throw new InvalidOperationException("This accounting-system switch cannot be blocked from its current state.");

        if (Status != AccountingProviderSwitchStatuses.Recovery)
            BlockedFromStatus = Status;
        Status = AccountingProviderSwitchStatuses.Blocked;
        FailureCode = RequiredText(failureCode, nameof(failureCode), 100).ToLowerInvariant();
        FailureSummary = RequiredText(failureSummary, nameof(failureSummary), 1000);
        BlockedUtc = NormalizeUtc(blockedUtc, nameof(blockedUtc));
        StatusChangedUtc = BlockedUtc.Value;
        Touch(actorUserId, correlationId, blockedUtc);
    }

    public void Cancel(string reason, Guid actorUserId, string correlationId, DateTime cancelledUtc)
    {
        if (!CanCancel) throw new InvalidOperationException("This accounting-system switch can no longer be cancelled because target activation has begun.");
        Status = AccountingProviderSwitchStatuses.Cancelled;
        CancellationReason = RequiredText(reason, nameof(reason), 1000);
        CancelledByUserId = Required(actorUserId, nameof(actorUserId));
        CancelledUtc = NormalizeUtc(cancelledUtc, nameof(cancelledUtc));
        StatusChangedUtc = CancelledUtc.Value;
        FailureCode = null;
        FailureSummary = null;
        Touch(actorUserId, correlationId, cancelledUtc);
    }

    private void ApplyEndpoints(AccountingProviderEndpoint source, AccountingProviderEndpoint target)
    {
        SourceKind = source.Kind;
        SourceProviderKey = source.ProviderKey;
        TargetKind = target.Kind;
        TargetProviderKey = target.ProviderKey;
    }

    private void Touch(Guid actorUserId, string correlationId, DateTime updatedUtc)
    {
        UpdatedByUserId = Required(actorUserId, nameof(actorUserId));
        CorrelationId = RequiredText(correlationId, nameof(correlationId), 128);
        UpdatedUtc = NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    private static Guid Required(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;

    private static string RequiredText(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static DateTime NormalizeUtc(DateTime value, string name) =>
        value == default
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
