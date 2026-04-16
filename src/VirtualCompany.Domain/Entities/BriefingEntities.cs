using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyBriefing : ICompanyOwnedEntity
{
    private const int TitleMaxLength = 200;
    private const int BodyMaxLength = 16000;

    private CompanyBriefing()
    {
    }

    public CompanyBriefing(
        Guid id,
        Guid companyId,
        CompanyBriefingType briefingType,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        string title,
        string summaryBody,
        IDictionary<string, JsonNode?>? structuredPayload,
        IDictionary<string, JsonNode?>? sourceReferences,
        IDictionary<string, JsonNode?>? preferenceSnapshot = null,
        Guid? messageId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (periodEndUtc <= periodStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(periodEndUtc), "Period end must be after period start.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BriefingType = briefingType;
        PeriodStartUtc = NormalizeUtc(periodStartUtc);
        PeriodEndUtc = NormalizeUtc(periodEndUtc);
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        SummaryBody = NormalizeRequired(summaryBody, nameof(summaryBody), BodyMaxLength);
        StructuredPayload = CloneNodes(structuredPayload);
        SourceReferences = CloneNodes(sourceReferences);
        PreferenceSnapshot = CloneNodes(preferenceSnapshot);
        MessageId = messageId;
        Status = CompanyBriefingStatus.Generated;
        GeneratedUtc = DateTime.UtcNow;
        CreatedUtc = GeneratedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public CompanyBriefingType BriefingType { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public string Title { get; private set; } = null!;
    public string SummaryBody { get; private set; } = null!;
    public Dictionary<string, JsonNode?> StructuredPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> SourceReferences { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> PreferenceSnapshot { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public CompanyBriefingStatus Status { get; private set; }
    public Guid? MessageId { get; private set; }
    public DateTime GeneratedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Message? Message { get; private set; }
    public ICollection<CompanyBriefingSection> Sections { get; private set; } = new List<CompanyBriefingSection>();

    public void AttachMessage(Guid messageId)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("MessageId cannot be empty.", nameof(messageId));
        }

        MessageId = messageId;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanyBriefingSection : ICompanyOwnedEntity
{
    private const int SectionKeyMaxLength = 256;
    private const int TitleMaxLength = 200;
    private const int GroupingTypeMaxLength = 64;
    private const int GroupingKeyMaxLength = 256;
    private const int EventCorrelationIdMaxLength = 128;
    private const int NarrativeMaxLength = 8000;
    private const int ConflictSummaryMaxLength = 2000;

    private CompanyBriefingSection()
    {
    }

    public CompanyBriefingSection(
        Guid id,
        Guid companyId,
        Guid briefingId,
        string sectionKey,
        string title,
        string groupingType,
        string groupingKey,
        string narrative,
        bool isConflicting,
        string? conflictSummary,
        Guid? companyEntityId,
        string sectionType,
        BriefingSectionPriorityCategory priorityCategory,
        int priorityScore,
        string? priorityRuleCode,
        Guid? workflowInstanceId,
        Guid? taskId,
        string? eventCorrelationId,
        IDictionary<string, JsonNode?>? sourceReferences)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (briefingId == Guid.Empty)
        {
            throw new ArgumentException("BriefingId is required.", nameof(briefingId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BriefingId = briefingId;
        SectionKey = NormalizeRequired(sectionKey, nameof(sectionKey), SectionKeyMaxLength);
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        GroupingType = NormalizeRequired(groupingType, nameof(groupingType), GroupingTypeMaxLength);
        GroupingKey = NormalizeRequired(groupingKey, nameof(groupingKey), GroupingKeyMaxLength);
        Narrative = NormalizeRequired(narrative, nameof(narrative), NarrativeMaxLength);
        IsConflicting = isConflicting;
        ConflictSummary = NormalizeOptional(conflictSummary, nameof(conflictSummary), ConflictSummaryMaxLength);
        CompanyEntityId = NormalizeOptionalGuid(companyEntityId);
        WorkflowInstanceId = NormalizeOptionalGuid(workflowInstanceId);
        SectionType = NormalizeRequired(sectionType, nameof(sectionType), GroupingTypeMaxLength);
        PriorityCategory = priorityCategory;
        PriorityScore = priorityScore;
        PriorityRuleCode = NormalizeOptional(priorityRuleCode, nameof(priorityRuleCode), GroupingKeyMaxLength);
        TaskId = NormalizeOptionalGuid(taskId);
        EventCorrelationId = NormalizeOptional(eventCorrelationId, nameof(eventCorrelationId), EventCorrelationIdMaxLength);
        SourceReferences = CloneNodes(sourceReferences);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BriefingId { get; private set; }
    public string SectionKey { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string GroupingType { get; private set; } = null!;
    public string GroupingKey { get; private set; } = null!;
    public Guid? CompanyEntityId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public string SectionType { get; private set; } = null!;
    public BriefingSectionPriorityCategory PriorityCategory { get; private set; }
    public int PriorityScore { get; private set; }
    public string? PriorityRuleCode { get; private set; }
    public Guid? TaskId { get; private set; }
    public string? EventCorrelationId { get; private set; }
    public string Narrative { get; private set; } = null!;
    public bool IsConflicting { get; private set; }
    public string? ConflictSummary { get; private set; }
    public Dictionary<string, JsonNode?> SourceReferences { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyBriefing Briefing { get; private set; } = null!;
    public ICollection<CompanyBriefingContribution> Contributions { get; private set; } = new List<CompanyBriefingContribution>();

    private static Guid? NormalizeOptionalGuid(Guid? value) =>
        value is null || value == Guid.Empty ? null : value;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanyBriefingContribution : ICompanyOwnedEntity
{
    private const int SourceEntityTypeMaxLength = 100;
    private const int SourceLabelMaxLength = 300;
    private const int SourceStatusMaxLength = 100;
    private const int SourceRouteMaxLength = 2048;
    private const int TopicMaxLength = 300;
    private const int AssessmentMaxLength = 200;
    private const int NarrativeMaxLength = 8000;
    private const int EventCorrelationIdMaxLength = 128;

    private CompanyBriefingContribution()
    {
    }

    public CompanyBriefingContribution(
        Guid id,
        Guid companyId,
        Guid sectionId,
        Guid agentId,
        string sourceEntityType,
        Guid sourceEntityId,
        string sourceLabel,
        string? sourceStatus,
        string? sourceRoute,
        DateTime timestampUtc,
        decimal? confidenceScore,
        IDictionary<string, JsonNode?>? confidenceMetadata,
        Guid? companyEntityId,
        Guid? workflowInstanceId,
        Guid? taskId,
        string? eventCorrelationId,
        string topic,
        string narrative,
        string? assessment,
        IDictionary<string, JsonNode?>? metadata)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (sectionId == Guid.Empty)
        {
            throw new ArgumentException("SectionId is required.", nameof(sectionId));
        }

        if (confidenceScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceScore), "ConfidenceScore must be between 0 and 1.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SectionId = sectionId;
        AgentId = agentId;
        SourceEntityType = NormalizeRequired(sourceEntityType, nameof(sourceEntityType), SourceEntityTypeMaxLength);
        SourceEntityId = sourceEntityId;
        SourceLabel = NormalizeRequired(sourceLabel, nameof(sourceLabel), SourceLabelMaxLength);
        SourceStatus = NormalizeOptional(sourceStatus, nameof(sourceStatus), SourceStatusMaxLength);
        SourceRoute = NormalizeOptional(sourceRoute, nameof(sourceRoute), SourceRouteMaxLength);
        TimestampUtc = NormalizeUtc(timestampUtc);
        ConfidenceScore = confidenceScore;
        ConfidenceMetadata = CloneNodes(confidenceMetadata);
        CompanyEntityId = NormalizeOptionalGuid(companyEntityId);
        WorkflowInstanceId = NormalizeOptionalGuid(workflowInstanceId);
        TaskId = NormalizeOptionalGuid(taskId);
        EventCorrelationId = NormalizeOptional(eventCorrelationId, nameof(eventCorrelationId), EventCorrelationIdMaxLength);
        Topic = NormalizeRequired(topic, nameof(topic), TopicMaxLength);
        Narrative = NormalizeRequired(narrative, nameof(narrative), NarrativeMaxLength);
        Assessment = NormalizeOptional(assessment, nameof(assessment), AssessmentMaxLength);
        Metadata = CloneNodes(metadata);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SectionId { get; private set; }
    public Guid AgentId { get; private set; }
    public string SourceEntityType { get; private set; } = null!;
    public Guid SourceEntityId { get; private set; }
    public string SourceLabel { get; private set; } = null!;
    public string? SourceStatus { get; private set; }
    public string? SourceRoute { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public Dictionary<string, JsonNode?> ConfidenceMetadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Guid? CompanyEntityId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public Guid? TaskId { get; private set; }
    public string? EventCorrelationId { get; private set; }
    public string Topic { get; private set; } = null!;
    public string Narrative { get; private set; } = null!;
    public string? Assessment { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyBriefingSection Section { get; private set; } = null!;

    private static Guid? NormalizeOptionalGuid(Guid? value) =>
        value is null || value == Guid.Empty ? null : value;

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanyBriefingUpdateJob : ICompanyOwnedEntity
{
    private const int EventTypeMaxLength = 100;
    private const int CorrelationIdMaxLength = 128;
    private const int IdempotencyKeyMaxLength = 300;
    private const int LastErrorCodeMaxLength = 256;
    private const int LastErrorMaxLength = 4000;
    private const int LastErrorDetailsMaxLength = 12000;

    private CompanyBriefingUpdateJob()
    {
    }

    public CompanyBriefingUpdateJob(
        Guid id,
        Guid companyId,
        CompanyBriefingUpdateJobTriggerType triggerType,
        CompanyBriefingType? briefingType,
        string? eventType,
        string correlationId,
        string idempotencyKey,
        IDictionary<string, JsonNode?>? sourceMetadata,
        int maxAttempts = 5,
        DateTime? nextAttemptAtUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        TriggerType = triggerType;
        BriefingType = briefingType;
        EventType = NormalizeOptional(eventType, nameof(eventType), EventTypeMaxLength);
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        IdempotencyKey = NormalizeRequired(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength);
        SourceMetadata = CloneNodes(sourceMetadata);
        Status = CompanyBriefingUpdateJobStatus.Pending;
        AttemptCount = 0;
        MaxAttempts = Math.Max(1, maxAttempts);
        NextAttemptAt = nextAttemptAtUtc is null ? DateTime.UtcNow : NormalizeUtc(nextAttemptAtUtc.Value);
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public CompanyBriefingUpdateJobTriggerType TriggerType { get; private set; }
    public CompanyBriefingType? BriefingType { get; private set; }
    public string? EventType { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public CompanyBriefingUpdateJobStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastError { get; private set; }
    public string? LastErrorDetails { get; private set; }
    public DateTime? LastFailureAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FinalFailedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Dictionary<string, JsonNode?> SourceMetadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Company Company { get; private set; } = null!;

    public bool IsTerminal => Status is CompanyBriefingUpdateJobStatus.Completed or CompanyBriefingUpdateJobStatus.Failed;

    public void MarkProcessing(DateTime nowUtc)
    {
        var normalized = NormalizeUtc(nowUtc);
        Status = CompanyBriefingUpdateJobStatus.Processing;
        NextAttemptAt = null;
        StartedAt = normalized;
        UpdatedAt = normalized;
    }

    public void MarkCompleted(DateTime nowUtc)
    {
        var normalized = NormalizeUtc(nowUtc);
        Status = CompanyBriefingUpdateJobStatus.Completed;
        NextAttemptAt = null;
        LastErrorCode = null;
        LastError = null;
        LastErrorDetails = null;
        LastFailureAt = null;
        CompletedAt = normalized;
        UpdatedAt = normalized;
    }

    public void ScheduleRetry(DateTime nextAttemptAtUtc, string? errorCode, string error, string? errorDetails, DateTime nowUtc)
    {
        AttemptCount++;
        Status = CompanyBriefingUpdateJobStatus.Retrying;
        NextAttemptAt = NormalizeUtc(nextAttemptAtUtc);
        LastErrorCode = NormalizeOptional(errorCode, nameof(errorCode), LastErrorCodeMaxLength);
        LastError = NormalizeOptional(error, nameof(error), LastErrorMaxLength);
        LastErrorDetails = NormalizeOptional(errorDetails, nameof(errorDetails), LastErrorDetailsMaxLength);
        LastFailureAt = NormalizeUtc(nowUtc);
        UpdatedAt = LastFailureAt.Value;
    }

    public void MarkFailed(string? errorCode, string error, string? errorDetails, DateTime nowUtc)
    {
        var normalized = NormalizeUtc(nowUtc);
        AttemptCount++;
        Status = CompanyBriefingUpdateJobStatus.Failed;
        NextAttemptAt = null;
        LastErrorCode = NormalizeOptional(errorCode, nameof(errorCode), LastErrorCodeMaxLength);
        LastError = NormalizeOptional(error, nameof(error), LastErrorMaxLength);
        LastErrorDetails = NormalizeOptional(errorDetails, nameof(errorDetails), LastErrorDetailsMaxLength);
        LastFailureAt = normalized;
        FinalFailedAt = normalized;
        CompletedAt = normalized;
        UpdatedAt = normalized;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanyBriefingDeliveryPreference : ICompanyOwnedEntity
{
    private CompanyBriefingDeliveryPreference()
    {
    }

    public CompanyBriefingDeliveryPreference(Guid id, Guid companyId, Guid userId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        InAppEnabled = true;
        MobileEnabled = false;
        DailyEnabled = true;
        WeeklyEnabled = true;
        PreferredDeliveryTime = new TimeOnly(8, 0);
        PreferredTimezone = null;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public bool InAppEnabled { get; private set; }
    public bool MobileEnabled { get; private set; }
    public bool DailyEnabled { get; private set; }
    public bool WeeklyEnabled { get; private set; }
    public TimeOnly PreferredDeliveryTime { get; private set; }
    public string? PreferredTimezone { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;

    public void Update(bool inAppEnabled, bool mobileEnabled, bool dailyEnabled, bool weeklyEnabled)
    {
        Update(inAppEnabled, mobileEnabled, dailyEnabled, weeklyEnabled, PreferredDeliveryTime, PreferredTimezone);
    }

    public void Update(bool inAppEnabled, bool mobileEnabled, bool dailyEnabled, bool weeklyEnabled, TimeOnly preferredDeliveryTime, string? preferredTimezone)
    {
        InAppEnabled = inAppEnabled;
        MobileEnabled = mobileEnabled;
        DailyEnabled = dailyEnabled;
        WeeklyEnabled = weeklyEnabled;
        PreferredDeliveryTime = preferredDeliveryTime;
        PreferredTimezone = NormalizeOptional(preferredTimezone, nameof(preferredTimezone), 100);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
}

public sealed class UserBriefingPreference : ICompanyOwnedEntity
{
    private UserBriefingPreference()
    {
    }

    public UserBriefingPreference(
        Guid id,
        Guid companyId,
        Guid userId,
        BriefingDeliveryFrequency deliveryFrequency,
        IEnumerable<string>? includedFocusAreas,
        BriefingSectionPriorityCategory priorityThreshold)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        CreatedUtc = DateTime.UtcNow;
        Update(deliveryFrequency, includedFocusAreas, priorityThreshold);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public BriefingDeliveryFrequency DeliveryFrequency { get; private set; }
    public List<string> IncludedFocusAreas { get; private set; } = [];
    public BriefingSectionPriorityCategory PriorityThreshold { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;

    public void Update(
        BriefingDeliveryFrequency deliveryFrequency,
        IEnumerable<string>? includedFocusAreas,
        BriefingSectionPriorityCategory priorityThreshold)
    {
        EnsureValidThreshold(priorityThreshold);
        DeliveryFrequency = deliveryFrequency;
        IncludedFocusAreas = BriefingFocusAreaValues.NormalizeOrThrow(includedFocusAreas).ToList();
        PriorityThreshold = priorityThreshold;
        UpdatedUtc = DateTime.UtcNow;
    }

    public BriefingPreferenceSnapshot ToSnapshot(BriefingPreferenceSource source) =>
        new(CompanyId, UserId, source, DeliveryFrequency, IncludedFocusAreas, PriorityThreshold, Id, UpdatedUtc);

    private static void EnsureValidThreshold(BriefingSectionPriorityCategory threshold)
    {
        if (!Enum.IsDefined(threshold) || threshold == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, BriefingPreferenceErrorCodes.InvalidPriorityThreshold);
        }
    }
}

public sealed class TenantBriefingDefault : ICompanyOwnedEntity
{
    private TenantBriefingDefault()
    {
    }

    public TenantBriefingDefault(
        Guid id,
        Guid companyId,
        BriefingDeliveryFrequency deliveryFrequency,
        IEnumerable<string>? includedFocusAreas,
        BriefingSectionPriorityCategory priorityThreshold)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CreatedUtc = DateTime.UtcNow;
        Update(deliveryFrequency, includedFocusAreas, priorityThreshold);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public BriefingDeliveryFrequency DeliveryFrequency { get; private set; }
    public List<string> IncludedFocusAreas { get; private set; } = [];
    public BriefingSectionPriorityCategory PriorityThreshold { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Update(
        BriefingDeliveryFrequency deliveryFrequency,
        IEnumerable<string>? includedFocusAreas,
        BriefingSectionPriorityCategory priorityThreshold)
    {
        EnsureValidThreshold(priorityThreshold);
        DeliveryFrequency = deliveryFrequency;
        IncludedFocusAreas = BriefingFocusAreaValues.NormalizeOrThrow(includedFocusAreas).ToList();
        PriorityThreshold = priorityThreshold;
        UpdatedUtc = DateTime.UtcNow;
    }

    public BriefingPreferenceSnapshot ToSnapshot(Guid userId, BriefingPreferenceSource source = BriefingPreferenceSource.TenantDefault) =>
        new(CompanyId, userId, source, DeliveryFrequency, IncludedFocusAreas, PriorityThreshold, Id, UpdatedUtc);

    public static BriefingPreferenceSnapshot CreateSystemDefault(Guid companyId, Guid userId) =>
        new(
            companyId,
            userId,
            BriefingPreferenceSource.SystemDefault,
            BriefingDeliveryFrequency.DailyAndWeekly,
            BriefingFocusAreaValues.AllowedValues,
            BriefingSectionPriorityCategory.Informational,
            null,
            null);

    private static void EnsureValidThreshold(BriefingSectionPriorityCategory threshold)
    {
        if (!Enum.IsDefined(threshold) || threshold == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, BriefingPreferenceErrorCodes.InvalidPriorityThreshold);
        }
    }
}

public sealed record BriefingPreferenceSnapshot(
    Guid CompanyId,
    Guid UserId,
    BriefingPreferenceSource Source,
    BriefingDeliveryFrequency DeliveryFrequency,
    IReadOnlyList<string> IncludedFocusAreas,
    BriefingSectionPriorityCategory PriorityThreshold,
    Guid? PreferenceId,
    DateTime? PreferenceUpdatedUtc)
{
    public Dictionary<string, JsonNode?> ToMetadata() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = JsonValue.Create(Source.ToStorageValue()),
            ["deliveryFrequency"] = JsonValue.Create(DeliveryFrequency.ToStorageValue()),
            ["includedFocusAreas"] = JsonSerializer.SerializeToNode(IncludedFocusAreas),
            ["priorityThreshold"] = JsonValue.Create(PriorityThreshold.ToStorageValue()),
            ["preferenceId"] = JsonValue.Create(PreferenceId),
            ["preferenceUpdatedUtc"] = JsonValue.Create(PreferenceUpdatedUtc)
        };
}

public sealed class CompanyNotification : ICompanyOwnedEntity
{
    private const int TitleMaxLength = 200;
    private const int BodyMaxLength = 4000;

    private CompanyNotification()
    {
    }

    public CompanyNotification(
        Guid id,
        Guid companyId,
        Guid userId,
        CompanyNotificationType type,
        CompanyNotificationPriority priority,
        string title,
        string body,
        string relatedEntityType,
        Guid? relatedEntityId,
        string? actionUrl,
        string? metadataJson,
        string dedupeKey,
        Guid? briefingId = null,
        CompanyNotificationChannel channel = CompanyNotificationChannel.InApp)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Company and user ids are required.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        BriefingId = briefingId;
        Channel = channel;
        Type = type;
        Priority = priority;
        Title = string.IsNullOrWhiteSpace(title) ? "Executive briefing" : title.Trim()[..Math.Min(title.Trim().Length, TitleMaxLength)];
        Body = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim()[..Math.Min(body.Trim().Length, BodyMaxLength)];
        RelatedEntityType = string.IsNullOrWhiteSpace(relatedEntityType) ? type.ToStorageValue() : relatedEntityType.Trim()[..Math.Min(relatedEntityType.Trim().Length, 100)];
        RelatedEntityId = relatedEntityId;
        ActionUrl = string.IsNullOrWhiteSpace(actionUrl) ? null : actionUrl.Trim()[..Math.Min(actionUrl.Trim().Length, 2048)];
        MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson.Trim();
        DedupeKey = string.IsNullOrWhiteSpace(dedupeKey) ? $"{companyId:N}:{userId:N}:{type.ToStorageValue()}:{relatedEntityType}:{relatedEntityId:N}" : dedupeKey.Trim()[..Math.Min(dedupeKey.Trim().Length, 300)];
        Status = CompanyNotificationStatus.Unread;
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? BriefingId { get; private set; }
    public CompanyNotificationChannel Channel { get; private set; }
    public CompanyNotificationType Type { get; private set; }
    public CompanyNotificationPriority Priority { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string RelatedEntityType { get; private set; } = null!;
    public Guid? RelatedEntityId { get; private set; }
    public string? ActionUrl { get; private set; }
    public string MetadataJson { get; private set; } = "{}";
    public string DedupeKey { get; private set; } = null!;
    public CompanyNotificationStatus Status { get; private set; }
    public DateTime? ReadUtc { get; private set; }
    public DateTime? ActionedUtc { get; private set; }
    public Guid? ActionedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;
    public CompanyBriefing? Briefing { get; private set; }

    public void MarkRead()
    {
        var now = DateTime.UtcNow;
        if (Status == CompanyNotificationStatus.Actioned)
        {
            ReadUtc ??= now;
            return;
        }

        Status = CompanyNotificationStatus.Read;
        ReadUtc ??= now;
    }

    public void MarkUnread()
    {
        if (Status == CompanyNotificationStatus.Actioned)
        {
            throw new InvalidOperationException("Actioned notifications cannot be marked unread.");
        }

        Status = CompanyNotificationStatus.Unread;
        ReadUtc = null;
    }

    public void MarkActioned(Guid actionedByUserId)
    {
        if (actionedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Actioned by user id is required.", nameof(actionedByUserId));
        }

        var now = DateTime.UtcNow;
        Status = CompanyNotificationStatus.Actioned;
        ReadUtc ??= now;
        ActionedUtc ??= now;
        ActionedByUserId ??= actionedByUserId;
    }
}

public sealed class CompanyBriefingSeverityRule : ICompanyOwnedEntity
{
    private const int RuleCodeMaxLength = 100;
    private const int SectionTypeMaxLength = 64;
    private const int EntityTypeMaxLength = 64;
    private const int ConditionKeyMaxLength = 100;
    private const int ConditionValueMaxLength = 100;

    private CompanyBriefingSeverityRule()
    {
    }

    public CompanyBriefingSeverityRule(
        Guid id,
        Guid companyId,
        string ruleCode,
        string sectionType,
        string entityType,
        string conditionKey,
        string conditionValue,
        BriefingSectionPriorityCategory priorityCategory,
        int priorityScore,
        int sortOrder = 0)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (priorityScore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priorityScore), "Priority score must be non-negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RuleCode = NormalizeRequired(ruleCode, nameof(ruleCode), RuleCodeMaxLength);
        SectionType = NormalizeRequired(sectionType, nameof(sectionType), SectionTypeMaxLength);
        EntityType = NormalizeRequired(entityType, nameof(entityType), EntityTypeMaxLength);
        ConditionKey = NormalizeRequired(conditionKey, nameof(conditionKey), ConditionKeyMaxLength);
        ConditionValue = NormalizeRequired(conditionValue, nameof(conditionValue), ConditionValueMaxLength);
        PriorityCategory = priorityCategory;
        PriorityScore = priorityScore;
        SortOrder = sortOrder;
        Status = BriefingSeverityRuleStatus.Active;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string RuleCode { get; private set; } = null!;
    public string SectionType { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string ConditionKey { get; private set; } = null!;
    public string ConditionValue { get; private set; } = null!;
    public BriefingSectionPriorityCategory PriorityCategory { get; private set; }
    public int PriorityScore { get; private set; }
    public int SortOrder { get; private set; }
    public BriefingSeverityRuleStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }
}
