using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientNativeReceivablesOperationsTests
{
    [Fact]
    public async Task Readiness_client_uses_the_company_scoped_operations_route()
    {
        var companyId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        var result = await client.GetNativeReceivablesReadinessAsync(companyId);

        Assert.NotNull(result);
        var request = Assert.Single(transport.Requests);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"api/companies/{companyId:D}/finance/receivables/readiness", request.Uri);
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new(companyId, method, uri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"signals\":[]}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(Guid CompanyId, HttpMethod Method, string Uri);
}
