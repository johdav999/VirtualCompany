using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public static class LedgerEntryStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";

    public static string Normalize(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("Ledger entry status is required.", nameof(status));
        }

        return status.Trim().ToLowerInvariant() switch
        {
            Draft => Draft,
            Posted => Posted,
            _ => throw new ArgumentOutOfRangeException(nameof(status), "Ledger entry status must be 'draft' or 'posted'.")
        };
    }

    public static bool IsPosted(string status) =>
        string.Equals(Normalize(status), Posted, StringComparison.Ordinal);
}

public static class LedgerPostingTypeValues
{
    public const string Manual = "manual";
    public const string SourceDocument = "source_document";
    public const string Bank = "bank";
    public const string CashSettlement = "cash_settlement";
    public const string Reversal = "reversal";
    public const string Adjustment = "adjustment";
    public const string CurrencyRevaluation = "currency_revaluation";
    public const string YearEnd = "year_end";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Posting type is required.", nameof(value))
            : value.Trim().ToLowerInvariant() switch
            {
                Manual => Manual,
                SourceDocument => SourceDocument,
                Bank => Bank,
                CashSettlement => CashSettlement,
                Reversal => Reversal,
                Adjustment => Adjustment,
                CurrencyRevaluation => CurrencyRevaluation,
                YearEnd => YearEnd,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Posting type is not supported.")
            };
}

public sealed class FiscalPeriod : ICompanyOwnedEntity
{
    private FiscalPeriod()
    {
    }

    public FiscalPeriod(
        Guid id,
        Guid companyId,
        string name,
        DateTime startUtc,
        DateTime endUtc,
        bool isClosed = false,
        DateTime? closedUtc = null,
        bool isReportingLocked = false,
        DateTime? reportingLockedUtc = null,
        Guid? reportingLockedByUserId = null,
        DateTime? reportingUnlockedUtc = null,
        Guid? reportingUnlockedByUserId = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        DateTime? lastCloseValidatedUtc = null,
        Guid? lastCloseValidatedByUserId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        var normalizedStartUtc = EntityTimestampNormalizer.NormalizeUtc(startUtc, nameof(startUtc));
        var normalizedEndUtc = EntityTimestampNormalizer.NormalizeUtc(endUtc, nameof(endUtc));
        if (normalizedEndUtc <= normalizedStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endUtc), "EndUtc must be later than StartUtc.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = NormalizeRequired(name, nameof(name), 128);
        StartUtc = normalizedStartUtc;
        EndUtc = normalizedEndUtc;
        IsClosed = isClosed;
        IsReportingLocked = isReportingLocked;
        ReportingLockedUtc = isReportingLocked && reportingLockedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(reportingLockedUtc.Value, nameof(reportingLockedUtc)) : null;
        ReportingLockedByUserId = reportingLockedByUserId == Guid.Empty ? null : reportingLockedByUserId;
        ReportingUnlockedUtc = reportingUnlockedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(reportingUnlockedUtc.Value, nameof(reportingUnlockedUtc)) : null;
        ReportingUnlockedByUserId = reportingUnlockedByUserId == Guid.Empty ? null : reportingUnlockedByUserId;
        LastCloseValidatedUtc = lastCloseValidatedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(lastCloseValidatedUtc.Value, nameof(lastCloseValidatedUtc)) : null;
        LastCloseValidatedByUserId = lastCloseValidatedByUserId == Guid.Empty ? null : lastCloseValidatedByUserId;
        ClosedUtc = isClosed && closedUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(closedUtc.Value, nameof(closedUtc))
            : isClosed
                ? normalizedEndUtc
                : null;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? normalizedStartUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public bool IsClosed { get; private set; }
    public bool IsReportingLocked { get; private set; }
    public DateTime? ReportingLockedUtc { get; private set; }
    public Guid? ReportingLockedByUserId { get; private set; }
    public DateTime? ReportingUnlockedUtc { get; private set; }
    public Guid? ReportingUnlockedByUserId { get; private set; }
    public DateTime? LastCloseValidatedUtc { get; private set; }
    public Guid? LastCloseValidatedByUserId { get; private set; }
    public DateTime? ClosedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Company Company { get; private set; } = null!;

    public void RecordCloseValidation(Guid? actorUserId, DateTime? validatedUtc = null)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id cannot be empty.", nameof(actorUserId));
        }

