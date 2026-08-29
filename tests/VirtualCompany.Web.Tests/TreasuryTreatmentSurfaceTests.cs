namespace VirtualCompany.Web.Tests;

public sealed class TreasuryTreatmentSurfaceTests
{
    [Fact]
    public void Reconciliation_detail_exposes_governed_treasury_state_evidence_preview_and_corrections()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor.css");

        Assert.Contains("Treasury treatment", Read("src", "VirtualCompany.Web", "Localization", "Finance", "FinanceResources.resx"), StringComparison.Ordinal);
        Assert.Contains("SelectedTreasury.Summary.Status", page, StringComparison.Ordinal);
        Assert.Contains("SelectedTreasury.BankEvidence", page, StringComparison.Ordinal);
        Assert.Contains("TreasuryPostingPreview.Lines", page, StringComparison.Ordinal);
        Assert.Contains("SelectedTreasury.AllowedActions.CanPost", page, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedReversal", page, StringComparison.Ordinal);
        Assert.Contains("ImmutableHistory", page, StringComparison.Ordinal);
        Assert.Contains("ListTreasurySourcesAsync(companyId, bankTransactionId: transactionId)", code, StringComparison.Ordinal);
        Assert.Contains("ExpectedVersion = SelectedTreasury.Summary.Version", code, StringComparison.Ordinal);
        Assert.Contains("CanApproveAdvancedReconciliation", code, StringComparison.Ordinal);
        Assert.Contains("@media", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_treasury_reference_and_generation_prompt_are_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "treasury-treatment-reference.png")));
        var prompt = Read("docs", "design", "references", "treasury-treatment-reference-prompt.md");
        Assert.Contains("one-leg internal transfer", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gross", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evidence", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
