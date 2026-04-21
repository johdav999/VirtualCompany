using Bunit;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSnapshotCardTests
{
    private static readonly DateTime SnapshotUtc = new(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Finance_snapshot_card_shows_connect_accounting_cta_when_finance_data_is_missing()
    {
        using var context = new TestContext();
        var companyId = Guid.Parse("9e57e8b8-f4d9-4d2c-9f09-f58d25e26be9");

        var cut = context.RenderComponent<FinanceSnapshotCard>(parameters => parameters
            .Add(component => component.CompanyId, companyId)
            .Add(component => component.Snapshot, new DashboardFinanceSnapshotViewModel
            {
                CompanyId = companyId,
                CurrentCashBalance = 0m,
                ExpectedIncomingCash = 0m,
                ExpectedOutgoingCash = 0m,
                OverdueReceivables = 0m,
                UpcomingPayables = 0m,
                Currency = "USD",
                AsOfUtc = SnapshotUtc,
                UpcomingWindowDays = 30,
                Cash = 0m,
                BurnRate = 0m,
                RunwayDays = null,
                RiskLevel = "missing",
                HasFinanceData = false,
            }));

        var cta = cut.Find("[data-testid='connect-accounting-cta']");
        Assert.Equal("Connect accounting", cta.TextContent.Trim());
        Assert.Equal(DashboardRoutes.BuildFinancePath(companyId, FinanceRoutes.Home, "open-workspace", "this-month"), cta.GetAttribute("href"));
        Assert.NotEmpty(cut.FindAll("[data-testid='finance-snapshot-empty']"));
    }

    [Fact]
    public void Finance_snapshot_card_shows_all_cash_metrics_when_finance_data_is_available()
    {
        using var context = new TestContext();
        var companyId = Guid.Parse("5d7ae44d-04db-43a0-ab8d-97a21544d9ba");

        var cut = context.RenderComponent<FinanceSnapshotCard>(parameters => parameters
            .Add(component => component.CompanyId, companyId)
            .Add(component => component.Snapshot, new DashboardFinanceSnapshotViewModel
            {
                CompanyId = companyId,
                CurrentCashBalance = 125400.25m,
                ExpectedIncomingCash = 18400m,
                ExpectedOutgoingCash = 12650m,
                OverdueReceivables = 7200m,
                UpcomingPayables = 24300m,
                Currency = "USD",
                AsOfUtc = SnapshotUtc,
                UpcomingWindowDays = 30,
                Cash = 125400.25m,
                BurnRate = 5700m,
                RunwayDays = 22,
                RiskLevel = "critical",
                HasFinanceData = true,
            }));

        Assert.Contains("USD 125,400.25", cut.Find("[data-testid='executive-cockpit-cash-position']").TextContent);
        Assert.Contains("USD 18,400.00", cut.Find("[data-testid='executive-cockpit-expected-incoming-cash']").TextContent);
        Assert.Contains("USD 12,650.00", cut.Find("[data-testid='executive-cockpit-expected-outgoing-cash']").TextContent);
        Assert.Contains("USD 7,200.00", cut.Find("[data-testid='executive-cockpit-overdue-receivables']").TextContent);
        Assert.Contains("USD 24,300.00", cut.Find("[data-testid='executive-cockpit-upcoming-payables']").TextContent);
        Assert.Contains("Upcoming window: next 30 day(s).", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='connect-accounting-cta']"));
    }
}