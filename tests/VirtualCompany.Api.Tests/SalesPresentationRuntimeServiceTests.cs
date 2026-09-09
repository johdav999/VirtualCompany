using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationRuntimeServiceTests
{
    [Fact]
    public async Task Commands_are_idempotent_ordered_and_resume_from_the_persisted_marker()
    {
        await using var fixture = await Fixture.CreateAsync();
        var commandId = Guid.NewGuid();

        var first = await fixture.Service.ExecuteAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, SalesPresentationToolNames.Next,
            new(commandId, 1, 1), null, CancellationToken.None);
        var duplicate = await fixture.Service.ExecuteAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, SalesPresentationToolNames.Next,
            new(commandId, 1, 1), null, CancellationToken.None);

        Assert.Equal("accepted", first!.Disposition);
        Assert.Equal("duplicate", duplicate!.Disposition);
        Assert.Equal(1, duplicate.Snapshot.Stage.SlideNumber);
        Assert.Equal(1, fixture.Publisher.PublishCount);

        var conflict = await Assert.ThrowsAsync<SalesPresentationRuntimeConflictException>(() =>
            fixture.Service.ExecuteAsync(
                fixture.CompanyId, fixture.UserId, fixture.SessionId, SalesPresentationToolNames.Next,
                new(Guid.NewGuid(), 3, 2), null, CancellationToken.None));
        Assert.Equal(SalesPresentationRuntimeProblemCodes.OutOfOrder, conflict.Code);
        Assert.Equal(1, conflict.Snapshot.Stage.Sequence);
        Assert.Contains(await fixture.Db.AuditEvents.ToListAsync(),
            x => x.Action == "sales.presentation.command_rejected" && x.Outcome == "rejected");

        var paused = await fixture.Service.ExecuteAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, SalesPresentationToolNames.Pause,
            new(Guid.NewGuid(), 2, 2, TalkingPointIndex: 2), null, CancellationToken.None);
        var resumed = await fixture.Service.ExecuteAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, SalesPresentationToolNames.Resume,
            new(Guid.NewGuid(), 3, 3), null, CancellationToken.None);

        Assert.Equal("interrupted", paused!.Snapshot.Stage.SessionStatus);
        Assert.Equal("slide:1:talking-point:2", paused.Snapshot.Private.ResumeMarker);
        Assert.Equal("presenting", resumed!.Snapshot.Stage.SessionStatus);
        Assert.Equal(1, resumed.Snapshot.Stage.SlideNumber);
        Assert.Equal("slide:1:talking-point:2", resumed.Snapshot.Private.ResumeMarker);
        Assert.Equal(2, resumed.Snapshot.Private.TalkingPointIndex);
        Assert.Equal(3, fixture.Publisher.PublishCount);
    }

    [Fact]
    public async Task Stage_snapshot_cannot_serialize_private_notes_or_plan_artifacts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var snapshot = await fixture.Service.GetCurrentAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, CancellationToken.None);

        var stageJson = JsonSerializer.Serialize(snapshot!.Stage);
        var privateJson = JsonSerializer.Serialize(snapshot.Private);

        Assert.Null(snapshot.Stage.ImageStorageUrl);
        Assert.DoesNotContain("speakerNotes", stageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planArtifacts", stageJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("speakerNotes", privateJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private presenter note", privateJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_returns_only_customer_visible_slide_content()
    {
        await using var fixture = await Fixture.CreateAsync();
        var results = await fixture.Service.SearchAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, "Welcome", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(1, result.SlideNumber);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            VirtualCompanyDbContext db, SalesPresentationRuntimeService service, RecordingPublisher publisher,
            Guid companyId, Guid userId, Guid sessionId)
        {
            Db = db;
            Service = service;
            Publisher = publisher;
            CompanyId = companyId;
            UserId = userId;
            SessionId = sessionId;
        }

        public VirtualCompanyDbContext Db { get; }
        public SalesPresentationRuntimeService Service { get; }
        public RecordingPublisher Publisher { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid SessionId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var companyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var deckId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var context = new TestCompanyContextAccessor(companyId, userId);
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseInMemoryDatabase($"sales-presentation-runtime-{Guid.NewGuid():N}").Options,
                context);
            db.Companies.Add(new Company(companyId, "Runtime Company"));
            db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.SalesMeetingSessions.Add(new SalesMeetingSession(
                sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(),
                "Confirm fit", "Finance team", 45, null, "provider-meeting",
                SalesMeetingConsentStatus.Pending, SalesMeetingRetentionPolicy.Standard, 365,
                now.AddHours(1), userId, now));
            var deck = new SalesPresentationDeck(
                deckId, companyId, sessionId, agentId, 1, "Board deck", "board.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100,
                new string('a', 64), "safe/deck.pptx", null, userId, now);
            deck.BeginProcessing(now, TimeSpan.FromMinutes(10));
            deck.MarkProcessed(2, "test", "1", "static", 1, now);
            deck.Activate(now);
            db.SalesPresentationDecks.Add(deck);
            var slide = new SalesPresentationSlide(
                Guid.NewGuid(), companyId, deckId, 1, 1, "Opening", "Welcome to the company",
                "private presenter note", "safe/1.svg", null, 1600, 900, 12192000, 6858000,
                new string('b', 64), "Open the conversation", 60, "Move to discovery", now);
            db.SalesPresentationSlides.Add(slide);
            db.SalesPresentationSlides.Add(new SalesPresentationSlide(
                Guid.NewGuid(), companyId, deckId, 1, 2, "Discovery", "Customer goals",
                null, "safe/2.svg", null, 1600, 900, 12192000, 6858000,
                new string('c', 64), "Discover needs", 90, "Continue", now));
            db.SalesMeetingArtifacts.Add(new SalesMeetingArtifact(
                Guid.NewGuid(), companyId, sessionId, deckId, slide.Id, 1,
                SalesMeetingArtifactType.SlideTalkingPoint, "opening", 0, "Internal talking point",
                SalesMeetingArtifactClassification.Recommendation, null, null, now));
            await db.SaveChangesAsync();
            var publisher = new RecordingPublisher();
            var service = new SalesPresentationRuntimeService(
                db, TimeProvider.System, [publisher], NullLogger<SalesPresentationRuntimeService>.Instance);
            return new Fixture(db, service, publisher, companyId, userId, sessionId);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingPublisher : ISalesPresentationEventPublisher
    {
        public int PublishCount { get; private set; }
        public Task PublishAsync(Guid companyId, Guid sessionId, SalesPresentationAuthoritativeSnapshotDto snapshot, CancellationToken cancellationToken)
        {
            PublishCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value)
        {
            Membership = value;
            CompanyId = value?.CompanyId;
            UserId = value?.UserId;
        }
    }
}
