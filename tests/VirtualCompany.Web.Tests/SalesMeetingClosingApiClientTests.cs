using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingClosingApiClientTests
{
    [Fact]
    public async Task Prepare_uses_company_transport_and_deserializes_separate_artifacts()
    {
        var companyId = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var minutesId = Guid.NewGuid(); var internalId = Guid.NewGuid();
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith($"/api/sales/meeting-sessions/{sessionId:D}/closing/prepare", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            var minutes = new SalesMeetingMinutesViewModel(minutesId, sessionId, null, 1, "draft", 3, DateTime.UtcNow, Guid.NewGuid(), null, "deterministic-v1", "v1", DateTime.UtcNow.AddDays(30), null, null, DateTime.UtcNow, 1, false, null, null, [new(Guid.NewGuid(), 0, "action", "Send plan", "Alex", null, "action-item:1", null, false)]);
            var internalValue = new SalesMeetingInternalIntelligenceViewModel(internalId, sessionId, minutesId, 1, "draft", 3, DateTime.UtcNow, Guid.NewGuid(), null, "deterministic-v1", "v1", DateTime.UtcNow.AddDays(30), null, null, DateTime.UtcNow, 1, false, null, null, [new(Guid.NewGuid(), 0, "objection", "Private", .8m, "observation:1", null, false)]);
            return Json(new SalesMeetingClosingSnapshotViewModel(minutes, internalValue));
        });
        var http = new HttpClient(handler) { BaseAddress = new("https://example.test/") }; var client = new SalesMeetingClosingApiClient(new CompanyApiTransport(http), false);
        var result = await client.PrepareAsync(companyId, sessionId, new(Guid.NewGuid(), Guid.NewGuid(), 2, 3, Guid.NewGuid()));
        Assert.Equal(minutesId, result!.CustomerMinutes.Id); Assert.Equal(internalId, result.InternalIntelligence.Id); Assert.DoesNotContain("Confidence", JsonSerializer.Serialize(result.CustomerMinutes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_preview_has_a_dedicated_route_and_offline_failure_is_explicit()
    {
        var sessionId = Guid.NewGuid(); var minutesId = Guid.NewGuid(); var called = false;
        var handler = new Handler(request => { called = true; Assert.Contains($"/customer-preview/{minutesId:D}", request.RequestUri!.AbsolutePath, StringComparison.Ordinal); return new(HttpStatusCode.NotFound); });
        var online = new SalesMeetingClosingApiClient(new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new("https://example.test/") }), false);
        Assert.Null(await online.GetCustomerPreviewAsync(Guid.NewGuid(), sessionId, minutesId)); Assert.True(called);
        var offline = new SalesMeetingClosingApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => throw new InvalidOperationException())) { BaseAddress = new("https://example.test/") }), true);
        await Assert.ThrowsAsync<SalesMeetingClosingApiException>(() => offline.ListMinutesAsync(Guid.NewGuid(), sessionId));
    }
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request)); }
}
