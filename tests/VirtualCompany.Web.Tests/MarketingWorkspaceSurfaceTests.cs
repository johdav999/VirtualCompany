namespace VirtualCompany.Web.Tests;

public sealed class MarketingWorkspaceSurfaceTests
{
    [Fact]
    public void Creative_workspace_exposes_fail_closed_scan_and_recovery_states()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");

        Assert.Contains("Quarantined — an authoritative safety scan must pass", source, StringComparison.Ordinal);
        Assert.Contains("Request changes", source, StringComparison.Ordinal);
        Assert.Contains(">Rescan</button>", source, StringComparison.Ordinal);
        Assert.Contains("scan?.Result != \"passed\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("malwareScan = \"storage_provider_required\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Segment_workspace_allows_reviewable_bootstrap_drafts_and_exposes_retry()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");

        Assert.Contains("!segmentProposal.CanCreateDraft", source, StringComparison.Ordinal);
        Assert.Contains("Create reviewable draft", source, StringComparison.Ordinal);
        Assert.Contains("Retry Maya", source, StringComparison.Ordinal);
        Assert.Contains("Evidence gaps remain visible", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Marketing_client_uses_company_scoped_scan_and_recovery_routes()
    {
        var source = Read("src", "VirtualCompany.Web", "Services", "MarketingApiClient.cs");

        Assert.Contains("GetCreativeAssetScansAsync(Guid companyId", source, StringComparison.Ordinal);
        Assert.Contains("RequestCreativeAssetChangesAsync(Guid companyId", source, StringComparison.Ordinal);
        Assert.Contains("RescanCreativeAssetAsync(Guid companyId", source, StringComparison.Ordinal);
        Assert.Contains("api/marketing/creative-assets/{assetId:D}/scans", source, StringComparison.Ordinal);
        Assert.Contains("api/marketing/creative-assets/{assetId:D}/request-changes", source, StringComparison.Ordinal);
        Assert.Contains("api/marketing/creative-assets/{assetId:D}/rescan", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Marketing_workspace_retains_narrow_width_layout_rules()
    {
        var css = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor.css");

        Assert.Contains("@media", css, StringComparison.Ordinal);
        Assert.Contains("max-width", css, StringComparison.Ordinal);
        Assert.Contains("overflow", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Strategy_workshop_action_uses_the_responsive_section_header()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");

        Assert.Contains("<div class=\"marketing-section__header\">", source, StringComparison.Ordinal);
        Assert.Contains("GuidedWorkApi.ListAsync(companyId, artifactType: \"marketing_strategy\")", source, StringComparison.Ordinal);
        Assert.Contains("UNSAVED WORKSHOP DRAFT", source, StringComparison.Ordinal);
        Assert.Contains("Resume workshop", source, StringComparison.Ordinal);
        Assert.Contains("Resume and finish", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan 4P &amp; STP strategy with Maya", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_workspace_is_plan_first_policy_driven_and_exposes_daily_review_and_calendar_semantics()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");
        var client = Read("src", "VirtualCompany.Web", "Services", "MarketingApiClient.cs");

        Assert.Contains("Ask Maya to populate plan", source, StringComparison.Ordinal);
        Assert.Contains("Create marketing plan", source, StringComparison.Ordinal);
        Assert.Contains("CreateGroundedPlanAsync", source, StringComparison.Ordinal);
        Assert.Contains("This older plan has no linked objective or approved audience", source, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedPlan.Objectives.Count == 0 || selectedPlan.Segments.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("selectedPlan.AllowedActions.Contains(\"Submit for review\")", source, StringComparison.Ordinal);
        Assert.Contains("MAYA DAILY REVIEW", source, StringComparison.Ordinal);
        Assert.Contains("item.IsSpan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Decompose into campaign work", source, StringComparison.Ordinal);
        Assert.Contains("SubmitPlanForReviewAsync", client, StringComparison.Ordinal);
        Assert.Contains("ActivateGroundedPlanAsync", client, StringComparison.Ordinal);
        Assert.Contains("api/marketing/plans/grounded", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_workspace_starts_and_resumes_the_shared_marketing_plan_workshop()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");
        var workshop = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");

        Assert.Contains("/workshops/marketing_plan", source, StringComparison.Ordinal);
        Assert.Contains("GuidedWorkApi.ListAsync(companyId, artifactType: \"marketing_plan\")", source, StringComparison.Ordinal);
        Assert.Contains("Create plan with Maya", source, StringComparison.Ordinal);
        Assert.Contains("planWorkshopDraft", source, StringComparison.Ordinal);
        Assert.Contains("UNSAVED WORKSHOP DRAFT", source, StringComparison.Ordinal);
        Assert.Contains("section=Plans", workshop, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
