using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPersistenceTenantIsolationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Sales_pipeline_stage_seed_contains_only_system_stages_once()
    {
        var stages = await _factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.SalesPipelineStages
                .IgnoreQueryFilters()
                .OrderBy(x => x.DisplayOrder)
                .Select(x => new { x.Name, x.CompanyId, x.IsSystem, x.IsDeleted })
                .ToListAsync());

        Assert.Equal(["New", "Qualified", "Proposal", "Won", "Lost"], stages.Select(x => x.Name).ToArray());
        Assert.All(stages, stage =>
        {
            Assert.Equal(SalesPipelineStage.SystemCompanyId, stage.CompanyId);
            Assert.True(stage.IsSystem);
            Assert.False(stage.IsDeleted);
        });
    }

    [Fact]
    public void Sales_model_declares_required_sales_indexes()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        AssertIndex<SalesPipelineStage>(dbContext, nameof(SalesPipelineStage.CompanyId));
        AssertIndex<SalesPipelineStage>(dbContext, nameof(SalesPipelineStage.CreatedUtc));
        AssertTenantStatusCreatedIndexes<CustomerCompany>(dbContext);
        AssertTenantStatusCreatedIndexes<Contact>(dbContext);
        AssertTenantStatusCreatedIndexes<Lead>(dbContext);
        AssertTenantStatusCreatedIndexes<Deal>(dbContext);
        AssertTenantStatusCreatedIndexes<SalesActivity>(dbContext);
        AssertTenantStatusCreatedIndexes<SalesAgentRecommendation>(dbContext);
        AssertTenantStatusCreatedIndexes<SalesActionApproval>(dbContext);
        AssertTenantStatusCreatedIndexes<SalesEmailLink>(dbContext);
    }

    [Fact]
    public void Sales_email_links_have_tenant_scoped_foreign_keys()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        AssertSalesEmailLinkForeignKey<Lead>(dbContext, nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.LeadId));
        AssertSalesEmailLinkForeignKey<Deal>(dbContext, nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.DealId));
        AssertSalesEmailLinkForeignKey<Contact>(dbContext, nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.ContactId));
        AssertSalesEmailLinkForeignKey<CustomerCompany>(dbContext, nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.CustomerCompanyId));

        var leadEntityType = dbContext.Model.FindEntityType(typeof(Lead));
        Assert.NotNull(leadEntityType);
        Assert.Contains(leadEntityType!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Deal) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(Lead.CompanyId),
                nameof(Lead.ConvertedDealId)
            ]) &&
            foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(ICompanyOwnedEntity.CompanyId),
                nameof(Deal.Id)
            ]));
    }

    [Fact]
    public async Task Sales_seed_does_not_create_business_records()
    {
        var counts = await _factory.ExecuteDbContextAsync(async dbContext => new
        {
            Customers = await dbContext.CustomerCompanies.IgnoreQueryFilters().CountAsync(),
            Contacts = await dbContext.Contacts.IgnoreQueryFilters().CountAsync(),
            Leads = await dbContext.Leads.IgnoreQueryFilters().CountAsync(),
            Deals = await dbContext.Deals.IgnoreQueryFilters().CountAsync()
        });

        Assert.Equal(0, counts.Customers);
        Assert.Equal(0, counts.Contacts);
        Assert.Equal(0, counts.Leads);
        Assert.Equal(0, counts.Deals);
    }

    [Fact]
    public async Task Sales_queries_are_scoped_to_active_company_context()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        await SeedTwoCompaniesAsync(companyAId, companyBId);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyAId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.All(await dbContext.CustomerCompanies.AsNoTracking().ToListAsync(), customer => Assert.Equal(companyAId, customer.CompanyId));
        Assert.All(await dbContext.Contacts.AsNoTracking().ToListAsync(), contact => Assert.Equal(companyAId, contact.CompanyId));
        Assert.All(await dbContext.Leads.AsNoTracking().ToListAsync(), lead => Assert.Equal(companyAId, lead.CompanyId));
        Assert.All(await dbContext.Deals.AsNoTracking().ToListAsync(), deal => Assert.Equal(companyAId, deal.CompanyId));
        Assert.All(await dbContext.SalesActivities.AsNoTracking().ToListAsync(), activity => Assert.Equal(companyAId, activity.CompanyId));
        Assert.All(await dbContext.SalesAgentRecommendations.AsNoTracking().ToListAsync(), recommendation => Assert.Equal(companyAId, recommendation.CompanyId));
        Assert.All(await dbContext.SalesActionApprovals.AsNoTracking().ToListAsync(), approval => Assert.Equal(companyAId, approval.CompanyId));
        Assert.All(await dbContext.SalesEmailLinks.AsNoTracking().ToListAsync(), link => Assert.Equal(companyAId, link.CompanyId));
    }

    [Fact]
    public async Task Sales_records_from_another_company_cannot_be_mutated()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        await SeedTwoCompaniesAsync(companyAId, companyBId);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyBId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var customerFromCompanyA = await dbContext.CustomerCompanies
            .IgnoreQueryFilters()
            .FirstAsync(x => x.CompanyId == companyAId);

        customerFromCompanyA.Update("Cross-tenant rename attempt", SalesStatuses.Active, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Sales_lead_conversion_cannot_reference_cross_tenant_deal()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        await SeedTwoCompaniesAsync(companyAId, companyBId);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyAId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var leadFromCompanyA = await dbContext.Leads
            .IgnoreQueryFilters()
            .FirstAsync(x => x.CompanyId == companyAId);
        var dealFromCompanyB = await dbContext.Deals
            .IgnoreQueryFilters()
            .FirstAsync(x => x.CompanyId == companyBId);

        leadFromCompanyA.Qualify();
        leadFromCompanyA.ConvertToDeal(dealFromCompanyB.Id);
        await Assert.ThrowsAnyAsync<Exception>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Sales_repository_rejects_cross_tenant_links()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        Guid companyAContactId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(new Company(companyAId, "Sales Company A"), new Company(companyBId, "Sales Company B"));
            dbContext.Contacts.Add(new Contact(companyAContactId, companyAId, "A Contact", "a@example.com"));
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyBId);
        var repository = scope.ServiceProvider.GetRequiredService<ISalesPersistenceRepository>();
        var lead = new Lead(
            Guid.NewGuid(),
            companyBId,
            "Cross tenant lead",
            SalesPipelineStage.NewStageId,
            primaryContactId: companyAContactId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddLeadAsync(lead, CancellationToken.None));
    }

    [Fact]
    public async Task Sales_repository_rejects_soft_deleted_link_targets()
    {
        var companyId = Guid.NewGuid();
        var deletedContactId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Deleted Contact Company"));
            var contact = new Contact(deletedContactId, companyId, "Deleted Contact", "deleted@example.com");
            contact.SoftDelete();
            dbContext.Contacts.Add(contact);
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        var repository = scope.ServiceProvider.GetRequiredService<ISalesPersistenceRepository>();
        var lead = new Lead(
            Guid.NewGuid(),
            companyId,
            "Lead linked to deleted contact",
            SalesPipelineStage.NewStageId,
            primaryContactId: deletedContactId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddLeadAsync(lead, CancellationToken.None));
    }

    [Fact]
    public async Task Sales_soft_deleted_records_are_hidden_by_query_filters()
    {
        var companyId = Guid.NewGuid();
        var deletedLeadId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Soft Delete Company"));
            var lead = new Lead(deletedLeadId, companyId, "Deleted lead", SalesPipelineStage.NewStageId);
            lead.SoftDelete();
            dbContext.Leads.Add(lead);
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.Empty(await dbContext.Leads.AsNoTracking().ToListAsync());
        Assert.Equal(1, await dbContext.Leads.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.IsDeleted));
    }

    private async Task SeedTwoCompaniesAsync(Guid companyAId, Guid companyBId)
    {
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(
                new Company(companyAId, "Sales Company A"),
                new Company(companyBId, "Sales Company B"));

            AddSalesData(dbContext, companyAId, "A");
            AddSalesData(dbContext, companyBId, "B");
            return Task.CompletedTask;
        });
    }

    private static void AddSalesData(VirtualCompanyDbContext dbContext, Guid companyId, string suffix)
    {
        var customerId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();

        dbContext.CustomerCompanies.Add(new CustomerCompany(customerId, companyId, $"Customer {suffix}"));
        dbContext.Contacts.Add(new Contact(contactId, companyId, $"Contact {suffix}", $"{suffix.ToLowerInvariant()}@example.com", customerId));
        dbContext.Leads.Add(new Lead(leadId, companyId, $"Lead {suffix}", SalesPipelineStage.NewStageId, primaryContactId: contactId, customerCompanyId: customerId));
        dbContext.Deals.Add(new Deal(dealId, companyId, $"Deal {suffix}", SalesPipelineStage.QualifiedStageId, 1000m, "USD", sourceLeadId: leadId, primaryContactId: contactId, customerCompanyId: customerId));
        dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "call", $"Call {suffix}", DateTime.UtcNow, leadId: leadId, dealId: dealId, contactId: contactId, customerCompanyId: customerId));
        dbContext.SalesAgentRecommendations.Add(new SalesAgentRecommendation(recommendationId, companyId, $"Recommendation {suffix}", "Follow up with the buyer.", leadId, dealId));
        dbContext.SalesActionApprovals.Add(new SalesActionApproval(Guid.NewGuid(), companyId, $"Approval {suffix}", "A human should review the outreach.", recommendationId, leadId, dealId));
        dbContext.SalesEmailLinks.Add(new SalesEmailLink(Guid.NewGuid(), companyId, $"message-{suffix}", leadId, dealId, contactId, customerId));
    }

    private static void AssertTenantStatusCreatedIndexes<TEntity>(VirtualCompanyDbContext dbContext)
    {
        AssertIndex<TEntity>(dbContext, nameof(ICompanyOwnedEntity.CompanyId));
        AssertIndex<TEntity>(dbContext, "Status");
        AssertIndex<TEntity>(dbContext, "CreatedUtc");
    }

    private static void AssertIndex<TEntity>(VirtualCompanyDbContext dbContext, params string[] propertyNames)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(
            entityType!.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static void AssertSalesEmailLinkForeignKey<TPrincipal>(VirtualCompanyDbContext dbContext, params string[] propertyNames)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(SalesEmailLink));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(TPrincipal) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(propertyNames) &&
            foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([nameof(ICompanyOwnedEntity.CompanyId), "Id"]));
    }
}
