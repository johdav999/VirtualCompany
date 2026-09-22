using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Documents;

internal sealed record GraphSetupSite(string SiteId, string DisplayName);
internal sealed record GraphSetupSource(string ProviderKind, string? SiteId, string DriveId, string RootItemId, string DisplayName, string? Context, string? WebUrl);
internal sealed record GraphSetupFolder(string ItemId, string DisplayName, string? ParentItemId, DateTime? LastModifiedUtc, string? WebUrl);
internal sealed record GraphSetupPage<T>(IReadOnlyList<T> Items, string? NextCursor);
internal sealed record GraphSetupFolderPage(IReadOnlyList<GraphSetupFolder> Items, IReadOnlyList<GraphSetupFolder> Breadcrumbs, string? NextCursor);

internal interface IDocumentRepositoryMicrosoftSetupAdapter
{
    Task<GraphSetupSource?> GetOneDriveForBusinessAsync(string accessToken, CancellationToken cancellationToken);
    Task<GraphSetupPage<GraphSetupSite>> SearchSharePointSitesAsync(string accessToken, string query, string? cursor, int maxItems, CancellationToken cancellationToken);
    Task<GraphSetupPage<GraphSetupSource>> GetSharePointLibrariesAsync(string accessToken, string siteId, string? cursor, int maxItems, CancellationToken cancellationToken);
    Task<GraphSetupFolderPage> BrowseFoldersAsync(string accessToken, GraphSetupSource source, string folderItemId, string? cursor, int maxItems, CancellationToken cancellationToken);
    Task<GraphSetupFolder> ValidateFolderAsync(string accessToken, GraphSetupSource source, string folderItemId, CancellationToken cancellationToken);
}

