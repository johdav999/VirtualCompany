namespace VirtualCompany.Shared.Mobile;

public enum MobileCompanionCapability
{
    SignIn,
    CompanySelection,
    Alerts,
    Approvals,
    DailyBriefing,
    DirectAgentChat,
    CompanyStatus,
    TaskFollowUp
}

public static class MobileCompanionScope
{
    public const string LandingRoute = "MainPage";
    public const string CompanionScopeDescription = "Mobile is optimized for approvals, alerts, daily briefing, quick company status, task follow-up, and direct agent chat.";
    public const string ProductDirectionDescription = "Web-first, mobile-companion: the Blazor web app remains the primary command center, while the .NET MAUI app is the intended focused companion.";
    public const string ResponsiveWebBridgeMessage = "Responsive web may cover some early mobile access, but it is an interim bridge rather than the final mobile strategy.";
    public const string MauiTargetCompanionMessage = "The long-term companion experience remains the .NET MAUI app for approvals, alerts, briefings, quick chat, and lightweight follow-up.";
    public const string WebFirstAdministrationMessage = "For full setup, agent configuration, workflow administration, cockpit analytics, and system management, use the web app.";
    public const string SharedBackendApiReuseMessage = "Web and MAUI clients reuse the same backend APIs and shared contracts; mobile does not get separate business workflows in this slice.";

    public static IReadOnlyList<MobileCompanionCapability> SupportedCapabilities { get; } =
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

    public static IReadOnlyList<string> SupportedFeatureLabels { get; } =
    [
        "sign-in",
        "company selection",
        "alerts",
        "approvals",
        "daily briefing",
        "direct agent chat",
        "company status",
        "task follow-up"
    ];

    public static IReadOnlyDictionary<MobileCompanionCapability, string> CapabilityRouteKeys { get; } =
        new Dictionary<MobileCompanionCapability, string>
        {
            [MobileCompanionCapability.SignIn] = "sign-in",
            [MobileCompanionCapability.CompanySelection] = "company-selection",
            [MobileCompanionCapability.Alerts] = "alerts",
            [MobileCompanionCapability.Approvals] = "approvals",
            [MobileCompanionCapability.DailyBriefing] = "daily-briefing",
            [MobileCompanionCapability.DirectAgentChat] = "direct-agent-chat",
            [MobileCompanionCapability.CompanyStatus] = "company-status",
            [MobileCompanionCapability.TaskFollowUp] = "task-follow-up"
        };

    public static IReadOnlySet<string> UnsupportedRouteKeys { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "admin",
            "agents",
            "agent-admin",
            "agent-configuration",
            "agent-hiring",
            "analytics",
            "cockpit",
            "company-setup",
            "executive-cockpit",
            "management",
            "onboarding",
            "settings",
            "workflow-builder",
            "workflow-definition",
            "workflows"
        };

    public static bool IsRouteSupported(string? route)
    {
        var normalized = NormalizeRoute(route);
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, LandingRoute, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 &&
            !segments.Any(segment => UnsupportedRouteKeys.Contains(segment)) &&
            segments.Any(segment => CapabilityRouteKeys.Values.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeRoute(string? route) =>
        (route ?? string.Empty).Split('?', '#')[0].Trim('/');
}
