using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentDecisionServiceTests
{
    [Fact]
    public async Task Payment_run_enforces_cash_reserve_after_deterministic_ordering()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var supplier = new FinanceCounterparty(Guid.NewGuid(), companyId, "Supplier", "supplier");
        var account = new FinanceAccount(Guid.NewGuid(), companyId, "1930", "Bank", "bank", "SEK", 0m, DateTime.UtcNow.AddYears(-1));
        var now = new DateTime(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);
        var first = new FinanceBill(Guid.NewGuid(), companyId, supplier.Id, "B-1", now.AddDays(-10), now.AddDays(-2), 300m, "SEK", "approved");
        var second = new FinanceBill(Guid.NewGuid(), companyId, supplier.Id, "B-2", now.AddDays(-8), now.AddDays(-1), 300m, "SEK", "approved");
        db.AddRange(supplier, account, first, second,
            new FinanceBalance(Guid.NewGuid(), companyId, account.Id, now, 1000m, "SEK"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AnalyzePaymentRunAsync(companyId, Guid.NewGuid(), null,
            new FinancePaymentRunAnalysisRequest(now.AddDays(2), 500m, AsOfUtc: now), CancellationToken.None);

        Assert.Equal(FinancePaymentRunGroups.Pay, result.Items.Single(x => x.BillId == first.Id).Group);
        Assert.Equal(FinancePaymentRunGroups.Defer, result.Items.Single(x => x.BillId == second.Id).Group);
        Assert.Equal(300m, result.RecommendedOutflowByCurrency["SEK"]);
        Assert.Equal(700m, result.CashAfterByCurrency["SEK"]);
    }

    [Fact]
    public async Task Accounting_treatment_excludes_control_and_liability_accounts()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var supplier = new FinanceCounterparty(Guid.NewGuid(), companyId, "Supplier", "supplier");
        var bill = new FinanceBill(Guid.NewGuid(), companyId, supplier.Id, "B-1", DateTime.UtcNow.AddDays(-3),
            DateTime.UtcNow.AddDays(10), 1200m, "SEK", "approved");
        var expense = new FinanceAccount(Guid.NewGuid(), companyId, "6540", "IT services", "expense", "SEK", 0m, DateTime.UtcNow.AddYears(-1));
        var liability = new FinanceAccount(Guid.NewGuid(), companyId, "2440", "Trade payables", "liability", "SEK", 0m, DateTime.UtcNow.AddYears(-1));
        db.AddRange(supplier, bill, expense, liability);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RecommendAccountingTreatmentAsync(companyId, Guid.NewGuid(), null,
            new FinanceAccountingTreatmentRequest(bill.Id), CancellationToken.None);

        Assert.Contains(result.Candidates, x => x.AccountCode == "6540");
        Assert.DoesNotContain(result.Candidates, x => x.AccountCode == "2440");
        Assert.Contains(result.ExcludedCandidates, x => x.AccountCode == "2440" && x.ReasonCode == "liability_account_not_expense");
        Assert.Contains("Authoritative VAT treatment evidence", result.MissingEvidence);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task Close_period_choices_are_tenant_scoped_and_newest_first()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var older = new FiscalPeriod(Guid.NewGuid(), companyId, "June 2026",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = new FiscalPeriod(Guid.NewGuid(), companyId, "July 2026",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var other = new FiscalPeriod(Guid.NewGuid(), otherCompanyId, "Other tenant",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        db.AddRange(older, newer, other);
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListClosePeriodsAsync(companyId, CancellationToken.None);

        Assert.Equal([newer.Id, older.Id], result.Select(x => x.Id).ToArray());
        Assert.DoesNotContain(result, x => x.Id == other.Id);
    }

    private static FinanceAgentDecisionService CreateService(VirtualCompanyDbContext db) => new(
        db, new StubAnalysis(), null!, null!, null!, null!, null!, null!);

    private static VirtualCompanyDbContext CreateDb() => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class StubAnalysis : IFinanceAgentAnalysisService
    {
        public Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
            RoleAgentAnalysisRequest request, CancellationToken cancellationToken) => Task.FromResult(
            new RoleAgentAnalysisResult(Guid.NewGuid(), "test", AgentAiRunStatuses.Completed, "Test advice", .8m,
                request.AsOfUtc ?? DateTime.UtcNow, [], [], [], [], [], [], false));
    }
}
