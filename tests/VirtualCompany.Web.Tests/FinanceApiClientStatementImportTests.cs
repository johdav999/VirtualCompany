using System.Net;
using System.Text;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientStatementImportTests
{
    [Fact]
    public async Task Statement_import_client_uses_company_transport_typed_routes_and_multipart_upload()
    {
        var company = Guid.NewGuid(); var job = Guid.NewGuid(); var row = Guid.NewGuid(); var account = Guid.NewGuid();
        var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);
        await client.GetStatementImportWorkspaceAsync(company);
        await client.GetStatementImportJobAsync(company, job);
        await client.PreviewStatementImportAsync(company, account, "statement.xml", "application/xml", 4,
            new MemoryStream([1, 2, 3, 4]), null, null);
        await client.CommitStatementImportAsync(company, job, 2);
        await client.SkipStatementImportRowAsync(company, job, row, 3, "Reviewed exclusion");
        await client.CreateStatementCsvProfileAsync(company, new("Bank CSV", ";", "sv-SE", "yyyy-MM-dd", true,
            "Date", null, "Amount", null, null, "Currency", "Reference", "Counterparty", "Id", "Account", "SEK"));
        Assert.Collection(transport.Requests,
            x => AssertRequest(x, company, HttpMethod.Get, $"api/companies/{company:D}/finance/statement-imports"),
            x => AssertRequest(x, company, HttpMethod.Get, $"api/companies/{company:D}/finance/statement-imports/{job:D}"),
            x => { AssertRequest(x, company, HttpMethod.Post, $"api/companies/{company:D}/finance/statement-imports/preview"); Assert.StartsWith("multipart/form-data", x.ContentType, StringComparison.OrdinalIgnoreCase); },
            x => AssertRequest(x, company, HttpMethod.Post, $"api/companies/{company:D}/finance/statement-imports/{job:D}/commit"),
            x => AssertRequest(x, company, HttpMethod.Post, $"api/companies/{company:D}/finance/statement-imports/{job:D}/rows/{row:D}/decision"),
            x => AssertRequest(x, company, HttpMethod.Post, $"api/companies/{company:D}/finance/statement-imports/csv-profiles"));
    }
    private static void AssertRequest(Request request, Guid company, HttpMethod method, string uri)
    { Assert.Equal(company, request.CompanyId); Assert.Equal(method, request.Method); Assert.Equal(uri, request.Uri); }
    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/"); public List<Request> Requests { get; } = [];
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new(companyId, method, uri, content?.Headers.ContentType?.ToString()));
            var body = uri.EndsWith("statement-imports", StringComparison.Ordinal) && method == HttpMethod.Get
                ? "{\"accounts\":[],\"csvProfiles\":[],\"jobs\":[]}"
                : uri.EndsWith("csv-profiles", StringComparison.Ordinal)
                    ? $"{{\"id\":\"{Guid.NewGuid():D}\",\"name\":\"Bank CSV\",\"version\":1,\"delimiter\":\";\",\"cultureName\":\"sv-SE\",\"dateFormat\":\"yyyy-MM-dd\",\"hasHeader\":true,\"bookingDateColumn\":\"Date\",\"referenceColumn\":\"Reference\",\"createdUtc\":\"2026-08-28T00:00:00Z\"}}"
                    : $"{{\"id\":\"{Guid.NewGuid():D}\",\"bankAccountId\":\"{Guid.NewGuid():D}\",\"bankAccountName\":\"Operating\",\"originalFileName\":\"statement.xml\",\"checksum\":\"{new string('a', 64)}\",\"status\":\"preview_ready\",\"version\":2,\"issues\":[],\"rows\":[]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed record Request(Guid CompanyId, HttpMethod Method, string Uri, string? ContentType);
}
