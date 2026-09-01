using System.Text.RegularExpressions;
using System.Xml.Linq;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceAgentWorkbenchSurfaceTests
{
    [Fact]
    public void Workbench_component_exposes_governed_conversation_and_supervision_states()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "FinanceAgentWorkbenchPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "FinanceAgentWorkbenchPage.razor.cs");

        Assert.Contains("@page \"/finance/workbench\"", page, StringComparison.Ordinal);
        Assert.Contains("awaiting_clarification", page + code, StringComparison.Ordinal);
        Assert.Contains("awaiting_confirmation", page + code, StringComparison.Ordinal);
        Assert.Contains("awaiting_approval", page + code, StringComparison.Ordinal);
        Assert.Contains("SupersedeConversationRunAsync", code, StringComparison.Ordinal);
        Assert.Contains("ConfirmConversationRunStepAsync", code, StringComparison.Ordinal);
        Assert.Contains("CancelConversationRunAsync", code, StringComparison.Ordinal);
        Assert.Contains("RequestedEffect", page, StringComparison.Ordinal);
        Assert.Contains("ActualEffect", page, StringComparison.Ordinal);
        Assert.Contains("FactsCount", page, StringComparison.Ordinal);
        Assert.Contains("NoAssumptionsRetained", page, StringComparison.Ordinal);
        Assert.Contains("UnknownCount", page, StringComparison.Ordinal);
        Assert.Contains("for (var attempt = 0; attempt < 40", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Workbench_accessibility_and_responsive_contract_is_explicit()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "FinanceAgentWorkbenchPage.razor");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "FinanceAgentWorkbenchPage.razor.css");

        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-atomic=\"true\"", page, StringComparison.Ordinal);
        Assert.Contains("<caption class=\"visually-hidden\"", page, StringComparison.Ordinal);
        Assert.Contains("scope=\"col\"", page, StringComparison.Ordinal);
        Assert.Contains("data-label=", page, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 700px)", css, StringComparison.Ordinal);
        Assert.Contains("content: attr(data-label)", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
        Assert.DoesNotContain("/system/admin/", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NormalizedArguments", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ResultSummary", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolName", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Workbench_localization_is_complete_in_english_and_swedish()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "FinanceAgentWorkbenchPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "FinanceAgentWorkbenchPage.razor.cs");
        var english = Resources("FinanceResources.resx");
        var swedish = Resources("FinanceResources.sv-SE.resx");
        var keys = Regex.Matches(page + code, "FinanceText\\[\\\"([^\\\"]+)\\\"")
            .Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

        Assert.All(keys, key => Assert.True(english.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
            $"Missing English FinanceResources key: {key}"));
        Assert.All(keys, key => Assert.True(swedish.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
            $"Missing Swedish FinanceResources key: {key}"));
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), swedish.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Finance_record_deep_links_preserve_company_and_only_forward_supported_visible_references()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var linked = FinanceRoutes.BuildAgentWorkbenchPath(companyId, agentId,
            $"https://virtual.test/finance/invoices/{invoiceId:D}?companyId={companyId:D}");
        Assert.Contains($"companyId={companyId:D}", linked, StringComparison.Ordinal);
        Assert.Contains($"agentId={agentId:D}", linked, StringComparison.Ordinal);
        Assert.Contains("referenceType=invoice", linked, StringComparison.Ordinal);
        Assert.Contains($"referenceValue={invoiceId:D}", linked, StringComparison.Ordinal);

        var unsupported = FinanceRoutes.BuildAgentWorkbenchPath(companyId, agentId,
            $"https://virtual.test/system/admin/tool-executions/{invoiceId:D}");
        Assert.DoesNotContain("referenceType", unsupported, StringComparison.Ordinal);
        Assert.DoesNotContain(invoiceId.ToString("D"), unsupported, StringComparison.Ordinal);
    }

    [Fact]
    public void Workbench_retains_its_screenshot_first_design_reference()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references", "finance-agent-workbench-reference.png")));
    }

    [Theory]
    [InlineData("planned", true, true)]
    [InlineData("executing", true, true)]
    [InlineData("queued", true, true)]
    [InlineData("reconciling", true, true)]
    [InlineData("awaiting_clarification", false, true)]
    [InlineData("awaiting_confirmation", false, true)]
    [InlineData("awaiting_approval", false, true)]
    [InlineData("completed", false, false)]
    [InlineData("partially_completed", false, false)]
    [InlineData("cancelled", false, false)]
    [InlineData("stale", false, false)]
    [InlineData("failed", false, false)]
    public void Run_state_policy_bounds_polling_and_cancellation(string state, bool shouldPoll, bool canCancel)
    {
        Assert.Equal(shouldPoll, FinanceConversationRunUiState.ShouldPoll(state));
        Assert.Equal(canCancel, FinanceConversationRunUiState.CanCancel(state));
    }

    [Fact]
    public void Human_checkpoint_policy_keeps_confirmation_and_independent_approval_distinct()
    {
        var approvalId = Guid.NewGuid();
        Assert.True(FinanceConversationRunUiState.CanConfirm("awaiting_confirmation"));
        Assert.False(FinanceConversationRunUiState.CanConfirm("awaiting_approval"));
        Assert.True(FinanceConversationRunUiState.CanOpenApproval("awaiting_approval", approvalId));
        Assert.False(FinanceConversationRunUiState.CanOpenApproval("awaiting_approval", null));
        Assert.False(FinanceConversationRunUiState.CanOpenApproval("awaiting_confirmation", approvalId));
    }

    private static Dictionary<string, string> Resources(string file) =>
        XDocument.Load(Path.Combine(Root(), "src", "VirtualCompany.Web", "Localization", "Finance", file)).Root!
            .Elements("data").ToDictionary(x => (string)x.Attribute("name")!,
                x => (string)x.Element("value")!, StringComparer.Ordinal);
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
