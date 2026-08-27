using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientVatReturnTests
{
    [Fact]
    public async Task Client_exposes_company_scoped_vat_return_lifecycle_routes()
    {
        var companyId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var returnId = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);

        await client.GetVatFilingPeriodsAsync(companyId);
        await client.CreateVatFilingPeriodAsync(companyId, "2026-08", new(2026, 8, 1), new(2026, 8, 31), null);
        await client.GetVatReturnsAsync(companyId, periodId);
        await client.CalculateVatReturnAsync(companyId, periodId, null, "calculate:1");
        await client.GetVatReturnAsync(companyId, returnId);
        await client.RequestVatReturnApprovalAsync(companyId, returnId, new string('a', 64));
        await client.FinalizeVatReturnAsync(companyId, returnId, new string('a', 64));
        await client.CreateVatReturnCorrectionAsync(companyId, returnId, "Correction reason", "evidence:1", "correction:1");

        Assert.Equal($"internal/companies/{companyId}/finance/accounting/vat/returns/{returnId:D}/package",
            FinanceApiClient.GetVatReturnPackageDownloadUrl(companyId, returnId));

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/vat/filing-periods"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/vat/filing-periods"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/vat/returns?filingPeriodId={periodId:D}"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/vat/returns/calculate"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/vat/returns/{returnId:D}"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/vat/returns/{returnId:D}/approval"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/vat/returns/{returnId:D}/finalize"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/vat/returns/{returnId:D}/corrections"));
    }

    private static void AssertRequest(RecordedRequest request, HttpMethod method, string uri)
    { Assert.Equal(method, request.Method); Assert.Equal(uri, request.Uri); }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new(method, uri));
            var body = method == HttpMethod.Get && (uri.EndsWith("filing-periods", StringComparison.Ordinal) ||
                uri.Contains("vat/returns?", StringComparison.Ordinal)) ? "[]" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed record RecordedRequest(HttpMethod Method, string Uri);
}
