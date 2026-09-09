using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingArtifact : ICompanyOwnedEntity
{
    private SalesMeetingArtifact() { }

    public SalesMeetingArtifact(
        Guid id, Guid companyId, Guid sessionId, Guid deckId, Guid? slideId,
        int artifactVersion, SalesMeetingArtifactType artifactType, string section,
        int order, string content, SalesMeetingArtifactClassification classification,
        string? sourceId, Guid? aiRunId, DateTime createdUtc)
    {
        if (companyId == Guid.Empty || sessionId == Guid.Empty || deckId == Guid.Empty)
            throw new ArgumentException("Company, session, and deck are required.");
        if (slideId == Guid.Empty) throw new ArgumentException("SlideId cannot be empty.", nameof(slideId));
        if (artifactVersion < 1 || order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        _ = artifactType.ToStorageValue();
        _ = classification.ToStorageValue();
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        DeckId = deckId;
        SlideId = slideId;
        ArtifactVersion = artifactVersion;
        ArtifactType = artifactType;
        Section = Required(section, nameof(section), 100);
        Order = order;
        Content = Required(content, nameof(content), 4000);
        Classification = classification;
        SourceId = Optional(sourceId, 500);
        AiRunId = aiRunId;
        CreatedUtc = createdUtc.Kind == DateTimeKind.Utc ? createdUtc : createdUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid DeckId { get; private set; }
    public Guid? SlideId { get; private set; }
    public int ArtifactVersion { get; private set; }
    public SalesMeetingArtifactType ArtifactType { get; private set; }
    public string Section { get; private set; } = null!;
    public int Order { get; private set; }
    public string Content { get; private set; } = null!;
    public SalesMeetingArtifactClassification Classification { get; private set; }
    public string? SourceId { get; private set; }
    public Guid? AiRunId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public SalesPresentationDeck Deck { get; private set; } = null!;
    public SalesPresentationSlide? Slide { get; private set; }

    private static string Required(string? value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > max) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }

    private static string? Optional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
