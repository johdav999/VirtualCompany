using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientCustomerInvoiceCorrectionTests
{
    [Fact]
    public async Task Client_uses_typed_company_scoped_correction_and_reconciliation_routes()
    {
        var companyId = Guid.NewGuid(); var invoiceId = Guid.NewGuid(); var correctionId = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);
        await client.EvaluateCustomerInvoiceCorrectionAsync(companyId, invoiceId, "refund", 10m, "SEK");
        await client.ProposeCustomerInvoiceCorrectionAsync(companyId, invoiceId,
            new("refund", 10m, "SEK", "Duplicate payment", "document:1", "proposal-1",
                "SE00", "bank-statement", "bank"));
        await client.GetCustomerInvoiceCorrectionsAsync(companyId, invoiceId);
        await client.GetCustomerInvoiceCorrectionAsync(companyId, correctionId);
        await client.ExecuteCustomerInvoiceCorrectionAsync(companyId, correctionId,
            new(2, new string('a', 64), "execute-1"));
        await client.ReconcileCustomerInvoiceRefundAsync(companyId, correctionId,
            new(3, true, false, "provider:accepted", "REF-1"));

        Assert.Equal(6, transport.Requests.Count);
        Assert.All(transport.Requests, x => Assert.Contains($"internal/companies/{companyId}/finance/accounting/", x.Uri));
        Assert.EndsWith($"customer-invoice-corrections/{correctionId}/refund-reconciliation", transport.Requests[^1].Uri);
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<(HttpMethod Method, string Uri)> Requests { get; } = [];
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add((method, uri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        }
    }
}
