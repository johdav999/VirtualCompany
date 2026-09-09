using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesPresentationDeck : ICompanyOwnedEntity
{
    private SalesPresentationDeck() { }

    public SalesPresentationDeck(
        Guid id,
        Guid companyId,
        Guid sessionId,
        Guid agentId,
        int version,
        string title,
        string originalFileName,
        string? contentType,
        long fileSizeBytes,
        string contentHash,
        string storageKey,
        string? storageUrl,
        Guid uploadedByUserId,
        DateTime createdUtc)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(sessionId, nameof(sessionId));
        EnsureId(agentId, nameof(agentId));
        EnsureId(uploadedByUserId, nameof(uploadedByUserId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (fileSizeBytes < 1) throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        AgentId = agentId;
        Version = version;
        Title = Required(title, nameof(title), 200);
        OriginalFileName = Required(originalFileName, nameof(originalFileName), 260);
        ContentType = Optional(contentType, nameof(contentType), 200);
        FileSizeBytes = fileSizeBytes;
        ContentHash = Required(contentHash, nameof(contentHash), 64).ToLowerInvariant();
        StorageKey = Required(storageKey, nameof(storageKey), 1000);
        StorageUrl = Optional(storageUrl, nameof(storageUrl), 2000);
        UploadedByUserId = uploadedByUserId;
        Status = SalesPresentationDeckStatus.PendingScan;
        ProcessingVersion = 1;
        CreatedUtc = Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
        ConcurrencyVersion = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid AgentId { get; private set; }
    public int Version { get; private set; }
    public string Title { get; private set; } = null!;
    public string OriginalFileName { get; private set; } = null!;
    public string? ContentType { get; private set; }
    public long FileSizeBytes { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string? StorageUrl { get; private set; }
    public SalesPresentationDeckStatus Status { get; private set; }
    public int ProcessingVersion { get; private set; }
    public int ProcessingAttemptCount { get; private set; }
    public int SlideCount { get; private set; }
    public string? RendererName { get; private set; }
    public string? RendererVersion { get; private set; }
    public string? AnimationHandling { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public bool CanRetry { get; private set; }
    public bool IsActive { get; private set; }
    public int BriefVersion { get; private set; }
    public DateTime? BriefRegenerationRequestedUtc { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ProcessingStartedUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public DateTime? FailedUtc { get; private set; }
    public DateTime? ActivatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public Company Company { get; private set; } = null!;
    public SalesMeetingSession Session { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;

    public void BeginProcessing(DateTime nowUtc, TimeSpan staleAfter)
    {
        var now = Utc(nowUtc);
        var stale = Status == SalesPresentationDeckStatus.Processing &&
                    ProcessingStartedUtc.HasValue &&
                    ProcessingStartedUtc.Value <= now.Subtract(staleAfter);
        if (Status != SalesPresentationDeckStatus.PendingScan && !stale)
            throw new InvalidOperationException("The presentation deck is not ready for processing.");

        Status = SalesPresentationDeckStatus.Processing;
        ProcessingAttemptCount++;
        ProcessingStartedUtc = now;
        FailureCode = null;
        FailureSummary = null;
        CanRetry = false;
        FailedUtc = null;
        Touch(now);
    }

    public void MarkProcessed(
        int slideCount,
        string rendererName,
        string rendererVersion,
        string animationHandling,
        int briefVersion,
        DateTime nowUtc)
    {
        if (Status != SalesPresentationDeckStatus.Processing)
            throw new InvalidOperationException("Only a processing deck can be completed.");
        if (slideCount < 1) throw new ArgumentOutOfRangeException(nameof(slideCount));
        if (briefVersion < 1) throw new ArgumentOutOfRangeException(nameof(briefVersion));
        var now = Utc(nowUtc);
        SlideCount = slideCount;
        RendererName = Required(rendererName, nameof(rendererName), 100);
        RendererVersion = Required(rendererVersion, nameof(rendererVersion), 50);
        AnimationHandling = Required(animationHandling, nameof(animationHandling), 100);
        BriefVersion = briefVersion;
        Status = SalesPresentationDeckStatus.Processed;
        ProcessedUtc = now;
        BriefRegenerationRequestedUtc = null;
        Touch(now);
    }

    public void MarkFailed(string code, string summary, bool canRetry, bool blocked, DateTime nowUtc)
    {
        var now = Utc(nowUtc);
        Status = blocked ? SalesPresentationDeckStatus.Blocked : SalesPresentationDeckStatus.Failed;
        FailureCode = Required(code, nameof(code), 100);
        FailureSummary = Required(summary, nameof(summary), 1000);
        CanRetry = canRetry && !blocked;
        FailedUtc = now;
        BriefRegenerationRequestedUtc = null;
        Touch(now);
    }

    public void QueueRetry(DateTime nowUtc)
    {
        if (Status != SalesPresentationDeckStatus.Failed || !CanRetry)
            throw new InvalidOperationException("This presentation deck cannot be retried.");
        Status = SalesPresentationDeckStatus.PendingScan;
        CanRetry = false;
        FailureCode = null;
        FailureSummary = null;
        FailedUtc = null;
        Touch(Utc(nowUtc));
    }

    public void Activate(DateTime nowUtc)
    {
        if (Status != SalesPresentationDeckStatus.Processed)
            throw new InvalidOperationException("Only a processed presentation deck can be activated.");
        IsActive = true;
        ActivatedUtc = Utc(nowUtc);
        Touch(ActivatedUtc.Value);
    }

    public void Deactivate(DateTime nowUtc)
    {
        if (!IsActive) return;
        IsActive = false;
        ActivatedUtc = null;
        Touch(Utc(nowUtc));
    }

    public void RequestBriefRegeneration(DateTime nowUtc)
    {
        if (Status != SalesPresentationDeckStatus.Processed)
            throw new InvalidOperationException("A brief can only be regenerated for a processed deck.");
        BriefRegenerationRequestedUtc = Utc(nowUtc);
        Touch(BriefRegenerationRequestedUtc.Value);
    }

    public void CompleteBriefRegeneration(int briefVersion, DateTime nowUtc)
    {
        if (!BriefRegenerationRequestedUtc.HasValue)
            throw new InvalidOperationException("Brief regeneration was not requested.");
        if (briefVersion <= BriefVersion) throw new ArgumentOutOfRangeException(nameof(briefVersion));
        BriefVersion = briefVersion;
        BriefRegenerationRequestedUtc = null;
        Touch(Utc(nowUtc));
    }

    private void Touch(DateTime now)
    {
        UpdatedUtc = now;
        ConcurrencyVersion++;
    }

    private static void EnsureId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException($"{name} is required.", name);
    }

    private static string Required(string? value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > max) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }

    private static string? Optional(string? value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, max);

    private static DateTime Utc(DateTime value) => value == default
        ? throw new ArgumentException("A UTC timestamp is required.")
        : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
