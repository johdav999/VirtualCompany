using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Documents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MicrosoftGraphDocumentRepositoryAdapterTests
{
    private static readonly GraphRepositoryContext Context = new(
        DocumentRepositoryProviderKinds.SharePointLibrary,
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "m365-test-secret",
        "drive-1",
        "root-1");

    [Fact]
    public async Task Browse_follows_only_valid_graph_paging_links_and_returns_a_bounded_page()
    {
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, """{"id":"root-1","name":"Root","folder":{}}"""),
            Json(HttpStatusCode.OK, """{"value":[{"id":"child-1","name":"First","size":12,"parentReference":{"driveId":"drive-1","id":"root-1"}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/drives/drive-1/items/root-1/children?$skiptoken=safe"}"""),
            Json(HttpStatusCode.OK, """{"value":[{"id":"child-2","name":"Second","folder":{},"parentReference":{"driveId":"drive-1","id":"root-1"}}]}"""));
        var adapter = CreateAdapter(handler, out _);

        var result = await adapter.BrowseAsync(Context, "root-1", 10, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.False(result.IsTruncated);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("graph.microsoft.com", request.Host));
    }

    [Fact]
    public async Task Browse_rejects_remote_shortcuts_and_cross_drive_items()
    {
        var handler = new SequenceHandler(Json(HttpStatusCode.OK,
            """{"id":"outside","name":"Shortcut","folder":{},"remoteItem":{"id":"remote"},"parentReference":{"driveId":"another-drive","id":"root-1"}}"""));
        var adapter = CreateAdapter(handler, out _);

        var exception = await Assert.ThrowsAsync<DocumentRepositoryUnavailableException>(() =>
            adapter.BrowseAsync(Context, "outside", 10, CancellationToken.None));

        Assert.Equal(DocumentRepositoryValidationCodes.BoundaryViolation, exception.Code);
    }

    [Fact]
    public async Task Paging_link_on_an_unapproved_host_is_rejected()
    {
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, """{"id":"root-1","name":"Root","folder":{}}"""),
            Json(HttpStatusCode.OK, """{"value":[],"@odata.nextLink":"https://attacker.example/v1.0/drives/drive-1/items/root-1/children"}"""));
        var adapter = CreateAdapter(handler, out _);

        var exception = await Assert.ThrowsAsync<DocumentRepositoryUnavailableException>(() =>
            adapter.BrowseAsync(Context, "root-1", 10, CancellationToken.None));

        Assert.Equal(DocumentRepositoryValidationCodes.BoundaryViolation, exception.Code);
    }

    [Fact]
    public async Task Expired_token_401_is_invalidated_and_retried_once()
    {
        var handler = new SequenceHandler(
            Json(HttpStatusCode.Unauthorized, "{}"),
            Json(HttpStatusCode.OK, """{"id":"root-1","name":"Approved root","folder":{}}"""),
            Json(HttpStatusCode.OK, """{"id":"drive-1","name":"Company library","driveType":"documentLibrary"}"""));
        var adapter = CreateAdapter(handler, out var tokens);

        var result = await adapter.ValidateAsync(Context, CancellationToken.None);

        Assert.Equal("Company library", result.RepositoryName);
        Assert.Equal(1, tokens.InvalidationCount);
        Assert.Equal(3, tokens.RequestCount);
    }

    [Fact]
    public async Task OneDrive_folder_validation_does_not_require_ungranted_drive_discovery()
    {
        var oneDriveContext = Context with { ProviderKind = DocumentRepositoryProviderKinds.OneDriveForBusiness };
        var handler = new SequenceHandler(Json(HttpStatusCode.OK, """{"id":"root-1","name":"Published folder","folder":{}}"""));
        var adapter = CreateAdapter(handler, out _);

        var result = await adapter.ValidateAsync(oneDriveContext, CancellationToken.None);

        Assert.Equal("Published folder", result.RepositoryName);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Create_uses_create_only_precondition_and_never_overwrites_a_name_collision()
    {
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, """{"id":"output-1","name":"Approved output","folder":{},"parentReference":{"driveId":"drive-1","id":"root-1"}}"""),
            Json(HttpStatusCode.PreconditionFailed, "{}"));
        var adapter = CreateAdapter(handler, out _);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("approved content"));

        var exception = await Assert.ThrowsAsync<DocumentRepositoryConflictException>(() =>
            adapter.CreateFileAsync(Context, "output-1", "Board brief.txt", content, content.Length, "text/plain", default));

        Assert.Contains("never overwritten", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Put, handler.CapturedRequests.Last().Method);
        Assert.Equal("*", handler.CapturedRequests.Last().IfNoneMatch);
        Assert.Contains("Board%20brief.txt", handler.CapturedRequests.Last().Uri.AbsoluteUri, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Initial_import_enumeration_returns_versioned_file_metadata()
    {
        var handler = new SequenceHandler(Json(HttpStatusCode.OK, """{"value":[{"id":"file-1","name":"Policy.pdf","size":42,"eTag":"v1","webUrl":"https://tenant.sharepoint.com/policy.pdf","file":{"mimeType":"application/pdf"},"parentReference":{"driveId":"drive-1","id":"root-1"}}]}"""));
        var adapter = CreateAdapter(handler, out _);

        var files = await adapter.EnumerateFilesAsync(Context, CancellationToken.None);

        var file = Assert.Single(files);
        Assert.Equal("v1", file.RemoteVersion);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("https://tenant.sharepoint.com/policy.pdf", file.WebUrl);
    }

    [Fact]
    public async Task Delta_page_returns_lasting_opaque_cursors_and_deleted_items()
    {
        var handler = new SequenceHandler(Json(HttpStatusCode.OK,
            """{"value":[{"id":"file-1","name":"Policy.pdf","eTag":"v2","parentReference":{"driveId":"drive-1","id":"root-1"},"file":{"mimeType":"application/pdf"}},{"id":"file-2","deleted":{}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/drives/drive-1/items/root-1/delta?$skiptoken=opaque"}"""));
        var adapter = CreateAdapter(handler, out _);

        var page = await adapter.ReadDeltaPageAsync(Context, null, default);

        Assert.Equal(2, page.Changes.Count);
        Assert.True(page.Changes.Single(x => x.ItemId == "file-2").IsDeleted);
        Assert.Contains("$skiptoken=opaque", page.NextCursor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delta_cursor_on_unapproved_host_is_rejected_before_any_request()
    {
        var handler = new SequenceHandler();
        var adapter = CreateAdapter(handler, out _);

        var exception = await Assert.ThrowsAsync<DocumentRepositoryUnavailableException>(() => adapter.ReadDeltaPageAsync(Context, "https://attacker.example/v1.0/drives/drive-1/items/root-1/delta?token=x", default));

        Assert.Equal(DocumentRepositoryValidationCodes.BoundaryViolation, exception.Code);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, DocumentRepositoryValidationCodes.MissingAccess)]
    [InlineData(HttpStatusCode.NotFound, DocumentRepositoryValidationCodes.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, DocumentRepositoryValidationCodes.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DocumentRepositoryValidationCodes.Unavailable)]
    public async Task Validation_translates_provider_failures_to_safe_stable_codes(HttpStatusCode status, string expectedCode)
    {
        var handler = new SequenceHandler(Json(status, """{"error":{"message":"sensitive provider detail"}}"""));
        var adapter = CreateAdapter(handler, out _);

        var exception = await Assert.ThrowsAsync<DocumentRepositoryUnavailableException>(() =>
            adapter.ValidateAsync(Context, CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain("sensitive", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static MicrosoftGraphDocumentRepositoryAdapter CreateAdapter(SequenceHandler handler, out FakeTokenProvider tokens)
    {
        tokens = new FakeTokenProvider();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
        return new MicrosoftGraphDocumentRepositoryAdapter(
            new SingleClientFactory(client),
            tokens,
            Options.Create(new MicrosoftGraphDocumentRepositoryOptions()));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeTokenProvider : IMicrosoftGraphApplicationTokenProvider
    {
        public int RequestCount { get; private set; }
        public int InvalidationCount { get; private set; }
        public Task<string> GetAccessTokenAsync(GraphRepositoryContext context, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult($"token-{RequestCount}");
        }
        public void Invalidate(GraphRepositoryContext context) => InvalidationCount++;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Uri> Requests { get; } = [];
        public List<(Uri Uri, HttpMethod Method, string? IfNoneMatch, string? IfMatch)> CapturedRequests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            CapturedRequests.Add((request.RequestUri!, request.Method,
                request.Headers.TryGetValues("If-None-Match", out var values) ? values.SingleOrDefault() : null,
                request.Headers.TryGetValues("If-Match", out var matchValues) ? matchValues.SingleOrDefault() : null));
            if (_responses.Count == 0) throw new InvalidOperationException("No response was configured for this request.");
            return Task.FromResult(_responses.Dequeue());
        }
    }
    [Fact]
    public async Task Update_uses_provider_enforced_if_match_and_returns_the_new_version()
    {
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, """{"id":"file-1","name":"Board brief.md","size":16,"eTag":"etag-12","file":{"mimeType":"text/markdown"},"parentReference":{"driveId":"drive-1","id":"root-1"}}"""),
            Json(HttpStatusCode.OK, """{"id":"file-1","name":"Board brief.md","size":18,"eTag":"etag-13","webUrl":"https://tenant.sharepoint.com/brief","file":{"mimeType":"text/markdown"},"parentReference":{"driveId":"drive-1","id":"root-1"}}"""));
        var adapter = CreateAdapter(handler, out _);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("reviewed replacement"));

        var result = await adapter.UpdateFileAsync(Context, "file-1", "etag-12", content, content.Length, "text/markdown", default);

        Assert.Equal("etag-13", result.RemoteVersion);
        Assert.Equal("etag-12", handler.CapturedRequests.Last().IfMatch);
        Assert.Equal("/v1.0/drives/drive-1/items/file-1/content", handler.CapturedRequests.Last().Uri.AbsolutePath);
    }

    [Fact]
    public async Task Update_412_reports_the_current_version_without_retrying_an_overwrite()
    {
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, """{"id":"file-1","name":"Board brief.md","size":16,"eTag":"etag-12","file":{"mimeType":"text/markdown"},"parentReference":{"driveId":"drive-1","id":"root-1"}}"""),
            Json(HttpStatusCode.PreconditionFailed, "{}"),
            Json(HttpStatusCode.OK, """{"id":"file-1","name":"Board brief.md","size":20,"eTag":"etag-13","file":{"mimeType":"text/markdown"},"parentReference":{"driveId":"drive-1","id":"root-1"}}"""));
        var adapter = CreateAdapter(handler, out _);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("stale replacement"));

        var exception = await Assert.ThrowsAsync<DocumentRepositoryConflictException>(() =>
            adapter.UpdateFileAsync(Context, "file-1", "etag-12", content, content.Length, "text/markdown", default));

        Assert.Equal("etag-13", exception.CurrentRemoteVersion);
        Assert.Single(handler.CapturedRequests.Where(x => x.Method == HttpMethod.Put));
        Assert.Equal("etag-12", handler.CapturedRequests.Single(x => x.Method == HttpMethod.Put).IfMatch);
    }

    [Fact]
    public async Task Update_reconciliation_proves_an_ambiguous_success_by_hash_without_another_write()
    {
        var bytes = Encoding.UTF8.GetBytes("reviewed replacement");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, $"{{\"id\":\"file-1\",\"name\":\"Board brief.md\",\"size\":{bytes.Length},\"eTag\":\"etag-13\",\"webUrl\":\"https://tenant.sharepoint.com/brief\",\"file\":{{\"mimeType\":\"text/markdown\"}},\"parentReference\":{{\"driveId\":\"drive-1\",\"id\":\"root-1\"}}}}"),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var adapter = CreateAdapter(handler, out _);

        var result = await adapter.ReconcileUpdatedFileAsync(Context, "file-1", hash, bytes.Length, default);

        Assert.True(result.Found);
        Assert.True(result.ContentMatches);
        Assert.Equal("etag-13", result.File!.RemoteVersion);
        Assert.DoesNotContain(handler.CapturedRequests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Update_reconciliation_after_a_later_human_edit_never_writes()
    {
        var reviewed = Encoding.UTF8.GetBytes("reviewed replacement");
        var laterHumanEdit = Encoding.UTF8.GetBytes("human edit is newer!");
        Assert.Equal(reviewed.Length, laterHumanEdit.Length);
        var reviewedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(reviewed)).ToLowerInvariant();
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, $"{{\"id\":\"file-1\",\"name\":\"Board brief.md\",\"size\":{laterHumanEdit.Length},\"eTag\":\"etag-14\",\"webUrl\":\"https://tenant.sharepoint.com/brief\",\"file\":{{\"mimeType\":\"text/markdown\"}},\"parentReference\":{{\"driveId\":\"drive-1\",\"id\":\"root-1\"}}}}"),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(laterHumanEdit) });
        var adapter = CreateAdapter(handler, out _);

        var result = await adapter.ReconcileUpdatedFileAsync(Context, "file-1", reviewedHash, reviewed.Length, default);

        Assert.True(result.Found);
        Assert.False(result.ContentMatches);
        Assert.Equal("etag-14", result.File!.RemoteVersion);
        Assert.DoesNotContain(handler.CapturedRequests, request => request.Method == HttpMethod.Put);
    }
}
