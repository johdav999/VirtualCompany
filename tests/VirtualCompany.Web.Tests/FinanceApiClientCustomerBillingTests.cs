using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientCustomerBillingTests
{
    [Fact]
    public async Task Client_uses_typed_company_scoped_customer_billing_routes()
    {
        var companyId = Guid.NewGuid(); var customerId = Guid.NewGuid(); var conflictId = Guid.NewGuid(); var candidateId = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);

        await client.GetCustomerBillingProfileAsync(companyId, customerId);
        await client.GetCustomerBillingProfileHistoryAsync(companyId, customerId, 25);
        await client.UpsertCustomerBillingProfileAsync(companyId, customerId, new(null!, null));
        await client.ResolveCustomerBillingSourceConflictAsync(companyId, conflictId, new(1, 2, false, "Keep current"));
        await client.GetCustomerDuplicateCandidatesAsync(companyId, "pending", 50);
        await client.DecideCustomerDuplicateAsync(companyId, candidateId, new(1, "keep_separate", null, null, "Separate"));

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/customers/{customerId}/billing-profile"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/customers/{customerId}/billing-profile/history?limit=25"),
            x => AssertRequest(x, HttpMethod.Put, $"internal/companies/{companyId}/finance/customers/{customerId}/billing-profile"),
            x => AssertRequest(x, HttpMethod.Put, $"internal/companies/{companyId}/finance/customer-billing/source-conflicts/{conflictId}"),
            x => Assert.Contains($"internal/companies/{companyId}/finance/customer-duplicates", x.Uri, StringComparison.Ordinal),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/customer-duplicates/{candidateId}/decision"));
    }

    [Fact]
    public async Task Customer_billing_mutations_are_rejected_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);
        await Assert.ThrowsAsync<FinanceApiException>(() => client.UpsertCustomerBillingProfileAsync(Guid.NewGuid(), Guid.NewGuid(), new(null!, null)));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.DecideCustomerDuplicateAsync(Guid.NewGuid(), Guid.NewGuid(), new(1, "keep_separate", null, null, "Separate")));
    }

    private static void AssertRequest(RecordedRequest request, HttpMethod method, string uri)
    { Assert.Equal(method, request.Method); Assert.Equal(uri, request.Uri); }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/"); public List<RecordedRequest> Requests { get; } = [];
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new(method, uri)); var list = uri.Contains("history", StringComparison.Ordinal) || uri.Contains("customer-duplicates", StringComparison.Ordinal) && method == HttpMethod.Get;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(list ? "[]" : "{}", Encoding.UTF8, "application/json") });
        }
    }
    private sealed record RecordedRequest(HttpMethod Method, string Uri);
}
