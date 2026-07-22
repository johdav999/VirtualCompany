using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Finance;

public sealed record FinanceCashScenarioAnalysisRequest(
    int HorizonDays = 30,
    decimal UpsideAdditionalInflows = 0m,
    decimal DownsideDelayedInflows = 0m,
    decimal DownsideAdditionalOutflows = 0m,
    DateTime? AsOfUtc = null,
    string? Objective = null);

public sealed record FinanceCashScenarioDto(
    string Scenario,
    decimal StartingCash,
    decimal ProjectedInflows,
    decimal ProjectedOutflows,
    decimal EndingCash,
    decimal ChangeFromBaseline,
    string Currency,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> SourceIds);

public sealed record FinanceCashScenarioAnalysisResult(
    RoleAgentAnalysisResult Advice,
    FinanceCashPositionDto CashPosition,
    FinanceCashScenarioDto Baseline,
    FinanceCashScenarioDto Upside,
    FinanceCashScenarioDto Downside,
    IReadOnlyList<string> FreshnessWarnings,
    bool RequiresReview);

public sealed record FinancePaymentRunAnalysisRequest(
    DateTime CutoffUtc,
    decimal MinimumCashReserve,
    decimal? MaximumOutflow = null,
    IReadOnlyList<string>? IncludedCurrencies = null,
    DateTime? AsOfUtc = null,
    string? Objective = null);

public static class FinancePaymentRunGroups
{
    public const string Pay = "pay";
    public const string Defer = "defer";
    public const string DisputeOrReview = "dispute_or_review";
    public const string NotEligible = "not_eligible";
}

public sealed record FinancePaymentRunItemDto(
    Guid BillId,
    string BillNumber,
    Guid SupplierId,
    string SupplierName,
    decimal OutstandingAmount,
    string Currency,
    DateTime DueUtc,
    int PriorityScore,
    string Group,
    IReadOnlyList<string> ReasonCodes,
    bool RequiresApproval,
    string SourceId,
    DateTime SourceVersionUtc);

public sealed record FinancePaymentRunAnalysisResult(
    RoleAgentAnalysisResult Advice,
    string SnapshotToken,
    DateTime AsOfUtc,
    IReadOnlyDictionary<string, decimal> CashBeforeByCurrency,
    IReadOnlyDictionary<string, decimal> RecommendedOutflowByCurrency,
    IReadOnlyDictionary<string, decimal> CashAfterByCurrency,
    IReadOnlyList<FinancePaymentRunItemDto> Items,
    IReadOnlyList<string> MissingEvidence,
    bool RequiresReview);

public sealed record CommitFinancePaymentRunCommand(
    Guid RecommendationRunId,
    string SnapshotToken,
    FinancePaymentRunAnalysisRequest AnalysisRequest,
    IReadOnlyList<Guid> SelectedBillIds,
    bool Reviewed,
    string ActorDisplayName = "Finance reviewer");

public sealed record CommitFinancePaymentRunResult(
    Guid RecommendationRunId,
    IReadOnlyList<SupplierInvoicePaymentProposalDto> PaymentProposals,
    IReadOnlyList<Guid> RejectedBillIds,
    string Status);

public sealed record FinanceCollectionsPlanRequest(
    int HorizonDays = 90,
    IReadOnlyList<Guid>? StrategicCustomerIds = null,
    Guid? SalesAgentId = null,
    bool CreateStrategicAccountHandoffs = false,
    DateTime? AsOfUtc = null,
    string? Objective = null);

public sealed record FinanceCollectionsPlanItemDto(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    decimal OutstandingAmount,
    string Currency,
    int DaysOverdue,
    int PriorityScore,
    string RiskBand,
    string RecommendedStrategy,
    DateTime NextReviewUtc,
    IReadOnlyList<string> RiskFactors,
    bool RoutineReminderAllowed,
    bool RequiresSalesHandoff,
    Guid? HandoffId,
    string SourceId);

