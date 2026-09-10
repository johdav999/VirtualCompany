namespace VirtualCompany.Domain.Entities;

/// <summary>A company/audience/content keyed object; SQL contains metadata, never PCM.</summary>
public sealed class SalesNarrationAsset : ICompanyOwnedEntity
{
    public const string Pending = "pending";
    public const string Generating = "generating";
    public const string Ready = "ready";
    public const string Rejected = "rejected";
    public const string NeedsReview = "needs_review";
    public const string Deleted = "deleted";
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CacheKey { get; set; } = "";
    public string Status { get; set; } = Pending;
    public string? StorageKey { get; set; }
    public string? AudioHash { get; set; }
    public string? TranscriptHash { get; set; }
    public string MediaFormat { get; set; } = "audio/wav;pcm16;24000;mono";
    public long Bytes { get; set; }
    public int DurationMilliseconds { get; set; }
    public int AttemptCount { get; set; }
    public long PreviewCount { get; set; }
    public long ReusedMilliseconds { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public string? FailureCode { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public long Version { get; set; } = 1;
}

