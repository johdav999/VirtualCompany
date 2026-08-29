using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VirtualCompany.Web.Tests;

public sealed class PaymentBatchSurfaceTests
{
    [Fact]
    public void Payment_batch_workspace_is_server_backed_localized_and_explicitly_internal_only()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "PaymentBatchesPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "PaymentBatchesPage.razor.cs");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.PaymentBatches.cs");
        var routes = Read("src", "VirtualCompany.Web", "Services", "FinanceRoutes.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "PaymentBatchesPage.razor.css");

        Assert.Contains("InternalApprovalOnlyDescription", page, StringComparison.Ordinal);
        Assert.Contains("ImmutableSourceEvidence", page, StringComparison.Ordinal);
        Assert.Contains("DifferentApproverRequired", page, StringComparison.Ordinal);
        Assert.Contains("LauraPaymentBatchRecommendation", page, StringComparison.Ordinal);
        Assert.Contains("AllowedActions", page, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentBatchAsync", code, StringComparison.Ordinal);
        Assert.Contains("SubmitPaymentBatchAsync", code, StringComparison.Ordinal);
        Assert.Contains("ApprovePaymentBatchAsync", code, StringComparison.Ordinal);
        Assert.Contains("RegeneratePaymentBatchAsync", code, StringComparison.Ordinal);
        Assert.Contains("EnsureOnlineMutation", client, StringComparison.Ordinal);
        Assert.Contains("internal/companies/{companyId}/finance/payment-batches", client, StringComparison.Ordinal);
        Assert.Contains("PaymentBatchDetail", routes, StringComparison.Ordinal);
        Assert.Contains("BuildPaymentBatchPath", routes, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("SendPayment", page + code + client, StringComparison.OrdinalIgnoreCase);

        var english = Resources("FinanceResources.resx");
        var swedish = Resources("FinanceResources.sv-SE.resx");
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), swedish.Keys.Order(StringComparer.Ordinal));
        var keys = Regex.Matches(page + code, "FinanceText\\[\\\"([^\\\"]+)\\\"")
            .Select(x => x.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);
        Assert.All(keys, key => Assert.True(english.ContainsKey(key), $"Missing FinanceResources key: {key}"));
    }

    [Fact]
    public void Payment_batch_screenshot_first_reference_and_prompt_are_versioned_together()
    {
        Assert.True(Exists("docs", "design", "references", "payment-batches-reference.png"));
        var prompt = Read("docs", "design", "references", "payment-batches-reference-prompt.md");
        Assert.Contains("payment batches", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nothing is sent to a bank", prompt, StringComparison.OrdinalIgnoreCase);
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
