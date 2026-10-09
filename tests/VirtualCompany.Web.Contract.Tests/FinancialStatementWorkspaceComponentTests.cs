using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Localization.Formatting;

namespace VirtualCompany.Web.Tests;

public sealed class FinancialStatementWorkspaceComponentTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Period = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Previous = Guid.Parse("33333333-3333-3333-3333-333333333333");
    [Fact]
    public void Reports_render_localized_comparison_and_expand_actual_accounts()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        var cut = Render(context, "profit-loss");
        Assert.Equal("Resultaträkning",cut.Find("h1").TextContent);
        Assert.Equal(3,cut.FindAll(".statement-kpi").Count);
        Assert.Contains("560",cut.FindAll(".statement-kpi")[2].TextContent);
        Assert.Single(cut.FindAll(".statement-account"));
        cut.Find("button[aria-label='Visa konton för Nettoomsättning']").Click();
        Assert.Empty(cut.FindAll(".statement-account"));
        cut.Find("button[aria-label='Visa konton för Personalkostnader']").Click();
        Assert.Contains("7010",Assert.Single(cut.FindAll(".statement-account")).TextContent);
        Assert.Equal("negative",cut.FindAll(".statement-row").Single(r => r.TextContent.Contains("Personalkostnader")).QuerySelector("td:last-child")!.ClassName);
    }
    [Fact]
    public void Row_selection_and_filters_emit_the_exact_context_without_switching_report()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        StatementWorkspaceRow? selected = null; StatementWorkspaceAccount? account = null; string? comparison = null;
        var cut = Render(context,"profit-loss",p => p.Add(x => x.OnRowSelected,r => selected = r)
            .Add(x => x.OnAccountSelected,a => account = a).Add(x => x.OnComparisonChanged,s => comparison = s));
        cut.FindAll(".statement-select").Single(b => b.TextContent == "Nettoomsättning").Click();
        Assert.Equal("sales",selected!.Key);
        cut.Find(".statement-account button").Click(); Assert.Equal("3001",account!.Code);
        cut.Find("select[aria-label='Jämförelseperiod']").Change(Previous.ToString());
        Assert.Equal(Previous.ToString(),comparison); Assert.Equal("Resultaträkning",cut.Find("h1").TextContent);
        Assert.Contains($"companyId={Company}",cut.Find(".statement-tabs a.active").GetAttribute("href"));
    }
    [Fact]
    public void Failed_balance_and_missing_comparison_never_display_success()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        var fixture = Fixture("balance-sheet") with { BalanceDifference = 50m, ComparisonName = null };
        var cut = Render(context,"balance-sheet",report:fixture);
        Assert.Contains("Balansen behöver granskas",cut.Find(".statement-balance").TextContent);
        Assert.DoesNotContain("balanced",cut.Find(".statement-balance").ClassList);
        Assert.Contains("Välj en jämförelseperiod",cut.Find(".statement-comparison-note").TextContent);
    }
    [Fact]
    public void Print_export_and_saved_report_controls_are_connected()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        var prints = 0; var exports = 0; Guid? snapshot = null;
        var saved = new StatementWorkspaceSnapshot(Guid.NewGuid(),Period,2,new string('a',64),new(2026,9,1),"profit_and_loss");
        var cut = Render(context,"profit-loss",p => p.Add(x => x.OnPrint,() => prints++).Add(x => x.OnExport,() => exports++).Add(x => x.OnSnapshotChanged,id => snapshot = id),
            Fixture("profit-loss") with { SavedReports = [saved] });
        cut.FindAll("button").Single(b => b.TextContent.Contains("Skriv ut")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Exportera")).Click();
        cut.FindAll(".statement-history button").Single(b => b.TextContent.Contains("v2")).Click();
        Assert.Equal(1,prints); Assert.Equal(1,exports); Assert.Equal(saved.Id,snapshot);
    }
    [Fact]
    public void Evidence_links_preserve_company_period_and_snapshot_values()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        var report = Fixture("balance-sheet"); var account = report.Rows.Single(r => r.Key == "cash").Accounts[0];
        var cut = Render(context,"balance-sheet",p => p.Add(x => x.SelectedAccount,account)
            .Add(x => x.Evidence,new StatementEvidenceResponse { OpeningBalanceAdjustment=800000m,JournalLineTotal=280000m,ReconciliationTotal=1080000m,
                JournalEntries=[new() { LedgerEntryId=Guid.NewGuid(),EntryNumber="A142",TotalContributionAmount=280000m }] }));
        Assert.Contains("800",cut.Find(".statement-evidence").TextContent);
        var link = cut.FindAll(".statement-evidence a").Single(a => a.TextContent.Contains("Visa huvudbok")).GetAttribute("href");
        Assert.Contains($"companyId={Company}",link); Assert.Contains($"periodId={Period}",link); Assert.Contains($"accountId={account.AccountId}",link);
        Assert.Contains("A142",cut.Markup);
    }
    [Fact]
    public async Task Typed_client_passes_company_comparison_snapshot_and_decodes_journal_entries()
    {
        var snapshot = Guid.NewGuid(); var urls = new List<string>();
        using var http = new HttpClient(new Handler(req =>
        {
            Assert.Equal(Company.ToString(),Assert.Single(req.Headers.GetValues("X-Company-Id")));
            urls.Add(req.RequestUri!.ToString());
            return req.RequestUri.AbsolutePath.Contains("drilldown")
                ? new(HttpStatusCode.OK) { Content=JsonContent.Create(new { journalEntries = new[] { new { ledgerEntryId=Guid.NewGuid(),entryNumber="A142" } } }) }
                : new(HttpStatusCode.OK) { Content=JsonContent.Create(Fixture("profit-loss")) };
        })) { BaseAddress=new("https://test.local/") };
        var client = new FinanceApiClient(http);
        await client.GetStatementWorkspaceAsync(Company,Period,"profit-loss",Previous,snapshot,snapshot);
        var evidence = await client.GetStatementEvidenceAsync(Company,Period,"profit-loss","3001",snapshot);
        Assert.Contains($"comparisonFiscalPeriodId={Previous:D}",urls[0]); Assert.Contains($"snapshotId={snapshot:D}",urls[0]);
        Assert.Contains($"snapshots/{snapshot:D}/lines/3001/drilldown",urls[1]); Assert.Equal("A142",Assert.Single(evidence!.JournalEntries).EntryNumber);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStatementWorkspaceAsync(Guid.Empty,Period,"profit-loss"));
    }
    [Fact]
    public void Expense_evidence_uses_statement_signs_and_detects_changed_live_values()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        var account = Fixture("profit-loss").Rows.Single(x => x.Key == "external").Accounts[0];
        var cut = Render(context,"profit-loss",p => p.Add(x => x.SelectedAccount,account).Add(x => x.Evidence,
            new StatementEvidenceResponse { SelectedLine = new() { ReportSection="profit_and_loss_operating_expenses",LineClassification="operating_expense",Amount=970000m },
                JournalLineTotal=970000m,ReconciliationTotal=970000m }));
        Assert.Contains("−970",cut.Find(".statement-evidence").TextContent);
        Assert.Contains("−960",cut.Find(".statement-evidence").TextContent);
        Assert.Contains("10",cut.Find(".statement-evidence .negative").TextContent);
    }
    [Fact]
    public void Workspace_discards_a_late_response_after_company_context_changes()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherCompany = Guid.NewGuid();
        using var http = new HttpClient(new AsyncHandler(req => req.RequestUri!.AbsolutePath.Contains(Company.ToString())
            ? late.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Fixture("profit-loss") with { CompanyId = otherCompany, Currency = "EUR" }) })))
            { BaseAddress = new("https://test.local/") };
        context.Services.AddSingleton(new FinanceApiClient(http));
        var cut = context.RenderComponent<FinancialStatementWorkspace>(p => p.Add(x => x.CompanyId,Company)
            .Add(x => x.InitialPeriodId,Period).Add(x => x.Periods,Periods("profit-loss")));
        Assert.Contains("Hämtar",cut.Markup);
        cut.SetParametersAndRender(p => p.Add(x => x.CompanyId,otherCompany));
        cut.WaitForAssertion(() => Assert.Equal("EUR",cut.Find(".statement-currency").TextContent));
        late.SetResult(new(HttpStatusCode.OK) { Content=JsonContent.Create(Fixture("profit-loss")) });
        cut.WaitForAssertion(() => Assert.Equal("EUR",cut.Find(".statement-currency").TextContent));
    }
    [Fact]
    public void Workspace_exposes_missing_periods_and_disables_output_actions()
    {
        using var culture = new SwedishCulture(); using var context = Context();
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No request expected"))) { BaseAddress=new("https://test.local/") };
        context.Services.AddSingleton(new FinanceApiClient(http));
        var cut = context.RenderComponent<FinancialStatementWorkspace>(p => p.Add(x => x.CompanyId,Company));
        Assert.Contains("Inga räkenskapsperioder",cut.Find("[role='alert']").TextContent);
        Assert.All(cut.FindAll(".statement-toolbar button"), b => Assert.True(b.HasAttribute("disabled")));
    }
    [Fact]
    public void Reload_restores_exact_statement_selection_and_export_rereads_its_authorized_scope()
    {
        using var context = Context();
        var snapshot = Guid.NewGuid(); var requests = new List<string>();
        using var http = new HttpClient(new Handler(req =>
        {
            requests.Add(req.RequestUri!.ToString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Fixture("profit-loss") with { CsvContent = requests.Count == 1 ? "old" : "fresh" }) };
        })) { BaseAddress = new("https://test.local/") };
        context.Services.AddSingleton(new FinanceApiClient(http)); context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(
            $"/finance/accounting/reports?companyId={Company}&periodId={Period}&view=profit-loss&comparisonId={Previous}&snapshotId={snapshot}");
        var cut = context.RenderComponent<FinancialStatementWorkspace>(p => p.Add(x => x.CompanyId, Company)
            .Add(x => x.InitialPeriodId, Period).Add(x => x.Periods, Periods("profit-loss")));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Export")).Click();
        cut.WaitForAssertion(() => Assert.Equal(2, requests.Count));
        Assert.All(requests, url => { Assert.Contains($"snapshotId={snapshot}", url); Assert.Contains($"comparisonFiscalPeriodId={Previous}", url); });
        Assert.Equal("fresh", context.JSInterop.Invocations["downloadReport"].Single().Arguments[1]);
    }

    [Fact]
    public void Revoked_statement_export_shows_failure_and_never_downloads_cached_rows()
    {
        using var context = Context(); var reads = 0;
        using var http = new HttpClient(new Handler(_ => ++reads == 1
            ? new(HttpStatusCode.OK) { Content = JsonContent.Create(Fixture("profit-loss")) }
            : new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { detail = "Accounting access denied." }) })) { BaseAddress = new("https://test.local/") };
        context.Services.AddSingleton(new FinanceApiClient(http)); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = context.RenderComponent<FinancialStatementWorkspace>(p => p.Add(x => x.CompanyId, Company)
            .Add(x => x.InitialPeriodId, Period).Add(x => x.Periods, Periods("profit-loss")));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Export")).Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='alert']")));
        Assert.DoesNotContain(context.JSInterop.Invocations, call => call.Identifier == "downloadReport");
    }

    [Fact]
    public void Reload_of_an_already_persisted_selection_does_not_redirect_to_itself()
    {
        using var context = Context();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(Fixture("profit-loss")) }))
            { BaseAddress = new("https://test.local/") };
        context.Services.AddSingleton(new FinanceApiClient(http));
        var navigation = (Bunit.TestDoubles.FakeNavigationManager)context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo($"/finance/accounting/reports?companyId={Company}&periodId={Period}&view=profit-loss&comparisonId=none");
        var historyCount = navigation.History.Count;
        var cut = context.RenderComponent<FinancialStatementWorkspace>(p => p.Add(x => x.CompanyId, Company)
            .Add(x => x.InitialPeriodId, Period).Add(x => x.Periods, Periods("profit-loss")));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".statement-kpi")));
        Assert.Equal(historyCount, navigation.History.Count);
    }
    [Fact]
    public void Export_visual_fixtures_from_the_production_components()
    {
        var directory = Environment.GetEnvironmentVariable("VC_STATEMENT_UAT_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        using var culture = new SwedishCulture(); using var context = Context();
        Directory.CreateDirectory(directory);
        foreach (var kind in new[] { "profit-loss", "balance-sheet" })
        {
            var report = Fixture(kind);
            var account = report.Rows.Single(x => x.Key == (kind == "profit-loss" ? "sales" : "cash")).Accounts[0];
            var cut = Render(context,kind,p => p.Add(x => x.SelectedAccount,account)
                .Add(x => x.Evidence,new StatementEvidenceResponse { OpeningBalanceAdjustment=kind == "balance-sheet" ? 800000m : 0m,
                    JournalLineTotal=kind == "balance-sheet" ? 280000m : 4800000m,ReconciliationTotal=account.Amount }));
            File.WriteAllText(Path.Combine(directory,kind+".html"),cut.Markup);
            File.WriteAllText(Path.Combine(directory,kind+".json"),JsonSerializer.Serialize(report));
        }
    }
    private static IRenderedComponent<FinancialStatementReport> Render(TestContext c,string kind,
        Action<ComponentParameterCollectionBuilder<FinancialStatementReport>>? extra=null, StatementWorkspaceReport? report=null) => c.RenderComponent<FinancialStatementReport>(p =>
        {
            p.Add(x => x.CompanyId,Company).Add(x => x.ReportKind,kind).Add(x => x.Report,report ?? Fixture(kind)).Add(x => x.Periods,Periods(kind))
                .Add(x => x.SelectedPeriodId,Period).Add(x => x.ComparisonPeriodId,Previous);
            extra?.Invoke(p);
        });
    private static IReadOnlyList<AccountingPeriodResponse> Periods(string kind) => [new() { Id=Period,Name="Jan–aug 2026",StartDate=new(2026,1,1),EndDate=new(2026,8,31) },
        new() { Id=Previous,Name=kind == "profit-loss" ? "Jan–aug 2025" : "December 2025",StartDate=kind == "profit-loss" ? new(2025,1,1) : new(2025,12,1),EndDate=kind == "profit-loss" ? new(2025,8,31) : new(2025,12,31) }];
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
    private static TestContext Context()
    {
        var c=new TestContext();c.Services.AddLocalization();var formatting=new CompanyPresentationContext();formatting.SetFormattingCulture("sv-SE");
        c.Services.AddSingleton<ICompanyPresentationContext>(formatting);c.Services.AddSingleton<ILocalDateTimeFormatter,LocalDateTimeFormatter>();
        c.Services.AddSingleton<INumberFormatter,NumberFormatter>();c.Services.AddSingleton<IMoneyFormatter,MoneyFormatter>();return c;
    }
    private sealed class SwedishCulture:IDisposable { private readonly CultureInfo PreviousCulture=CultureInfo.CurrentUICulture; public SwedishCulture()=>CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("sv-SE");public void Dispose()=>CultureInfo.CurrentUICulture=PreviousCulture; }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>Task.FromResult(reply(r)); }
    private sealed class AsyncHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> reply):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>reply(r); }
}
