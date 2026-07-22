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
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string FinanceDataSourceAll = "all";
    private const string FinanceDataSourceFortnox = "fortnox";
    private const string FinanceDataSourceSimulation = "simulation";
    private readonly ICompanyApiTransport _transport;
    private readonly bool _useOfflineMode;
    private readonly string? _financeDataSourceFilter;
    private readonly ILogger<FinanceApiClient>? _logger;
    private readonly IApiProblemMessageResolver? _problemResolver;

    public FinanceApiClient(
        HttpClient httpClient,
        ILogger<FinanceApiClient>? logger = null,
        bool useOfflineMode = false,
        string? financeDataSourceFilter = null,
        IApiProblemMessageResolver? problemResolver = null)
    {
        _transport = new CompanyApiTransport(httpClient);
        _logger = logger;
        _useOfflineMode = useOfflineMode;
        _financeDataSourceFilter = NormalizeFinanceDataSourceFilter(financeDataSourceFilter);
        _problemResolver = problemResolver;
    }

    public FinanceApiClient(
        ICompanyApiTransport transport,
        ILogger<FinanceApiClient>? logger = null,
        bool useOfflineMode = false,
        string? financeDataSourceFilter = null,
        IApiProblemMessageResolver? problemResolver = null)
    {
        _transport = transport;
        _logger = logger;
        _useOfflineMode = useOfflineMode;
        _financeDataSourceFilter = NormalizeFinanceDataSourceFilter(financeDataSourceFilter);
        _problemResolver = problemResolver;
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(Guid companyId, string uri, CancellationToken cancellationToken)
    {
        var items = await GetAsync<List<T>>(companyId, uri, allowNotFound: false, cancellationToken);
        return items ?? [];
    }

    private async Task<T?> SendCompanyScopedGetAsync<T>(Guid companyId, string uri, bool allowNotFound, CancellationToken cancellationToken)
    {
        using var response = await _transport.SendAsync(companyId, HttpMethod.Get, uri, null, cancellationToken);

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
            using var response = await _transport.SendAsync(
                companyId,
                method,
                uri,
                JsonContent.Create(payload),
                cancellationToken);
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

        var problem = await response.Content.ReadFromJsonAsync<FinanceApiProblemResponse>(SerializerOptions, cancellationToken);
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
            return new FinanceApiValidationException(ResolveProblemMessage(problem, FormatProblemMessage(problem)), problem.Errors);
        }

        return new FinanceApiException(problem is null ? "The finance request failed." : ResolveProblemMessage(problem, FormatProblemMessage(problem)));
    }

    private FinanceApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _transport.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
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

    private static string? NormalizeFinanceDataSourceFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            FinanceDataSourceAll => null,
            FinanceDataSourceFortnox => FinanceDataSourceFortnox,
            FinanceDataSourceSimulation => FinanceDataSourceSimulation,
            _ => null
        };
    }

    private string ResolveProblemMessage(FinanceApiProblemResponse problem, string fallback) =>
        _problemResolver?.Resolve(new global::VirtualCompany.Web.Services.ApiProblemResponse
        {
            Title = problem.Title,
            Detail = problem.Detail,
            Message = problem.Message,
            Code = problem.Code,
            TraceId = problem.TraceId,
            CorrelationId = problem.CorrelationId,
            Arguments = problem.Arguments,
            Errors = problem.Errors
        }, fallback) ?? fallback;

    private static string FormatProblemMessage(FinanceApiProblemResponse problem)
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

    private static bool IsMailboxProviderConfigurationProblem(FinanceApiProblemResponse? problem) =>
        problem is not null &&
        (string.Equals(problem.Title, "Mailbox provider is not configured.", StringComparison.OrdinalIgnoreCase) ||
            (problem.Detail?.Contains("mailbox OAuth client settings are not configured", StringComparison.OrdinalIgnoreCase) ?? false));

    private sealed class FinanceApiProblemResponse
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
        public Dictionary<string, JsonElement> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
    public FinanceTransactionPaymentContextResponse? PaymentContext { get; set; }
}

