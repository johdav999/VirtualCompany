using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingCanonicalChangeCommandHandlerTests
{
    [Fact]
    public async Task Typed_command_changes_the_allowed_field_once_and_rejects_a_stale_replay()
    {
        var company = Guid.NewGuid(); var user = Guid.NewGuid(); var deal = new Deal(Guid.NewGuid(), company, "Renewal", Guid.NewGuid(), 1000m, "SEK", createdUtc: DateTime.UtcNow.AddMinutes(-1), updatedUtc: DateTime.UtcNow.AddMinutes(-1));
        await using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"proposal-command-{Guid.NewGuid():N}").Options, new Context(company, user));
        db.Deals.Add(deal); await db.SaveChangesAsync(); var originalVersion = SalesMeetingCanonicalChangeCommandHandler.Version(deal.UpdatedUtc); var handler = new SalesMeetingCanonicalChangeCommandHandler(db);

        var result = await handler.ApplyAsync(new(company, "deal", deal.Id, "deal_probability", new("decimal", DecimalValue: .75m), originalVersion, user), default);
        await db.SaveChangesAsync(); Assert.Equal(.75m, deal.Probability); Assert.NotEqual(result.BeforeValueJson, result.AfterValueJson);
        await Assert.ThrowsAsync<SalesMeetingChangeProposalConflictException>(() => handler.ApplyAsync(new(company, "deal", deal.Id, "deal_probability", new("decimal", DecimalValue: .9m), originalVersion, user), default));
        Assert.Equal(.75m, deal.Probability);
    }

    private sealed class Context(Guid company, Guid user) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = company; public Guid? UserId { get; private set; } = user; public bool IsResolved => true; public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value; public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}