        LastCloseValidatedUtc = EntityTimestampNormalizer.NormalizeUtc(validatedUtc ?? DateTime.UtcNow, nameof(validatedUtc));
        LastCloseValidatedByUserId = actorUserId;
        UpdatedUtc = LastCloseValidatedUtc.Value;
    }

    public void Close(DateTime? closedUtc = null)
    {
        IsClosed = true;
        ClosedUtc = EntityTimestampNormalizer.NormalizeUtc(closedUtc ?? EndUtc, nameof(closedUtc));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void LockReporting(Guid? actorUserId, DateTime? lockedUtc = null)
    {
        if (!IsClosed)
        {
            throw new InvalidOperationException("Reporting can only be locked for a closed fiscal period.");
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id cannot be empty.", nameof(actorUserId));
        }

        IsReportingLocked = true;
        ReportingLockedUtc = EntityTimestampNormalizer.NormalizeUtc(lockedUtc ?? DateTime.UtcNow, nameof(lockedUtc));
        ReportingLockedByUserId = actorUserId;
        ReportingUnlockedUtc = null;
        ReportingUnlockedByUserId = null;
        UpdatedUtc = ReportingLockedUtc.Value;
    }

    public void UnlockReporting(Guid? actorUserId, DateTime? unlockedUtc = null)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id cannot be empty.", nameof(actorUserId));
        }

        IsReportingLocked = false;
        ReportingUnlockedUtc = EntityTimestampNormalizer.NormalizeUtc(unlockedUtc ?? DateTime.UtcNow, nameof(unlockedUtc));
        ReportingUnlockedByUserId = actorUserId;
        UpdatedUtc = ReportingUnlockedUtc.Value;
    }

    public void Reopen(Guid actorUserId, DateTime? reopenedUtc = null)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        var occurredUtc = EntityTimestampNormalizer.NormalizeUtc(reopenedUtc ?? DateTime.UtcNow, nameof(reopenedUtc));
        IsClosed = false;
        ClosedUtc = null;
        IsReportingLocked = false;
        ReportingUnlockedUtc = occurredUtc;
        ReportingUnlockedByUserId = actorUserId;
        UpdatedUtc = occurredUtc;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class LedgerEntry : ICompanyOwnedEntity
{
    private LedgerEntry()
    {
    }

    public LedgerEntry(
        Guid id,
        Guid companyId,
        Guid fiscalPeriodId,
        string entryNumber,
        DateTime entryUtc,
        string status,
        string? description = null,
        string? sourceType = null,
        string? sourceId = null,
        DateTime? postedAtUtc = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        Guid? voucherSeriesId = null,
        long? voucherSequenceNumber = null,
        int? voucherFiscalYear = null,
        DateOnly? documentDate = null,
        DateOnly? postingDate = null,
        string? baseCurrency = null,
        string? postingType = null,
        string? sourceVersion = null,
        string? idempotencyKey = null,
        string? policyPackKey = null,
        string? policyPackVersion = null,
        string? policyFactsJson = null,
        Guid? postedByUserId = null,
        Guid? approvalRequestId = null,
        Guid? originalLedgerEntryId = null,
        string? correctionReason = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (fiscalPeriodId == Guid.Empty)
        {
            throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FiscalPeriodId = fiscalPeriodId;
        EntryNumber = NormalizeRequired(entryNumber, nameof(entryNumber), 64);
        EntryUtc = EntityTimestampNormalizer.NormalizeUtc(entryUtc, nameof(entryUtc));
        Status = LedgerEntryStatuses.Normalize(status);
        ValidateSourceReference(sourceType, sourceId);
        Description = NormalizeOptional(description, nameof(description), 500);
        SourceType = NormalizeOptional(sourceType, nameof(sourceType), 64)?.ToLowerInvariant();
        SourceId = NormalizeOptional(sourceId, nameof(sourceId), 128);
        PostedAtUtc = postedAtUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(postedAtUtc.Value, nameof(postedAtUtc))
            : LedgerEntryStatuses.IsPosted(Status)
                ? EntryUtc
                : null;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? EntryUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
        if (voucherSeriesId == Guid.Empty) throw new ArgumentException("VoucherSeriesId cannot be empty.", nameof(voucherSeriesId));
        if (voucherSequenceNumber is <= 0) throw new ArgumentOutOfRangeException(nameof(voucherSequenceNumber));
        if (voucherFiscalYear is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(voucherFiscalYear));
        if (postedByUserId == Guid.Empty) throw new ArgumentException("PostedByUserId cannot be empty.", nameof(postedByUserId));
        if (approvalRequestId == Guid.Empty) throw new ArgumentException("ApprovalRequestId cannot be empty.", nameof(approvalRequestId));
        if (originalLedgerEntryId == Guid.Empty) throw new ArgumentException("OriginalLedgerEntryId cannot be empty.", nameof(originalLedgerEntryId));
        VoucherSeriesId = voucherSeriesId;
        VoucherSequenceNumber = voucherSequenceNumber;
        VoucherFiscalYear = voucherFiscalYear;
        DocumentDate = documentDate;
        PostingDate = postingDate;
        BaseCurrency = NormalizeOptional(baseCurrency, nameof(baseCurrency), 3)?.ToUpperInvariant();
        PostingType = string.IsNullOrWhiteSpace(postingType) ? null : LedgerPostingTypeValues.Normalize(postingType);
        SourceVersion = NormalizeOptional(sourceVersion, nameof(sourceVersion), 128);
        IdempotencyKey = NormalizeOptional(idempotencyKey, nameof(idempotencyKey), 200);
        PolicyPackKey = NormalizeOptional(policyPackKey, nameof(policyPackKey), 96)?.ToLowerInvariant();
        PolicyPackVersion = NormalizeOptional(policyPackVersion, nameof(policyPackVersion), 32);
        PolicyFactsJson = NormalizeOptional(policyFactsJson, nameof(policyFactsJson), 16000);
        PostedByUserId = postedByUserId;
        ApprovalRequestId = approvalRequestId;
        OriginalLedgerEntryId = originalLedgerEntryId;
        CorrectionReason = NormalizeOptional(correctionReason, nameof(correctionReason), 1000);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string EntryNumber { get; private set; } = null!;
    public DateTime EntryUtc { get; private set; }
    public string Status { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? SourceType { get; private set; }
    public string? SourceId { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Guid? VoucherSeriesId { get; private set; }
    public long? VoucherSequenceNumber { get; private set; }
    public int? VoucherFiscalYear { get; private set; }
    public DateOnly? DocumentDate { get; private set; }
    public DateOnly? PostingDate { get; private set; }
    public string? BaseCurrency { get; private set; }
    public string? PostingType { get; private set; }
    public string? SourceVersion { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? PolicyPackKey { get; private set; }
    public string? PolicyPackVersion { get; private set; }
    public string? PolicyFactsJson { get; private set; }
    public Guid? PostedByUserId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? OriginalLedgerEntryId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<LedgerEntrySourceMapping> SourceMappings { get; } = new List<LedgerEntrySourceMapping>();
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public VoucherSeries? VoucherSeries { get; private set; }
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public LedgerEntry? OriginalLedgerEntry { get; private set; }
    public ICollection<LedgerEntryLine> Lines { get; } = new List<LedgerEntryLine>();
    public ICollection<LedgerEntryEvidenceLink> EvidenceLinks { get; } = new List<LedgerEntryEvidenceLink>();
    public ICollection<LedgerEntry> Corrections { get; } = new List<LedgerEntry>();

    public void Post(DateTime? updatedUtc = null)
    {
        if (LedgerEntryStatuses.IsPosted(Status))
        {
            throw new InvalidOperationException("A posted ledger entry cannot be posted again.");
        }

        Status = LedgerEntryStatuses.Posted;
        PostedAtUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static void ValidateSourceReference(string? sourceType, string? sourceId)
    {
        var hasSourceType = !string.IsNullOrWhiteSpace(sourceType);
        var hasSourceId = !string.IsNullOrWhiteSpace(sourceId);

        if (hasSourceType != hasSourceId)
        {
            throw new ArgumentException("Source type and source id must be provided together.");
        }
    }
}

public sealed class LedgerEntrySourceMapping : ICompanyOwnedEntity
{
    private LedgerEntrySourceMapping()
    {
    }

    public LedgerEntrySourceMapping(
        Guid id,
        Guid companyId,
        Guid ledgerEntryId,
        string sourceType,
        string sourceId,
        DateTime postedAtUtc,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (ledgerEntryId == Guid.Empty)
        {
            throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        LedgerEntryId = ledgerEntryId;
        SourceType = NormalizeRequired(sourceType, nameof(sourceType), 64).ToLowerInvariant();
        SourceId = NormalizeRequired(sourceId, nameof(sourceId), 128);
        PostedAtUtc = EntityTimestampNormalizer.NormalizeUtc(postedAtUtc, nameof(postedAtUtc));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? PostedAtUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceId { get; private set; } = null!;
    public DateTime PostedAtUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public LedgerEntry LedgerEntry { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class LedgerEntryLine : ICompanyOwnedEntity
{
    private LedgerEntryLine()
    {
    }

    public LedgerEntryLine(
        Guid id,
        Guid companyId,
        Guid ledgerEntryId,
        Guid financeAccountId,
        decimal debitAmount,
        decimal creditAmount,
        string currency,
        string? description,
        DateTime? createdUtc = null)
        : this(id, companyId, ledgerEntryId, financeAccountId, debitAmount, creditAmount, currency, null, description, createdUtc)
    {
    }

    public LedgerEntryLine(
        Guid id,
        Guid companyId,
        Guid ledgerEntryId,
        Guid financeAccountId,
        decimal debitAmount,
        decimal creditAmount,
        string currency,
        Guid? costCenterId = null,
        string? description = null,
        DateTime? createdUtc = null,
        string? taxFactsJson = null,
        string? dimensionFactsJson = null,
        decimal? documentDebitAmount = null,
        decimal? documentCreditAmount = null,
        string? documentCurrency = null,
        decimal? exchangeRate = null,
        DateOnly? exchangeRateDate = null,
        Guid? exchangeRateConversionId = null,
        string? exchangeRateIdentity = null,
        decimal? conversionRoundingResidual = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (ledgerEntryId == Guid.Empty)
        {
            throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        if (debitAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(debitAmount), "DebitAmount cannot be negative.");
        }

        if (creditAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(creditAmount), "CreditAmount cannot be negative.");
        }

        if (debitAmount == 0m && creditAmount == 0m)
        {
            throw new ArgumentException("Ledger entry line must carry a debit or credit amount.");
        }

        if (debitAmount > 0m && creditAmount > 0m)
        {
            throw new ArgumentException("Ledger entry line cannot carry both debit and credit amounts.");
        }

        if (costCenterId == Guid.Empty)
        {
            throw new ArgumentException("CostCenterId cannot be empty.", nameof(costCenterId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        LedgerEntryId = ledgerEntryId;
        FinanceAccountId = financeAccountId;
        DebitAmount = debitAmount;
        CreditAmount = creditAmount;
        CostCenterId = costCenterId;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Description = NormalizeOptional(description, nameof(description), 500);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        TaxFactsJson = NormalizeOptional(taxFactsJson, nameof(taxFactsJson), 8000);
        DimensionFactsJson = NormalizeOptional(dimensionFactsJson, nameof(dimensionFactsJson), 8000);
        var hasDocumentFacts = documentDebitAmount.HasValue || documentCreditAmount.HasValue ||
            !string.IsNullOrWhiteSpace(documentCurrency);
        DocumentDebitAmount = documentDebitAmount ?? debitAmount;
        DocumentCreditAmount = documentCreditAmount ?? creditAmount;
        var isFunctionalOnlyAdjustment = documentDebitAmount == 0m && documentCreditAmount == 0m;
        if (DocumentDebitAmount < 0m || DocumentCreditAmount < 0m ||
            !isFunctionalOnlyAdjustment && DocumentDebitAmount == 0m && DocumentCreditAmount == 0m ||
            DocumentDebitAmount > 0m && DocumentCreditAmount > 0m)
            throw new ArgumentException("Document amounts must contain either one positive debit or one positive credit.");
        DocumentCurrency = NormalizeRequired(documentCurrency ?? currency, nameof(documentCurrency), 3).ToUpperInvariant();
        ExchangeRate = exchangeRate ?? (hasDocumentFacts ? null : 1m);
        if (ExchangeRate is <= 0m) throw new ArgumentOutOfRangeException(nameof(exchangeRate));
        if (exchangeRateConversionId == Guid.Empty)
            throw new ArgumentException("ExchangeRateConversionId cannot be empty.", nameof(exchangeRateConversionId));
        ExchangeRateDate = exchangeRateDate;
        ExchangeRateConversionId = exchangeRateConversionId;
        ExchangeRateIdentity = NormalizeOptional(exchangeRateIdentity, nameof(exchangeRateIdentity), 128)
            ?? (hasDocumentFacts ? null : $"legacy-base:{Currency}");
        ConversionRoundingResidual = conversionRoundingResidual.HasValue
            ? decimal.Round(conversionRoundingResidual.Value, 18, MidpointRounding.ToEven)
            : null;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public decimal DebitAmount { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public decimal CreditAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public string? TaxFactsJson { get; private set; }
    public string? DimensionFactsJson { get; private set; }
    public decimal DocumentDebitAmount { get; private set; }
    public decimal DocumentCreditAmount { get; private set; }
    public string DocumentCurrency { get; private set; } = null!;
    public decimal? ExchangeRate { get; private set; }
    public DateOnly? ExchangeRateDate { get; private set; }
    public Guid? ExchangeRateConversionId { get; private set; }
    public string? ExchangeRateIdentity { get; private set; }
    public decimal? ConversionRoundingResidual { get; private set; }
    public Company Company { get; private set; } = null!;
    public LedgerEntry LedgerEntry { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;
    public ExchangeRateConversion? ExchangeRateConversion { get; private set; }
    public ICollection<LedgerEntryLineDimension> DimensionAssignments { get; } = new List<LedgerEntryLineDimension>();

    public decimal SignedAmount => DebitAmount - CreditAmount;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class FinancialStatementSnapshot : ICompanyOwnedEntity
{
    private FinancialStatementSnapshot()
    {
    }

    public FinancialStatementSnapshot(
        Guid id,
        Guid companyId,
        Guid fiscalPeriodId,
        FinancialStatementType statementType,
        DateTime sourcePeriodStartUtc,
        DateTime sourcePeriodEndUtc,
        int versionNumber,
        string balancesChecksum,
        DateTime generatedAtUtc,
        string currency)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (fiscalPeriodId == Guid.Empty)
        {
            throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId));
        }

        if (versionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "VersionNumber must be positive.");
        }

        var normalizedSourcePeriodStartUtc = EntityTimestampNormalizer.NormalizeUtc(sourcePeriodStartUtc, nameof(sourcePeriodStartUtc));
        var normalizedSourcePeriodEndUtc = EntityTimestampNormalizer.NormalizeUtc(sourcePeriodEndUtc, nameof(sourcePeriodEndUtc));
        if (normalizedSourcePeriodEndUtc <= normalizedSourcePeriodStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePeriodEndUtc), "SourcePeriodEndUtc must be later than SourcePeriodStartUtc.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FiscalPeriodId = fiscalPeriodId;
        StatementType = statementType;
        SourcePeriodStartUtc = normalizedSourcePeriodStartUtc;
        SourcePeriodEndUtc = normalizedSourcePeriodEndUtc;
        VersionNumber = versionNumber;
        BalancesChecksum = NormalizeRequired(balancesChecksum, nameof(balancesChecksum), 128);
        GeneratedAtUtc = EntityTimestampNormalizer.NormalizeUtc(generatedAtUtc, nameof(generatedAtUtc));
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public FinancialStatementType StatementType { get; private set; }
    public DateTime SourcePeriodStartUtc { get; private set; }
    public DateTime SourcePeriodEndUtc { get; private set; }
    public int VersionNumber { get; private set; }
    public string BalancesChecksum { get; private set; } = null!;
    public DateTime GeneratedAtUtc { get; private set; }
    public string Currency { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public ICollection<FinancialStatementSnapshotLine> Lines { get; } = new List<FinancialStatementSnapshotLine>();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class FinancialStatementSnapshotLine : ICompanyOwnedEntity
{
    private FinancialStatementSnapshotLine()
    {
    }

    public FinancialStatementSnapshotLine(
        Guid id,
        Guid companyId,
        Guid snapshotId,
        Guid? financeAccountId,
        string lineCode,
        string lineName,
        int lineOrder,
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification,
        decimal amount,
        string currency)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (snapshotId == Guid.Empty)
        {
            throw new ArgumentException("SnapshotId is required.", nameof(snapshotId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId cannot be empty.", nameof(financeAccountId));
        }

        if (lineOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineOrder), "LineOrder cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SnapshotId = snapshotId;
        FinanceAccountId = financeAccountId;
        LineCode = NormalizeRequired(lineCode, nameof(lineCode), 64);
        LineName = NormalizeRequired(lineName, nameof(lineName), 160);
        LineOrder = lineOrder;
        ReportSection = reportSection;
        LineClassification = lineClassification;
        Amount = amount;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid? FinanceAccountId { get; private set; }
    public string LineCode { get; private set; } = null!;
    public string LineName { get; private set; } = null!;
    public int LineOrder { get; private set; }
    public FinancialStatementReportSection ReportSection { get; private set; }
    public FinancialStatementLineClassification LineClassification { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public FinancialStatementSnapshot Snapshot { get; private set; } = null!;
    public FinanceAccount? FinanceAccount { get; private set; }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class TrialBalanceSnapshot : ICompanyOwnedEntity
{
    private TrialBalanceSnapshot()
    {
    }

    public TrialBalanceSnapshot(
        Guid id,
        Guid companyId,
        Guid fiscalPeriodId,
        Guid financeAccountId,
        decimal balanceAmount,
        string currency,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (fiscalPeriodId == Guid.Empty)
        {
            throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FiscalPeriodId = fiscalPeriodId;
        FinanceAccountId = financeAccountId;
        BalanceAmount = balanceAmount;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public decimal BalanceAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}