public sealed class FinanceTransactionPaymentContextResponse
{
    public bool IsPartiallyPaid { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
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
    public string PostingStatus { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = string.Empty;
    public string DueStatus { get; set; } = string.Empty;
    public string DocumentKind { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public FinanceTransactionPaymentContextResponse? PaymentContext { get; set; }
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
    public string PostingStatus { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = string.Empty;
    public string DueStatus { get; set; } = string.Empty;
    public string DocumentKind { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails { get; set; }
    public List<FinanceInvoiceWorkflowHistoryItemResponse> WorkflowHistory { get; set; } = [];
    public FinanceInvoiceWorkflowContextResponse? WorkflowContext { get; set; }
    public FinanceActionPermissionsResponse Permissions { get; set; } = new();
    public FinanceLinkedDocumentAccessResponse LinkedDocument { get; set; } = new();
    public List<NormalizedFinanceInsightResponse> AgentInsights { get; set; } = [];
    public FinanceTransactionPaymentContextResponse? PaymentContext { get; set; }
    public List<FinanceInvoiceRelatedTransactionResponse> RelatedTransactions { get; set; } = [];
}

public sealed class FinanceInvoiceRelatedTransactionResponse
{
    public Guid Id { get; set; }
    public DateTime TransactionUtc { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
}

public sealed class CustomerInvoiceFortnoxActionResponse
{
    public Guid InvoiceId { get; set; }
    public Guid? CreateWriteRequestId { get; set; }
    public Guid? CreateApprovalId { get; set; }
    public string CreateStatus { get; set; } = string.Empty;
    public Guid? BookkeepWriteRequestId { get; set; }
    public Guid? BookkeepApprovalId { get; set; }
    public string? BookkeepStatus { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool CanRequestCreate { get; set; }
    public bool CanExecuteCreate { get; set; }
    public bool CanRequestBookkeep { get; set; }
    public bool CanExecuteBookkeep { get; set; }
    public string? FortnoxInvoiceNumber { get; set; }
    public DateTime? LastSyncedUtc { get; set; }
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
    public string PostingStatus { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = string.Empty;
    public string DueStatus { get; set; } = string.Empty;
    public string DocumentKind { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public FinanceTransactionPaymentContextResponse? PaymentContext { get; set; }
    public SupplierInvoicePaymentProposalResponse? PaymentProposal { get; set; }
    public SupplierInvoiceSourceDocumentAttachmentResponse? SourceDocumentAttachment { get; set; }
    public SupplierInvoiceDraftActionResponse? DraftAction { get; set; }
    public List<SupplierInvoiceCorrectionActionResponse> CorrectionActions { get; set; } = [];
    public SupplierInvoiceEnrichmentActionResponse? EnrichmentAction { get; set; }
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
    public string PostingStatus { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = string.Empty;
    public string DueStatus { get; set; } = string.Empty;
    public string DocumentKind { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public FinanceActionPermissionsResponse Permissions { get; set; } = new();
    public FinanceLinkedDocumentAccessResponse LinkedDocument { get; set; } = new();
    public List<NormalizedFinanceInsightResponse> AgentInsights { get; set; } = [];
    public FinanceTransactionPaymentContextResponse? PaymentContext { get; set; }
    public List<FinanceInvoiceRelatedTransactionResponse> RelatedTransactions { get; set; } = [];
    public SupplierInvoicePaymentProposalResponse? PaymentProposal { get; set; }
    public SupplierInvoiceSourceDocumentAttachmentResponse? SourceDocumentAttachment { get; set; }
    public SupplierInvoiceDraftActionResponse? DraftAction { get; set; }
    public List<SupplierInvoiceCorrectionActionResponse> CorrectionActions { get; set; } = [];
    public SupplierInvoiceEnrichmentActionResponse? EnrichmentAction { get; set; }
    public PaidSupplierBillExpenseAvailabilityResponse? PaidExpensePostingAvailability { get; set; }
}

public sealed class PaidSupplierBillExpenseAvailabilityResponse
{
    public bool CanPost { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "neutral";
    public string Message { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public List<string> BlockingReasons { get; set; } = [];
    public List<string> ReasonCodes { get; set; } = [];
    public bool RequiresApproval { get; set; }
}

public sealed class SupplierInvoicePaymentProposalResponse
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime DueUtc { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedUtc { get; set; }
    public string ExportMode { get; set; } = "register_payment";
    public string ExportStatus { get; set; } = "not_exported";
    public string? ExportProviderKey { get; set; }
    public Guid? ExportConnectionId { get; set; }
    public Guid? ExportRequestedByUserId { get; set; }
    public DateTime? ExportRequestedUtc { get; set; }
    public DateTime? ExportedUtc { get; set; }
    public string? ExportResponseSummary { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class ExportSupplierBillPaymentInstructionRequest
{
    public string? ExportMode { get; set; }
}

public sealed class SupplierInvoiceSourceDocumentAttachmentResponse
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public Guid? DocumentId { get; set; }
    public string Status { get; set; } = "not_attached";
    public string? ProviderKey { get; set; }
    public Guid? ConnectionId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTime? RequestedUtc { get; set; }
    public DateTime? AttachedUtc { get; set; }
    public string? ResponseSummary { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class SupplierInvoiceEnrichmentActionResponse
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public string Status { get; set; } = "not_suggested";
    public string? ProviderKey { get; set; }
    public Guid? ConnectionId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public DateTime? RequestedUtc { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public DateTime? SyncedUtc { get; set; }
    public string? ResponseSummary { get; set; }
    public JsonObject SuggestionPayload { get; set; } = [];
    public JsonArray ReconciliationWarnings { get; set; } = [];
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class SupplierInvoiceDraftActionResponse
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public string Status { get; set; } = "draft";
    public string? ProviderKey { get; set; }
    public Guid? ConnectionId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTime? RequestedUtc { get; set; }
    public DateTime? UpdatedInProviderUtc { get; set; }
    public DateTime? BookedUtc { get; set; }
    public string? ResponseSummary { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class PaidSupplierBillExpensePostingResponse
{
    public Guid BillId { get; set; }
    public Guid DraftActionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Posted { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public Guid? ConnectionId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime? RequestedUtc { get; set; }
    public DateTime? BookedUtc { get; set; }
    public SupplierInvoiceDraftActionResponse DraftAction { get; set; } = new();
}

public sealed class SupplierInvoiceCorrectionActionResponse
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderKey { get; set; }
    public Guid? ConnectionId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public DateTime? RequestedUtc { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public Guid? CreditNoteBillId { get; set; }
    public string? ProviderCreditNoteNumber { get; set; }
    public string? ResponseSummary { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
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
    public FinanceBillSourcePreviewResponse? SourcePreview { get; set; }
    public List<FinanceBillReviewActionResponse> ActionHistory { get; set; } = [];
    public bool CanApprove { get; set; }
    public string? ApprovalBlockedReason { get; set; }
    public FinanceBillFortnoxRegistrationResponse? FortnoxRegistration { get; set; }
}

public sealed class FinanceBillSourcePreviewResponse
{
    public string Title { get; set; } = string.Empty;
    public string? From { get; set; }
    public DateTime? ReceivedUtc { get; set; }
    public string? BodyText { get; set; }
    public string SourceLabel { get; set; } = "Email body";
    public string? FileName { get; set; }
}

public sealed class FinanceBillFortnoxRegistrationResponse
{
    public Guid? WriteRequestId { get; set; }
    public Guid? ApprovalId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool CanRequest { get; set; }
    public bool CanSendDirect { get; set; }
    public bool CanExecute { get; set; }
    public bool HasPendingRequest { get; set; }
    public bool HasExecuted { get; set; }
    public string? FortnoxPath { get; set; }
    public string? ExternalId { get; set; }
    public string? ActionKind { get; set; }
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
    public string Purpose { get; set; } = string.Empty;
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

    public MailboxProviderAvailability HostedEmail { get; set; } = new()
    {
        Provider = "standard_email",
        DisplayName = "Hosted email",
        IsConfigured = true
    };
}

public sealed class StandardMailboxProfileResponse
{
    public string ProfileKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public MailboxEndpointResponse Imap { get; set; } = new();
    public MailboxEndpointResponse Smtp { get; set; } = new();
    public List<string> AuthenticationTypes { get; set; } = [];
    public bool AllowsEndpointOverride { get; set; }
}

public sealed class MailboxEndpointResponse
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string TlsMode { get; set; } = string.Empty;
}

public sealed class StandardMailboxConnectionRequest
{
    public string ProfileKey { get; set; } = "zoho-eu";
    public string EmailAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string AuthenticationType { get; set; } = "application_password";
    public string? Credential { get; set; }
    public MailboxEndpointResponse? Imap { get; set; }
    public MailboxEndpointResponse? Smtp { get; set; }
    public List<string>? SelectedFolderIds { get; set; }
    public string? TestTarget { get; set; }
    public string? ReturnUri { get; set; }
}

public sealed class StandardMailboxConnectionResultResponse
{
    public Guid? ConnectionId { get; set; }
    public bool IncomingSucceeded { get; set; }
    public bool SendingSucceeded { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
    public int Capabilities { get; set; }
    public List<MailboxTransportFolderResponse> Folders { get; set; } = [];
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTime CheckedUtc { get; set; }
}

public sealed class MailboxTransportFolderResponse
{
    public string FolderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool CanRead { get; set; }
    public bool CanAppend { get; set; }
    public bool IsInbox { get; set; }
    public bool IsDrafts { get; set; }
    public bool IsSent { get; set; }
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
    public Guid? DetectedBillId { get; set; }
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

public sealed record StartFinanceIntegrationConnectionRequest(string ReturnUri, bool Reconnect);

public sealed record SyncFinanceIntegrationNowRequest(Guid? ConnectionId, bool FullSync = false);

public sealed class FinanceIntegrationProviderResponse
{
    public string ProviderKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = [];
    public FinanceIntegrationConnectionStatusResponse Status { get; set; } = new();
}

public sealed class StartFinanceIntegrationConnectionResponse
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
}

public sealed class FinanceIntegrationConnectionStatusResponse
{
    public string ProviderKey { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public Guid? ConnectionId { get; set; }
    public string? ConnectionStatus { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }
    public DateTime? AccessTokenExpiresUtc { get; set; }
    public DateTime? LastRefreshAttemptUtc { get; set; }
    public string? LastErrorSummary { get; set; }
    public DateTime? LastSuccessfulSyncUtc { get; set; }
}

public sealed class FinanceIntegrationConnectionDisconnectResponse
{
    public string ProviderKey { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime DisconnectedUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class FinanceIntegrationSyncResultResponse
{
    public string ProviderKey { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<FinanceIntegrationEntitySyncResultResponse> Entities { get; set; } = [];
    public string? ErrorSummary { get; set; }
    public int RetryAttempts { get; set; }
    public string? RetryOutcome { get; set; }
}

public sealed class FinanceIntegrationEntitySyncResultResponse
{
    public string EntityType { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string? ErrorSummary { get; set; }
}

public sealed class FinanceIntegrationSyncHistoryResponse
{
    public string ProviderKey { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public List<FinanceIntegrationSyncHistoryItemResponse> Items { get; set; } = [];
}

public sealed class FinanceIntegrationSyncHistoryItemResponse
{
    public Guid Id { get; set; }
    public Guid? ConnectionId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? ErrorSummary { get; set; }
    public int RetryAttempts { get; set; }
    public string? RetryOutcome { get; set; }
    public List<FinanceIntegrationEntitySyncResultResponse> Entities { get; set; } = [];
}
