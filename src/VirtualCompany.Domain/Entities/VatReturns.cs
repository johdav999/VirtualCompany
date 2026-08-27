namespace VirtualCompany.Domain.Entities;

public static class VatReturnStatuses
{
    public const string Draft = "draft";
    public const string Calculated = "calculated";
    public const string NeedsReview = "needs_review";
    public const string Approved = "approved";
    public const string Locked = "locked";
    public const string Corrected = "corrected";
}

public sealed class VatFilingPeriod : ICompanyOwnedEntity
{
    private VatFilingPeriod() { }

    public VatFilingPeriod(Guid id, Guid companyId, string periodCode, DateOnly startDate,
        DateOnly endDate, string currency, Guid? fiscalPeriodId, DateTime createdUtc)
    {
        if (endDate < startDate) throw new ArgumentOutOfRangeException(nameof(endDate));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        PeriodCode = Text(periodCode, nameof(periodCode), 40).ToUpperInvariant();
        StartDate = startDate;
        EndDate = endDate;
        Currency = Text(currency, nameof(currency), 3).ToUpperInvariant();
        FiscalPeriodId = fiscalPeriodId == Guid.Empty ? null : fiscalPeriodId;
        CreatedUtc = Utc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string PeriodCode { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string Currency { get; private set; } = null!;
    public Guid? FiscalPeriodId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod? FiscalPeriod { get; private set; }
    public ICollection<VatReturn> Returns { get; } = new List<VatReturn>();

    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static string Text(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static DateTime Utc(DateTime value, string name) => EntityTimestampNormalizer.NormalizeUtc(value, name);
}

public sealed class VatReturn : ICompanyOwnedEntity
{
    private VatReturn() { }

    public VatReturn(Guid id, Guid companyId, Guid filingPeriodId, int version,
        string idempotencyKey, Guid? correctionOfVatReturnId, string? correctionReason,
        string? correctionEvidenceReference, DateTime createdUtc)
    {
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        FilingPeriodId = Required(filingPeriodId, nameof(filingPeriodId));
        Version = version;
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 200);
        CorrectionOfVatReturnId = correctionOfVatReturnId == Guid.Empty ? null : correctionOfVatReturnId;
        CorrectionReason = Optional(correctionReason, 1000);
        CorrectionEvidenceReference = Optional(correctionEvidenceReference, 500);
        Status = VatReturnStatuses.Draft;
        CreatedUtc = Utc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FilingPeriodId { get; private set; }
    public int Version { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid? CorrectionOfVatReturnId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public string? CorrectionEvidenceReference { get; private set; }
    public DateTime? CutoffUtc { get; private set; }
    public string? InputHash { get; private set; }
    public string? CalculationChecksum { get; private set; }
    public int IncludedSourceCount { get; private set; }
    public int ExcludedSourceCount { get; private set; }
    public decimal OutputVatExact { get; private set; }
    public decimal InputVatExact { get; private set; }
    public decimal SettlementExact { get; private set; }
    public long SettlementFilingAmount { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? FinalizedByUserId { get; private set; }
    public DateTime? FinalizedUtc { get; private set; }
    public string? PackageStorageKey { get; private set; }
    public string? PackageChecksum { get; private set; }
    public string? PackageFileName { get; private set; }
    public string? PackageMediaType { get; private set; }
    public long? PackageContentLength { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public VatFilingPeriod FilingPeriod { get; private set; } = null!;
    public VatReturn? CorrectionOfVatReturn { get; private set; }
    public ICollection<VatReturn> Corrections { get; } = new List<VatReturn>();
    public ICollection<VatReturnBoxResult> Boxes { get; } = new List<VatReturnBoxResult>();
    public ICollection<VatReturnSourceContribution> Contributions { get; } = new List<VatReturnSourceContribution>();
    public ICollection<VatReturnValidationIssue> Issues { get; } = new List<VatReturnValidationIssue>();
    public ICollection<VatReturnReview> Reviews { get; } = new List<VatReturnReview>();

    public void ReplaceCalculation(DateTime cutoffUtc, string inputHash, string checksum,
        int includedSourceCount, int excludedSourceCount, decimal outputVat, decimal inputVat,
        decimal settlement, long filingSettlement, bool hasBlockingIssues)
    {
        if (Status == VatReturnStatuses.Locked)
            throw new InvalidOperationException("A locked VAT return is immutable.");
        CutoffUtc = Utc(cutoffUtc, nameof(cutoffUtc));
        InputHash = Hash(inputHash, nameof(inputHash));
        CalculationChecksum = Hash(checksum, nameof(checksum));
        IncludedSourceCount = Math.Max(0, includedSourceCount);
        ExcludedSourceCount = Math.Max(0, excludedSourceCount);
        OutputVatExact = decimal.Round(outputVat, 6, MidpointRounding.ToEven);
        InputVatExact = decimal.Round(inputVat, 6, MidpointRounding.ToEven);
        SettlementExact = decimal.Round(settlement, 6, MidpointRounding.ToEven);
        SettlementFilingAmount = filingSettlement;
        ApprovalRequestId = null;
        Status = hasBlockingIssues
            ? VatReturnStatuses.NeedsReview
            : VatReturnStatuses.Calculated;
        UpdatedUtc = CutoffUtc.Value;
    }

    public void AttachApproval(Guid approvalRequestId, DateTime utcNow)
    {
        if (Status != VatReturnStatuses.Calculated)
            throw new InvalidOperationException("Only a current calculated VAT return can be sent for approval.");
        ApprovalRequestId = Required(approvalRequestId, nameof(approvalRequestId));
        Status = VatReturnStatuses.NeedsReview;
        UpdatedUtc = Utc(utcNow, nameof(utcNow));
    }

    public void MarkApproved(DateTime utcNow)
    {
        if (!ApprovalRequestId.HasValue) throw new InvalidOperationException("Approval evidence is required.");
        Status = VatReturnStatuses.Approved;
        UpdatedUtc = Utc(utcNow, nameof(utcNow));
    }

    public void Finalize(Guid actorUserId, DateTime utcNow, string storageKey, string checksum,
        string fileName, string mediaType, long contentLength)
    {
        if (Status != VatReturnStatuses.Approved)
            throw new InvalidOperationException("Only an approved VAT return can be finalized.");
        FinalizedByUserId = Required(actorUserId, nameof(actorUserId));
        FinalizedUtc = Utc(utcNow, nameof(utcNow));
        PackageStorageKey = Text(storageKey, nameof(storageKey), 500);
        PackageChecksum = Hash(checksum, nameof(checksum));
        PackageFileName = Text(fileName, nameof(fileName), 180);
        PackageMediaType = Text(mediaType, nameof(mediaType), 100);
        PackageContentLength = contentLength > 0 ? contentLength : throw new ArgumentOutOfRangeException(nameof(contentLength));
        Status = VatReturnStatuses.Locked;
        UpdatedUtc = FinalizedUtc.Value;
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static string Text(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
    private static string Hash(string value, string name) => Text(value, name, 64).ToLowerInvariant();
    private static DateTime Utc(DateTime value, string name) => EntityTimestampNormalizer.NormalizeUtc(value, name);
}

public sealed class VatReturnBoxResult : ICompanyOwnedEntity
{
    private VatReturnBoxResult() { }
    public VatReturnBoxResult(Guid id, Guid companyId, Guid vatReturnId, string boxCode,
        string factType, decimal exactAmount, long filingAmount, string currency, int sourceCount)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; VatReturnId = vatReturnId;
        BoxCode = boxCode; FactType = factType; ExactAmount = exactAmount; FilingAmount = filingAmount;
        Currency = currency; SourceCount = sourceCount;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VatReturnId { get; private set; }
    public string BoxCode { get; private set; } = null!;
    public string FactType { get; private set; } = null!;
    public decimal ExactAmount { get; private set; }
    public long FilingAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public int SourceCount { get; private set; }
    public VatReturn VatReturn { get; private set; } = null!;
}

public sealed class VatReturnSourceContribution : ICompanyOwnedEntity
{
    private VatReturnSourceContribution() { }
    public VatReturnSourceContribution(Guid id, Guid companyId, Guid vatReturnId, Guid ledgerEntryId,
        string voucherNumber, DateOnly postingDate, string sourceType, string sourceId, string sourceVersion,
        string policyPackKey, string policyPackVersion, string taxRuleKey, string taxRuleVersion,
        string boxCode, string factType, decimal exactAmount, string currency, string sourceChecksum)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; VatReturnId = vatReturnId;
        LedgerEntryId = ledgerEntryId; VoucherNumber = voucherNumber; PostingDate = postingDate;
        SourceType = sourceType; SourceId = sourceId; SourceVersion = sourceVersion;
        PolicyPackKey = policyPackKey; PolicyPackVersion = policyPackVersion; TaxRuleKey = taxRuleKey;
        TaxRuleVersion = taxRuleVersion; BoxCode = boxCode; FactType = factType;
        ExactAmount = exactAmount; Currency = currency; SourceChecksum = sourceChecksum;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VatReturnId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public string VoucherNumber { get; private set; } = null!;
    public DateOnly PostingDate { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceId { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public string PolicyPackKey { get; private set; } = null!;
    public string PolicyPackVersion { get; private set; } = null!;
    public string TaxRuleKey { get; private set; } = null!;
    public string TaxRuleVersion { get; private set; } = null!;
    public string BoxCode { get; private set; } = null!;
    public string FactType { get; private set; } = null!;
    public decimal ExactAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string SourceChecksum { get; private set; } = null!;
    public VatReturn VatReturn { get; private set; } = null!;
}

public sealed class VatReturnValidationIssue : ICompanyOwnedEntity
{
    private VatReturnValidationIssue() { }
    public VatReturnValidationIssue(Guid id, Guid companyId, Guid vatReturnId, string code,
        string explanation, bool isBlocking, Guid? ledgerEntryId, string? sourceReference, decimal? difference)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; VatReturnId = vatReturnId;
        Code = code; Explanation = explanation; IsBlocking = isBlocking; LedgerEntryId = ledgerEntryId;
        SourceReference = sourceReference; Difference = difference;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VatReturnId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public bool IsBlocking { get; private set; }
    public Guid? LedgerEntryId { get; private set; }
    public string? SourceReference { get; private set; }
    public decimal? Difference { get; private set; }
    public VatReturn VatReturn { get; private set; } = null!;
}

public sealed class VatReturnReview : ICompanyOwnedEntity
{
    private VatReturnReview() { }
    public VatReturnReview(Guid id, Guid companyId, Guid vatReturnId, string action,
        Guid actorUserId, Guid? approvalRequestId, string evidenceHash, DateTime occurredUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; VatReturnId = vatReturnId;
        Action = action; ActorUserId = actorUserId; ApprovalRequestId = approvalRequestId;
        EvidenceHash = evidenceHash; OccurredUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VatReturnId { get; private set; }
    public string Action { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public string EvidenceHash { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; }
    public VatReturn VatReturn { get; private set; } = null!;
}
