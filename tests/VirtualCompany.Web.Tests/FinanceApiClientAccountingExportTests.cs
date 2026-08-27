using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientAccountingExportTests
{
    [Fact]
    public async Task Client_keeps_generic_default_and_sends_statutory_export_metadata()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        await client.RequestAccountingExportAsync(companyId, periodId, "generic:1");
        await client.RequestAccountingExportAsync(companyId, periodId, "archive:1", "swedish_statutory_archive", "corr-1");
        await client.GetAccountingExportsAsync(companyId, periodId);

        var exportId = Guid.NewGuid();
        Assert.Equal($"internal/companies/{companyId}/finance/accounting/exports/{exportId:D}/download",
            FinanceApiClient.GetAccountingExportDownloadUrl(companyId, exportId));

        Assert.Collection(transport.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                using var json = JsonDocument.Parse(request.Body!);
                Assert.Equal("generic_json", json.RootElement.GetProperty("exportType").GetString());
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                using var json = JsonDocument.Parse(request.Body!);
                Assert.Equal("swedish_statutory_archive", json.RootElement.GetProperty("exportType").GetString());
                Assert.Equal("corr-1", json.RootElement.GetProperty("correlationId").GetString());
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal($"internal/companies/{companyId}/finance/accounting/exports?fiscalPeriodId={periodId:D}", request.Uri);
            });
    }

    [Fact]
    public async Task Export_request_is_blocked_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);

        await Assert.ThrowsAsync<FinanceApiException>(() => client.RequestAccountingExportAsync(
            Guid.NewGuid(), Guid.NewGuid(), "archive:offline", "swedish_statutory_archive"));
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];

        public async Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(method, uri,
                content is null ? null : await content.ReadAsStringAsync(cancellationToken)));
            var body = method == HttpMethod.Get ? "[]" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Body);
}
