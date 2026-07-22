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
    public Task<IReadOnlyList<FinanceInvoiceReviewListItemResponse>> GetInvoiceReviewsAsync(
        Guid companyId,
        string? status = null,
        string? supplier = null,
        string? riskLevel = null,
        string? recommendationOutcome = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceInvoiceReviewListItemResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/reviews{BuildQuery(("status", status), ("supplier", supplier), ("riskLevel", riskLevel), ("outcome", recommendationOutcome), ("limit", limit.ToString()))}";
        return GetListAsync<FinanceInvoiceReviewListItemResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceInvoiceReviewDetailResponse?> GetInvoiceReviewDetailAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceInvoiceReviewDetailResponse?>(null)
            : GetAsync<FinanceInvoiceReviewDetailResponse>(companyId, $"internal/companies/{companyId}/finance/reviews/{invoiceId}", allowNotFound: true, cancellationToken);

    public async Task<FinanceMonthlySummaryResponse?> GetMonthlySummaryAsync(Guid companyId, DateTime? referenceUtc = null, CancellationToken cancellationToken = default)
    {
        FinanceSimulationClockResponse? clock = null;
        var resolvedReferenceUtc = referenceUtc;
        if (!resolvedReferenceUtc.HasValue)
        {
            var simulationState = await GetCompanySimulationStateAsync(companyId, cancellationToken);
            if (simulationState.CurrentSimulatedDateTime.HasValue)
            {
                resolvedReferenceUtc = simulationState.CurrentSimulatedDateTime.Value;
            }

            clock = await GetSimulationClockAsync(companyId, cancellationToken);
            resolvedReferenceUtc = clock?.SimulatedUtc ?? DateTime.UtcNow;
        }

        var monthStartUtc = new DateTime(resolvedReferenceUtc.Value.Year, resolvedReferenceUtc.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEndUtc = monthStartUtc.AddMonths(1);

        if (_useOfflineMode)
        {
            return new FinanceMonthlySummaryResponse
            {
                CompanyId = companyId,
                ReferenceUtc = resolvedReferenceUtc.Value,
                StartUtc = monthStartUtc,
                EndUtc = monthEndUtc,
                Clock = clock,
                ProfitAndLoss = new FinanceMonthlyProfitAndLossResponse
                {
                    CompanyId = companyId,
                    Year = monthStartUtc.Year,
                    Month = monthStartUtc.Month,
                    StartUtc = monthStartUtc,
                    EndUtc = monthEndUtc,
                    Currency = "USD"
                },
                ExpenseBreakdown = new FinanceExpenseBreakdownResponse
                {
                    CompanyId = companyId,
                    StartUtc = monthStartUtc,
                    EndUtc = monthEndUtc,
                    Currency = "USD"
                }
            };
        }

        var profitAndLoss = await GetAsync<FinanceMonthlyProfitAndLossResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/profit-and-loss/monthly?year={monthStartUtc.Year}&month={monthStartUtc.Month}",
            allowNotFound: true,
            cancellationToken);

        if (profitAndLoss is null)
        {
            return null;
        }

        var expenseBreakdown = await GetAsync<FinanceExpenseBreakdownResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/expense-breakdown?startUtc={Uri.EscapeDataString(monthStartUtc.ToString("O"))}&endUtc={Uri.EscapeDataString(monthEndUtc.ToString("O"))}",
            allowNotFound: true,
            cancellationToken);

        return new FinanceMonthlySummaryResponse
        {
            CompanyId = companyId,
            ReferenceUtc = resolvedReferenceUtc.Value,
            StartUtc = monthStartUtc,
            EndUtc = monthEndUtc,
            Clock = clock,
            ProfitAndLoss = profitAndLoss,
            ExpenseBreakdown = expenseBreakdown
        };
    }

    public Task<FinanceInvoiceReviewDetailResponse> SubmitInvoiceReviewActionAsync(
        Guid companyId,
        Guid invoiceId,
        string action,
        CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<object, FinanceInvoiceReviewDetailResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/reviews/{invoiceId}/{action}",
            new { },
            cancellationToken);

}

