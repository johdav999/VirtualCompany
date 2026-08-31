namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CurrencyRevaluationRunListResponse?> GetCurrencyRevaluationRunsAsync(Guid companyId,
        Guid fiscalPeriodId, CancellationToken cancellationToken = default) =>
        GetAsync<CurrencyRevaluationRunListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/currency-revaluations?fiscalPeriodId={fiscalPeriodId:D}",
            false, cancellationToken);

    public Task<CurrencyRevaluationRunResponse?> GetCurrencyRevaluationRunAsync(Guid companyId, Guid runId,
        CancellationToken cancellationToken = default) => GetAsync<CurrencyRevaluationRunResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/currency-revaluations/{runId:D}", true, cancellationToken);

    public Task<CurrencyRevaluationRunResponse> PreviewCurrencyRevaluationAsync(Guid companyId, Guid fiscalPeriodId,
        string voucherSeriesCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<PreviewCurrencyRevaluationApiRequest, CurrencyRevaluationRunResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/currency-revaluations/preview",
            new() { FiscalPeriodId = fiscalPeriodId, VoucherSeriesCode = voucherSeriesCode, IdempotencyKey = idempotencyKey },
            cancellationToken);
    }

    public Task<CurrencyRevaluationRunResponse> ReviewCurrencyRevaluationItemAsync(Guid companyId, Guid runId,
        Guid itemId, string action, string reason, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<ReviewCurrencyRevaluationApiRequest, CurrencyRevaluationRunResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/currency-revaluations/{runId:D}/population/{itemId:D}/review",
            new() { Action = action, Reason = reason, ExpectedVersion = expectedVersion }, cancellationToken);

    public Task<CurrencyRevaluationRunResponse> SubmitCurrencyRevaluationAsync(Guid companyId, Guid runId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CurrencyRevaluationVersionApiRequest, CurrencyRevaluationRunResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/currency-revaluations/{runId:D}/submit",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);

    public Task<CurrencyRevaluationRunResponse> PostCurrencyRevaluationAsync(Guid companyId, Guid runId,
        long expectedVersion, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CurrencyRevaluationActionApiRequest, CurrencyRevaluationRunResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/currency-revaluations/{runId:D}/post",
            new() { ExpectedVersion = expectedVersion, IdempotencyKey = idempotencyKey }, cancellationToken);

    public Task<CurrencyRevaluationRunResponse> ReverseCurrencyRevaluationAsync(Guid companyId, Guid runId,
        long expectedVersion, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CurrencyRevaluationActionApiRequest, CurrencyRevaluationRunResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/currency-revaluations/{runId:D}/reverse",
            new() { ExpectedVersion = expectedVersion, IdempotencyKey = idempotencyKey }, cancellationToken);

    public Task<CurrencyRevaluationScheduleResponse?> GetCurrencyRevaluationScheduleAsync(Guid companyId,
        CancellationToken cancellationToken = default) => GetAsync<CurrencyRevaluationScheduleResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/currency-revaluation-schedule", false, cancellationToken);

    public Task<CurrencyRevaluationScheduleResponse> ConfigureCurrencyRevaluationScheduleAsync(Guid companyId,
        CurrencyRevaluationScheduleApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CurrencyRevaluationScheduleApiRequest, CurrencyRevaluationScheduleResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/currency-revaluation-schedule",
            request, cancellationToken);
}

public sealed class PreviewCurrencyRevaluationApiRequest
{ public Guid FiscalPeriodId { get; set; } public string VoucherSeriesCode { get; set; } = ""; public string IdempotencyKey { get; set; } = ""; }
public class CurrencyRevaluationVersionApiRequest { public long ExpectedVersion { get; set; } }
public sealed class CurrencyRevaluationActionApiRequest : CurrencyRevaluationVersionApiRequest
{ public string IdempotencyKey { get; set; } = ""; }
public sealed class ReviewCurrencyRevaluationApiRequest : CurrencyRevaluationVersionApiRequest
{ public string Action { get; set; } = ""; public string Reason { get; set; } = ""; }
public sealed class CurrencyRevaluationScheduleApiRequest
{
    public bool IsEnabled { get; set; } public int DaysBeforePeriodEnd { get; set; }
    public bool AutomaticReversal { get; set; } public string VoucherSeriesCode { get; set; } = "";
    public long? ExpectedVersion { get; set; }
}

