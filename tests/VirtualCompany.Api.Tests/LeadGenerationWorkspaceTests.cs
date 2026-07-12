using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class LeadGenerationDomainTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void Active_icp_is_immutable_and_versionable()
    {
        var profile = Profile();
        profile.Activate();

        Assert.Equal(LeadGenerationStatuses.Active, profile.Status);
        Assert.Throws<InvalidOperationException>(() => profile.UpdateDraft("se", "saas", 10, 50, null, null, "cfo", "", "", "", "", _userId));
    }

    [Fact]
    public void Icp_rejects_contradictory_company_size()
    {
        Assert.Throws<ArgumentException>(() => new IdealCustomerProfile(Guid.NewGuid(), _companyId, "SME", 1, _userId, "se", "saas", 100, 10, null, null, "cfo", "", "", "", ""));
    }

    [Fact]
    public void Prospect_score_is_bounded_and_hard_rejection_is_terminal()
    {
        var prospect = Prospect();
        prospect.ApplyEvaluation("matched", 90, "{}");
        prospect.ApplyScores(150, -5, 80, 120, "High priority", "{}");

        Assert.Equal(100, prospect.TimingScore);
        Assert.Equal(0, prospect.RoleScore);
        Assert.Equal(100, prospect.OverallScore);
        prospect.Reject("Competitor");
        Assert.Throws<InvalidOperationException>(prospect.Accept);
    }

    [Fact]
    public void Prospecting_run_enforces_limits_and_lifecycle()
    {
        var run = new ProspectingRun(Guid.NewGuid(), _companyId, Guid.NewGuid(), _userId, "Nordics", 10, 20, "first_party", "se", 30, 0, null);
        run.Start(); run.Progress(99, 99, "Search", "cursor", 0); run.Complete();

        Assert.Equal(10, run.AccountsFound);
        Assert.Equal(20, run.ContactsFound);
        Assert.Equal(LeadGenerationStatuses.Completed, run.Status);
        Assert.Throws<InvalidOperationException>(run.Cancel);
    }

    [Fact]
    public void Source_policy_prevents_budget_overrun()
    {
        var policy = new ProspectSourcePolicy(Guid.NewGuid(), _companyId);
        policy.Update("first_party", "se", "company", 25, 50, 10, 365, 30);
        policy.Reserve(25);

        Assert.Throws<InvalidOperationException>(() => policy.Reserve(30));
        policy.Reconcile(25, 20);
        Assert.Equal(20, policy.ActualThisMonth);
    }

    private IdealCustomerProfile Profile() => new(Guid.NewGuid(), _companyId, "Nordic SME", 1, _userId, "se,no", "saas", 10, 249, null, null, "cfo", "", "cash flow", "hiring", "competitor");
    private ProspectAccount Prospect() => new(Guid.NewGuid(), _companyId, Guid.NewGuid(), Guid.NewGuid(), "Example", "example.com", "se", "saas", 50, null, "manual", Guid.NewGuid().ToString("N"), DateTime.UtcNow);
}

public sealed class LeadGenerationPersistenceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public LeadGenerationPersistenceTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Model_has_tenant_scoped_operational_indexes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        AssertIndex<IdealCustomerProfile>(db, nameof(IdealCustomerProfile.CompanyId), nameof(IdealCustomerProfile.Status));
        AssertIndex<ProspectAccount>(db, nameof(ProspectAccount.CompanyId), nameof(ProspectAccount.Status), nameof(ProspectAccount.OverallScore));
        AssertIndex<ProspectSignal>(db, nameof(ProspectSignal.CompanyId), nameof(ProspectSignal.DedupeKey));
        AssertIndex<SalesSuppression>(db, nameof(SalesSuppression.CompanyId), nameof(SalesSuppression.ScopeType), nameof(SalesSuppression.ScopeValue), nameof(SalesSuppression.IsActive));
        AssertIndex<SalesSourceTouch>(db, nameof(SalesSourceTouch.CompanyId), nameof(SalesSourceTouch.SubjectType), nameof(SalesSourceTouch.SubjectId), nameof(SalesSourceTouch.ObservedUtc));
        AssertIndex<SalesSourceAttribution>(db, nameof(SalesSourceAttribution.CompanyId), nameof(SalesSourceAttribution.SubjectType), nameof(SalesSourceAttribution.SubjectId));
    }

    [Fact]
    public async Task First_party_provider_never_returns_another_companys_accounts()
    {
        var companyA = Guid.NewGuid(); var companyB = Guid.NewGuid();
        await _factory.ExecuteDbContextAsync(async db =>
        {
            db.Companies.AddRange(new Company(companyA, "Lead generation A"), new Company(companyB, "Lead generation B"));
            db.CustomerCompanies.AddRange(new CustomerCompany(Guid.NewGuid(), companyA, "A account"), new CustomerCompany(Guid.NewGuid(), companyB, "B account"));
            await db.SaveChangesAsync();
        });
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IProspectDataProvider>().Single(x => x.Key == "first_party");

        var result = await provider.SearchAccountsAsync(companyA, new(Guid.NewGuid(), 50, null, "", ""), CancellationToken.None);

        Assert.Contains(result.Accounts, x => x.Name == "A account");
        Assert.DoesNotContain(result.Accounts, x => x.Name == "B account");
    }

    private static void AssertIndex<T>(VirtualCompanyDbContext db, params string[] properties)
    {
        var entity = db.Model.FindEntityType(typeof(T));
        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index => index.Properties.Select(x => x.Name).SequenceEqual(properties));
    }
}
