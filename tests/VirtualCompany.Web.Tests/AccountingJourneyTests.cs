using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AccountingJourneyTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Statement_to_journal_return_keeps_exact_snapshot_comparison_and_overview()
    {
        var period = Guid.NewGuid(); var snapshot = Guid.NewGuid();
        var overview = DashboardRoutes.BuildMonthlyPath(Company, "finance", 2026, 9);
        var source = $"/finance/accounting/reports?view=balance-sheet&companyId={Company}&periodId={period}&snapshotId={snapshot}&comparisonId=none&accountCode=1930&financeSource=operational&returnUrl={Uri.EscapeDataString(overview)}";
        var target = AccountingJourneyRoutes.Build("/finance/accounting/journals?journalId=" + Guid.NewGuid(), Company, "https://test.local" + source);
        var query = HttpUtility.ParseQueryString(new Uri("https://test.local" + target).Query);
        Assert.Equal(period.ToString(), query["periodId"]);
        Assert.Equal("operational", query["financeSource"]);
        Assert.Equal(Uri.UnescapeDataString(source), Uri.UnescapeDataString(FinanceJourneyRoutes.Back("https://test.local" + target, Company)));
        Assert.Equal(overview, DashboardRoutes.OverviewPathFromLocation("https://test.local" + target, Company));
    }

    [Theory]
    [InlineData("https://evil.test/finance/accounting/reports")]
    [InlineData("/finance/accounting/reports?companyId=22222222-2222-2222-2222-222222222222")]
    [InlineData("/finance/accounting/../reports?companyId=11111111-1111-1111-1111-111111111111")]
    public void Untrusted_accounting_origin_is_rejected(string origin) => Assert.Null(AccountingJourneyRoutes.Normalize(origin, Company));

    [Fact]
    public void General_ledger_returns_to_the_selected_statement_view()
    {
        var source = $"/finance/accounting/reports?view=profit-loss&companyId={Company}&periodId={Guid.NewGuid()}&comparisonId=none&accountCode=3001";
        var target = AccountingJourneyRoutes.Build("/finance/accounting/reports?view=ledger&accountId=" + Guid.NewGuid(), Company, "https://test.local" + source);
        Assert.Equal(Uri.UnescapeDataString(source), Uri.UnescapeDataString(FinanceJourneyRoutes.Back("https://test.local" + target, Company)));
    }

    [Fact]
    public void Journal_return_preserves_the_statements_bounded_close_parent()
    {
        var close = $"/finance/accounting/close-workspace?companyId={Company}&periodId={Guid.NewGuid()}";
        var statement = AccountingJourneyRoutes.Build("/finance/accounting/reports?view=profit-loss&comparisonId=none&accountCode=3001", Company, "https://test.local" + close);
        var journal = AccountingJourneyRoutes.Build("/finance/accounting/journals?journalId=" + Guid.NewGuid(), Company, "https://test.local" + statement);
        var returnedStatement = FinanceJourneyRoutes.Back("https://test.local" + journal, Company);
        Assert.Equal(Uri.UnescapeDataString(statement), Uri.UnescapeDataString(returnedStatement));
        Assert.Equal(Uri.UnescapeDataString(close), Uri.UnescapeDataString(FinanceJourneyRoutes.Back("https://test.local" + returnedStatement, Company)));
    }

    [Theory]
    [InlineData("/work")]
    [InlineData("/accountant/portfolio")]
    [InlineData("/documents/07070707-0707-0707-0707-070707070707")]
    public void Close_evidence_destinations_keep_their_canonical_path(string destination)
    {
        var source = $"https://test.local/finance/accounting/close-workspace?companyId={Company}&periodId={Guid.NewGuid()}";
        var result = AccountingJourneyRoutes.Build(destination, Company, source);
        Assert.Equal(destination, new Uri("https://test.local" + result).AbsolutePath);
        Assert.Equal(Company.ToString(), HttpUtility.ParseQueryString(new Uri("https://test.local" + result).Query)["companyId"]);
    }

    [Fact]
    public void Accounting_navigation_carries_the_close_period_and_returns_to_the_close()
    {
        using var context = new TestContext();
        VirtualCompany.Api.Tests.WebTestContextServiceRegistration.AddVirtualCompanyWebPresentationServices(context);
        var navigation = context.Services.GetService(typeof(NavigationManager)) as NavigationManager;
        var source = $"/finance/accounting/close-workspace?companyId={Company}&periodId={Guid.NewGuid()}";
        navigation!.NavigateTo(source);
        var cut = context.RenderComponent<AccountingNavigation>(p => p.Add(x => x.CompanyId, Company));
        foreach (var link in cut.FindAll("a").Where(x => !x.ClassList.Contains("active")))
            Assert.Equal(source, FinanceJourneyRoutes.Back("http://localhost" + link.GetAttribute("href"), Company));
    }
}
