using System.Net;
using System.Net.Http.Json;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesPresentationPresetApiClientTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Preset = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Version = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Routes_payloads_uploads_and_company_context_are_preserved()
    {
        var handler = new RecordingHandler();
        var client = Client(handler);
        await client.ListAsync(Company, "quarterly review", true);
        await client.GetAsync(Company, Preset);
        await client.CreateAsync(Company, Request());
        await client.UpdateDraftAsync(Company, Preset, Update());
        await using var stream = new MemoryStream([1, 2, 3]);
        await client.ImportAssetAsync(Company, Preset, Version, "qbr.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", stream);
        await client.GetReadinessAsync(Company, Preset, Version);
        await client.GetSlidesAsync(Company, Preset, Version);
        await client.RetryAssetAsync(Company, Preset, Version);
        await client.PublishAsync(Company, Preset, Version, 4, 2);
        await client.CreateNextDraftAsync(Company, Preset, 5);
        await client.ArchiveAsync(Company, Preset, 6, "No longer current");

        Assert.Equal(11, handler.Requests.Count);
        Assert.All(handler.Requests, x => Assert.Equal(Company.ToString(), x.CompanyId));
        Assert.Contains("search=quarterly%20review", handler.Requests[0].Uri.Query);
        Assert.Contains("includeArchived=true", handler.Requests[0].Uri.Query);
        Assert.Equal($"/api/sales/presentation-presets/{Preset:D}/versions/{Version:D}/publish", handler.Requests[8].Uri.AbsolutePath);
        Assert.Contains("\"expectedPresetVersion\":4", handler.Requests[8].Body);
        Assert.Equal("qbr.pptx", handler.UploadName);
    }

    [Fact]
    public async Task Conflict_problem_is_mapped_to_typed_exception()
    {
        var client = Client(new ConflictHandler());
        var ex = await Assert.ThrowsAsync<SalesPresentationPresetConflictApiException>(() => client.PublishAsync(Company, Preset, Version, 1, 1));
        Assert.Equal("sales.presentation_preset.conflict", ex.Code);
        Assert.Contains("changed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_transport()
    {
        var handler = new CancellationHandler();
        var client = Client(handler);
        using var cts = new CancellationTokenSource();
        var request = client.GetAsync(Company, Preset, cts.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(handler.Cancelled);
    }

    private static SalesPresentationPresetApiClient Client(HttpMessageHandler handler) => new(new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }), false, new Resolver());
    private static SalesPresentationPresetCreateRequest Request() => new("QBR", "Review", Guid.NewGuid(), null, "Align", "Executives", 30, null, "guided", "en", ["sales_meeting"], null, "{}");
    private static SalesPresentationPresetUpdateRequest Update() => new(1, 1, "QBR", "Review", Guid.NewGuid(), null, "Align", "Executives", 30, null, "guided", "en", ["sales_meeting"], null, "{}");

    private sealed class Resolver : IApiProblemMessageResolver { public string Resolve(ApiProblemResponse? problem, string fallbackMessage) => problem?.Detail ?? fallbackMessage; }
    private sealed record Captured(HttpMethod Method, Uri Uri, string CompanyId, string Body);
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = []; public string? UploadName { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            if (request.Content is MultipartFormDataContent multipart) UploadName = multipart.Single(x => x.Headers.ContentDisposition?.Name == "file").Headers.ContentDisposition!.FileName!.Trim('"');
            Requests.Add(new(request.Method, request.RequestUri!, request.Headers.GetValues("X-Company-Id").Single(), body));
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("presentation-presets")) return Json(new[] { ListItem() });
            if (path.EndsWith("/readiness")) return Json(new SalesPresentationPresetReadinessViewModel("ready", true, [], ["publish"], false));
            if (path.EndsWith("/slides")) return Json(Array.Empty<SalesPresentationPresetSlideViewModel>());
            if (path.EndsWith("/asset") || path.EndsWith("/asset/retry")) return Json(Asset(), HttpStatusCode.Accepted);
            return Json(Detail(), request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK);
        }
        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }

    private sealed class ConflictHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"code\":\"sales.presentation_preset.conflict\",\"detail\":\"The preset changed. Reload it.\"}", Encoding.UTF8, "application/problem+json")
        });
    }
    private sealed class CancellationHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public bool Cancelled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return new(HttpStatusCode.OK);
        }
    }

    private static SalesPresentationPresetListItemViewModel ListItem() => new(Preset, "QBR", "Review", Guid.NewGuid(), "active", null, 1, "processed", 3, 0, DateTime.UtcNow, 1);
    private static SalesPresentationPresetAssetViewModel Asset() => new(Guid.NewGuid(), "qbr.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", 3, new string('a', 64), "processed", 1, 1, 3, "renderer", "1", "flattened", null, null, false, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, 1);
    private static SalesPresentationPresetVersionViewModel Draft() => new(Version, 1, "draft", null, "{}", "Align", "Executives", 30, null, "guided", "en", ["sales_meeting"], null, null, null, DateTime.UtcNow, DateTime.UtcNow, 1, Asset());
    private static SalesPresentationPresetViewModel Detail() => new(Preset, Company, "QBR", "Review", Guid.NewGuid(), "active", null, DateTime.UtcNow, DateTime.UtcNow, null, 1, 0, Draft(), null, [Draft()], ["update_defaults", "archive"]);
}
