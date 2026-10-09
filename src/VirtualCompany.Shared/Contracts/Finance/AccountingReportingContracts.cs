using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;
public sealed record AccountingTaxSummaryLineDto(string PolicyPackKey, string PolicyPackVersion, string TaxRuleKey, string TaxTreatment, decimal TaxableAmount, decimal TaxAmount, string Currency, int JournalLineCount, IReadOnlyList<Guid> LedgerEntryIds)
{
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public string TaxRuleKey { get; set; } = TaxRuleKey;
    public string TaxTreatment { get; set; } = TaxTreatment;
    public decimal TaxableAmount { get; set; } = TaxableAmount;
    public decimal TaxAmount { get; set; } = TaxAmount;
    public string Currency { get; set; } = Currency;
    public int JournalLineCount { get; set; } = JournalLineCount;
    public IReadOnlyList<Guid> LedgerEntryIds { get; set; } = LedgerEntryIds;

    public AccountingTaxSummaryLineDto() : this("", "", "", "", default !, default !, "", default !, default !)
    {
    }
}

public sealed record GeneralLedgerAccountDto(Guid AccountId, string AccountCode, string AccountName, string AccountClass, string Currency, decimal OpeningBalance, decimal Debit, decimal Credit, decimal ClosingBalance, int TotalLineCount, IReadOnlyList<GeneralLedgerLineDto> Lines)
{
    public Guid AccountId { get; set; } = AccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string AccountClass { get; set; } = AccountClass;
    public string Currency { get; set; } = Currency;
    public decimal OpeningBalance { get; set; } = OpeningBalance;
    public decimal Debit { get; set; } = Debit;
    public decimal Credit { get; set; } = Credit;
    public decimal ClosingBalance { get; set; } = ClosingBalance;
    public int TotalLineCount { get; set; } = TotalLineCount;
    public IReadOnlyList<GeneralLedgerLineDto> Lines { get; set; } = Lines;

    public GeneralLedgerAccountDto() : this(default !, "", "", "", "", default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record TrialBalanceAccountDto(Guid AccountId, string AccountCode, string AccountName, string AccountClass, string Currency, decimal OpeningBalance, decimal Debit, decimal Credit, decimal ClosingBalance, int JournalLineCount)
{
    public Guid AccountId { get; set; } = AccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string AccountClass { get; set; } = AccountClass;
    public string Currency { get; set; } = Currency;
    public decimal OpeningBalance { get; set; } = OpeningBalance;
    public decimal Debit { get; set; } = Debit;
    public decimal Credit { get; set; } = Credit;
    public decimal ClosingBalance { get; set; } = ClosingBalance;
    public int JournalLineCount { get; set; } = JournalLineCount;

    public TrialBalanceAccountDto() : this(default !, "", "", "", "", default !, default !, default !, default !, default !)
    {
    }
}

public sealed record GeneralLedgerLineDto(Guid LedgerEntryLineId, Guid LedgerEntryId, string VoucherNumber, DateOnly PostingDate, string? Description, decimal Debit, decimal Credit, decimal RunningBalance, string Currency, string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalLedgerEntryId, IReadOnlyList<AccountingEvidenceReferenceDto> Evidence)
{
    public Guid LedgerEntryLineId { get; set; } = LedgerEntryLineId;
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string VoucherNumber { get; set; } = VoucherNumber;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public string? Description { get; set; } = Description;
    public decimal Debit { get; set; } = Debit;
    public decimal Credit { get; set; } = Credit;
    public decimal RunningBalance { get; set; } = RunningBalance;
    public string Currency { get; set; } = Currency;
    public string? SourceType { get; set; } = SourceType;
    public string? SourceId { get; set; } = SourceId;
    public string? SourceVersion { get; set; } = SourceVersion;
    public Guid? OriginalLedgerEntryId { get; set; } = OriginalLedgerEntryId;
    public IReadOnlyList<AccountingEvidenceReferenceDto> Evidence { get; set; } = Evidence;

    public GeneralLedgerLineDto() : this(default !, default !, "", default !, default !, default !, default !, default !, "", default !, default !, default !, default !, [])
    {
    }
}

public sealed record GeneralLedgerReportDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, DateTime PeriodStartUtc, DateTime PeriodEndUtc, bool IsClosed, bool IsReportingLocked, string SourceMode, IReadOnlyList<GeneralLedgerAccountDto> Accounts, int Page, int PageSize, long TotalLineCount, bool HasMore)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public DateTime PeriodStartUtc { get; set; } = PeriodStartUtc;
    public DateTime PeriodEndUtc { get; set; } = PeriodEndUtc;
    public bool IsClosed { get; set; } = IsClosed;
    public bool IsReportingLocked { get; set; } = IsReportingLocked;
    public string SourceMode { get; set; } = SourceMode;
    public IReadOnlyList<GeneralLedgerAccountDto> Accounts { get; set; } = Accounts;
    public int Page { get; set; } = Page;
    public int PageSize { get; set; } = PageSize;
    public long TotalLineCount { get; set; } = TotalLineCount;
    public bool HasMore { get; set; } = HasMore;

