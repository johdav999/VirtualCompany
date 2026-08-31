using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ExchangeRateSet : ICompanyOwnedEntity
{
    private ExchangeRateSet() { }

    public ExchangeRateSet(Guid id, Guid companyId, Guid sourceId, long setVersion,
        string importIdentity, string contentHash, DateOnly effectiveFrom, DateOnly effectiveThrough,
        DateTime publishedUtc, Guid? importedByUserId, Guid? correctsRateSetId,
        bool requiresApproval, DateTime createdUtc)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        SourceId = ExchangeRateText.Id(sourceId, nameof(sourceId));
        if (setVersion < 1) throw new ArgumentOutOfRangeException(nameof(setVersion));
        if (effectiveThrough < effectiveFrom) throw new ArgumentOutOfRangeException(nameof(effectiveThrough));
        if (importedByUserId == Guid.Empty || correctsRateSetId == Guid.Empty)
            throw new ArgumentException("Optional identifiers cannot be empty.");
        SetVersion = setVersion;
        ImportIdentity = ExchangeRateText.Required(importIdentity, 200, nameof(importIdentity));
        ContentHash = ExchangeRateText.Required(contentHash, 64, nameof(contentHash)).ToLowerInvariant();
        EffectiveFrom = effectiveFrom;
        EffectiveThrough = effectiveThrough;
        PublishedUtc = ExchangeRateText.Utc(publishedUtc);
        ImportedByUserId = importedByUserId;
        CorrectsRateSetId = correctsRateSetId;
        Status = requiresApproval ? ExchangeRateSetStatuses.PendingReview : ExchangeRateSetStatuses.Approved;
        CreatedUtc = ExchangeRateText.Utc(createdUtc);
        ApprovedUtc = requiresApproval ? null : CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SourceId { get; private set; }
    public long SetVersion { get; private set; }
    public string ImportIdentity { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveThrough { get; private set; }
    public DateTime PublishedUtc { get; private set; }
    public Guid? ImportedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public Guid? CorrectsRateSetId { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public string? ReviewNote { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public ExchangeRateSource Source { get; private set; } = null!;
    public ICollection<ExchangeRateObservation> Observations { get; } = new List<ExchangeRateObservation>();
    public ICollection<ExchangeRateEvidence> Evidence { get; } = new List<ExchangeRateEvidence>();

    public void Approve(Guid actorUserId, string reviewNote, long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        ExchangeRateText.Id(actorUserId, nameof(actorUserId));
        if (Status != ExchangeRateSetStatuses.PendingReview)
            throw new InvalidOperationException("Only a pending exchange-rate set can be approved.");
        Status = ExchangeRateSetStatuses.Approved;
        ApprovedByUserId = actorUserId;
        ApprovedUtc = ExchangeRateText.Utc(nowUtc);
        ReviewNote = ExchangeRateText.Required(reviewNote, 1000, nameof(reviewNote));
        Version++;
    }

    public void Reject(Guid actorUserId, string reviewNote, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        ExchangeRateText.Id(actorUserId, nameof(actorUserId));
        if (Status != ExchangeRateSetStatuses.PendingReview)
            throw new InvalidOperationException("Only a pending exchange-rate set can be rejected.");
        Status = ExchangeRateSetStatuses.Rejected;
        ApprovedByUserId = actorUserId;
        ReviewNote = ExchangeRateText.Required(reviewNote, 1000, nameof(reviewNote));
        Version++;
    }

    public void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The exchange-rate set changed after it was loaded.");
    }
}

public sealed class ExchangeRateObservation : ICompanyOwnedEntity
{
    private ExchangeRateObservation() { }

    public ExchangeRateObservation(Guid id, Guid companyId, Guid rateSetId, string baseCurrency,
        string quoteCurrency, decimal rate, int ratePrecision, string quotationConvention,
        DateOnly effectiveDate, DateTime observedUtc, Guid? correctsObservationId)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        RateSetId = ExchangeRateText.Id(rateSetId, nameof(rateSetId));
        BaseCurrency = ExchangeRateText.Currency(baseCurrency, nameof(baseCurrency));
        QuoteCurrency = ExchangeRateText.Currency(quoteCurrency, nameof(quoteCurrency));
        if (BaseCurrency == QuoteCurrency) throw new ArgumentException("An exchange-rate pair must contain two currencies.");
        if (rate <= 0m) throw new ArgumentOutOfRangeException(nameof(rate), "An exchange rate must be positive.");
        if (ratePrecision is < 0 or > 18) throw new ArgumentOutOfRangeException(nameof(ratePrecision));
        if (quotationConvention is not (ExchangeRateQuotationConventions.BaseCurrencyPerQuoteCurrency or ExchangeRateQuotationConventions.QuoteCurrencyPerBaseCurrency))
            throw new ArgumentOutOfRangeException(nameof(quotationConvention));
        if (correctsObservationId == Guid.Empty) throw new ArgumentException("Correction observation cannot be empty.", nameof(correctsObservationId));
        Rate = rate;
        RatePrecision = ratePrecision;
        QuotationConvention = quotationConvention;
        EffectiveDate = effectiveDate;
        ObservedUtc = ExchangeRateText.Utc(observedUtc);
        CorrectsObservationId = correctsObservationId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RateSetId { get; private set; }
    public string BaseCurrency { get; private set; } = null!;
    public string QuoteCurrency { get; private set; } = null!;
    public decimal Rate { get; private set; }
    public int RatePrecision { get; private set; }
    public string QuotationConvention { get; private set; } = null!;
    public DateOnly EffectiveDate { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public Guid? CorrectsObservationId { get; private set; }
    public ExchangeRateSet RateSet { get; private set; } = null!;
}

public sealed class ExchangeRateEvidence : ICompanyOwnedEntity
{
    public const string ExpiredPayloadMarker = "[expired]";
    private ExchangeRateEvidence() { }

    public ExchangeRateEvidence(Guid id, Guid companyId, Guid rateSetId, string checksum,
        string protectedPayload, string contentType, DateTime retentionExpiresUtc, DateTime createdUtc)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        RateSetId = ExchangeRateText.Id(rateSetId, nameof(rateSetId));
        Checksum = ExchangeRateText.Required(checksum, 64, nameof(checksum)).ToLowerInvariant();
        ProtectedPayload = ExchangeRateText.Required(protectedPayload, int.MaxValue, nameof(protectedPayload));
        ContentType = ExchangeRateText.Required(contentType, 100, nameof(contentType));
        RetentionExpiresUtc = ExchangeRateText.Utc(retentionExpiresUtc);
        CreatedUtc = ExchangeRateText.Utc(createdUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RateSetId { get; private set; }
    public string Checksum { get; private set; } = null!;
    public string ProtectedPayload { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public DateTime RetentionExpiresUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public ExchangeRateSet RateSet { get; private set; } = null!;

    public void ExpireProtectedPayload(DateTime nowUtc)
    {
        if (RetentionExpiresUtc > ExchangeRateText.Utc(nowUtc) || ProtectedPayload == ExpiredPayloadMarker) return;
        ProtectedPayload = ExpiredPayloadMarker;
    }
}
