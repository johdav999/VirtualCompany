namespace VirtualCompany.Web.Tests;

public sealed class CustomerInvoiceAccountingSurfaceTests
{
    [Fact]
    public void Invoice_surface_separates_native_accounting_payment_and_provider_states()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "InvoicesPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "InvoicesPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "wwwroot", "css", "app.css");

        Assert.Contains("Accounting:", page, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus", page, StringComparison.Ordinal);
        Assert.Contains("Delivery and provider status", page, StringComparison.Ordinal);
        Assert.Contains("Preview journal", page, StringComparison.Ordinal);
        Assert.Contains("Submit for approval", page, StringComparison.Ordinal);
        Assert.Contains("Post to ledger", page, StringComparison.Ordinal);
        Assert.Contains("Create credit note", page, StringComparison.Ordinal);
        Assert.Contains("Open journal", page, StringComparison.Ordinal);
        Assert.Contains("Posted journal facts", page, StringComparison.Ordinal);
        Assert.Contains("Open original invoice", page, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", page, StringComparison.Ordinal);
        Assert.Contains("CustomerInvoiceAccountingApiRequest", code, StringComparison.Ordinal);
        Assert.Contains("PostCustomerInvoiceAccountingAsync", code, StringComparison.Ordinal);
        Assert.Contains("CreateCustomerCreditNoteAsync", code, StringComparison.Ordinal);
        Assert.Contains("@media", css, StringComparison.Ordinal);
        Assert.DoesNotContain("Fortnox" + " required", page, StringComparison.OrdinalIgnoreCase);

        var journalPage = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingJournalsPage.razor");
        var journalCode = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingJournalsPage.razor.cs");
        Assert.Contains("FinanceText[\"OpenSourceInvoice\"]", journalPage, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"OpenOriginalJournal\"]", journalPage, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery", journalCode, StringComparison.Ordinal);
        Assert.Contains("journalId", journalCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_exposes_the_complete_invoice_accounting_workflow()
    {
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.InvoicesAndCounterparties.cs");

        Assert.Contains("GetCustomerInvoiceAccountingReferenceDataAsync", client, StringComparison.Ordinal);
        Assert.Contains("PreviewCustomerInvoiceAccountingAsync", client, StringComparison.Ordinal);
        Assert.Contains("SubmitCustomerInvoiceAccountingAsync", client, StringComparison.Ordinal);
        Assert.Contains("PostCustomerInvoiceAccountingAsync", client, StringComparison.Ordinal);
        Assert.Contains("CreateCustomerCreditNoteAsync", client, StringComparison.Ordinal);
        Assert.Contains("GetCustomerInvoiceReceivableReconciliationAsync", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_reference_is_saved_with_its_generation_prompt()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "customer-invoice-accounting-reference.png")));
        var prompt = Read("docs", "design", "references", "customer-invoice-accounting-reference-prompt.md");
        Assert.Contains("Accounting status", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accounting preview", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fortnox", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
