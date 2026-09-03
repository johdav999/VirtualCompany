namespace VirtualCompany.Web.Tests;

public sealed class SupplierBillAccountingSurfaceTests
{
    [Fact]
    public void Supplier_bill_surface_separates_native_posting_from_optional_provider_export()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Web", "Pages", "Finance", "BillsPage.razor"));
        var client = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Web", "Services", "FinanceApiClient.SupplierBillAccounting.cs"));
        var english = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Web", "Localization", "Finance", "FinanceResources.resx"));
        var swedish = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Web", "Localization", "Finance", "FinanceResources.sv-SE.resx"));

        Assert.Contains("NativeSupplierAccounting", markup, StringComparison.Ordinal);
        Assert.Contains("PostNativeAccountingAsync", markup, StringComparison.Ordinal);
        Assert.Contains("ViewVoucher", markup, StringComparison.Ordinal);
        Assert.Contains("DuplicateChecks", markup, StringComparison.Ordinal);
        Assert.Contains("SourceDocumentEvidence", markup, StringComparison.Ordinal);
        Assert.Contains("LoadingNativeAccounting", markup, StringComparison.Ordinal);
        Assert.Contains("NativeAccountingUnavailable", markup, StringComparison.Ordinal);
        Assert.Contains("NeedsAccountingReview", markup, StringComparison.Ordinal);
        Assert.Contains("NativeAccountingPreview.Issues.Where(x => x.IsBlocking)", markup, StringComparison.Ordinal);
        Assert.Contains("CreateNativeCreditNoteAsync", markup, StringComparison.Ordinal);
        Assert.Contains("CorrectionChain", markup, StringComparison.Ordinal);
        Assert.Contains("FortnoxExportOptional", markup, StringComparison.Ordinal);
        Assert.Contains("/accounting/preview", client, StringComparison.Ordinal);
        Assert.Contains("/accounting/post", client, StringComparison.Ordinal);
        Assert.Contains("Posted in Virtual Company", english, StringComparison.Ordinal);
        Assert.Contains("Bokförd i Virtual Company", swedish, StringComparison.Ordinal);
        Assert.Contains("Native accounting is not ready", english, StringComparison.Ordinal);
        Assert.Contains("Den interna bokföringen är inte klar", swedish, StringComparison.Ordinal);
    }

    [Fact]
    public void Bill_review_uses_company_accounting_authority_instead_of_assuming_fortnox()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Web", "Pages", "Finance", "BillInboxDetailPage.razor"));
        var logic = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Web", "Pages", "Finance", "BillInboxDetailPage.razor.cs"));

        Assert.Contains("Detail.UsesInternalAccounting", markup, StringComparison.Ordinal);
        Assert.Contains("Virtual Company accounting", markup, StringComparison.Ordinal);
        Assert.Contains("Internal ledger", markup, StringComparison.Ordinal);
        Assert.Contains("Open supplier bills", markup, StringComparison.Ordinal);
        Assert.Contains("if (Detail.UsesInternalAccounting)", logic, StringComparison.Ordinal);
        Assert.Contains("Nothing will be sent to Fortnox", logic, StringComparison.Ordinal);
        Assert.Contains("Detail?.OperationalBillId is Guid operationalBillId", logic, StringComparison.Ordinal);
        Assert.Contains("FinanceRoutes.BuildBillDetailPath(operationalBillId", logic, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "VirtualCompany.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
