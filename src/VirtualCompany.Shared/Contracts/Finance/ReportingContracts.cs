using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record BalanceSheetReportDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, DateTime PeriodStartUtc, DateTime PeriodEndUtc, bool IsClosed, bool UsedSnapshot, string Currency, IReadOnlyList<FinanceStatementLineDto> AssetLines, IReadOnlyList<FinanceStatementLineDto> LiabilityLines, IReadOnlyList<FinanceStatementLineDto> EquityLines, decimal TotalAssets, decimal TotalLiabilities, decimal TotalEquity, bool IsBalanced, FinancialStatementSnapshotMetadataDto? Snapshot)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public DateTime PeriodStartUtc { get; set; } = PeriodStartUtc;
    public DateTime PeriodEndUtc { get; set; } = PeriodEndUtc;
    public bool IsClosed { get; set; } = IsClosed;
    public bool UsedSnapshot { get; set; } = UsedSnapshot;
    public string Currency { get; set; } = Currency;
    public IReadOnlyList<FinanceStatementLineDto> AssetLines { get; set; } = AssetLines;
    public IReadOnlyList<FinanceStatementLineDto> LiabilityLines { get; set; } = LiabilityLines;
    public IReadOnlyList<FinanceStatementLineDto> EquityLines { get; set; } = EquityLines;
    public decimal TotalAssets { get; set; } = TotalAssets;
    public decimal TotalLiabilities { get; set; } = TotalLiabilities;
    public decimal TotalEquity { get; set; } = TotalEquity;
    public bool IsBalanced { get; set; } = IsBalanced;
    public FinancialStatementSnapshotMetadataDto? Snapshot { get; set; } = Snapshot;

    public BalanceSheetReportDto() : this(default !, default !, "", default !, default !, default !, default !, "", [], [], [], default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceAccountBalanceDto(Guid AccountId, string AccountCode, string AccountName, string AccountType, decimal Amount, string Currency, DateTime AsOfUtc)
{
    public Guid AccountId { get; set; } = AccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string AccountType { get; set; } = AccountType;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public DateTime AsOfUtc { get; set; } = AsOfUtc;

    public FinanceAccountBalanceDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !)
    {
    }
}

public sealed record FinanceStatementLineDto(Guid? FinanceAccountId, string AccountCode, string AccountName, string ReportSection, string LineClassification, decimal Amount, string Currency)
{
    public Guid? FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string ReportSection { get; set; } = ReportSection;
    public string LineClassification { get; set; } = LineClassification;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;

    public FinanceStatementLineDto() : this(default !, "", "", "", "", default !, "")
    {
    }
}

public sealed record ReportingPeriodLockStateDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, bool IsClosed, bool IsReportingLocked, DateTime? ReportingLockedAtUtc, Guid? ReportingLockedByUserId, DateTime? ReportingUnlockedAtUtc, Guid? ReportingUnlockedByUserId, DateTime? LastCloseValidatedAtUtc, Guid? LastCloseValidatedByUserId, DateTime UpdatedAtUtc)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public bool IsClosed { get; set; } = IsClosed;
    public bool IsReportingLocked { get; set; } = IsReportingLocked;
    public DateTime? ReportingLockedAtUtc { get; set; } = ReportingLockedAtUtc;
    public Guid? ReportingLockedByUserId { get; set; } = ReportingLockedByUserId;
    public DateTime? ReportingUnlockedAtUtc { get; set; } = ReportingUnlockedAtUtc;
    public Guid? ReportingUnlockedByUserId { get; set; } = ReportingUnlockedByUserId;
    public DateTime? LastCloseValidatedAtUtc { get; set; } = LastCloseValidatedAtUtc;
    public Guid? LastCloseValidatedByUserId { get; set; } = LastCloseValidatedByUserId;
    public DateTime UpdatedAtUtc { get; set; } = UpdatedAtUtc;

    public ReportingPeriodLockStateDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceExpenseCategoryDto(string Category, decimal Amount, string Currency)
{
    public string Category { get; set; } = Category;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;

    public FinanceExpenseCategoryDto() : this(string.Empty, default !, string.Empty)
    {
    }
}

public sealed record FinanceExpenseBreakdownDto(Guid CompanyId, DateTime StartUtc, DateTime EndUtc, decimal TotalExpenses, string Currency, IReadOnlyList<FinanceExpenseCategoryDto> Categories)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateTime StartUtc { get; set; } = StartUtc;
    public DateTime EndUtc { get; set; } = EndUtc;
    public decimal TotalExpenses { get; set; } = TotalExpenses;
    public string Currency { get; set; } = Currency;
    public IReadOnlyList<FinanceExpenseCategoryDto> Categories { get; set; } = Categories;

    public FinanceExpenseBreakdownDto() : this(default !, default !, default !, default !, string.Empty, [])
    {
    }
}