internal sealed class MicrosoftGraphDocumentRepositorySetupAdapter(
    IHttpClientFactory clients,
    IOptions<MicrosoftGraphDocumentRepositoryOptions> configured) : IDocumentRepositoryMicrosoftSetupAdapter
{
    internal const string ClientName = "MicrosoftGraphDocumentRepositorySetup";
    private readonly MicrosoftGraphDocumentRepositoryOptions options = configured.Value;

    public async Task<GraphSetupSource?> GetOneDriveForBusinessAsync(string accessToken, CancellationToken ct)
    {
        using var document = await SendAsync(accessToken, "me/drive?$select=id,name,driveType,owner,webUrl&$expand=root($select=id,name,folder,parentReference,webUrl)", ct);
        var root = document.RootElement;
        if (!string.Equals(Read(root, "driveType"), "business", StringComparison.OrdinalIgnoreCase)) return null;
        var driveId = Required(root, "id");
        if (!root.TryGetProperty("root", out var rootItem) || !IsFolder(rootItem) || HasRemoteItem(rootItem)) throw InvalidResponse();
        var owner = root.TryGetProperty("owner", out var ownerElement) && ownerElement.TryGetProperty("user", out var user) ? Read(user, "displayName") : null;
        return new(DocumentRepositoryProviderKinds.OneDriveForBusiness, null, driveId, Required(rootItem, "id"), Required(root, "name"), owner, Read(root, "webUrl"));
    }

    public async Task<GraphSetupPage<GraphSetupSite>> SearchSharePointSitesAsync(string accessToken, string query, string? cursor, int maxItems, CancellationToken ct)
    {
        ValidateCount(maxItems);
        query = (query ?? string.Empty).Trim();
        if (query.Length is < 2 or > 100) throw new DocumentRepositoryOnboardingException("invalid_discovery_query", "Enter between 2 and 100 characters to search SharePoint sites.");
        var path = cursor ?? $"sites?search={Uri.EscapeDataString(query)}&$select=id,displayName&$top={maxItems}";
        using var document = await SendAsync(accessToken, ValidateCursor(path, "sites"), ct);
        var items = Values(document).Select(x => new GraphSetupSite(Required(x, "id"), Required(x, "displayName"))).Take(maxItems).ToArray();
        return new(items, Next(document, "sites"));
    }

    public async Task<GraphSetupPage<GraphSetupSource>> GetSharePointLibrariesAsync(string accessToken, string siteId, string? cursor, int maxItems, CancellationToken ct)
    {
        ValidateCount(maxItems); RequireOpaqueId(siteId);
        var prefix = $"sites/{Escape(siteId)}/drives";
        var path = cursor ?? $"{prefix}?$select=id,name,driveType,webUrl&$top={maxItems}";
        using var document = await SendAsync(accessToken, ValidateCursor(path, prefix), ct);
        var items = new List<GraphSetupSource>();
        foreach (var drive in Values(document).Take(maxItems))
        {
            var driveType = Read(drive, "driveType");
            if (string.Equals(driveType, "personal", StringComparison.OrdinalIgnoreCase)) continue;
            var driveId = Required(drive, "id");
            using var rootDocument = await SendAsync(accessToken, $"drives/{Escape(driveId)}/root?$select=id,name,folder,parentReference,remoteItem,webUrl", ct);
            if (!IsFolder(rootDocument.RootElement) || HasRemoteItem(rootDocument.RootElement)) continue;
            items.Add(new(DocumentRepositoryProviderKinds.SharePointLibrary, siteId, driveId, Required(rootDocument.RootElement, "id"), Required(drive, "name"), null, Read(drive, "webUrl")));
        }
        return new(items, Next(document, prefix));
    }

    public async Task<GraphSetupFolderPage> BrowseFoldersAsync(string accessToken, GraphSetupSource source, string folderItemId, string? cursor, int maxItems, CancellationToken ct)
    {
        ValidateCount(maxItems); RequireOpaqueId(source.DriveId); RequireOpaqueId(folderItemId);
        var parent = await GetFolderAsync(accessToken, source, folderItemId, ct);
        var breadcrumbs = await ResolveBreadcrumbsAsync(accessToken, source, parent, ct);
        var prefix = $"drives/{Escape(source.DriveId)}/items/{Escape(folderItemId)}/children";
        var path = cursor ?? $"{prefix}?$select=id,name,lastModifiedDateTime,parentReference,folder,package,remoteItem,webUrl&$top={maxItems}";
        using var document = await SendAsync(accessToken, ValidateCursor(path, prefix), ct);
        var folders = new List<GraphSetupFolder>();
        foreach (var item in Values(document))
        {
            if (!IsFolder(item) || HasRemoteItem(item)) continue;
            var parentDrive = ParentValue(item, "driveId");
            var parentId = ParentValue(item, "id");
            if (!string.Equals(parentDrive, source.DriveId, StringComparison.Ordinal) || !string.Equals(parentId, folderItemId, StringComparison.Ordinal)) throw Boundary();
            folders.Add(ParseFolder(item));
            if (folders.Count == maxItems) break;
        }
        return new(folders, breadcrumbs, Next(document, prefix));
    }

    public async Task<GraphSetupFolder> ValidateFolderAsync(string accessToken, GraphSetupSource source, string folderItemId, CancellationToken ct)
    {
        var folder = await GetFolderAsync(accessToken, source, folderItemId, ct);
        _ = await ResolveBreadcrumbsAsync(accessToken, source, folder, ct);
        return folder;
    }

    private async Task<GraphSetupFolder> GetFolderAsync(string token, GraphSetupSource source, string itemId, CancellationToken ct)
    {
        using var document = await SendAsync(token, $"drives/{Escape(source.DriveId)}/items/{Escape(itemId)}?$select=id,name,lastModifiedDateTime,parentReference,folder,package,remoteItem,webUrl", ct);
        var item = document.RootElement;
        if (!IsFolder(item) || HasRemoteItem(item)) throw Boundary();
        var driveId = ParentValue(item, "driveId");
        if (!string.IsNullOrWhiteSpace(driveId) && !string.Equals(driveId, source.DriveId, StringComparison.Ordinal)) throw Boundary();
        return ParseFolder(item);
    }

    private async Task<IReadOnlyList<GraphSetupFolder>> ResolveBreadcrumbsAsync(string token, GraphSetupSource source, GraphSetupFolder folder, CancellationToken ct)
    {
        var result = new List<GraphSetupFolder> { folder };
        var current = folder;
        for (var depth = 0; depth < options.MaxAncestryDepth; depth++)
        {
            if (string.Equals(current.ItemId, source.RootItemId, StringComparison.Ordinal)) { result.Reverse(); return result; }
            if (string.IsNullOrWhiteSpace(current.ParentItemId)) throw Boundary();
            current = await GetFolderAsync(token, source, current.ParentItemId, ct);
            result.Add(current);
        }
        throw Boundary();
    }

    private async Task<JsonDocument> SendAsync(string token, string path, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw Map(response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new DocumentRepositoryOnboardingException(DocumentRepositoryOnboardingFailureCodes.ProviderUnavailable, "Microsoft 365 discovery timed out.", true); }
        catch (HttpRequestException) { throw new DocumentRepositoryOnboardingException(DocumentRepositoryOnboardingFailureCodes.ProviderUnavailable, "Microsoft 365 discovery is temporarily unavailable.", true); }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private string ValidateCursor(string value, string prefix)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return value.StartsWith(prefix, StringComparison.Ordinal) ? value : throw Boundary();
        var baseUri = new Uri(options.BaseUrl, UriKind.Absolute);
        if (absolute.Scheme != Uri.UriSchemeHttps || !string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) || absolute.Port != baseUri.Port || !absolute.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal)) throw Boundary();
        var relative = absolute.PathAndQuery[baseUri.AbsolutePath.Length..];
        return relative.StartsWith(prefix, StringComparison.Ordinal) ? absolute.ToString() : throw Boundary();
    }

    private string? Next(JsonDocument document, string prefix) => document.RootElement.TryGetProperty("@odata.nextLink", out var next) && next.ValueKind == JsonValueKind.String ? ValidateCursor(next.GetString()!, prefix) : null;
    private static IEnumerable<JsonElement> Values(JsonDocument document) => document.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray() : throw InvalidResponse();
    private static GraphSetupFolder ParseFolder(JsonElement item) => new(Required(item, "id"), Required(item, "name"), ParentValue(item, "id"), item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.TryGetDateTime(out var parsed) ? parsed.ToUniversalTime() : null, Read(item, "webUrl"));
    private static bool IsFolder(JsonElement item) => item.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.Object || item.TryGetProperty("package", out var package) && package.ValueKind == JsonValueKind.Object;
    private static bool HasRemoteItem(JsonElement item) => item.TryGetProperty("remoteItem", out var remote) && remote.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    private static string? ParentValue(JsonElement item, string name) => item.TryGetProperty("parentReference", out var parent) ? Read(parent, name) : null;
    private static string Required(JsonElement element, string name) => Read(element, name) is { Length: > 0 } value ? value : throw InvalidResponse();
    private static string? Read(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static void RequireOpaqueId(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 500) throw Boundary(); }
    private static void ValidateCount(int value) { if (value is < 1 or > 100) throw new DocumentRepositoryOnboardingException("invalid_page_size", "Page size must be between 1 and 100."); }
    private static DocumentRepositoryOnboardingException Map(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new(DocumentRepositoryOnboardingFailureCodes.AccessLost, "Microsoft administrator access was lost. Reconnect to continue."),
        HttpStatusCode.Forbidden => new(DocumentRepositoryOnboardingFailureCodes.DiscoveryAccessDenied, "Microsoft 365 does not allow access to this source."),
        HttpStatusCode.NotFound => new(DocumentRepositoryOnboardingFailureCodes.SourceUnavailable, "The Microsoft 365 source is no longer available."),
        HttpStatusCode.TooManyRequests => new(DocumentRepositoryOnboardingFailureCodes.ProviderThrottled, "Microsoft 365 temporarily throttled discovery. Try again shortly.", true),
        _ when (int)status >= 500 => new(DocumentRepositoryOnboardingFailureCodes.ProviderUnavailable, "Microsoft 365 discovery is temporarily unavailable.", true),
        _ => InvalidResponse()
    };
    private static DocumentRepositoryOnboardingException InvalidResponse() => new(DocumentRepositoryOnboardingFailureCodes.ProviderUnavailable, "Microsoft 365 returned an invalid discovery response.");
    private static DocumentRepositoryOnboardingException Boundary() => new(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The selected Microsoft 365 source is invalid or outside the approved browsing session.");
}