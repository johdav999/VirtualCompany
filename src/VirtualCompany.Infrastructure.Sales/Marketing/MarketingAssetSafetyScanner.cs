using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Security;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingAssetSafetyOptions
{
    public const string SectionName = "Marketing:AssetSafety";
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "unavailable";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKeySecretReference { get; set; } = string.Empty;
    public string ScannerVersion { get; set; } = "unconfigured-v1";
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Calls a deployment-owned authoritative malware/content-safety service. Disabled or unhealthy configuration
/// deliberately returns pending/error evidence; it never claims that storage or signature validation is a scan.
/// </summary>
public sealed class HttpMarketingAssetSafetyScanner(
    IHttpClientFactory clients,
    IPlatformSecretStore secrets,
    IOptions<MarketingAssetSafetyOptions> options) : IMarketingAssetSafetyScanner
{
    public const string ClientName = nameof(HttpMarketingAssetSafetyScanner);

    public async Task<MarketingAssetScanResult> ScanAsync(MarketingAssetScanRequest request, CancellationToken ct)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            return Pending(request, "scanner_not_enabled", "Enable and configure an authoritative asset scanner before use.");
        if (!Uri.TryCreate(configured.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            return Pending(request, "scanner_endpoint_invalid", "Configure a secure HTTPS scanner endpoint before use.");
        if (string.IsNullOrWhiteSpace(configured.Provider) || string.IsNullOrWhiteSpace(configured.ScannerVersion) ||
            string.IsNullOrWhiteSpace(configured.ApiKeySecretReference))
            return Pending(request, "scanner_configuration_incomplete", "Complete the scanner provider, version, and protected credential configuration.");

        var secret = await secrets.GetAsync(configured.ApiKeySecretReference, null, ct);
        if (secret is null) return Pending(request, "scanner_credential_unavailable", "Restore the protected scanner credential and retry.");

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Value);
        message.Headers.Add("X-Company-Id", request.CompanyId.ToString("D"));
        message.Headers.Add("X-Asset-Id", request.AssetId.ToString("D"));
        message.Headers.Add("X-Content-Sha256", request.Checksum);
        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(request.Content);
        bytes.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        content.Add(bytes, "file", Path.GetFileName(request.FileName));
        message.Content = content;

        using var response = await clients.CreateClient(ClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return Error(request, "scanner_http_error", $"The scanner returned status {(int)response.StatusCode}; inspect scanner health and retry.");
        var payload = await response.Content.ReadFromJsonAsync<ScannerPayload>(cancellationToken: ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Reference) ||
            payload.Result?.Trim().ToLowerInvariant() is not ("passed" or "pending" or "failed" or "error"))
            return Error(request, "scanner_response_invalid", "The scanner response did not match the bounded safety contract.");
        var result = payload.Result.Trim().ToLowerInvariant();
        return new(configured.Provider, payload.Reference, configured.ScannerVersion, result,
            string.IsNullOrWhiteSpace(payload.ReasonCode) ? $"scanner_{result}" : payload.ReasonCode,
            JsonSerializer.Serialize(new { payload.Malware, payload.ContentSafety, payload.PrivacyMetadata,
                payload.ProhibitedContent, payload.DetailsReference }), DateTime.UtcNow);
    }

    private MarketingAssetScanResult Pending(MarketingAssetScanRequest request, string code, string guidance) =>
        new(options.Value.Provider, $"scan:{request.AssetId:N}", options.Value.ScannerVersion, "pending", code,
            JsonSerializer.Serialize(new { guidance, request.Checksum }), DateTime.UtcNow);
    private MarketingAssetScanResult Error(MarketingAssetScanRequest request, string code, string guidance) =>
        new(options.Value.Provider, $"scan:{request.AssetId:N}", options.Value.ScannerVersion, "error", code,
            JsonSerializer.Serialize(new { guidance, request.Checksum }), DateTime.UtcNow);

    private sealed record ScannerPayload(string Reference, string Result, string? ReasonCode, string? Malware,
        string? ContentSafety, string? PrivacyMetadata, string? ProhibitedContent, string? DetailsReference);
}
