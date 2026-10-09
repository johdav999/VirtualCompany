namespace VirtualCompany.Application.Finance;
public sealed record FinancialReportBlockerDto(string Code, string Explanation, Guid? SubjectId = null)
{
    public string Code { get; set; } = Code;
    public string Explanation { get; set; } = Explanation;
    public Guid? SubjectId { get; set; } = SubjectId;

    public FinancialReportBlockerDto() : this("", "", default !)
    {
    }
}

public sealed record FinancialReportDrilldownItemDto(Guid LedgerEntryLineId, Guid LedgerEntryId, string VoucherNumber, DateOnly PostingDate, string AccountCode, string AccountName, decimal Debit, decimal Credit, string Currency, decimal? DocumentDebit, decimal? DocumentCredit, string? DocumentCurrency, string? ExchangeRateIdentity, string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalLedgerEntryId, IReadOnlyList<Guid> DocumentIds, IReadOnlyList<string> DimensionPaths)
{
    public Guid LedgerEntryLineId { get; set; } = LedgerEntryLineId;
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string VoucherNumber { get; set; } = VoucherNumber;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public decimal Debit { get; set; } = Debit;
    public decimal Credit { get; set; } = Credit;
    public string Currency { get; set; } = Currency;
    public decimal? DocumentDebit { get; set; } = DocumentDebit;
    public decimal? DocumentCredit { get; set; } = DocumentCredit;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;
    public string? ExchangeRateIdentity { get; set; } = ExchangeRateIdentity;
    public string? SourceType { get; set; } = SourceType;
    public string? SourceId { get; set; } = SourceId;
    public string? SourceVersion { get; set; } = SourceVersion;
    public Guid? OriginalLedgerEntryId { get; set; } = OriginalLedgerEntryId;
    public IReadOnlyList<Guid> DocumentIds { get; set; } = DocumentIds;
    public IReadOnlyList<string> DimensionPaths { get; set; } = DimensionPaths;

    public FinancialReportDrilldownItemDto() : this(default !, default !, "", default !, "", "", default !, default !, "", default !, default !, default !, default !, default !, default !, default !, default !, [], [])
    {
    }
}

public sealed record FinancialReportControlTotalsDto(decimal TotalDebit, decimal TotalCredit, decimal NetAmount, decimal SourceControlAmount, decimal Difference, bool IsReconciled)
{
    public decimal TotalDebit { get; set; } = TotalDebit;
    public decimal TotalCredit { get; set; } = TotalCredit;
    public decimal NetAmount { get; set; } = NetAmount;
    public decimal SourceControlAmount { get; set; } = SourceControlAmount;
    public decimal Difference { get; set; } = Difference;
    public bool IsReconciled { get; set; } = IsReconciled;

    public FinancialReportControlTotalsDto() : this(default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinancialReportSnapshotDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string ReportKind, string CalculationVersion, string MappingVersion, string ParametersHash, string Checksum, Guid CreatedByUserId, DateTime CreatedUtc, CompleteFinancialReportDto Report, bool IsIdempotentReplay)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string ReportKind { get; set; } = ReportKind;
    public string CalculationVersion { get; set; } = CalculationVersion;
    public string MappingVersion { get; set; } = MappingVersion;
    public string ParametersHash { get; set; } = ParametersHash;
    public string Checksum { get; set; } = Checksum;
    public Guid CreatedByUserId { get; set; } = CreatedByUserId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public CompleteFinancialReportDto Report { get; set; } = Report;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public FinancialReportSnapshotDto() : this(default !, default !, default !, default !, default !, "", default !, "", default !, default !, new(), default !)
    {
    }
}

public sealed record FinancialReportProvenanceDto(IReadOnlyList<Guid> LedgerEntryIds, IReadOnlyList<Guid> LedgerEntryLineIds, IReadOnlyList<string> SourceReferences, IReadOnlyList<Guid> DocumentIds, IReadOnlyList<Guid> SubledgerItemIds, IReadOnlyList<string> DimensionPaths, IReadOnlyList<string> ExchangeRateIdentities)
{
    public IReadOnlyList<Guid> LedgerEntryIds { get; set; } = LedgerEntryIds;
    public IReadOnlyList<Guid> LedgerEntryLineIds { get; set; } = LedgerEntryLineIds;
    public IReadOnlyList<string> SourceReferences { get; set; } = SourceReferences;
    public IReadOnlyList<Guid> DocumentIds { get; set; } = DocumentIds;
    public IReadOnlyList<Guid> SubledgerItemIds { get; set; } = SubledgerItemIds;
    public IReadOnlyList<string> DimensionPaths { get; set; } = DimensionPaths;
    public IReadOnlyList<string> ExchangeRateIdentities { get; set; } = ExchangeRateIdentities;

    public FinancialReportProvenanceDto() : this([], [], [], [], [], [], [])
    {
    }
}