public sealed record ReportingPeriodBlockingIssueDto(string Code, string Message, int Count, IReadOnlyList<string> SampleReferences, decimal? Amount = null, string? Currency = null, IReadOnlyList<string>? RecordLinks = null, string? Remediation = null, IReadOnlyDictionary<string, string>? Evidence = null)
{
    public string Code { get; set; } = Code;
    public string Message { get; set; } = Message;
    public int Count { get; set; } = Count;
    public IReadOnlyList<string> SampleReferences { get; set; } = SampleReferences;
    public decimal? Amount { get; set; } = Amount;
    public string? Currency { get; set; } = Currency;
    public IReadOnlyList<string>? RecordLinks { get; set; } = RecordLinks;
    public string? Remediation { get; set; } = Remediation;
    public IReadOnlyDictionary<string, string>? Evidence { get; set; } = Evidence;

    public ReportingPeriodBlockingIssueDto() : this("", "", default !, [], default !, default !, [], default !, default !)
    {
    }
}

public sealed record FinanceCashPositionDto(Guid CompanyId, DateTime AsOfUtc, decimal AvailableBalance, string Currency, decimal AverageMonthlyBurn, int? EstimatedRunwayDays, FinanceCashPositionThresholdsDto Thresholds, FinanceCashPositionAlertStateDto AlertState, FinanceWorkflowOutputSchemaDto WorkflowOutput)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateTime AsOfUtc { get; set; } = AsOfUtc;
    public decimal AvailableBalance { get; set; } = AvailableBalance;
    public string Currency { get; set; } = Currency;
    public decimal AverageMonthlyBurn { get; set; } = AverageMonthlyBurn;
    public int? EstimatedRunwayDays { get; set; } = EstimatedRunwayDays;
    public FinanceCashPositionThresholdsDto Thresholds { get; set; } = Thresholds;
    public FinanceCashPositionAlertStateDto AlertState { get; set; } = AlertState;
    public FinanceWorkflowOutputSchemaDto WorkflowOutput { get; set; } = WorkflowOutput;
    public string Classification => WorkflowOutput.Classification;
    public string RiskLevel => WorkflowOutput.RiskLevel;
    public string RecommendedAction => WorkflowOutput.RecommendedAction;
    public string Rationale => WorkflowOutput.Rationale;
    public decimal Confidence => WorkflowOutput.Confidence;
    public string SourceWorkflow => WorkflowOutput.SourceWorkflow;

    public FinanceCashPositionDto() : this(default !, default !, default !, string.Empty, default !, default !, new(), new(), new("", "", "", "", 0m, ""))
    {
    }
}

public sealed record ProfitAndLossReportDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, DateTime PeriodStartUtc, DateTime PeriodEndUtc, bool IsClosed, bool UsedSnapshot, string Currency, IReadOnlyList<FinanceStatementLineDto> RevenueLines, IReadOnlyList<FinanceStatementLineDto> ExpenseLines, decimal TotalRevenue, decimal TotalExpenses, decimal NetIncome, FinancialStatementSnapshotMetadataDto? Snapshot)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public DateTime PeriodStartUtc { get; set; } = PeriodStartUtc;
    public DateTime PeriodEndUtc { get; set; } = PeriodEndUtc;
    public bool IsClosed { get; set; } = IsClosed;
    public bool UsedSnapshot { get; set; } = UsedSnapshot;
    public string Currency { get; set; } = Currency;
    public IReadOnlyList<FinanceStatementLineDto> RevenueLines { get; set; } = RevenueLines;
    public IReadOnlyList<FinanceStatementLineDto> ExpenseLines { get; set; } = ExpenseLines;
    public decimal TotalRevenue { get; set; } = TotalRevenue;
    public decimal TotalExpenses { get; set; } = TotalExpenses;
    public decimal NetIncome { get; set; } = NetIncome;
    public FinancialStatementSnapshotMetadataDto? Snapshot { get; set; } = Snapshot;

    public ProfitAndLossReportDto() : this(default !, default !, "", default !, default !, default !, default !, "", [], [], default !, default !, default !, default !)
    {
    }
}

