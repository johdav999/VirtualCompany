using System.Text.RegularExpressions;
using System.Xml.Linq;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceAgentCoverageSurfaceTests
{
    [Fact]
    public void Coverage_surface_is_effective_workflow_first_and_never_claims_percentage_completion()
    {
        var component = Read("src", "VirtualCompany.Web", "Components", "FinanceAgentCoverageWorkspace.razor");
        var profile = Read("src", "VirtualCompany.Web", "Pages", "AgentProfile.razor");

        Assert.Contains("FinanceAgentCoverageWorkspace", profile, StringComparison.Ordinal);
        Assert.Contains("GetFinanceCoverageAsync", profile, StringComparison.Ordinal);
        Assert.Contains("IsAuthorized", component, StringComparison.Ordinal);
        Assert.Contains("IsEffective", component, StringComparison.Ordinal);
        Assert.Contains("ImplementedRead", component, StringComparison.Ordinal);
        Assert.Contains("approval_required", component, StringComparison.Ordinal);
        Assert.Contains("human_only", component, StringComparison.Ordinal);
        Assert.Contains("configuration_dependent", component, StringComparison.Ordinal);
        Assert.Contains("BuildAuditHref", component, StringComparison.Ordinal);
        Assert.Contains("BuildAskHref", component, StringComparison.Ordinal);
        Assert.DoesNotContain("percent", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ToolName", component, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_surface_has_loading_empty_error_stale_accessibility_and_narrow_states()
    {
        var component = Read("src", "VirtualCompany.Web", "Components", "FinanceAgentCoverageWorkspace.razor");
        var css = Read("src", "VirtualCompany.Web", "Components", "FinanceAgentCoverageWorkspace.razor.css");

        Assert.Contains("aria-live=\"polite\"", component, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", component, StringComparison.Ordinal);
        Assert.Contains("IsStale", component, StringComparison.Ordinal);
        Assert.Contains("FinanceCoverageEmpty", component, StringComparison.Ordinal);
        Assert.Contains("FinanceCoverageRestricted", component, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 700px)", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Finance_context_link_preserves_supported_record_and_workflow_without_granting_authority()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var invoice = FinanceRoutes.BuildAgentWorkbenchPath(companyId, agentId,
            $"https://virtual.test/finance/invoices/{invoiceId:D}?companyId={companyId:D}");
        Assert.Contains("referenceType=invoice", invoice, StringComparison.Ordinal);
        Assert.Contains("workflow=receivables", invoice, StringComparison.Ordinal);

        var close = FinanceRoutes.BuildAgentWorkbenchPath(companyId, agentId,
            "https://virtual.test/finance/accounting/close-workspace?companyId=ignored");
        Assert.Contains("workflow=close", close, StringComparison.Ordinal);

        var explicitCoverage = FinanceRoutes.BuildAgentWorkbenchPath(companyId, agentId, workflow: "coverage");
        Assert.Contains("workflow=coverage", explicitCoverage, StringComparison.Ordinal);

        var unsupported = FinanceRoutes.BuildAgentWorkbenchPath(companyId, agentId,
            "https://virtual.test/system/admin/tool-executions", "grant-everything");
        Assert.DoesNotContain("workflow=", unsupported, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_localization_is_complete_in_english_and_swedish()
    {
        var component = Read("src", "VirtualCompany.Web", "Components", "FinanceAgentCoverageWorkspace.razor");
        var english = Resources("AgentsResources.resx");
        var swedish = Resources("AgentsResources.sv-SE.resx");
        var keys = Regex.Matches(component, "AgentText\\[\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(keys, key => Assert.True(english.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
            $"Missing English AgentsResources key: {key}"));
        Assert.All(keys, key => Assert.True(swedish.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
            $"Missing Swedish AgentsResources key: {key}"));
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), swedish.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Screenshot_first_reference_is_retained()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "finance-agent-coverage-reference.png")));
    }

    private static Dictionary<string, string> Resources(string file) =>
        XDocument.Load(Path.Combine(Root(), "src", "VirtualCompany.Web", "Localization", "Agents", file)).Root!
            .Elements("data")
            .ToDictionary(element => (string)element.Attribute("name")!, element => (string)element.Element("value")!, StringComparer.Ordinal);

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
