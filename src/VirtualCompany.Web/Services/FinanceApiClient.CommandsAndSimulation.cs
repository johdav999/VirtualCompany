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
    public Task<FinanceTransactionResponse> UpdateTransactionCategoryAsync(
        Guid companyId,
        Guid transactionId,
        string category,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpdateFinanceTransactionCategoryRequest, FinanceTransactionResponse>(
            companyId,
            HttpMethod.Patch,
            $"internal/companies/{companyId}/finance/transactions/{transactionId}/category",
            new UpdateFinanceTransactionCategoryRequest { Category = category },
            cancellationToken);
    }

    public Task<JsonElement> StartInvoiceReviewWorkflowAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, JsonElement>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/review-workflow",
            new { },
            cancellationToken);
    }

    public Task<JsonElement> EvaluateTransactionAnomalyAsync(Guid companyId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, JsonElement>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/transactions/{transactionId}/anomaly-evaluation",
            new { },
            cancellationToken);
    }

    public Task<FinanceCashPositionResponse> EvaluateCashPositionAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<object, FinanceCashPositionResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/cash-position/evaluation", new { }, cancellationToken);

    public Task<FinanceInvoiceResponse> UpdateInvoiceApprovalStatusAsync(Guid companyId, Guid invoiceId, string status, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpdateFinanceInvoiceApprovalStatusRequest, FinanceInvoiceResponse>(
            companyId,
            HttpMethod.Patch,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/approval-status",
            new UpdateFinanceInvoiceApprovalStatusRequest { Status = status },
            cancellationToken);
    }

    public Task<FinanceCompanySimulationStateResponse> GetCompanySimulationStateAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(FinanceCompanySimulationStateResponse.NotStarted(companyId))
            : GetAsync<FinanceCompanySimulationStateResponse>(companyId, $"internal/companies/{companyId}/simulation", allowNotFound: false, cancellationToken)!;

    public Task<FinanceCompanySimulationStateResponse> StartCompanySimulationAsync(
        Guid companyId,
        FinanceCompanySimulationStartRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        _logger?.LogInformation(
            "Finance API client posting simulation start. CompanyId: {CompanyId}. StartSimulatedDateTime: {StartSimulatedDateTime}. GenerationEnabled: {GenerationEnabled}. Seed: {Seed}.",
            companyId,
            request.StartSimulatedDateTime,
            request.GenerationEnabled,
            request.Seed);
        return SendCompanyScopedAsync<FinanceCompanySimulationStartRequest, FinanceCompanySimulationStateResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/simulation/start",
            request,
            cancellationToken);
    }

    public Task<FinanceCompanySimulationStateResponse> UpdateCompanySimulationSettingsAsync(
        Guid companyId,
        FinanceCompanySimulationUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceCompanySimulationUpdateRequest, FinanceCompanySimulationStateResponse>(
            companyId,
            HttpMethod.Patch,
            $"internal/companies/{companyId}/simulation",
            request,
            cancellationToken);
    }

    public Task<FinanceCompanySimulationStateResponse> PauseCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendCompanySimulationMutationAsync(companyId, "pause", cancellationToken);

    public Task<FinanceCompanySimulationStateResponse> ResumeCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendCompanySimulationMutationAsync(companyId, "resume", cancellationToken);

    public Task<FinanceCompanySimulationStateResponse> StepForwardCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendCompanySimulationMutationAsync(companyId, "step-forward", cancellationToken);

    public Task<FinanceCompanySimulationStateResponse> StopCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendCompanySimulationMutationAsync(companyId, "stop", cancellationToken);

    public async Task<DateTime> GetFinanceReferenceUtcAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var simulationState = await GetCompanySimulationStateAsync(companyId, cancellationToken);
        if (simulationState.CurrentSimulatedDateTime.HasValue)
        {
            return simulationState.CurrentSimulatedDateTime.Value;
        }

        var clock = await GetSimulationClockAsync(companyId, cancellationToken);
        return clock?.SimulatedUtc ?? DateTime.UtcNow;
    }

    public Task<FinanceSimulationClockResponse?> GetSimulationClockAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSimulationClockResponse?>(new FinanceSimulationClockResponse
              {
                  CompanyId = companyId,
                  SimulatedUtc = DateTime.UtcNow,
                  Enabled = false
              })
            : GetAsync<FinanceSimulationClockResponse>(companyId, $"internal/companies/{companyId}/finance/simulation/clock", allowNotFound: true, cancellationToken);

}

