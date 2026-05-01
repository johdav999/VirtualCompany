using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VirtualCompany.Application.Finance;

public sealed record FortnoxRequestContext(
    Guid CompanyId,
    Guid? ConnectionId = null,
    string? CorrelationId = null,
    Guid? ApprovedApprovalId = null,
    Guid? ActorUserId = null);

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

public sealed record FortnoxWriteApprovalCheck(
    Guid CompanyId,
    Guid? ConnectionId,
    Guid? ActorUserId,
    Guid? ApprovedApprovalId,
    string HttpMethod,
    string Path,
    string TargetCompany,
    string EntityType,
    string PayloadSummary,
    string PayloadHash,
    string SanitizedPayloadJson,
    Guid WriteRequestId);

public interface IFortnoxApiClient
{
    Task<FortnoxCompanyInformation> GetCompanyInformationAsync(FortnoxRequestContext context, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxCustomer>> GetCustomersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxSupplier>> GetSuppliersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxInvoice>> GetInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxSupplierInvoice>> GetSupplierInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxVoucher>> GetVouchersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxAccount>> GetAccountsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxArticle>> GetArticlesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<FortnoxPagedResponse<FortnoxProject>> GetProjectsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<TResponse?> GetAsync<TResponse>(FortnoxRequestContext context, string path, FortnoxPageOptions? options, CancellationToken cancellationToken);
    Task<TResponse?> PostAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken);
    Task<TResponse?> PutAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken);
    Task DeleteAsync(FortnoxRequestContext context, string path, CancellationToken cancellationToken);
}

public interface IFortnoxWriteApprovalService
{
    Task EnsureApprovedAsync(FortnoxWriteApprovalCheck check, CancellationToken cancellationToken);
    Task RecordExecutionSucceededAsync(FortnoxWriteApprovalCheck check, object? responsePayload, CancellationToken cancellationToken);
    Task RecordExecutionFailedAsync(FortnoxWriteApprovalCheck check, Exception exception, CancellationToken cancellationToken);
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
    public string? DatabaseNumber { get; set; }
    public string? CountryCode { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxCustomer
{
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
    public string? DocumentNumber { get; set; }
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
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxSupplierInvoice
{
    public string? GivenNumber { get; set; }
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
    public string? LastModified { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class FortnoxArticle
{
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
    public string? Error { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
}

public sealed class FortnoxErrorEnvelope
{
    public FortnoxErrorInformation? ErrorInformation { get; set; }
    public FortnoxErrorInformation? Error { get; set; }
}
