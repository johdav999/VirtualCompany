using VirtualCompany.Shared.Mobile;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MobileCompanionScopeTests
{
    [Fact]
    public void Supported_capabilities_are_limited_to_mobile_companion_scenarios()
    {
        MobileCompanionCapability[] expected =
        [
            MobileCompanionCapability.SignIn,
            MobileCompanionCapability.CompanySelection,
            MobileCompanionCapability.Alerts,
            MobileCompanionCapability.Approvals,
            MobileCompanionCapability.DailyBriefing,
            MobileCompanionCapability.DirectAgentChat,
            MobileCompanionCapability.CompanyStatus,
            MobileCompanionCapability.TaskFollowUp
        ];

        Assert.Equal(expected, MobileCompanionScope.SupportedCapabilities);
        Assert.Equal(expected, MobileCompanionScope.CapabilityRouteKeys.Keys);
    }

    [Fact]
    public void Product_direction_keeps_responsive_web_interim_and_maui_target_explicit()
    {
        Assert.Contains("Web-first", MobileCompanionScope.ProductDirectionDescription);
        Assert.Contains(".NET MAUI", MobileCompanionScope.ProductDirectionDescription);
        Assert.Contains("interim bridge", MobileCompanionScope.ResponsiveWebBridgeMessage);
        Assert.Contains("final mobile strategy", MobileCompanionScope.ResponsiveWebBridgeMessage);
        Assert.Contains(".NET MAUI app", MobileCompanionScope.MauiTargetCompanionMessage);
    }

    [Fact]
    public void Companion_copy_keeps_backend_reuse_and_limited_scope_explicit()
    {
        Assert.Contains("approvals", MobileCompanionScope.CompanionScopeDescription);
        Assert.Contains("quick chat", MobileCompanionScope.MauiTargetCompanionMessage);
        Assert.Contains("same backend APIs", MobileCompanionScope.SharedBackendApiReuseMessage);
        Assert.Contains("does not get separate business workflows", MobileCompanionScope.SharedBackendApiReuseMessage);
    }

    [Fact]
    public void Mobile_backend_route_catalog_reuses_existing_backend_workflows()
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var approvalId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var notificationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var agentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var conversationId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var routes = new[]
        {
            MobileBackendApiRoutes.AuthMe,
            MobileBackendApiRoutes.SelectCompany,
            MobileBackendApiRoutes.Inbox(companyId),
            MobileBackendApiRoutes.CompactInbox(companyId, take: 30),
            MobileBackendApiRoutes.MobileSummary(companyId),
            MobileBackendApiRoutes.LatestBriefings(companyId),
            MobileBackendApiRoutes.CompactLatestBriefing(companyId),
            MobileBackendApiRoutes.Agents(companyId),
            MobileBackendApiRoutes.ApprovalDecision(companyId, approvalId),
            MobileBackendApiRoutes.NotificationStatus(companyId, notificationId),
            MobileBackendApiRoutes.OpenDirectConversation(companyId, agentId),
            MobileBackendApiRoutes.CompactDirectConversations(companyId, skip: 0, take: 25),
            MobileBackendApiRoutes.CompactConversationMessages(companyId, conversationId, skip: 0, take: 50),
            MobileBackendApiRoutes.DirectConversations(companyId, skip: 0, take: 25),
            MobileBackendApiRoutes.ConversationMessages(companyId, conversationId, skip: 0, take: 50),
            MobileBackendApiRoutes.SendConversationMessage(companyId, conversationId)
        };

        Assert.All(routes, route => Assert.StartsWith("api/", route));
        Assert.Contains($"api/companies/{companyId}/approvals/{approvalId}/decisions", routes);
        Assert.Contains($"api/companies/{companyId}/conversations/{conversationId}/messages", routes);
        Assert.DoesNotContain(routes, route => route.Contains("mobile/approvals", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("mobile/chat", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("MainPage")]
    [InlineData("///MainPage")]
    [InlineData("alerts")]
    [InlineData("approvals")]
    [InlineData("daily-briefing")]
    [InlineData("direct-agent-chat")]
    [InlineData("company-status")]
    [InlineData("task-follow-up")]
    public void Supported_companion_routes_are_allowed(string route)
    {
        Assert.True(MobileCompanionScope.IsRouteSupported(route));
    }

    [Theory]
    [InlineData("onboarding")]
    [InlineData("company-setup")]
    [InlineData("agents")]
    [InlineData("agent-hiring")]
    [InlineData("agent-configuration")]
    [InlineData("workflows")]
    [InlineData("workflow-builder")]
    [InlineData("executive-cockpit")]
    [InlineData("cockpit")]
    [InlineData("analytics")]
    [InlineData("admin")]
    [InlineData("management")]
    [InlineData("settings")]
    [InlineData("admin/approvals")]
    [InlineData("workflow-builder/task-follow-up")]
    public void Web_first_admin_routes_are_not_allowed(string route)
    {
        Assert.False(MobileCompanionScope.IsRouteSupported(route));
    }
}
