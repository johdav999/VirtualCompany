using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingRealtimeServiceTests
{
    [Fact]
    public async Task Disabled_voice_reports_degraded_without_disabling_typed_workflows()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: false);

        var status = await fixture.Service.GetStatusAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, CancellationToken.None);

        Assert.False(status!.VoiceAvailable);
        Assert.Equal("degraded", status.State);
        Assert.Contains("Typed questions", status.DegradedStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starting_voice_requires_explicit_consent()
    {
        await using var fixture = await Fixture.CreateAsync(consent: SalesMeetingConsentStatus.Pending);

        var exception = await Assert.ThrowsAsync<SalesMeetingRealtimeConflictException>(() => fixture.Service.StartAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, new(fixture.AgentId, "offer"), null, CancellationToken.None));

        Assert.Equal(SalesMeetingRealtimeProblemCodes.ConsentRequired, exception.Code);
        Assert.Empty(await fixture.Db.SalesMeetingVoiceSessions.ToListAsync());
    }

    [Fact]
    public async Task Interruption_is_persisted_answered_once_and_resumes_exact_position()
    {
        await using var fixture = await Fixture.CreateAsync(presenting: true);
        var start = await fixture.StartAsync();

        await fixture.EventAsync(start.Status.VoiceSessionId!.Value, "speech-1", 1, new { type = "speech_started" });
        var interrupted = await fixture.Db.SalesMeetingSessions.SingleAsync();
        Assert.Equal(SalesMeetingSessionStatus.Interrupted, interrupted.Status);
        Assert.Equal(3, interrupted.CurrentSlideIndex);
        Assert.Equal(2, interrupted.CurrentTalkingPointIndex);
        Assert.Equal("voice:3:2:speech-1", interrupted.ResumeMarker);

        var answered = await fixture.EventAsync(start.Status.VoiceSessionId.Value, "question-1", 2,
            new { type = "transcript", text = "What is the approved price?" });
        var duplicate = await fixture.EventAsync(start.Status.VoiceSessionId.Value, "question-1", 2,
            new { type = "transcript", text = "What is the approved price?" });
        var resumed = await fixture.Db.SalesMeetingSessions.SingleAsync();

        Assert.NotNull(answered.QuestionId);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(1, fixture.Questions.AskCount);
        Assert.Equal(SalesMeetingSessionStatus.Presenting, resumed.Status);
        Assert.Equal("voice:3:2:speech-1", resumed.ResumeMarker);
        Assert.Equal(2, await fixture.Db.SalesMeetingVoiceEventReceipts.CountAsync());
    }

    [Fact]
    public async Task Unsupported_tools_and_reordered_events_have_no_side_effects()
    {
        await using var fixture = await Fixture.CreateAsync();
        var start = await fixture.StartAsync();
        var rejected = await fixture.EventAsync(start.Status.VoiceSessionId!.Value, "tool-2", 2,
            new { type = "tool", name = "execute_discount", arguments = "{}" });
        var reordered = await fixture.EventAsync(start.Status.VoiceSessionId.Value, "late-1", 1,
            new { type = "speech_started" });

        Assert.Equal("tool_rejected", rejected.Outcome);
        Assert.Contains("tool_not_allowed", rejected.ToolResultJson, StringComparison.Ordinal);
        Assert.True(reordered.IgnoredAsReordered);
        Assert.Equal(0, fixture.Questions.AskCount);
    }

    [Fact]
    public async Task Usage_limit_terminates_voice_and_returns_immediate_typed_fallback()
    {
        await using var fixture = await Fixture.CreateAsync();
        var start = await fixture.StartAsync();

        var limited = await fixture.EventAsync(start.Status.VoiceSessionId!.Value, "usage-1", 1,
            new { type = "usage", inputTokens = 50001 });

        Assert.Equal("quota_exceeded", limited.Status.State);
        Assert.False(limited.Status.VoiceAvailable && limited.Status.State == "active");
        Assert.Contains("Typed", limited.Status.DegradedStatus, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Gateway.TerminateCount);
    }

    [Fact]
    public async Task Revoking_consent_stops_provider_and_preserves_retention_boundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var retentionUntil = (await fixture.Db.SalesMeetingSessions.AsNoTracking().SingleAsync()).RetentionUntilUtc;
        var start = await fixture.StartAsync();

        var revoked = await fixture.Service.RevokeConsentAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            start.Status.VoiceSessionId!.Value, start.Status.Version, "corr", CancellationToken.None);
        var meeting = await fixture.Db.SalesMeetingSessions.AsNoTracking().SingleAsync();

        Assert.Equal("consent_revoked", revoked!.State);
        Assert.Equal(SalesMeetingConsentStatus.Revoked, meeting.ConsentStatus);
        Assert.Equal(retentionUntil, meeting.RetentionUntilUtc);
        Assert.Equal(1, fixture.Gateway.TerminateCount);
    }

    [Fact]
    public async Task Cross_tenant_user_cannot_discover_voice_status()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetStatusAsync(
            fixture.CompanyId, Guid.NewGuid(), fixture.SessionId, CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, Guid companyId, Guid userId, Guid sessionId, Guid agentId,
            SalesMeetingRealtimeService service, FakeGateway gateway, FakeQuestions questions)
        {
            Db = db; CompanyId = companyId; UserId = userId; SessionId = sessionId; AgentId = agentId;
            Service = service; Gateway = gateway; Questions = questions;
        }

        public VirtualCompanyDbContext Db { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid SessionId { get; }
        public Guid AgentId { get; }
        public SalesMeetingRealtimeService Service { get; }
        public FakeGateway Gateway { get; }
        public FakeQuestions Questions { get; }

        public static async Task<Fixture> CreateAsync(bool enabled = true,
            SalesMeetingConsentStatus consent = SalesMeetingConsentStatus.Granted, bool presenting = false)
        {
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var sessionId = Guid.NewGuid();
            var agentId = Guid.NewGuid(); var customerId = Guid.NewGuid(); var now = DateTime.UtcNow;
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase($"meeting-voice-{Guid.NewGuid():N}").Options, new TestContext(companyId, userId));
            db.Companies.Add(new Company(companyId, "Voice Company"));
            db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.CustomerCompanies.Add(new CustomerCompany(customerId, companyId, "Customer"));
            db.Agents.Add(new Agent(agentId, companyId, "alex", "Alex", "Sales Manager", "Sales", null,
                AgentSeniority.Senior, AgentStatus.Active));
            var meeting = new SalesMeetingSession(sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null,
                customerId, "Confirm fit", "Operations leaders", 30, null, "provider", consent,
                SalesMeetingRetentionPolicy.Standard, 365, now, userId, now);
            if (presenting)
                meeting.TransitionTo(SalesMeetingSessionStatus.Presenting, 3, 2, null, null, userId, now.AddSeconds(1));
            db.SalesMeetingSessions.Add(meeting);
            await db.SaveChangesAsync();
            var options = Options.Create(new SalesMeetingVoiceOptions
            {
                Enabled = enabled, PilotApproved = enabled, MediaRoute = "browser_webrtc",
                MaximumSessionMinutes = 30, MaximumReconnects = 2, MaximumAudioSeconds = 1800,
                MaximumInputTokens = 50_000, MaximumOutputTokens = 10_000
            });
            var gateway = new FakeGateway(enabled);
            var questions = new FakeQuestions();
            var service = new SalesMeetingRealtimeService(db, gateway, new ConfiguredMeetingMediaAdapter(options),
                questions, new FakePresentation(), options, TimeProvider.System, NullLogger<SalesMeetingRealtimeService>.Instance);
            return new(db, companyId, userId, sessionId, agentId, service, gateway, questions);
        }

        public Task<SalesMeetingRealtimeStartResult> StartAsync() => Service.StartAsync(CompanyId, UserId, SessionId,
            new(AgentId, "offer-sdp"), "corr", CancellationToken.None)!;

        public Task<SalesMeetingRealtimeEventResult> EventAsync(Guid voiceSessionId, string eventId, long sequence, object payload) =>
            Service.ProcessEventAsync(CompanyId, UserId, SessionId,
                new(voiceSessionId, eventId, sequence, JsonSerializer.Serialize(payload)), "corr", CancellationToken.None)!;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeGateway(bool available) : IRealtimeAgentSessionGateway
    {
        public int TerminateCount { get; private set; }
        public Task<RealtimeAgentHealth> GetHealthAsync(CancellationToken cancellationToken) => Task.FromResult(
            new RealtimeAgentHealth(available, available, available, "test", "test-realtime", available ? "available" : "degraded"));
        public Task<RealtimeAgentSessionConnection> CreateSessionAsync(RealtimeAgentSessionCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new RealtimeAgentSessionConnection("test", "call_test", "test-realtime", "webrtc", "answer-sdp", DateTime.UtcNow.AddMinutes(30)));
        public Task<RealtimeAgentEvent> NormalizeEventAsync(RealtimeAgentProviderEvent value, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(value.PayloadJson);
            var type = document.RootElement.GetProperty("type").GetString();
            var result = type switch
            {
                "speech_started" => new RealtimeAgentEvent(value.EventId, value.Sequence, RealtimeAgentEventTypes.ParticipantSpeechStarted),
                "transcript" => new RealtimeAgentEvent(value.EventId, value.Sequence, RealtimeAgentEventTypes.ParticipantTranscriptCompleted,
                    document.RootElement.GetProperty("text").GetString()),
                "tool" => new RealtimeAgentEvent(value.EventId, value.Sequence, RealtimeAgentEventTypes.ToolInvocation,
                    ToolCallId: value.EventId, ToolName: document.RootElement.GetProperty("name").GetString(),
                    ToolArgumentsJson: document.RootElement.GetProperty("arguments").GetString()),
                "usage" => new RealtimeAgentEvent(value.EventId, value.Sequence, RealtimeAgentEventTypes.UsageUpdated,
                    InputTokens: document.RootElement.GetProperty("inputTokens").GetInt32()),
                _ => new RealtimeAgentEvent(value.EventId, value.Sequence, RealtimeAgentEventTypes.Connected)
            };
            return Task.FromResult(result);
        }
        public Task<RealtimeAgentControlResult> CancelResponseAsync(string providerSessionId, string? responseId, CancellationToken cancellationToken) =>
            Task.FromResult(new RealtimeAgentControlResult(true, "{\"type\":\"response.cancel\"}"));
        public Task TerminateSessionAsync(string providerSessionId, CancellationToken cancellationToken) { TerminateCount++; return Task.CompletedTask; }
    }

    private sealed class FakeQuestions : ISalesMeetingQuestionAnsweringService
    {
        public int AskCount { get; private set; }
        public Task<IReadOnlyList<SalesMeetingQuestionDto>> ListQuestionsAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SalesMeetingQuestionDto>>([]);
        public Task<IReadOnlyList<SalesMeetingStageAnswerDto>> ListStageAnswersAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SalesMeetingStageAnswerDto>>([]);
        public Task<SalesMeetingQuestionDto?> GetQuestionAsync(Guid companyId, Guid userId, Guid sessionId, Guid questionId, CancellationToken cancellationToken) => Task.FromResult<SalesMeetingQuestionDto?>(null);
        public Task<SalesMeetingQuestionDto?> AskAsync(Guid companyId, Guid userId, Guid sessionId, AskSalesMeetingQuestionRequest request, string? correlationId, CancellationToken cancellationToken)
        {
            AskCount++;
            var now = DateTime.UtcNow; var id = Guid.NewGuid();
            return Task.FromResult<SalesMeetingQuestionDto?>(new(id, request.ClientQuestionId, request.Sequence, request.AgentId,
                request.Question, "Grounded answer", request.AskerType, request.AskerLabel, request.InputSource, null, 1,
                "completed", .9m, false, "verified", "private", Guid.NewGuid(), null, null, now, now, null, now, 1, []));
        }
        public Task<SalesMeetingQuestionDto?> ApproveForStageAsync(Guid companyId, Guid userId, Guid sessionId, Guid questionId, long expectedVersion, string? correlationId, CancellationToken cancellationToken) => Task.FromResult<SalesMeetingQuestionDto?>(null);
    }

    private sealed class FakePresentation : ISalesPresentationRuntimeService
    {
        public Task<SalesPresentationAuthoritativeSnapshotDto?> GetCurrentAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesPresentationAuthoritativeSnapshotDto?>(null);
        public Task<IReadOnlyList<SalesPresentationSearchResultDto>> SearchAsync(Guid companyId, Guid userId, Guid sessionId, string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SalesPresentationSearchResultDto>>([]);
        public Task<SalesPresentationCommandResultDto?> ExecuteAsync(Guid companyId, Guid userId, Guid sessionId, string toolName, SalesPresentationCommandRequest request, string? correlationId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesPresentationCommandResultDto?>(null);
        public Task RecordReconnectAsync(Guid companyId, Guid userId, Guid sessionId, string surface, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestContext(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}
