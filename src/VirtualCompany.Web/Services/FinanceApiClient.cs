using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VirtualCompany.Shared;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class FinanceApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string CompanyContextHeaderName = "X-Company-Id";
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;
    private readonly ILogger<FinanceApiClient>? _logger;

    public FinanceApiClient(HttpClient httpClient, ILogger<FinanceApiClient>? logger = null, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _logger = logger;
        _useOfflineMode = useOfflineMode;
    }

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

    public Task<FinanceCashPositionResponse?> GetCashPositionAsync(Guid companyId, DateTime? asOfUtc = null, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<FinanceCashPositionResponse?>(null);
        }

        var uri = $"internal/companies/{companyId}/finance/cash-position{BuildQuery(("asOfUtc", asOfUtc?.ToString("O")))}";
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

        var uri = $"internal/companies/{companyId}/finance/transactions{BuildQuery(("startUtc", startUtc?.ToString("O")), ("endUtc", endUtc?.ToString("O")), ("category", category), ("flagged", flagged), ("limit", limit.ToString()))}";
        return GetListAsync<FinanceTransactionResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceTransactionDetailResponse?> GetTransactionDetailAsync(Guid companyId, Guid transactionId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceTransactionDetailResponse?>(null)
            : GetAsync<FinanceTransactionDetailResponse>(companyId, $"internal/companies/{companyId}/finance/transactions/{transactionId}", allowNotFound: true, cancellationToken);

    public Task<IReadOnlyList<FinanceInvoiceResponse>> GetInvoicesAsync(Guid companyId, DateTime? startUtc = null, DateTime? endUtc = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceInvoiceResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/invoices{BuildQuery(("startUtc", startUtc?.ToString("O")), ("endUtc", endUtc?.ToString("O")), ("limit", limit.ToString()))}";
        return GetListAsync<FinanceInvoiceResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceInvoiceDetailResponse?> GetInvoiceDetailAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceInvoiceDetailResponse?>(null)
            : GetAsync<FinanceInvoiceDetailResponse>(companyId, $"internal/companies/{companyId}/finance/invoices/{invoiceId}", allowNotFound: true, cancellationToken);

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

    private async Task<IReadOnlyList<T>> GetListAsync<T>(Guid companyId, string uri, CancellationToken cancellationToken)
    {
        var items = await GetAsync<List<T>>(companyId, uri, allowNotFound: false, cancellationToken);
        return items ?? [];
    }

    private async Task<T?> SendCompanyScopedGetAsync<T>(Guid companyId, string uri, bool allowNotFound, CancellationToken cancellationToken)
    {
        using var request = CreateCompanyScopedRequest(companyId, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (response.IsSuccessStatusCode)
        {
            if (response.Content.Headers.ContentLength is 0)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        }

        throw await CreateExceptionAsync(response, cancellationToken);
    }

    private async Task<TResponse> SendCompanyScopedAsync<TRequest, TResponse>(
        Guid companyId,
        HttpMethod method,
        string uri,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateCompanyScopedRequest(companyId, method, uri, JsonContent.Create(payload));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
                return result ?? throw new FinanceApiException("The finance request returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private HttpRequestMessage CreateCompanyScopedRequest(Guid companyId, string uri) =>
        CreateCompanyScopedRequest(companyId, HttpMethod.Get, uri, null);

    private HttpRequestMessage CreateCompanyScopedRequest(Guid companyId, HttpMethod method, string uri, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Content = content;
        request.Headers.TryAddWithoutValidation(CompanyContextHeaderName, companyId.ToString());
        return request;
    }

    private Task<FinanceCompanySimulationStateResponse> SendCompanySimulationMutationAsync(
        Guid companyId,
        string action,
        CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, FinanceCompanySimulationStateResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/simulation/{action}",
            new { },
            cancellationToken);
    }

    private async Task<T?> GetAsync<T>(Guid companyId, string uri, bool allowNotFound, CancellationToken cancellationToken)
    {
        try
        {
            return await SendCompanyScopedGetAsync<T>(companyId, uri, allowNotFound, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<FinanceApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            await response.Content.ReadAsStringAsync(cancellationToken);
            return new FinanceApiException($"The finance request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        if (problem is not null &&
            string.Equals(problem.Code, FinanceInitializationProblemCodeValues.NotInitialized, StringComparison.OrdinalIgnoreCase))
        {
            return new FinanceNotInitializedApiException(problem.ToFinanceInitializationProblemResponse());
        }
        if (problem?.Errors is { Count: > 0 })
        {
            return new FinanceApiValidationException(FormatProblemMessage(problem), problem.Errors);
        }

        return new FinanceApiException(problem is null ? "The finance request failed." : FormatProblemMessage(problem));
    }

    private FinanceApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new FinanceApiException($"The web app could not reach the finance backend at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private void EnsureOnlineMutation()
    {
        if (_useOfflineMode)
        {
            throw new FinanceApiException("Finance edits are unavailable while the web app is running in offline mode.");
        }
    }

    private static string BuildQuery(params (string Key, string? Value)[] pairs)
    {
        var segments = pairs
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value!)}")
            .ToArray();

        return segments.Length == 0 ? string.Empty : $"?{string.Join("&", segments)}";
    }

    private static string FormatProblemMessage(ApiProblemResponse problem)
    {
        var baseMessage = problem.Detail ?? problem.Message ?? problem.Title ?? "The finance request failed.";
        var identifiers = new[]
        {
            string.IsNullOrWhiteSpace(problem.TraceId) ? null : $"TraceId={problem.TraceId}",
            string.IsNullOrWhiteSpace(problem.CorrelationId) ? null : $"CorrelationId={problem.CorrelationId}"
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();

        return identifiers.Length == 0
            ? baseMessage
            : $"{baseMessage} ({string.Join(", ", identifiers)})";
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Code { get; set; }
        public string? Detail { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
        public Guid CompanyId { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public bool CanTriggerSeed { get; set; }
        public bool CanGenerate { get; set; }
        public string RecommendedAction { get; set; } = string.Empty;
        public IReadOnlyList<string> SupportedModes { get; set; } = [];
        public bool FallbackTriggered { get; set; }
        public bool SeedRequested { get; set; }
        public bool SeedJobActive { get; set; }
        public bool ConfirmationRequired { get; set; }
        public string ProgressState { get; set; } = string.Empty;
        public string SeedingState { get; set; } = string.Empty;
        public string InitializationStatus { get; set; } = string.Empty;
        public string? JobStatus { get; set; }
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }
        public string? StatusEndpoint { get; set; }
        public string? SeedEndpoint { get; set; }
        public string? ConfirmationMessage { get; set; }

        public FinanceInitializationProblemResponse ToFinanceInitializationProblemResponse() =>
            new()
            {
                Title = Title ?? string.Empty,
                Detail = Detail ?? string.Empty,
                Code = Code ?? string.Empty,
                Message = Message ?? string.Empty,
                CompanyId = CompanyId,
                Domain = Domain,
                Module = Module,
                CanTriggerSeed = CanTriggerSeed,
                CanGenerate = CanGenerate,
                RecommendedAction = RecommendedAction,
                SupportedModes = SupportedModes,
                FallbackTriggered = FallbackTriggered,
                SeedRequested = SeedRequested,
                SeedJobActive = SeedJobActive,
                ConfirmationRequired = ConfirmationRequired,
                ProgressState = ProgressState,
                SeedingState = SeedingState,
                InitializationStatus = InitializationStatus,
                JobStatus = JobStatus,
                CorrelationId = CorrelationId,
                StatusEndpoint = StatusEndpoint,
                SeedEndpoint = SeedEndpoint,
                ConfirmationMessage = ConfirmationMessage
            };
    }
}

public class FinanceApiException : Exception
{
    public FinanceApiException(string message) : base(message)
    {
    }
}

public sealed class FinanceNotInitializedApiException : FinanceApiException
{
    public FinanceNotInitializedApiException(FinanceInitializationProblemResponse problem)
        : base(problem.Message ?? problem.Detail ?? "Finance data is not initialized.")
    {
        Problem = problem;
    }

    public FinanceInitializationProblemResponse Problem { get; }
}

public sealed class FinanceApiValidationException : FinanceApiException
{
    public FinanceApiValidationException(string message, IDictionary<string, string[]> errors)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class FinanceSimulationClockResponse
{
    public Guid CompanyId { get; set; }
    public DateTime SimulatedUtc { get; set; }
    public bool Enabled { get; set; }
}

public sealed class FinanceCashPositionResponse
{
    public Guid CompanyId { get; set; }
    public DateTime AsOfUtc { get; set; }
    public decimal AvailableBalance { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AverageMonthlyBurn { get; set; }
    public int? EstimatedRunwayDays { get; set; }
    public FinanceCashPositionThresholdsResponse Thresholds { get; set; } = new();
    public FinanceCashPositionAlertStateResponse AlertState { get; set; } = new();
    public string Classification { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string SourceWorkflow { get; set; } = string.Empty;
}

public sealed class FinanceCashPositionThresholdsResponse
{
    public int WarningRunwayDays { get; set; }
    public int CriticalRunwayDays { get; set; }
    public decimal? WarningCashAmount { get; set; }
    public decimal? CriticalCashAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class FinanceCashPositionAlertStateResponse
{
    public bool IsLowCash { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public bool AlertCreated { get; set; }
    public bool AlertDeduplicated { get; set; }
    public Guid? AlertId { get; set; }
    public string? AlertStatus { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

public sealed class FinanceAccountBalanceResponse
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
}

public sealed class FinanceMonthlySummaryResponse
{
    public Guid CompanyId { get; set; }
    public DateTime ReferenceUtc { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public FinanceSimulationClockResponse? Clock { get; set; }
    public FinanceMonthlyProfitAndLossResponse ProfitAndLoss { get; set; } = new();
    public FinanceExpenseBreakdownResponse? ExpenseBreakdown { get; set; }
}

public sealed class FinanceMonthlyProfitAndLossResponse
{
    public Guid CompanyId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetResult { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class FinanceExpenseBreakdownResponse
{
    public Guid CompanyId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public decimal TotalExpenses { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<FinanceExpenseCategoryResponse> Categories { get; set; } = [];
}

public sealed class FinanceExpenseCategoryResponse
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class FinanceTransactionResponse
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public Guid? CounterpartyId { get; set; }
    public string? CounterpartyName { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? BillId { get; set; }
    public DateTime TransactionUtc { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public FinanceLinkedDocumentResponse? LinkedDocument { get; set; }
    public bool IsFlagged { get; set; }
    public string AnomalyState { get; set; } = string.Empty;
}

public sealed class FinanceTransactionDetailResponse
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public Guid? CounterpartyId { get; set; }
    public string? CounterpartyName { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? BillId { get; set; }
    public DateTime TransactionUtc { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public bool IsFlagged { get; set; }
    public string AnomalyState { get; set; } = string.Empty;
    public List<string> Flags { get; set; } = [];
    public FinanceActionPermissionsResponse Permissions { get; set; } = new();
    public FinanceLinkedDocumentAccessResponse LinkedDocument { get; set; } = new();
}

public sealed class FinanceInvoiceResponse
{
    public Guid Id { get; set; }
    public Guid CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime DueUtc { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public FinanceLinkedDocumentResponse? LinkedDocument { get; set; }
}

public sealed class FinanceInvoiceDetailResponse
{
    public Guid Id { get; set; }
    public Guid CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime DueUtc { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails { get; set; }
    public List<FinanceInvoiceWorkflowHistoryItemResponse> WorkflowHistory { get; set; } = [];
    public FinanceInvoiceWorkflowContextResponse? WorkflowContext { get; set; }
    public FinanceActionPermissionsResponse Permissions { get; set; } = new();
    public FinanceLinkedDocumentAccessResponse LinkedDocument { get; set; } = new();
}

public sealed class FinanceInvoiceReviewListItemResponse
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string RecommendationStatus { get; set; } = string.Empty;
    public string RecommendationOutcome { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}

public sealed class FinanceInvoiceReviewDetailResponse
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string RecommendationStatus { get; set; } = string.Empty;
    public string RecommendationSummary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public Guid SourceInvoiceId { get; set; }
    public Guid? RelatedApprovalId { get; set; }
    public FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails { get; set; }
    public List<FinanceInvoiceWorkflowHistoryItemResponse> WorkflowHistory { get; set; } = [];
    public FinanceInvoiceReviewActionAvailabilityResponse Actions { get; set; } = new();
}

public sealed class FinanceInvoiceRecommendationDetailsResponse
{
    public string Classification { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string RationaleSummary { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public string CurrentWorkflowStatus { get; set; } = string.Empty;
}

public sealed class FinanceInvoiceWorkflowHistoryItemResponse
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ActorOrSourceDisplayName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public Guid? RelatedAuditId { get; set; }
    public Guid? RelatedApprovalId { get; set; }
}

public sealed class FinanceInvoiceReviewActionAvailabilityResponse
{
    public bool IsActionable { get; set; }
    public bool CanApprove { get; set; }
    public bool CanReject { get; set; }
    public bool CanSendForFollowUp { get; set; }
}

public sealed class FinanceSeedAnomalyResponse
{
    public Guid Id { get; set; }
    public string AnomalyType { get; set; } = string.Empty;
    public string ScenarioProfile { get; set; } = string.Empty;
    public List<Guid> AffectedRecordIds { get; set; } = [];
    public string ExpectedDetectionMetadataJson { get; set; } = string.Empty;
}

public sealed class FinanceAnomalyWorkbenchResponse
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<FinanceAnomalyWorkbenchItemResponse> Items { get; set; } = [];
}

public sealed class FinanceAnomalyWorkbenchItemResponse
{
    public Guid Id { get; set; }
    public string AnomalyType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? SupplierName { get; set; }
    public Guid? AffectedRecordId { get; set; }
    public string AffectedRecordReference { get; set; } = string.Empty;
    public string ExplanationSummary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
    public FinanceAnomalyDeduplicationResponse? Deduplication { get; set; }
    public Guid? FollowUpTaskId { get; set; }
    public string? FollowUpTaskStatus { get; set; }
    public Guid? RelatedInvoiceId { get; set; }
    public Guid? RelatedBillId { get; set; }
}

public sealed class FinanceAnomalyDetailResponse
{
    public Guid Id { get; set; }
    public string AnomalyType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? SupplierName { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
    public FinanceAnomalyDeduplicationResponse? Deduplication { get; set; }
    public FinanceAnomalyRelatedRecordResponse? AffectedRecord { get; set; }
    public Guid? RelatedInvoiceId { get; set; }
    public string? RelatedInvoiceReference { get; set; }
    public Guid? RelatedBillId { get; set; }
    public string? RelatedBillReference { get; set; }
    public List<FinanceAnomalyRecordLinkResponse> RelatedRecordLinks { get; set; } = [];
    public List<FinanceAnomalyFollowUpTaskResponse> FollowUpTasks { get; set; } = [];
}

public sealed class FinanceAnomalyDeduplicationResponse
{
    public string? Key { get; set; }
    public DateTime? WindowStartUtc { get; set; }
    public DateTime? WindowEndUtc { get; set; }
}

public sealed class FinanceAnomalyRecordLinkResponse
{
    public Guid? RecordId { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public DateTime? OccurredAtUtc { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}

public sealed class FinanceAnomalyRelatedRecordResponse
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
}

public sealed class FinanceAnomalyFollowUpTaskResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? DueUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class FinanceLinkedDocumentResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

public sealed class FinanceLinkedDocumentAccessResponse
{
    public string Availability { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool CanNavigate { get; set; }
    public FinanceLinkedDocumentResponse? Document { get; set; }
}

public sealed class FinanceInvoiceWorkflowContextResponse
{
    public Guid? WorkflowInstanceId { get; set; }
    public Guid TaskId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public string ReviewTaskStatus { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public bool RequiresHumanApproval { get; set; }
    public string? ApprovalStatus { get; set; }
    public string? ApprovalAssigneeSummary { get; set; }
    public bool CanNavigateToWorkflow { get; set; }
    public bool CanNavigateToApproval { get; set; }
}

public sealed class FinanceActionPermissionsResponse
{
    public bool CanEditTransactionCategory { get; set; }
    public bool CanChangeInvoiceApprovalStatus { get; set; }
    public bool CanManagePolicyConfiguration { get; set; }
}

public sealed class UpdateFinanceTransactionCategoryRequest
{
    public string Category { get; set; } = string.Empty;
}

public sealed class UpdateFinanceInvoiceApprovalStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
