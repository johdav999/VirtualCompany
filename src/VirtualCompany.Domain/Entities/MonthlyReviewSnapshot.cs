namespace VirtualCompany.Domain.Entities;

// Append-only management review; financial statement and operating cycle snapshots remain separate owners.
public sealed class MonthlyReviewSnapshot : ICompanyOwnedEntity
{
    public const int MaximumPayloadBytes = 524288;
    private MonthlyReviewSnapshot() { }
    public MonthlyReviewSnapshot(Guid id, Guid companyId, Guid userId, Guid seriesId, int revision, Guid? previousId,
        Guid requestId, string lens, int year, int month, string accessStamp, string payloadJson, string checksum,
        DateTime asOfUtc, DateTime savedAtUtc, string calculationVersion)
    {
        if (new[] { id, companyId, userId, seriesId, requestId }.Any(x => x == Guid.Empty)) throw new ArgumentException("Review identities are required.");
        if (revision < 1 || year is < 2000 or > 2100 || month is < 1 or > 12 ||
            (revision == 1) != (previousId is null)) throw new ArgumentException("Invalid review revision or period.");
        if (System.Text.Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes) throw new ArgumentException("The review exceeds the 512 KiB retention limit.");
        if (string.IsNullOrWhiteSpace(lens) || accessStamp.Length != 64 || checksum.Length != 64) throw new ArgumentException("Invalid review provenance.");
        Id=id; CompanyId=companyId; CreatedByUserId=userId; SeriesId=seriesId; Revision=revision; PreviousId=previousId;
        RequestId=requestId; Lens=lens; Year=year; Month=month; AccessStamp=accessStamp; PayloadJson=payloadJson;
        Checksum=checksum; AsOfUtc=EntityTimestampNormalizer.NormalizeUtc(asOfUtc,nameof(asOfUtc));
        SavedAtUtc=EntityTimestampNormalizer.NormalizeUtc(savedAtUtc,nameof(savedAtUtc)); CalculationVersion=calculationVersion;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid SeriesId { get; private set; }
    public int Revision { get; private set; }
    public Guid? PreviousId { get; private set; }
    public Guid RequestId { get; private set; }
    public string Lens { get; private set; } = null!;
    public int Year { get; private set; }
    public int Month { get; private set; }
    public string AccessStamp { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public string Checksum { get; private set; } = null!;
    public DateTime AsOfUtc { get; private set; }
    public DateTime SavedAtUtc { get; private set; }
    public string CalculationVersion { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
}
