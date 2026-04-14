using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class Escalation : ICompanyOwnedEntity
{
    private const int SourceEntityTypeMaxLength = 64;
    private const int ReasonMaxLength = 1000;
    private const int CorrelationIdMaxLength = 128;

    private Escalation()
    {
    }

    public Escalation(
        Guid id,
        Guid companyId,
        Guid policyId,
        Guid sourceEntityId,
        string sourceEntityType,
        int escalationLevel,
        string reason,
        DateTime triggeredUtc,
        string? correlationId,
        int lifecycleVersion)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (policyId == Guid.Empty)
        {
            throw new ArgumentException("PolicyId is required.", nameof(policyId));
        }

        if (sourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("SourceEntityId is required.", nameof(sourceEntityId));
        }

        if (escalationLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(escalationLevel), "EscalationLevel must be greater than zero.");
        }

        if (lifecycleVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleVersion), "LifecycleVersion cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        PolicyId = policyId;
        SourceEntityId = sourceEntityId;
        SourceEntityType = NormalizeRequired(sourceEntityType, nameof(sourceEntityType), SourceEntityTypeMaxLength);
        EscalationLevel = escalationLevel;
        Reason = NormalizeRequired(reason, nameof(reason), ReasonMaxLength);
        TriggeredUtc = NormalizeUtc(triggeredUtc, nameof(triggeredUtc));
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        LifecycleVersion = lifecycleVersion;
        Status = EscalationStatusValues.DefaultStatus;
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PolicyId { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public string SourceEntityType { get; private set; } = null!;
    public int EscalationLevel { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTime TriggeredUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public int LifecycleVersion { get; private set; }
    public EscalationStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return NormalizeOptional(value, name, maxLength)!;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed record EscalationPolicyDefinition(
    Guid PolicyId,
    string Name,
    bool Enabled,
    IReadOnlyList<EscalationLevelDefinition> Levels);

public sealed record EscalationLevelDefinition(
    int EscalationLevel,
    string Reason,
    IReadOnlyList<EscalationConditionDefinition> Conditions,
    EscalationConditionMode Mode = EscalationConditionMode.All);

public sealed record EscalationConditionDefinition(
    EscalationConditionType Type,
    string Field,
    string Operator,
    object? Value = null,
    int? DurationSeconds = null,
    string? SinceField = null);

public enum EscalationConditionMode
{
    All = 1,
    Any = 2
}

public enum EscalationConditionType
{
    Threshold = 1,
    Timer = 2,
    Rule = 3
}
