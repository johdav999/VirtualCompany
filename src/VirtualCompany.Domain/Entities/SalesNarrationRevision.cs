namespace VirtualCompany.Domain.Entities;

/// <summary>Immutable script manifest; approval and revocation are explicit release decisions.</summary>
public sealed class SalesNarrationRevision : ICompanyOwnedEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SessionId { get; set; }
    public Guid DeckId { get; set; }
    public int DeckVersion { get; set; }
    public int ProcessingVersion { get; set; }
    public Guid AudienceId { get; set; }
    public string AudienceHash { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public string Language { get; set; } = "";
    public string Voice { get; set; } = "";
    public string Model { get; set; } = "";
    public string ConfigurationVersion { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
    public long Version { get; set; } = 1;
}

