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
public sealed class ReviewCurrencyRevaluationApiRequest : CurrencyRevaluationVersionApiRequest
{ public string Action { get; set; } = ""; public string Reason { get; set; } = ""; }
