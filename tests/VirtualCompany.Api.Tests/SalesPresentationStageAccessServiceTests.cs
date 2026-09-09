using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationStageAccessServiceTests
{
    [Fact]
    public async Task Grant_is_organizer_scoped_stage_safe_and_limits_assets_to_current_or_next_slide()
    {
        await using var fixture = await Fixture.CreateAsync();

        var denied = await Assert.ThrowsAsync<SalesPresentationStageAccessException>(() =>
            fixture.Service.IssueAsync(fixture.CompanyId, fixture.OtherUserId, fixture.SessionId, default));
        Assert.Equal(SalesPresentationStageAccessProblemCodes.OrganizerRequired, denied.Code);

        var grant = await fixture.Service.IssueAsync(
            fixture.CompanyId, fixture.OrganizerId, fixture.SessionId, default);
        var snapshot = await fixture.Service.GetSnapshotAsync(fixture.SessionId, grant.AccessToken, default);

        Assert.Equal(fixture.DeckId, snapshot.DeckId);
        Assert.Null(snapshot.ImageStorageUrl);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(snapshot), StringComparison.OrdinalIgnoreCase);
        await using var current = await fixture.Service.OpenSlideAsync(
            fixture.SessionId, grant.AccessToken, fixture.DeckId, 1, 1, default);
        Assert.Equal("image/svg+xml", current.ContentType);
        await using var next = await fixture.Service.OpenSlideAsync(
            fixture.SessionId, grant.AccessToken, fixture.DeckId, 1, 2, default);

        var outOfWindow = await Assert.ThrowsAsync<SalesPresentationStageAccessException>(() =>
            fixture.Service.OpenSlideAsync(fixture.SessionId, grant.AccessToken, fixture.DeckId, 1, 3, default));
        Assert.Equal(SalesPresentationStageAccessProblemCodes.SlideNotAllowed, outOfWindow.Code);
        var crossSession = await Assert.ThrowsAsync<SalesPresentationStageAccessException>(() =>
            fixture.Service.GetSnapshotAsync(Guid.NewGuid(), grant.AccessToken, default));
        Assert.Equal(SalesPresentationStageAccessProblemCodes.InvalidGrant, crossSession.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, SalesPresentationStageAccessService service,
            Guid companyId, Guid organizerId, Guid otherUserId, Guid sessionId, Guid deckId)
        { Db = db; Service = service; CompanyId = companyId; OrganizerId = organizerId; OtherUserId = otherUserId; SessionId = sessionId; DeckId = deckId; }

        public VirtualCompanyDbContext Db { get; }
        public SalesPresentationStageAccessService Service { get; }
        public Guid CompanyId { get; }
        public Guid OrganizerId { get; }
        public Guid OtherUserId { get; }
        public Guid SessionId { get; }
        public Guid DeckId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var companyId = Guid.NewGuid();
            var organizerId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var deckId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseInMemoryDatabase($"stage-access-{Guid.NewGuid():N}").Options,
                new TestCompanyContextAccessor(companyId, organizerId));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, organizerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, otherUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.SalesMeetingSessions.Add(new SalesMeetingSession(
                sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(),
                "Present", "Buyer", 45, null, "provider-meeting", SalesMeetingConsentStatus.Pending,
                SalesMeetingRetentionPolicy.Standard, 365, now.AddHours(1), organizerId, now));
            db.TeamsMeetingCalls.Add(new TeamsMeetingCall(Guid.NewGuid(), companyId, sessionId, Guid.NewGuid(),
                organizerId, new string('a', 64), 1, 1, "media-host", now));
            var deck = new SalesPresentationDeck(deckId, companyId, sessionId, Guid.NewGuid(), 1, "Deck", "deck.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100,
                new string('b', 64), "safe/deck.pptx", null, organizerId, now);
            deck.BeginProcessing(now, TimeSpan.FromMinutes(5));
            deck.MarkProcessed(2, "test", "1", "static", 1, now);
            deck.Activate(now);
            db.SalesPresentationDecks.Add(deck);
            db.SalesPresentationSlides.AddRange(
                Slide(companyId, deckId, 1, "Opening", "private speaker note", "safe/1.svg", 'c', now),
                Slide(companyId, deckId, 2, "Next", null, "safe/2.svg", 'd', now));
            await db.SaveChangesAsync();

            var service = new SalesPresentationStageAccessService(db, new MemoryStorage(),
                new EphemeralDataProtectionProvider(), Options.Create(new TeamsPresenterOptions
                { Enabled = true, SharedStageEnabled = true, StageAccessMinutes = 30 }),
                new NullAuditWriter(), TimeProvider.System);
            return new(db, service, companyId, organizerId, otherUserId, sessionId, deckId);
        }

        private static SalesPresentationSlide Slide(Guid companyId, Guid deckId, int number, string title,
            string? notes, string key, char hash, DateTime now) => new(Guid.NewGuid(), companyId, deckId, 1,
            number, title, $"Visible content {number}", notes, key, null, 1600, 900, 12192000, 6858000,
            new string(hash, 64), "Objective", 60, "Continue", now);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MemoryStorage : ICompanyDocumentStorage
    {
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")));
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NullAuditWriter : IAuditEventWriter
    { public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask; }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value)
        { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}
