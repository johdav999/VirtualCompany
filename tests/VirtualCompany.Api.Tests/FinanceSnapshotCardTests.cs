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
                Cash = 0m,
                BurnRate = 0m,
                RunwayDays = null,
                RiskLevel = "missing",
                HasFinanceData = false,
                Currency = "USD",
                AsOfUtc = SnapshotUtc
            }));

        var cta = cut.Find("[data-testid='connect-accounting-cta']");
        Assert.Equal("Connect accounting", cta.TextContent.Trim());
        Assert.Equal(DashboardRoutes.BuildFinancePath(companyId, FinanceRoutes.Home, "open-workspace", "this-month"), cta.GetAttribute("href"));
        Assert.NotEmpty(cut.FindAll("[data-testid='finance-snapshot-empty']"));
    }

    [Fact]
    public void Finance_snapshot_card_shows_cash_runway_and_risk_badge_when_finance_data_is_available()
    {
        using var context = new TestContext();
        var companyId = Guid.Parse("5d7ae44d-04db-43a0-ab8d-97a21544d9ba");

        var cut = context.RenderComponent<FinanceSnapshotCard>(parameters => parameters
            .Add(component => component.CompanyId, companyId)
            .Add(component => component.Snapshot, new DashboardFinanceSnapshotViewModel
            {
                CompanyId = companyId,
                Cash = 125400.25m,
                BurnRate = 5700m,
                RunwayDays = 22,
                RiskLevel = "critical",
                HasFinanceData = true,
                Currency = "USD",
                AsOfUtc = SnapshotUtc
            }));

        Assert.Contains("USD 125,400.25", cut.Find("[data-testid='executive-cockpit-cash-position']").TextContent);
        Assert.Contains("22 days", cut.Find("[data-testid='executive-cockpit-runway']").TextContent);

        var riskBadge = cut.Find("[data-testid='executive-cockpit-runway-status']");
        Assert.Contains("Critical", riskBadge.TextContent);
        Assert.Contains("finance-risk-pill--critical", riskBadge.ClassName);

        Assert.Contains("Burn rate: USD 5,700.00 per day.", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='connect-accounting-cta']"));
    }
}