using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientCustomerCollectionsTests
{
    [Fact]
    public async Task Client_uses_typed_company_scoped_receivables_and_collection_routes()
    {
        var companyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var reminderId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        await client.GetCustomerAgingAsync(companyId, new DateOnly(2026, 8, 26), "Europe/Stockholm", customerId, "SEK", 5, 50);
        await client.GetCustomerCollectionMetricsAsync(companyId, new DateOnly(2026, 8, 26), 120, "SEK");
        await client.GetCustomerCollectionCasesAsync(companyId, customerId, invoiceId, "open", 5, 50);
        await client.GetCustomerStatementsAsync(companyId, customerId, 5, 50);
        await client.GenerateCustomerStatementAsync(companyId, new(customerId, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 26), "Europe/Stockholm", "sv-SE", "SEK", "statement-1"));
        await client.RecordCustomerDisputeAsync(companyId, invoiceId, new(250m, "Price disputed", null, null, null, "dispute-1"));
        await client.RecordCustomerPromiseAsync(companyId, invoiceId, new(500m, new DateOnly(2026, 9, 1), null, null, null, "promise-1"));
        await client.PrepareCustomerReminderAsync(companyId, invoiceId, new(null, null, "prepare-1"));
        await client.SendCustomerReminderAsync(companyId, reminderId, new(2, new string('a', 64), "send-1"));

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/receivables/aging?cutoffDate=2026-08-26&timeZoneId=Europe%2FStockholm&customerId={customerId}&currency=SEK&skip=5&take=50"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-collections/metrics?asOfDate=2026-08-26&lookbackDays=120&currency=SEK"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-collections/cases?customerId={customerId}&invoiceId={invoiceId}&status=open&skip=5&take=50"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-statements?customerId={customerId}&skip=5&take=50"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-statements"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId}/collection-disputes"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId}/promises-to-pay"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId}/reminders"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-reminders/{reminderId}/send"));
    }

    [Fact]
    public async Task Collection_mutations_are_rejected_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);
        await Assert.ThrowsAsync<FinanceApiException>(() => client.RecordCustomerDisputeAsync(
            Guid.NewGuid(), Guid.NewGuid(), new(1m, "Dispute", null, null, null, "dispute")));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.GenerateCustomerStatementAsync(
            Guid.NewGuid(), new(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 26), "UTC", "en-US", "SEK", "statement")));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.SendCustomerReminderAsync(
            Guid.NewGuid(), Guid.NewGuid(), new(1, new string('a', 64), "send")));
    }

    private static void AssertRequest(RecordedRequest request, HttpMethod method, string uri)
    {
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
            Requests.Add(new(method, uri));
            var isList = method == HttpMethod.Get &&
                         (uri.Contains("cases", StringComparison.Ordinal) || uri.Contains("statements", StringComparison.Ordinal));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(isList ? "{\"totalCount\":0,\"items\":[]}" : "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri);
}
