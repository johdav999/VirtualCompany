using System.Xml.Linq;

namespace VirtualCompany.Web.Tests;

public sealed class TreasuryWorkspaceSurfaceTests
{
    [Fact]
    public void Cash_route_is_a_consolidated_evidence_grounded_daily_treasury_workspace()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "CashPositionPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "CashPositionPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "CashPositionPage.razor.css");

        Assert.Contains("@page \"/finance/cash-position\"", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Accounts", page, StringComparison.Ordinal);
        Assert.Contains("EvidenceSource", page, StringComparison.Ordinal);
        Assert.Contains("EvidenceUtc", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Exceptions", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PaymentWork", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Laura.Citations", page, StringComparison.Ordinal);
        Assert.Contains("MissingEvidenceMessages()", page, StringComparison.Ordinal);
        Assert.Contains("ExceptionTitle(item)", page, StringComparison.Ordinal);
        Assert.Contains("ProjectionEvidenceBasis(point)", page, StringComparison.Ordinal);
        Assert.Contains("TreasuryCashProjectionCitation", code, StringComparison.Ordinal);
        Assert.Contains("GetTreasuryWorkspaceAsync", code, StringComparison.Ordinal);
        Assert.Contains("TreasuryWorkspaceUsageTelemetry", code, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 640px)", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
    }

    [Fact]
    public void English_and_swedish_have_matching_daily_treasury_resources()
    {
        var english = Keys("FinanceResources.resx");
        var swedish = Keys("FinanceResources.sv-SE.resx");
        var treasuryKeys = english.Where(key => key.StartsWith("TreasuryDaily", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryAccountCoverage", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryEvidenceNeeds", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryPaymentWork", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryRecommendOnly", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(treasuryKeys);
        Assert.All(treasuryKeys, key => Assert.Contains(key, swedish));
        Assert.Contains("Daglig likviditet", Read("src", "VirtualCompany.Web", "Localization", "Finance",
            "FinanceResources.sv-SE.resx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_reference_and_operations_runbook_are_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references",
            "daily-treasury-workspace-reference.png")));
        var prompt = Read("docs", "design", "references", "daily-treasury-workspace-reference-prompt.md");
        var runbook = Read("docs", "runbooks", "daily-treasury-workspace.md");
        Assert.Contains("account coverage", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recommendation-only", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconciliation_required", runbook, StringComparison.Ordinal);
        Assert.Contains("maximum 50", runbook, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> Keys(string file) => XDocument.Load(Path.Combine(
            Root(), "src", "VirtualCompany.Web", "Localization", "Finance", file))
        .Root!
        .Elements("data")
        .Select(element => (string?)element.Attribute("name"))
        .Where(name => name is not null)
        .Cast<string>()
        .ToHashSet(StringComparer.Ordinal);

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
