using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceInsightPersistenceServiceTests
{
    [Fact]
    public async Task Reconcile_creates_updates_and_resolves_the_same_insight_record()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Insight Persistence Company"));
        await dbContext.SaveChangesAsync();

        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 22, 8, 0, 0, TimeSpan.Zero));
        var service = new FinanceInsightPersistenceService(dbContext, timeProvider);
        var context = new FinancialCheckContext(companyId, timeProvider.GetUtcNow().UtcDateTime, 90, 30, 14);
        var primaryEntity = new FinanceInsightEntityReferenceDto("counterparty", "customer-1", "Contoso", true);

        await service.ReconcileAsync(
            context,
            ["overdue_receivables"],
            [
                new FinancialCheckResult(
                    FinancialCheckDefinitions.OverdueReceivables,
                    "overdue_receivables:customer-1",
                    primaryEntity.EntityType,
                    primaryEntity.EntityId,
                    FinancialCheckSeverity.High,
                    "Contoso has overdue receivables.",
                    "Start collections outreach.",
                    0.81m,
                    primaryEntity,
                    [primaryEntity])
            ],
            CancellationToken.None);

        var created = await dbContext.FinanceAgentInsights.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(FinanceInsightStatus.Active, created.Status);

        timeProvider.SetUtcNow(new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero));

        await service.ReconcileAsync(
            context with { AsOfUtc = timeProvider.GetUtcNow().UtcDateTime },
            ["overdue_receivables"],
            [
                new FinancialCheckResult(
                    FinancialCheckDefinitions.OverdueReceivables,
                    "overdue_receivables:customer-1",
                    primaryEntity.EntityType,
                    primaryEntity.EntityId,
                    FinancialCheckSeverity.Critical,
                    "Contoso now has severely overdue receivables.",
                    "Escalate collections immediately.",
                    0.92m,
                    primaryEntity,
                    [primaryEntity])
            ],
            CancellationToken.None);

        var updatedRows = await dbContext.FinanceAgentInsights.IgnoreQueryFilters().ToListAsync();
        Assert.Single(updatedRows);
        Assert.Equal(created.Id, updatedRows[0].Id);
        Assert.Equal(created.CreatedUtc, updatedRows[0].CreatedUtc);
        Assert.Equal(FinancialCheckSeverity.Critical, updatedRows[0].Severity);
        Assert.Contains("severely overdue", updatedRows[0].Message);
        Assert.Null(updatedRows[0].ResolvedUtc);

        timeProvider.SetUtcNow(new DateTimeOffset(2026, 4, 22, 10, 0, 0, TimeSpan.Zero));

        await service.ReconcileAsync(
            context with { AsOfUtc = timeProvider.GetUtcNow().UtcDateTime },
            ["overdue_receivables"],
            [],
            CancellationToken.None);

        var resolved = await dbContext.FinanceAgentInsights.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(FinanceInsightStatus.Resolved, resolved.Status);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime, resolved.ResolvedUtc);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime, resolved.ObservedUtc);
    }

    [Fact]
    public async Task List_filters_by_entity_reference_and_returns_shared_metadata()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Insight Filter Company"));
        await dbContext.SaveChangesAsync();

        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 22, 8, 0, 0, TimeSpan.Zero));
        var service = new FinanceInsightPersistenceService(dbContext, timeProvider);
        var context = new FinancialCheckContext(companyId, timeProvider.GetUtcNow().UtcDateTime, 90, 30, 14);
        var contoso = new FinanceInsightEntityReferenceDto("counterparty", "customer-1", "Contoso", true);
        var fabrikam = new FinanceInsightEntityReferenceDto("counterparty", "supplier-7", "Fabrikam", true);

        await service.ReconcileAsync(
            context,
            ["overdue_receivables", "payables_pressure"],
            [
                new FinancialCheckResult(
                    FinancialCheckDefinitions.OverdueReceivables,
                    "overdue_receivables:customer-1",
                    contoso.EntityType,
                    contoso.EntityId,
                    FinancialCheckSeverity.High,
                    "Contoso has overdue receivables.",
                    "Start collections outreach.",
                    0.81m,
                    contoso,
                    [contoso]),
                new FinancialCheckResult(
                    FinancialCheckDefinitions.PayablesPressure,
                    "payables_pressure:supplier-7",
                    fabrikam.EntityType,
                    fabrikam.EntityId,
                    FinancialCheckSeverity.Medium,
                    "Fabrikam has bills due soon.",
                    "Sequence the payment run.",
                    0.75m,
                    fabrikam,
                    [fabrikam])
            ],
            CancellationToken.None);

        var filtered = await service.ListAsync(companyId, "counterparty", "customer-1", includeResolved: false, CancellationToken.None);

        var insight = Assert.Single(filtered);
        Assert.Equal("Overdue receivables", insight.CheckName);
        Assert.Equal("overdue_receivables:customer-1", insight.ConditionKey);
        Assert.Equal("counterparty", insight.EntityType);
        Assert.Equal("customer-1", insight.EntityId);
        Assert.True(insight.ObservedAt > DateTime.MinValue);
        Assert.Equal("counterparty", insight.PrimaryEntity!.EntityType);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }
}