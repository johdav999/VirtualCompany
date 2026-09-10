using System.Net.Http.Json;
using System.Text.Json;
namespace VirtualCompany.Web.Services;

public sealed record BrowserRoomParticipant(Guid Id, string DisplayName, string State, long Version, bool Connected,
    string MediaIdentity, bool IsOrganizer, bool AiProcessingAllowed, bool TranscriptRetentionAllowed);
public sealed record BrowserRoomOperation(Guid Id, string Action, string State, int Attempts, string? ProblemCode);
public sealed record BrowserRoomSnapshot(Guid Id, Guid? MeetingSessionId, string State, string AgentHealth, long Version, DateTime ExpiresUtc,
    IReadOnlyList<BrowserRoomParticipant> Participants, IReadOnlyList<BrowserRoomOperation> Operations)
{ public Guid? InvitationId { get; init; } public string ConsentNoticeVersion { get; init; } = ""; }
public sealed record BrowserRoomAudience(Guid Id, string DisplayName, string MediaIdentity);
public sealed record BrowserGuestSnapshot(Guid RoomId, Guid ParticipantId, string RoomState, string AdmissionState, long Version, DateTime ExpiresUtc,
    bool AiProcessingAllowed, bool TranscriptRetentionAllowed, IReadOnlyList<BrowserRoomAudience> Participants)
{ public string ConsentNoticeVersion { get; init; } = ""; }
public sealed record BrowserGuestSession(string Credential, BrowserGuestSnapshot Participant)
{ public override string ToString() => "BrowserGuestSession { Credential = [redacted] }"; }
public sealed record BrowserRoomToken(string Url, string Token, string Identity, DateTimeOffset ExpiresAt)
{ public override string ToString() => "BrowserRoomToken { Token = [redacted] }"; }
public sealed record BrowserRoomInvitation(Guid Id, string Secret, DateTime ExpiresUtc)
{ public override string ToString() => "BrowserRoomInvitation { Secret = [redacted] }"; }
public sealed record BrowserRoomAgentEvidence(string SourceId, string SourceType, string SourceTitle);
public sealed record BrowserRoomAgentAnswer(Guid QuestionId, string Question, string? Answer, string Status,
    string Visibility, long Version, IReadOnlyList<BrowserRoomAgentEvidence> Evidence);
public sealed record BrowserRoomAgentSpeech(Guid Id, string Kind, string Status, int DurationMilliseconds,
    string? FailureCode, string? FailureSummary, DateTime CreatedUtc);
public sealed record BrowserRoomPlaybackStop(Guid? StopId, string State, int RequiredCount, int AcknowledgedCount,
    DateTime? RequestedUtc, DateTime? DeadlineUtc, int? ElapsedMilliseconds);
public sealed record BrowserRoomFloor(string State, string OwnerLabel, Guid? OwnerParticipantId, string ControlMode,
    Guid? PendingTurnId, Guid? PendingParticipantId, string? PendingParticipantLabel, Guid? PendingQuestionId,
    string PendingTurnState, bool AddressedAgent, bool Overlap, long TurnGeneration, long ResponseGeneration,
    long PresentationVersion, int SlideNumber, int TalkingPointIndex, string? ResumeMarker,
    int ResumeOffsetMilliseconds, Guid? PreauthorizedCoHostParticipantId, long Version,
    BrowserRoomPlaybackStop PlaybackStop);
public sealed record BrowserRoomAgentStatus(Guid RoomId, Guid? AgentId, string AgentName, string State,
    string VoiceHealth, long Generation, long TurnGeneration, int ConsentedParticipants, int RequiredParticipants,
    bool AllParticipantsConsented, DateTime? StartedUtc, DateTime? LeaseExpiresUtc,
    Guid? NarrationRevisionId, Guid? NarrationSegmentId, string NarrationState,
    long ReceivedAudioMilliseconds, long DetectedSpeechMilliseconds, long ForwardedAudioMilliseconds,
    long? ProviderBilledAudioMilliseconds, string BillingEvidenceState, long OutputAudioMilliseconds,
    int InputTokens, int OutputTokens, decimal EstimatedSpendUsd, string? LastErrorCode, string? LastErrorSummary,
    long RoomVersion, BrowserRoomAgentAnswer? LatestAnswer, IReadOnlyList<BrowserRoomAgentSpeech> RecentSpeech,
    BrowserRoomFloor? Floor = null);
