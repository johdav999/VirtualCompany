using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxApiClient : IFortnoxApiClient
{
    private const int DefaultMaxRetries = 3;
    private readonly HttpClient _httpClient;
    private readonly IFortnoxOAuthService _oauthService;
    private readonly IFortnoxWriteApprovalService _writeApprovalService;
    private readonly IOptionsMonitor<FortnoxOptions> _options;
    private readonly ILogger<FortnoxApiClient> _logger;
    private readonly TimeProvider _timeProvider;

    public FortnoxApiClient(
        HttpClient httpClient,
        IFortnoxOAuthService oauthService,
        IOptionsMonitor<FortnoxOptions> options,
        ILogger<FortnoxApiClient> logger,
        TimeProvider timeProvider,
        IFortnoxWriteApprovalService? writeApprovalService = null)
    {
        _httpClient = httpClient;
        _oauthService = oauthService;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
        _writeApprovalService = writeApprovalService ?? NoOpFortnoxWriteApprovalService.Instance;

        _httpClient.BaseAddress ??= NormalizeBaseAddress(_options.CurrentValue.ApiBaseUrl);
    }

    public static Uri NormalizeBaseAddress(string? configuredBaseUrl)
    {
        var value = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "https://api.fortnox.se/3"
            : configuredBaseUrl.Trim();

        return new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public Task<FortnoxCompanyInformation> GetCompanyInformationAsync(
        FortnoxRequestContext context,
        CancellationToken cancellationToken) =>
        GetEnvelopeAsync<FortnoxCompanyInformation>(context, "companyinformation", "CompanyInformation", cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxCustomer>> GetCustomersAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxCustomer>(context, "customers", "Customers", options, cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxSupplier>> GetSuppliersAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxSupplier>(context, "suppliers", "Suppliers", options, cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxInvoice>> GetInvoicesAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxInvoice>(context, "invoices", "Invoices", options, cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxSupplierInvoice>> GetSupplierInvoicesAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxSupplierInvoice>(context, "supplierinvoices", "SupplierInvoices", options, cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxVoucher>> GetVouchersAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxVoucher>(context, "vouchers", "Vouchers", options, cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxAccount>> GetAccountsAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxAccount>(context, "accounts", "Accounts", options, cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxArticle>> GetArticlesAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxArticle>(context, "articles", "Articles", options, cancellationToken);

    public Task<FortnoxPagedResponse<FortnoxProject>> GetProjectsAsync(
        FortnoxRequestContext context,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken) =>
        GetPageAsync<FortnoxProject>(context, "projects", "Projects", options, cancellationToken);

    public async Task<TResponse?> GetAsync<TResponse>(
        FortnoxRequestContext context,
        string path,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(context, HttpMethod.Get, path, options, contentFactory: null, cancellationToken);
        return await DeserializeBodyAsync<TResponse>(response, cancellationToken);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        FortnoxRequestContext context,
        string path,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        var check = await EnsureWriteApprovedAsync(context, HttpMethod.Post, path, payload, cancellationToken);
        try
        {
            using var response = await SendAsync(context, HttpMethod.Post, path, null, () => CreateJsonContent(payload), cancellationToken);
            var result = await DeserializeBodyAsync<TResponse>(response, cancellationToken);
            await _writeApprovalService.RecordExecutionSucceededAsync(check, result, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            await _writeApprovalService.RecordExecutionFailedAsync(check, exception, cancellationToken);
            throw;
        }
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(
        FortnoxRequestContext context,
        string path,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        var check = await EnsureWriteApprovedAsync(context, HttpMethod.Put, path, payload, cancellationToken);
        try
        {
            using var response = await SendAsync(context, HttpMethod.Put, path, null, () => CreateJsonContent(payload), cancellationToken);
            var result = await DeserializeBodyAsync<TResponse>(response, cancellationToken);
            await _writeApprovalService.RecordExecutionSucceededAsync(check, result, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            await _writeApprovalService.RecordExecutionFailedAsync(check, exception, cancellationToken);
            throw;
        }
    }

    public async Task DeleteAsync(
        FortnoxRequestContext context,
        string path,
        CancellationToken cancellationToken)
    {
        var check = await EnsureWriteApprovedAsync<object?>(context, HttpMethod.Delete, path, null, cancellationToken);
        try
        {
            using var response = await SendAsync(context, HttpMethod.Delete, path, null, contentFactory: null, cancellationToken);
            await _writeApprovalService.RecordExecutionSucceededAsync(check, null, cancellationToken);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            await _writeApprovalService.RecordExecutionFailedAsync(check, exception, cancellationToken);
            throw;
        }
    }

    private async Task<FortnoxWriteApprovalCheck> EnsureWriteApprovedAsync<TPayload>(
        FortnoxRequestContext context,
        HttpMethod method,
        string path,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var summary = FortnoxWritePayloadSanitizer.CreateSummary(payload);
        var check = new FortnoxWriteApprovalCheck(
                context.CompanyId,
                context.ConnectionId,
                context.ActorUserId,
                context.ApprovedApprovalId,
                method.Method,
                path,
                "Fortnox company",
                ResolveEntityType(path),
                summary,
                FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
                FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload),
                DeterministicWriteRequestId(context, method, path, summary));

        await _writeApprovalService.EnsureApprovedAsync(check, cancellationToken);
        return check;
    }

    private static string ResolveEntityType(string path) =>
        path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "fortnox_record";

    private async Task<T> GetEnvelopeAsync<T>(
        FortnoxRequestContext context,
        string path,
        string envelopeProperty,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(context, HttpMethod.Get, path, null, contentFactory: null, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        if (!TryGetProperty(document.RootElement, envelopeProperty, out var element))
        {
            throw new FortnoxApiException("Fortnox returned an unexpected response.", response.StatusCode, "invalid_response");
        }

        return element.Deserialize<T>(FortnoxJson.Options)
            ?? throw new FortnoxApiException("Fortnox returned an unexpected response.", response.StatusCode, "invalid_response");
    }

    private async Task<FortnoxPagedResponse<T>> GetPageAsync<T>(
        FortnoxRequestContext context,
        string path,
        string collectionProperty,
        FortnoxPageOptions? options,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(context, HttpMethod.Get, path, options, contentFactory: null, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        if (!TryGetProperty(document.RootElement, collectionProperty, out var itemsElement))
        {
            throw new FortnoxApiException("Fortnox returned an unexpected response.", response.StatusCode, "invalid_response");
        }

        var items = itemsElement.Deserialize<IReadOnlyList<T>>(FortnoxJson.Options) ?? [];
        var metadata = TryGetProperty(document.RootElement, "MetaInformation", out var metaElement)
            ? metaElement.Deserialize<FortnoxMetaInformation>(FortnoxJson.Options)?.ToMetadata()
            : null;

        metadata ??= new FortnoxPageMetadata(options?.Page, null, null, options?.Limit);
        return new FortnoxPagedResponse<T>(items, metadata);
    }

    private async Task<HttpResponseMessage> SendAsync(
        FortnoxRequestContext context,
        HttpMethod method,
        string path,
        FortnoxPageOptions? options,
        Func<HttpContent>? contentFactory,
        CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CompanyId"] = context.CompanyId,
            ["FortnoxConnectionId"] = context.ConnectionId,
            ["CorrelationId"] = context.CorrelationId
        });

        var accessToken = await ResolveAccessTokenAsync(context, cancellationToken);
        var requestUri = BuildUri(path, options);
        var maxRetries = Math.Max(0, _options.CurrentValue.ApiMaxRetries);
        if (maxRetries == 0)
        {
            maxRetries = DefaultMaxRetries;
        }

        for (var attempt = 1; ; attempt++)
        {
            using var request = CreateRequest(method, requestUri, accessToken, context.CorrelationId, contentFactory);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt <= maxRetries)
            {
                await DelayForRetryAsync(attempt, null, requestUri, exception, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (IsRetriable(response.StatusCode) && attempt <= maxRetries)
            {
                var retryAfter = GetRetryAfter(response);
                response.Dispose();
                await DelayForRetryAsync(attempt, retryAfter, requestUri, null, cancellationToken);
                continue;
            }

            await ThrowTranslatedExceptionAsync(context, response, requestUri, cancellationToken);
        }
    }

    private async Task<string> ResolveAccessTokenAsync(FortnoxRequestContext context, CancellationToken cancellationToken)
    {
        var tokenResult = await _oauthService.GetValidAccessTokenAsync(
            new RefreshFortnoxAccessTokenCommand(context.CompanyId, context.ConnectionId),
            cancellationToken);

        if (tokenResult.Succeeded && !string.IsNullOrWhiteSpace(tokenResult.AccessToken))
        {
            return tokenResult.AccessToken;
        }

        throw new FortnoxApiException(
            tokenResult.SafeFailureMessage ?? "Fortnox connection needs attention.",
            HttpStatusCode.Unauthorized,
            "authorization",
            isTransient: !tokenResult.NeedsReconnect,
            requiresReconnect: tokenResult.NeedsReconnect);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri requestUri,
        string accessToken,
        string? correlationId,
        Func<HttpContent>? contentFactory)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }

        if (contentFactory is not null)
        {
            request.Content = contentFactory();
        }

        return request;
    }

    private Uri BuildUri(string path, FortnoxPageOptions? options)
    {
        var builder = new UriBuilder(new Uri(_httpClient.BaseAddress!, path.TrimStart('/')));
        var query = BuildQuery(options);
        builder.Query = query;
        return builder.Uri;
    }

    internal static string BuildQuery(FortnoxPageOptions? options)
    {
        if (options is null)
        {
            return string.Empty;
        }

        var values = new List<KeyValuePair<string, string>>();
        Add(values, "lastmodified", options.LastModified?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Add(values, "fromdate", options.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(values, "todate", options.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(values, "sortby", options.SortBy);
        Add(values, "sortorder", options.SortOrder);
        Add(values, "page", options.Page?.ToString(CultureInfo.InvariantCulture));
        Add(values, "limit", options.Limit?.ToString(CultureInfo.InvariantCulture));

        return string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static void Add(List<KeyValuePair<string, string>> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(new KeyValuePair<string, string>(key, value.Trim()));
        }
    }

    private async Task DelayForRetryAsync(
        int attempt,
        TimeSpan? retryAfter,
        Uri requestUri,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? CalculateBackoff(attempt);
        _logger.LogWarning(
            exception,
            "Retrying Fortnox request after transient failure. Attempt {Attempt}; delay {DelayMilliseconds} ms; endpoint {Endpoint}.",
            attempt,
            delay.TotalMilliseconds,
            requestUri.AbsolutePath);

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _timeProvider, cancellationToken);
        }
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        var configuredBaseMilliseconds = _options.CurrentValue.ApiRetryBaseDelayMilliseconds;
        var baseDelay = TimeSpan.FromMilliseconds(configuredBaseMilliseconds > 0 ? configuredBaseMilliseconds : 200);
        var jitter = Random.Shared.Next(0, 75);
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1) + jitter);
        var maxDelay = TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.ApiMaxRetryDelaySeconds));
        return delay <= maxDelay ? delay : maxDelay;
    }

    private static bool IsRetriable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private async Task ThrowTranslatedExceptionAsync(
        FortnoxRequestContext context,
        HttpResponseMessage response,
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        var error = TryReadError(body);
        var retryAfter = GetRetryAfter(response);
        var exception = CreateException(response.StatusCode, error, retryAfter);

        _logger.LogWarning(
            "Fortnox API request failed. Status {StatusCode}; category {Category}; Fortnox code {FortnoxErrorCode}; Fortnox message {FortnoxErrorMessage}; endpoint {Endpoint}; company {CompanyId}; connection {ConnectionId}.",
            (int)response.StatusCode,
            exception.Category,
            exception.FortnoxErrorCode,
            exception.FortnoxErrorMessage,
            requestUri.AbsolutePath,
            context.CompanyId,
            context.ConnectionId);

        response.Dispose();
        throw exception;
    }

    private static FortnoxApiException CreateException(
        HttpStatusCode statusCode,
        FortnoxErrorInformation? error,
        TimeSpan? retryAfter)
    {
        var code = error?.Code ?? error?.Error;
        var message = error?.Message;

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new FortnoxApiException("Fortnox connection needs attention.", statusCode, "authorization", code, message, requiresReconnect: true),
            HttpStatusCode.Forbidden => new FortnoxApiException("The connected Fortnox account does not have permission for this data.", statusCode, "permission", code, message),
            HttpStatusCode.NotFound => new FortnoxApiException("The requested Fortnox data could not be found.", statusCode, "not_found", code, message),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => new FortnoxApiException("Fortnox could not process the requested data.", statusCode, "validation", code, message),
            HttpStatusCode.TooManyRequests => new FortnoxApiException("Fortnox is receiving too many requests. Please try again shortly.", statusCode, "rate_limited", code, message, isTransient: true, retryAfter: retryAfter),
            _ when (int)statusCode >= 500 => new FortnoxApiException("Fortnox is temporarily unavailable. Please try again shortly.", statusCode, "upstream_unavailable", code, message, isTransient: true),
            _ => new FortnoxApiException("Fortnox request failed.", statusCode, "upstream_error", code, message)
        };
    }

    private static FortnoxErrorInformation? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<FortnoxErrorEnvelope>(body, FortnoxJson.Options);
            return envelope?.ErrorInformation ?? envelope?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<TResponse?> DeserializeBodyAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null || response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, FortnoxJson.Options, cancellationToken);
    }

    private static HttpContent CreateJsonContent<T>(T payload) =>
        new StringContent(JsonSerializer.Serialize(payload, FortnoxJson.Options), Encoding.UTF8, "application/json");

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Guid DeterministicWriteRequestId(FortnoxRequestContext context, HttpMethod method, string path, string summary)
    {
        var input = $"{context.CompanyId:N}|{context.ConnectionId:N}|{method.Method}|{path}|{summary}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed class NoOpFortnoxWriteApprovalService : IFortnoxWriteApprovalService
    {
        public static readonly NoOpFortnoxWriteApprovalService Instance = new();

        private NoOpFortnoxWriteApprovalService()
        {
        }

        public Task EnsureApprovedAsync(FortnoxWriteApprovalCheck check, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordExecutionSucceededAsync(FortnoxWriteApprovalCheck check, object? responsePayload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordExecutionFailedAsync(FortnoxWriteApprovalCheck check, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
