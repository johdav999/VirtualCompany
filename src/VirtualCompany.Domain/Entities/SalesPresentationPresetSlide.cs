namespace VirtualCompany.Domain.Entities;

public sealed class SalesPresentationPresetSlide : ICompanyOwnedEntity
{
    private SalesPresentationPresetSlide() { }
    public SalesPresentationPresetSlide(Guid id, Guid companyId, Guid assetId, int processingVersion, int slideNumber, string? title,
        string extractedText, string? speakerNotes, string imageStorageKey, string? imageStorageUrl, int width, int height,
        long sourceWidth, long sourceHeight, string contentHash, string objective, string baselineTalkingPoints,
        int durationSeconds, string transition, DateTime nowUtc)
    {
        SalesPresentationPreset.RequiredId(companyId, nameof(companyId)); SalesPresentationPreset.RequiredId(assetId, nameof(assetId));
        if (processingVersion < 1 || slideNumber < 1 || width < 1 || height < 1 || durationSeconds < 1) throw new ArgumentOutOfRangeException(nameof(slideNumber));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; AssetId = assetId; ProcessingVersion = processingVersion; SlideNumber = slideNumber;
        Title = SalesPresentationPreset.Optional(title, 500);
        var normalizedText = extractedText?.Trim() ?? string.Empty;
        if (normalizedText.Length > 16000) throw new ArgumentOutOfRangeException(nameof(extractedText));
        ExtractedText = normalizedText; SpeakerNotes = SalesPresentationPreset.Optional(speakerNotes, 16000);
        ImageStorageKey = SalesPresentationPreset.Required(imageStorageKey, 1000); ImageStorageUrl = SalesPresentationPreset.Optional(imageStorageUrl, 2000);
        ImageWidthPixels = width; ImageHeightPixels = height; SourceWidthEmus = sourceWidth; SourceHeightEmus = sourceHeight;
        ContentHash = SalesPresentationPreset.Required(contentHash, 64).ToLowerInvariant(); Objective = SalesPresentationPreset.Required(objective, 1000);
        BaselineTalkingPoints = SalesPresentationPreset.Required(baselineTalkingPoints, 8000); ExpectedDurationSeconds = durationSeconds;
        TransitionText = SalesPresentationPreset.Required(transition, 1000); CreatedUtc = SalesPresentationPreset.Utc(nowUtc);
    }
    public void UpdateSpeakerNotes(string? notes)
    {
        SpeakerNotes = SalesPresentationPreset.Optional(notes, 16000);
        var baseline = SpeakerNotes ?? (string.IsNullOrWhiteSpace(ExtractedText) ? Objective : ExtractedText);
        BaselineTalkingPoints = baseline.Length <= 8000 ? baseline : baseline[..8000];
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid AssetId { get; private set; }
    public int ProcessingVersion { get; private set; } public int SlideNumber { get; private set; } public string? Title { get; private set; }
    public string ExtractedText { get; private set; } = null!; public string? SpeakerNotes { get; private set; } public string ImageStorageKey { get; private set; } = null!;
    public string? ImageStorageUrl { get; private set; } public int ImageWidthPixels { get; private set; } public int ImageHeightPixels { get; private set; }
    public long SourceWidthEmus { get; private set; } public long SourceHeightEmus { get; private set; } public string ContentHash { get; private set; } = null!;
    public string Objective { get; private set; } = null!; public string BaselineTalkingPoints { get; private set; } = null!; public int ExpectedDurationSeconds { get; private set; }
    public string TransitionText { get; private set; } = null!; public DateTime CreatedUtc { get; private set; } public SalesPresentationPresetAsset Asset { get; private set; } = null!;
}
