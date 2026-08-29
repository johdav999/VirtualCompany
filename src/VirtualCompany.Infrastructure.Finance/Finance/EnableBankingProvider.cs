using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class EnableBankingOptions
{
    public const string SectionName = "EnableBanking";
    public bool Enabled { get; set; }
    public string BaseUri { get; set; } = "https://api.enablebanking.com/";
    public string ApplicationId { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "SE";
    public string PsuType { get; set; } = "business";
    public int ConsentValidityDays { get; set; } = 90;
    public int RequestTimeoutSeconds { get; set; } = 45;
    public bool PaymentInitiationEnabled { get; set; }
    public string Environment { get; set; } = "SANDBOX";
    public string SingleSepaPaymentType { get; set; } = "SEPA";
    public string BulkSepaPaymentType { get; set; } = "BULK_SEPA";
}

public sealed partial class EnableBankingProvider : IBankConnectionProvider, IBankFeedProvider, IPaymentInitiationProvider
{
    public const string ProviderKeyValue = "enable-banking";
    public const string HttpClientName = "enable-banking";
    private static readonly string[] Capabilities =
    [
        BankProviderCapabilities.Accounts,
        BankProviderCapabilities.AccountOwnership,
        BankProviderCapabilities.Balances,
        BankProviderCapabilities.Transactions
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EnableBankingOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EnableBankingProvider> _logger;

    public EnableBankingProvider(IHttpClientFactory httpClientFactory, IOptions<EnableBankingOptions> options,
        TimeProvider clock, ILogger<EnableBankingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public BankProviderDescriptor Descriptor => new(ProviderKeyValue, "Enable Banking", ProviderCapabilities(), IsConfigured());
    string IBankFeedProvider.ProviderKey => ProviderKeyValue;

    public async Task<IReadOnlyList<BankInstitutionDescriptor>> GetInstitutionsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendAsync(HttpMethod.Get,
            $"aspsps?country={Uri.EscapeDataString(_options.CountryCode)}&service=AIS", null, cancellationToken);
        using var document = JsonDocument.Parse(response.Payload);
        if (!document.RootElement.TryGetProperty("aspsps", out var aspsps) || aspsps.ValueKind != JsonValueKind.Array)
            throw Malformed("The bank provider returned an unsupported institution response.");
        return aspsps.EnumerateArray().Select(item =>
        {
            var name = RequiredString(item, "name");
            var country = OptionalString(item, "country") ?? _options.CountryCode;
            return new BankInstitutionDescriptor(InstitutionId(country, name), name, country, ProviderCapabilities());
        }).OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task<BankProviderConsentStartResult> StartConsentAsync(BankProviderConsentStartRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var (country, institution) = ParseInstitutionId(request.InstitutionId);
        var validUntil = _clock.GetUtcNow().UtcDateTime.AddDays(Math.Clamp(_options.ConsentValidityDays, 1, 180));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            access = new { balances = true, transactions = true, valid_until = validUntil.ToString("O", CultureInfo.InvariantCulture) },
            aspsp = new { name = institution, country },
            state = request.ProtectedState,
            redirect_url = request.CallbackUri.ToString(),
            psu_type = _options.PsuType,
            language = "sv"
        });
        using var response = await SendAsync(HttpMethod.Post, "auth", payload, cancellationToken);
        using var document = JsonDocument.Parse(response.Payload);
        var url = RequiredString(document.RootElement, "url");
        var authorizationId = RequiredString(document.RootElement, "authorization_id");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var authorizationUri)) throw Malformed("The bank provider returned an invalid authorization address.");
        return new BankProviderConsentStartResult(authorizationUri, authorizationId, validUntil);
    }

    public async Task<BankProviderConsentResult> CompleteConsentAsync(BankProviderCallbackRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (!string.IsNullOrWhiteSpace(request.ProviderError))
            throw new BankProviderSafeException("bank_authorization_declined", "Bank authorization was not completed.", false);
        if (string.IsNullOrWhiteSpace(request.AuthorizationCode))
            throw new BankProviderSafeException("authorization_code_missing", "The bank did not return an authorization code.", false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { code = request.AuthorizationCode });
        using var response = await SendAsync(HttpMethod.Post, "sessions", payload, cancellationToken);
        using var document = JsonDocument.Parse(response.Payload);
        var root = document.RootElement;
        var sessionId = RequiredString(root, "session_id");
        var institution = root.TryGetProperty("aspsp", out var aspsp) ? RequiredString(aspsp, "name") : ParseInstitutionId(request.InstitutionId).Institution;
        var expires = root.TryGetProperty("access", out var access) ? OptionalDateTime(access, "valid_until") : null;
        return new BankProviderConsentResult(sessionId, institution, expires, ProviderCapabilities(),
            new BankProviderCredentialBundle(sessionId, null, request.ProviderSessionReference, expires));
    }

    public async Task<IReadOnlyList<BankProviderDiscoveredAccount>> DiscoverAccountsAsync(Guid companyId,
        string providerConsentId, BankProviderCredentialBundle credentials, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var sessionResponse = await SendAsync(HttpMethod.Get, $"sessions/{Uri.EscapeDataString(providerConsentId)}", null, cancellationToken);
        using var sessionDocument = JsonDocument.Parse(sessionResponse.Payload);
        var root = sessionDocument.RootElement;
        if (!root.TryGetProperty("accounts_data", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
            throw Malformed("The bank provider returned an unsupported account response.");
        var results = new List<BankProviderDiscoveredAccount>();
        foreach (var account in accounts.EnumerateArray())
        {
            var uid = RequiredString(account, "uid");
            var stableId = RequiredString(account, "identification_hash");
            using var detailsResponse = await SendAsync(HttpMethod.Get, $"accounts/{Uri.EscapeDataString(uid)}/details", null, cancellationToken);
            using var detailsDocument = JsonDocument.Parse(detailsResponse.Payload);
            var details = detailsDocument.RootElement;
            var displayName = OptionalString(details, "name") ?? OptionalString(details, "product") ?? "Bank account";
            var currency = RequiredString(details, "currency");
            var accountNumber = AccountNumber(details);
            var psuStatus = OptionalString(details, "psu_status");
            var ownership = Ownership(psuStatus);
            results.Add(new BankProviderDiscoveredAccount(stableId, displayName, Mask(accountNumber), currency,
                ownership.Status, ownership.Summary, uid));
        }
        return results;
    }

    public async Task<BankProviderHealthResult> GetHealthAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendAsync(HttpMethod.Get, $"sessions/{Uri.EscapeDataString(providerConsentId)}", null, cancellationToken);
        using var document = JsonDocument.Parse(response.Payload);
        var status = OptionalString(document.RootElement, "status");
        return string.Equals(status, "AUTHORIZED", StringComparison.OrdinalIgnoreCase)
            ? new BankProviderHealthResult(BankConnectionHealthStatuses.Healthy, null, null)
            : new BankProviderHealthResult(BankConnectionHealthStatuses.Degraded, BankConnectionReasonCodes.ExpiredConsent,
                "Bank authorization is no longer active. Renew consent before synchronizing.");
    }

    public async Task RevokeConsentAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendAsync(HttpMethod.Delete, $"sessions/{Uri.EscapeDataString(providerConsentId)}", null, cancellationToken);
    }

    public async Task<BankFeedProviderBalances> GetBalancesAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, string providerAccountAccessReference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendAsync(HttpMethod.Get,
            $"accounts/{Uri.EscapeDataString(providerAccountAccessReference)}/balances", null, cancellationToken);
        using var document = JsonDocument.Parse(response.Payload);
        if (!document.RootElement.TryGetProperty("balances", out var balances) || balances.ValueKind != JsonValueKind.Array)
            throw Malformed("The bank provider returned an unsupported balance response.");
        var normalized = balances.EnumerateArray().Select(balance =>
        {
            var amount = RequiredProperty(balance, "balance_amount");
            return new BankFeedProviderBalance(RequiredString(balance, "balance_type"), Decimal(amount, "amount"),
                RequiredString(amount, "currency"), OptionalDateTime(balance, "last_change_date_time"),
                OptionalDateOnly(balance, "reference_date"), OptionalString(balance, "last_committed_transaction"));
        }).ToArray();
        return new BankFeedProviderBalances(normalized, response.Payload, "application/json", response.RequestId);
    }

    public async Task<BankFeedProviderPage> GetTransactionsAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, BankFeedProviderPageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var providerStatus = request.TransactionStatus switch
        {
            BankFeedProviderTransactionStatuses.Booked => "BOOK",
            BankFeedProviderTransactionStatuses.Pending => "PDNG",
            _ => throw new BankProviderSafeException(BankFeedReasonCodes.MalformedSource,
                "The requested bank transaction state is not supported.", false)
        };
        var query = new StringBuilder($"accounts/{Uri.EscapeDataString(request.ProviderAccountAccessReference)}/transactions")
            .Append("?date_from=").Append(request.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append("&date_to=").Append(request.DateTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append("&transaction_status=").Append(providerStatus);
        if (!string.IsNullOrWhiteSpace(request.ContinuationToken))
            query.Append("&continuation_key=").Append(Uri.EscapeDataString(request.ContinuationToken));
        using var response = await SendAsync(HttpMethod.Get, query.ToString(), null, cancellationToken);
        using var document = JsonDocument.Parse(response.Payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("transactions", out var transactions) || transactions.ValueKind != JsonValueKind.Array)
            throw Malformed("The bank provider returned an unsupported transaction response.");
        var normalized = transactions.EnumerateArray().Select(item => NormalizeTransaction(item, request.TransactionStatus)).ToArray();
        return new BankFeedProviderPage(normalized, OptionalString(root, "continuation_key"), response.Payload,
            "application/json", response.RequestId);
    }

    private BankFeedProviderTransaction NormalizeTransaction(JsonElement item, string requestedStatus)
    {
        var stableIdentity = OptionalString(item, "entry_reference");
        if (string.IsNullOrWhiteSpace(stableIdentity))
            throw Malformed("A bank transaction did not include the provider's stable entry reference.");
        var providerStatus = RequiredString(item, "status");
        var status = providerStatus switch
        {
            "BOOK" => BankFeedProviderTransactionStatuses.Booked,
            "PDNG" => BankFeedProviderTransactionStatuses.Pending,
            _ => throw Malformed("A bank transaction used an unsupported status.")
        };
        if (!string.Equals(status, requestedStatus, StringComparison.Ordinal))
            throw Malformed("A bank transaction did not match the requested status page.");
        var amountElement = RequiredProperty(item, "transaction_amount");
        var amount = Decimal(amountElement, "amount");
        if (amount == 0m) throw Malformed("A bank transaction amount was zero.");
        if (string.Equals(RequiredString(item, "credit_debit_indicator"), "DBIT", StringComparison.OrdinalIgnoreCase)) amount = -Math.Abs(amount);
        else amount = Math.Abs(amount);
        var transactionDate = OptionalDateOnly(item, "transaction_date") ?? OptionalDateOnly(item, "booking_date") ?? OptionalDateOnly(item, "value_date")
            ?? throw Malformed("A bank transaction did not include a usable date.");
        var bookingDate = OptionalDateOnly(item, "booking_date");
        var valueDate = OptionalDateOnly(item, "value_date") ?? bookingDate;
        var reference = OptionalString(item, "reference_number") ?? JoinStrings(item, "remittance_information") ?? OptionalString(item, "note") ?? "Bank transaction";
        var counterparty = Counterparty(item, amount > 0);
        return new BankFeedProviderTransaction(stableIdentity, status, bookingDate.HasValue ? AtUtc(bookingDate.Value) : null,
            valueDate.HasValue ? AtUtc(valueDate.Value) : null, AtUtc(transactionDate), amount,
            RequiredString(amountElement, "currency"), Limit(reference, 240), Limit(counterparty, 200), OptionalString(item, "transaction_id"));
    }

    private async Task<ProviderResponse> SendAsync(HttpMethod method, string relativeUri, byte[]? payload,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null) request.Content = new ByteArrayContent(payload) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new BankProviderSafeException(BankConnectionReasonCodes.ProviderOutage, "The bank provider did not respond in time.", true); }
        catch (HttpRequestException exception)
        { throw new BankProviderSafeException(BankConnectionReasonCodes.ProviderOutage, "The bank provider could not be reached.", true, exception); }
        using (response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw Translate(response, bytes);
            var requestId = response.Headers.TryGetValues("X-Request-ID", out var values) ? values.FirstOrDefault() : null;
            return new ProviderResponse(bytes, requestId);
        }
    }

    private BankProviderSafeException Translate(HttpResponseMessage response, byte[] body)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var (code, summary, transient) = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => (BankFeedReasonCodes.RateLimited, "The bank provider rate limit was reached. Synchronization will retry later.", true),
            HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                (BankConnectionReasonCodes.ProviderOutage, "The bank provider is temporarily unavailable.", true),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                (BankConnectionReasonCodes.ScopeLoss, "Bank authorization no longer permits this operation. Renew consent.", false),
            HttpStatusCode.NotFound => (BankConnectionReasonCodes.ExpiredConsent, "The bank authorization or account is no longer available. Renew consent.", false),
            _ when (int)response.StatusCode >= 500 => (BankConnectionReasonCodes.ProviderOutage, "The bank provider is temporarily unavailable.", true),
            _ => ("bank_provider_request_rejected", "The bank provider rejected the request. Review the bank connection before retrying.", false)
        };
        _logger.LogWarning("Enable Banking request failed with HTTP {StatusCode}. SafeReasonCode={ReasonCode}.", (int)response.StatusCode, code);
        return new BankProviderSafeException(code, summary, transient, retryAfter: retryAfter);
    }

    private string CreateJwt()
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "RS256", kid = _options.ApplicationId }));
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iss = "enablebanking.com", aud = "api.enablebanking.com", iat = now, exp = now + 300 }));
        var input = Encoding.ASCII.GetBytes($"{header}.{body}");
        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(PrivateKeyPem()); }
        catch (CryptographicException exception) { throw new BankProviderSafeException(BankConnectionReasonCodes.ProviderNotConfigured, "Enable Banking private-key configuration is invalid.", false, exception); }
        var signature = rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{body}.{Base64Url(signature)}";
    }

    private string PrivateKeyPem() => !string.IsNullOrWhiteSpace(_options.PrivateKeyPem)
        ? _options.PrivateKeyPem
        : !string.IsNullOrWhiteSpace(_options.PrivateKeyPath) && File.Exists(_options.PrivateKeyPath)
            ? File.ReadAllText(_options.PrivateKeyPath)
            : throw new BankProviderSafeException(BankConnectionReasonCodes.ProviderNotConfigured,
                "Enable Banking private-key configuration is missing.", false);

    private bool IsConfigured() => _options.Enabled && Uri.TryCreate(_options.BaseUri, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(_options.ApplicationId) &&
        (!string.IsNullOrWhiteSpace(_options.PrivateKeyPem) || !string.IsNullOrWhiteSpace(_options.PrivateKeyPath));
    private void EnsureConfigured() { if (!IsConfigured()) throw new BankProviderSafeException(BankConnectionReasonCodes.ProviderNotConfigured, "Enable Banking is not configured for this environment.", false); }
    private IReadOnlyCollection<string> ProviderCapabilities() => _options.PaymentInitiationEnabled
        ? [.. Capabilities, BankProviderCapabilities.PaymentInitiation]
        : Capabilities;
    private static BankProviderSafeException Malformed(string summary) => new(BankFeedReasonCodes.MalformedSource, summary, false);
    private static JsonElement RequiredProperty(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value : throw Malformed($"The bank provider response omitted {name}.");
    private static string RequiredString(JsonElement element, string name) => OptionalString(element, name) ?? throw Malformed($"The bank provider response omitted {name}.");
    private static string? OptionalString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : null;
    private static DateTime? OptionalDateTime(JsonElement element, string name) => OptionalString(element, name) is { } value && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.UtcDateTime : null;
    private static DateOnly? OptionalDateOnly(JsonElement element, string name) => OptionalString(element, name) is { } value && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    private static decimal Decimal(JsonElement element, string name) => decimal.TryParse(RequiredString(element, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : throw Malformed("A bank amount was invalid.");
    private static DateTime AtUtc(DateOnly value) => DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    private static string? JoinStrings(JsonElement item, string name) => item.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array ? string.Join(" ", values.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } joined ? joined : null : null;
    private static string Counterparty(JsonElement item, bool incoming)
    {
        var property = incoming ? "debtor" : "creditor";
        return item.TryGetProperty(property, out var party) ? OptionalString(party, "name") ?? "Unknown counterparty" : "Unknown counterparty";
    }
    private static string AccountNumber(JsonElement details)
    {
        if (details.TryGetProperty("account_id", out var accountId) && accountId.ValueKind == JsonValueKind.Object)
            foreach (var property in accountId.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString())) return property.Value.GetString()!;
        return RequiredString(details, "identification_hash");
    }
    private static (string Status, string Summary) Ownership(string? psuStatus)
    {
        if (psuStatus?.Contains("holder", StringComparison.OrdinalIgnoreCase) == true && !psuStatus.Contains("attorney", StringComparison.OrdinalIgnoreCase))
            return (BankAccountOwnershipStatuses.Verified, $"Provider relationship: {psuStatus}.");
        return (BankAccountOwnershipStatuses.Unverified, string.IsNullOrWhiteSpace(psuStatus) ? "The provider did not return an account-holder relationship." : $"Provider relationship requires review: {psuStatus}.");
    }
    private static string Mask(string value) { var compact = new string(value.Where(char.IsLetterOrDigit).ToArray()); return compact.Length <= 4 ? $"•••• {compact}" : $"•••• {compact[^4..]}"; }
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private static string InstitutionId(string country, string institution) => $"{country.Trim().ToUpperInvariant()}|{institution.Trim()}";
    private static (string Country, string Institution) ParseInstitutionId(string value)
    {
        var split = value.Split('|', 2, StringSplitOptions.TrimEntries);
        return split.Length == 2 && split.All(x => !string.IsNullOrWhiteSpace(x)) ? (split[0].ToUpperInvariant(), split[1]) : throw new BankProviderSafeException("institution_not_supported", "The selected institution identifier is invalid.", false);
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed class ProviderResponse : IDisposable
    {
        public ProviderResponse(byte[] payload, string? requestId) { Payload = payload; RequestId = requestId; }
        public byte[] Payload { get; } public string? RequestId { get; }
        public void Dispose() { }
    }
}
