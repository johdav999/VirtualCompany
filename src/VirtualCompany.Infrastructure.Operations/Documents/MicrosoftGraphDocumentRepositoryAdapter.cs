using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Cryptography;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class MicrosoftGraphDocumentRepositoryOptions
{
    public const string SectionName = "MicrosoftGraphDocumentRepositories";
    public string BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0/";
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxProviderPagesPerBrowse { get; set; } = 20;
    public int MaxAncestryDepth { get; set; } = 256;
    public int MaxImportItems { get; set; } = 500;
    public string[] AllowedDownloadHostSuffixes { get; set; } = [".sharepoint.com", ".onedrive.com"];
}

internal sealed record GraphRepositoryContext(
    string ProviderKind,
    Guid DirectoryTenantId,
    Guid ApplicationClientId,
    string CredentialReference,
    string DriveId,
    string RootItemId);

internal sealed record GraphRepositoryValidation(string RepositoryName, string RootName);
internal sealed record GraphBrowsePage(IReadOnlyList<DocumentRepositoryBrowseItem> Items, bool IsTruncated);
internal sealed record GraphRepositoryFile(string ItemId, string Name, long SizeBytes, string RemoteVersion, string? WebUrl, string? ContentType, DateTime? LastModifiedUtc);
internal sealed record GraphRepositoryChange(string ItemId, string? ParentItemId, string Name, bool IsFolder, bool IsDeleted, long SizeBytes, string? RemoteVersion, string? WebUrl, string? ContentType, DateTime? LastModifiedUtc);
internal sealed record GraphRepositoryDeltaPage(IReadOnlyList<GraphRepositoryChange> Changes, string? NextCursor, string? DeltaCursor);
internal sealed record GraphRepositoryItemAvailability(bool IsAvailable, string? RemoteVersion, string? ParentItemId = null, string? Name = null, long? SizeBytes = null, string? ContentType = null, string? WebUrl = null);
internal sealed record GraphRepositoryCreatedFile(string ItemId, string? RemoteVersion, string? WebUrl);
internal sealed record GraphRepositoryReconciliation(bool Found, bool ContentMatches, GraphRepositoryCreatedFile? File);

internal interface IDocumentRepositoryGraphAdapter
{
    Task<GraphRepositoryValidation> ValidateAsync(GraphRepositoryContext context, CancellationToken cancellationToken);
    Task<GraphBrowsePage> BrowseAsync(GraphRepositoryContext context, string parentItemId, int maxItems, CancellationToken cancellationToken);
    Task<IReadOnlyList<GraphRepositoryFile>> EnumerateFilesAsync(GraphRepositoryContext context, CancellationToken cancellationToken);
    Task<GraphRepositoryDeltaPage> ReadDeltaPageAsync(GraphRepositoryContext context, string? cursor, CancellationToken cancellationToken);
    Task<GraphRepositoryItemAvailability> ValidateItemAsync(GraphRepositoryContext context, string itemId, CancellationToken cancellationToken);
    Task CopyContentAsync(GraphRepositoryContext context, string itemId, Stream destination, long maxBytes, CancellationToken cancellationToken);
    Task CopyContentVersionAsync(GraphRepositoryContext context, string itemId, string expectedRemoteVersion, Stream destination, long maxBytes, CancellationToken cancellationToken);
    Task<GraphRepositoryCreatedFile> CreateFileAsync(GraphRepositoryContext context, string folderItemId, string fileName, Stream content, long sizeBytes, string? contentType, CancellationToken cancellationToken);
    Task<GraphRepositoryCreatedFile> UpdateFileAsync(GraphRepositoryContext context, string itemId, string expectedRemoteVersion, Stream content, long sizeBytes, string? contentType, CancellationToken cancellationToken);
    Task<GraphRepositoryReconciliation> ReconcileFileAsync(GraphRepositoryContext context, string folderItemId, string fileName, string expectedSha256, long expectedSizeBytes, CancellationToken cancellationToken);
    Task<GraphRepositoryReconciliation> ReconcileUpdatedFileAsync(GraphRepositoryContext context, string itemId, string expectedSha256, long expectedSizeBytes, CancellationToken cancellationToken);
}

