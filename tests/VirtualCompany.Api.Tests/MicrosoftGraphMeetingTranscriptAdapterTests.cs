using System.Net;
using System.Net.Http.Headers;
using System.Text;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MicrosoftGraphMeetingTranscriptAdapterTests
{
    [Fact]
    public void WebVtt_is_normalized_with_speaker_attribution_and_meeting_relative_times()
    {
        var meetingStart = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
        var segments = MicrosoftGraphMeetingTranscriptAdapter.ParseWebVtt("""
            WEBVTT

            00:00:01.000 --> 00:00:03.500
            <v Jordan Lee>We need a faster close.</v>
            """, meetingStart);

        var segment = Assert.Single(segments);
        Assert.Equal("Jordan Lee", segment.SpeakerLabel);
        Assert.Equal("We need a faster close.", segment.Content);
        Assert.Equal(meetingStart.AddSeconds(1), segment.StartedUtc);
        Assert.Equal(meetingStart.AddMilliseconds(3500), segment.EndedUtc);
    }

    [Fact]
    public async Task Pagination_accepts_only_graph_v1_https_continuations()
    {
        var handler = new QueueHandler(Response(HttpStatusCode.OK, """{"value":[],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users/u/onlineMeetings/m/transcripts?$skiptoken=safe"}"""));
        var adapter = new MicrosoftGraphMeetingTranscriptAdapter(new Factory(handler));
        var context = Context();

        var first = await adapter.ListTranscriptsAsync(context, "meeting", null, CancellationToken.None);
        Assert.Contains("$skiptoken=safe", first.ContinuationToken);
        await adapter.ListTranscriptsAsync(context, "meeting", first.ContinuationToken, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);

        var exception = await Assert.ThrowsAsync<MeetingTranscriptProviderException>(() =>
            adapter.ListTranscriptsAsync(context, "meeting", "https://attacker.example/v1.0/transcripts", CancellationToken.None));
        Assert.Equal("graph_pagination_token_invalid", exception.Code);
    }

    [Fact]
    public async Task Throttling_is_retryable_and_preserves_retry_after()
    {
        var throttled = Response(HttpStatusCode.TooManyRequests, """{"error":{"code":"TooManyRequests"}}""");
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
        var adapter = new MicrosoftGraphMeetingTranscriptAdapter(new Factory(new QueueHandler(throttled)));

        var exception = await Assert.ThrowsAsync<MeetingTranscriptProviderException>(() =>
            adapter.ListTranscriptsAsync(Context(), "meeting", null, CancellationToken.None));

        Assert.Equal(MeetingTranscriptProviderFailureKind.Retryable, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(17), exception.RetryAfter);
    }

    [Fact]
    public async Task Speaker_policy_denial_falls_back_to_unattributed_content()
    {
        var handler = new QueueHandler(
            Response(HttpStatusCode.OK, """{"id":"transcript","createdDateTime":"2026-09-04T08:00:00Z","contentCorrelationId":"v1"}"""),
            Response(HttpStatusCode.Forbidden, """{"error":{"code":"SpeakerAttributionNotAllowed"}}"""),
            Response(HttpStatusCode.OK,
                "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nA policy-safe unattributed statement.\n",
                "application/vnd.microsoft.graph.transcript+text"));
        var adapter = new MicrosoftGraphMeetingTranscriptAdapter(new Factory(handler));

        var document = await adapter.FetchTranscriptAsync(Context(), "meeting", "transcript", CancellationToken.None);

        Assert.Single(document.Segments);
        Assert.Null(document.Segments[0].SpeakerLabel);
        Assert.Equal("application/vnd.microsoft.graph.transcript+text", handler.Requests[2].Accept);
    }

    private static MeetingTranscriptProviderContext Context() =>
        new("secret-token", "organizer@example.com", new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc));

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<(Uri Uri, string? Accept)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Accept.FirstOrDefault()?.MediaType));
            return Task.FromResult(queue.Count > 0 ? queue.Dequeue() : Response(HttpStatusCode.OK, """{"value":[]}"""));
        }
    }
}
