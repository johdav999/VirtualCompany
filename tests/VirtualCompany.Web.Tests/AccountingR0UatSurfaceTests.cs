using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VirtualCompany.Web.Tests;

public sealed partial class AccountingR0UatSurfaceTests
{
    private static readonly string[] LocalizedAccountingPages =
    [
        "AccountingAccountsPage.razor",
        "AccountingConnectionsPage.razor",
        "AccountingJournalsPage.razor",
        "AccountingPeriodsPage.razor",
        "AccountingReconciliationPage.razor",
        "AccountingReportsPage.razor",
        "AccountingSetupPage.razor",
        "ManualJournalWorkbenchPage.razor",
        "SupplierSubscriptionsPage.razor",
        "FinanceProviderManagementPage.razor",
        "FinanceWorkerOperationsPage.razor"
    ];

    [Fact]
    public void Accounting_routes_have_no_unlocalized_client_owned_visible_english()
    {
        var violations = new List<string>();
        foreach (var file in LocalizedAccountingPages)
        {
            var markup = Read(file);
            foreach (Match match in VisibleEnglish().Matches(markup))
            {
                var text = Regex.Replace(match.Groups[1].Value, "\\s+", " ").Trim();
                if (text.Length > 1 && text != "L" && text != "J") violations.Add($"{file}: {text}");
            }
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void Accounting_uat_resources_are_complete_in_english_and_swedish()
    {
        var financeRoot = Path.Combine(Root(), "src", "VirtualCompany.Web", "Localization", "Finance");
        var english = ReadResources(Path.Combine(financeRoot, "FinanceResources.resx"));
        var swedish = ReadResources(Path.Combine(financeRoot, "FinanceResources.sv-SE.resx"));
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), swedish.Keys.Order(StringComparer.Ordinal));

        foreach (var file in LocalizedAccountingPages)
        {
            var keys = ResourceKey().Matches(Read(file)).Select(x => x.Groups[1].Value)
                .Distinct(StringComparer.Ordinal);
            Assert.All(keys, key =>
            {
                Assert.True(english.ContainsKey(key), $"{file} uses missing English key {key}.");
                Assert.True(swedish.ContainsKey(key), $"{file} uses missing Swedish key {key}.");
            });
        }
    }

    [Fact]
    public void Accounting_drill_down_rows_and_async_regions_expose_keyboard_and_status_semantics()
    {
        var reports = Read("AccountingReportsPage.razor");
        var reconciliation = Read("AccountingReconciliationPage.razor");
        var workers = Read("FinanceWorkerOperationsPage.razor");

        Assert.Contains("role=\"button\" tabindex=\"0\"", reports, StringComparison.Ordinal);
        Assert.Contains("aria-selected", reports, StringComparison.Ordinal);
        Assert.Contains("aria-busy", reports, StringComparison.Ordinal);
        Assert.Contains("role=\"button\" tabindex=\"0\"", reconciliation, StringComparison.Ordinal);
        Assert.Contains("aria-selected", reconciliation, StringComparison.Ordinal);
        Assert.Contains("aria-busy", reconciliation, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", workers, StringComparison.Ordinal);
        Assert.Contains("aria-pressed", workers, StringComparison.Ordinal);
    }

    [Fact]
    public void Accounting_empty_and_access_states_do_not_require_unavailable_prerequisites()
    {
        var reconciliationCode = ReadCodeBehind("AccountingReconciliationPage.razor.cs");
        var emptyStateGuard = reconciliationCode.IndexOf(
            "if (!TransactionId.HasValue && Workspace.Items.Count == 0) return;",
            StringComparison.Ordinal);
        var fiscalYearRequest = reconciliationCode.IndexOf(
            "GetAccountingFiscalYearsAsync(companyId)",
            StringComparison.Ordinal);

        Assert.True(emptyStateGuard >= 0 && emptyStateGuard < fiscalYearRequest,
            "The empty reconciliation route must render before accounting-setup prerequisites are requested.");

        var workers = Read("FinanceWorkerOperationsPage.razor");
        Assert.Contains("Message=\"@AccessState.Message\"", workers, StringComparison.Ordinal);
        Assert.Contains("Companies=\"@AccessState.AvailableCompanies\"", workers, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanySelectionRequiredState State=", workers, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ReadResources(string path) => XDocument.Load(path).Root!
        .Elements("data").ToDictionary(x => (string)x.Attribute("name")!, x => (string)x.Element("value")!,
            StringComparer.Ordinal);
    private static string Read(string file) => File.ReadAllText(Path.Combine(Root(), "src", "VirtualCompany.Web", "Pages", "Finance", file));
    private static string ReadCodeBehind(string file) => Read(file);
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }

    [GeneratedRegex(@"<(?:""[^""]*""|'[^']*'|[^'"">])*?>\s*(?![^<]*@)([^<{}]*[A-Za-z][^<{}]*?)\s*(?=<)", RegexOptions.CultureInvariant)]
    private static partial Regex VisibleEnglish();
    [GeneratedRegex("FinanceText\\[\\\"([^\\\"]+)\\\"")]
    private static partial Regex ResourceKey();
}
