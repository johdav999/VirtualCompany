using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VirtualCompany.Web.Tests;

public sealed class PaymentExecutionSurfaceTests
{
    [Fact]
    public void Payment_execution_workspace_exposes_authority_status_ambiguity_settlement_and_remittance_boundaries()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "PaymentBatchesPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "PaymentBatchesPage.razor.cs");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.PaymentExecutions.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "PaymentBatchesPage.razor.css");

        Assert.Contains("FinalAuthorityRecheck", page, StringComparison.Ordinal);
        Assert.Contains("NoBlindResubmission", page, StringComparison.Ordinal);
        Assert.Contains("SettlementEvidenceRequirement", page, StringComparison.Ordinal);
        Assert.Contains("RemittanceAcceptanceBoundary", page, StringComparison.Ordinal);
        Assert.Contains("ProviderAcknowledgements", page, StringComparison.Ordinal);
        Assert.Contains("QueuePaymentExecutionAsync", code, StringComparison.Ordinal);
        Assert.Contains("ReconcilePaymentExecutionAsync", code, StringComparison.Ordinal);
        Assert.Contains("SettlePaymentExecutionAsync", code, StringComparison.Ordinal);
        Assert.Contains("EnsureOnlineMutation", client, StringComparison.Ordinal);
        Assert.Contains("finance/payment-executions", client, StringComparison.Ordinal);
        Assert.Contains("execution-progress", css, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:", css, StringComparison.Ordinal);

        var english = Resources("FinanceResources.resx");
        var swedish = Resources("FinanceResources.sv-SE.resx");
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), swedish.Keys.Order(StringComparer.Ordinal));
        var keys = Regex.Matches(page + code, "FinanceText\\[\\\"([^\\\"]+)\\\"")
            .Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal);
        Assert.All(keys, key => Assert.True(english.ContainsKey(key), $"Missing FinanceResources key: {key}"));
    }

    [Fact]
    public void Payment_execution_reference_and_runbook_are_versioned_with_the_surface()
    {
        Assert.True(Exists("docs", "design", "references", "payment-execution-reference.png"));
        Assert.Contains("payment execution", Read("docs", "design", "references",
            "payment-execution-reference-prompt.md"), StringComparison.OrdinalIgnoreCase);
        var runbook = Read("docs", "runbooks", "payment-execution.md");
        Assert.Contains("Never retry an unknown provider-write result blindly", runbook, StringComparison.Ordinal);
        Assert.Contains("signed webhooks", runbook, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> Resources(string file) =>
        XDocument.Load(Path.Combine(Root(), "src", "VirtualCompany.Web", "Localization", "Finance", file)).Root!
            .Elements("data").ToDictionary(x => (string)x.Attribute("name")!,
                x => (string)x.Element("value")!, StringComparer.Ordinal);
    private static bool Exists(params string[] segments) => File.Exists(Path.Combine([Root(), .. segments]));
    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
