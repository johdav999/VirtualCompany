using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AgentCapabilityPresenterTests
{
    [Fact]
    public void PermissionRequirements_ArePresentedInPlainEnglish()
    {
        var capability = CreateCapability(
            "permission_denied",
            "agent_permission_missing",
            ["permission:tool:tasks.list", "permission:scope:read:tasks"]);

        var explanation = AgentCapabilityPresenter.GetExplanation(capability);

        Assert.Equal(
            "Allow this agent to use the task list tool. Grant this agent read access to tasks.",
            explanation);
        Assert.Equal("Manage access", AgentCapabilityPresenter.GetActionLabel(capability));
    }

    [Fact]
    public void MissingTrustedTool_LinksToToolRegistry()
    {
        var companyId = Guid.NewGuid();
        var capability = CreateCapability(
            "configuration_required",
            "required_tool_unregistered",
            ["tool:knowledge.search"]);

        Assert.Equal(
            "Register the company knowledge search trusted tool.",
            AgentCapabilityPresenter.GetExplanation(capability));
        Assert.Equal("Configure tools", AgentCapabilityPresenter.GetActionLabel(capability));
        Assert.Equal(
            $"/system/admin/tool-registry?companyId={companyId:D}",
            AgentCapabilityPresenter.GetActionHref(capability, companyId, Guid.NewGuid()));
    }

    [Fact]
    public void RestrictedCapability_LinksToSelectedAgentAccessConfiguration()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var capability = CreateCapability(
            "permission_denied",
            "autonomy_level_too_low",
            ["autonomy:level_1"]);

        Assert.True(AgentCapabilityPresenter.RequiresRemediation(capability));
        Assert.Equal(
            $"/agents/manage?companyId={companyId:D}&agentId={agentId:D}#agent-access-configuration",
            AgentCapabilityPresenter.GetActionHref(capability, companyId, agentId));
        Assert.Equal(
            "Set this agent's autonomy to Level 1 or higher.",
            AgentCapabilityPresenter.GetExplanation(capability));
    }

    [Fact]
    public void MissingConfiguration_IdentifiesEachRequiredSettingInPlainEnglish()
    {
        var capability = CreateCapability(
            "configuration_required",
            "required_configuration_missing",
            ["configuration:knowledge_indexing_enabled", "configuration:briefing_scheduler_enabled"]);

        Assert.Equal(
            "Enable document indexing. Enable the briefing scheduler.",
            AgentCapabilityPresenter.GetExplanation(capability));
    }

    [Fact]
    public void InactiveAgent_ExplainsTheExactRequiredChange()
    {
        var capability = CreateCapability(
            "permission_denied",
            "agent_not_active",
            []);

        Assert.Equal(
            "Set this agent to Active and allow it to receive work.",
            AgentCapabilityPresenter.GetExplanation(capability));
    }

    private static AgentCapabilityViewModel CreateCapability(
        string state,
        string reasonCode,
        List<string> missingRequirements) =>
        new()
        {
            State = state,
            ReasonCode = reasonCode,
            Explanation = "Generic explanation.",
            MissingRequirements = missingRequirements
        };
}