    public GeneralLedgerReportDto() : this(default !, default !, "", default !, default !, default !, default !, default !, [], default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingExportJobDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string Status, int AttemptCount, DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, DateTime ExpiresUtc, string? Checksum, string? FileName, string? MediaType, long? ContentLength, string? FailureCode, string? FailureSummary, bool CanDownload, string ExportType = "generic_json", string? SpecificationVersion = null, string? InputChecksum = null, string? EncodingName = null, int? SourceAccountCount = null, int? SourceJournalCount = null, int? SourceLineCount = null, decimal? SourceDebitTotal = null, decimal? SourceCreditTotal = null, string? CorrelationId = null)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string Status { get; set; } = Status;
    public int AttemptCount { get; set; } = AttemptCount;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? StartedUtc { get; set; } = StartedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public DateTime ExpiresUtc { get; set; } = ExpiresUtc;
    public string? Checksum { get; set; } = Checksum;
    public string? FileName { get; set; } = FileName;
    public string? MediaType { get; set; } = MediaType;
    public long? ContentLength { get; set; } = ContentLength;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public bool CanDownload { get; set; } = CanDownload;
    public string ExportType { get; set; } = ExportType;
    public string? SpecificationVersion { get; set; } = SpecificationVersion;
    public string? InputChecksum { get; set; } = InputChecksum;
    public string? EncodingName { get; set; } = EncodingName;
    public int? SourceAccountCount { get; set; } = SourceAccountCount;
    public int? SourceJournalCount { get; set; } = SourceJournalCount;
    public int? SourceLineCount { get; set; } = SourceLineCount;
    public decimal? SourceDebitTotal { get; set; } = SourceDebitTotal;
    public decimal? SourceCreditTotal { get; set; } = SourceCreditTotal;
    public string? CorrelationId { get; set; } = CorrelationId;

    public AccountingExportJobDto() : this(default !, default !, default !, "", default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, "generic_json", default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingPeriodHistoryDto(Guid Id, string Action, Guid ActorUserId, string Reason, string? SnapshotChecksum, DateTime OccurredUtc)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public string Reason { get; set; } = Reason;
    public string? SnapshotChecksum { get; set; } = SnapshotChecksum;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;

    public AccountingPeriodHistoryDto() : this(default !, "", default !, "", default !, default !)
    {
    }
}

public sealed record ControlAccountReconciliationLineDto(string RoleKey, Guid AccountId, string AccountCode, string AccountName, string Currency, decimal LedgerBalance, decimal SourcePostingBalance, decimal Difference, bool IsReconciled, IReadOnlyList<Guid> DifferenceJournalEntryIds)
{
    public string RoleKey { get; set; } = RoleKey;
    public Guid AccountId { get; set; } = AccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string Currency { get; set; } = Currency;
    public decimal LedgerBalance { get; set; } = LedgerBalance;
    public decimal SourcePostingBalance { get; set; } = SourcePostingBalance;
    public decimal Difference { get; set; } = Difference;
    public bool IsReconciled { get; set; } = IsReconciled;
    public IReadOnlyList<Guid> DifferenceJournalEntryIds { get; set; } = DifferenceJournalEntryIds;

