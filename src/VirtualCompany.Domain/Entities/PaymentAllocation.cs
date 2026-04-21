using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class PaymentAllocation : ICompanyOwnedEntity
{
    private PaymentAllocation()
    {
    }

    public PaymentAllocation(
        Guid id,
        Guid companyId,
        Guid paymentId,
        Guid? invoiceId,
        Guid? billId,
        decimal allocatedAmount,
        string currency,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        PaymentId = paymentId == Guid.Empty ? throw new ArgumentException("PaymentId is required.", nameof(paymentId)) : paymentId;
        InvoiceId = invoiceId == Guid.Empty ? throw new ArgumentException("InvoiceId cannot be empty.", nameof(invoiceId)) : invoiceId;
        BillId = billId == Guid.Empty ? throw new ArgumentException("BillId cannot be empty.", nameof(billId)) : billId;
        EnsureSingleTarget(InvoiceId, BillId);
        AllocatedAmount = NormalizeAmount(allocatedAmount, nameof(allocatedAmount));
        Currency = NormalizeCurrency(currency, nameof(currency));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public Guid? BillId { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Payment Payment { get; private set; } = null!;
    public FinanceInvoice? Invoice { get; private set; }
    public FinanceBill? Bill { get; private set; }

    public void Update(
        Guid paymentId,
        Guid? invoiceId,
        Guid? billId,
        decimal allocatedAmount,
        string currency,
        DateTime? updatedUtc = null)
    {
        PaymentId = paymentId == Guid.Empty
            ? throw new ArgumentException("PaymentId is required.", nameof(paymentId))
            : paymentId;
        InvoiceId = invoiceId == Guid.Empty ? throw new ArgumentException("InvoiceId cannot be empty.", nameof(invoiceId)) : invoiceId;
        BillId = billId == Guid.Empty ? throw new ArgumentException("BillId cannot be empty.", nameof(billId)) : billId;
        EnsureSingleTarget(InvoiceId, BillId);
        AllocatedAmount = NormalizeAmount(allocatedAmount, nameof(allocatedAmount));
        Currency = NormalizeCurrency(currency, nameof(currency));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    private static decimal NormalizeAmount(decimal value, string name)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(name, "Allocated amount must be greater than zero.");
        }

        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
        {
            throw new ArgumentOutOfRangeException(name, "Currency must be a three-letter ISO code.");
        }

        return normalized;
    }

    private static void EnsureSingleTarget(Guid? invoiceId, Guid? billId)
    {
        var hasInvoice = invoiceId.HasValue;
        var hasBill = billId.HasValue;
        if (hasInvoice == hasBill)
        {
            throw new ArgumentException("Allocation must reference either an invoice or a bill.");
        }
    }
}