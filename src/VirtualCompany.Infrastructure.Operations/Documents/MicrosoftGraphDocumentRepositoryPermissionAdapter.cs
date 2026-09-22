using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Documents;

internal sealed record GraphApplicationPermission(string PermissionId, string Role);
internal sealed record GraphPermissionMutation(string PermissionId, bool Created);

internal interface IDocumentRepositoryMicrosoftPermissionAdapter
{
    Task<GraphApplicationPermission?> FindAsync(string delegatedToken, string driveId, string itemId, Guid applicationId, CancellationToken cancellationToken);
    Task<GraphPermissionMutation> EnsureAsync(string delegatedToken, string driveId, string itemId, Guid applicationId, string role, CancellationToken cancellationToken);
    Task DeleteAsync(string delegatedToken, string driveId, string itemId, string permissionId, CancellationToken cancellationToken);
}

internal sealed class MicrosoftGraphDocumentRepositoryPermissionAdapter(
    IHttpClientFactory clients,
    IOptions<MicrosoftGraphDocumentRepositoryOptions> configured) : IDocumentRepositoryMicrosoftPermissionAdapter
{
    internal const string ClientName = "MicrosoftGraphDocumentRepositoryPermissions";
    private readonly MicrosoftGraphDocumentRepositoryOptions options = configured.Value;

    public async Task<GraphApplicationPermission?> FindAsync(string token, string driveId, string itemId, Guid applicationId, CancellationToken ct)
    {
        var path = $"drives/{Escape(driveId)}/items/{Escape(itemId)}/permissions?$select=id,roles,grantedToV2,inheritedFrom";
        using var document = await SendJsonAsync(token, HttpMethod.Get, path, null, ct);
        if (!document.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
            throw Failure("invalid_permission_response", "Microsoft 365 returned an invalid permission response.");
        foreach (var permission in values.EnumerateArray())
        {
            if (permission.TryGetProperty("inheritedFrom", out var inherited) && inherited.ValueKind == JsonValueKind.Object) continue;
            if (!permission.TryGetProperty("grantedToV2", out var granted) || !granted.TryGetProperty("application", out var application)) continue;
            if (!application.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var parsed) || parsed != applicationId) continue;
            if (!permission.TryGetProperty("id", out var permissionId) || string.IsNullOrWhiteSpace(permissionId.GetString())) continue;
            var role = permission.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                ? roles.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => x is "read" or "write") : null;
            if (role is not null) return new(permissionId.GetString()!, role);
        }
        return null;
    }

    public async Task<GraphPermissionMutation> EnsureAsync(string token, string driveId, string itemId, Guid applicationId, string role, CancellationToken ct)
    {
        if (role is not ("read" or "write")) throw new ArgumentException("Permission role must be read or write.", nameof(role));
        var existing = await FindAsync(token, driveId, itemId, applicationId, ct);
        if (existing is not null)
        {
            if (existing.Role == role) return new(existing.PermissionId, false);
            throw Failure("permission_role_conflict", "The selected folder already has a different application permission. Review it in Microsoft 365 before retrying.");
        }

        var path = $"drives/{Escape(driveId)}/items/{Escape(itemId)}/permissions";
        var body = new { grantedToV2 = new { application = new { id = applicationId.ToString("D") } }, roles = new[] { role } };
        try
        {
            using var document = await SendJsonAsync(token, HttpMethod.Post, path, JsonContent.Create(body), ct);
            if (!document.RootElement.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
                throw Failure("ambiguous_permission_response", "Microsoft 365 did not confirm the permission identity.", true);
            return new(id.GetString()!, true);
        }
        catch (DocumentRepositoryOnboardingException exception) when (exception.Retryable)
        {
            var reconciled = await FindAsync(token, driveId, itemId, applicationId, ct);
            if (reconciled is not null && reconciled.Role == role)
                return new(reconciled.PermissionId, true);
            throw;
        }
    }

    public async Task DeleteAsync(string token, string driveId, string itemId, string permissionId, CancellationToken ct)
    {
        var path = $"drives/{Escape(driveId)}/items/{Escape(itemId)}/permissions/{Escape(permissionId)}";
        using var request = Request(token, HttpMethod.Delete, path, null);
        using var response = await clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode) throw Map(response.StatusCode);
    }

    private async Task<JsonDocument> SendJsonAsync(string token, HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        try
        {
            using var request = Request(token, method, path, content);
            using var response = await clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw Map(response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Failure("permission_outcome_ambiguous", "Microsoft 365 permission provisioning timed out and requires reconciliation.", true);
        }
        catch (HttpRequestException)
        {
            throw Failure("permission_outcome_ambiguous", "Microsoft 365 permission provisioning is temporarily unavailable and requires reconciliation.", true);
        }
    }

    private HttpRequestMessage Request(string token, HttpMethod method, string path, HttpContent? content)
    {
        if (string.IsNullOrWhiteSpace(token)) throw Failure(DocumentRepositoryOnboardingFailureCodes.AccessLost, "Microsoft administrator access was lost. Reconnect to continue.");
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static DocumentRepositoryOnboardingException Map(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => Failure(DocumentRepositoryOnboardingFailureCodes.AccessLost, "Microsoft administrator access was lost. Reconnect to continue."),
        HttpStatusCode.Forbidden => Failure("permission_assignment_denied", "Microsoft 365 did not allow the reviewed selected-resource permission assignment."),
        HttpStatusCode.NotFound => Failure(DocumentRepositoryOnboardingFailureCodes.SourceUnavailable, "The selected Microsoft 365 folder is no longer available."),
        HttpStatusCode.TooManyRequests => Failure(DocumentRepositoryOnboardingFailureCodes.ProviderThrottled, "Microsoft 365 throttled permission provisioning. Retry after the provider delay.", true),
        _ when (int)status >= 500 => Failure("permission_outcome_ambiguous", "Microsoft 365 permission provisioning returned an ambiguous result that requires reconciliation.", true),
        _ => Failure("permission_assignment_failed", "Microsoft 365 rejected the reviewed permission assignment.")
    };
    private static string Escape(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Contains('/') || value.Contains('\\')) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The selected Microsoft 365 resource identity was invalid."); return Uri.EscapeDataString(value); }
    private static DocumentRepositoryOnboardingException Failure(string code, string message, bool retryable = false) => new(code, message, retryable);
}
