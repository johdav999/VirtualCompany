using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Globalization;

namespace VirtualCompany.Application.Finance;

public sealed record FortnoxRequestContext(
    Guid CompanyId,
    Guid? ConnectionId = null,
    string? CorrelationId = null,
    Guid? ApprovedApprovalId = null,
    Guid? ActorUserId = null,
    Guid? WriteRequestId = null,
    bool RetryExternalFailures = true);

public sealed record FortnoxPageOptions(
    DateTimeOffset? LastModified = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SortBy = null,
    string? SortOrder = null,
    int? Page = null,
    int? Limit = null);

public sealed record FortnoxPagedResponse<T>(
    IReadOnlyList<T> Items,
    FortnoxPageMetadata Metadata)
{
    public bool HasNextPage =>
        Metadata.CurrentPage.HasValue &&
        Metadata.TotalPages.HasValue &&
        Metadata.CurrentPage.Value < Metadata.TotalPages.Value;
}

public sealed record FortnoxPageMetadata(
    int? CurrentPage,
    int? TotalPages,
    int? TotalResources,
    int? Limit);

public sealed class FortnoxApiException : Exception
{
    public FortnoxApiException(
        string safeMessage,
        HttpStatusCode? statusCode,
        string category,
        string? fortnoxErrorCode = null,
        string? fortnoxErrorMessage = null,
        bool isTransient = false,
        bool requiresReconnect = false,
        TimeSpan? retryAfter = null)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
        StatusCode = statusCode;
        Category = category;
        FortnoxErrorCode = fortnoxErrorCode;
        FortnoxErrorMessage = fortnoxErrorMessage;
        IsTransient = isTransient;
        RequiresReconnect = requiresReconnect;
        RetryAfter = retryAfter;
    }

    public string SafeMessage { get; }
    public HttpStatusCode? StatusCode { get; }
    public string Category { get; }
    public string? FortnoxErrorCode { get; }
    public string? FortnoxErrorMessage { get; }
    public bool IsTransient { get; }
    public bool RequiresReconnect { get; }
    public TimeSpan? RetryAfter { get; }
}

public sealed class FortnoxApprovalRequiredException : Exception
{
    public FortnoxApprovalRequiredException(Guid approvalId, string safeMessage)
        : base(safeMessage)
    {
        ApprovalId = approvalId;
        SafeMessage = safeMessage;
    }

    public Guid ApprovalId { get; }
    public string SafeMessage { get; }
}

public sealed record FortnoxErrorTranslationContext(
    HttpStatusCode? StatusCode,
    string? Category,
    string? FortnoxErrorCode,
    string? FortnoxErrorMessage,
    TimeSpan? RetryAfter = null);

public interface IFortnoxErrorTranslator
{
    string Translate(FortnoxErrorTranslationContext context);
}

public interface IFortnoxApiClient
{
    Task<FortnoxCompanyInformation> GetCompanyInformationAsync(FortnoxRequestContext context, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxCustomer>> GetCustomersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxSupplier>> GetSuppliersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxInvoice>> GetInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxInvoicePayment>> GetInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxSupplierInvoice>> GetSupplierInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxSupplierInvoicePayment>> GetSupplierInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxVoucher>> GetVouchersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxAccount>> GetAccountsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxArticle>> GetArticlesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxProject>> GetProjectsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<TResponse?> GetAsync<TResponse>(FortnoxRequestContext context, string path, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<TResponse?> PostAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken);
    Task<TResponse?> PostDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken);
    Task<TResponse?> PostMultipartFileDirectAsync<TResponse>(
        FortnoxRequestContext context,
        string path,
        string formFieldName,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken);
    Task<TResponse?> PutAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken);
    Task<TResponse?> PutDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken);
    Task DeleteAsync(FortnoxRequestContext context, string path, CancellationToken cancellationToken);
}

public static class FortnoxWritePayloadSanitizer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] SensitiveNames =
    [
        "access_token",
        "accessToken",
        "refresh_token",
        "refreshToken",
        "client_secret",
        "clientSecret",
        "authorization_code",
        "authorizationCode",
        "code",
        "token",
        "secret"
    ];

    public static string CreateSanitizedJson<T>(T payload)
    {
        if (payload is null)
        {
            return "{}";
        }

        return Redact(JsonSerializer.SerializeToNode(payload, SerializerOptions))?.ToJsonString(SerializerOptions) ?? "{}";
    }

    public static string CreateSummary<T>(T payload)
    {
        if (payload is null)
        {
            return "No payload body.";
        }

        var text = CreateSanitizedJson(payload);
        return text.Length <= 500 ? text : string.Concat(text.AsSpan(0, 497), "...");
    }

    public static string CreatePayloadHash<T>(T payload)
    {
        var redacted = CreateSanitizedJson(payload);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(redacted));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                obj[property.Key] = IsSensitive(property.Key) ? "*** redacted ***" : Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                array[index] = Redact(array[index]);
            }
        }

        return node;
    }

    private static bool IsSensitive(string name) =>
        SensitiveNames.Any(sensitive => name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
}

