namespace VirtualCompany.Web.Tests;

public sealed class TeamsMeetingSurfaceTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void Shared_stage_is_stage_safe_exact_aspect_and_acknowledged_only_after_image_load()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Teams", "MeetingStage.razor");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Teams", "MeetingStage.razor.css");

        Assert.Contains("/teams/meetings/{MeetingSessionId:guid}/stage", source, StringComparison.Ordinal);
        Assert.Contains("GetStageSlideImageAsync", source, StringComparison.Ordinal);
        Assert.Contains("@onload=\"AcknowledgeDisplayedAsync\"", source, StringComparison.Ordinal);
        Assert.Contains("AcknowledgeStageRenderAsync", source, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio", source, StringComparison.Ordinal);
        Assert.Contains("object-fit:contain", css.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("SpeakerNotes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanArtifacts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nextSlide", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_panel_exposes_human_preemption_modes_failures_and_accessibility()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Teams", "MeetingSidePanel.razor");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Teams", "MeetingSidePanel.razor.css");

        Assert.Contains("Share to meeting", source, StringComparison.Ordinal);
        Assert.Contains("presentation.previous", source, StringComparison.Ordinal);
        Assert.Contains("presentation.next", source, StringComparison.Ordinal);
        Assert.Contains("presentation.goto", source, StringComparison.Ordinal);
        Assert.Contains("presentation.pause", source, StringComparison.Ordinal);
        Assert.Contains("presentation.resume", source, StringComparison.Ordinal);
        Assert.Contains("manual", source, StringComparison.Ordinal);
        Assert.Contains("assisted", source, StringComparison.Ordinal);
        Assert.Contains("autonomous", source, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@busy\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", source, StringComparison.Ordinal);
        Assert.Contains("min-height:44px", css.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("Use Teams’ Stop presenting control", source, StringComparison.Ordinal);
        Assert.Contains("Request presenter to join", source, StringComparison.Ordinal);
        Assert.Contains("Start presenter audio", source, StringComparison.Ordinal);
        Assert.Contains("Pause presenter · Take control", source, StringComparison.Ordinal);
        Assert.Contains("Revoke meeting consent", source, StringComparison.Ordinal);
        Assert.Contains("waiting for Microsoft Teams provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Approve next", source, StringComparison.Ordinal);
        Assert.Contains("Reject and keep control", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_admin_surface_exposes_verified_rollout_evidence_and_safe_remediation()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "TeamsPresenterAdmin.razor");
        var css = Read("src", "VirtualCompany.Web", "Pages", "TeamsPresenterAdmin.razor.css");

        Assert.Contains("Required access and configuration", source, StringComparison.Ordinal);
        Assert.Contains("Production disabled", source, StringComparison.Ordinal);
        Assert.Contains("Emergency disable company", source, StringComparison.Ordinal);
        Assert.Contains("Not recorded — production remains disabled", source, StringComparison.Ordinal);
        Assert.Contains("Credentials and tokens are never displayed", source, StringComparison.Ordinal);
        Assert.Contains("min-height:44px", css.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("ClientSecret", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Teams_boundary_uses_sso_and_real_capability_checked_stage_share_without_persistence()
    {
        var service = Read("src", "VirtualCompany.Web", "Services", "TeamsMeetingContextService.cs");
        var script = Read("src", "VirtualCompany.Web", "wwwroot", "js", "teams-meeting.js");

        Assert.Contains("getAuthToken", script, StringComparison.Ordinal);
        Assert.Contains("getAppContentStageSharingCapabilities", script, StringComparison.Ordinal);
        Assert.Contains("doesAppHaveSharePermission", script, StringComparison.Ordinal);
        Assert.Contains("shareAppContentToStage", script, StringComparison.Ordinal);
        Assert.Contains("AuthenticationHeaderValue(\"Bearer\"", service, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sales_navigation_uses_invitation_readiness_and_preserves_deep_link_compatibility()
    {
        var lead = Read("src", "VirtualCompany.Web", "Pages", "Sales", "SalesLeadDetail.razor");
        var status = Read("src", "VirtualCompany.Web", "Components", "Sales", "SalesMeetingInvitationPresentationStatus.razor");
        var preparation = Read("src", "VirtualCompany.Web", "Pages", "Sales", "SalesMeetingPreparation.razor");
        var panel = Read("src", "VirtualCompany.Web", "Pages", "Teams", "MeetingSidePanel.razor");

        Assert.Contains("SalesMeetingInvitationPresentationStatus", lead, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery(Name = \"meetingSessionId\")", lead, StringComparison.Ordinal);
        Assert.Contains("CanOpenPresenter", status, StringComparison.Ordinal);
        Assert.Contains("/side-panel?companyId=", status, StringComparison.Ordinal);
        Assert.Contains("BrowserDiagnosticsEnabled", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("stageToken", preparation, StringComparison.Ordinal);
        Assert.Contains("RecoverAuthoritativeStateAsync", panel, StringComparison.Ordinal);
        Assert.Contains("current?.Private", panel, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