internal interface IMicrosoftGraphApplicationTokenProvider
{
    Task<string> GetAccessTokenAsync(GraphRepositoryContext context, CancellationToken cancellationToken);
    void Invalidate(GraphRepositoryContext context);
}

internal sealed class MicrosoftGraphApplicationTokenProvider(
    IPlatformSecretStore secretStore,
    IOptions<MicrosoftGraphDocumentRepositoryOptions> options) : IMicrosoftGraphApplicationTokenProvider
{
    private readonly ConcurrentDictionary<TokenCacheKey, CachedToken> _tokens = new();
    private readonly ConcurrentDictionary<TokenCacheKey, SemaphoreSlim> _locks = new();
    private readonly string _scope = new Uri(options.Value.BaseUrl, UriKind.Absolute).GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/.default";

    public async Task<string> GetAccessTokenAsync(GraphRepositoryContext context, CancellationToken cancellationToken)
    {
        var secret = await secretStore.GetAsync(context.CredentialReference, null, cancellationToken)
            ?? throw new DocumentRepositoryUnavailableException(DocumentRepositoryValidationCodes.InvalidCredentials, "The configured Microsoft Graph credential is unavailable.");
        var key = BuildKey(context);
        if (_tokens.TryGetValue(key, out var cached) && cached.SecretVersion == secret.Version && cached.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            return cached.Value;

        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_tokens.TryGetValue(key, out cached) && cached.SecretVersion == secret.Version && cached.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(5))
                return cached.Value;

            try
            {
                var credential = new ClientSecretCredential(
                    context.DirectoryTenantId.ToString("D"),
                    context.ApplicationClientId.ToString("D"),
                    secret.Value);
                var token = await credential.GetTokenAsync(new TokenRequestContext([_scope]), cancellationToken);
                _tokens[key] = new CachedToken(token.Token, token.ExpiresOn, secret.Version);
                return token.Token;
            }
            catch (AuthenticationFailedException exception)
            {
                throw new DocumentRepositoryUnavailableException(DocumentRepositoryValidationCodes.InvalidCredentials, "Microsoft Graph application authentication failed.") { Source = exception.Source };
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(GraphRepositoryContext context) => _tokens.TryRemove(BuildKey(context), out _);
    private TokenCacheKey BuildKey(GraphRepositoryContext context) => new(context.DirectoryTenantId, context.ApplicationClientId, context.CredentialReference, _scope);
    private sealed record TokenCacheKey(Guid TenantId, Guid ClientId, string CredentialReference, string Scope);
    private sealed record CachedToken(string Value, DateTimeOffset ExpiresUtc, string SecretVersion);
}

internal sealed class MicrosoftGraphDocumentRepositoryAdapter(
    IHttpClientFactory httpClientFactory,
    IMicrosoftGraphApplicationTokenProvider tokenProvider,
    IOptions<MicrosoftGraphDocumentRepositoryOptions> options) : IDocumentRepositoryGraphAdapter
{
    public const string ClientName = "MicrosoftGraphDocumentRepositories";
    private readonly MicrosoftGraphDocumentRepositoryOptions _options = options.Value;

    public async Task<GraphRepositoryDeltaPage> ReadDeltaPageAsync(GraphRepositoryContext context, string? cursor, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(cursor)
            ? $"drives/{Escape(context.DriveId)}/items/{Escape(context.RootItemId)}/delta?$select=id,name,size,lastModifiedDateTime,parentReference,folder,package,remoteItem,eTag,cTag,webUrl,file,deleted&$top=200"
            : ValidateDeltaCursor(context, cursor);
        using var page = await SendAsync(context, path, cancellationToken);
        if (!page.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) throw InvalidProviderResponse();
        var changes = new List<GraphRepositoryChange>();
        foreach (var element in values.EnumerateArray())
        {
            var deleted = element.TryGetProperty("deleted", out var deletedFacet) && deletedFacet.ValueKind == JsonValueKind.Object;
            var id = ReadRequiredString(element, "id", "Microsoft Graph returned a delta item without an identity.");
            var name = TryReadString(element, "name") ?? id;
            string? driveId = null; string? parentId = null;
            if (element.TryGetProperty("parentReference", out var parent) && parent.ValueKind == JsonValueKind.Object) { driveId = TryReadString(parent, "driveId"); parentId = TryReadString(parent, "id"); }
            if (!string.IsNullOrWhiteSpace(driveId) && !string.Equals(driveId, context.DriveId, StringComparison.Ordinal)) throw BoundaryViolation();
            var item = ParseItemForDelta(element, id, name, parentId, deleted);
            changes.Add(item);
        }
        return new GraphRepositoryDeltaPage(changes, ReadDeltaLink(page.RootElement, "@odata.nextLink", context), ReadDeltaLink(page.RootElement, "@odata.deltaLink", context));
    }

    public async Task<GraphRepositoryItemAvailability> ValidateItemAsync(GraphRepositoryContext context, string itemId, CancellationToken cancellationToken)
    {
        try
        {
            var item = await GetItemAsync(context, itemId, cancellationToken);
            EnsureLocalItem(context, item, string.Equals(item.Id, context.RootItemId, StringComparison.Ordinal));
            await EnsureWithinRootAsync(context, item, cancellationToken);
            return new GraphRepositoryItemAvailability(true, item.RemoteVersion, item.ParentItemId, item.Name, item.SizeBytes, item.ContentType, item.WebUrl);
        }
        catch (DocumentRepositoryUnavailableException exception) when (exception.Code is DocumentRepositoryValidationCodes.NotFound or DocumentRepositoryValidationCodes.MissingAccess or DocumentRepositoryValidationCodes.BoundaryViolation)
        {
            return new GraphRepositoryItemAvailability(false, null);
        }
    }

    public async Task<IReadOnlyList<GraphRepositoryFile>> EnumerateFilesAsync(GraphRepositoryContext context, CancellationToken cancellationToken)
    {
        var files = new List<GraphRepositoryFile>();
        var folders = new Queue<string>(); folders.Enqueue(context.RootItemId);
        while (folders.Count > 0)
        {
            var folderId = folders.Dequeue();
            string? next = BuildChildrenPath(context.DriveId, folderId, 200); var pages = 0;
            while (next is not null)
            {
                if (++pages > _options.MaxProviderPagesPerBrowse) throw new DocumentRepositoryUnavailableException("import_page_limit", "A repository folder exceeds the configured import page limit.");
                using var page = await SendAsync(context, next, cancellationToken);
                if (!page.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) throw InvalidProviderResponse();
                foreach (var element in values.EnumerateArray())
                {
                    var item = ParseItem(element); EnsureLocalItem(context, item, false);
                    if (!string.Equals(item.ParentItemId, folderId, StringComparison.Ordinal)) throw BoundaryViolation();
                    if (item.IsFolder) folders.Enqueue(item.Id);
                    else
                    {
                        if (files.Count >= _options.MaxImportItems) throw new DocumentRepositoryUnavailableException("import_limit_exceeded", "The repository contains more files than the configured initial-import limit.");
                        files.Add(new GraphRepositoryFile(item.Id, item.Name, item.SizeBytes ?? 0, item.RemoteVersion ?? item.LastModifiedUtc?.Ticks.ToString() ?? "unknown", item.WebUrl, item.ContentType, item.LastModifiedUtc));
                    }
                }
                next = ReadNextLink(page.RootElement, context, folderId);
            }
        }
        return files;
    }

    public Task CopyContentAsync(GraphRepositoryContext context, string itemId, Stream destination, long maxBytes, CancellationToken cancellationToken) =>
        CopyContentCoreAsync(context, itemId, null, destination, maxBytes, cancellationToken);

    public Task CopyContentVersionAsync(GraphRepositoryContext context, string itemId, string expectedRemoteVersion,
        Stream destination, long maxBytes, CancellationToken cancellationToken) =>
        CopyContentCoreAsync(context, itemId, expectedRemoteVersion, destination, maxBytes, cancellationToken);

    private async Task CopyContentCoreAsync(GraphRepositoryContext context, string itemId, string? expectedRemoteVersion,
        Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"drives/{Escape(context.DriveId)}/items/{Escape(itemId)}/content");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync(context, cancellationToken));
        if (!string.IsNullOrWhiteSpace(expectedRemoteVersion)) request.Headers.TryAddWithoutValidation("If-Match", expectedRemoteVersion);
        using var first = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        HttpResponseMessage response = first;
        HttpResponseMessage? redirected = null;
        if ((int)first.StatusCode is >= 300 and < 400 && first.Headers.Location is { } location)
        {
            if (!location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps || !_options.AllowedDownloadHostSuffixes.Any(s => location.Host.EndsWith(s, StringComparison.OrdinalIgnoreCase))) throw BoundaryViolation();
            redirected = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, location), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response = redirected;
        }
        using (redirected)
        {
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                var current = await ValidateItemAsync(context, itemId, cancellationToken);
                throw new DocumentRepositoryConflictException("The remote file changed before the reviewed original could be captured.", current.RemoteVersion);
            }
            if (!response.IsSuccessStatusCode) throw MapFailure(response.StatusCode, ReadRetryAfter(response));
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[64 * 1024]; long total = 0;
            while (true) { var read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) break; total += read; if (total > maxBytes) throw new DocumentRepositoryUnavailableException("file_too_large", "The document exceeds the configured import size limit."); await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken); }
        }
    }

    public async Task<GraphRepositoryCreatedFile> CreateFileAsync(GraphRepositoryContext context, string folderItemId,
        string fileName, Stream content, long sizeBytes, string? contentType, CancellationToken cancellationToken)
    {
        var folder = await GetItemAsync(context, folderItemId, cancellationToken);
        EnsureLocalItem(context, folder, false);
        if (!folder.IsFolder) throw new DocumentRepositoryValidationException("The configured publication target is not a folder.");
        await EnsureWithinRootAsync(context, folder, cancellationToken);
        var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"drives/{Escape(context.DriveId)}/items/{Escape(folderItemId)}:/{Uri.EscapeDataString(fileName)}:/content");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync(context, cancellationToken));
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentLength = sizeBytes;
        if (!string.IsNullOrWhiteSpace(contentType)) request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DocumentRepositoryAmbiguousWriteException("Microsoft Graph timed out after the file upload started.");
        }
        catch (HttpRequestException)
        {
            throw new DocumentRepositoryAmbiguousWriteException("The Microsoft Graph upload result could not be confirmed.");
        }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
                throw new DocumentRepositoryConflictException("A file with the proposed name already exists. Existing files are never overwritten.");
            if (!response.IsSuccessStatusCode) throw MapFailure(response.StatusCode, ReadRetryAfter(response));
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var item = ParseItem(document.RootElement);
            EnsureLocalItem(context, item, false);
            if (!string.Equals(item.ParentItemId, folderItemId, StringComparison.Ordinal)) throw BoundaryViolation();
            return new GraphRepositoryCreatedFile(item.Id, item.RemoteVersion, item.WebUrl);
        }
    }

    public async Task<GraphRepositoryCreatedFile> UpdateFileAsync(GraphRepositoryContext context, string itemId,
        string expectedRemoteVersion, Stream content, long sizeBytes, string? contentType, CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(context, itemId, cancellationToken);
        EnsureLocalItem(context, item, false);
        if (item.IsFolder) throw new DocumentRepositoryValidationException("A folder cannot be replaced as a file.");
        await EnsureWithinRootAsync(context, item, cancellationToken);
        if (!string.Equals(item.RemoteVersion, expectedRemoteVersion, StringComparison.Ordinal))
            throw new DocumentRepositoryConflictException("The remote file changed after the proposal was prepared.", item.RemoteVersion);

        var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"drives/{Escape(context.DriveId)}/items/{Escape(itemId)}/content");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync(context, cancellationToken));
        request.Headers.TryAddWithoutValidation("If-Match", expectedRemoteVersion);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentLength = sizeBytes;
        if (!string.IsNullOrWhiteSpace(contentType)) request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new DocumentRepositoryAmbiguousWriteException("Microsoft Graph timed out after the conditional update started."); }
        catch (HttpRequestException)
        { throw new DocumentRepositoryAmbiguousWriteException("The Microsoft Graph conditional update result could not be confirmed."); }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                var current = await ValidateItemAsync(context, itemId, cancellationToken);
                throw new DocumentRepositoryConflictException("A newer human edit was detected. The reviewed replacement was not uploaded.", current.RemoteVersion);
            }
            if (!response.IsSuccessStatusCode) throw MapFailure(response.StatusCode, ReadRetryAfter(response));
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var updated = ParseItem(document.RootElement);
            EnsureLocalItem(context, updated, false);
            if (!string.Equals(updated.Id, itemId, StringComparison.Ordinal)) throw BoundaryViolation();
            return new GraphRepositoryCreatedFile(updated.Id, updated.RemoteVersion, updated.WebUrl);
        }
    }

    public async Task<GraphRepositoryReconciliation> ReconcileUpdatedFileAsync(GraphRepositoryContext context,
        string itemId, string expectedSha256, long expectedSizeBytes, CancellationToken cancellationToken)
    {
        GraphItem item;
        try { item = await GetItemAsync(context, itemId, cancellationToken); }
        catch (DocumentRepositoryUnavailableException ex) when (ex.Code == DocumentRepositoryValidationCodes.NotFound)
        { return new GraphRepositoryReconciliation(false, false, null); }
        EnsureLocalItem(context, item, false);
        await EnsureWithinRootAsync(context, item, cancellationToken);
        var file = new GraphRepositoryCreatedFile(item.Id, item.RemoteVersion, item.WebUrl);
        if (item.IsFolder || item.SizeBytes != expectedSizeBytes) return new GraphRepositoryReconciliation(true, false, file);
        await using var downloaded = new MemoryStream();
        await CopyContentVersionAsync(context, item.Id, item.RemoteVersion ?? string.Empty, downloaded, expectedSizeBytes, cancellationToken);
        downloaded.Position = 0;
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken)).ToLowerInvariant();
        var matches = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(expectedSha256));
        return new GraphRepositoryReconciliation(true, matches, file);
    }

    public async Task<GraphRepositoryReconciliation> ReconcileFileAsync(GraphRepositoryContext context, string folderItemId,
        string fileName, string expectedSha256, long expectedSizeBytes, CancellationToken cancellationToken)
    {
        var folder = await GetItemAsync(context, folderItemId, cancellationToken);
        EnsureLocalItem(context, folder, false);
        await EnsureWithinRootAsync(context, folder, cancellationToken);
        string? next = BuildChildrenPath(context.DriveId, folderItemId, 200);
        GraphItem? match = null;
        var pages = 0;
        while (next is not null && match is null)
        {
            if (++pages > _options.MaxProviderPagesPerBrowse) throw new DocumentRepositoryUnavailableException("reconciliation_page_limit", "The publication folder exceeds the configured reconciliation page limit.");
            using var page = await SendAsync(context, next, cancellationToken);
            if (!page.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) throw InvalidProviderResponse();
            foreach (var element in values.EnumerateArray())
            {
                var item = ParseItem(element);
                EnsureLocalItem(context, item, false);
                if (string.Equals(item.Name, fileName, StringComparison.Ordinal))
                {
                    match = item;
                    break;
                }
            }
            next = ReadNextLink(page.RootElement, context, folderItemId);
        }
        if (match is null) return new GraphRepositoryReconciliation(false, false, null);
        if (match.IsFolder || match.SizeBytes != expectedSizeBytes)
            return new GraphRepositoryReconciliation(true, false, new GraphRepositoryCreatedFile(match.Id, match.RemoteVersion, match.WebUrl));
        await using var downloaded = new MemoryStream();
        await CopyContentAsync(context, match.Id, downloaded, expectedSizeBytes, cancellationToken);
        downloaded.Position = 0;
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken)).ToLowerInvariant();
        var matches = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(expectedSha256));
        return new GraphRepositoryReconciliation(true, matches, new GraphRepositoryCreatedFile(match.Id, match.RemoteVersion, match.WebUrl));
    }

    public async Task<GraphRepositoryValidation> ValidateAsync(GraphRepositoryContext context, CancellationToken cancellationToken)
    {
        var root = await GetItemAsync(context, context.RootItemId, cancellationToken);
        EnsureLocalItem(context, root, allowMissingParent: true);
        if (!root.IsFolder)
            throw new DocumentRepositoryUnavailableException(DocumentRepositoryValidationCodes.BoundaryViolation, "The approved repository root must be a folder.");

        if (context.ProviderKind == DocumentRepositoryProviderKinds.OneDriveForBusiness)
            return new GraphRepositoryValidation(root.Name, root.Name);

        using var drive = await SendAsync(context, $"drives/{Escape(context.DriveId)}?$select=id,name,driveType", cancellationToken);
        var repositoryName = ReadRequiredString(drive.RootElement, "name", "Microsoft Graph returned an invalid repository response.");
        return new GraphRepositoryValidation(repositoryName, root.Name);
    }

    public async Task<GraphBrowsePage> BrowseAsync(GraphRepositoryContext context, string parentItemId, int maxItems, CancellationToken cancellationToken)
    {
        if (maxItems is < 1 or > 200) throw new DocumentRepositoryValidationException("Browse maxItems must be between 1 and 200.");
        var parent = await GetItemAsync(context, parentItemId, cancellationToken);
        EnsureLocalItem(context, parent, allowMissingParent: string.Equals(parentItemId, context.RootItemId, StringComparison.Ordinal));
        if (!parent.IsFolder) throw new DocumentRepositoryValidationException("The requested repository item is not a folder.");
        await EnsureWithinRootAsync(context, parent, cancellationToken);

        var items = new List<DocumentRepositoryBrowseItem>(maxItems);
        var next = BuildChildrenPath(context.DriveId, parentItemId, Math.Min(maxItems, 200));
        var pages = 0;
        var truncated = false;
        while (next is not null && items.Count < maxItems)
        {
            if (++pages > _options.MaxProviderPagesPerBrowse)
            {
                truncated = true;
                break;
            }

            using var page = await SendAsync(context, next, cancellationToken);
            if (!page.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
                throw InvalidProviderResponse();
            foreach (var element in values.EnumerateArray())
            {
                var item = ParseItem(element);
                EnsureLocalItem(context, item, allowMissingParent: false);
                if (!string.Equals(item.ParentItemId, parentItemId, StringComparison.Ordinal))
                    throw BoundaryViolation();
                items.Add(new DocumentRepositoryBrowseItem(item.Id, item.Name, item.IsFolder, item.SizeBytes, item.LastModifiedUtc));
                if (items.Count == maxItems) break;
            }

            next = ReadNextLink(page.RootElement, context, parentItemId);
            truncated |= next is not null && items.Count == maxItems;
        }

        return new GraphBrowsePage(items, truncated);
    }

    private async Task EnsureWithinRootAsync(GraphRepositoryContext context, GraphItem item, CancellationToken cancellationToken)
    {
        if (string.Equals(item.Id, context.RootItemId, StringComparison.Ordinal)) return;
        var current = item;
        for (var depth = 0; depth < _options.MaxAncestryDepth; depth++)
        {
            if (string.IsNullOrWhiteSpace(current.ParentItemId)) throw BoundaryViolation();
            if (string.Equals(current.ParentItemId, context.RootItemId, StringComparison.Ordinal)) return;
            current = await GetItemAsync(context, current.ParentItemId, cancellationToken);
            EnsureLocalItem(context, current, allowMissingParent: false);
        }
        throw BoundaryViolation();
    }

    private async Task<GraphItem> GetItemAsync(GraphRepositoryContext context, string itemId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(context, $"drives/{Escape(context.DriveId)}/items/{Escape(itemId)}?$select=id,name,size,lastModifiedDateTime,parentReference,folder,package,remoteItem", cancellationToken);
        return ParseItem(document.RootElement);
    }

    private async Task<JsonDocument> SendAsync(GraphRepositoryContext context, string requestPath, CancellationToken cancellationToken)
    {
        try
        {
            return await SendCoreAsync(context, requestPath, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DocumentRepositoryUnavailableException(DocumentRepositoryValidationCodes.Unavailable, "Microsoft Graph repository access timed out.");
        }
        catch (HttpRequestException)
        {
            throw new DocumentRepositoryUnavailableException(DocumentRepositoryValidationCodes.Unavailable, "Microsoft Graph is temporarily unavailable.");
        }
    }

    private async Task<JsonDocument> SendCoreAsync(GraphRepositoryContext context, string requestPath, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        var response = await SendOnceAsync(client, context, requestPath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            tokenProvider.Invalidate(context);
            response = await SendOnceAsync(client, context, requestPath, cancellationToken);
        }
        using (response)
        {
        if (!response.IsSuccessStatusCode) throw MapFailure(response.StatusCode, ReadRetryAfter(response));
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        try { return await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken); }
        catch (JsonException) { throw InvalidProviderResponse(); }
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpClient client, GraphRepositoryContext context, string requestPath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync(context, cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private string? ReadNextLink(JsonElement root, GraphRepositoryContext context, string parentItemId)
    {
        if (!root.TryGetProperty("@odata.nextLink", out var value) || value.ValueKind != JsonValueKind.String) return null;
        if (!Uri.TryCreate(value.GetString(), UriKind.Absolute, out var next) || next.Scheme != Uri.UriSchemeHttps) throw BoundaryViolation();
        var configured = new Uri(_options.BaseUrl, UriKind.Absolute);
        if (!string.Equals(next.Scheme, configured.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(next.Host, configured.Host, StringComparison.OrdinalIgnoreCase) || next.Port != configured.Port ||
            !next.AbsolutePath.StartsWith(configured.AbsolutePath, StringComparison.Ordinal))
            throw BoundaryViolation();
        var segments = next.AbsolutePath[configured.AbsolutePath.Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (segments.Length != 5 ||
            !string.Equals(segments[0], "drives", StringComparison.Ordinal) ||
            !string.Equals(segments[1], context.DriveId, StringComparison.Ordinal) ||
            !string.Equals(segments[2], "items", StringComparison.Ordinal) ||
            !string.Equals(segments[3], parentItemId, StringComparison.Ordinal) ||
            !string.Equals(segments[4], "children", StringComparison.Ordinal))
            throw BoundaryViolation();
        return next.ToString();
    }

    private string ValidateDeltaCursor(GraphRepositoryContext context, string cursor)
    {
        if (!Uri.TryCreate(cursor, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw BoundaryViolation();
        var configured = new Uri(_options.BaseUrl, UriKind.Absolute);
        if (!string.Equals(uri.Host, configured.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != configured.Port || !uri.AbsolutePath.StartsWith(configured.AbsolutePath, StringComparison.Ordinal)) throw BoundaryViolation();
        var expected = $"drives/{Escape(context.DriveId)}/items/{Escape(context.RootItemId)}/delta";
        var relative = uri.PathAndQuery[configured.AbsolutePath.Length..];
        if (!relative.StartsWith(expected, StringComparison.Ordinal)) throw BoundaryViolation();
        return uri.ToString();
    }

    private string? ReadDeltaLink(JsonElement root, string propertyName, GraphRepositoryContext context)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String) return null;
        return ValidateDeltaCursor(context, value.GetString()!);
    }

    private static GraphRepositoryChange ParseItemForDelta(JsonElement element, string id, string name, string? parentId, bool deleted)
    {
        var folder = element.TryGetProperty("folder", out var folderFacet) && folderFacet.ValueKind == JsonValueKind.Object;
        var package = element.TryGetProperty("package", out var packageFacet) && packageFacet.ValueKind == JsonValueKind.Object;
        var remote = element.TryGetProperty("remoteItem", out var remoteItem) && remoteItem.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        if (remote) throw BoundaryViolation();
        var size = element.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize) ? parsedSize : 0;
        DateTime? modified = element.TryGetProperty("lastModifiedDateTime", out var modifiedElement) && modifiedElement.TryGetDateTime(out var parsedModified) ? parsedModified.ToUniversalTime() : null;
        var version = TryReadString(element, "eTag") ?? TryReadString(element, "cTag");
        var contentType = element.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.Object && file.TryGetProperty("mimeType", out var mime) ? mime.GetString() : null;
        return new GraphRepositoryChange(id, parentId, name, folder || package, deleted, size, version, TryReadString(element, "webUrl"), contentType, modified);
    }

    private static GraphItem ParseItem(JsonElement element)
    {
        var id = ReadRequiredString(element, "id", "Microsoft Graph returned an item without an identity.");
        var name = ReadRequiredString(element, "name", "Microsoft Graph returned an item without a name.");
        var remote = element.TryGetProperty("remoteItem", out var remoteItem) && remoteItem.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        var folder = element.TryGetProperty("folder", out var folderFacet) && folderFacet.ValueKind == JsonValueKind.Object;
        var package = element.TryGetProperty("package", out var packageFacet) && packageFacet.ValueKind == JsonValueKind.Object;
        string? driveId = null;
        string? parentId = null;
        if (element.TryGetProperty("parentReference", out var parent) && parent.ValueKind == JsonValueKind.Object)
        {
            driveId = TryReadString(parent, "driveId");
            parentId = TryReadString(parent, "id");
        }
        long? size = element.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize) ? parsedSize : null;
        DateTime? modified = element.TryGetProperty("lastModifiedDateTime", out var modifiedElement) && modifiedElement.TryGetDateTime(out var parsedModified) ? parsedModified.ToUniversalTime() : null;
        var version = TryReadString(element, "eTag") ?? TryReadString(element, "cTag");
        var webUrl = TryReadString(element, "webUrl");
        var contentType = element.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.Object && file.TryGetProperty("mimeType", out var mime) ? mime.GetString() : null;
        return new GraphItem(id, name, driveId, parentId, folder || package, remote, size, modified, version, webUrl, contentType);
    }

    private static void EnsureLocalItem(GraphRepositoryContext context, GraphItem item, bool allowMissingParent)
    {
        if (item.IsRemote || (!string.IsNullOrWhiteSpace(item.DriveId) && !string.Equals(item.DriveId, context.DriveId, StringComparison.Ordinal)) || (!allowMissingParent && string.IsNullOrWhiteSpace(item.ParentItemId)))
            throw BoundaryViolation();
    }

    private static string BuildChildrenPath(string driveId, string itemId, int top) =>
        $"drives/{Escape(driveId)}/items/{Escape(itemId)}/children?$select=id,name,size,lastModifiedDateTime,parentReference,folder,package,remoteItem,eTag,cTag,webUrl,file&$top={top}";
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string? TryReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string ReadRequiredString(JsonElement element, string name, string message) => !string.IsNullOrWhiteSpace(TryReadString(element, name)) ? TryReadString(element, name)! : throw new DocumentRepositoryUnavailableException(DocumentRepositoryValidationCodes.Unavailable, message);
    private static DocumentRepositoryUnavailableException InvalidProviderResponse() => new(DocumentRepositoryValidationCodes.Unavailable, "Microsoft Graph returned an invalid response.");
    private static DocumentRepositoryUnavailableException BoundaryViolation() => new(DocumentRepositoryValidationCodes.BoundaryViolation, "Microsoft Graph returned an item outside the approved repository boundary.");
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter?.Date is { } date) return date - DateTimeOffset.UtcNow > TimeSpan.Zero ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return null;
    }
    private static DocumentRepositoryUnavailableException MapFailure(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
    {
        var failure = statusCode switch
        {
        HttpStatusCode.Unauthorized => new(DocumentRepositoryValidationCodes.InvalidCredentials, "Microsoft Graph rejected the configured application identity."),
        HttpStatusCode.Forbidden => new(DocumentRepositoryValidationCodes.MissingAccess, "Microsoft Graph access is missing. Confirm both Entra consent and the explicit resource grant."),
        HttpStatusCode.NotFound => new(DocumentRepositoryValidationCodes.NotFound, "The configured Microsoft 365 repository resource was not found or is not granted."),
        HttpStatusCode.Gone => new("delta_token_expired", "The Microsoft Graph synchronization cursor expired and a complete reconciliation is required."),
        HttpStatusCode.TooManyRequests => new(DocumentRepositoryValidationCodes.Throttled, "Microsoft Graph temporarily throttled repository access."),
        _ when (int)statusCode >= 500 => new(DocumentRepositoryValidationCodes.Unavailable, "Microsoft Graph is temporarily unavailable."),
            _ => new DocumentRepositoryUnavailableException(DocumentRepositoryValidationCodes.Unavailable, "Microsoft Graph could not validate the configured repository.")
        };
        failure.RetryAfter = retryAfter;
        return failure;
    }

    private sealed record GraphItem(string Id, string Name, string? DriveId, string? ParentItemId, bool IsFolder, bool IsRemote, long? SizeBytes, DateTime? LastModifiedUtc, string? RemoteVersion, string? WebUrl, string? ContentType);
}
