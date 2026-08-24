using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VirtualCompany.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<FinanceCashPositionResponse?> GetCashPositionAsync(Guid companyId, DateTime? asOfUtc = null, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<FinanceCashPositionResponse?>(null);
        }

        var normalizedAsOfUtc = asOfUtc.HasValue && asOfUtc.Value > DateTime.MinValue
            ? asOfUtc.Value
            : (DateTime?)null;
        var uri = $"internal/companies/{companyId}/finance/cash-position{BuildQuery(("asOfUtc", normalizedAsOfUtc?.ToString("O")))}";
        return GetAsync<FinanceCashPositionResponse>(companyId, uri, allowNotFound: true, cancellationToken);
    }

    public Task<IReadOnlyList<FinanceAccountBalanceResponse>> GetBalancesAsync(Guid companyId, DateTime? asOfUtc = null, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceAccountBalanceResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/balances{BuildQuery(("asOfUtc", asOfUtc?.ToString("O")))}";
        return GetListAsync<FinanceAccountBalanceResponse>(companyId, uri, cancellationToken);
    }

    public Task<IReadOnlyList<FinanceSeedAnomalyResponse>> GetAnomaliesAsync(Guid companyId, int limit = 25, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceSeedAnomalyResponse>>([]);
        }

        return GetListAsync<FinanceSeedAnomalyResponse>(companyId, $"internal/companies/{companyId}/finance/anomalies?limit={limit}", cancellationToken);
    }

    public Task<FinanceAnomalyWorkbenchResponse> GetAnomalyWorkbenchAsync(
        Guid companyId,
        string? anomalyType = null,
        string? status = null,
        decimal? confidenceMin = null,
        decimal? confidenceMax = null,
        string? supplier = null,
        DateTime? dateFromUtc = null,
        DateTime? dateToUtc = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(new FinanceAnomalyWorkbenchResponse());
        }

        var uri = $"internal/companies/{companyId}/finance/anomalies/workbench{BuildQuery(("type", anomalyType), ("status", status), ("confidenceMin", confidenceMin?.ToString("0.##", CultureInfo.InvariantCulture)), ("confidenceMax", confidenceMax?.ToString("0.##", CultureInfo.InvariantCulture)), ("supplier", supplier), ("dateFromUtc", dateFromUtc?.ToString("O")), ("dateToUtc", dateToUtc?.ToString("O")), ("page", page.ToString(CultureInfo.InvariantCulture)), ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)))}";
        return GetAsync<FinanceAnomalyWorkbenchResponse>(companyId, uri, allowNotFound: false, cancellationToken)!;
    }

    public Task<FinanceAnomalyDetailResponse?> GetAnomalyDetailAsync(Guid companyId, Guid anomalyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceAnomalyDetailResponse?>(null)
            : GetAsync<FinanceAnomalyDetailResponse>(companyId, $"internal/companies/{companyId}/finance/anomalies/workbench/{anomalyId}", allowNotFound: true, cancellationToken);

    public Task<IReadOnlyList<FinanceTransactionResponse>> GetTransactionsAsync(
        Guid companyId,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        string? category = null,
        string? flagged = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceTransactionResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/transactions{BuildQuery(("startUtc", startUtc?.ToString("O")), ("endUtc", endUtc?.ToString("O")), ("category", category), ("flagged", flagged), ("limit", limit.ToString()), ("source", _financeDataSourceFilter))}";
        return GetListAsync<FinanceTransactionResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceTransactionDetailResponse?> GetTransactionDetailAsync(Guid companyId, Guid transactionId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceTransactionDetailResponse?>(null)
            : GetAsync<FinanceTransactionDetailResponse>(companyId, $"internal/companies/{companyId}/finance/transactions/{transactionId}{BuildQuery(("source", _financeDataSourceFilter))}", allowNotFound: true, cancellationToken);

    public Task<IReadOnlyList<FinancePaymentResponse>> GetPaymentsAsync(
        Guid companyId,
        string? paymentType = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinancePaymentResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/payments{BuildQuery(("type", paymentType), ("limit", limit.ToString(CultureInfo.InvariantCulture)), ("source", _financeDataSourceFilter))}";
        return GetListAsync<FinancePaymentResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinancePaymentResponse?> GetPaymentDetailAsync(Guid companyId, Guid paymentId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinancePaymentResponse?>(null)
            : GetAsync<FinancePaymentResponse>(companyId, $"internal/companies/{companyId}/finance/payments/{paymentId}{BuildQuery(("source", _financeDataSourceFilter))}", allowNotFound: true, cancellationToken);

}

