using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingClosingServiceTests
{
    [Fact]
    public async Task Prepare_persists_separate_customer_and_internal_artifacts_without_leakage_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.Request(proposedChanges: ["Raise probability to 80%"]);

        var first = await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, request, "corr", default);
        var retry = await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, request, "corr", default);

        Assert.NotNull(first); Assert.NotNull(retry);
        Assert.Equal(first!.CustomerMinutes.Id, retry!.CustomerMinutes.Id);
        Assert.Contains(first.CustomerMinutes.Items, x => x.Content.Contains("Agreed next step", StringComparison.Ordinal));
        Assert.Contains(first.CustomerMinutes.Items, x => x.Content.Contains("Send implementation plan", StringComparison.Ordinal));
        Assert.DoesNotContain(first.CustomerMinutes.Items, x => x.Content.Contains("Private objection", StringComparison.Ordinal));
        Assert.DoesNotContain(first.CustomerMinutes.Items, x => x.Type == "proposed_deal_change");
        Assert.Contains(first.InternalIntelligence.Items, x => x.Content.Contains("Private objection", StringComparison.Ordinal));
        Assert.Contains(first.InternalIntelligence.Items, x => x.Type == "proposed_deal_change");
        var customerJson = JsonSerializer.Serialize(first.CustomerMinutes);
        Assert.DoesNotContain("Private objection", customerJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidence", customerJson, StringComparison.Ordinal);
        Assert.Equal(1, await fixture.Db.SalesMeetingMinutes.CountAsync());
        Assert.Equal(1, await fixture.Db.SalesMeetingInternalIntelligence.CountAsync());
        Assert.Equal(2, fixture.Reasoning.CallCount);
    }

    [Fact]
    public async Task Approved_history_is_preserved_when_a_new_version_is_generated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = (await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, fixture.Request(), null, default))!;
        var submitted = (await fixture.Service.SubmitMinutesAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            first.CustomerMinutes.Id, first.CustomerMinutes.Version, null, default))!;
        var approved = (await fixture.Service.ApproveMinutesAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            submitted.Id, submitted.Version, null, default))!;

        var second = (await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            fixture.Request(Guid.NewGuid()), null, default))!;
        var storedFirst = await fixture.Db.SalesMeetingMinutes.AsNoTracking().SingleAsync(x => x.Id == approved.Id);

        Assert.Equal(2, second.CustomerMinutes.ArtifactVersion);
        Assert.Equal(approved.Id, second.CustomerMinutes.PreviousVersionId);
        Assert.Equal(SalesMeetingClosingArtifactStatus.Approved, storedFirst.Status);
    }

    [Fact]
    public async Task Unsupported_product_statement_stays_reviewable_and_cannot_be_approved()
    {
        await using var fixture = await Fixture.CreateAsync();
        var closing = (await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, fixture.Request(), null, default))!;
        var edited = (await fixture.Service.EditMinutesAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            closing.CustomerMinutes.Id, new(closing.CustomerMinutes.Version,
            [new(0, "approved_product_statement", "Unlimited discount is guaranteed.", null, null, "host-claim", null, true)]), null, default))!;
        var submitted = (await fixture.Service.SubmitMinutesAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            edited.Id, edited.Version, null, default))!;

        var exception = await Assert.ThrowsAsync<SalesMeetingClosingConflictException>(() => fixture.Service.ApproveMinutesAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, submitted.Id, submitted.Version, null, default));
        Assert.Contains("Unsupported product statements", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completion_rejects_a_stale_capture_snapshot_and_succeeds_after_regeneration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = (await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, fixture.Request(), null, default))!;
        var batchId = Guid.NewGuid(); fixture.Session.ApplyCaptureBatch(batchId, 0, fixture.UserId, DateTime.UtcNow); await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<SalesMeetingClosingConflictException>(() => fixture.Service.CompleteAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(fixture.Session.ConcurrencyVersion, 0, null, first.CustomerMinutes.Id, 1, first.InternalIntelligence.Id, 1), null, default));

        var second = (await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            fixture.Request(Guid.NewGuid(), fixture.Session.CaptureVersion, batchId), null, default))!;
        var completed = await fixture.Service.CompleteAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(fixture.Session.ConcurrencyVersion, fixture.Session.CaptureVersion, batchId, second.CustomerMinutes.Id,
                second.CustomerMinutes.ArtifactVersion, second.InternalIntelligence.Id, second.InternalIntelligence.ArtifactVersion), null, default);

        Assert.Equal("completed", completed!.Status);
    }

    [Fact]
    public async Task Generated_wording_with_any_unapproved_source_is_not_used()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Reasoning.ResultFactory = request =>
        {
            var allowed = request.Sources.First().Id;
            return new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "Unsafe",
                [new("Private strategy leak", request.Sources.First().Type, .99m, [allowed, "outside-source"])],
                .99m, [], [], [], [allowed, "outside-source"]);
        };

        var closing = await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            fixture.Request(), null, default);

        Assert.DoesNotContain(closing!.CustomerMinutes.Items,
            x => x.Content.Contains("Private strategy leak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Internal_intelligence_requires_internal_sales_access_beyond_customer_minutes_access()
    {
        await using var fixture = await Fixture.CreateAsync();
        var closing = (await fixture.Service.PrepareAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            fixture.Request(), null, default))!;
        var accountantId = Guid.NewGuid();
        fixture.Db.Users.Add(new User(accountantId, "accountant@example.com", "Accountant", "test", accountantId.ToString("N")));
        fixture.Db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), fixture.CompanyId, accountantId,
            CompanyMembershipRole.Accountant, CompanyMembershipStatus.Active));
        await fixture.Db.SaveChangesAsync();

        Assert.NotNull(await fixture.Service.GetMinutesAsync(fixture.CompanyId, accountantId, fixture.SessionId,
            closing.CustomerMinutes.Id, default));
        var error = await Assert.ThrowsAsync<SalesMeetingClosingConflictException>(() => fixture.Service.GetInternalAsync(
            fixture.CompanyId, accountantId, fixture.SessionId, closing.InternalIntelligence.Id, default));
        Assert.Equal(SalesMeetingClosingProblemCodes.InternalAccessDenied, error.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, Guid companyId, Guid userId, Guid sessionId, Guid agentId, SalesMeetingSession session, RecordingReasoning reasoning)
        { Db = db; CompanyId = companyId; UserId = userId; SessionId = sessionId; AgentId = agentId; Session = session; Reasoning = reasoning; Service = new(db, reasoning, new AllowingAuthority(companyId, agentId), TimeProvider.System, NullLogger<SalesMeetingClosingService>.Instance); }
        public VirtualCompanyDbContext Db { get; } public SalesMeetingClosingService Service { get; } public RecordingReasoning Reasoning { get; }
        public Guid CompanyId { get; } public Guid UserId { get; } public Guid SessionId { get; } public Guid AgentId { get; } public SalesMeetingSession Session { get; }
        public PrepareSalesMeetingClosingRequest Request(Guid? requestId = null, long captureVersion = 0, Guid? checkpoint = null, IReadOnlyList<string>? proposedChanges = null) => new(requestId ?? Guid.NewGuid(), AgentId, Session.ConcurrencyVersion, captureVersion, checkpoint, DateTime.UtcNow.AddDays(7), proposedChanges);
        public static async Task<Fixture> CreateAsync()
        {
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var agentId = Guid.NewGuid(); var customerId = Guid.NewGuid(); var now = DateTime.UtcNow;
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"meeting-closing-{Guid.NewGuid():N}").Options, new TestContext(companyId, userId));
            db.Companies.Add(new Company(companyId, "Closing Company")); db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active)); db.CustomerCompanies.Add(new CustomerCompany(customerId, companyId, "Customer"));
            db.Agents.Add(new Agent(agentId, companyId, "alex-closing", "Alex", "Sales Manager", "Sales", null, AgentSeniority.Senior, AgentStatus.Active));
            var session = new SalesMeetingSession(sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null, customerId, "Close", "Buyer", 30, null, "provider", SalesMeetingConsentStatus.Pending, SalesMeetingRetentionPolicy.Standard, 365, now, userId, now);
            session.TransitionTo(SalesMeetingSessionStatus.Closing, null, null, null, null, userId, now); db.SalesMeetingSessions.Add(session);
            db.SalesMeetingObservations.AddRange(
                new SalesMeetingObservation(Guid.NewGuid(), companyId, sessionId, Guid.NewGuid(), 1, SalesMeetingObservationCategory.Commitment, "Agreed next step", .9m, "typed", SalesMeetingReviewState.Reviewed, userId, Guid.NewGuid(), now),
                new SalesMeetingObservation(Guid.NewGuid(), companyId, sessionId, Guid.NewGuid(), 2, SalesMeetingObservationCategory.Objection, "Private objection about procurement", .8m, "typed", SalesMeetingReviewState.Reviewed, userId, Guid.NewGuid(), now),
                new SalesMeetingObservation(Guid.NewGuid(), companyId, sessionId, Guid.NewGuid(), 3, SalesMeetingObservationCategory.BuyingSignal, "Asked for rollout dates", .7m, "typed", SalesMeetingReviewState.Unreviewed, userId, Guid.NewGuid(), now));
            db.SalesMeetingActionItems.Add(new SalesMeetingActionItem(Guid.NewGuid(), companyId, sessionId, Guid.NewGuid(), 1, "Send implementation plan", null, "Alex", now.AddDays(2), SalesMeetingActionItemStatus.Open, .9m, "typed", SalesMeetingReviewState.Reviewed, userId, Guid.NewGuid(), now));
            await db.SaveChangesAsync(); return new(db, companyId, userId, sessionId, agentId, session, new RecordingReasoning());
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class RecordingReasoning : IAgentReasoningGateway
    {
        public int CallCount { get; private set; }
        public Func<AgentReasoningRequest, AgentReasoningResult>? ResultFactory { get; set; }
        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken) { CallCount++; return Task.FromResult(ResultFactory?.Invoke(request) ?? new AgentReasoningResult(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "Grounded", [], .9m, [], [], [], request.Sources.Select(x => x.Id).ToArray())); }
        public Task<AgentReasoningResult?> GetRunAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken) => Task.FromResult<AgentReasoningResult?>(null);
    }
    private sealed class AllowingAuthority(Guid companyId, Guid agentId) : IAgentEffectiveAuthorityResolver
    {
        public Task<AgentEffectiveAuthorityDto> ResolveAsync(Guid requestedCompanyId, Guid requestedAgentId, CancellationToken cancellationToken) => Task.FromResult(new AgentEffectiveAuthorityDto(companyId, agentId, "Alex", "Sales", "active", true, "level_0", "v1", "hash", [], [], [Tool(SalesMeetingClosingToolNames.ReadEvidence, "read"), Tool(SalesMeetingClosingToolNames.GenerateSummary, "recommend")], DateTime.UtcNow));
        private static EffectiveAgentToolAuthorityDto Tool(string name, string action) => new(name, "1.0.0", action, "sales", AgentCapabilityStates.Available, AgentAuthorityReasonCodes.Available, "Allowed", "configured", "v1", [], []);
    }
    private sealed class TestContext(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId; public Guid? UserId { get; private set; } = userId; public bool IsResolved => true; public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value; public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}
