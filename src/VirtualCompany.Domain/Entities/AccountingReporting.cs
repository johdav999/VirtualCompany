namespace VirtualCompany.Domain.Entities;

public static class AccountingExportStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static bool IsTerminal(string status) => status is Completed or Failed;
}

public static class AccountingPeriodHistoryActions
{
    public const string ClosedAndLocked = "closed_and_locked";
    public const string Reopened = "reopened";
}

public sealed class AccountingTaxReview : ICompanyOwnedEntity
{
    private AccountingTaxReview() { }

    public AccountingTaxReview(Guid id, Guid companyId, Guid fiscalPeriodId, string summaryJson,
        string checksum, Guid reviewedByUserId, DateTime reviewedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        FiscalPeriodId = fiscalPeriodId == Guid.Empty ? throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId)) : fiscalPeriodId;
        SummaryJson = Required(summaryJson, nameof(summaryJson), 64000);
        Checksum = Required(checksum, nameof(checksum), 64).ToLowerInvariant();
        ReviewedByUserId = reviewedByUserId == Guid.Empty ? throw new ArgumentException("ReviewedByUserId is required.", nameof(reviewedByUserId)) : reviewedByUserId;
        ReviewedUtc = EntityTimestampNormalizer.NormalizeUtc(reviewedUtc, nameof(reviewedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string SummaryJson { get; private set; } = null!;
    public string Checksum { get; private set; } = null!;
    public Guid ReviewedByUserId { get; private set; }
    public DateTime ReviewedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;

    public void Replace(string summaryJson, string checksum, Guid actorUserId, DateTime reviewedUtc)
    {
        SummaryJson = Required(summaryJson, nameof(summaryJson), 64000);
        Checksum = Required(checksum, nameof(checksum), 64).ToLowerInvariant();
        ReviewedByUserId = actorUserId == Guid.Empty ? throw new ArgumentException("Actor is required.", nameof(actorUserId)) : actorUserId;
        ReviewedUtc = EntityTimestampNormalizer.NormalizeUtc(reviewedUtc, nameof(reviewedUtc));
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }
}

public sealed class AccountingPeriodHistory : ICompanyOwnedEntity
{
    private AccountingPeriodHistory() { }

    public AccountingPeriodHistory(Guid id, Guid companyId, Guid fiscalPeriodId, string action,
        Guid actorUserId, string reason, string? snapshotChecksum, DateTime occurredUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        FiscalPeriodId = fiscalPeriodId == Guid.Empty ? throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId)) : fiscalPeriodId;
        Action = action is AccountingPeriodHistoryActions.ClosedAndLocked or AccountingPeriodHistoryActions.Reopened
            ? action : throw new ArgumentOutOfRangeException(nameof(action));
        ActorUserId = actorUserId == Guid.Empty ? throw new ArgumentException("ActorUserId is required.", nameof(actorUserId)) : actorUserId;
        Reason = Required(reason, nameof(reason), 1000);
        SnapshotChecksum = Optional(snapshotChecksum, 64)?.ToLowerInvariant();
        OccurredUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string Action { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public string Reason { get; private set; } = null!;
    public string? SnapshotChecksum { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;

    private static string Required(string value, string name, int maxLength) =>
        Optional(value, maxLength) ?? throw new ArgumentException($"{name} is required.", name);
    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public sealed class AccountingExportJob : ICompanyOwnedEntity
{
    private AccountingExportJob() { }

    public AccountingExportJob(Guid id, Guid companyId, Guid fiscalPeriodId, Guid requestedByUserId,
        string idempotencyKey, DateTime requestedUtc, DateTime expiresUtc)
    {
        if (expiresUtc <= requestedUtc) throw new ArgumentOutOfRangeException(nameof(expiresUtc));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        FiscalPeriodId = fiscalPeriodId == Guid.Empty ? throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId)) : fiscalPeriodId;
        RequestedByUserId = requestedByUserId == Guid.Empty ? throw new ArgumentException("RequestedByUserId is required.", nameof(requestedByUserId)) : requestedByUserId;
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 200);
        Status = AccountingExportStatuses.Queued;
        RequestedUtc = EntityTimestampNormalizer.NormalizeUtc(requestedUtc, nameof(requestedUtc));
        ExpiresUtc = EntityTimestampNormalizer.NormalizeUtc(expiresUtc, nameof(expiresUtc));
        UpdatedUtc = RequestedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public string? Checksum { get; private set; }
    public string? FileName { get; private set; }
    public string? MediaType { get; private set; }
    public long? ContentLength { get; private set; }
    public byte[]? Content { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;

    public void Start(DateTime utcNow)
    {
        Status = AccountingExportStatuses.Running;
        AttemptCount++;
        StartedUtc = EntityTimestampNormalizer.NormalizeUtc(utcNow, nameof(utcNow));
        NextAttemptUtc = null;
        FailureCode = null;
        FailureSummary = null;
        UpdatedUtc = StartedUtc.Value;
    }

    public void Complete(byte[] content, string checksum, string fileName, DateTime utcNow)
    {
        Content = content is { Length: > 0 } ? content : throw new ArgumentException("Export content is required.", nameof(content));
        Checksum = Required(checksum, nameof(checksum), 64).ToLowerInvariant();
        FileName = Required(fileName, nameof(fileName), 180);
        MediaType = "application/json";
        ContentLength = content.LongLength;
        Status = AccountingExportStatuses.Completed;
        CompletedUtc = EntityTimestampNormalizer.NormalizeUtc(utcNow, nameof(utcNow));
        UpdatedUtc = CompletedUtc.Value;
    }

    public void Retry(string code, string summary, DateTime nextAttemptUtc, DateTime utcNow)
    {
        Status = AccountingExportStatuses.Queued;
        FailureCode = Required(code, nameof(code), 100);
        FailureSummary = Required(summary, nameof(summary), 1000);
        NextAttemptUtc = EntityTimestampNormalizer.NormalizeUtc(nextAttemptUtc, nameof(nextAttemptUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(utcNow, nameof(utcNow));
    }

    public void Fail(string code, string summary, DateTime utcNow)
    {
        Status = AccountingExportStatuses.Failed;
        FailureCode = Required(code, nameof(code), 100);
        FailureSummary = Required(summary, nameof(summary), 1000);
        CompletedUtc = EntityTimestampNormalizer.NormalizeUtc(utcNow, nameof(utcNow));
        UpdatedUtc = CompletedUtc.Value;
    }

    public long ExpireContent(DateTime utcNow)
    {
        if (Status != AccountingExportStatuses.Completed || Content is null)
        {
            return 0;
        }

        var releasedBytes = Content.LongLength;
        Content = null;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(utcNow, nameof(utcNow));
        return releasedBytes;
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }
}
