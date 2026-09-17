using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesPresentationPresetAsset : ICompanyOwnedEntity
{
    private SalesPresentationPresetAsset() { }
    public SalesPresentationPresetAsset(Guid id, Guid companyId, Guid presetVersionId, string originalFileName, string? contentType,
        long fileSizeBytes, string contentHash, string storageKey, string? storageUrl, Guid uploadedByUserId, DateTime nowUtc)
    {
        SalesPresentationPreset.RequiredId(companyId, nameof(companyId)); SalesPresentationPreset.RequiredId(presetVersionId, nameof(presetVersionId));
        SalesPresentationPreset.RequiredId(uploadedByUserId, nameof(uploadedByUserId)); if (fileSizeBytes < 1) throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; PresetVersionId = presetVersionId;
        OriginalFileName = SalesPresentationPreset.Required(originalFileName, 260); ContentType = SalesPresentationPreset.Optional(contentType, 200);
        FileSizeBytes = fileSizeBytes; ContentHash = SalesPresentationPreset.Required(contentHash, 64).ToLowerInvariant();
        StorageKey = SalesPresentationPreset.Required(storageKey, 1000); StorageUrl = SalesPresentationPreset.Optional(storageUrl, 2000);
        UploadedByUserId = uploadedByUserId; Status = SalesPresentationPresetAssetStatus.PendingScan; ProcessingVersion = 1;
        CreatedUtc = UpdatedUtc = SalesPresentationPreset.Utc(nowUtc); ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid PresetVersionId { get; private set; }
    public string OriginalFileName { get; private set; } = null!; public string? ContentType { get; private set; } public long FileSizeBytes { get; private set; }
    public string ContentHash { get; private set; } = null!; public string StorageKey { get; private set; } = null!; public string? StorageUrl { get; private set; }
    public SalesPresentationPresetAssetStatus Status { get; private set; } public int ProcessingVersion { get; private set; } public int ProcessingAttemptCount { get; private set; }
    public int SlideCount { get; private set; } public string? RendererName { get; private set; } public string? RendererVersion { get; private set; }
    public string? AnimationHandling { get; private set; } public string? FailureCode { get; private set; } public string? FailureSummary { get; private set; }
    public bool CanRetry { get; private set; } public Guid UploadedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public DateTime? ProcessingStartedUtc { get; private set; } public DateTime? ProcessedUtc { get; private set; }
    public DateTime? FailedUtc { get; private set; } public long ConcurrencyVersion { get; private set; }
    public SalesPresentationPresetVersion PresetVersion { get; private set; } = null!; public ICollection<SalesPresentationPresetSlide> Slides { get; } = new List<SalesPresentationPresetSlide>();
    public void BeginProcessing(DateTime nowUtc, TimeSpan staleAfter) { var now = SalesPresentationPreset.Utc(nowUtc); var stale = Status == SalesPresentationPresetAssetStatus.Processing && ProcessingStartedUtc <= now.Subtract(staleAfter); if (Status != SalesPresentationPresetAssetStatus.PendingScan && !stale) throw new InvalidOperationException("Asset is not ready for processing."); Status = SalesPresentationPresetAssetStatus.Processing; ProcessingAttemptCount++; ProcessingStartedUtc = now; FailureCode = FailureSummary = null; CanRetry = false; Touch(now); }
    public void MarkProcessed(int count, string renderer, string rendererVersion, string animation, DateTime nowUtc) { if (Status != SalesPresentationPresetAssetStatus.Processing || count < 1) throw new InvalidOperationException("Only a processing asset with slides can complete."); SlideCount = count; RendererName = SalesPresentationPreset.Required(renderer, 100); RendererVersion = SalesPresentationPreset.Required(rendererVersion, 50); AnimationHandling = SalesPresentationPreset.Required(animation, 100); Status = SalesPresentationPresetAssetStatus.Processed; ProcessedUtc = SalesPresentationPreset.Utc(nowUtc); Touch(nowUtc); }
    public void MarkFailed(string code, string summary, bool canRetry, bool blocked, DateTime nowUtc) { Status = blocked ? SalesPresentationPresetAssetStatus.Blocked : SalesPresentationPresetAssetStatus.Failed; FailureCode = SalesPresentationPreset.Required(code, 100); FailureSummary = SalesPresentationPreset.Required(summary, 1000); CanRetry = canRetry && !blocked; FailedUtc = SalesPresentationPreset.Utc(nowUtc); Touch(nowUtc); }
    public void QueueRetry(DateTime nowUtc) { if (Status != SalesPresentationPresetAssetStatus.Failed || !CanRetry) throw new InvalidOperationException("Asset cannot be retried."); Status = SalesPresentationPresetAssetStatus.PendingScan; FailureCode = FailureSummary = null; CanRetry = false; FailedUtc = null; Touch(nowUtc); }
    private void Touch(DateTime nowUtc) { UpdatedUtc = SalesPresentationPreset.Utc(nowUtc); ConcurrencyVersion++; }
}
