namespace VirtualCompany.Web.Tests;

public sealed class ManualJournalSurfaceTests
{
    [Fact]
    public void Journal_and_workbench_cover_accessible_operational_and_correction_states()
    {
        var journal = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingJournalsPage.razor");
        var journalCode = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingJournalsPage.razor.cs");
        var journalCss = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingJournalsPage.razor.css");
        var workbench = Read("src", "VirtualCompany.Web", "Pages", "Finance", "ManualJournalWorkbenchPage.razor");
        var workbenchCode = Read("src", "VirtualCompany.Web", "Pages", "Finance", "ManualJournalWorkbenchPage.razor.cs");
        var workbenchCss = Read("src", "VirtualCompany.Web", "Pages", "Finance", "ManualJournalWorkbenchPage.razor.css");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.ManualJournals.cs");

        Assert.Contains("FinanceDataState", journal, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", journal, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", journal, StringComparison.Ordinal);
        Assert.Contains("AuditTimeline", journal, StringComparison.Ordinal);
        Assert.Contains("CreateAdjustment", journal, StringComparison.Ordinal);
        Assert.Contains("ReverseAccountingJournalAsync", journalCode, StringComparison.Ordinal);

        Assert.Contains("role=\"status\"", workbench, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", workbench, StringComparison.Ordinal);
        Assert.Contains("EvidenceDocument", workbench, StringComparison.Ordinal);
        Assert.Contains("SubmitForApproval", workbench, StringComparison.Ordinal);
        Assert.Contains("PostApprovedJournal", workbench, StringComparison.Ordinal);
        Assert.Contains("ManualJournalConflictApiException", workbenchCode, StringComparison.Ordinal);
        Assert.Contains("OriginalLedgerEntryId", workbenchCode, StringComparison.Ordinal);
        Assert.Contains("SourceRecords = Model.SourceRecords", workbenchCode, StringComparison.Ordinal);
        Assert.Contains("ManualJournalSourceReferenceResponse", client, StringComparison.Ordinal);
        Assert.Contains("@media", journalCss, StringComparison.Ordinal);
        Assert.Contains("@media", workbenchCss, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant", journal + workbench, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Screenshot_first_reference_artifacts_are_committed_with_prompts()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "accounting-journals-reference.png")));
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "manual-journal-workbench-reference.png")));
        var prompts = Read("docs", "design", "references", "manual-journal-reference-prompts.md");
        Assert.Contains("Journal list and detail", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual journal", prompts, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
}
