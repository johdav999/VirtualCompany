using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientCustomerInvoiceDraftTests
{
    [Fact]
    public async Task Client_uses_typed_company_scoped_native_invoice_draft_routes()
    {
        var companyId = Guid.NewGuid(); var draftId = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);
        var save = new SaveCustomerInvoiceDraftApiRequest(1, "save-1", Guid.NewGuid(), "customer_invoice",
            new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 24), "SEK",
            "fixed_days", 30, null, null, null, "email", "user", null, [], []);

        await client.GetCustomerInvoiceDraftsAsync(companyId, "draft", null, 0, 25);
        await client.GetCustomerInvoiceDraftAsync(companyId, draftId);
        await client.CreateCustomerInvoiceDraftAsync(companyId, save);
        await client.UpdateCustomerInvoiceDraftAsync(companyId, draftId, save);
        await client.CopyCustomerInvoiceDraftAsync(companyId, draftId, new(1, "copy-1", new DateOnly(2026, 9, 1)));
        await client.PreviewCustomerInvoiceDraftAsync(companyId, draftId, 1);
        await client.GetCustomerInvoiceDraftReadinessAsync(companyId, draftId, 1);
        await client.SubmitCustomerInvoiceDraftAsync(companyId, draftId, new(1, "submit-1"));
        await client.IssueCustomerInvoiceDraftAsync(companyId, draftId, new(1, "issue-1", new string('a', 64),
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 25), "G"));
        await client.DiscardCustomerInvoiceDraftAsync(companyId, draftId, new(1, "discard-1"));

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts?skip=0&take=25&status=draft"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts"),
            x => AssertRequest(x, HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}/copy"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}/preview"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}/readiness?expectedVersion=1"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}/submit"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}/issue"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}/discard"));
    }

    [Fact]
    public async Task Draft_mutations_are_rejected_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);
        await Assert.ThrowsAsync<FinanceApiException>(() => client.DiscardCustomerInvoiceDraftAsync(
            Guid.NewGuid(), Guid.NewGuid(), new(1, "discard")));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.SubmitCustomerInvoiceDraftAsync(
            Guid.NewGuid(), Guid.NewGuid(), new(1, "submit")));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.IssueCustomerInvoiceDraftAsync(Guid.NewGuid(), Guid.NewGuid(),
            new(1, "issue", new string('a', 64), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 25), "G")));
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri);
}
