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
        DateTime? updatedUtc = null,
        Guid? sourceSimulationEventRecordId = null,
        Guid? paymentSourceSimulationEventRecordId = null,
        Guid? targetSourceSimulationEventRecordId = null,
        string? idempotencyKey = null,
        decimal feeAmount = 0m,
        decimal writeOffAmount = 0m)
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
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        if (paymentSourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("PaymentSourceSimulationEventRecordId cannot be empty.", nameof(paymentSourceSimulationEventRecordId));
        }

        if (targetSourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("TargetSourceSimulationEventRecordId cannot be empty.", nameof(targetSourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
        PaymentSourceSimulationEventRecordId = paymentSourceSimulationEventRecordId;
        TargetSourceSimulationEventRecordId = targetSourceSimulationEventRecordId;
        IdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        FeeAmount = NormalizeNonNegativeAmount(feeAmount, nameof(feeAmount));
        WriteOffAmount = NormalizeNonNegativeAmount(writeOffAmount, nameof(writeOffAmount));
        AllocatedPaymentAmount = AllocatedAmount;
        PaymentCurrency = Currency;
        SettlementStatus = PaymentAllocationSettlementStatuses.LegacyUnavailable;
        Version = 1;
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
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public Guid? PaymentSourceSimulationEventRecordId { get; private set; }
    public Guid? TargetSourceSimulationEventRecordId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public decimal FeeAmount { get; private set; }
    public decimal WriteOffAmount { get; private set; }
    public decimal AllocatedPaymentAmount { get; private set; }
    public string PaymentCurrency { get; private set; } = null!;
    public string? FunctionalCurrency { get; private set; }
    public decimal? AllocatedFunctionalAmount { get; private set; }
    public decimal? SettlementFunctionalAmount { get; private set; }
    public decimal? BankFunctionalAmount { get; private set; }
    public decimal? FeeFunctionalAmount { get; private set; }
    public decimal? WriteOffFunctionalAmount { get; private set; }
    public decimal? RealizedGainLossAmount { get; private set; }
    public decimal? RoundingFunctionalAmount { get; private set; }
    public decimal? DocumentOutstandingAfter { get; private set; }
    public decimal? FunctionalOutstandingAfter { get; private set; }
    public DateOnly? SettlementRateDate { get; private set; }
    public decimal? SettlementRate { get; private set; }
    public Guid? SettlementExchangeRateConversionId { get; private set; }
    public string? SettlementRateIdentity { get; private set; }
    public decimal? SettlementConversionRoundingResidual { get; private set; }
    public Guid? SettlementLedgerEntryId { get; private set; }
    public Guid? ReversalLedgerEntryId { get; private set; }
    public string SettlementStatus { get; private set; } = PaymentAllocationSettlementStatuses.LegacyUnavailable;
    public DateTime? ReversedUtc { get; private set; }
    public Guid? ReversedByUserId { get; private set; }
    public string? ReversalReason { get; private set; }
    public string? ReversalIdempotencyKey { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public Payment Payment { get; private set; } = null!;
    public FinanceInvoice? Invoice { get; private set; }
    public FinanceBill? Bill { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public SimulationEventRecord? PaymentSourceSimulationEventRecord { get; private set; }
    public SimulationEventRecord? TargetSourceSimulationEventRecord { get; private set; }
    public ExchangeRateConversion? SettlementExchangeRateConversion { get; private set; }
    public LedgerEntry? SettlementLedgerEntry { get; private set; }
    public LedgerEntry? ReversalLedgerEntry { get; private set; }

    public decimal AppliedDocumentAmount => AllocatedAmount + WriteOffAmount;
    public bool IsReversed => SettlementStatus == PaymentAllocationSettlementStatuses.Reversed;

    public void Update(
        Guid paymentId,
        Guid? invoiceId,
        Guid? billId,
        decimal allocatedAmount,
        string currency,
        DateTime? updatedUtc = null,
        Guid? sourceSimulationEventRecordId = null,
        Guid? paymentSourceSimulationEventRecordId = null,
        Guid? targetSourceSimulationEventRecordId = null)
    {
        if (SettlementLedgerEntryId.HasValue || SettlementStatus == PaymentAllocationSettlementStatuses.Reversed)
            throw new InvalidOperationException("Posted settlement allocations are immutable and must be reversed instead.");
        PaymentId = paymentId == Guid.Empty
            ? throw new ArgumentException("PaymentId is required.", nameof(paymentId))
            : paymentId;
        InvoiceId = invoiceId == Guid.Empty ? throw new ArgumentException("InvoiceId cannot be empty.", nameof(invoiceId)) : invoiceId;
        BillId = billId == Guid.Empty ? throw new ArgumentException("BillId cannot be empty.", nameof(billId)) : billId;
        EnsureSingleTarget(InvoiceId, BillId);
        AllocatedAmount = NormalizeAmount(allocatedAmount, nameof(allocatedAmount));
        Currency = NormalizeCurrency(currency, nameof(currency));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        if (paymentSourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("PaymentSourceSimulationEventRecordId cannot be empty.", nameof(paymentSourceSimulationEventRecordId));
        }

        if (targetSourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("TargetSourceSimulationEventRecordId cannot be empty.", nameof(targetSourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
        PaymentSourceSimulationEventRecordId = paymentSourceSimulationEventRecordId;
        TargetSourceSimulationEventRecordId = targetSourceSimulationEventRecordId;
        AllocatedPaymentAmount = AllocatedAmount;
        PaymentCurrency = Currency;
        Version++;
    }

    public void RecordSettlement(
        decimal allocatedPaymentAmount,
        string paymentCurrency,
        string functionalCurrency,
        decimal allocatedFunctionalAmount,
        decimal settlementFunctionalAmount,
        decimal bankFunctionalAmount,
        decimal feeFunctionalAmount,
        decimal writeOffFunctionalAmount,
        decimal realizedGainLossAmount,
        decimal roundingFunctionalAmount,
        decimal documentOutstandingAfter,
        decimal functionalOutstandingAfter,
        DateOnly settlementRateDate,
        decimal settlementRate,
        Guid? settlementExchangeRateConversionId,
        string settlementRateIdentity,
        decimal settlementConversionRoundingResidual,
        Guid settlementLedgerEntryId,
        DateTime updatedUtc)
    {
        if (SettlementLedgerEntryId.HasValue)
            throw new InvalidOperationException("Settlement facts are immutable after posting.");
        if (settlementLedgerEntryId == Guid.Empty)
            throw new ArgumentException("SettlementLedgerEntryId is required.", nameof(settlementLedgerEntryId));
        AllocatedPaymentAmount = NormalizeAmount(allocatedPaymentAmount, nameof(allocatedPaymentAmount));
        PaymentCurrency = NormalizeCurrency(paymentCurrency, nameof(paymentCurrency));
        FunctionalCurrency = NormalizeCurrency(functionalCurrency, nameof(functionalCurrency));
        AllocatedFunctionalAmount = NormalizeNonNegativeAmount(allocatedFunctionalAmount, nameof(allocatedFunctionalAmount));
        SettlementFunctionalAmount = NormalizeNonNegativeAmount(settlementFunctionalAmount, nameof(settlementFunctionalAmount));
        BankFunctionalAmount = NormalizeNonNegativeAmount(bankFunctionalAmount, nameof(bankFunctionalAmount));
        FeeFunctionalAmount = NormalizeNonNegativeAmount(feeFunctionalAmount, nameof(feeFunctionalAmount));
        WriteOffFunctionalAmount = NormalizeNonNegativeAmount(writeOffFunctionalAmount, nameof(writeOffFunctionalAmount));
        RealizedGainLossAmount = NormalizeSignedAmount(realizedGainLossAmount);
        RoundingFunctionalAmount = NormalizeSignedAmount(roundingFunctionalAmount);
        DocumentOutstandingAfter = NormalizeNonNegativeAmount(documentOutstandingAfter, nameof(documentOutstandingAfter));
        FunctionalOutstandingAfter = NormalizeNonNegativeAmount(functionalOutstandingAfter, nameof(functionalOutstandingAfter));
        if (settlementRate <= 0m) throw new ArgumentOutOfRangeException(nameof(settlementRate));
        SettlementRateDate = settlementRateDate;
        SettlementRate = settlementRate;
        SettlementExchangeRateConversionId = settlementExchangeRateConversionId == Guid.Empty
            ? throw new ArgumentException("SettlementExchangeRateConversionId cannot be empty.", nameof(settlementExchangeRateConversionId))
            : settlementExchangeRateConversionId;
        SettlementRateIdentity = NormalizeRequired(settlementRateIdentity, nameof(settlementRateIdentity), 128).ToLowerInvariant();
        SettlementConversionRoundingResidual = decimal.Round(settlementConversionRoundingResidual, 18, MidpointRounding.ToEven);
        SettlementLedgerEntryId = settlementLedgerEntryId;
        SettlementStatus = PaymentAllocationSettlementStatuses.Posted;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void Reverse(Guid reversalLedgerEntryId, Guid actorUserId, string reason,
        string idempotencyKey, DateTime reversedUtc)
    {
        if (SettlementStatus == PaymentAllocationSettlementStatuses.Reversed)
            throw new InvalidOperationException("The settlement allocation is already reversed.");
        if (!SettlementLedgerEntryId.HasValue)
            throw new InvalidOperationException("Only a posted settlement allocation can be reversed.");
        if (reversalLedgerEntryId == Guid.Empty) throw new ArgumentException("Reversal ledger entry id is required.", nameof(reversalLedgerEntryId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        ReversalLedgerEntryId = reversalLedgerEntryId;
        ReversedByUserId = actorUserId;
        ReversalReason = NormalizeRequired(reason, nameof(reason), 500);
        ReversalIdempotencyKey = NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 200);
        ReversedUtc = EntityTimestampNormalizer.NormalizeUtc(reversedUtc, nameof(reversedUtc));
        SettlementStatus = PaymentAllocationSettlementStatuses.Reversed;
        UpdatedUtc = ReversedUtc.Value;
        Version++;
    }

    private static decimal NormalizeAmount(decimal value, string name)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(name, "Allocated amount must be greater than zero.");
        }

        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal NormalizeNonNegativeAmount(decimal value, string name)
    {
        if (value < 0m) throw new ArgumentOutOfRangeException(name, "Amount cannot be negative.");
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal NormalizeSignedAmount(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} cannot exceed {maxLength} characters.");
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

    private static string? NormalizeIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= 200
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), "Idempotency key cannot exceed 200 characters.");
    }
}
