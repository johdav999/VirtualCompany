namespace VirtualCompany.Web.Tests;

public sealed class BankConnectionsSurfaceTests
{
    [Fact]
    public void Bank_connections_surface_is_localized_accessible_explicit_and_responsive()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "BankConnectionsPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "BankConnectionsPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "BankConnectionsPage.razor.css");

        Assert.Contains("@page \"/finance/settings/bank-connections\"", page, StringComparison.Ordinal);
        Assert.Contains("@rendermode InteractiveServer", page, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", page, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", page, StringComparison.Ordinal);
        Assert.Contains("NoBankProviderConfigured", page, StringComparison.Ordinal);
        Assert.Contains("OwnershipMismatch", page, StringComparison.Ordinal);
        Assert.Contains("MappedCompanyBankAccountId", page, StringComparison.Ordinal);
        Assert.Contains("MappingVersion", page, StringComparison.Ordinal);
        Assert.Contains("ConfirmDisconnect", page, StringComparison.Ordinal);
        Assert.Contains("account.OwnershipStatus == \"verified\"", code, StringComparison.Ordinal);
        Assert.Contains("CanManageFinanceIntegrations", code, StringComparison.Ordinal);
        Assert.Contains("BankConnectionRefreshed", code, StringComparison.Ordinal);
        Assert.Contains("FeedCoverage", page, StringComparison.Ordinal);
        Assert.Contains("RecoverGapAsync", page, StringComparison.Ordinal);
        Assert.Contains("RequestBankFeedSynchronizationAsync", code, StringComparison.Ordinal);
        Assert.Contains("BankFeedRecoveryReason", code, StringComparison.Ordinal);
        Assert.Contains("@media", css, StringComparison.Ordinal);
        Assert.DoesNotContain("token", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Screenshot_first_bank_feed_operations_reference_and_prompt_are_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "bank-feed-operations-reference.png")));
        var prompt = Read("docs", "design", "references", "bank-feed-operations-reference-prompt.md");
        Assert.Contains("Feed coverage", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing range", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recover range", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Screenshot_first_bank_connections_reference_and_prompt_are_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "bank-connections-reference.png")));
        var prompt = Read("docs", "design", "references", "bank-connections-reference-prompt.md");
        Assert.Contains("Bank connections", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ownership", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit internal-account", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
}