    public ControlAccountReconciliationLineDto() : this("", default !, "", "", "", default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingEvidenceReferenceDto(Guid DocumentId, string Title, string ContentHash)
{
    public Guid DocumentId { get; set; } = DocumentId;
    public string Title { get; set; } = Title;
    public string ContentHash { get; set; } = ContentHash;

    public AccountingEvidenceReferenceDto() : this(default !, "", "")
    {
    }
}

public sealed record TrialBalanceReportDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, DateTime PeriodStartUtc, DateTime PeriodEndUtc, bool IsClosed, bool IsReportingLocked, string SourceMode, string Checksum, decimal TotalOpeningDebits, decimal TotalOpeningCredits, decimal TotalDebits, decimal TotalCredits, decimal TotalClosingDebits, decimal TotalClosingCredits, bool IsBalanced, IReadOnlyList<TrialBalanceAccountDto> Accounts)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public DateTime PeriodStartUtc { get; set; } = PeriodStartUtc;
    public DateTime PeriodEndUtc { get; set; } = PeriodEndUtc;
    public bool IsClosed { get; set; } = IsClosed;
    public bool IsReportingLocked { get; set; } = IsReportingLocked;
    public string SourceMode { get; set; } = SourceMode;
    public string Checksum { get; set; } = Checksum;
    public decimal TotalOpeningDebits { get; set; } = TotalOpeningDebits;
    public decimal TotalOpeningCredits { get; set; } = TotalOpeningCredits;
    public decimal TotalDebits { get; set; } = TotalDebits;
    public decimal TotalCredits { get; set; } = TotalCredits;
    public decimal TotalClosingDebits { get; set; } = TotalClosingDebits;
    public decimal TotalClosingCredits { get; set; } = TotalClosingCredits;
    public bool IsBalanced { get; set; } = IsBalanced;
    public IReadOnlyList<TrialBalanceAccountDto> Accounts { get; set; } = Accounts;

    public TrialBalanceReportDto() : this(default !, default !, "", default !, default !, default !, default !, "", "", default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record ControlAccountReconciliationDto(Guid CompanyId, Guid FiscalPeriodId, bool IsReconciled, IReadOnlyList<ControlAccountReconciliationLineDto> Accounts)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public bool IsReconciled { get; set; } = IsReconciled;
    public IReadOnlyList<ControlAccountReconciliationLineDto> Accounts { get; set; } = Accounts;

    public ControlAccountReconciliationDto() : this(default !, default !, default !, [])
    {
    }
}

public sealed record AccountingTaxSummaryDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, bool IsCountryNeutral, bool IsStatutoryComplianceValidated, string Label, string ComplianceNotice, string Checksum, bool IsReviewed, Guid? ReviewedByUserId, DateTime? ReviewedUtc, IReadOnlyList<AccountingTaxSummaryLineDto> Lines)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public bool IsCountryNeutral { get; set; } = IsCountryNeutral;
    public bool IsStatutoryComplianceValidated { get; set; } = IsStatutoryComplianceValidated;
    public string Label { get; set; } = Label;
    public string ComplianceNotice { get; set; } = ComplianceNotice;
    public string Checksum { get; set; } = Checksum;
    public bool IsReviewed { get; set; } = IsReviewed;
    public Guid? ReviewedByUserId { get; set; } = ReviewedByUserId;
    public DateTime? ReviewedUtc { get; set; } = ReviewedUtc;
    public IReadOnlyList<AccountingTaxSummaryLineDto> Lines { get; set; } = Lines;

    public AccountingTaxSummaryDto() : this(default !, default !, default !, default !, default !, "", "", "", default !, default !, default !, [])
    {
    }
}
