namespace VirtualCompany.Domain.Entities;

public sealed class VoucherSequence : ICompanyOwnedEntity
{
    private VoucherSequence() { }

    public VoucherSequence(Guid id, Guid companyId, Guid voucherSeriesId, int fiscalYear, long lastAllocatedNumber, DateTime createdUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (voucherSeriesId == Guid.Empty) throw new ArgumentException("VoucherSeriesId is required.", nameof(voucherSeriesId));
        if (fiscalYear is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(fiscalYear));
        if (lastAllocatedNumber < 0) throw new ArgumentOutOfRangeException(nameof(lastAllocatedNumber));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        VoucherSeriesId = voucherSeriesId;
        FiscalYear = fiscalYear;
        LastAllocatedNumber = lastAllocatedNumber;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VoucherSeriesId { get; private set; }
    public int FiscalYear { get; private set; }
    public long LastAllocatedNumber { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Company Company { get; private set; } = null!;
    public VoucherSeries VoucherSeries { get; private set; } = null!;

    public long Allocate(DateTime allocatedUtc)
    {
        checked { LastAllocatedNumber++; }
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(allocatedUtc, nameof(allocatedUtc));
        return LastAllocatedNumber;
    }
}
