namespace VirtualCompany.Web.Tests;

public sealed class StatementImportsSurfaceTests
{
    [Fact]
    public void Import_center_matches_reference_states_is_localized_and_responsive()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "StatementImportsPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "StatementImportsPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "StatementImportsPage.razor.css");
        Assert.Contains("@page \"/finance/transactions/imports\"", page, StringComparison.Ordinal);
        Assert.Contains("@rendermode InteractiveServer", page, StringComparison.Ordinal);
        Assert.Contains("InputFile", page, StringComparison.Ordinal);
        Assert.Contains("ValidationReport", page, StringComparison.Ordinal);
        Assert.Contains("PreviewIsNotImport", page, StringComparison.Ordinal);
        Assert.Contains("ControlTotals", page, StringComparison.Ordinal);
        Assert.Contains("ImportHistory", page, StringComparison.Ordinal);
        Assert.Contains("SkipRowAsync", page, StringComparison.Ordinal);
        Assert.Contains("CommitStatementImportAsync", code, StringComparison.Ordinal);
        Assert.Contains("OpenReadStream(MaximumFileBytes)", code, StringComparison.Ordinal);
        Assert.Contains("CanManageFinanceIntegrations", code, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("mock", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Screenshot_first_statement_import_reference_and_prompt_are_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "statement-import-center-reference.png")));
        var prompt = Read("docs", "design", "references", "statement-import-center-reference-prompt.md");
        Assert.Contains("Statement imports", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validation report", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resumable", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
}
