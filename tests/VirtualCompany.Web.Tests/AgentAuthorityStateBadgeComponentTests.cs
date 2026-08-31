using Bunit;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components;

namespace VirtualCompany.Web.Tests;

public sealed class AgentAuthorityStateBadgeComponentTests
{
    [Theory]
    [InlineData("available", "Available", "is-ready")]
    [InlineData("approval_required", "Approval required", "is-review")]
    [InlineData("configuration_required", "Configuration required", "is-setup")]
    [InlineData("permission_denied", "Permission denied", "is-blocked")]
    [InlineData("integration_unavailable", "Integration unavailable", "is-setup")]
    [InlineData("not_implemented", "Not implemented", "is-blocked")]
    public void RendersEveryCapabilityState(string state, string label, string tone)
    {
        using var context = CreateContext();

        var cut = context.RenderComponent<AgentAuthorityStateBadge>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Equal(label, cut.Find("span").TextContent);
        Assert.Contains(tone, cut.Find("span").ClassList);
    }

    [Theory]
    [InlineData("pending", "Pending review", "is-review")]
    [InlineData("approved", "Approved", "is-ready")]
    [InlineData("rejected", "Rejected", "is-blocked")]
    [InlineData("expired", "Expired", "is-blocked")]
    [InlineData("cancelled", "Cancelled", "is-setup")]
    [InlineData("stale", "Stale", "is-blocked")]
    [InlineData("superseded", "Superseded", "is-blocked")]
    [InlineData("revoked", "Revoked", "is-blocked")]
    public void RendersEveryApprovalState(string state, string label, string tone)
    {
        using var context = CreateContext();

        var cut = context.RenderComponent<AgentAuthorityStateBadge>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.IsApprovalStatus, true));

        Assert.Equal(label, cut.Find("span").TextContent);
        Assert.Contains(tone, cut.Find("span").ClassList);
    }

    private static TestContext CreateContext()
        => new TestContext().AddVirtualCompanyWebPresentationServices();
}
