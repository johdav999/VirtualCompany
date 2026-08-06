using System.Net;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientSourceFilterTests
{
    [Fact]
    public async Task Normal_finance_lists_exclude_simulation_data()
    {
        var companyId = Guid.NewGuid();
        var handler = new QueryRecordingHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FinanceApiClient(httpClient);

        await client.GetBillsAsync(companyId);
        await client.GetInvoicesAsync(companyId);
        await client.GetTransactionsAsync(companyId);
        await client.GetPaymentsAsync(companyId);

        Assert.Equal(4, handler.Queries.Count);
        Assert.All(handler.Queries, query => Assert.Contains("source=operational", query, StringComparison.Ordinal));
    }

    private sealed class QueryRecordingHandler : HttpMessageHandler
    {
        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Queries.Add(request.RequestUri?.Query ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
