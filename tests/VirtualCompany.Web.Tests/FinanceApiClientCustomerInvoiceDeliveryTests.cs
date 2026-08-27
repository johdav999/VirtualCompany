using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientCustomerInvoiceDeliveryTests
{
    [Fact]
    public async Task Client_uses_the_company_scoped_preferred_delivery_route()
    {
        var companyId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        await client.RequestCustomerInvoicePreferredDeliveryAsync(companyId, invoiceId,
            new(artifactId, "billing@example.test", true, "Normal invoice delivery", "delivery-1"));

        var request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/preferred-delivery",
            request.Uri);
    }

    [Fact]
    public async Task Client_uses_company_scoped_electronic_delivery_and_operator_routes()
    {
        var companyId = Guid.NewGuid(); var invoiceId = Guid.NewGuid(); var artifactId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid(); var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        await client.RequestCustomerInvoiceElectronicAsync(companyId, invoiceId,
            new(artifactId, true, "billing@example.test", "Peppol delivery", "peppol-1"));
        await client.ReconcileCustomerInvoiceElectronicAsync(companyId, deliveryId,
            new("Check current acknowledgement"));

        Assert.Collection(transport.Requests,
            request => Assert.Equal((HttpMethod.Post,
                $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/electronic-deliveries"), request),
            request => Assert.Equal((HttpMethod.Post,
                $"internal/companies/{companyId}/finance/accounting/customer-invoices/electronic-deliveries/{deliveryId:D}/reconcile"), request));
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
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
