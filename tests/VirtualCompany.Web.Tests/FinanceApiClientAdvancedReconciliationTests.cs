using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientAdvancedReconciliationTests
{
    [Fact]
    public async Task Advanced_reconciliation_client_uses_company_scoped_typed_routes()
    {
        var companyId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        await client.ListAdvancedReconciliationAsync(companyId, "proposed", "batch/ref", .75m, 25);
        await client.GetAdvancedReconciliationAsync(companyId, groupId);
        await client.AcceptAdvancedReconciliationAsync(companyId, groupId,
            new() { ExpectedVersion = 3, ExpectedRuleVersion = 2, DecisionReason = "Reviewed" });
        await client.RejectAdvancedReconciliationAsync(companyId, groupId,
            new() { ExpectedVersion = 3, DecisionReason = "Incorrect reference" });
        await client.ReverseAdvancedReconciliationAsync(companyId, groupId,
            new() { ExpectedVersion = 4, FiscalPeriodId = Guid.NewGuid(), PostingDate = new(2026, 8, 28), Reason = "Correction" });

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/advanced-reconciliation?limit=25&status=proposed&search=batch%2Fref&maximumConfidence=0.75"),
            x => AssertRequest(x, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}/accept"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}/reject"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}/reverse"));
    }

    [Fact]
    public async Task Advanced_reconciliation_decisions_are_blocked_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);
        await Assert.ThrowsAsync<FinanceApiException>(() => client.AcceptAdvancedReconciliationAsync(Guid.NewGuid(), Guid.NewGuid(), new()));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.RejectAdvancedReconciliationAsync(Guid.NewGuid(), Guid.NewGuid(), new()));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.ReverseAdvancedReconciliationAsync(Guid.NewGuid(), Guid.NewGuid(), new()));
    }

    private static void AssertRequest(Request request, Guid companyId, HttpMethod method, string uri)
    { Assert.Equal(companyId, request.CompanyId); Assert.Equal(method, request.Method); Assert.Equal(uri, request.Uri); }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<Request> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(companyId, method, uri));
            var body = uri.Contains('?') ? "{\"groups\":[],\"metrics\":{},\"currentRule\":null}" :
                "{\"summary\":{\"id\":\"00000000-0000-0000-0000-000000000001\"},\"nodes\":[],\"edges\":[],\"reasonContributions\":[],\"results\":[],\"history\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed record Request(Guid CompanyId, HttpMethod Method, string Uri);
}
