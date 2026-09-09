using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MicrosoftGraphMeetingTranscriptAdapter(IHttpClientFactory clients) : IMeetingTranscriptProviderAdapter
{
    public const string ClientName = "microsoft-graph-meeting-transcripts";
    private const string GraphRoot = "https://graph.microsoft.com/v1.0/";
    public string Provider => "microsoft_graph";
    public IReadOnlyCollection<string> RequiredScopes { get; } = ["OnlineMeetings.Read", "OnlineMeetingTranscript.Read.All"];

    public async Task<MeetingTranscriptResolvedMeeting> ResolveMeetingAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingUrl, CancellationToken cancellationToken)
    {
        var organizer = Uri.EscapeDataString(Required(context.OrganizerUserId, nameof(context.OrganizerUserId)));
        var literal = Required(onlineMeetingUrl, nameof(onlineMeetingUrl)).Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"JoinWebUrl eq '{literal}'");
        using var response = await SendAsync(Authorized(HttpMethod.Get,
            $"{GraphRoot}users/{organizer}/onlineMeetings?$filter={filter}&$select=id"), context.AccessToken, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("value", out var values) || values.GetArrayLength() != 1)
            throw new MeetingTranscriptProviderException("graph_online_meeting_not_found",
                "Microsoft Graph could not resolve exactly one online meeting from the stored Teams join URL.",
                MeetingTranscriptProviderFailureKind.Permanent);
        return new(Required(values[0].GetProperty("id").GetString(), "onlineMeetingId"));
    }

    public async Task<MeetingTranscriptProviderSubscription> CreateSubscriptionAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingId, Uri notificationUrl, Uri lifecycleNotificationUrl, string clientState,
        DateTime expiresUtc, CancellationToken cancellationToken)
    {
        var resource = $"communications/onlineMeetings/{Required(onlineMeetingId, nameof(onlineMeetingId))}/transcripts";
        using var request = Authorized(HttpMethod.Post, $"{GraphRoot}subscriptions");
        request.Content = JsonContent.Create(new
        {
            changeType = "created",
            notificationUrl = notificationUrl.AbsoluteUri,
            lifecycleNotificationUrl = lifecycleNotificationUrl.AbsoluteUri,
            resource,
            expirationDateTime = expiresUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            clientState = Required(clientState, nameof(clientState))
        });
        using var response = await SendAsync(request, context.AccessToken, cancellationToken);
        return await ReadSubscriptionAsync(response, resource, cancellationToken);
    }

    public async Task<MeetingTranscriptProviderSubscription> RenewSubscriptionAsync(MeetingTranscriptProviderContext context,
        string subscriptionId, string resource, DateTime expiresUtc, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Patch,
            $"{GraphRoot}subscriptions/{Uri.EscapeDataString(Required(subscriptionId, nameof(subscriptionId)))}");
        request.Content = JsonContent.Create(new
        {
            expirationDateTime = expiresUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        });
        using var response = await SendAsync(request, context.AccessToken, cancellationToken);
        return await ReadSubscriptionAsync(response, resource, cancellationToken);
    }

    public async Task DeleteSubscriptionAsync(MeetingTranscriptProviderContext context, string subscriptionId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(Authorized(HttpMethod.Delete,
            $"{GraphRoot}subscriptions/{Uri.EscapeDataString(Required(subscriptionId, nameof(subscriptionId)))}"),
            context.AccessToken, cancellationToken);
    }

    public async Task<MeetingTranscriptDescriptorPage> ListTranscriptsAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingId, string? continuationToken, CancellationToken cancellationToken)
    {
        var uri = string.IsNullOrWhiteSpace(continuationToken)
            ? $"{GraphRoot}users/{Uri.EscapeDataString(context.OrganizerUserId)}/onlineMeetings/{Uri.EscapeDataString(onlineMeetingId)}/transcripts"
            : ValidateContinuation(continuationToken);
        using var response = await SendAsync(Authorized(HttpMethod.Get, uri), context.AccessToken, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var items = json.RootElement.TryGetProperty("value", out var values)
            ? values.EnumerateArray().Select(ReadDescriptor).ToArray()
            : [];
        var next = json.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        return new(items, next);
    }

    public async Task<MeetingTranscriptDocument> FetchTranscriptAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingId, string transcriptId, CancellationToken cancellationToken)
    {
        var baseUri = $"{GraphRoot}users/{Uri.EscapeDataString(context.OrganizerUserId)}/onlineMeetings/{Uri.EscapeDataString(onlineMeetingId)}/transcripts/{Uri.EscapeDataString(transcriptId)}";
        using var metadataResponse = await SendAsync(Authorized(HttpMethod.Get, baseUri), context.AccessToken, cancellationToken);
        using var metadataJson = await JsonDocument.ParseAsync(await metadataResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var descriptor = ReadDescriptor(metadataJson.RootElement);
        var version = metadataResponse.Headers.ETag?.Tag ?? descriptor.Version;
        descriptor = descriptor with { Version = version };

        string content;
        try
        {
            content = await FetchContentAsync(baseUri, context.AccessToken, "text/vtt", cancellationToken);
        }
        catch (MeetingTranscriptProviderException exception)
            when (exception.Code.Equals("SpeakerAttributionNotAllowed", StringComparison.OrdinalIgnoreCase))
        {
            // Some tenants allow transcript retrieval but disable speaker attribution. Graph exposes
            // an explicit unattributed representation for that policy rather than requiring a failure.
            content = await FetchContentAsync(baseUri, context.AccessToken,
                "application/vnd.microsoft.graph.transcript+text", cancellationToken);
        }
        return new(descriptor, Sha256(content), ParseWebVtt(content, context.MeetingStartsUtc));
    }

    private async Task<string> FetchContentAsync(string baseUri, string accessToken, string mediaType,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, baseUri + "/content");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        using var response = await SendAsync(request, accessToken, cancellationToken);
        const int maximumBytes = 5_000_000;
        if (response.Content.Headers.ContentLength > maximumBytes) throw TranscriptTooLarge();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes) throw TranscriptTooLarge();
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        try { return new UTF8Encoding(false, true).GetString(destination.GetBuffer(), 0, checked((int)destination.Length)); }
        catch (DecoderFallbackException e)
        {
            throw new MeetingTranscriptProviderException("graph_transcript_invalid_encoding",
                "Microsoft Graph returned transcript content with invalid text encoding.",
                MeetingTranscriptProviderFailureKind.Permanent, innerException: e);
        }
    }

    private static MeetingTranscriptProviderException TranscriptTooLarge() => new("graph_transcript_too_large",
        "The Microsoft Graph transcript exceeds the supported ingestion size.",
        MeetingTranscriptProviderFailureKind.Permanent);

    internal static IReadOnlyList<MeetingTranscriptNormalizedSegment> ParseWebVtt(string content, DateTime meetingStartsUtc)
    {
        var blocks = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var result = new List<MeetingTranscriptNormalizedSegment>();
        var sequence = 0;
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var timestampIndex = Array.FindIndex(lines, line => TimestampLine().IsMatch(line));
            if (timestampIndex < 0 || timestampIndex + 1 >= lines.Length) continue;
            var match = TimestampLine().Match(lines[timestampIndex]);
            if (!TryOffset(match.Groups[1].Value, out var start) || !TryOffset(match.Groups[2].Value, out var end)) continue;
            var text = WebUtility.HtmlDecode(string.Join(" ", lines.Skip(timestampIndex + 1))).Trim();
            var speakerMatch = SpeakerTag().Match(text);
            string? speaker = null;
            if (speakerMatch.Success)
            {
                speaker = speakerMatch.Groups[1].Value.Trim();
                text = speakerMatch.Groups[2].Value.Trim();
            }
            text = StripTags().Replace(text, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            sequence++;
            result.Add(new($"cue-{sequence}-{start.TotalMilliseconds:0}", text, speaker,
                meetingStartsUtc.ToUniversalTime().Add(start), meetingStartsUtc.ToUniversalTime().Add(end)));
        }
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string accessToken, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Required(accessToken, nameof(accessToken)));
        try
        {
            var response = await clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            var retryAfter = response.Headers.RetryAfter?.Delta;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            var code = ReadGraphErrorCode(body);
            throw status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new MeetingTranscriptProviderException(
                    code ?? "graph_transcript_permission_required",
                    code == "GraphAccessToTranscriptsDisabled"
                        ? "Microsoft Teams transcript API access is disabled by the tenant administrator."
                        : "Reconnect Microsoft 365 and grant transcript access.", MeetingTranscriptProviderFailureKind.AuthenticationRequired),
                HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                    new MeetingTranscriptProviderException(code ?? "graph_transcript_temporarily_unavailable",
                        "Microsoft Graph transcript access is temporarily unavailable.", MeetingTranscriptProviderFailureKind.Retryable, retryAfter),
                _ => new MeetingTranscriptProviderException(code ?? "graph_transcript_request_rejected",
                    "Microsoft Graph rejected the transcript request.", MeetingTranscriptProviderFailureKind.Permanent)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (MeetingTranscriptProviderException) { throw; }
        catch (OperationCanceledException e)
        {
            throw new MeetingTranscriptProviderException("graph_transcript_timeout",
                "Microsoft Graph transcript access timed out.", MeetingTranscriptProviderFailureKind.Retryable, innerException: e);
        }
        catch (HttpRequestException e)
        {
            throw new MeetingTranscriptProviderException("graph_transcript_transport",
                "Microsoft Graph transcript access is temporarily unavailable.", MeetingTranscriptProviderFailureKind.Retryable, innerException: e);
        }
    }

    private static async Task<MeetingTranscriptProviderSubscription> ReadSubscriptionAsync(HttpResponseMessage response,
        string fallbackResource, CancellationToken cancellationToken)
    {
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        return new(Required(root.GetProperty("id").GetString(), "subscriptionId"),
            root.TryGetProperty("resource", out var resource) ? resource.GetString() ?? fallbackResource : fallbackResource,
            root.GetProperty("expirationDateTime").GetDateTime().ToUniversalTime());
    }

    private static MeetingTranscriptDescriptor ReadDescriptor(JsonElement value)
    {
        var id = Required(value.GetProperty("id").GetString(), "transcriptId");
        var created = value.TryGetProperty("createdDateTime", out var createdValue) && createdValue.ValueKind == JsonValueKind.String
            ? createdValue.GetDateTime().ToUniversalTime() : DateTime.UnixEpoch;
        var ended = value.TryGetProperty("endDateTime", out var endedValue) && endedValue.ValueKind == JsonValueKind.String
            ? endedValue.GetDateTime().ToUniversalTime() : (DateTime?)null;
        var correlation = value.TryGetProperty("contentCorrelationId", out var correlationValue) ? correlationValue.GetString() : null;
        var version = value.TryGetProperty("@odata.etag", out var etag) ? etag.GetString() : null;
        version ??= correlation ?? created.ToString("O", CultureInfo.InvariantCulture);
        var metadata = JsonSerializer.Serialize(new { id, createdDateTime = created, endDateTime = ended, contentCorrelationId = correlation });
        return new(id, version, created, ended, metadata);
    }

    private static string ValidateContinuation(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.OrdinalIgnoreCase))
            throw new MeetingTranscriptProviderException("graph_pagination_token_invalid",
                "Microsoft Graph returned an invalid transcript continuation link.", MeetingTranscriptProviderFailureKind.Permanent);
        return uri.AbsoluteUri;
    }

    private static string? ReadGraphErrorCode(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.GetProperty("error");
            if (error.TryGetProperty("innerError", out var inner) && inner.TryGetProperty("code", out var innerCode)) return innerCode.GetString();
            return error.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string uri) => new(method, uri);
    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name) : value.Trim();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool TryOffset(string value, out TimeSpan result)
    {
        var formats = new[] { @"hh\:mm\:ss\.fff", @"mm\:ss\.fff" };
        return TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out result);
    }

    [GeneratedRegex(@"^(\d{2}:\d{2}(?::\d{2})?\.\d{3})\s+-->\s+(\d{2}:\d{2}(?::\d{2})?\.\d{3})", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampLine();
    [GeneratedRegex(@"^<v(?:\.[^\s>]+)?\s+([^>]+)>(.*)</v>$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerTag();
    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex StripTags();
}