public sealed class CurrencyRevaluationRunListResponse
{ public List<CurrencyRevaluationRunResponse> Items { get; set; } = []; public int TotalCount { get; set; } }
public sealed class CurrencyRevaluationRunResponse
{
    public Guid Id { get; set; } public Guid FiscalPeriodId { get; set; } public string FiscalPeriodName { get; set; } = "";
    public int RunNumber { get; set; } public DateOnly AsOfDate { get; set; } public string FunctionalCurrency { get; set; } = "";
    public string VoucherSeriesCode { get; set; } = ""; public string Status { get; set; } = "";
    public string? FailureReasonCode { get; set; } public string? FailureSummary { get; set; }
    public string? PopulationChecksum { get; set; } public string? RateSetChecksum { get; set; }
    public string? ProposalChecksum { get; set; } public int PopulationCount { get; set; }
    public int IncludedCount { get; set; } public int ExcludedCount { get; set; } public int ReviewCount { get; set; }
    public decimal DocumentBalanceTotal { get; set; } public decimal CarryingFunctionalTotal { get; set; }
    public decimal RevaluedFunctionalTotal { get; set; } public decimal ProposedAdjustmentTotal { get; set; }
    public Guid? ApprovalRequestId { get; set; } public Guid? LedgerEntryId { get; set; }
    public Guid? ReversalLedgerEntryId { get; set; } public Guid? SupersededByRunId { get; set; }
    public bool IsScheduled { get; set; } public long Version { get; set; } public DateTime UpdatedUtc { get; set; }
    public DateTime? SubmittedUtc { get; set; } public DateTime? PostedUtc { get; set; } public DateTime? ReversedUtc { get; set; }
    public List<CurrencyRevaluationPopulationResponse> Population { get; set; } = [];
    public List<CurrencyRevaluationRateBindingResponse> RateBindings { get; set; } = [];
    public List<CurrencyRevaluationProposalLineResponse> ProposalLines { get; set; } = [];
    public List<CurrencyRevaluationReconciliationResponse> Reconciliations { get; set; } = [];
    public CurrencyRevaluationApprovalResponse? Approval { get; set; }
}
public sealed class CurrencyRevaluationPopulationResponse
{
    public Guid Id { get; set; } public string PopulationKey { get; set; } = ""; public string MonetaryClass { get; set; } = "";
    public Guid FinanceAccountId { get; set; } public string AccountCode { get; set; } = ""; public string AccountName { get; set; } = "";
    public string DocumentCurrency { get; set; } = ""; public string FunctionalCurrency { get; set; } = "";
    public decimal DocumentBalance { get; set; } public decimal CarryingFunctionalAmount { get; set; }
    public decimal RevaluedFunctionalAmount { get; set; } public decimal AdjustmentAmount { get; set; }
    public decimal? PeriodEndRate { get; set; } public DateOnly? RateDate { get; set; }
    public string SourceChecksum { get; set; } = ""; public string Status { get; set; } = ""; public string? ReviewReason { get; set; }
}
public sealed class CurrencyRevaluationRateBindingResponse
{ public Guid PopulationItemId { get; set; } public decimal EffectiveRate { get; set; } public DateOnly RateDate { get; set; } public string RateSetIdentity { get; set; } = ""; public string EvidenceChecksum { get; set; } = ""; }
public sealed class CurrencyRevaluationProposalLineResponse
{ public int Sequence { get; set; } public string AccountCode { get; set; } = ""; public string AccountName { get; set; } = ""; public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string Currency { get; set; } = ""; public string Description { get; set; } = ""; }
public sealed class CurrencyRevaluationReconciliationResponse
{ public string ReconciliationType { get; set; } = ""; public decimal Difference { get; set; } public string Currency { get; set; } = ""; public bool IsReconciled { get; set; } }
public sealed class CurrencyRevaluationApprovalResponse
{ public Guid Id { get; set; } public string Status { get; set; } = ""; public string? DecisionSummary { get; set; } public DateTime CreatedUtc { get; set; } public DateTime? DecidedUtc { get; set; } }
public sealed class CurrencyRevaluationScheduleResponse
{ public Guid Id { get; set; } public bool IsEnabled { get; set; } public int DaysBeforePeriodEnd { get; set; } public bool AutomaticReversal { get; set; } public string VoucherSeriesCode { get; set; } = ""; public long Version { get; set; } public DateTime UpdatedUtc { get; set; } public DateTime? LastEvaluatedUtc { get; set; } }
