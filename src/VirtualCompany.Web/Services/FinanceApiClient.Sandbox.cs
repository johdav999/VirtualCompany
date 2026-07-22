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
    public Task<FinanceSandboxDatasetGenerationResponse?> GetSandboxDatasetGenerationAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSandboxDatasetGenerationResponse?>(null)
            : GetAsync<FinanceSandboxDatasetGenerationResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/dataset-generation", allowNotFound: true, cancellationToken);

    public Task<FinanceSeedingStateResponse?> GetSeedingStateAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSeedingStateResponse?>(new FinanceSeedingStateResponse
            {
                CompanyId = companyId,
                SeedingState = FinanceSeedingStateContractValues.NotSeeded
            })
            : GetAsync<FinanceSeedingStateResponse>(companyId, $"internal/companies/{companyId}/finance/seeding-state", allowNotFound: true, cancellationToken);

    public Task<FinanceEntryInitializationResponse> GetEntryInitializationStateAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Ready,
                SeedingState = FinanceSeedingStateContractValues.Seeded,
                Message = "Finance workspace is ready in offline mode."
            })
            : GetAsync<FinanceEntryInitializationResponse>(companyId, $"internal/companies/{companyId}/finance/entry-state", allowNotFound: false, cancellationToken)!;

    public Task<FinanceEntryInitializationResponse> RequestEntryInitializationAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, FinanceEntryInitializationResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/entry-state/request",
            new { },
            cancellationToken);
    }

    public Task<FinanceEntryInitializationResponse> RetryEntryInitializationAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, FinanceEntryInitializationResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/entry-state/retry",
            new { },
            cancellationToken);
    }

    public Task<FinanceEntryInitializationResponse> RequestManualSeedAsync(
        Guid companyId,
        FinanceManualSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceManualSeedRequest, FinanceEntryInitializationResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/manual-seed",
            request,
            cancellationToken);
    }

    public Task<FinanceSandboxSeedGenerationResponse> GenerateSandboxSeedDatasetAsync(
        Guid companyId,
        FinanceSandboxSeedGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceSandboxSeedGenerationRequest, FinanceSandboxSeedGenerationResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/sandbox-admin/seed-generation", request, cancellationToken);
    }

    public Task<FinanceSandboxAnomalyInjectionResponse?> GetSandboxAnomalyInjectionAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSandboxAnomalyInjectionResponse?>(null)
            : GetAsync<FinanceSandboxAnomalyInjectionResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/anomaly-injection", allowNotFound: true, cancellationToken);

    public Task<FinanceSandboxAnomalyDetailResponse?> GetSandboxAnomalyDetailAsync(Guid companyId, Guid anomalyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSandboxAnomalyDetailResponse?>(null)
            : GetAsync<FinanceSandboxAnomalyDetailResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/anomaly-injection/{anomalyId}", allowNotFound: true, cancellationToken);

    public Task<FinanceSandboxAnomalyDetailResponse> InjectSandboxAnomalyAsync(Guid companyId, FinanceSandboxAnomalyInjectionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceSandboxAnomalyInjectionRequest, FinanceSandboxAnomalyDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/sandbox-admin/anomaly-injection", request, cancellationToken);
    }

    public Task<FinanceSandboxSimulationControlsResponse?> GetSandboxSimulationControlsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSandboxSimulationControlsResponse?>(null)
            : GetAsync<FinanceSandboxSimulationControlsResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/simulation-controls", allowNotFound: true, cancellationToken);

    public Task<FinanceSandboxProgressionRunSummaryResponse> AdvanceSandboxSimulationAsync(Guid companyId, FinanceSandboxSimulationAdvanceRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceSandboxSimulationAdvanceRequest, FinanceSandboxProgressionRunSummaryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/sandbox-admin/simulation-controls/advance", request, cancellationToken);
    }

    public Task<FinanceSandboxProgressionRunSummaryResponse> StartSandboxProgressionRunAsync(Guid companyId, FinanceSandboxSimulationAdvanceRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceSandboxSimulationAdvanceRequest, FinanceSandboxProgressionRunSummaryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/sandbox-admin/simulation-controls/progression-run", request, cancellationToken);
    }

    public Task<FinanceDataResetResponse> ResetFinancialDataAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, FinanceDataResetResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/finance/reset",
            new { },
            cancellationToken);
    }


    public Task<FinanceSandboxToolExecutionVisibilityResponse?> GetSandboxToolExecutionVisibilityAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSandboxToolExecutionVisibilityResponse?>(null)
            : GetAsync<FinanceSandboxToolExecutionVisibilityResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/tool-execution-visibility", allowNotFound: true, cancellationToken);

    public Task<FinanceSandboxDomainEventsResponse?> GetSandboxDomainEventsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceSandboxDomainEventsResponse?>(null)
            : GetAsync<FinanceSandboxDomainEventsResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/domain-events", allowNotFound: true, cancellationToken);

    public Task<FinanceTransparencyToolManifestListResponse?> GetTransparencyToolManifestsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceTransparencyToolManifestListResponse?>(null)
            : GetAsync<FinanceTransparencyToolManifestListResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/transparency/tool-manifests", allowNotFound: true, cancellationToken);

    public Task<FinanceTransparencyToolExecutionHistoryResponse?> GetTransparencyToolExecutionsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceTransparencyToolExecutionHistoryResponse?>(null)
            : GetAsync<FinanceTransparencyToolExecutionHistoryResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/transparency/tool-executions", allowNotFound: true, cancellationToken);

    public Task<FinanceTransparencyToolExecutionDetailResponse?> GetTransparencyToolExecutionDetailAsync(Guid companyId, Guid executionId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceTransparencyToolExecutionDetailResponse?>(null)
            : GetAsync<FinanceTransparencyToolExecutionDetailResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/transparency/tool-executions/{executionId}", allowNotFound: true, cancellationToken);

    public Task<FinanceTransparencyEventStreamResponse?> GetTransparencyEventsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceTransparencyEventStreamResponse?>(null)
            : GetAsync<FinanceTransparencyEventStreamResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/transparency/events", allowNotFound: true, cancellationToken);

    public Task<FinanceTransparencyEventDetailResponse?> GetTransparencyEventDetailAsync(Guid companyId, Guid eventId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceTransparencyEventDetailResponse?>(null)
            : GetAsync<FinanceTransparencyEventDetailResponse>(companyId, $"internal/companies/{companyId}/finance/sandbox-admin/transparency/events/{eventId}", allowNotFound: true, cancellationToken);

}

