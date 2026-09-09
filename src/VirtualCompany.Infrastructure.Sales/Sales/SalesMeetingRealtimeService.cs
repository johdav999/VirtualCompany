using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingRealtimeService(
    VirtualCompanyDbContext db,
    IRealtimeAgentSessionGateway gateway,
    IMeetingMediaAdapter media,
    ISalesMeetingQuestionAnsweringService questions,
    ISalesPresentationRuntimeService presentation,
    IOptions<SalesMeetingVoiceOptions> configured,
    TimeProvider timeProvider,
    ILogger<SalesMeetingRealtimeService> logger,
    ICompanyOutboxEnqueuer? outbox = null,
    ISalesMeetingPresentationConductor? conductor = null,
    ITeamsMeetingPresenterService? teamsPresenters = null) : ISalesMeetingRealtimeService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string TypedFallback = "Voice is unavailable. Typed questions, slides, capture, closing, review, and approval remain available.";

    public async Task<SalesMeetingRealtimeStatusDto?> GetStatusAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, userId, ct);
        if (!await db.SalesMeetingSessions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct)) return null;
        var voice = await db.SalesMeetingVoiceSessions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MeetingSessionId == sessionId)
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
        return await ToStatusAsync(companyId, sessionId, voice, ct);
    }

    public async Task<SalesMeetingRealtimeStartResult?> StartAsync(Guid companyId, Guid userId, Guid sessionId,
        StartSalesMeetingRealtimeRequest request, string? correlationId, CancellationToken ct)
    {
        EnsureIds(companyId, userId, sessionId, request.AgentId);
        if (string.IsNullOrWhiteSpace(request.OfferSdp) || request.OfferSdp.Length > 100_000)
            throw Validation(nameof(request.OfferSdp), "A WebRTC offer of 100,000 characters or fewer is required.");
        await RequireMemberAsync(companyId, userId, ct);
        var meeting = await db.SalesMeetingSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct);
        if (meeting is null) return null;
        if (meeting.ConsentStatus != SalesMeetingConsentStatus.Granted)
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.ConsentRequired, "Explicit meeting-media consent is required before voice starts.");
        if (meeting.Status is SalesMeetingSessionStatus.Completed or SalesMeetingSessionStatus.Cancelled or SalesMeetingSessionStatus.Failed)
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.Conflict, "Voice cannot start for a finished meeting.");
        if (meeting.RetentionUntilUtc <= Now().AddMinutes(1))
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.Unavailable,
                "The meeting retention window has expired. Typed meeting controls remain available.");
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.AgentId, ct)
            ?? throw new KeyNotFoundException("The Sales agent is unavailable in this company.");
        if (!agent.Department.Equals("Sales", StringComparison.OrdinalIgnoreCase))
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.Conflict, "Voice requires the company-owned Sales agent.");

        var options = configured.Value;
        var providerHealth = await gateway.GetHealthAsync(ct);
        var mediaHealth = await media.GetHealthAsync(ct);
        if (!options.Enabled || !options.PilotApproved)
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.Disabled, TypedFallback);
        if (!providerHealth.Available || !mediaHealth.Available)
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.Unavailable, TypedFallback);
        var now = Now();
        var alreadyActive = await db.SalesMeetingVoiceSessions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.MeetingSessionId == sessionId && x.StartedByUserId == userId && x.EndedUtc == null && x.ExpiresUtc > now, ct);
        if (alreadyActive)
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.Conflict, "This user already has an active voice session for the meeting.");

        var configuredDuration = TimeSpan.FromMinutes(Math.Clamp(options.MaximumSessionMinutes, 1, 120));
        var maximumDuration = meeting.RetentionUntilUtc < now.Add(configuredDuration)
            ? meeting.RetentionUntilUtc - now : configuredDuration;
        var voice = new SalesMeetingVoiceSession(Guid.NewGuid(), companyId, sessionId, request.AgentId, userId,
            mediaHealth.Route, now.Add(maximumDuration), now);
        db.SalesMeetingVoiceSessions.Add(voice);
        await db.SaveChangesAsync(ct);
        try
        {
            var connection = await gateway.CreateSessionAsync(new RealtimeAgentSessionCreateRequest(companyId, userId,
                request.AgentId, "sales_meeting_voice_pilot", request.OfferSdp, Instructions(), Tools(), maximumDuration,
                Normalize(correlationId)), ct);
            voice.Activate(connection.Provider, connection.ProviderSessionId, connection.Model, connection.MediaTransport,
                connection.ExpiresUtc < voice.ExpiresUtc ? connection.ExpiresUtc : voice.ExpiresUtc, Now());
            AddAudit(voice, userId, "sales.meeting_voice.started", AuditEventOutcomes.Succeeded,
                "The consented realtime voice pilot started through the approved media route.", correlationId);
            await SaveAsync(ct);
            SalesMeetingVoiceTelemetry.SessionsStarted.Add(1);
            return new SalesMeetingRealtimeStartResult(await ToStatusAsync(companyId, sessionId, voice, ct), connection.AnswerSdp);
        }
        catch (RealtimeAgentUnavailableException exception)
        {
            voice.Fail(exception.Code, exception.Message,
                exception.Code == "quota_exceeded" ? SalesMeetingVoiceSessionStatus.QuotaExceeded : SalesMeetingVoiceSessionStatus.Failed, Now());
            AddAudit(voice, userId, "sales.meeting_voice.start_failed", AuditEventOutcomes.Failed,
                "Realtime voice did not start; typed meeting controls remain available.", correlationId);
            await SaveAsync(CancellationToken.None);
            SalesMeetingVoiceTelemetry.SessionsFailed.Add(1);
            SalesMeetingVoiceTelemetry.Fallbacks.Add(1);
            throw new SalesMeetingRealtimeConflictException(exception.Code == "quota_exceeded"
                ? SalesMeetingRealtimeProblemCodes.QuotaExceeded : SalesMeetingRealtimeProblemCodes.Unavailable, exception.Message);
        }
    }

    public async Task<SalesMeetingRealtimeEventResult?> ProcessEventAsync(Guid companyId, Guid userId, Guid sessionId,
        SubmitSalesMeetingRealtimeEventRequest request, string? correlationId, CancellationToken ct)
    {
        EnsureIds(companyId, userId, sessionId, request.VoiceSessionId);
        if (string.IsNullOrWhiteSpace(request.EventId) || request.EventId.Length > 200 || request.Sequence < 1 ||
            string.IsNullOrWhiteSpace(request.PayloadJson) || request.PayloadJson.Length > 128_000)
            throw Validation(nameof(request.EventId), "A bounded realtime event ID, sequence, and payload are required.");
        await RequireMemberAsync(companyId, userId, ct);
        var voice = await db.SalesMeetingVoiceSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.MeetingSessionId == sessionId && x.Id == request.VoiceSessionId && x.StartedByUserId == userId, ct);
        if (voice is null) return null;
        var duplicate = await db.SalesMeetingVoiceEventReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.VoiceSessionId == voice.Id && x.ProviderEventId == request.EventId, ct);
        if (duplicate is not null)
        {
            SalesMeetingVoiceTelemetry.EventsDeduplicated.Add(1);
            return new(await ToStatusAsync(companyId, sessionId, voice, ct), true, false,
                duplicate.Outcome.ToStorageValue(), duplicate.QuestionId, duplicate.ResultJson);
        }
        if (voice.ProviderSessionId is null || voice.EndedUtc.HasValue || voice.ExpiresUtc <= Now())
            throw new SalesMeetingRealtimeConflictException(SalesMeetingRealtimeProblemCodes.Unavailable, TypedFallback);

        RealtimeAgentEvent normalized;
        try
        {
            normalized = await gateway.NormalizeEventAsync(new RealtimeAgentProviderEvent(voice.ProviderSessionId,
                request.EventId, request.Sequence, request.PayloadJson), ct);
        }
        catch (RealtimeAgentEventException exception)
        {
            throw Validation(nameof(request.PayloadJson), exception.Message);
        }
        var value = media.Normalize(normalized);
        var receipt = new SalesMeetingVoiceEventReceipt(Guid.NewGuid(), companyId, voice.Id, value.EventId,
            value.Sequence, value.Type, Now());
        if (value.Sequence <= voice.LastProviderSequence)
        {
            receipt.Complete(SalesMeetingVoiceEventOutcome.IgnoredReordered, null, null, "reordered");
            db.SalesMeetingVoiceEventReceipts.Add(receipt);
            await SaveAsync(ct);
            return new(await ToStatusAsync(companyId, sessionId, voice, ct), false, true,
                SalesMeetingVoiceEventOutcome.IgnoredReordered.ToStorageValue());
        }

        var options = configured.Value;
        if (voice.AudioDurationMilliseconds + value.AudioDurationMilliseconds > options.MaximumAudioSeconds * 1000 ||
            voice.InputTokens + value.InputTokens > options.MaximumInputTokens ||
            voice.OutputTokens + value.OutputTokens > options.MaximumOutputTokens)
        {
            voice.RecordEvent(value.Sequence, 0, 0, 0, Now());
            voice.Fail("usage_limit_exceeded", TypedFallback, SalesMeetingVoiceSessionStatus.QuotaExceeded, Now());
            receipt.Complete(SalesMeetingVoiceEventOutcome.Failed, null, null, "usage_limit_exceeded");
            db.SalesMeetingVoiceEventReceipts.Add(receipt);
            AddAudit(voice, userId, "sales.meeting_voice.limit_reached", AuditEventOutcomes.Blocked,
                "The configured voice usage limit was reached; typed meeting controls remain available.", correlationId);
            await SaveAsync(ct);
            await gateway.TerminateSessionAsync(voice.ProviderSessionId, ct);
            SalesMeetingVoiceTelemetry.Fallbacks.Add(1);
            return new(await ToStatusAsync(companyId, sessionId, voice, ct), false, false,
                SalesMeetingVoiceEventOutcome.Failed.ToStorageValue());
        }

        voice.RecordEvent(value.Sequence, value.AudioDurationMilliseconds, value.InputTokens, value.OutputTokens, Now());
        db.SalesMeetingVoiceEventReceipts.Add(receipt);
        Guid? questionId = null;
        string? toolResult = null;
        var outcome = SalesMeetingVoiceEventOutcome.Processed;
        string? reason = null;
        var meeting = await db.SalesMeetingSessions.SingleAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct);

        switch (value.Type)
        {
            case RealtimeAgentEventTypes.Connected:
                voice.MarkConnected(Now());
                break;
            case RealtimeAgentEventTypes.ParticipantSpeechStarted:
                if (meeting.Status == SalesMeetingSessionStatus.Presenting)
                    meeting.TransitionTo(SalesMeetingSessionStatus.Interrupted, meeting.CurrentSlideIndex,
                        meeting.CurrentTalkingPointIndex, Marker(meeting, value.EventId), "Participant interruption detected.", userId, Now());
                break;
            case RealtimeAgentEventTypes.ParticipantTranscriptCompleted:
                if (voice.MediaRoute != "teams_application_hosted" && !string.IsNullOrWhiteSpace(value.Text))
                    questionId = await AnswerAndResumeAsync(meeting, voice, value.EventId, value.Text, correlationId, ct);
                break;
            case RealtimeAgentEventTypes.ToolInvocation:
                (outcome, reason, questionId, toolResult) = await ExecuteToolAsync(meeting, voice, value, correlationId, ct);
                break;
            case RealtimeAgentEventTypes.Disconnected:
                voice.MarkReconnecting(options.MaximumReconnects, "The voice connection was interrupted; reconnect or continue with typed controls.", Now());
                break;
            case RealtimeAgentEventTypes.ProviderError:
                voice.Fail(value.ErrorCode ?? "provider_error", value.ErrorSummary ?? TypedFallback,
                    value.ErrorCode == "quota_exceeded" ? SalesMeetingVoiceSessionStatus.QuotaExceeded : SalesMeetingVoiceSessionStatus.Degraded, Now());
                AddAudit(voice, userId, "sales.meeting_voice.provider_failed", AuditEventOutcomes.Failed,
                    "The realtime provider degraded; typed meeting controls remain available.", correlationId);
                SalesMeetingVoiceTelemetry.Fallbacks.Add(1);
                break;
        }

        receipt.Complete(outcome, questionId, toolResult, reason);
        await SaveAsync(ct);
        SalesMeetingVoiceTelemetry.EventsProcessed.Add(1);
        return new(await ToStatusAsync(companyId, sessionId, voice, ct), false, false, outcome.ToStorageValue(), questionId, toolResult);
    }

    public async Task<SalesMeetingRealtimeCancelResult?> CancelResponseAsync(Guid companyId, Guid userId, Guid sessionId,
        CancelSalesMeetingRealtimeResponseRequest request, string? correlationId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, userId, ct);
        var voice = await FindVoiceAsync(companyId, userId, sessionId, request.VoiceSessionId, ct);
        if (voice is null) return null;
        if (voice.ProviderSessionId is null || voice.EndedUtc.HasValue)
            return new(await ToStatusAsync(companyId, sessionId, voice, ct), null);
        var result = await gateway.CancelResponseAsync(voice.ProviderSessionId, request.ResponseId, ct);
        AddAudit(voice, userId, "sales.meeting_voice.response_cancelled", AuditEventOutcomes.Succeeded,
            "The active voice response was cancelled for participant barge-in.", correlationId);
        await SaveAsync(ct);
        return new(await ToStatusAsync(companyId, sessionId, voice, ct), result.ClientEventJson);
    }

    public async Task<SalesMeetingRealtimeStatusDto?> StopAsync(Guid companyId, Guid userId, Guid sessionId, Guid voiceSessionId,
        StopSalesMeetingRealtimeRequest request, string? correlationId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, userId, ct);
        var voice = await FindVoiceAsync(companyId, userId, sessionId, voiceSessionId, ct);
        if (voice is null) return null;
        if (voice.EndedUtc.HasValue) return await ToStatusAsync(companyId, sessionId, voice, ct);
        if (voice.ProviderSessionId is not null) await gateway.TerminateSessionAsync(voice.ProviderSessionId, ct);
        try { voice.Stop(request.ExpectedVersion, string.IsNullOrWhiteSpace(request.Reason) ? "ended" : request.Reason, Now()); }
        catch (InvalidOperationException exception) { throw Conflict(exception.Message); }
        AddAudit(voice, userId, "sales.meeting_voice.stopped", AuditEventOutcomes.Succeeded,
            "Realtime voice stopped without affecting typed meeting workflows.", correlationId);
        await SaveAsync(ct);
        return await ToStatusAsync(companyId, sessionId, voice, ct);
    }

    public async Task<SalesMeetingRealtimeStatusDto?> RevokeConsentAsync(Guid companyId, Guid userId, Guid sessionId,
        Guid voiceSessionId, long expectedVersion, string? correlationId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, userId, ct);
        var voice = await FindVoiceAsync(companyId, userId, sessionId, voiceSessionId, ct);
        if (voice is null) return null;
        var meeting = await db.SalesMeetingSessions.SingleAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct);
        if (voice.ProviderSessionId is not null && !voice.EndedUtc.HasValue) await gateway.TerminateSessionAsync(voice.ProviderSessionId, ct);
        try { voice.RevokeConsent(expectedVersion, Now()); }
        catch (InvalidOperationException exception) { throw Conflict(exception.Message); }
        meeting.RevokeMediaConsent(userId, Now());
        if (outbox is not null)
        {
            var teamsCall = await db.TeamsMeetingCalls.SingleOrDefaultAsync(x => x.CompanyId == companyId &&
                x.MeetingSessionId == sessionId && x.State != TeamsMeetingCallStates.Ended &&
                x.State != TeamsMeetingCallStates.Rejected && x.State != TeamsMeetingCallStates.Failed, ct);
            if (teamsCall is not null)
            {
                var key = teamsCall.RequestLeave(Now(), forced: true);
                outbox.Enqueue(companyId, CompanyOutboxTopics.TeamsCallControlRequested,
                    new TeamsCallControlWorkItem(companyId, teamsCall.Id, teamsCall.ActionVersion, "terminate", correlationId),
                    correlationId, idempotencyKey: key, messageType: nameof(TeamsCallControlWorkItem));
            }
        }
        AddAudit(voice, userId, "sales.meeting_voice.consent_revoked", AuditEventOutcomes.Succeeded,
            "Meeting-media consent was revoked and realtime media handling stopped. Existing evidence remains governed by the meeting retention policy.", correlationId);
        await SaveAsync(ct);
        return await ToStatusAsync(companyId, sessionId, voice, ct);
    }

    private async Task<Guid?> AnswerAndResumeAsync(SalesMeetingSession meeting, SalesMeetingVoiceSession voice,
        string eventId, string questionText, string? correlationId, CancellationToken ct)
    {
        if (meeting.Status == SalesMeetingSessionStatus.Presenting)
        {
            meeting.TransitionTo(SalesMeetingSessionStatus.Interrupted, meeting.CurrentSlideIndex,
                meeting.CurrentTalkingPointIndex, Marker(meeting, eventId), "Participant question received.", voice.StartedByUserId, Now());
            await SaveAsync(ct);
        }
        if (meeting.Status == SalesMeetingSessionStatus.Interrupted)
        {
            meeting.TransitionTo(SalesMeetingSessionStatus.Answering, null, null, meeting.ResumeMarker,
                "Alex is answering from approved evidence.", voice.StartedByUserId, Now());
            await SaveAsync(ct);
        }
        var sequence = (await db.SalesMeetingQuestions.Where(x => x.CompanyId == meeting.CompanyId && x.SessionId == meeting.Id)
            .MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
        var result = await questions.AskAsync(meeting.CompanyId, voice.StartedByUserId, meeting.Id,
            new AskSalesMeetingQuestionRequest(DeterministicId(voice.Id, eventId), sequence, voice.AgentId,
                questionText, "participant", null, "voice"), correlationId, ct);
        if (meeting.Status == SalesMeetingSessionStatus.Answering)
        {
            meeting.TransitionTo(SalesMeetingSessionStatus.Resuming, null, null, meeting.ResumeMarker,
                "Grounded answer completed; restoring the exact presentation marker.", voice.StartedByUserId, Now());
            await SaveAsync(ct);
            meeting.TransitionTo(SalesMeetingSessionStatus.Presenting, null, null, meeting.ResumeMarker,
                "Presentation resumed after the grounded answer.", voice.StartedByUserId, Now());
            await SaveAsync(ct);
        }
        return result?.Id;
    }

    private async Task<(SalesMeetingVoiceEventOutcome Outcome, string? Reason, Guid? QuestionId, string? ResultJson)> ExecuteToolAsync(
        SalesMeetingSession meeting, SalesMeetingVoiceSession voice, MeetingMediaEvent value, string? correlationId, CancellationToken ct)
    {
        var teamsToolAllowed = voice.MediaRoute != "teams_application_hosted" ||
            (teamsPresenters is not null && (await teamsPresenters.ResolveAsync(meeting.CompanyId, meeting.Id, ct)).Tools.Any(t => t.Name == value.ToolName));
        if (!teamsToolAllowed || string.IsNullOrWhiteSpace(value.ToolName) || !SalesMeetingRealtimeToolNames.Allowed.Contains(value.ToolName))
        {
            SalesMeetingVoiceTelemetry.ToolsRejected.Add(1);
            AddAudit(voice, voice.StartedByUserId, "sales.meeting_voice.tool_rejected", AuditEventOutcomes.Blocked,
                "A realtime tool outside the explicit read/recommend allowlist was rejected.", correlationId);
            return (SalesMeetingVoiceEventOutcome.ToolRejected, "tool_not_allowed", null,
                JsonSerializer.Serialize(new { error = "tool_not_allowed" }, Json));
        }
        if (value.ToolName == SalesPresentationToolNames.GetCurrentSlide)
        {
            var snapshot = await presentation.GetCurrentAsync(meeting.CompanyId, voice.StartedByUserId, meeting.Id, ct);
            return (SalesMeetingVoiceEventOutcome.Processed, null, null, JsonSerializer.Serialize(new
            {
                slideIndex = snapshot?.Stage.SlideNumber ?? meeting.CurrentSlideIndex,
                talkingPointIndex = snapshot?.Private.TalkingPointIndex ?? meeting.CurrentTalkingPointIndex,
                resumeMarker = snapshot?.Private.ResumeMarker ?? meeting.ResumeMarker,
                title = snapshot?.Stage.SlideTitle,
                text = snapshot?.Stage.SlideText
            }, Json));
        }
        if (value.ToolName == SalesPresentationToolNames.SearchSlides)
        {
            var search = ReadStringArgument(value.ToolArgumentsJson, "query", 200);
            if (search is null)
                return (SalesMeetingVoiceEventOutcome.ToolRejected, "invalid_tool_arguments", null,
                    JsonSerializer.Serialize(new { error = "invalid_tool_arguments" }, Json));
            var matches = await presentation.SearchAsync(meeting.CompanyId, voice.StartedByUserId, meeting.Id, search, ct);
            return (SalesMeetingVoiceEventOutcome.Processed, null, null, JsonSerializer.Serialize(new { matches }, Json));
        }
        if (SalesPresentationToolNames.IsMutation(value.ToolName))
        {
            if (conductor is null)
                return (SalesMeetingVoiceEventOutcome.ToolRejected, "presentation_conductor_unavailable", null,
                    JsonSerializer.Serialize(new { error = "presentation_conductor_unavailable" }, Json));
            var slideNumber = ReadIntArgument(value.ToolArgumentsJson, "slideNumber");
            var talkingPointIndex = ReadIntArgument(value.ToolArgumentsJson, "talkingPointIndex");
            var resumeMarker = ReadStringArgument(value.ToolArgumentsJson, "resumeMarker", 1000);
            var plan = await conductor.PrepareAsync(new SalesPresentationNarrationRequest(
                meeting.CompanyId, voice.StartedByUserId, meeting.Id, value.ToolName, slideNumber,
                talkingPointIndex, resumeMarker, null, correlationId, voice.AgentId), ct);
            if (plan is null)
                return (SalesMeetingVoiceEventOutcome.ToolRejected, "presentation_unavailable", null,
                    JsonSerializer.Serialize(new { error = "presentation_unavailable" }, Json));
            var accepted = plan.Disposition is "ready" or "recommended" or "discussion";
            return (accepted ? SalesMeetingVoiceEventOutcome.Processed : SalesMeetingVoiceEventOutcome.ToolRejected,
                plan.ReasonCode, null, JsonSerializer.Serialize(plan, Json));
        }
        string? question = null;
        try
        {
            using var document = JsonDocument.Parse(value.ToolArgumentsJson ?? "{}");
            if (document.RootElement.TryGetProperty("question", out var property) && property.ValueKind == JsonValueKind.String)
                question = property.GetString();
        }
        catch (JsonException) { }
        if (string.IsNullOrWhiteSpace(question) || question.Length > 2000)
            return (SalesMeetingVoiceEventOutcome.ToolRejected, "invalid_tool_arguments", null,
                JsonSerializer.Serialize(new { error = "invalid_tool_arguments" }, Json));
        var id = await AnswerAndResumeAsync(meeting, voice, value.ToolCallId ?? value.EventId, question, correlationId, ct);
        return (SalesMeetingVoiceEventOutcome.Processed, null, id,
            JsonSerializer.Serialize(new { questionId = id, status = "captured_once" }, Json));
    }

    private async Task<SalesMeetingRealtimeStatusDto> ToStatusAsync(Guid companyId, Guid sessionId,
        SalesMeetingVoiceSession? voice, CancellationToken ct)
    {
        var provider = await gateway.GetHealthAsync(ct);
        var route = await media.GetHealthAsync(ct);
        var meeting = await db.SalesMeetingSessions.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == sessionId)
            .Select(x => new { x.ConsentStatus, x.RetentionUntilUtc }).SingleAsync(ct);
        var now = Now();
        var expired = voice is { EndedUtc: null } && voice.ExpiresUtc <= now;
        var sessionAvailable = !expired && (voice is null || voice.Status is SalesMeetingVoiceSessionStatus.Connecting or
            SalesMeetingVoiceSessionStatus.Active or SalesMeetingVoiceSessionStatus.Reconnecting);
        var available = provider.Available && route.Available && meeting.ConsentStatus == SalesMeetingConsentStatus.Granted &&
                        meeting.RetentionUntilUtc > now && sessionAvailable;
        return new SalesMeetingRealtimeStatusDto(voice?.Id, sessionId, configured.Value.Enabled,
            meeting.ConsentStatus == SalesMeetingConsentStatus.Granted, provider.Configured, route.Approved, available,
            expired ? "expired" : voice?.Status.ToStorageValue() ?? (available ? "ready" : "degraded"),
            available ? "Voice pilot is ready; typed workflows remain available." : TypedFallback,
            voice?.Provider ?? provider.Provider, voice?.Model ?? provider.Model, voice?.MediaRoute ?? route.Route,
            voice?.ExpiresUtc, voice?.ReconnectCount ?? 0, (voice?.AudioDurationMilliseconds ?? 0) / 1000,
            voice?.InputTokens ?? 0, voice?.OutputTokens ?? 0, voice?.LastErrorCode,
            voice?.LastErrorSummary ?? (available ? null : provider.Message ?? route.Message), voice?.ConcurrencyVersion ?? 0);
    }

    private Task<SalesMeetingVoiceSession?> FindVoiceAsync(Guid companyId, Guid userId, Guid sessionId, Guid voiceSessionId, CancellationToken ct) =>
        db.SalesMeetingVoiceSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MeetingSessionId == sessionId &&
            x.Id == voiceSessionId && x.StartedByUserId == userId, ct);

    private async Task RequireMemberAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        EnsureIds(companyId, userId);
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId &&
            x.Status == CompanyMembershipStatus.Active, ct)) throw new UnauthorizedAccessException("An active company membership is required.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict("The voice or meeting state changed after it was opened. Refresh before retrying."); }
        catch (DbUpdateException exception)
        {
            logger.LogInformation(exception, "A realtime event lost an idempotency or ordering race.");
            throw Conflict("The realtime event was already accepted or arrived out of order. Refresh the voice status.");
        }
    }

    private void AddAudit(SalesMeetingVoiceSession voice, Guid userId, string action, string outcome, string rationale, string? correlationId) =>
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), voice.CompanyId, AuditActorTypes.User, userId, action,
            "sales_meeting_voice_session", voice.Id.ToString("D"), outcome, rationale,
            ["meeting consent", "realtime provider health"], new Dictionary<string, string?>
            {
                ["meetingSessionId"] = voice.MeetingSessionId.ToString("D"), ["provider"] = voice.Provider,
                ["mediaRoute"] = voice.MediaRoute, ["status"] = voice.Status.ToStorageValue()
            }, Normalize(correlationId), Now()));

    internal static IReadOnlyList<RealtimeAgentToolDefinition> Tools() =>
    [
        new(SalesPresentationToolNames.GetCurrentSlide, "Read the current authoritative customer-visible slide and exact resume position.",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}", "read"),
        new(SalesPresentationToolNames.SearchSlides, "Search customer-visible slide titles and text. Ask for confirmation unless exactly one slide matches.",
            "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}},\"required\":[\"query\"],\"additionalProperties\":false}", "read"),
        new(SalesPresentationToolNames.Next, "Move to the next slide only when organizer-approved autonomous mode permits it.", MutationSchema(false), "execute"),
        new(SalesPresentationToolNames.Previous, "Move to the previous slide only when organizer-approved autonomous mode permits it.", MutationSchema(false), "execute"),
        new(SalesPresentationToolNames.Goto, "Move to one explicit slide number only when organizer-approved autonomous mode permits it.", MutationSchema(true), "execute"),
        new(SalesPresentationToolNames.Pause, "Pause narration at the exact slide and talking-point marker.", MutationSchema(false), "execute"),
        new(SalesPresentationToolNames.Resume, "Resume narration from the persisted marker after authoritative state is restored.", MutationSchema(false), "execute"),
        new(SalesMeetingRealtimeToolNames.AskGroundedQuestion, "Answer one participant question from approved company and meeting evidence and capture it once.",
            "{\"type\":\"object\",\"properties\":{\"question\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":2000}},\"required\":[\"question\"],\"additionalProperties\":false}", "recommend")
    ];

    internal static string Instructions() =>
        "You are Alex, the company's Sales meeting sidekick. Speak concisely. Use only the supplied meeting state and exact presentation.* tool names; never invent JavaScript calls such as nextSlide(). " +
        "For factual product, pricing, policy, customer, promise, discount, or contractual questions, call ask_grounded_question and use its result. " +
        "Never invent facts, execute changes, make commitments, modify sales records, send messages, or bypass review, approval, and outbox controls. " +
        "When interrupted, stop speaking immediately. Typed and host-mediated controls are always the fallback.";

    private static string MutationSchema(bool requireSlide) => requireSlide
        ? "{\"type\":\"object\",\"properties\":{\"slideNumber\":{\"type\":\"integer\",\"minimum\":1},\"talkingPointIndex\":{\"type\":\"integer\",\"minimum\":0},\"resumeMarker\":{\"type\":\"string\",\"maxLength\":1000}},\"required\":[\"slideNumber\"],\"additionalProperties\":false}"
        : "{\"type\":\"object\",\"properties\":{\"talkingPointIndex\":{\"type\":\"integer\",\"minimum\":0},\"resumeMarker\":{\"type\":\"string\",\"maxLength\":1000}},\"additionalProperties\":false}";

    private static string? ReadStringArgument(string? json, string name, int maximumLength)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "{}");
            if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(text) || text.Length > maximumLength ? null : text;
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static int? ReadIntArgument(string? json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "{}");
            return document.RootElement.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
        }
        catch (JsonException) { return null; }
    }

    private static Guid DeterministicId(Guid voiceId, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{voiceId:N}:{value}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static string Marker(SalesMeetingSession meeting, string eventId) =>
        $"voice:{meeting.CurrentSlideIndex}:{meeting.CurrentTalkingPointIndex}:{eventId}"[..Math.Min(1000, $"voice:{meeting.CurrentSlideIndex}:{meeting.CurrentTalkingPointIndex}:{eventId}".Length)];
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(128, value.Trim().Length)];
    private static void EnsureIds(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Required identifiers cannot be empty."); }
    private static SalesMeetingRealtimeValidationException Validation(string field, string message) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] });
    private static SalesMeetingRealtimeConflictException Conflict(string message) => new(SalesMeetingRealtimeProblemCodes.Conflict, message);
}
