using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientCustomerInvoiceScheduleTests
{
    [Fact]
    public async Task Client_uses_typed_company_scoped_recurring_invoice_schedule_routes()
    {
        var companyId = Guid.NewGuid(); var scheduleId = Guid.NewGuid(); var customerId = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);
        var action = new CustomerInvoiceScheduleActionApiRequest(2, "resume-1", true, true);

        await client.GetCustomerInvoiceSchedulesAsync(companyId, "active", customerId, 0, 25);
        await client.GetCustomerInvoiceScheduleAsync(companyId, scheduleId);
        await client.PreviewCustomerInvoiceScheduleAsync(companyId, scheduleId, 3);
        await client.SubmitCustomerInvoiceScheduleAsync(companyId, scheduleId,
            new(2, "submit-1"));
        await client.ChangeCustomerInvoiceScheduleStatusAsync(companyId, scheduleId, "resume", action);

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules?skip=0&take=25&status=active&customerId={customerId}"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId}"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId}/preview?count=3"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId}/submit"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId}/resume"));
    }

    [Fact]
    public async Task Schedule_mutations_are_rejected_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);

        await Assert.ThrowsAsync<FinanceApiException>(() => client.ChangeCustomerInvoiceScheduleStatusAsync(
            Guid.NewGuid(), Guid.NewGuid(), "pause", new(1, "pause-1")));
    }

    private static void AssertRequest(RecordedRequest request, HttpMethod method, string uri)
    { Assert.Equal(method, request.Method); Assert.Equal(uri, request.Uri); }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/"); public List<RecordedRequest> Requests { get; } = [];
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new(method, uri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        }
    }
    private sealed record RecordedRequest(HttpMethod Method, string Uri);
}
