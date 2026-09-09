using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingChangeProposalApiClientTests
{
    [Fact]
    public async Task Bulk_approval_uses_the_tenant_transport_and_safe_server_route()
    {
        var session = Guid.NewGuid(); var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith($"/api/sales/meeting-sessions/{session:D}/change-proposals/approve-all-safe", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); Assert.All(ids, id => Assert.Contains(id.ToString(), body, StringComparison.OrdinalIgnoreCase));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new BulkApproveSalesMeetingChangeProposalsResultViewModel([], ids)), Encoding.UTF8, "application/json") };
        });
        var client = new SalesMeetingChangeProposalApiClient(new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new("https://example.test/") }), false);
        var result = await client.ApproveAllSafeAsync(Guid.NewGuid(), session, ids); Assert.Equal(ids, result!.SkippedProposalIds);
    }

    [Fact]
    public async Task Offline_review_is_explicit()
    {
        var client = new SalesMeetingChangeProposalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => throw new InvalidOperationException())) { BaseAddress = new("https://example.test/") }), true);
        await Assert.ThrowsAsync<SalesMeetingChangeProposalApiException>(() => client.ListAsync(Guid.NewGuid(), Guid.NewGuid()));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request)); }
}
