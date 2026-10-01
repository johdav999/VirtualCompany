using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;

namespace VirtualCompany.Finance.Tests;

public sealed class FinancialStatementWorkspaceTests
{
    [Fact]
    public void Reference_profit_and_loss_reconciles_cards_subtotals_and_accounts()
    {
        var current = ProfitLines();
        var prior = ProfitLines([4250000m,100000m,1600000m,850000m,1200000m,100000m,15000m,35000m,130000m]);
        var rows = FinancialStatementWorkspaceLayout.Build("profit-loss", current, prior, new Bas2026AccountingChartCatalog());
        Assert.Equal(4920000m, rows.Single(x => x.Key == "revenue").Amount);
        Assert.Equal(720000m, rows.Single(x => x.Key == "operating").Amount);
        Assert.Equal(700000m, rows.Single(x => x.Key == "before_tax").Amount);
        Assert.Equal(560000m, rows.Single(x => x.Key == "net").Amount);
        Assert.Equal(450000m, rows.Single(x => x.Key == "net").ComparisonAmount);
        Assert.Equal(24.4m, rows.Single(x => x.Key == "net").ChangePercent);
        Assert.Equal(-1320000m, rows.Single(x => x.Key == "staff").Amount);
        Assert.All(rows.Where(x => x.Kind == "detail"), r => Assert.Equal(r.Amount, r.Accounts.Sum(x => x.Amount)));
    }
    [Fact]
    public void Reversals_keep_their_sign_and_unknown_lines_remain_visible()
    {
        var lines = ProfitLines().ToList();
        lines.Add(new(Guid.NewGuid(), "9999", "Unclassified", "profit_and_loss_operating_expenses", "unknown", 10m, "SEK"));
        lines.Add(new(Guid.NewGuid(), "5011", "Expense reversal", "profit_and_loss_operating_expenses", "operating_expense", -500m, "SEK"));
        var rows = FinancialStatementWorkspaceLayout.Build("profit-loss", lines, null, new Bas2026AccountingChartCatalog());
        Assert.Equal(-10m, rows.Single(x => x.Key == "unclassified").Amount);
        Assert.Equal(560490m, rows.Single(x => x.Key == "net").Amount);
        Assert.Contains(rows.Single(x => x.Key == "external").Accounts, a => a.Amount == 500m);
        Assert.All(rows, r => Assert.Null(r.ComparisonAmount));
    }
    [Theory]
    [InlineData(100, 0)] [InlineData(100, -10)]
    public void Zero_and_negative_baselines_have_no_misleading_percentage(decimal amount, decimal baseline) =>
        Assert.Null(FinancialStatementWorkspaceLayout.ChangePercent(amount, baseline));
    [Fact]
    public void Balance_groups_preserve_opening_equity_and_do_not_invent_current_earnings()
    {
        FinanceStatementLineDto[] lines = [
            Line("1930", "balance_sheet_assets", "current_asset", 1000),
            Line("2081", "balance_sheet_equity", "equity", 100),
            Line("2091", "balance_sheet_equity", "equity", 500),
            Line("2099", "balance_sheet_equity", "equity", 400)];
        var rows = FinancialStatementWorkspaceLayout.Build("balance-sheet", lines, null, new Bas2026AccountingChartCatalog());
        Assert.Equal(1000, rows.Single(x => x.Key == "assets").Amount);
        Assert.Equal(1000, rows.Single(x => x.Key == "equity_debt").Amount);
        Assert.Equal(400, rows.Single(x => x.Key == "earnings").Amount);
        Assert.Single(rows.Single(x => x.Key == "earnings").Accounts);
    }
    [Fact]
    public void Catalog_subdivisions_are_not_applied_to_arbitrary_foreign_accounts()
    {
        var rows = FinancialStatementWorkspaceLayout.Build("balance-sheet", [Line("1930", "balance_sheet_assets", "current_asset", 100)], null, null);
        Assert.Equal(0, rows.Single(x => x.Key == "cash").Amount);
        Assert.Equal(100, rows.Single(x => x.Key == "other_assets").Amount);
    }
    [Fact]
    public void Csv_keeps_exact_values_versions_and_escapes_formula_injection()
    {
        var snapshot = new StatementWorkspaceSnapshot(Guid.NewGuid(), Guid.NewGuid(), 2, new string('a',64), DateTime.UtcNow, "profit_and_loss");
        var rows = FinancialStatementWorkspaceLayout.Build("profit-loss", [Line("3001", "profit_and_loss_revenue", "revenue", 125.55m) with { AccountName = "=HYPERLINK(\"bad\")" }], null, null);
        var report = new StatementWorkspaceReport(Guid.NewGuid(), snapshot.PeriodId, "profit-loss", "January", new(2026,1,1), new(2026,2,1),
            "SEK", true, true, "snapshot", snapshot, null,null,null,null,null, FinancialStatementWorkspaceLayout.Version,
            rows, [], null, [], [snapshot], "");
        var csv = FinancialStatementWorkspaceService.ToCsv(report);
        Assert.Contains("125.55", csv); Assert.Contains(snapshot.Id.ToString(),csv); Assert.Contains(snapshot.Checksum,csv);
        // Account code precedes the name, so the cell cannot begin with '='.
        Assert.Contains("3001 =HYPERLINK",csv); Assert.Contains("\"\"bad\"\"",csv);
    }
    public static IReadOnlyList<FinanceStatementLineDto> ProfitLines(decimal[]? amounts = null)
    {
        amounts ??= [4800000m,120000m,1800000m,960000m,1320000m,120000m,20000m,40000m,140000m];
        return [Line("3001","profit_and_loss_revenue","revenue",amounts[0]),Line("3990","profit_and_loss_revenue","revenue",amounts[1]),
            Line("4010","profit_and_loss_cost_of_sales","cost_of_sales",amounts[2]),Line("5010","profit_and_loss_operating_expenses","operating_expense",amounts[3]),
            Line("7010","profit_and_loss_operating_expenses","operating_expense",amounts[4]),Line("7830","profit_and_loss_operating_expenses","depreciation_and_amortization",amounts[5]),
            Line("8310","profit_and_loss_other_income_expense","non_operating_income",amounts[6]),Line("8410","profit_and_loss_other_income_expense","non_operating_expense",amounts[7]),
            Line("8910","profit_and_loss_taxes","income_tax",amounts[8])];
    }
    private static FinanceStatementLineDto Line(string code,string section,string classification,decimal amount) =>
        new(Guid.Parse($"00000000-0000-0000-0000-00000000{code}"),code,code,section,classification,amount,"SEK");
}
