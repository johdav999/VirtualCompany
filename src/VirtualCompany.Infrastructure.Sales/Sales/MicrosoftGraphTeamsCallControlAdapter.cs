using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class MicrosoftGraphTeamsCallControlAdapter(
    IHttpClientFactory clients, ITeamsAppOnlyTokenProvider tokens,
    IOptions<TeamsPresenterOptions> configured,
    ITeamsCallMediaPreparationProvider? media = null) : ITeamsCallControlAdapter
{
    public const string ClientName = "teams-call-control";

    public async Task<TeamsCallProviderResult> JoinAsync(TeamsCallJoinContext request, CancellationToken ct)
    {
        if (!TryParseJoin(request.JoinWebUrl, out var threadId, out var tenantId, out var organizerId) || tenantId != request.EntraTenantId)
            return new(TeamsCallProviderOutcome.PermanentFailure, ErrorCode: "invalid_meeting_identity", ErrorSummary: "The Teams join identity is invalid or belongs to another tenant.");
        TeamsCallMediaPreparation? prepared = null;
        if (configured.Value.AudioEnabled && string.Equals(configured.Value.MediaRoute, "teams_application_hosted", StringComparison.Ordinal))
            prepared = await (media ?? throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.NotReady,
                "The application-hosted media platform is not registered."))
                .PrepareAsync(request.CallId, request.MediaHostInstanceId, ct);
        var body = new Dictionary<string, object?>
        {
            ["callbackUri"] = request.CallbackUri.AbsoluteUri,
            ["requestedModalities"] = new[] { "audio" },
            ["mediaConfig"] = prepared is null
                ? new Dictionary<string, object?> { ["@odata.type"] = "#microsoft.graph.serviceHostedMediaConfig", ["preFetchMedia"] = Array.Empty<object>() }
                : new Dictionary<string, object?> { ["@odata.type"] = prepared.ODataType, ["blob"] = prepared.ConfigurationBlob },
            ["chatInfo"] = new Dictionary<string, object?> { ["@odata.type"] = "#microsoft.graph.teamsMeetingInfo", ["threadId"] = threadId, ["messageId"] = "0" },
            ["meetingInfo"] = new Dictionary<string, object?> { ["@odata.type"] = "#microsoft.graph.organizerMeetingInfo", ["organizer"] = new Dictionary<string, object?> { ["@odata.type"] = "#microsoft.graph.identitySet", ["user"] = new Dictionary<string, object?> { ["@odata.type"] = "#microsoft.graph.identity", ["id"] = organizerId, ["tenantId"] = tenantId.ToString("D") } } },
            ["tenantId"] = tenantId.ToString("D")
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, "communications/calls") { Content = JsonContent.Create(body) };
        var result = await SendAsync(request.EntraTenantId, request.RequiredPermissions, message, true, ct);
        if (prepared is not null && result.Outcome == TeamsCallProviderOutcome.Succeeded && !string.IsNullOrWhiteSpace(result.ProviderCallId))
            await media!.BindProviderCallAsync(request.CallId, result.ProviderCallId, ct);
        else if (prepared is not null && result.Outcome is TeamsCallProviderOutcome.Rejected or TeamsCallProviderOutcome.PermanentFailure)
            await media!.ReleaseAsync(request.CallId, CancellationToken.None);
        return result;
    }

    public async Task<TeamsCallProviderResult> AcceptAsync(TeamsCallProviderContext request, CancellationToken ct)
    {
        TeamsCallMediaPreparation? prepared = null;
        if (configured.Value.AudioEnabled && string.Equals(configured.Value.MediaRoute, "teams_application_hosted", StringComparison.Ordinal))
            prepared = await (media ?? throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.NotReady,
                "The application-hosted media platform is not registered."))
                .PrepareAsync(request.CallId, request.MediaHostInstanceId, ct);
        var body = new Dictionary<string, object?> { ["callbackUri"] = configured.Value.BotCallingCallbackUrl,
            ["acceptedModalities"] = new[] { "audio" }, ["mediaConfig"] = prepared is null
                ? new Dictionary<string, object?> { ["@odata.type"] = "#microsoft.graph.serviceHostedMediaConfig", ["preFetchMedia"] = Array.Empty<object>() }
                : new Dictionary<string, object?> { ["@odata.type"] = prepared.ODataType, ["blob"] = prepared.ConfigurationBlob } };
        var result = await SendAsync(request.EntraTenantId, request.RequiredPermissions,
            new HttpRequestMessage(HttpMethod.Post, $"communications/calls/{Uri.EscapeDataString(request.ProviderCallId)}/answer") { Content = JsonContent.Create(body) }, false, ct);
        if (prepared is not null && result.Outcome == TeamsCallProviderOutcome.Succeeded)
            await media!.BindProviderCallAsync(request.CallId, request.ProviderCallId, ct);
        return result;
    }

    public async Task<TeamsCallProviderResult> LeaveAsync(TeamsCallProviderContext request, CancellationToken ct)
    {
        var result = await SendAsync(request.EntraTenantId, request.RequiredPermissions,
            new HttpRequestMessage(HttpMethod.Delete, $"communications/calls/{Uri.EscapeDataString(request.ProviderCallId)}"), false, ct);
        if (media is not null && result.Outcome is TeamsCallProviderOutcome.Succeeded or TeamsCallProviderOutcome.NotFound)
            await media.ReleaseAsync(request.CallId, CancellationToken.None);
        return result;
    }

    public Task<TeamsCallProviderResult> GetAsync(TeamsCallProviderContext request, CancellationToken ct) =>
        SendAsync(request.EntraTenantId, request.RequiredPermissions,
            new HttpRequestMessage(HttpMethod.Get, $"communications/calls/{Uri.EscapeDataString(request.ProviderCallId)}"), false, ct);

    private async Task<TeamsCallProviderResult> SendAsync(Guid tenantId, IReadOnlyCollection<string> permissions,
        HttpRequestMessage message, bool create, CancellationToken ct)
    {
        var token = await tokens.AcquireAsync(new TeamsAppOnlyTokenRequest(tenantId, permissions), ct);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var watch = Stopwatch.StartNew();
        try
        {
            using (message)
            using (var response = await clients.CreateClient(ClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                TeamsCallTelemetry.ProviderLatency.Record(watch.Elapsed.TotalMilliseconds);
                if (response.StatusCode == HttpStatusCode.NotFound) return new(TeamsCallProviderOutcome.NotFound);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
                    return new(TeamsCallProviderOutcome.Rejected, ErrorCode: "provider_rejected", ErrorSummary: "Microsoft Graph rejected the call-control request.");
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
                    return new(create ? TeamsCallProviderOutcome.Ambiguous : TeamsCallProviderOutcome.RetryableFailure,
                        ErrorCode: "provider_unavailable", ErrorSummary: "Microsoft Graph did not return a conclusive call-control result.");
                if (!response.IsSuccessStatusCode) return new(TeamsCallProviderOutcome.PermanentFailure, ErrorCode: "provider_error", ErrorSummary: "Microsoft Graph could not process the call-control request.");
                if (response.StatusCode == HttpStatusCode.NoContent) return new(TeamsCallProviderOutcome.Succeeded, ProviderState: "terminated");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = json.RootElement;
                var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                var state = root.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
                return new(TeamsCallProviderOutcome.Succeeded, id, state, id is null ? null : SafeReference(id));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(create ? TeamsCallProviderOutcome.Ambiguous : TeamsCallProviderOutcome.RetryableFailure,
                ErrorCode: "provider_timeout", ErrorSummary: "The Graph call-control outcome is unknown and requires reconciliation.");
        }
        catch (HttpRequestException)
        {
            return new(create ? TeamsCallProviderOutcome.Ambiguous : TeamsCallProviderOutcome.RetryableFailure,
                ErrorCode: "provider_transport", ErrorSummary: "The Graph call-control outcome is unknown and requires reconciliation.");
        }
    }

    private static bool TryParseJoin(string value, out string threadId, out Guid tenantId, out string organizerId)
    {
        threadId = organizerId = string.Empty; tenantId = Guid.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)) return false;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(segments, x => x.Equals("meetup-join", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= segments.Length) return false;
        threadId = Uri.UnescapeDataString(segments[index + 1]);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("context", out var raw)) return false;
        try
        {
            using var context = JsonDocument.Parse(raw.ToString());
            var root = context.RootElement;
            organizerId = root.TryGetProperty("Oid", out var oid) ? oid.GetString() ?? string.Empty : string.Empty;
            return root.TryGetProperty("Tid", out var tid) && Guid.TryParse(tid.GetString(), out tenantId) && !string.IsNullOrWhiteSpace(organizerId);
        }
        catch (JsonException) { return false; }
    }
    private static string SafeReference(string id)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)));
        return $"graph-call:{hash[..12].ToLowerInvariant()}";
    }
}