public sealed record FinanceCollectionsPlanResult(
    RoleAgentAnalysisResult Advice,
    IReadOnlyList<FinanceCollectionsPlanItemDto> Items,
    IReadOnlyList<string> MissingEvidence,
    bool RequiresReview);

public sealed record FinanceAccountingTreatmentRequest(
    Guid BillId,
    DateTime? ServicePeriodStartUtc = null,
    DateTime? ServicePeriodEndUtc = null,
    bool IsCorrection = false,
    DateTime? AsOfUtc = null,
    string? Objective = null);

public sealed record FinanceAccountingCandidateDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    int Rank,
    int HistoricalUseCount,
    decimal Confidence,
    string? VatTreatment,
    string? PeriodTreatment,
    IReadOnlyList<string> EvidenceSourceIds,
    IReadOnlyList<string> Warnings);

public sealed record FinanceExcludedAccountingCandidateDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string ReasonCode);

public sealed record FinanceAccountingTreatmentResult(
    RoleAgentAnalysisResult Advice,
    Guid BillId,
    IReadOnlyList<FinanceAccountingCandidateDto> Candidates,
    IReadOnlyList<FinanceExcludedAccountingCandidateDto> ExcludedCandidates,
    IReadOnlyList<string> MissingEvidence,
    bool RequiresReview);

public sealed record FinanceCloseAnalysisRequest(
    Guid FiscalPeriodId,
    string ComparisonType = FinanceVarianceComparisonTypes.ActualVsBudget,
    string? PlanningVersion = null,
    decimal MaterialityAmount = 1000m,
    decimal MaterialityPercentage = 10m,
    DateTime? AsOfUtc = null,
    string? Objective = null);

public sealed record FinanceVarianceContributionDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    decimal ActualAmount,
    decimal ComparisonAmount,
    decimal VarianceAmount,
    decimal? VariancePercentage,
    string Currency,
    bool IsMaterial,
    string SourceId);

public sealed record FinanceCloseChecklistItemDto(
    string Code,
    string Title,
    string Status,
    string Owner,
    DateTime DueUtc,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> SourceIds);

public sealed record FinanceCloseAnalysisResult(
    RoleAgentAnalysisResult Advice,
    Guid FiscalPeriodId,
    string SnapshotToken,
    bool IsReadyToClose,
    bool IsClosed,
    bool IsReportingLocked,
    IReadOnlyList<FinanceVarianceContributionDto> MaterialVariances,
    IReadOnlyList<FinanceCloseChecklistItemDto> Checklist,
    IReadOnlyList<string> MissingEvidence,
    bool RequiresReview);

public sealed record FinanceClosePeriodOptionDto(Guid Id, string Label, DateTime StartUtc, DateTime EndUtc,
    string Status, bool IsClosed, bool IsReportingLocked);

public interface IFinanceAgentDecisionService
{
    Task<IReadOnlyList<FinanceClosePeriodOptionDto>> ListClosePeriodsAsync(Guid companyId,
        CancellationToken cancellationToken);

    Task<FinanceCashScenarioAnalysisResult> AnalyzeCashAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        FinanceCashScenarioAnalysisRequest request, CancellationToken cancellationToken);

    Task<FinancePaymentRunAnalysisResult> AnalyzePaymentRunAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        FinancePaymentRunAnalysisRequest request, CancellationToken cancellationToken);

    Task<CommitFinancePaymentRunResult> CommitPaymentRunAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        CommitFinancePaymentRunCommand command, CancellationToken cancellationToken);

    Task<FinanceCollectionsPlanResult> AnalyzeCollectionsAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        FinanceCollectionsPlanRequest request, CancellationToken cancellationToken);

    Task<FinanceAccountingTreatmentResult> RecommendAccountingTreatmentAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        FinanceAccountingTreatmentRequest request, CancellationToken cancellationToken);

    Task<FinanceCloseAnalysisResult> AnalyzeCloseAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        FinanceCloseAnalysisRequest request, CancellationToken cancellationToken);
}