public sealed class FortnoxCompanyInformation
{
    public string? CompanyName { get; set; }
    public string? OrganizationNumber { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? DatabaseNumber { get; set; }
    public string? CountryCode { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxCustomer
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? CustomerNumber { get; set; }
    public string? Name { get; set; }
    public string? OrganisationNumber { get; set; }
    public string? Email { get; set; }
    public bool? Active { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxSupplier
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? SupplierNumber { get; set; }
    public string? Name { get; set; }
    public string? OrganisationNumber { get; set; }
    public string? Email { get; set; }
    public bool? Active { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxInvoice
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? DocumentNumber { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? CustomerNumber { get; set; }
    public string? CustomerName { get; set; }
    public string? InvoiceDate { get; set; }
    public string? DueDate { get; set; }
    public decimal? Total { get; set; }
    public string? Currency { get; set; }
    public bool? Cancelled { get; set; }
    public bool? Booked { get; set; }
    public decimal? Balance { get; set; }
    public bool? FullyPaid { get; set; }
    public bool? Sent { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxSupplierInvoice
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? GivenNumber { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? SupplierNumber { get; set; }
    public string? SupplierName { get; set; }
    public string? InvoiceDate { get; set; }
    public string? DueDate { get; set; }
    public decimal? Total { get; set; }
    public string? Currency { get; set; }
    public bool? Cancelled { get; set; }
    public bool? Booked { get; set; }
    public decimal? Balance { get; set; }
    public bool? FullyPaid { get; set; }
    public bool? PaymentPending { get; set; }
    public bool? AuthorizePending { get; set; }
    public string? AuthorizerName { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxInvoicePayment
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? Number { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? InvoiceNumber { get; set; }
    public string? InvoiceCustomerName { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? InvoiceCustomerNumber { get; set; }
    public string? InvoiceOCR { get; set; }
    public string? InvoiceDueDate { get; set; }
    public decimal? Amount { get; set; }
    public decimal? AmountCurrency { get; set; }
    public string? Currency { get; set; }
    public bool? Booked { get; set; }
    public string? ModeOfPayment { get; set; }
    public string? PaymentDate { get; set; }
    public string? Source { get; set; }
    public string? VoucherSeries { get; set; }
    public int? VoucherNumber { get; set; }
    public int? VoucherYear { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxSupplierInvoicePayment
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? Number { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? InvoiceNumber { get; set; }
    public string? InvoiceSupplierName { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? InvoiceSupplierNumber { get; set; }
    public string? InvoiceOCR { get; set; }
    public string? InvoiceDueDate { get; set; }
    public decimal? Amount { get; set; }
    public decimal? AmountCurrency { get; set; }
    public string? Currency { get; set; }
    public bool? Booked { get; set; }
    public string? Information { get; set; }
    public string? ModeOfPayment { get; set; }
    public string? PaymentDate { get; set; }
    public string? Source { get; set; }
    public string? VoucherSeries { get; set; }
    public int? VoucherNumber { get; set; }
    public int? VoucherYear { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxVoucher
{
    public string? VoucherSeries { get; set; }
    public int? VoucherNumber { get; set; }
    public string? VoucherDate { get; set; }
    public string? Description { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal? Total { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxAccount
{
    public int? Number { get; set; }
    public string? Description { get; set; }
    public bool? Active { get; set; }
    public string? Type { get; set; }
    public decimal? Balance { get; set; }
    public decimal? CurrentBalance { get; set; }
    public decimal? BalanceBroughtForward { get; set; }
    public decimal? BalanceCarriedForward { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxArticle
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? ArticleNumber { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public decimal? SalesPrice { get; set; }
    public bool? Active { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxProject
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? ProjectNumber { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxEnvelope<T>
{
    public T? Value { get; set; }
    public FortnoxMetaInformation? MetaInformation { get; set; }
}

public sealed class FortnoxMetaInformation
{
    [JsonPropertyName("@CurrentPage")]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("@TotalPages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("@TotalResources")]
    public int? TotalResources { get; set; }

    [JsonPropertyName("@Limit")]
    public int? Limit { get; set; }

    public FortnoxPageMetadata ToMetadata() =>
        new(CurrentPage, TotalPages, TotalResources, Limit);
}

public sealed class FortnoxErrorInformation
{
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? Error { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? Code { get; set; }
    [JsonConverter(typeof(FortnoxFlexibleStringConverter))]
    public string? Message { get; set; }
}

public sealed class FortnoxErrorEnvelope
{
    public FortnoxErrorInformation? ErrorInformation { get; set; }
    public FortnoxErrorInformation? Error { get; set; }
}

internal sealed class FortnoxFlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => bool.TrueString,
            JsonTokenType.False => bool.FalseString,
            _ => throw new JsonException($"Cannot convert Fortnox token type '{reader.TokenType}' to string.")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
