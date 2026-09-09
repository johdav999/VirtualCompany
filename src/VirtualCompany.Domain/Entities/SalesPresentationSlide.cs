using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesPresentationSlide : ICompanyOwnedEntity
{
    private SalesPresentationSlide() { }

    public SalesPresentationSlide(
        Guid id, Guid companyId, Guid deckId, int processingVersion, int slideNumber,
        string? title, string extractedText, string? speakerNotes,
        string imageStorageKey, string? imageStorageUrl, int imageWidthPixels, int imageHeightPixels,
        long sourceWidthEmus, long sourceHeightEmus, string contentHash,
        string objective, int expectedDurationSeconds, string transitionText, DateTime createdUtc)
    {
        if (companyId == Guid.Empty || deckId == Guid.Empty) throw new ArgumentException("Company and deck are required.");
        if (processingVersion < 1 || slideNumber < 1) throw new ArgumentOutOfRangeException(nameof(slideNumber));
        if (imageWidthPixels < 1 || imageHeightPixels < 1) throw new ArgumentOutOfRangeException(nameof(imageWidthPixels));
        if (expectedDurationSeconds < 1) throw new ArgumentOutOfRangeException(nameof(expectedDurationSeconds));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DeckId = deckId;
        ProcessingVersion = processingVersion;
        SlideNumber = slideNumber;
        Title = Optional(title, 500);
        ExtractedText = Bounded(extractedText, nameof(extractedText), 16000);
        SpeakerNotes = Optional(speakerNotes, 16000);
        ImageStorageKey = Required(imageStorageKey, nameof(imageStorageKey), 1000);
        ImageStorageUrl = Optional(imageStorageUrl, 2000);
        ImageWidthPixels = imageWidthPixels;
        ImageHeightPixels = imageHeightPixels;
        SourceWidthEmus = sourceWidthEmus;
        SourceHeightEmus = sourceHeightEmus;
        ContentHash = Required(contentHash, nameof(contentHash), 64).ToLowerInvariant();
        Objective = Required(objective, nameof(objective), 1000);
        ExpectedDurationSeconds = expectedDurationSeconds;
        TransitionText = Required(transitionText, nameof(transitionText), 1000);
        Status = SalesPresentationSlideStatus.Processed;
        CreatedUtc = createdUtc.Kind == DateTimeKind.Utc ? createdUtc : createdUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DeckId { get; private set; }
    public int ProcessingVersion { get; private set; }
    public int SlideNumber { get; private set; }
    public string? Title { get; private set; }
    public string ExtractedText { get; private set; } = null!;
    public string? SpeakerNotes { get; private set; }
    public string ImageStorageKey { get; private set; } = null!;
    public string? ImageStorageUrl { get; private set; }
    public int ImageWidthPixels { get; private set; }
    public int ImageHeightPixels { get; private set; }
    public long SourceWidthEmus { get; private set; }
    public long SourceHeightEmus { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public string Objective { get; private set; } = null!;
    public int ExpectedDurationSeconds { get; private set; }
    public string TransitionText { get; private set; } = null!;
    public SalesPresentationSlideStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SalesPresentationDeck Deck { get; private set; } = null!;

    private static string Required(string? value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > max) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }

    private static string Bounded(string? value, string name, int max)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > max) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }

    private static string? Optional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
