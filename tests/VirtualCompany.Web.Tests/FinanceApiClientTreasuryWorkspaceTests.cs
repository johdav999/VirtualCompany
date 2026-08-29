using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientTreasuryWorkspaceTests
{
    [Fact]
    public async Task Client_uses_the_company_scoped_bounded_daily_treasury_route()
    {
        var companyId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        var result = await client.GetTreasuryWorkspaceAsync(companyId, 20, 17, 9);

        Assert.NotNull(result);
        var request = Assert.Single(transport.Requests);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"api/companies/{companyId:D}/finance/treasury-workspace?horizonDays=20&exceptionLimit=17&taskLimit=9",
            request.Uri);
    }

    [Fact]
    public async Task Offline_mode_returns_an_explicit_empty_read_without_transport_or_demo_data()
    {
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport, useOfflineMode: true);

        Assert.Null(await client.GetTreasuryWorkspaceAsync(Guid.NewGuid()));
        Assert.Empty(transport.Requests);
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<Request> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAsync(
            Guid companyId,
            HttpMethod method,
            string uri,
            HttpContent? content,
            CancellationToken cancellationToken)
        {
            Requests.Add(new Request(companyId, method, uri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record Request(Guid CompanyId, HttpMethod Method, string Uri);
}
