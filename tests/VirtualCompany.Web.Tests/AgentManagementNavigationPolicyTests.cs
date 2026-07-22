using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AgentManagementNavigationPolicyTests
{
    [Theory]
    [InlineData("agents/manage")]
    [InlineData("/agents/manage/")]
    [InlineData("agents/manage?companyId=43e6a825-d1b7-429a-8608-7e668087d005&agentId=6ef444d5-3646-4a13-b88a-26e65d9a494b")]
    [InlineData("agents/manage#agent-access-configuration")]
    public void ManagementRoutes_CanBeCanonicalized(string uri)
    {
        Assert.True(AgentManagementNavigationPolicy.IsManagementRoute(uri));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dashboard?companyId=43e6a825-d1b7-429a-8608-7e668087d005")]
    [InlineData("finance")]
    [InlineData("support/cases")]
    [InlineData("agents/6ef444d5-3646-4a13-b88a-26e65d9a494b/chat")]
    public void OtherRoutes_CannotBeOverriddenByAgentCanonicalization(string? uri)
    {
        Assert.False(AgentManagementNavigationPolicy.IsManagementRoute(uri));
    }
}
