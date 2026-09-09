using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesPresentationDeckApiClientTests
{
    [Fact]
    public async Task Client_uses_company_scoped_typed_deck_routes_and_multipart_import()
    {
        var companyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var deckId = Guid.NewGuid();
        var handler = new RecordingHandler(companyId, sessionId, deckId);
        var client = new SalesPresentationDeckApiClient(
            new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }),
            false);

        await using var content = new MemoryStream([1, 2, 3]);
        var imported = await client.ImportAsync(
            companyId, sessionId, Guid.NewGuid(), "Board deck", "board.pptx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation", content);
        var listed = await client.ListAsync(companyId, sessionId);
        var fetched = await client.GetAsync(companyId, sessionId, deckId);
        var slide = await client.GetSlideAsync(companyId, sessionId, deckId, 1);
        await client.ActivateAsync(companyId, sessionId, deckId);
        await client.RetryAsync(companyId, sessionId, deckId);
        var brief = await client.GetBriefAsync(companyId, sessionId);
        await client.RegenerateBriefAsync(companyId, sessionId);

        Assert.Equal(deckId, imported.Id);
        Assert.Single(listed);
        Assert.Equal(deckId, fetched!.Id);
        Assert.Equal(1, slide!.SlideNumber);
        Assert.Equal(deckId, brief!.DeckId);
        Assert.Equal(8, handler.Paths.Count);
        Assert.All(handler.CompanyIds, value => Assert.Equal(companyId.ToString(), value));
    }

    [Fact]
    public async Task Read_returns_null_for_not_found()
    {
        var client = new SalesPresentationDeckApiClient(
            new CompanyApiTransport(new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("http://localhost/") }),
            false);

        Assert.Null(await client.GetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    private sealed class RecordingHandler(Guid companyId, Guid sessionId, Guid deckId) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> CompanyIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            CompanyIds.Add(request.Headers.GetValues("X-Company-Id").Single());
            Assert.StartsWith($"/api/sales/meeting-sessions/{sessionId:D}", request.RequestUri.AbsolutePath, StringComparison.Ordinal);

            string json;
            if (request.RequestUri.AbsolutePath.EndsWith("/slides/1", StringComparison.Ordinal))
            {
                json = SlideJson();
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/brief", StringComparison.Ordinal))
            {
                json = $$"""{"sessionId":"{{sessionId}}","deckId":"{{deckId}}","version":1,"requiresReview":true,"items":[]}""";
            }
            else
            {
                if (request.Content is MultipartFormDataContent multipart)
                {
                    Assert.Equal("board.pptx", multipart.Single(x => x.Headers.ContentDisposition!.Name == "file").Headers.ContentDisposition!.FileName!.Trim('"'));
                    Assert.Contains(multipart, x => x.Headers.ContentDisposition!.Name == "agentId");
                }
                json = request.RequestUri.AbsolutePath.EndsWith("/decks", StringComparison.Ordinal) && request.Method == HttpMethod.Get
                    ? $"[{DeckJson()}]"
                    : DeckJson();
            }

            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private string DeckJson() => $$"""
            {"id":"{{deckId}}","companyId":"{{companyId}}","sessionId":"{{sessionId}}","agentId":"{{Guid.NewGuid()}}",
             "version":1,"title":"Board deck","originalFileName":"board.pptx","contentType":"application/vnd.openxmlformats-officedocument.presentationml.presentation",
             "fileSizeBytes":3,"contentHash":"{{new string('a', 64)}}","storageKey":"safe/deck.pptx","storageUrl":null,
             "status":"pending_scan","processingVersion":1,"processingAttemptCount":0,"slideCount":0,
             "rendererName":null,"rendererVersion":null,"animationHandling":null,"failureCode":null,"failureSummary":null,
             "canRetry":false,"isActive":false,"briefVersion":0,"briefRegenerationPending":false,
             "uploadedByUserId":"{{Guid.NewGuid()}}","createdUtc":"2026-09-03T10:00:00Z","updatedUtc":"2026-09-03T10:00:00Z",
             "processingStartedUtc":null,"processedUtc":null,"failedUtc":null,"activatedUtc":null,"concurrencyVersion":1,"slides":[]}
            """;

        private string SlideJson() => $$"""
            {"id":"{{Guid.NewGuid()}}","processingVersion":1,"slideNumber":1,"title":"Opening","extractedText":"Hello",
             "speakerNotes":null,"imageStorageKey":"safe/slide.svg","imageStorageUrl":null,"imageWidthPixels":1600,
             "imageHeightPixels":900,"sourceWidthEmus":12192000,"sourceHeightEmus":6858000,"contentHash":"{{new string('b', 64)}}",
             "objective":"Introduce","expectedDurationSeconds":60,"transitionText":"Continue","status":"processed","planArtifacts":[]}
            """;
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
