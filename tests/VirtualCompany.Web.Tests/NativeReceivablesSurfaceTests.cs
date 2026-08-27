using System.Xml.Linq;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Localization.Formatting;

namespace VirtualCompany.Web.Tests;

public sealed class NativeReceivablesSurfaceTests
{
    [Fact]
    public void Receivables_navigation_keeps_company_context_across_all_native_views()
    {
        using var context = new TestContext();
        context.Services.AddLocalization();
        var presentationContext = new CompanyPresentationContext();
        presentationContext.SetFormattingCulture("en-US");
        context.Services.AddSingleton<ICompanyPresentationContext>(presentationContext);
        context.Services.AddSingleton<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
        context.Services.AddSingleton<INumberFormatter, NumberFormatter>();
        context.Services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        var companyId = Guid.NewGuid();

        var cut = context.RenderComponent<ReceivablesNavigation>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.ActiveView, "collections"));

        var links = cut.FindAll("a");
        Assert.Equal(6, links.Count);
        Assert.All(links, link => Assert.Contains($"companyId={companyId}", link.GetAttribute("href"), StringComparison.Ordinal));
        Assert.Single(links, link => link.ClassList.Contains("active") && link.GetAttribute("href")!.Contains("view=collections", StringComparison.Ordinal));
    }

    [Fact]
    public void Prompt_9_surfaces_use_server_backed_actions_and_complete_localization()
    {
        var draft = Read("src", "VirtualCompany.Web", "Pages", "Finance", "InvoiceDraftPage.razor");
        var receivables = Read("src", "VirtualCompany.Web", "Pages", "Finance", "ReceivablesPage.razor");
        var billing = Read("src", "VirtualCompany.Web", "Pages", "Finance", "CustomerBillingPage.razor");
        var lifecycle = Read("src", "VirtualCompany.Web", "Components", "Finance", "NativeInvoiceLifecyclePanel.razor");

        Assert.Contains("PreviewCustomerInvoiceDraftAsync", Read("src", "VirtualCompany.Web", "Pages", "Finance", "InvoiceDraftPage.razor.cs"), StringComparison.Ordinal);
        Assert.Contains("Readiness.IsAllowed", draft, StringComparison.Ordinal);
        Assert.Contains("RecommendedAction", receivables, StringComparison.Ordinal);
        Assert.Contains("ResolveConflictAsync", billing, StringComparison.Ordinal);
        Assert.Contains("CorrectionPolicy.IsAllowed", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ApprovalRequestId", lifecycle, StringComparison.Ordinal);
        Assert.Contains("GetNativeReceivablesReadinessAsync", Read("src", "VirtualCompany.Web", "Pages", "Finance", "ReceivablesPage.razor.cs"), StringComparison.Ordinal);
        Assert.Contains("ProviderAcceptanceNotDelivery", receivables, StringComparison.Ordinal);

        var english = Resources("FinanceResources.resx");
        var swedish = Resources("FinanceResources.sv-SE.resx");
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), swedish.Keys.Order(StringComparer.Ordinal));

        foreach (var file in new[] { draft, receivables, billing, lifecycle })
        {
            var keys = System.Text.RegularExpressions.Regex.Matches(file, "FinanceText\\[\\\"([^\\\"]+)\\\"")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal);
            Assert.All(keys, key => Assert.True(english.ContainsKey(key), $"Missing FinanceResources key: {key}"));
        }
    }

    [Fact]
    public void Screenshot_first_references_and_prompts_are_versioned_together()
    {
        Assert.True(Exists("docs", "design", "references", "native-invoice-editor-issue-reference.png"));
        Assert.True(Exists("docs", "design", "references", "native-receivables-collections-reference.png"));
        Assert.True(Exists("docs", "design", "references", "native-receivables-operations-reference.png"));
        var prompts = Read("docs", "design", "references", "native-receivables-reference-prompts.md");
        Assert.Contains("invoice editor", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collections", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("production operations", prompts, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> Resources(string file) =>
        XDocument.Load(Path.Combine(Root(), "src", "VirtualCompany.Web", "Localization", "Finance", file)).Root!
            .Elements("data")
            .ToDictionary(x => (string)x.Attribute("name")!, x => (string)x.Element("value")!, StringComparer.Ordinal);

    private static bool Exists(params string[] segments) => File.Exists(Path.Combine([Root(), .. segments]));
    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
