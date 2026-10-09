namespace VirtualCompany.Application.Finance;
public sealed record VatReturnSourceContributionDto(Guid Id, Guid LedgerEntryId, string VoucherNumber, DateOnly PostingDate, string SourceType, string SourceId, string SourceVersion, string PolicyPackKey, string PolicyPackVersion, string TaxRuleKey, string TaxRuleVersion, string BoxCode, string FactType, decimal ExactAmount, string Currency, string SourceChecksum)
{
    public Guid Id { get; set; } = Id;
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string VoucherNumber { get; set; } = VoucherNumber;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public string SourceType { get; set; } = SourceType;
    public string SourceId { get; set; } = SourceId;
    public string SourceVersion { get; set; } = SourceVersion;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public string TaxRuleKey { get; set; } = TaxRuleKey;
    public string TaxRuleVersion { get; set; } = TaxRuleVersion;
    public string BoxCode { get; set; } = BoxCode;
    public string FactType { get; set; } = FactType;
    public decimal ExactAmount { get; set; } = ExactAmount;
    public string Currency { get; set; } = Currency;
    public string SourceChecksum { get; set; } = SourceChecksum;

    public VatReturnSourceContributionDto() : this(default !, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, string.Empty, string.Empty, default !, string.Empty, default !)
    {
    }
}

public sealed record VatReturnDto(Guid Id, Guid CompanyId, Guid FilingPeriodId, string PeriodCode, DateOnly StartDate, DateOnly EndDate, string Currency, int Version, string Status, bool IsStale, bool IsSuperseded, Guid? CorrectionOfVatReturnId, string? CorrectionReason, string? CorrectionEvidenceReference, DateTime? CutoffUtc, string? InputHash, string? CalculationChecksum, int IncludedSourceCount, int ExcludedSourceCount, decimal OutputVatExact, decimal InputVatExact, decimal SettlementExact, long SettlementFilingAmount, Guid? ApprovalRequestId, string? ApprovalStatus, Guid? FinalizedByUserId, DateTime? FinalizedUtc, string? PackageChecksum, string? PackageFileName, string? PackageMediaType, long? PackageContentLength, bool CanDownloadPackage, IReadOnlyList<VatReturnBoxResultDto> Boxes, IReadOnlyList<VatReturnSourceContributionDto> Contributions, IReadOnlyList<VatReturnValidationIssueDto> Issues, IReadOnlyList<VatReturnReviewDto> Reviews, IReadOnlyList<string> AllowedActions)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FilingPeriodId { get; set; } = FilingPeriodId;
    public string PeriodCode { get; set; } = PeriodCode;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;
    public string Currency { get; set; } = Currency;
    public int Version { get; set; } = Version;
    public string Status { get; set; } = Status;
    public bool IsStale { get; set; } = IsStale;
    public bool IsSuperseded { get; set; } = IsSuperseded;
    public Guid? CorrectionOfVatReturnId { get; set; } = CorrectionOfVatReturnId;
    public string? CorrectionReason { get; set; } = CorrectionReason;
    public string? CorrectionEvidenceReference { get; set; } = CorrectionEvidenceReference;
    public DateTime? CutoffUtc { get; set; } = CutoffUtc;
    public string? InputHash { get; set; } = InputHash;
    public string? CalculationChecksum { get; set; } = CalculationChecksum;
    public int IncludedSourceCount { get; set; } = IncludedSourceCount;
    public int ExcludedSourceCount { get; set; } = ExcludedSourceCount;
    public decimal OutputVatExact { get; set; } = OutputVatExact;
    public decimal InputVatExact { get; set; } = InputVatExact;
    public decimal SettlementExact { get; set; } = SettlementExact;
    public long SettlementFilingAmount { get; set; } = SettlementFilingAmount;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string? ApprovalStatus { get; set; } = ApprovalStatus;
    public Guid? FinalizedByUserId { get; set; } = FinalizedByUserId;
    public DateTime? FinalizedUtc { get; set; } = FinalizedUtc;
    public string? PackageChecksum { get; set; } = PackageChecksum;
    public string? PackageFileName { get; set; } = PackageFileName;
    public string? PackageMediaType { get; set; } = PackageMediaType;
    public long? PackageContentLength { get; set; } = PackageContentLength;
    public bool CanDownloadPackage { get; set; } = CanDownloadPackage;
    public IReadOnlyList<VatReturnBoxResultDto> Boxes { get; set; } = Boxes;
    public IReadOnlyList<VatReturnSourceContributionDto> Contributions { get; set; } = Contributions;
    public IReadOnlyList<VatReturnValidationIssueDto> Issues { get; set; } = Issues;
    public IReadOnlyList<VatReturnReviewDto> Reviews { get; set; } = Reviews;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;

    public VatReturnDto() : this(default !, default !, default !, string.Empty, default !, default !, string.Empty, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], [], [], [])
    {
    }
}

public sealed record VatFilingPeriodDto(Guid Id, Guid CompanyId, string PeriodCode, DateOnly StartDate, DateOnly EndDate, string Currency, Guid? FiscalPeriodId, DateTime CreatedUtc, DateOnly? DueDate = null)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string PeriodCode { get; set; } = PeriodCode;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;
    public string Currency { get; set; } = Currency;
    public Guid? FiscalPeriodId { get; set; } = FiscalPeriodId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateOnly? DueDate { get; set; } = DueDate;

    public VatFilingPeriodDto() : this(default !, default !, string.Empty, default !, default !, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record VatReturnValidationIssueDto(Guid Id, string Code, string Explanation, bool IsBlocking, Guid? LedgerEntryId, string? SourceReference, decimal? Difference = null)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Explanation { get; set; } = Explanation;
    public bool IsBlocking { get; set; } = IsBlocking;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public string? SourceReference { get; set; } = SourceReference;
    public decimal? Difference { get; set; } = Difference;

    public VatReturnValidationIssueDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record VatReturnReviewDto(Guid Id, string Action, Guid ActorUserId, Guid? ApprovalRequestId, string EvidenceHash, DateTime OccurredUtc)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string EvidenceHash { get; set; } = EvidenceHash;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;

    public VatReturnReviewDto() : this(default !, string.Empty, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record VatReturnBoxResultDto(string BoxCode, string FactType, decimal ExactAmount, long FilingAmount, string Currency, int SourceCount)
{
    public string BoxCode { get; set; } = BoxCode;
    public string FactType { get; set; } = FactType;
    public decimal ExactAmount { get; set; } = ExactAmount;
    public long FilingAmount { get; set; } = FilingAmount;
    public string Currency { get; set; } = Currency;
    public int SourceCount { get; set; } = SourceCount;

    public VatReturnBoxResultDto() : this(string.Empty, string.Empty, default !, default !, default !, default !)
    {
    }
}
