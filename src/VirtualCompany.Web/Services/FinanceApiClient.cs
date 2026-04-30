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

        var uri = $"internal/companies/{companyId}/finance/payments{BuildQuery(("type", paymentType), ("limit", limit.ToString(CultureInfo.InvariantCulture)))}";
        return GetListAsync<FinancePaymentResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinancePaymentResponse?> GetPaymentDetailAsync(Guid companyId, Guid paymentId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinancePaymentResponse?>(null)
            : GetAsync<FinancePaymentResponse>(companyId, $"internal/companies/{companyId}/finance/payments/{paymentId}", allowNotFound: true, cancellationToken);

    public Task<IReadOnlyList<FinanceBillResponse>> GetBillsAsync(
        Guid companyId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceBillResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/bills{BuildQuery(("limit", limit.ToString(CultureInfo.InvariantCulture)))}";
        return GetListAsync<FinanceBillResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceBillDetailResponse?> GetBillDetailAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceBillDetailResponse?>(null)
            : GetAsync<FinanceBillDetailResponse>(companyId, $"internal/companies/{companyId}/finance/bills/{billId}", allowNotFound: true, cancellationToken);

    public Task<IReadOnlyList<FinanceBillInboxRowResponse>> GetBillInboxAsync(
        Guid companyId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceBillInboxRowResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/bill-inbox{BuildQuery(("limit", limit.ToString(CultureInfo.InvariantCulture)))}";
        return GetListAsync<FinanceBillInboxRowResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceBillInboxDetailResponse?> GetBillInboxDetailAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceBillInboxDetailResponse?>(null)
            : GetAsync<FinanceBillInboxDetailResponse>(companyId, $"internal/companies/{companyId}/finance/bill-inbox/{billId}", allowNotFound: true, cancellationToken);

    public Task<FinanceBillReviewActionResultResponse> ApproveBillInboxItemAsync(Guid companyId, Guid billId, string rationale, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceBillReviewActionRequest, FinanceBillReviewActionResultResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/approve", new FinanceBillReviewActionRequest(rationale), cancellationToken);

    public Task<FinanceBillReviewActionResultResponse> RejectBillInboxItemAsync(Guid companyId, Guid billId, string rationale, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceBillReviewActionRequest, FinanceBillReviewActionResultResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/reject", new FinanceBillReviewActionRequest(rationale), cancellationToken);

    public Task<FinanceBillReviewActionResultResponse> RequestBillInboxClarificationAsync(Guid companyId, Guid billId, string rationale, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceBillReviewActionRequest, FinanceBillReviewActionResultResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/request-clarification", new FinanceBillReviewActionRequest(rationale), cancellationToken);

    public Task<MailboxConnectionStatusResponse?> GetMailboxConnectionStatusAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<MailboxConnectionStatusResponse?>(new MailboxConnectionStatusResponse())
            : GetAsync<MailboxConnectionStatusResponse>(companyId, $"api/companies/{companyId}/mailbox-connections/current", allowNotFound: false, cancellationToken);

    public Task<MailboxProviderAvailabilityResponse> GetMailboxProviderAvailabilityAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(new MailboxProviderAvailabilityResponse())
            : GetAsync<MailboxProviderAvailabilityResponse>(companyId, $"api/companies/{companyId}/mailbox-connections/providers", allowNotFound: false, cancellationToken)!;

    public Task<IReadOnlyList<MailboxScannedMessageResponse>> GetMailboxScannedMessagesAsync(
        Guid companyId,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<MailboxScannedMessageResponse>>([])
            : GetListAsync<MailboxScannedMessageResponse>(
                companyId,
                $"api/companies/{companyId}/mailbox-connections/messages{BuildQuery(("limit", limit.ToString(CultureInfo.InvariantCulture)))}",
                cancellationToken);

    public async Task<string> StartMailboxConnectionAsync(
        Guid companyId,
        string provider,
        string? returnUri = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        var response = await SendCompanyScopedAsync<StartMailboxConnectionRequest, StartMailboxConnectionResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/{provider}/start",
            new StartMailboxConnectionRequest { ReturnUri = returnUri },
            cancellationToken);

        return response.AuthorizationUrl;
    }

    public Task<ManualMailboxScanResponse> TriggerManualMailboxScanAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ManualMailboxScanResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/scan",
            new { },
            cancellationToken);
    }

    public Task<FinanceEmailSettingsResponse> GetEmailSettingsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(new FinanceEmailSettingsResponse())
            : GetAsync<FinanceEmailSettingsResponse>(companyId, $"internal/companies/{companyId}/finance/settings/email", allowNotFound: false, cancellationToken)!;

    public Task<FinanceEmailSettingsResponse> UpdateEmailSettingsAsync(
        Guid companyId,
        UpdateFinanceEmailSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpdateFinanceEmailSettingsRequest, FinanceEmailSettingsResponse>(
            companyId,
            HttpMethod.Put,
            $"internal/companies/{companyId}/finance/settings/email",
            request,
            cancellationToken);
    }

    public Task<FinancePaymentResponse> CreatePaymentAsync(
        Guid companyId,
        CreateFinancePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateFinancePaymentRequest, FinancePaymentResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/payments",
            request,
            cancellationToken);
    }

    public Task<IReadOnlyList<FinanceInvoiceResponse>> GetInvoicesAsync(Guid companyId, DateTime? startUtc = null, DateTime? endUtc = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceInvoiceResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/invoices{BuildQuery(("startUtc", startUtc?.ToString("O")), ("endUtc", endUtc?.ToString("O")), ("limit", limit.ToString()))}";
        return GetListAsync<FinanceInvoiceResponse>(companyId, uri, cancellationToken);
    }

    public Task<IReadOnlyList<FinanceCounterpartyResponse>> GetCustomersAsync(Guid companyId, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceCounterpartyResponse>>([]);
        }

        return GetListAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/customers?limit={limit}", cancellationToken);
    }

    public Task<IReadOnlyList<FinanceCounterpartyResponse>> GetSuppliersAsync(Guid companyId, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceCounterpartyResponse>>([]);
        }

        return GetListAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/suppliers?limit={limit}", cancellationToken);
    }

    public Task<FinanceCounterpartyResponse?> GetCustomerAsync(Guid companyId, Guid counterpartyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode ? Task.FromResult<FinanceCounterpartyResponse?>(null) : GetAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/customers/{counterpartyId}", allowNotFound: true, cancellationToken);

    public Task<FinanceCounterpartyResponse?> GetSupplierAsync(Guid companyId, Guid counterpartyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode ? Task.FromResult<FinanceCounterpartyResponse?>(null) : GetAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/suppliers/{counterpartyId}", allowNotFound: true, cancellationToken);

    public Task<FinanceCounterpartyResponse> CreateCustomerAsync(Guid companyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/customers", request, cancellationToken);

    public Task<FinanceCounterpartyResponse> UpdateCustomerAsync(Guid companyId, Guid counterpartyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/customers/{counterpartyId}", request, cancellationToken);

    public Task<FinanceCounterpartyResponse> CreateSupplierAsync(Guid companyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/suppliers", request, cancellationToken);

    public Task<FinanceCounterpartyResponse> UpdateSupplierAsync(Guid companyId, Guid counterpartyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/suppliers/{counterpartyId}", request, cancellationToken);

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
        if (IsMailboxProviderConfigurationProblem(problem))
        {
            return new MailboxProviderNotConfiguredApiException(problem.Detail ?? problem.Title ?? "Mailbox provider OAuth client settings are not configured.");
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

    private static bool IsMailboxProviderConfigurationProblem(ApiProblemResponse? problem) =>
        problem is not null &&
        (string.Equals(problem.Title, "Mailbox provider is not configured.", StringComparison.OrdinalIgnoreCase) ||
            (problem.Detail?.Contains("mailbox OAuth client settings are not configured", StringComparison.OrdinalIgnoreCase) ?? false));

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

public sealed class MailboxProviderNotConfiguredApiException : FinanceApiException
{
    public MailboxProviderNotConfiguredApiException(string message) : base(message)
    {
    }
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

public sealed class FinanceEmailSettingsResponse
{
    public bool IsWritable { get; set; }
    public bool RequiresRestart { get; set; }
    public FinanceEmailProviderSettingsResponse Gmail { get; set; } = new();
    public FinanceEmailProviderSettingsResponse Microsoft365 { get; set; } = new();
}

public sealed class FinanceEmailProviderSettingsResponse
{
    public string ClientId { get; set; } = string.Empty;
    public bool IsClientIdConfigured { get; set; }
    public bool IsClientSecretConfigured { get; set; }
}

public sealed class UpdateFinanceEmailSettingsRequest
{
    public UpdateFinanceEmailProviderSettingsRequest Gmail { get; set; } = new();
    public UpdateFinanceEmailProviderSettingsRequest Microsoft365 { get; set; } = new();
}

public sealed class UpdateFinanceEmailProviderSettingsRequest
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
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

public sealed class FinanceDataResetResponse
{
    public Guid CompanyId { get; set; }
    public int TotalDeleted { get; set; }
    public Dictionary<string, int> DeletedCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

public sealed class FinancePaymentResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CounterpartyReference { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public List<NormalizedFinanceInsightResponse> AgentInsights { get; set; } = [];
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

public sealed class FinanceCounterpartyResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CounterpartyType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PaymentTerms { get; set; }
    public string? TaxId { get; set; }
    public decimal? CreditLimit { get; set; }
    public string? PreferredPaymentMethod { get; set; }
    public string? DefaultAccountMapping { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
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
    public List<NormalizedFinanceInsightResponse> AgentInsights { get; set; } = [];
}

public sealed class FinanceBillResponse
{
    public Guid Id { get; set; }
    public Guid CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public string BillNumber { get; set; } = string.Empty;
    public DateTime ReceivedUtc { get; set; }
    public DateTime DueUtc { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class FinanceBillDetailResponse
{
    public Guid Id { get; set; }
    public Guid CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public string BillNumber { get; set; } = string.Empty;
    public DateTime ReceivedUtc { get; set; }
    public DateTime DueUtc { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public FinanceActionPermissionsResponse Permissions { get; set; } = new();
    public FinanceLinkedDocumentAccessResponse LinkedDocument { get; set; } = new();
    public List<NormalizedFinanceInsightResponse> AgentInsights { get; set; } = [];
}

public sealed class FinanceBillInboxRowResponse
{
    public Guid Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string BillReference { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime DetectedUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ConfidenceLevel { get; set; } = string.Empty;
    public int ValidationWarningCount { get; set; }
    public int DuplicateWarningCount { get; set; }
}

public sealed class FinanceBillInboxDetailResponse
{
    public Guid Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierOrgNumber { get; set; }
    public string BillReference { get; set; } = string.Empty;
    public DateTime? BillDateUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public decimal? Amount { get; set; }
    public decimal? VatAmount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? Confidence { get; set; }
    public string ConfidenceLevel { get; set; } = string.Empty;
    public List<FinanceBillExtractedFieldResponse> ExtractedFields { get; set; } = [];
    public List<FinanceBillWarningResponse> ValidationWarnings { get; set; } = [];
    public List<FinanceBillWarningResponse> DuplicateWarnings { get; set; } = [];
    public FinanceBillProposalSummaryResponse ProposalSummary { get; set; } = new();
    public List<FinanceBillReviewActionResponse> ActionHistory { get; set; } = [];
    public bool CanApprove { get; set; }
    public string? ApprovalBlockedReason { get; set; }
}

public sealed class FinanceBillExtractedFieldResponse
{
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? NormalizedValue { get; set; }
    public decimal? Confidence { get; set; }
    public List<FinanceBillEvidenceReferenceResponse> EvidenceReferences { get; set; } = [];
}

public sealed class FinanceBillEvidenceReferenceResponse
{
    public string SourceDocument { get; set; } = string.Empty;
    public string? SourceDocumentType { get; set; }
    public string? PageReference { get; set; }
    public string? SectionReference { get; set; }
    public string? TextSpan { get; set; }
    public string? Locator { get; set; }
    public string? Snippet { get; set; }
}

public sealed class FinanceBillWarningResponse
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
}

public sealed class FinanceBillProposalSummaryResponse
{
    public string Headline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> RiskFlags { get; set; } = [];
    public string ApprovalAsk { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public bool ExplicitlyRequestsApproval { get; set; }
    public bool InitiatesPayment { get; set; }
}

public sealed class FinanceBillReviewActionResponse
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorDisplayName { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string PriorStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
}

public sealed class FinanceBillReviewActionResultResponse
{
    public Guid BillId { get; set; }
    public string PriorStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; }
}

public sealed record FinanceBillReviewActionRequest(string Rationale);

public sealed class StartMailboxConnectionRequest
{
    public string? ReturnUri { get; set; }
    public List<MailboxFolderSelectionRequest>? ConfiguredFolders { get; set; }
}

public sealed class MailboxFolderSelectionRequest
{
    public string ProviderFolderId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public sealed class StartMailboxConnectionResponse
{
    public string AuthorizationUrl { get; set; } = string.Empty;
}

public sealed class MailboxConnectionStatusResponse
{
    public bool IsConnected { get; set; }
    public Guid? MailboxConnectionId { get; set; }
    public string? Provider { get; set; }
    public string? ConnectionStatus { get; set; }
    public string? EmailAddress { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }
    public DateTime? LastSuccessfulScanAtUtc { get; set; }
    public string? LastErrorSummary { get; set; }
    public List<MailboxFolderSelectionSummaryResponse> ConfiguredFolders { get; set; } = [];
    public EmailIngestionRunSummaryResponse? LastRun { get; set; }
}

public sealed class MailboxProviderAvailabilityResponse
{
    public MailboxProviderAvailability Gmail { get; set; } = new()
    {
        Provider = "gmail",
        DisplayName = "Gmail"
    };

    public MailboxProviderAvailability Microsoft365 { get; set; } = new()
    {
        Provider = "microsoft365",
        DisplayName = "Microsoft 365"
    };
}

public sealed class MailboxProviderAvailability
{
    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public string? UnavailableReason { get; set; }
}

public sealed class MailboxFolderSelectionSummaryResponse
{
    public string ProviderFolderId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public sealed class EmailIngestionRunSummaryResponse
{
    public Guid Id { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Provider { get; set; } = string.Empty;
    public DateTime? ScanFromUtc { get; set; }
    public DateTime? ScanToUtc { get; set; }
    public int ScannedMessageCount { get; set; }
    public int DetectedCandidateCount { get; set; }
    public string? FailureDetails { get; set; }
}

public sealed class MailboxScannedMessageResponse
{
    public Guid Id { get; set; }
    public Guid EmailIngestionRunId { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string? FromAddress { get; set; }
    public string? FromDisplayName { get; set; }
    public string? Subject { get; set; }
    public DateTime? ReceivedUtc { get; set; }
    public string? FolderId { get; set; }
    public string? FolderDisplayName { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string CandidateDecision { get; set; } = string.Empty;
    public List<string> MatchedRules { get; set; } = [];
    public string ReasonSummary { get; set; } = string.Empty;
    public string? BodyPreview { get; set; }
    public List<MailboxScannedAttachmentResponse> Attachments { get; set; } = [];
    public DateTime CreatedUtc { get; set; }
}

public sealed class MailboxScannedAttachmentResponse
{
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public bool IsDuplicateByHash { get; set; }
}

public sealed class ManualMailboxScanResponse
{
    public Guid IngestionRunId { get; set; }
    public Guid MailboxConnectionId { get; set; }
    public DateTime ScanFromUtc { get; set; }
    public DateTime ScanToUtc { get; set; }
    public int ScannedMessageCount { get; set; }
    public int DetectedCandidateCount { get; set; }
    public string? FailureDetails { get; set; }
    public string Status { get; set; } = string.Empty;
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

public sealed class NormalizedFinanceInsightResponse
{
    public Guid Id { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public FinanceInsightEntityReferenceResponse EntityReference { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CheckCode { get; set; } = string.Empty;
    public string CheckName { get; set; } = string.Empty;
    public string ConditionKey { get; set; } = string.Empty;
    public List<FinanceInsightEntityReferenceResponse> AffectedEntities { get; set; } = [];
    public DateTime ObservedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public sealed class FinanceInsightEntityReferenceResponse
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsPrimary { get; set; }
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

public sealed class CreateFinancePaymentRequest
{
    public string PaymentType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CounterpartyReference { get; set; } = string.Empty;
}

public sealed class UpsertFinanceCounterpartyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PaymentTerms { get; set; }
    public string? TaxId { get; set; }
    public decimal? CreditLimit { get; set; }
    public string? PreferredPaymentMethod { get; set; }
    public string? DefaultAccountMapping { get; set; }
}
