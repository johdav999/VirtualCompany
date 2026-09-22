using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Infrastructure.Documents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MicrosoftGraphDocumentRepositoryPermissionAdapterTests
{
    private static readonly Guid ApplicationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Ensure_reuses_the_exact_existing_application_permission_without_posting()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            $"{{\"value\":[{{\"id\":\"permission-1\",\"roles\":[\"read\"],\"grantedToV2\":{{\"application\":{{\"id\":\"{ApplicationId:D}\"}}}}}}]}}"));
        var result = await Create(handler).EnsureAsync("delegated", "drive-1", "root-1", ApplicationId, "read", default);
        Assert.Equal("permission-1", result.PermissionId);
        Assert.False(result.Created);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Ensure_creates_only_the_reviewed_role_on_the_exact_drive_item()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, "{\"value\":[]}"),
            Json(HttpStatusCode.Created, "{\"id\":\"permission-2\"}")]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        var result = await Create(handler).EnsureAsync("delegated", "drive-1", "output-1", ApplicationId, "write", default);
        Assert.True(result.Created);
        Assert.Equal("permission-2", result.PermissionId);
        Assert.Equal("/v1.0/drives/drive-1/items/output-1/permissions", handler.Requests[1].Uri.AbsolutePath);
        Assert.Contains("\"roles\":[\"write\"]", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains(ApplicationId.ToString("D"), handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ensure_rejects_a_broader_existing_role_instead_of_claiming_read_only()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            $"{{\"value\":[{{\"id\":\"permission-write\",\"roles\":[\"write\"],\"grantedToV2\":{{\"application\":{{\"id\":\"{ApplicationId:D}\"}}}}}}]}}"));

        var exception = await Assert.ThrowsAsync<DocumentRepositoryOnboardingException>(() =>
            Create(handler).EnsureAsync("delegated", "drive-1", "root-1", ApplicationId, "read", default));

        Assert.Equal("permission_role_conflict", exception.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Ambiguous_create_is_reconciled_by_reading_the_exact_resource_before_retry()
    {
        var call = 0;
        var handler = new RecordingHandler(_ => ++call switch
        {
            1 => Json(HttpStatusCode.OK, "{\"value\":[]}"),
            2 => throw new HttpRequestException("connection lost after send"),
            _ => Json(HttpStatusCode.OK, $"{{\"value\":[{{\"id\":\"permission-3\",\"roles\":[\"read\"],\"grantedToV2\":{{\"application\":{{\"id\":\"{ApplicationId:D}\"}}}}}}]}}")
        });
        var result = await Create(handler).EnsureAsync("delegated", "drive-1", "root-1", ApplicationId, "read", default);
        Assert.True(result.Created);
        Assert.Equal("permission-3", result.PermissionId);
        Assert.Equal(3, handler.Requests.Count);
    }

    private static MicrosoftGraphDocumentRepositoryPermissionAdapter Create(HttpMessageHandler handler) => new(
        new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") }),
        Options.Create(new MicrosoftGraphDocumentRepositoryOptions()));
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed record Captured(HttpMethod Method, Uri Uri, string Body);
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(request.Method, request.RequestUri!, body));
            return response(request);
        }
    }
}