public sealed record FinancialStatementSnapshotMetadataDto(Guid SnapshotId, int VersionNumber, string BalancesChecksum, DateTime GeneratedAtUtc, DateTime SourcePeriodStartUtc, DateTime SourcePeriodEndUtc, string Currency);
public sealed record FinanceMonthlyProfitAndLossDto(Guid CompanyId, int Year, int Month, DateTime StartUtc, DateTime EndUtc, decimal Revenue, decimal Expenses, decimal NetResult, string Currency)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public int Year { get; set; } = Year;
    public int Month { get; set; } = Month;
    public DateTime StartUtc { get; set; } = StartUtc;
    public DateTime EndUtc { get; set; } = EndUtc;
    public decimal Revenue { get; set; } = Revenue;
    public decimal Expenses { get; set; } = Expenses;
    public decimal NetResult { get; set; } = NetResult;
    public string Currency { get; set; } = Currency;

    public FinanceMonthlyProfitAndLossDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record FinancialStatementDrilldownJournalEntryDto(Guid LedgerEntryId, string EntryNumber, DateTime EntryUtc, string? Description, decimal TotalContributionAmount, IReadOnlyList<FinancialStatementDrilldownJournalLineDto> Lines)
{
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string EntryNumber { get; set; } = EntryNumber;
    public DateTime EntryUtc { get; set; } = EntryUtc;
    public string? Description { get; set; } = Description;
    public decimal TotalContributionAmount { get; set; } = TotalContributionAmount;
    public IReadOnlyList<FinancialStatementDrilldownJournalLineDto> Lines { get; set; } = Lines;

    public FinancialStatementDrilldownJournalEntryDto() : this(default !, "", default !, default !, default !, default !)
    {
    }
}

public sealed record FinancialStatementDrilldownJournalLineDto(Guid LedgerEntryLineId, Guid FinanceAccountId, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, decimal ContributionAmount, string Currency, string? Description);
public sealed record FinancialStatementDrilldownLineDto(string LineCode, string LineName, string ReportSection, string LineClassification, decimal Amount, string Currency)
{
    public string LineCode { get; set; } = LineCode;
    public string LineName { get; set; } = LineName;
    public string ReportSection { get; set; } = ReportSection;
    public string LineClassification { get; set; } = LineClassification;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;

    public FinancialStatementDrilldownLineDto() : this(default !, default !, "", "", default !, default !)
    {
    }
}

public sealed record FinancialStatementDrilldownDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, string StatementType, string SourceMode, FinancialStatementSnapshotMetadataDto? Snapshot, FinancialStatementDrilldownLineDto SelectedLine, decimal OpeningBalanceAdjustment, decimal JournalLineTotal, decimal ReconciliationTotal, decimal ReconciliationDelta, IReadOnlyList<FinancialStatementDrilldownJournalEntryDto> JournalEntries)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public string StatementType { get; set; } = StatementType;
    public string SourceMode { get; set; } = SourceMode;
    public FinancialStatementSnapshotMetadataDto? Snapshot { get; set; } = Snapshot;
    public FinancialStatementDrilldownLineDto SelectedLine { get; set; } = SelectedLine;
    public decimal OpeningBalanceAdjustment { get; set; } = OpeningBalanceAdjustment;
    public decimal JournalLineTotal { get; set; } = JournalLineTotal;
    public decimal ReconciliationTotal { get; set; } = ReconciliationTotal;
    public decimal ReconciliationDelta { get; set; } = ReconciliationDelta;
    public IReadOnlyList<FinancialStatementDrilldownJournalEntryDto> JournalEntries { get; set; } = JournalEntries;

    public FinancialStatementDrilldownDto() : this(default, default, string.Empty, string.Empty, string.Empty, null, default!, default, default, default, default, [])
    {
    }
}

public sealed record ReportingPeriodCloseValidationResultDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, DateTime ExecutedAtUtc, string ActorType, Guid? ActorId, Guid MembershipId, string MembershipRole, bool IsReadyToClose, bool IsClosed, bool IsReportingLocked, IReadOnlyList<ReportingPeriodBlockingIssueDto> Issues)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public DateTime ExecutedAtUtc { get; set; } = ExecutedAtUtc;
    public string ActorType { get; set; } = ActorType;
    public Guid? ActorId { get; set; } = ActorId;
    public Guid MembershipId { get; set; } = MembershipId;
    public string MembershipRole { get; set; } = MembershipRole;
    public bool IsReadyToClose { get; set; } = IsReadyToClose;
    public bool IsClosed { get; set; } = IsClosed;
    public bool IsReportingLocked { get; set; } = IsReportingLocked;

    [System.Text.Json.Serialization.JsonPropertyName("blockingIssues")]
    public IReadOnlyList<ReportingPeriodBlockingIssueDto> Issues { get; set; } = Issues;

    public ReportingPeriodCloseValidationResultDto() : this(default, default, string.Empty, default, string.Empty, null, default, string.Empty, default, default, default, [])
    {
    }
}
