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
    public CompanyBriefingStatus Status { get; private set; }
    public Guid? MessageId { get; private set; }
    public DateTime GeneratedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Message? Message { get; private set; }

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