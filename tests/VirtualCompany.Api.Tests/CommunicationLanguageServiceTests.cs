using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Communication;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Communication;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class CommunicationLanguageServiceTests
{
    [Fact]
    public async Task ContactLanguageUpdate_IsTenantScopedNormalizedAndAudited()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var contactA = new Contact(Guid.NewGuid(), companyA, "A Contact", "a@example.com");
        var contactB = new Contact(Guid.NewGuid(), companyB, "B Contact", "b@example.com");
        var companyContext = new FixedCompanyContextAccessor(null);
        await using var db = CreateDbContext(companyContext);
        db.Contacts.AddRange(contactA, contactB);
        await db.SaveChangesAsync();
        companyContext.SetCompanyId(companyA);
        var audit = new CaptureAuditEventWriter();
        var service = new CommunicationLanguageService(db, audit);

        var updated = await service.UpdateContactAsync(
            companyA,
            userId,
            contactA.Id,
            new UpdateCommunicationLanguageRequest("SV-se"),
            CancellationToken.None);
        var crossTenant = await service.UpdateContactAsync(
            companyA,
            userId,
            contactB.Id,
            new UpdateCommunicationLanguageRequest("en-GB"),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("sv-SE", updated.LanguageTag);
        Assert.Null(crossTenant);
        Assert.Equal("sv-SE", (await db.Contacts.IgnoreQueryFilters().SingleAsync(x => x.Id == contactA.Id)).PreferredLanguage);
        Assert.Null((await db.Contacts.IgnoreQueryFilters().SingleAsync(x => x.Id == contactB.Id)).PreferredLanguage);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(companyA, auditEvent.CompanyId);
        Assert.Equal(userId, auditEvent.ActorId);
        Assert.Equal("communication.contact_language_changed", auditEvent.Action);
        Assert.Equal("sv-SE", auditEvent.Metadata!["newLanguage"]);
    }

    [Fact]
    public async Task InvalidLanguage_IsRejectedWithoutPersistenceOrAudit()
    {
        var companyId = Guid.NewGuid();
        var contact = new Contact(Guid.NewGuid(), companyId, "Contact", "contact@example.com");
        await using var db = CreateDbContext(new FixedCompanyContextAccessor(companyId));
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var service = new CommunicationLanguageService(db, audit);

        await Assert.ThrowsAsync<CommunicationLanguageValidationException>(() =>
            service.UpdateContactAsync(
                companyId,
                Guid.NewGuid(),
                contact.Id,
                new UpdateCommunicationLanguageRequest("Swedish please"),
                CancellationToken.None));

        Assert.Null((await db.Contacts.SingleAsync()).PreferredLanguage);
        Assert.Empty(audit.Events);
    }

    private static VirtualCompanyDbContext CreateDbContext(ICompanyContextAccessor companyContext) =>
        new(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            companyContext);

    private sealed class CaptureAuditEventWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];

        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedCompanyContextAccessor(Guid? companyId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; }
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;

        public void SetCompanyContext(ResolvedCompanyMembershipContext? context)
        {
            Membership = context;
            CompanyId = context?.CompanyId;
            UserId = context?.UserId;
        }
    }
}
