namespace VirtualCompany.Web.Tests;

public sealed class AdvancedReconciliationSurfaceTests
{
    [Fact]
    public void Advanced_reconciliation_surface_exposes_evidence_controls_and_guarded_decisions()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor.css");

        Assert.Contains("AdvancedSettlementGroups", page, StringComparison.Ordinal);
        Assert.Contains("ExpectedBankTotal", page, StringComparison.Ordinal);
        Assert.Contains("ReasonContributions", page, StringComparison.Ordinal);
        Assert.Contains("RuleVersion", page, StringComparison.Ordinal);
        Assert.Contains("ConfidenceValue", page, StringComparison.Ordinal);
        Assert.Contains("AdvancedDecisionReason", page, StringComparison.Ordinal);
        Assert.Contains("SelectedAdvanced.Summary.IsStale", page, StringComparison.Ordinal);
        Assert.Contains("!SelectedAdvanced.IsBalanced", page, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedReversal", page, StringComparison.Ordinal);
        Assert.Contains("ImmutableHistory", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("ExpectedRuleVersion = SelectedAdvanced.Summary.RuleVersion", code, StringComparison.Ordinal);
        Assert.Contains("ExpectedVersion = SelectedAdvanced.Summary.Version", code, StringComparison.Ordinal);
        Assert.Contains("CanApproveAdvancedReconciliation => FinanceAccess.CanApproveInvoices", code, StringComparison.Ordinal);
        Assert.Contains("@media", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_advanced_reconciliation_reference_and_prompt_are_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "advanced-reconciliation-reference.png")));
        var prompt = Read("docs", "design", "references", "advanced-reconciliation-reference-prompt.md");
        Assert.Contains("expected bank total", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("result graph", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confidence", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
