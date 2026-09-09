using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationDeckServiceTests
{
    [Fact]
    public async Task Repeated_import_is_idempotent_by_company_session_hash_and_processing_version()
    {
        await using var fixture = await Fixture.CreateAsync();
        var package = CreateMinimalPackage();

        var first = await fixture.ImportAsync(package);
        var second = await fixture.ImportAsync(package);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.Db.SalesPresentationDecks.CountAsync());
        Assert.Equal(1, fixture.Storage.WriteCount);
        Assert.Equal(1, await fixture.Db.AuditEvents.CountAsync(x => x.Action == "sales.presentation_deck.imported"));
    }

    [Fact]
    public async Task Invalid_or_oversized_content_is_rejected_before_storage()
    {
        await using var fixture = await Fixture.CreateAsync(maximumBytes: 32);
        await using var malformed = new MemoryStream([1, 2, 3, 4]);

        await Assert.ThrowsAsync<SalesPresentationValidationException>(() =>
            fixture.Service.ImportAsync(
                fixture.CompanyId, fixture.UserId, fixture.SessionId,
                new(fixture.AgentId, null, "deck.pptx", PowerPointContentType, malformed.Length, malformed),
                null, CancellationToken.None));
        Assert.Equal(0, fixture.Storage.WriteCount);

        var package = CreateMinimalPackage();
        await Assert.ThrowsAsync<SalesPresentationValidationException>(() =>
            fixture.Service.ImportAsync(
                fixture.CompanyId, fixture.UserId, fixture.SessionId,
                new(fixture.AgentId, null, "deck.pptx", PowerPointContentType, package.Length, package),
                null, CancellationToken.None));
        Assert.Equal(0, fixture.Storage.WriteCount);
    }

    private const string PowerPointContentType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private static MemoryStream CreateMinimalPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("[Content_Types].xml");
            archive.CreateEntry("ppt/presentation.xml");
        }
        stream.Position = 0;
        return stream;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, SalesPresentationDeckService service, RecordingStorage storage,
            Guid companyId, Guid userId, Guid sessionId, Guid agentId)
        {
            Db = db;
            Service = service;
            Storage = storage;
            CompanyId = companyId;
            UserId = userId;
            SessionId = sessionId;
            AgentId = agentId;
        }

        public VirtualCompanyDbContext Db { get; }
        public SalesPresentationDeckService Service { get; }
        public RecordingStorage Storage { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid SessionId { get; }
        public Guid AgentId { get; }

        public Task<SalesPresentationDeckDto> ImportAsync(MemoryStream package)
        {
            package.Position = 0;
            return Service.ImportAsync(
                CompanyId, UserId, SessionId,
                new(AgentId, "Board deck", "board.pptx", PowerPointContentType, package.Length, package),
                "correlation", CancellationToken.None);
        }

        public static async Task<Fixture> CreateAsync(long maximumBytes = 25 * 1024 * 1024)
        {
            var companyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var context = new TestCompanyContextAccessor(companyId, userId);
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseInMemoryDatabase($"sales-presentation-{Guid.NewGuid():N}").Options,
                context);
            var now = DateTime.UtcNow;
            db.Companies.Add(new Company(companyId, "Presentation Company"));
            db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.Agents.Add(new Agent(
                agentId, companyId, "alex-sales", "Alex", "Sales representative", "Sales", null,
                AgentSeniority.Senior, AgentStatus.Active));
            db.SalesMeetingSessions.Add(new SalesMeetingSession(
                sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(),
                "Confirm fit", "Finance team", 45, null, "provider-meeting",
                SalesMeetingConsentStatus.Pending, SalesMeetingRetentionPolicy.Standard, 365,
                now.AddHours(1), userId, now));
            await db.SaveChangesAsync();
            var storage = new RecordingStorage();
            var service = new SalesPresentationDeckService(
                db, storage, Options.Create(new SalesPresentationOptions { MaximumUploadBytes = maximumBytes }),
                TimeProvider.System);
            return new Fixture(db, service, storage, companyId, userId, sessionId, agentId);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingStorage : ICompanyDocumentStorage
    {
        public int WriteCount { get; private set; }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DocumentStorageWriteResult> WriteAsync(
            DocumentStorageWriteRequest request, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult(new DocumentStorageWriteResult(request.StorageKey, null));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestCompanyContextAccessor(Guid? companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}
