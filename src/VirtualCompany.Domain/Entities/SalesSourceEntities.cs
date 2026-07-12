using System.Security.Cryptography;
using System.Text;

namespace VirtualCompany.Domain.Entities;

public static class SalesSourceCategories
{
    public const string Website = "website";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string Referral = "referral";
    public const string Event = "event";
    public const string PaidCampaign = "paid_campaign";
    public const string OrganicSocial = "organic_social";
    public const string Research = "research";
    public const string Import = "import";
    public const string Manual = "manual";
}

public sealed class SalesAcquisitionCampaign : ICompanyOwnedEntity
{
    private SalesAcquisitionCampaign() { }

    public SalesAcquisitionCampaign(Guid id, Guid companyId, string name, string category, string provider,
        string? externalReference, decimal? budget, string? currency, DateTime? startsUtc, DateTime? endsUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = Required(name, 200);
        Category = Required(category, 64).ToLowerInvariant();
        Provider = Required(provider, 120).ToLowerInvariant();
        ExternalReference = Optional(externalReference, 256);
        Budget = budget;
        Currency = Optional(currency, 3)?.ToUpperInvariant();
        StartsUtc = Utc(startsUtc);
        EndsUtc = Utc(endsUtc);
        Status = "active";
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string Provider { get; private set; } = null!;
    public string? ExternalReference { get; private set; }
    public decimal? Budget { get; private set; }
    public string? Currency { get; private set; }
    public DateTime? StartsUtc { get; private set; }
    public DateTime? EndsUtc { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.") : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string? Optional(string? value, int max) { var x = value?.Trim(); return string.IsNullOrEmpty(x) ? null : x[..Math.Min(x.Length, max)]; }
    private static DateTime? Utc(DateTime? value) => value is null ? null : value.Value.Kind == DateTimeKind.Utc ? value : value.Value.ToUniversalTime();
}

public sealed class SalesSourceTouch : ICompanyOwnedEntity
{
    private SalesSourceTouch() { }

    public SalesSourceTouch(Guid id, Guid companyId, string subjectType, Guid subjectId, string category,
        string provider, string channel, string interactionType, string sourceReference, DateTime observedUtc,
        string actorType, string? actorReference, Guid? campaignId = null, string? evidence = null,
        string? landingPage = null, string? referrer = null, string? utmSource = null, string? utmMedium = null,
        string? utmCampaign = null, string? utmContent = null, string? utmTerm = null, decimal? cost = null,
        string? currency = null, string? metadataJson = null)
    {
        if (companyId == Guid.Empty || subjectId == Guid.Empty) throw new ArgumentException("Company and subject are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SubjectType = Required(subjectType, 40).ToLowerInvariant();
        SubjectId = subjectId;
        Category = Required(category, 64).ToLowerInvariant();
        Provider = Required(provider, 120).ToLowerInvariant();
        Channel = Required(channel, 64).ToLowerInvariant();
        InteractionType = Required(interactionType, 64).ToLowerInvariant();
        SourceReference = Required(sourceReference, 512);
        ObservedUtc = observedUtc.Kind == DateTimeKind.Utc ? observedUtc : observedUtc.ToUniversalTime();
        ActorType = Required(actorType, 32).ToLowerInvariant();
        ActorReference = Optional(actorReference, 160);
        CampaignId = campaignId;
        Evidence = Optional(evidence, 1000);
        LandingPage = Optional(landingPage, 512);
        Referrer = Optional(referrer, 512);
        UtmSource = Optional(utmSource, 120);
        UtmMedium = Optional(utmMedium, 120);
        UtmCampaign = Optional(utmCampaign, 200);
        UtmContent = Optional(utmContent, 200);
        UtmTerm = Optional(utmTerm, 200);
        Cost = cost;
        Currency = Optional(currency, 3)?.ToUpperInvariant();
        MetadataJson = Optional(metadataJson, 8000);
        var dedupeInput = $"{CompanyId:N}:{SubjectType}:{SubjectId:N}:{Provider}:{InteractionType}:{SourceReference}".ToLowerInvariant();
        DedupeKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dedupeInput))).ToLowerInvariant();
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public string SubjectType { get; private set; } = null!;
    public Guid SubjectId { get; private set; }
    public string Category { get; private set; } = null!;
    public string Provider { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string InteractionType { get; private set; } = null!;
    public string SourceReference { get; private set; } = null!;
    public string? Evidence { get; private set; }
    public string? LandingPage { get; private set; }
    public string? Referrer { get; private set; }
    public string? UtmSource { get; private set; }
    public string? UtmMedium { get; private set; }
    public string? UtmCampaign { get; private set; }
    public string? UtmContent { get; private set; }
    public string? UtmTerm { get; private set; }
    public decimal? Cost { get; private set; }
    public string? Currency { get; private set; }
    public string ActorType { get; private set; } = null!;
    public string? ActorReference { get; private set; }
    public string? MetadataJson { get; private set; }
    public string DedupeKey { get; private set; } = null!;
    public DateTime ObservedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.") : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string? Optional(string? value, int max) { var x = value?.Trim(); return string.IsNullOrEmpty(x) ? null : x[..Math.Min(x.Length, max)]; }
}

public sealed class SalesSourceAttribution : ICompanyOwnedEntity
{
    private SalesSourceAttribution() { }
    public SalesSourceAttribution(Guid id, Guid companyId, string subjectType, Guid subjectId, Guid firstTouchId, decimal? initialCost = null, string? currency = null, bool conversion = false)
    {
        if (companyId == Guid.Empty || subjectId == Guid.Empty || firstTouchId == Guid.Empty) throw new ArgumentException("Company, subject and touch are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SubjectType = subjectType.Trim().ToLowerInvariant(); SubjectId = subjectId;
        OriginalTouchId = FirstTouchId = LastTouchId = firstTouchId; ConversionTouchId = conversion ? firstTouchId : null; TouchCount = 1; TotalAcquisitionCost = initialCost ?? 0; Currency = currency; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SubjectType { get; private set; } = null!; public Guid SubjectId { get; private set; }
    public Guid OriginalTouchId { get; private set; } public Guid FirstTouchId { get; private set; } public Guid LastTouchId { get; private set; } public Guid? ConversionTouchId { get; private set; }
    public int TouchCount { get; private set; } public decimal TotalAcquisitionCost { get; private set; } public string? Currency { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Record(Guid touchId, decimal? cost, string? currency, bool conversion, bool earliest, bool latest) { if (latest) LastTouchId = touchId; if (earliest) { FirstTouchId = touchId; OriginalTouchId = touchId; } TouchCount++; TotalAcquisitionCost += cost ?? 0; Currency ??= currency; if (conversion) ConversionTouchId = touchId; UpdatedUtc = DateTime.UtcNow; }
}

public sealed class SalesContactPermission : ICompanyOwnedEntity
{
    private SalesContactPermission() { }
    public SalesContactPermission(Guid id, Guid companyId, Guid contactId, string channel, string address, string status, string legalBasis, string sourceReference, DateTime observedUtc)
    { if (companyId == Guid.Empty || contactId == Guid.Empty) throw new ArgumentException("Company and contact are required."); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ContactId = contactId; Channel = channel.Trim().ToLowerInvariant(); Address = address.Trim().ToLowerInvariant(); Status = status.Trim().ToLowerInvariant(); LegalBasis = legalBasis.Trim().ToLowerInvariant(); SourceReference = sourceReference.Trim(); ObservedUtc = observedUtc.Kind == DateTimeKind.Utc ? observedUtc : observedUtc.ToUniversalTime(); CreatedUtc = UpdatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ContactId { get; private set; } public string Channel { get; private set; } = null!; public string Address { get; private set; } = null!; public string Status { get; private set; } = null!; public string LegalBasis { get; private set; } = null!; public string SourceReference { get; private set; } = null!; public DateTime ObservedUtc { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Update(string status, string legalBasis, string sourceReference, DateTime observedUtc) { Status = status.Trim().ToLowerInvariant(); LegalBasis = legalBasis.Trim().ToLowerInvariant(); SourceReference = sourceReference.Trim(); ObservedUtc = observedUtc; UpdatedUtc = DateTime.UtcNow; }
}
