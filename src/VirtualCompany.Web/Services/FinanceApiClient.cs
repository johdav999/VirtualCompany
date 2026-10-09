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
    private const string FinanceDataSourceOperational = "operational";
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
        if (problem is not null && IsMailboxProviderConfigurationProblem(problem))
        {
            return new MailboxProviderNotConfiguredApiException(problem.Detail ?? problem.Title ?? "Mailbox provider OAuth client settings are not configured.");
        }
        if (problem?.Errors is { Count: > 0 })
        {
            return new FinanceApiValidationException(ResolveProblemMessage(problem, FormatProblemMessage(problem)), problem.Errors);
        }
        if (problem?.CurrentVersion is long currentVersion)
        {
            return new ManualJournalConflictApiException(ResolveProblemMessage(problem, FormatProblemMessage(problem)), currentVersion);
        }

        return new FinanceApiException(problem is null ? "The finance request failed." : ResolveProblemMessage(problem, FormatProblemMessage(problem)),
            problem?.ReasonCode ?? problem?.Code);
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
            return FinanceDataSourceOperational;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            FinanceDataSourceOperational => FinanceDataSourceOperational,
            FinanceDataSourceFortnox => FinanceDataSourceFortnox,
            FinanceDataSourceSimulation => FinanceDataSourceSimulation,
            _ => FinanceDataSourceOperational
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
        public string? ReasonCode { get; set; }
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
        public long? CurrentVersion { get; set; }

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
    public FinanceApiException(string message, string? reasonCode = null) : base(message)
    {
        ReasonCode = reasonCode;
    }

    public string? ReasonCode { get; }
}

public sealed class ManualJournalConflictApiException : FinanceApiException
{
    public ManualJournalConflictApiException(string message, long currentVersion) : base(message) => CurrentVersion = currentVersion;
    public long CurrentVersion { get; }
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

public sealed class FinanceMonthlySummaryViewModel
{
    public Guid CompanyId { get; set; }
    public DateTime ReferenceUtc { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public FinanceSimulationClockResponse? Clock { get; set; }
    public FinanceMonthlyProfitAndLossResponse ProfitAndLoss { get; set; } = new();
    public FinanceExpenseBreakdownResponse? ExpenseBreakdown { get; set; }
}

public sealed class ExportSupplierBillPaymentInstructionRequest
{
    public string? ExportMode { get; set; }
}
