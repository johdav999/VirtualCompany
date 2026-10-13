using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;

namespace VirtualCompany.Finance.Tests;

public sealed class FinancialStatementWorkspaceFixtureContractTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Period = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Previous = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("profit-loss")]
    [InlineData("balance-sheet")]
    public void Production_layout_and_csv_match_the_shared_component_contract_fixture(string kind)
    {
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "FinancialStatements", kind + ".json")));
        var actual = JsonSerializer.SerializeToNode(Fixture(kind));
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"The {kind} layout or CSV changed. Review the shared contract fixture and its component expectations.");
    }

    private static StatementWorkspaceReport Fixture(string kind)
    {
        FinanceStatementLineDto L(string code,string section,string classification,decimal amount) => new(Guid.Parse($"00000000-0000-0000-0000-00000000{code}"),code,
            code=="3001" ? "Försäljning Sverige" : code=="1930" ? "Företagskonto" : code,section,classification,amount,"SEK");
        IReadOnlyList<FinanceStatementLineDto> Profit(bool prior) => [L("3001","profit_and_loss_revenue","revenue",prior?4250000:4800000),L("3990","profit_and_loss_revenue","revenue",prior?100000:120000),
            L("4010","profit_and_loss_cost_of_sales","cost_of_sales",prior?1600000:1800000),L("5010","profit_and_loss_operating_expenses","operating_expense",prior?850000:960000),
            L("7010","profit_and_loss_operating_expenses","operating_expense",prior?1200000:1320000),L("7830","profit_and_loss_operating_expenses","depreciation_and_amortization",prior?100000:120000),
            L("8310","profit_and_loss_other_income_expense","non_operating_income",prior?15000:20000),L("8410","profit_and_loss_other_income_expense","non_operating_expense",prior?35000:40000),L("8910","profit_and_loss_taxes","income_tax",prior?130000:140000)];
        IReadOnlyList<FinanceStatementLineDto> Balance(bool prior) => [L("1210","balance_sheet_assets","non_current_asset",prior?1000000:900000),L("1410","balance_sheet_assets","current_asset",prior?480000:540000),
            L("1510","balance_sheet_assets","current_asset",prior?620000:720000),L("1930","balance_sheet_assets","current_asset",prior?800000:1080000),L("2081","balance_sheet_equity","equity",100000),
            L("2091","balance_sheet_equity","equity",prior?450000:900000),L("2099","balance_sheet_equity","equity",prior?450000:560000),L("2350","balance_sheet_liabilities","non_current_liability",prior?1100000:900000),
            L("2440","balance_sheet_liabilities","current_liability",prior?500000:420000),L("2510","balance_sheet_liabilities","current_liability",prior?300000:360000)];
        var rows = FinancialStatementWorkspaceLayout.Build(kind,kind=="profit-loss"?Profit(false):Balance(false),kind=="profit-loss"?Profit(true):Balance(true),new Bas2026AccountingChartCatalog());
        var keys = kind=="profit-loss"?new[]{"sales","operating","net"}:["assets","equity","debt"];
        var report = new StatementWorkspaceReport(Company,Period,kind,"Jan–aug 2026",new(2026,1,1),new(2026,9,1),"SEK",false,false,"posted_journals",null,
            Previous,kind=="profit-loss"?"Jan–aug 2025":"31 dec 2025",kind=="profit-loss"?new(2025,1,1):new(2025,12,1),kind=="profit-loss"?new(2025,9,1):new(2026,1,1),null,
            FinancialStatementWorkspaceLayout.Version,rows,keys.Select(k=>rows.Single(r=>r.Key==k)).ToArray(),kind=="balance-sheet"?0:null,[],[],"");
        return report with { CsvContent=FinancialStatementWorkspaceService.ToCsv(report) };
    }
}
