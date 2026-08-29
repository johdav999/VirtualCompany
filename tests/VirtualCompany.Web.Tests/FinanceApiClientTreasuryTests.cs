using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientTreasuryTests
{
    [Fact]
    public async Task Treasury_client_uses_company_scoped_typed_routes_for_the_full_lifecycle()
    {
        var companyId = Guid.NewGuid(); var sourceId = Guid.NewGuid(); var transactionId = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);

        await client.ListTreasurySourcesAsync(companyId, "in_transit", transactionId, 25);
        await client.GetTreasurySourceAsync(companyId, "account transfer", sourceId);
        await client.CreateTreasuryTransferAsync(companyId, new());
        await client.CreateBankAdjustmentAsync(companyId, new());
        await client.CreateCardSettlementAsync(companyId, new());
        await client.CreatePayoutSettlementAsync(companyId, new());
        await client.LinkTreasuryBankEvidenceAsync(companyId, "account_transfer", sourceId, new());
        await client.BindTreasuryApprovalAsync(companyId, "account_transfer", sourceId, new());
        await client.PreviewTreasuryPostingAsync(companyId, "account_transfer", sourceId, new());
        await client.PostTreasurySourceAsync(companyId, "account_transfer", sourceId, new());
        await client.ReverseTreasurySourceAsync(companyId, "account_transfer", sourceId, new());

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/treasury-sources?limit=25&status=in_transit&bankTransactionId={transactionId:D}"),
            x => AssertRequest(x, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/treasury-sources/account%20transfer/{sourceId:D}"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/transfers"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/bank-adjustments"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/card-settlements"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/payout-settlements"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/account_transfer/{sourceId:D}/bank-evidence"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/account_transfer/{sourceId:D}/approval"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/account_transfer/{sourceId:D}/preview"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/account_transfer/{sourceId:D}/post"),
            x => AssertRequest(x, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/treasury-sources/account_transfer/{sourceId:D}/reverse"));
    }

    [Fact]
    public async Task Treasury_mutations_are_blocked_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);
        var companyId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        await Assert.ThrowsAsync<FinanceApiException>(() => client.CreateTreasuryTransferAsync(companyId, new()));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.LinkTreasuryBankEvidenceAsync(companyId, "account_transfer", sourceId, new()));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.PostTreasurySourceAsync(companyId, "account_transfer", sourceId, new()));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.ReverseTreasurySourceAsync(companyId, "account_transfer", sourceId, new()));
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
            var body = uri.Contains("?limit=", StringComparison.Ordinal)
                ? "{\"items\":[],\"attentionCount\":0,\"inTransitCount\":0,\"readyCount\":0,\"postedCount\":0}"
                : uri.EndsWith("/preview", StringComparison.Ordinal)
                    ? "{\"canPost\":true,\"lines\":[]}"
                    : "{\"summary\":{\"id\":\"00000000-0000-0000-0000-000000000001\"},\"bankEvidence\":[],\"evidence\":[],\"journals\":[],\"history\":[],\"allowedActions\":{}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed record Request(Guid CompanyId, HttpMethod Method, string Uri);
}