public sealed record BrowserRoomCaptureReview(Guid RoomId, Guid SessionId, Guid? AgentId, string State,
    long SessionVersion, long CaptureVersion, Guid? CaptureCheckpointId, DateTime RetentionUntilUtc,
    string Coverage, IReadOnlyList<SalesMeetingTranscriptSegmentViewModel> Segments,
    SalesMeetingClosingSnapshotViewModel? Closing, Guid? EvidenceArtifactId = null, string MeetingStatus = "");
public sealed class BrowserRoomRequestException(int status, string code) : Exception(MessageFor(status, code))
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    private static string MessageFor(int status, string code) => code switch
    {
        "meeting_not_open" => "This meeting opens 15 minutes before its scheduled start. Try again then.",
        "admission_required" => "Wait for the host to admit you before connecting.",
        "version_conflict" => "The room changed. Review the refreshed participants and try again.",
        "invitation_unavailable" => "This invitation is expired or already used. Ask the host for a new link.",
        "sales.room_agent.floor_conflict" => "The room floor changed. Review the current speaker and try again.",
        "sales.room_agent.floor_not_ready" => "The agent is waiting for the current slide and audience to be ready.",
        _ => status switch
        {
            401 or 403 => "Meeting access is no longer available. You may have been denied or removed. Ask the host for a new invitation; organizers should check their sign-in.",
            404 or 410 => "This meeting is unavailable or has ended. Ask the host for a new invitation.",
            429 => "The meeting has reached an access limit. Wait a moment or contact the host.",
            503 => "The meeting service is unavailable. Try again shortly.",
            _ => "The request could not be completed. Refresh the room and try again."
        }
    };
}
public sealed class SalesBrowserRoomApiClient(ICompanyApiTransport transport, bool offline)
{
    private const string HostRoot = "api/sales/browser-rooms/";
    public Task<BrowserRoomCaptureReview> CaptureReviewAsync(Guid company, Guid room, CancellationToken ct = default) =>
        Host<BrowserRoomCaptureReview>(company, room, "/capture-review", null, ct);
    public Task<BrowserRoomSnapshot> StatusAsync(Guid company, Guid room, CancellationToken ct) => Host<BrowserRoomSnapshot>(company, room, "", null, ct);
    public Task<BrowserRoomToken> TokenAsync(Guid company, Guid room, CancellationToken ct) => Host<BrowserRoomToken>(company, room, "/media-token", new { }, ct);
    public Task<BrowserGuestSnapshot> ConsentAsync(Guid company, Guid room, long participantVersion, string purpose, bool granted, string noticeVersion, CancellationToken ct) =>
        Host<BrowserGuestSnapshot>(company, room, "/consent", new { commandId = Guid.NewGuid(), expectedVersion = participantVersion, purpose, granted, noticeVersion }, ct);
    public Task<BrowserRoomAgentStatus> AgentAsync(Guid company, Guid room, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent", null, ct);
    public Task<BrowserRoomAgentStatus> StartAgentAsync(Guid company, Guid room, long version, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/start", new { commandId = Guid.NewGuid(), expectedVersion = version }, ct);
    public Task<BrowserRoomAgentStatus> StopAgentAsync(Guid company, Guid room, long version, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/stop", new { commandId = Guid.NewGuid(), expectedVersion = version, reason = "host_stopped" }, ct);
    public Task<BrowserRoomAgentStatus> InvokeNarrationAsync(Guid company, Guid room, long version, Guid revision, Guid segment, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/narration", new { commandId = Guid.NewGuid(), expectedVersion = version, revisionId = revision, segmentId = segment, offsetMilliseconds = 0 }, ct);
    public Task<BrowserRoomAgentStatus> AskAgentAsync(Guid company, Guid room, long version, string question, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/questions", new { commandId = Guid.NewGuid(), expectedVersion = version, question }, ct);
    public Task<BrowserRoomAgentStatus> SpeakAnswerAsync(Guid company, Guid room, long version, Guid question, long questionVersion, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/answers", new { commandId = Guid.NewGuid(), expectedVersion = version, questionId = question, expectedQuestionVersion = questionVersion }, ct);
    public Task<BrowserRoomAgentStatus> TakeOverAsync(Guid company, Guid room, long version, long floorVersion, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/takeover", new { commandId = Guid.NewGuid(), expectedVersion = version, expectedFloorVersion = floorVersion }, ct);
    public Task<BrowserRoomAgentStatus> ResumeAgentAsync(Guid company, Guid room, long version, long floorVersion, long presentationVersion, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/resume", new { commandId = Guid.NewGuid(), expectedVersion = version, expectedFloorVersion = floorVersion, expectedPresentationVersion = presentationVersion }, ct);
    public Task<BrowserRoomAgentStatus> ConfirmPendingTurnAsync(Guid company, Guid room, long version, long floorVersion, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/pending-turn/confirm", new { commandId = Guid.NewGuid(), expectedVersion = version, expectedFloorVersion = floorVersion }, ct);
    public Task<BrowserRoomAgentStatus> DismissPendingTurnAsync(Guid company, Guid room, long version, long floorVersion, CancellationToken ct) =>
        Host<BrowserRoomAgentStatus>(company, room, "/agent/pending-turn/dismiss", new { commandId = Guid.NewGuid(), expectedVersion = version, expectedFloorVersion = floorVersion }, ct);
    public Task<BrowserRoomSnapshot> DecideAsync(Guid company, Guid room, Guid participant, string decision, long version, Guid command, CancellationToken ct)
    {
        if (decision is not ("admit" or "deny" or "remove")) throw new ArgumentException("Invalid participant decision.");
        return Host<BrowserRoomSnapshot>(company, room, $"/participants/{participant:D}/{decision}", new { commandId = command, expectedVersion = version }, ct);
    }
    public Task<BrowserRoomSnapshot> EndAsync(Guid company, Guid room, long version, Guid command, CancellationToken ct) =>
        Host<BrowserRoomSnapshot>(company, room, "/end", new { commandId = command, expectedVersion = version }, ct);
    public Task<BrowserRoomInvitation> InviteAsync(Guid company, Guid room, long version, DateTime expires, Guid command, CancellationToken ct) =>
        Host<BrowserRoomInvitation>(company, room, "/invitations", new { commandId = command, expectedVersion = version, expiresUtc = DateTime.SpecifyKind(expires, DateTimeKind.Utc) }, ct);
    public Task<SalesBrowserPresentationHostViewModel> PresentationAsync(Guid company, Guid room, CancellationToken ct) =>
        Host<SalesBrowserPresentationHostViewModel>(company, room, "/presentation", null, ct);
    public Task<SalesPresentationCommandResultViewModel> PresentationCommandAsync(
        Guid company, Guid room, string toolName, SalesPresentationCommandViewModel command, CancellationToken ct) =>
        Host<SalesPresentationCommandResultViewModel>(company, room,
            "/presentation/commands/" + Uri.EscapeDataString(toolName), command, ct);
    public Task<SalesBrowserPresentationReadinessViewModel> OverridePresentationAsync(
        Guid company, Guid room, long presentationVersion, CancellationToken ct) =>
        Host<SalesBrowserPresentationReadinessViewModel>(company, room,
            $"/presentation/render-override/{presentationVersion}", new { }, ct);
    public async Task<SalesPresentationControlModeViewModel> PresentationModeAsync(
        Guid company, Guid room, string mode, long expectedVersion, Guid actorId, long actorGeneration, CancellationToken ct)
    {
        if (offline) throw new BrowserRoomRequestException(503, "offline");
        using var response = await transport.SendAsync(company, HttpMethod.Put,
            HostRoot + room.ToString("D") + "/presentation/control-mode",
            JsonContent.Create(new { mode, expectedVersion, actorId, actorGeneration }), ct);
        return await Read<SalesPresentationControlModeViewModel>(response, ct);
    }
    public async Task<SalesPresentationSlideImageViewModel> PresentationImageAsync(
        Guid company, Guid room, SalesPresentationStageSnapshotViewModel snapshot, CancellationToken ct)
    {
        using var response = await transport.SendAsync(company, HttpMethod.Get,
            HostRoot + $"{room:D}/presentation/decks/{snapshot.DeckId:D}/versions/{snapshot.DeckVersion}/slides/{snapshot.SlideNumber}/image",
            null, ct);
        if (!response.IsSuccessStatusCode) return await Read<SalesPresentationSlideImageViewModel>(response, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new($"data:{response.Content.Headers.ContentType?.MediaType ?? "image/svg+xml"};base64,{Convert.ToBase64String(bytes)}",
            snapshot.ImageWidthPixels, snapshot.ImageHeightPixels);
    }
    private async Task<T> Host<T>(Guid company, Guid room, string suffix, object? body, CancellationToken ct)
    {
        if (company == Guid.Empty) throw new ArgumentException("Company context is required.");
        if (offline) throw new BrowserRoomRequestException(503, "offline");
        try
        {
            using var content = body is null ? null : JsonContent.Create(body);
            using var response = await transport.SendAsync(company, body is null ? HttpMethod.Get : HttpMethod.Post, HostRoot + room.ToString("D") + suffix, content, ct);
            return await Read<T>(response, ct);
        }
        catch (HttpRequestException) { throw new BrowserRoomRequestException(503, "unreachable"); }
    }
    internal static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            string code = "request_failed";
            try { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); if (json.RootElement.TryGetProperty("code", out var value)) code = value.GetString() ?? code; }
            catch (JsonException) { }
            throw new BrowserRoomRequestException((int)response.StatusCode, code);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new BrowserRoomRequestException(503, "empty_response");
    }
}
// Guests deliberately use a fresh client with no company or authentication headers.
public sealed class SalesBrowserGuestApiClient(HttpClient http) : IDisposable
{
    public Task<BrowserGuestSession> RedeemAsync(string secret, string name, CancellationToken ct) => Send<BrowserGuestSession>("redeem", null, new { secret, displayName = name }, ct);
    public Task<BrowserGuestSnapshot> StatusAsync(Guid room, string credential, CancellationToken ct) => Send<BrowserGuestSnapshot>(room.ToString("D"), credential, null, ct);
    public Task<BrowserRoomToken> TokenAsync(Guid room, string credential, CancellationToken ct) => Send<BrowserRoomToken>($"{room:D}/media-token", credential, new { }, ct);
    public Task<BrowserGuestSnapshot> ConsentAsync(Guid room, string credential, long participantVersion, string purpose, bool granted, string noticeVersion, CancellationToken ct) =>
        Send<BrowserGuestSnapshot>($"{room:D}/consent", credential,
            new { commandId = Guid.NewGuid(), expectedVersion = participantVersion, purpose, granted, noticeVersion }, ct);
    public async Task AddressAgentAsync(Guid room, string credential, long participantVersion, string question, CancellationToken ct)
    {
        using var request = Request($"{room:D}/agent/addressed-question", credential,
            new { commandId = Guid.NewGuid(), expectedParticipantVersion = participantVersion, question });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) await SalesBrowserRoomApiClient.Read<object>(response, ct);
    }
    public async Task LeaveAsync(Guid room, string credential, CancellationToken ct)
    {
        using var request = Request($"{room:D}/leave", credential, new { });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new BrowserRoomRequestException((int)response.StatusCode, "leave_failed");
    }
    public Task<SalesBrowserPresentationPublicViewModel> PresentationAsync(Guid room, string credential, CancellationToken ct) =>
        Send<SalesBrowserPresentationPublicViewModel>($"{room:D}/presentation", credential, null, ct);
    public async Task<SalesPresentationSlideImageViewModel> PresentationImageAsync(
        Guid room, string credential, SalesPresentationStageSnapshotViewModel snapshot, CancellationToken ct)
    {
        using var request = Request(
            $"{room:D}/presentation/decks/{snapshot.DeckId:D}/versions/{snapshot.DeckVersion}/slides/{snapshot.SlideNumber}/image",
            credential, null);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new BrowserRoomRequestException((int)response.StatusCode, "presentation_image_unavailable");
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new($"data:{response.Content.Headers.ContentType?.MediaType ?? "image/svg+xml"};base64,{Convert.ToBase64String(bytes)}",
            snapshot.ImageWidthPixels, snapshot.ImageHeightPixels);
    }
    private static HttpRequestMessage Request(string path, string? credential, object? body)
    {
        var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, "api/sales/browser-room-guests/" + path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (credential is not null) request.Headers.Add("X-Sales-Room-Session", credential);
        return request;
    }
    private async Task<T> Send<T>(string path, string? credential, object? body, CancellationToken ct)
    {
        try
        {
            using var request = Request(path, credential, body);
            using var response = await http.SendAsync(request, ct);
            return await SalesBrowserRoomApiClient.Read<T>(response, ct);
        }
        catch (HttpRequestException) { throw new BrowserRoomRequestException(503, "unreachable"); }
    }
    public void Dispose() => http.Dispose();
}
