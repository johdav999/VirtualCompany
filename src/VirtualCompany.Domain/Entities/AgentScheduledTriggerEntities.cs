using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class AgentScheduledTrigger : ICompanyOwnedEntity
{
    private const int NameMaxLength = 200;
    private const int CodeMaxLength = 100;
    private const int CronExpressionMaxLength = 200;
    private const int TimeZoneIdMaxLength = 100;

    private AgentScheduledTrigger()
    {
        Metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
    }

    public AgentScheduledTrigger(
        Guid id,
        Guid companyId,
        Guid agentId,
        string name,
        string code,
        string cronExpression,
        string timeZoneId,
        DateTime? nextRunUtc,
        bool isEnabled = true,
        IDictionary<string, JsonNode?>? metadata = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        Name = NormalizeRequired(name, nameof(name), NameMaxLength);
        Code = NormalizeCode(code);
        CronExpression = NormalizeRequired(cronExpression, nameof(cronExpression), CronExpressionMaxLength);
        TimeZoneId = NormalizeRequired(timeZoneId, nameof(timeZoneId), TimeZoneIdMaxLength);
        IsEnabled = isEnabled;
        NextRunUtc = isEnabled ? NormalizeUtc(nextRunUtc, nameof(nextRunUtc)) : null;
        Metadata = CloneNodes(metadata);
        CreatedUtc = DateTime.UtcNow;
        EnabledUtc = isEnabled ? CreatedUtc : null;
        DisabledUtc = isEnabled ? null : CreatedUtc;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string CronExpression { get; private set; } = null!;
    public string TimeZoneId { get; private set; } = null!;
    public bool IsEnabled { get; private set; }
    public DateTime? NextRunUtc { get; private set; }
    public DateTime? EnabledUtc { get; private set; }
    public DateTime? LastEvaluatedUtc { get; private set; }
    public DateTime? LastEnqueuedUtc { get; private set; }
    public DateTime? LastRunUtc { get; private set; }
    public DateTime? DisabledUtc { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
    public ICollection<AgentScheduledTriggerEnqueueWindow> EnqueueWindows { get; } = new List<AgentScheduledTriggerEnqueueWindow>();

    public bool IsEligibleForEnqueue(DateTime dueAtUtc) =>
        IsEnabled &&
        NextRunUtc.HasValue &&
        NextRunUtc.Value <= NormalizeUtc(dueAtUtc, nameof(dueAtUtc)) &&
        (!DisabledUtc.HasValue || dueAtUtc <= DisabledUtc.Value);

    public void UpdateSchedule(
        string name,
        string code,
        string cronExpression,
        string timeZoneId,
        DateTime? nextRunUtc,
        IDictionary<string, JsonNode?>? metadata = null)
    {
        Name = NormalizeRequired(name, nameof(name), NameMaxLength);
        Code = NormalizeCode(code);
        CronExpression = NormalizeRequired(cronExpression, nameof(cronExpression), CronExpressionMaxLength);
        TimeZoneId = NormalizeRequired(timeZoneId, nameof(timeZoneId), TimeZoneIdMaxLength);
        Metadata = CloneNodes(metadata);
        NextRunUtc = IsEnabled ? NormalizeUtc(nextRunUtc, nameof(nextRunUtc)) : null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Enable(DateTime nextRunUtc)
    {
        IsEnabled = true;
        DisabledUtc = null;
        EnabledUtc = DateTime.UtcNow;
        NextRunUtc = NormalizeUtc(nextRunUtc, nameof(nextRunUtc));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Disable(DateTime disabledUtc)
    {
        DisabledUtc = NormalizeUtc(disabledUtc, nameof(disabledUtc));
        IsEnabled = false;
        NextRunUtc = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkEvaluated(DateTime evaluatedUtc, DateTime? nextRunUtc)
    {
        LastEvaluatedUtc = NormalizeUtc(evaluatedUtc, nameof(evaluatedUtc));
        NextRunUtc = IsEnabled ? NormalizeUtc(nextRunUtc, nameof(nextRunUtc)) : null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkEnqueued(DateTime enqueuedUtc, DateTime? nextRunUtc)
    {
        LastEnqueuedUtc = NormalizeUtc(enqueuedUtc, nameof(enqueuedUtc));
        NextRunUtc = IsEnabled ? NormalizeUtc(nextRunUtc, nameof(nextRunUtc)) : null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkRun(DateTime runUtc)
    {
        LastRunUtc = NormalizeUtc(runUtc, nameof(runUtc));
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeCode(string value) =>
        NormalizeRequired(value, nameof(Code), CodeMaxLength).ToUpperInvariant();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static DateTime? NormalizeUtc(DateTime? value, string name) =>
        value.HasValue ? NormalizeUtc(value.Value, name) : null;

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentScheduledTriggerEnqueueWindow : ICompanyOwnedEntity
{
    private const int ExecutionRequestIdMaxLength = 128;

    private AgentScheduledTriggerEnqueueWindow()
    {
    }

    public AgentScheduledTriggerEnqueueWindow(
        Guid id,
        Guid companyId,
        Guid scheduledTriggerId,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        DateTime enqueuedUtc,
        string? executionRequestId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (scheduledTriggerId == Guid.Empty)
        {
            throw new ArgumentException("ScheduledTriggerId is required.", nameof(scheduledTriggerId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ScheduledTriggerId = scheduledTriggerId;
        WindowStartUtc = NormalizeUtc(windowStartUtc, nameof(windowStartUtc));
        WindowEndUtc = NormalizeUtc(windowEndUtc, nameof(windowEndUtc));
        if (WindowEndUtc <= WindowStartUtc)
        {
            throw new ArgumentException("WindowEndUtc must be after WindowStartUtc.", nameof(windowEndUtc));
        }

        EnqueuedUtc = NormalizeUtc(enqueuedUtc, nameof(enqueuedUtc));
        ExecutionRequestId = NormalizeOptional(executionRequestId, nameof(executionRequestId), ExecutionRequestIdMaxLength);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ScheduledTriggerId { get; private set; }
    public DateTime WindowStartUtc { get; private set; }
    public DateTime WindowEndUtc { get; private set; }
    public DateTime EnqueuedUtc { get; private set; }
    public string? ExecutionRequestId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public AgentScheduledTrigger ScheduledTrigger { get; private set; } = null!;

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

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }
}
