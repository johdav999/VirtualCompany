using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages;

namespace VirtualCompany.Web.Tests;

public sealed class ResponsibilitySettingsPageTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Owner_sees_responsive_matrix_complete_context_and_edit_controls()
    {
        using var context = CreateContext(new FakeClient(CreateData(canManage: true)));
        var cut = Render(context);

        cut.WaitForAssertion(() => Assert.Equal(6, cut.FindAll(".responsibility-card").Count));
        Assert.Contains("routing context, not permission", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Responsible person", cut.Markup);
        Assert.Contains("Primary agent", cut.Markup);
        Assert.Contains("Approval policy", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".responsibility-editor button"));
    }

    [Fact]
    public void Ordinary_member_receives_backend_driven_read_only_surface()
    {
        using var context = CreateContext(new FakeClient(CreateData(canManage: false)));
        var cut = Render(context);

        cut.WaitForAssertion(() => Assert.Contains("Only company owners and administrators", cut.Markup));
        Assert.Empty(cut.FindAll(".responsibility-editor button"));
        Assert.All(cut.FindAll(".responsibility-card"), card => Assert.Contains("Visible to all active members", card.TextContent));
    }

    [Fact]
    public void Replace_preview_requires_explicit_confirmation_before_apply()
    {
        var api = new FakeClient(CreateData(canManage: true));
        using var context = CreateContext(api);
        var cut = Render(context);
        cut.WaitForElement(".responsibility-preset__controls select");

        cut.FindAll(".responsibility-preset__controls select")[2].Change("replace_existing");
        cut.Find(".responsibility-preset__controls button").Click();
        cut.WaitForElement(".responsibility-confirm");
        Assert.True(cut.Find(".responsibility-preview > button").HasAttribute("disabled"));

        cut.Find(".responsibility-confirm input").Change(true);
        cut.Find(".responsibility-preview > button").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, api.ApplyCalls));
    }

    [Fact]
    public void Fill_missing_preview_distinguishes_retained_and_added_assignments()
    {
        var api = new FakeClient(CreateData(canManage: true));
        using var context = CreateContext(api);
        var cut = Render(context);
        cut.WaitForElement(".responsibility-preset__controls button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Retain", cut.Find("[data-testid='preset-preview']").TextContent);
            Assert.Contains("Add", cut.Find("[data-testid='preset-preview']").TextContent);
        });
    }

    [Fact]
    public void Owner_edit_updates_matrix_and_renders_success_state()
    {
        var api = new FakeClient(CreateData(canManage: true));
        using var context = CreateContext(api);
        var cut = Render(context);
        cut.WaitForElement(".responsibility-editor__actions .btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, api.UpsertCalls);
            Assert.Contains("assignment was saved", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Server_validation_keeps_editor_visible_and_places_field_error_by_control()
    {
        var api = new FakeClient(CreateData(canManage: true)) { FailUpsertValidation = true };
        using var context = CreateContext(api);
        var cut = Render(context);
        cut.WaitForElement(".responsibility-editor__actions .btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".responsibility-editor"));
            Assert.Contains("The member is inactive.", cut.Find(".responsibility-editor .validation-message").TextContent);
        });
    }

    [Fact]
    public void Stale_assignments_and_empty_compatible_agent_state_are_actionable()
    {
        var view = CreateData(canManage: true);
        view.Assignments.Add(new ResponsibilityAssignmentViewModel
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ResponsibilityArea = "company_performance", AssignmentKind = "primary",
            AssignedMember = new() { MembershipId = Guid.NewGuid(), DisplayName = "Former owner", Role = "owner", Status = "revoked" },
            AuthorityLevel = "level_1", Version = 2
        });
        using var context = CreateContext(new FakeClient(view));
        var cut = Render(context);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Inactive member", cut.Markup);
            Assert.Contains("No active agent is compatible", cut.Markup);
        });
    }

    [Fact]
    public void Read_failure_renders_retryable_error_state()
    {
        using var context = CreateContext(new FakeClient(CreateData(true)) { FailRead = true });
        var cut = Render(context);

        cut.WaitForAssertion(() => Assert.Contains("Responsibilities could not be loaded", cut.Markup));
        Assert.NotEmpty(cut.FindAll("button.btn-outline-danger"));
    }

    private static TestContext CreateContext(FakeClient api)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton<IResponsibilitySettingsApiClient>(api);
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }));
        return context;
    }

    private static IRenderedComponent<ResponsibilitySettings> Render(TestContext context)
    {
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/settings/responsibilities?companyId={CompanyId:D}");
        return context.RenderComponent<ResponsibilitySettings>();
    }

    private static ResponsibilitySettingsViewModel CreateData(bool canManage)
    {
        var owner = new ResponsibilityMemberViewModel { MembershipId = Guid.NewGuid(), DisplayName = "Owner", Email = "owner@example.com", Role = "owner", Status = "active" };
        var sales = new ResponsibilityAgentViewModel { AgentId = Guid.NewGuid(), DisplayName = "Alex", RoleName = "Sales Manager", Status = "active", CompatibleAreas = ["sales"] };
        return new ResponsibilitySettingsViewModel { CompanyId = CompanyId, CompanySize = "micro", CanManage = canManage, Members = [owner], Agents = [sales], Assignments = [] };
    }

    private sealed class FakeClient(ResponsibilitySettingsViewModel data) : IResponsibilitySettingsApiClient
    {
        public int ApplyCalls { get; private set; }
        public int UpsertCalls { get; private set; }
        public bool FailRead { get; init; }
        public bool FailUpsertValidation { get; init; }
        public Task<ResponsibilitySettingsViewModel?> GetAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            FailRead ? Task.FromException<ResponsibilitySettingsViewModel?>(new ResponsibilitySettingsApiException("Backend unavailable.")) : Task.FromResult<ResponsibilitySettingsViewModel?>(data);
        public Task<ResponsibilityPresetPreviewViewModel> PreviewAsync(Guid companyId, ResponsibilityPresetRequestViewModel request, CancellationToken cancellationToken = default) => Task.FromResult(new ResponsibilityPresetPreviewViewModel
        {
            CompanyId = companyId, CompanySize = request.CompanySize, Mode = request.Mode,
            Changes = request.Mode == "replace_existing"
                ? [new() { ResponsibilityArea = "sales", AssignmentKind = "primary", ChangeKind = "replace", AssignedMembershipId = data.Members[0].MembershipId }]
                : [
                    new() { ResponsibilityArea = "sales", AssignmentKind = "primary", ChangeKind = "retain", AssignedMembershipId = data.Members[0].MembershipId },
                    new() { ResponsibilityArea = "marketing", AssignmentKind = "primary", ChangeKind = "add", AssignedMembershipId = data.Members[0].MembershipId }
                ]
        });
        public Task<ResponsibilityPresetApplyResultViewModel> ApplyAsync(Guid companyId, ResponsibilityPresetRequestViewModel request, CancellationToken cancellationToken = default) { ApplyCalls++; return Task.FromResult(new ResponsibilityPresetApplyResultViewModel()); }
        public Task<ResponsibilityAssignmentViewModel> UpsertAsync(Guid companyId, UpsertResponsibilityAssignmentRequest request, CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            if (FailUpsertValidation) return Task.FromException<ResponsibilityAssignmentViewModel>(new ResponsibilitySettingsApiException(
                "Choose an active member.", new Dictionary<string, string[]> { ["assignedMembershipId"] = ["The member is inactive."] }));
            return Task.FromResult(new ResponsibilityAssignmentViewModel
            {
                Id = request.AssignmentId ?? Guid.NewGuid(), CompanyId = companyId, ResponsibilityArea = request.ResponsibilityArea,
                AssignmentKind = request.AssignmentKind, AssignedMember = data.Members.Single(m => m.MembershipId == request.AssignedMembershipId),
                PrimaryAgent = data.Agents.FirstOrDefault(a => a.AgentId == request.PrimaryAgentId), AuthorityLevel = request.AuthorityLevel,
                ApprovalPolicyId = request.ApprovalPolicyId, EscalationMember = data.Members.FirstOrDefault(m => m.MembershipId == request.EscalationMembershipId), Version = 1
            });
        }
        public Task RemoveAsync(Guid companyId, Guid assignmentId, long? expectedVersion, string? reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
