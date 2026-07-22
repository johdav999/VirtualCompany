namespace VirtualCompany.Web.Services;

public sealed class FinanceCashScenarioAnalysisRequestViewModel
{
    public int HorizonDays { get; set; } = 30;
    public decimal UpsideAdditionalInflows { get; set; }
    public decimal DownsideDelayedInflows { get; set; }
    public decimal DownsideAdditionalOutflows { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class FinanceCashScenarioAnalysisViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public FinanceCashPositionAgentViewModel CashPosition { get; set; } = new();
    public FinanceCashScenarioViewModel Baseline { get; set; } = new();
    public FinanceCashScenarioViewModel Upside { get; set; } = new();
    public FinanceCashScenarioViewModel Downside { get; set; } = new();
    public List<string> FreshnessWarnings { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class FinanceCashPositionAgentViewModel
{
    public decimal AvailableBalance { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AverageMonthlyBurn { get; set; }
    public int? EstimatedRunwayDays { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
}

public sealed class FinanceCashScenarioViewModel
{
    public string Scenario { get; set; } = string.Empty;
    public decimal StartingCash { get; set; }
    public decimal ProjectedInflows { get; set; }
    public decimal ProjectedOutflows { get; set; }
    public decimal EndingCash { get; set; }
    public decimal ChangeFromBaseline { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<string> Assumptions { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
}

public sealed class FinancePaymentRunAnalysisRequestViewModel
{
    public DateTime CutoffUtc { get; set; } = DateTime.UtcNow.AddDays(30);
    public decimal MinimumCashReserve { get; set; }
    public decimal? MaximumOutflow { get; set; }
    public List<string> IncludedCurrencies { get; set; } = [];
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class FinancePaymentRunAnalysisViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public string SnapshotToken { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
    public Dictionary<string, decimal> CashBeforeByCurrency { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, decimal> RecommendedOutflowByCurrency { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, decimal> CashAfterByCurrency { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FinancePaymentRunItemViewModel> Items { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class FinancePaymentRunItemViewModel
{
    public Guid BillId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal OutstandingAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime DueUtc { get; set; }
    public int PriorityScore { get; set; }
    public string Group { get; set; } = string.Empty;
    public List<string> ReasonCodes { get; set; } = [];
    public bool RequiresApproval { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public DateTime SourceVersionUtc { get; set; }
}

public sealed class CommitFinancePaymentRunRequestViewModel
{
    public Guid RecommendationRunId { get; set; }
    public string SnapshotToken { get; set; } = string.Empty;
    public FinancePaymentRunAnalysisRequestViewModel AnalysisRequest { get; set; } = new();
    public List<Guid> SelectedBillIds { get; set; } = [];
    public bool Reviewed { get; set; }
    public string ActorDisplayName { get; set; } = "Finance reviewer";
}

public sealed class CommitFinancePaymentRunResultViewModel
{
    public Guid RecommendationRunId { get; set; }
    public List<SupplierInvoicePaymentProposalAgentViewModel> PaymentProposals { get; set; } = [];
    public List<Guid> RejectedBillIds { get; set; } = [];
    public string Status { get; set; } = string.Empty;
}

public sealed class SupplierInvoicePaymentProposalAgentViewModel
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
}

public sealed class FinanceCollectionsPlanRequestViewModel
{
    public int HorizonDays { get; set; } = 90;
    public List<Guid> StrategicCustomerIds { get; set; } = [];
    public Guid? SalesAgentId { get; set; }
    public bool CreateStrategicAccountHandoffs { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class FinanceCollectionsPlanViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public List<FinanceCollectionsPlanItemViewModel> Items { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class FinanceCollectionsPlanItemViewModel
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal OutstandingAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DaysOverdue { get; set; }
    public int PriorityScore { get; set; }
    public string RiskBand { get; set; } = string.Empty;
    public string RecommendedStrategy { get; set; } = string.Empty;
    public DateTime NextReviewUtc { get; set; }
    public List<string> RiskFactors { get; set; } = [];
    public bool RoutineReminderAllowed { get; set; }
    public bool RequiresSalesHandoff { get; set; }
    public Guid? HandoffId { get; set; }
}

public sealed class FinanceAccountingTreatmentRequestViewModel
{
    public Guid BillId { get; set; }
    public DateTime? ServicePeriodStartUtc { get; set; }
    public DateTime? ServicePeriodEndUtc { get; set; }
    public bool IsCorrection { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class FinanceAccountingTreatmentViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public Guid BillId { get; set; }
    public List<FinanceAccountingCandidateViewModel> Candidates { get; set; } = [];
    public List<FinanceExcludedAccountingCandidateViewModel> ExcludedCandidates { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class FinanceAccountingCandidateViewModel
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int HistoricalUseCount { get; set; }
    public decimal Confidence { get; set; }
    public string? VatTreatment { get; set; }
    public string? PeriodTreatment { get; set; }
    public List<string> EvidenceSourceIds { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class FinanceExcludedAccountingCandidateViewModel
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
}

public sealed class FinanceClosePeriodOptionViewModel
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public bool IsReportingLocked { get; set; }
}

public sealed class FinanceCloseAnalysisRequestViewModel
{
    public Guid FiscalPeriodId { get; set; }
    public string ComparisonType { get; set; } = "actual_vs_budget";
    public string? PlanningVersion { get; set; }
    public decimal MaterialityAmount { get; set; } = 1000m;
    public decimal MaterialityPercentage { get; set; } = 10m;
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class FinanceCloseAnalysisViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public Guid FiscalPeriodId { get; set; }
    public string SnapshotToken { get; set; } = string.Empty;
    public bool IsReadyToClose { get; set; }
    public bool IsClosed { get; set; }
    public bool IsReportingLocked { get; set; }
    public List<FinanceVarianceContributionViewModel> MaterialVariances { get; set; } = [];
    public List<FinanceCloseChecklistItemViewModel> Checklist { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class FinanceVarianceContributionViewModel
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal ActualAmount { get; set; }
    public decimal ComparisonAmount { get; set; }
    public decimal VarianceAmount { get; set; }
    public decimal? VariancePercentage { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool IsMaterial { get; set; }
    public string SourceId { get; set; } = string.Empty;
}

public sealed class FinanceCloseChecklistItemViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime DueUtc { get; set; }
    public List<string> Dependencies { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
}
