using System.Net;
using System.Text;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientBankConnectionTests
{
    [Fact]
    public async Task Bank_connection_client_uses_typed_company_scoped_routes()
    {
        var companyId = Guid.NewGuid(); var connectionId = Guid.NewGuid(); var accountId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid(); var gapId = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);
        await client.GetBankConnectionsAsync(companyId);
        await client.GetBankInstitutionsAsync(companyId, "provider/key");
        await client.StartBankConnectionAsync(companyId, new("provider", "institution", "https://app.test/finance/settings/bank-connections", ["accounts"]));
        await client.RenewBankConnectionAsync(companyId, connectionId, new("provider", 2, null));
        await client.MapBankAccountAsync(companyId, connectionId, accountId, new(Guid.NewGuid(), 2, "Explicit mapping"));
        await client.RefreshBankConnectionAsync(companyId, connectionId, 3);
        await client.SuspendBankConnectionAsync(companyId, connectionId, new(4, "Review"));
        await client.DisconnectBankConnectionAsync(companyId, connectionId, new(5, "Disconnect"));
        await client.GetBankSynchronizationAccessAsync(companyId, connectionId);
        await client.GetBankFeedHealthAsync(companyId);
        await client.RequestBankFeedSynchronizationAsync(companyId, checkpointId);
        await client.RequestBankFeedBackfillAsync(companyId, checkpointId, gapId,
            new(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5), 3, "Recover exact gap"));
        Assert.Collection(transport.Requests,
            x => AssertRequest(x, companyId, HttpMethod.Get, $"api/companies/{companyId}/finance/bank-connections"),
            x => AssertRequest(x, companyId, HttpMethod.Get, $"api/companies/{companyId}/finance/bank-connections/providers/provider%2Fkey/institutions"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/connect"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/renew"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/accounts/{accountId:D}/mapping"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/refresh"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/suspend"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/disconnect"),
            x => AssertRequest(x, companyId, HttpMethod.Get, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/synchronization-access"),
            x => AssertRequest(x, companyId, HttpMethod.Get, $"api/companies/{companyId}/finance/bank-feeds"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-feeds/synchronize"),
            x => AssertRequest(x, companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-feeds/{checkpointId:D}/gaps/{gapId:D}/backfill"));
    }
    [Fact]
    public async Task Bank_connection_mutations_are_blocked_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);
        await Assert.ThrowsAsync<FinanceApiException>(() => client.StartBankConnectionAsync(Guid.NewGuid(), new("provider", "institution", null, [])));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.RequestBankFeedSynchronizationAsync(Guid.NewGuid(), null));
    }
    private static void AssertRequest(Request request, Guid companyId, HttpMethod method, string uri)
    { Assert.Equal(companyId, request.CompanyId); Assert.Equal(method, request.Method); Assert.Equal(uri, request.Uri); }
    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/"); public List<Request> Requests { get; } = [];
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new(companyId, method, uri));
            var body = method == HttpMethod.Get && uri.EndsWith("/bank-feeds", StringComparison.Ordinal) ? "{\"healthyCount\":0,\"attentionCount\":0,\"maximumLagMinutes\":0,\"accounts\":[]}" :
                method == HttpMethod.Get && uri.Contains("/institutions", StringComparison.Ordinal) ? "[]" :
                uri.EndsWith("/connect", StringComparison.Ordinal) || uri.EndsWith("/renew", StringComparison.Ordinal) ? "{\"sessionId\":\"00000000-0000-0000-0000-000000000001\",\"authorizationUri\":\"https://provider.test/auth\",\"expiresUtc\":\"2026-08-28T12:00:00Z\"}" :
                uri.EndsWith("/mapping", StringComparison.Ordinal) ? "{\"mappingId\":\"00000000-0000-0000-0000-000000000002\",\"mappingVersion\":1,\"connectionVersion\":3}" :
                uri.EndsWith("/synchronization-access", StringComparison.Ordinal) ? "{\"allowed\":true,\"explanation\":\"Allowed\",\"renewalRequired\":false}" :
                uri.Contains("/bank-feeds/", StringComparison.Ordinal) || uri.EndsWith("/bank-feeds/synchronize", StringComparison.Ordinal) ? "{\"queuedAccountCount\":1,\"status\":\"queued\",\"explanation\":\"Queued\"}" :
                "{\"providers\":[],\"connections\":[],\"internalAccounts\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed record Request(Guid CompanyId, HttpMethod Method, string Uri);
}
