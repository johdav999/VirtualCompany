using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TenantQueryFilterTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task CompanyNotes_query_returns_no_rows_without_company_context()
    {
        await SeedCompanyNotesAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var notes = await dbContext.CompanyNotes.AsNoTracking().ToListAsync();

        Assert.Empty(notes);
    }

    [Fact]
    public async Task CompanyNotes_query_filters_rows_to_active_company_context()
    {
        var ids = await SeedCompanyNotesAsync();

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(ids.CompanyAId);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var notes = await dbContext.CompanyNotes.AsNoTracking()
            .OrderBy(x => x.Title)
            .ToListAsync();

        var note = Assert.Single(notes);
        Assert.Equal(ids.CompanyANoteId, note.Id);
        Assert.Equal(ids.CompanyAId, note.CompanyId);
    }

    [Fact]
    public async Task ContextRetrievalSources_query_filters_rows_to_active_company_context()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var retrievalAId = Guid.NewGuid();
        var retrievalBId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.ContextRetrievals.AddRange(
                new ContextRetrieval(retrievalAId, companyAId, Guid.NewGuid(), null, null, "finance retrieval", "hash-a", null, "audit"),
                new ContextRetrieval(retrievalBId, companyBId, Guid.NewGuid(), null, null, "sales retrieval", "hash-b", null, "audit"));
            dbContext.ContextRetrievalSources.AddRange(
                new ContextRetrievalSource(Guid.NewGuid(), retrievalAId, companyAId, "memory_item", "memory-a", null, null, null, "Finance memory", "Finance memory snippet", "memory", "Memory", 1, "fact | company_wide", 1, 0.9d, DateTime.UtcNow),
                new ContextRetrievalSource(Guid.NewGuid(), retrievalBId, companyBId, "memory_item", "memory-b", null, null, null, "Sales memory", "Sales memory snippet", "memory", "Memory", 1, "fact | company_wide", 1, 0.9d, DateTime.UtcNow));

            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(companyAId);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var sources = await dbContext.ContextRetrievalSources.AsNoTracking().ToListAsync();

        var source = Assert.Single(sources);
        Assert.Equal(companyAId, source.CompanyId);
        Assert.Equal(retrievalAId, source.RetrievalId);
        Assert.Equal("memory", source.SectionId);
    }

    [Fact]
    public async Task Currency_revaluation_configuration_isolated_to_active_company_context()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.CurrencyRevaluationSchedules.AddRange(
                new CurrencyRevaluationSchedule(Guid.NewGuid(), companyAId, true, 3, true, "A", Guid.NewGuid(), DateTime.UtcNow),
                new CurrencyRevaluationSchedule(Guid.NewGuid(), companyBId, true, 5, false, "B", Guid.NewGuid(), DateTime.UtcNow));
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyAId);
        var schedules = await scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>()
            .CurrencyRevaluationSchedules.AsNoTracking().ToListAsync();

        var schedule = Assert.Single(schedules);
        Assert.Equal(companyAId, schedule.CompanyId);
        Assert.Equal("A", schedule.VoucherSeriesCode);
    }

    [Fact]
    public async Task Accounting_dimension_catalogue_isolated_to_active_company_context()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.AccountingDimensionTypes.AddRange(
                new AccountingDimensionType(Guid.NewGuid(), companyAId, "cost_center", "A cost centers", null,
                    true, AccountingDimensionStatusValues.Active, new DateOnly(2026, 1, 1), null, actorId, now),
                new AccountingDimensionType(Guid.NewGuid(), companyBId, "cost_center", "B cost centers", null,
                    true, AccountingDimensionStatusValues.Active, new DateOnly(2026, 1, 1), null, actorId, now));
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyAId);
        var dimensions = await scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>()
            .AccountingDimensionTypes.AsNoTracking().ToListAsync();

        var dimension = Assert.Single(dimensions);
        Assert.Equal(companyAId, dimension.CompanyId);
        Assert.Equal("A cost centers", dimension.Name);
    }

    [Fact]
    public void Fixed_asset_subledger_entities_all_have_company_query_filters()
    {
        using var scope = _factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().Model;
        var entityTypes = new[]
        {
            typeof(FixedAssetClass), typeof(FixedAssetRegisterItem), typeof(FixedAssetComponent),
            typeof(FixedAssetBookEvent), typeof(FixedAssetMigrationConflict),
            typeof(FixedAssetDepreciationRun), typeof(FixedAssetDepreciationRunItem)
        };

        foreach (var entityType in entityTypes)
            Assert.True(model.FindEntityType(entityType)?.GetQueryFilter() is not null,
                $"{entityType.Name} must have a company query filter.");
    }

    [Fact]
    public void Accounting_governance_entities_all_have_company_query_filters()
    {
        using var scope = _factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().Model;
        var entityTypes = new[]
        {
            typeof(AccountingAccountLifecycleHistory), typeof(AccountingSeriesPolicy),
            typeof(AccountingVoucherGapEvidence), typeof(AccountingCommerceEventReceipt)
        };

        foreach (var entityType in entityTypes)
            Assert.True(model.FindEntityType(entityType)?.GetQueryFilter() is not null,
                $"{entityType.Name} must have a company query filter.");
    }

    [Fact]
    public void Year_end_entities_all_have_company_query_filters()
    {
        using var scope = _factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().Model;
        var entityTypes = new[]
        {
            typeof(YearEndRun), typeof(YearEndReadinessSnapshot), typeof(YearEndRetainedEarningsProposal),
            typeof(YearEndOpeningBalanceCandidate), typeof(YearEndApprovalSignOff),
            typeof(YearEndSubsequentEvent), typeof(YearEndHistory), typeof(YearEndCorrectionRecord),
            typeof(YearEndOperation)
        };

        foreach (var entityType in entityTypes)
            Assert.True(model.FindEntityType(entityType)?.GetQueryFilter() is not null,
                $"{entityType.Name} must have a company query filter.");
    }

    private async Task<SeedIds> SeedCompanyNotesAsync()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var companyANoteId = Guid.NewGuid();
        var companyBNoteId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.CompanyNotes.AddRange(
                new CompanyOwnedNote(companyANoteId, companyAId, "A note", "inside company A"),
                new CompanyOwnedNote(companyBNoteId, companyBId, "B note", "inside company B"));

            return Task.CompletedTask;
        });

        return new SeedIds(companyAId, companyBId, companyANoteId, companyBNoteId);
    }

    private sealed record SeedIds(Guid CompanyAId, Guid CompanyBId, Guid CompanyANoteId, Guid CompanyBNoteId);
}