public sealed record FinancialReportDrilldownDto(Guid CompanyId, Guid FiscalPeriodId, string ReportKind, string LineKey, string ReportChecksum, IReadOnlyList<FinancialReportDrilldownItemDto> Items, int Page, int PageSize, long TotalCount, bool HasMore)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string ReportKind { get; set; } = ReportKind;
    public string LineKey { get; set; } = LineKey;
    public string ReportChecksum { get; set; } = ReportChecksum;
    public IReadOnlyList<FinancialReportDrilldownItemDto> Items { get; set; } = Items;
    public int Page { get; set; } = Page;
    public int PageSize { get; set; } = PageSize;
    public long TotalCount { get; set; } = TotalCount;
    public bool HasMore { get; set; } = HasMore;

    public FinancialReportDrilldownDto() : this(default !, default !, "", "", "", [], default !, default !, default !, default !)
    {
    }
}

public sealed record CompleteFinancialReportDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, string ReportKind, DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateOnly AsOfDate, string Currency, string CalculationVersion, string MappingVersion, string ParametersHash, string Checksum, bool IsClosed, bool IsReportingLocked, bool UsedSnapshot, Guid? SnapshotId, DateTime GeneratedUtc, IReadOnlyList<FinancialReportBlockerDto> Blockers, FinancialReportControlTotalsDto ControlTotals, IReadOnlyList<FinancialReportLineDto> Lines, int Page, int PageSize, long TotalLineCount, bool HasMore, long ReproducibilityBudgetMilliseconds, long ObservedDurationMilliseconds, Guid? ReportDefinitionVersionId = null, int? ReportDefinitionVersionNumber = null, string? ReportDefinitionHash = null)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public string ReportKind { get; set; } = ReportKind;
    public DateTime PeriodStartUtc { get; set; } = PeriodStartUtc;
    public DateTime PeriodEndUtc { get; set; } = PeriodEndUtc;
    public DateOnly AsOfDate { get; set; } = AsOfDate;
    public string Currency { get; set; } = Currency;
    public string CalculationVersion { get; set; } = CalculationVersion;
    public string MappingVersion { get; set; } = MappingVersion;
    public string ParametersHash { get; set; } = ParametersHash;
    public string Checksum { get; set; } = Checksum;
    public bool IsClosed { get; set; } = IsClosed;
    public bool IsReportingLocked { get; set; } = IsReportingLocked;
    public bool UsedSnapshot { get; set; } = UsedSnapshot;
    public Guid? SnapshotId { get; set; } = SnapshotId;
    public DateTime GeneratedUtc { get; set; } = GeneratedUtc;
    public IReadOnlyList<FinancialReportBlockerDto> Blockers { get; set; } = Blockers;
    public FinancialReportControlTotalsDto ControlTotals { get; set; } = ControlTotals;
    public IReadOnlyList<FinancialReportLineDto> Lines { get; set; } = Lines;
    public int Page { get; set; } = Page;
    public int PageSize { get; set; } = PageSize;
    public long TotalLineCount { get; set; } = TotalLineCount;
    public bool HasMore { get; set; } = HasMore;
    public long ReproducibilityBudgetMilliseconds { get; set; } = ReproducibilityBudgetMilliseconds;
    public long ObservedDurationMilliseconds { get; set; } = ObservedDurationMilliseconds;
    public Guid? ReportDefinitionVersionId { get; set; } = ReportDefinitionVersionId;
    public int? ReportDefinitionVersionNumber { get; set; } = ReportDefinitionVersionNumber;
    public string? ReportDefinitionHash { get; set; } = ReportDefinitionHash;

    public CompleteFinancialReportDto() : this(default !, default !, "", "", default !, default !, default !, "", "", "", "", "", default !, default !, default !, default !, default !, [], new(), [], default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinancialReportLineDto(string LineKey, string Section, string Label, decimal Amount, decimal? ComparativeAmount, decimal? RollingAmount, string Currency, int ItemCount, FinancialReportProvenanceDto Provenance, string? AccountCode = null, string? Classification = null, DateOnly? DueDate = null, int? DaysPastDue = null, decimal? DocumentCurrencyAmount = null, string? DocumentCurrency = null, decimal? FunctionalCurrencyAmount = null, string? FunctionalCurrency = null)
{
    public string LineKey { get; set; } = LineKey;
    public string Section { get; set; } = Section;
    public string Label { get; set; } = Label;
    public decimal Amount { get; set; } = Amount;
    public decimal? ComparativeAmount { get; set; } = ComparativeAmount;
    public decimal? RollingAmount { get; set; } = RollingAmount;
    public string Currency { get; set; } = Currency;
    public int ItemCount { get; set; } = ItemCount;
    public FinancialReportProvenanceDto Provenance { get; set; } = Provenance;
    public string? AccountCode { get; set; } = AccountCode;
    public string? Classification { get; set; } = Classification;
    public DateOnly? DueDate { get; set; } = DueDate;
    public int? DaysPastDue { get; set; } = DaysPastDue;
    public decimal? DocumentCurrencyAmount { get; set; } = DocumentCurrencyAmount;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal? FunctionalCurrencyAmount { get; set; } = FunctionalCurrencyAmount;
    public string? FunctionalCurrency { get; set; } = FunctionalCurrency;

    public FinancialReportLineDto() : this("", "", "", default !, default !, default !, "", default !, new(), default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}
