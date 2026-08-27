using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientStatutoryDocumentTests
{
    [Fact]
    public async Task Client_exposes_all_company_scoped_statutory_document_routes()
    {
        var companyId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        await client.PreviewStatutoryDocumentAsync(companyId, new());
        await client.GetStatutoryDocumentSeriesAsync(companyId);
        await client.CreateStatutoryDocumentSeriesAsync(companyId, new());
        await client.UpdateStatutoryDocumentSeriesAsync(companyId, seriesId, new());
        await client.GetStatutoryDocumentAllocationsAsync(companyId, seriesId);
        await client.RecordStatutoryDocumentGapAsync(companyId, seriesId, new());
        await client.IssueNativeStatutoryDocumentAsync(companyId, new());
        await client.RegisterImportedStatutoryDocumentAsync(companyId, new());
        await client.GetIssuedStatutoryDocumentAsync(companyId, documentId);
        await client.AttachStatutoryDocumentEvidenceAsync(companyId, documentId, new());

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-documents/preview"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/statutory-document-series"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-document-series"),
            x => AssertRequest(x, HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/statutory-document-series/{seriesId:D}"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/statutory-document-allocations?seriesId={seriesId:D}"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-document-series/{seriesId:D}/gaps"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-documents/issue-native"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-documents/register-imported"),
            x => AssertRequest(x, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/statutory-documents/{documentId:D}"),
            x => AssertRequest(x, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-documents/{documentId:D}/evidence"));
    }

    [Fact]
    public async Task Statutory_document_mutations_are_blocked_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);

        await Assert.ThrowsAsync<FinanceApiException>(() =>
            client.IssueNativeStatutoryDocumentAsync(Guid.NewGuid(), new()));
        await Assert.ThrowsAsync<FinanceApiException>(() =>
            client.RecordStatutoryDocumentGapAsync(Guid.NewGuid(), Guid.NewGuid(), new()));
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
            var body = method == HttpMethod.Get &&
                (uri.EndsWith("statutory-document-series", StringComparison.Ordinal) ||
                 uri.Contains("statutory-document-allocations", StringComparison.Ordinal)) ? "[]" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri);
}
