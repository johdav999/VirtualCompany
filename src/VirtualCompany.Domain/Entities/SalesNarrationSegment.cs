namespace VirtualCompany.Domain.Entities;

public sealed class SalesNarrationSegment : ICompanyOwnedEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid SourceSlideId { get; set; }
    public int SlideNumber { get; set; }
    public int TalkingPoint { get; set; }
    public string SourceHash { get; set; } = "";
    public string SourceText { get; set; } = "";
    public string Script { get; set; } = "";
    public string ScriptHash { get; set; } = "";
    public Guid AssetId { get; set; }
    public bool Reused { get; set; }
}

