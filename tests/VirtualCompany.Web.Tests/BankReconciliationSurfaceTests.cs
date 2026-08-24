namespace VirtualCompany.Web.Tests;

public sealed class BankReconciliationSurfaceTests
{
    [Fact]
    public void Reconciliation_surface_exposes_every_actionable_state_and_governed_drill_down()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReconciliationPage.razor.css");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.BankReconciliation.cs");

        Assert.Contains("unmatched", page, StringComparison.Ordinal);
        Assert.Contains("partial", page, StringComparison.Ordinal);
        Assert.Contains("matched", page, StringComparison.Ordinal);
        Assert.Contains("posted", page, StringComparison.Ordinal);
        Assert.Contains("suspense", page, StringComparison.Ordinal);
        Assert.Contains("conflict", page, StringComparison.Ordinal);
        Assert.Contains("correction", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"PostReviewedCategory\"]", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"ExplicitDifferenceLines\"]", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[journal.IsOriginalSuspense ? \"OriginalSuspenseJournal\"", page, StringComparison.Ordinal);
        Assert.Contains("JournalHref", page, StringComparison.Ordinal);
        Assert.Contains("PaymentHref", page, StringComparison.Ordinal);
        Assert.Contains("InvoiceHref", page, StringComparison.Ordinal);
        Assert.Contains("BillHref", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"OpenPayment\"]", page, StringComparison.Ordinal);
        Assert.Contains("role=\"button\" tabindex=\"0\"", page, StringComparison.Ordinal);
        Assert.Contains("HandleTransactionKeyAsync", code, StringComparison.Ordinal);
        Assert.Contains("CandidatePayments", page, StringComparison.Ordinal);
        Assert.Contains("CanManageAccounting", page, StringComparison.Ordinal);
        Assert.Contains("BuildAdjustments", code, StringComparison.Ordinal);
        Assert.Contains("ReclassifyBankSuspenseAsync", code, StringComparison.Ordinal);
        Assert.Contains("ExpectedSourceVersion", code, StringComparison.Ordinal);
        Assert.Contains("GetBankReconciliationDetailAsync", client, StringComparison.Ordinal);
        Assert.Contains("ReconcileBankTransactionAsync", client, StringComparison.Ordinal);
        Assert.Contains("ReclassifyBankSuspenseAsync", client, StringComparison.Ordinal);
        Assert.Contains("@media", css, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Screenshot_first_bank_reconciliation_reference_is_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "bank-reconciliation-reference.png")));
        var prompt = Read("docs", "design", "references", "bank-reconciliation-reference-prompt.md");
        Assert.Contains("Bank reconciliation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unmatched", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suspense", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
}
