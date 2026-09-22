using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Documents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MicrosoftGraphDocumentRepositorySetupAdapterTests
{
    [Fact]
    public async Task OneDrive_discovery_accepts_only_an_organizational_drive()
    {
        var adapter = Create(new Handler(Json(HttpStatusCode.OK, """{"id":"drive-1","name":"Johan's OneDrive","driveType":"business","owner":{"user":{"displayName":"Johan"}},"root":{"id":"root-1","name":"root","folder":{}}}""")));
        var source = await adapter.GetOneDriveForBusinessAsync("token", default);
        Assert.NotNull(source);
        Assert.Equal(DocumentRepositoryProviderKinds.OneDriveForBusiness, source.ProviderKind);
        Assert.Equal("Johan", source.Context);

        adapter = Create(new Handler(Json(HttpStatusCode.OK, """{"id":"drive-2","name":"Personal","driveType":"personal","root":{"id":"root-2","name":"root","folder":{}}}""")));
        Assert.Null(await adapter.GetOneDriveForBusinessAsync("token", default));
    }

    [Fact]
    public async Task SharePoint_search_returns_minimal_sites_and_validated_paging()
    {
        var next = "https://graph.microsoft.com/v1.0/sites?search=finance&$skiptoken=next";
        var handler = new Handler(Json(HttpStatusCode.OK, $$"""{"value":[{"id":"site-1","displayName":"Finance & Legal","webUrl":"https://tenant.sharepoint.com/sites/finance"}],"@odata.nextLink":"{{next}}"}"""));
        var page = await Create(handler).SearchSharePointSitesAsync("token", "finance", null, 25, default);
        Assert.Single(page.Items);
        Assert.Equal("Finance & Legal", page.Items[0].DisplayName);
        Assert.Equal(next, page.NextCursor);
        Assert.DoesNotContain("sharepoint.com", page.Items[0].DisplayName);
    }

    [Fact]
    public async Task Folder_browse_filters_shortcuts_and_builds_breadcrumbs()
    {
        var handler = new Handler(
            Json(HttpStatusCode.OK, """{"id":"root-1","name":"Documents","folder":{}}"""),
            Json(HttpStatusCode.OK, """{"value":[{"id":"folder-1","name":"Approved","folder":{},"parentReference":{"driveId":"drive-1","id":"root-1"}},{"id":"shortcut","name":"Remote","folder":{},"remoteItem":{"id":"remote"},"parentReference":{"driveId":"drive-1","id":"root-1"}}]}"""));
        var source = new GraphSetupSource(DocumentRepositoryProviderKinds.SharePointLibrary, "site-1", "drive-1", "root-1", "Documents", null, null);
        var page = await Create(handler).BrowseFoldersAsync("token", source, "root-1", null, 25, default);
        Assert.Single(page.Items);
        Assert.Equal("Approved", page.Items[0].DisplayName);
        Assert.Single(page.Breadcrumbs);
    }

    [Fact]
    public async Task Folder_browse_rejects_cross_drive_children()
    {
        var handler = new Handler(
            Json(HttpStatusCode.OK, """{"id":"root-1","name":"Documents","folder":{}}"""),
            Json(HttpStatusCode.OK, """{"value":[{"id":"folder-1","name":"Escape","folder":{},"parentReference":{"driveId":"other-drive","id":"root-1"}}]}"""));
        var source = new GraphSetupSource(DocumentRepositoryProviderKinds.SharePointLibrary, "site-1", "drive-1", "root-1", "Documents", null, null);
        var error = await Assert.ThrowsAsync<DocumentRepositoryOnboardingException>(() => Create(handler).BrowseFoldersAsync("token", source, "root-1", null, 25, default));
        Assert.Equal(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, error.Code);
    }

    [Fact]
    public async Task Paging_rejects_untrusted_hosts_and_throttling_is_actionable()
    {
        var source = new GraphSetupSource(DocumentRepositoryProviderKinds.SharePointLibrary, "site-1", "drive-1", "root-1", "Documents", null, null);
        var adapter = Create(new Handler(Json(HttpStatusCode.OK, """{"id":"root-1","name":"Documents","folder":{}}""")));
        var invalid = await Assert.ThrowsAsync<DocumentRepositoryOnboardingException>(() => adapter.BrowseFoldersAsync("token", source, "root-1", "https://attacker.example/steal", 25, default));
        Assert.Equal(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, invalid.Code);

        adapter = Create(new Handler(Json(HttpStatusCode.TooManyRequests, "{}")));
        var throttled = await Assert.ThrowsAsync<DocumentRepositoryOnboardingException>(() => adapter.SearchSharePointSitesAsync("token", "finance", null, 25, default));
        Assert.Equal(DocumentRepositoryOnboardingFailureCodes.ProviderThrottled, throttled.Code);
        Assert.True(throttled.Retryable);
    }

    private static MicrosoftGraphDocumentRepositorySetupAdapter Create(HttpMessageHandler handler) => new(
        new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") }),
        Options.Create(new MicrosoftGraphDocumentRepositoryOptions()));
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (queue.Count == 0) throw new InvalidOperationException("No response configured.");
            return Task.FromResult(queue.Dequeue());
        }
    }
}