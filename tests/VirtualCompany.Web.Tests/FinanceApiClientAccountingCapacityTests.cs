using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientAccountingCapacityTests
{
    [Fact]
    public async Task Capacity_client_uses_company_scoped_profile_and_retention_routes()
    {
        var companyId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport, useOfflineMode: false);

        await client.GetAccountingCapacityAsync(companyId, "medium");
        await client.PreviewAccountingRetentionAsync(companyId, 25);
        await client.RunAccountingRetentionCleanupAsync(companyId,
            new AccountingRetentionCleanupApiRequest("preview-token", 25, "Expired generated content was reviewed.", "corr-capacity"));

        Assert.Collection(transport.Requests,
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"api/companies/{companyId:D}/finance/accounting-capacity?profile=medium"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"api/companies/{companyId:D}/finance/accounting-capacity/retention/preview"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"api/companies/{companyId:D}/finance/accounting-capacity/retention/run"));
    }

    [Fact]
    public async Task Capacity_retention_mutations_are_blocked_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);

        await Assert.ThrowsAsync<FinanceApiException>(() =>
            client.PreviewAccountingRetentionAsync(Guid.NewGuid(), 10));
    }

    private static void AssertRequest(RecordedRequest request, Guid companyId, HttpMethod method, string uri)
    {
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(method, request.Method);
        Assert.Equal(uri, request.Uri);
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(companyId, method, uri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(Guid CompanyId, HttpMethod Method, string Uri);
}
