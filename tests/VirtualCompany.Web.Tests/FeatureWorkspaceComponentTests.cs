using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Agents;
using VirtualCompany.Web.Components.Marketing;
using VirtualCompany.Web.Features.Agents;
using VirtualCompany.Web.Features.Marketing;
using VirtualCompany.Web.Pages.Marketing;

namespace VirtualCompany.Web.Tests;

public sealed class FeatureWorkspaceComponentTests
{
    [Fact]
    public void Brief_edits_enable_save_and_category_changes_keep_unsaved_text()
    {
        using var context = Context();
        var state = new AgentBriefEditorState();
        var saves = 0;
        var cut = context.RenderComponent<AgentBriefEditor>(p => p
            .Add(x => x.Profile, Profile())
            .Add(x => x.State, state)
            .Add(x => x.HandleBriefTextInput, args => state.SetText(args.Value?.ToString()))
            .Add(x => x.SelectBriefCategory, state.SelectBriefCategory)
            .Add(x => x.SaveAgentBriefAsync, () => saves++));

        Assert.Equal("Briefing categories", cut.Find("nav").GetAttribute("aria-label"));
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
        cut.Find("textarea").Input("Company evidence");
        Assert.True(state.IsBriefDirty);
        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
        state.IsGeneratedBriefDraft = true;
        state.BriefSuccessMessage = "Previous draft";
        cut.FindAll(".agent-brief-category")[1].Click();
        cut.Find("textarea").Input("Product evidence");
        cut.FindAll(".agent-brief-category")[0].Click();

        Assert.Equal("Company evidence", cut.Find("textarea").GetAttribute("value"));
        Assert.Equal("Product evidence", state.AgentBriefModel.Get("products_and_services"));
        Assert.True(state.IsBriefDirty);
        Assert.False(state.IsGeneratedBriefDraft);
        Assert.Null(state.BriefSuccessMessage);
        cut.Find("button.btn-primary").Click();
        Assert.Equal(1, saves);
    }

    [Fact]
    public void Brief_documents_respect_agent_category_and_explicit_team_sharing()
    {
        var agent = Guid.NewGuid();
        var privateDocument = Document(agent, "company_information", false, "private.pdf");
        var shared = Document(Guid.NewGuid(), "company_information", true, "shared.pdf");
        var state = new AgentBriefEditorState
        {
            BriefDocuments = [privateDocument, shared,
                Document(Guid.NewGuid(), "company_information", false, "other-agent.pdf"),
                Document(agent, "policies", false, "policies.pdf")]
        };
        Assert.Equal(2, state.DocumentsForCategory(agent).Count);
        using var context = Context();
        var cut = context.RenderComponent<AgentBriefEditor>(p => p
            .Add(x => x.Profile, Profile()).Add(x => x.State, state)
            .Add(x => x.BriefDocumentsForSelectedCategory, state.DocumentsForCategory(agent)));
        Assert.Equal(2, cut.FindAll(".agent-brief-document").Count);
        Assert.Contains("private.pdf", cut.Markup);
        Assert.Contains("shared.pdf", cut.Markup);
        Assert.DoesNotContain("other-agent.pdf", cut.Markup);
        state.SelectBriefCategory("policies");
        Assert.Equal("policies.pdf", Assert.Single(state.DocumentsForCategory(agent)).OriginalFileName);
    }

    [Fact]
    public void Operating_profile_editor_sends_edited_visible_fields_and_excludes_hidden_governance()
    {
        using var context = Context();
        var profile = Profile();
        profile.Visibility.CanEditObjectives = true;
        profile.Visibility.CanEditRoleBrief = true;
        var state = new AgentOperatingProfileEditorState();
        // Restricted fields must not be parsed or sent, even if stale editor text is malformed.
        state.ProfileModel.ToolPermissionsJson = "invalid JSON";
        state.ProfileModel.WorkingHoursJson = "invalid JSON";
        UpdateAgentOperatingProfileRequest? saved = null;
        var cut = context.RenderComponent<AgentOperatingProfileEditor>(p => p
            .Add(x => x.Profile, profile).Add(x => x.State, state)
            .Add(x => x.ProfileHref, "/agents/profile?companyId=fixture")
            .Add(x => x.HandleProfileSaveAsync, () =>
            {
                Assert.True(state.ProfileModel.TryCreateRequest(profile.Visibility, out var request, out var errors));
                Assert.Empty(errors);
                saved = request;
            }));
        Assert.Empty(cut.FindAll("#ToolPermissions"));
        Assert.Empty(cut.FindAll("#WorkingHours"));
        cut.Find("#Objectives").Change("{\"goal\":\"Improve response time\"}");
        cut.Find("#role-brief").Change("Keep replies grounded");
        cut.Find("form").Submit();
        Assert.Equal("Improve response time", saved!.Objectives!["goal"]!.GetValue<string>());
        Assert.Equal("Keep replies grounded", saved.RoleBrief);
        Assert.Null(saved.ToolPermissions);
        Assert.Null(saved.WorkingHours);
        Assert.Equal("/agents/profile?companyId=fixture", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void Changing_payment_selection_requires_another_review_before_commit()
    {
        using var context = Context();
        var bill = Guid.NewGuid();
        var state = new AgentRoleAnalysisState
        {
            RoleAnalysisType = "payables", PaymentRunReviewed = true,
            SelectedPaymentBillIds = [bill],
            FinancePaymentRun = new()
            {
                SnapshotToken = "evidence-snapshot",
                Items = [new() { BillId = bill, BillNumber = "BILL-123", Group = "pay", Currency = "SEK" }]
            }
        };
        var cut = context.RenderComponent<AgentRoleAnalysisPanel>(p => p
            .Add(x => x.Profile, Profile()).Add(x => x.State, state));
        cut.FindAll("input[type=checkbox]").First().Change(false);
        Assert.False(state.PaymentRunReviewed);
        Assert.DoesNotContain(bill, state.SelectedPaymentBillIds);
        state.TogglePaymentBill(bill, true);
        Assert.Contains(bill, state.SelectedPaymentBillIds);
        state.ResetFinanceAnalysis();
        Assert.Null(state.FinancePaymentRun);
        Assert.Empty(state.SelectedPaymentBillIds);
        Assert.False(state.PaymentRunReviewed);
    }

    [Fact]
    public void Objective_form_and_record_actions_use_shared_state_and_exact_ids()
    {
        using var context = Context();
        var id = Guid.NewGuid();
        var state = new MarketingObjectivesState();
        var dashboard = EmptyDashboard() with
        {
            Objectives = [new(id, "Qualified demand", "leads", 100, "leads", null,
                new(2026, 10, 1), new(2026, 12, 31), "draft", 1)]
        };
        string? createdName = null;
        Guid? activated = null;
        var cut = context.RenderComponent<MarketingObjectivesWorkspace>(p => p
            .Add(x => x.Dashboard, dashboard).Add(x => x.ObjectivesState, state)
            .Add(x => x.CreateObjectiveAsync, () => createdName = state.ObjectiveForm.Name)
            .Add(x => x.ActivateObjectiveAsync, value => activated = value));
        cut.Find("form input").Change("New objective");
        cut.Find("form").Submit();
        cut.Find("article button").Click();
        Assert.Equal("New objective", createdName);
        Assert.Equal(id, activated);
    }

    [Fact]
    public void Plan_form_recalculates_eligibility_after_child_edits_and_strategy_changes()
    {
        using var context = Context();
        var segment = Guid.NewGuid();
        var version = new MarketingSegmentVersionViewModel(Guid.NewGuid(), segment, 1, "{}", "[]", "[]", "[]", "{}",
            null, null, "", 1, "{}", "{}", 1, "[]", DateTime.UtcNow, "approved", "target", null, null, 1);
        var strategy = new MarketingStrategyViewModel(Guid.NewGuid(), "Growth", "", "", new(2026, 1, 1), new(2027, 1, 1),
            "{}", "[]", "[]", "approved", null, 1, DateTime.UtcNow, [version.Id]);
        var state = new MarketingPlansState
        {
            ShowPlanForm = true, Strategies = [strategy],
            Objectives = [new(Guid.NewGuid(), "Demand", "leads", 10, "leads", null, new(2026, 1, 1), new(2027, 1, 1), "active", 1)],
            Segments = [new(segment, "Buyers", "", false, [version])]
        };
        state.PlanForm.StartsUtc = new(2026, 10, 1);
        state.PlanForm.EndsUtc = new(2026, 12, 31);
        state.InitializePlanFormSelections();
        var creates = 0;
        var cut = context.RenderComponent<MarketingPlansWorkspace>(p => p
            .Add(x => x.PlansState, state).Add(x => x.SegmentsState, new MarketingSegmentsState())
            .Add(x => x.CreateGroundedPlanAsync, () => creates++));
        Assert.True(cut.Find("form button.btn-primary").HasAttribute("disabled"));
        cut.Find("form input").Change("Quarterly growth");
        cut.Find("form textarea").Change("Grow qualified demand");
        Assert.False(cut.Find("form button.btn-primary").HasAttribute("disabled"));
        cut.Find("form").Submit();
        Assert.Equal(1, creates);
        cut.Find("form select").Change(Guid.Empty.ToString());
        Assert.Empty(state.AvailablePlanSegments);
        Assert.True(cut.Find("form button.btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public void Content_deep_link_keeps_company_and_return_context_and_reviews_the_selected_record()
    {
        using var context = Context();
        var company = Guid.NewGuid();
        var selected = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var origin = $"/dashboard?companyId={company:D}&lens=marketing";
        MarketingContentBriefViewModel Brief(Guid id) => new(id, campaign, null, "Launch brief", "Launch", "Buyers", "email", "en", "", "", null, "submitted", 1, []);
        var dashboard = EmptyDashboard() with { CompanyId = company, Content = [Brief(selected), Brief(Guid.NewGuid())] };
        (Guid Id, bool Approved)? reviewed = null;
        var cut = context.RenderComponent<MarketingContentWorkspace>(p => p
            .Add(x => x.Dashboard, dashboard).Add(x => x.ContentState, new MarketingContentState())
            .Add(x => x.CompanyId, company).Add(x => x.BriefId, selected).Add(x => x.WorkspaceOrigin, origin)
            .Add(x => x.ReviewContentAsync, value => reviewed = value));
        Assert.Single(cut.FindAll(".marketing-content-list article"));
        Assert.Equal(MarketingJourneyRoutes.Review(company, campaign, selected, origin), cut.Find("article a").GetAttribute("href"));
        cut.Find("article button.btn-primary").Click();
        Assert.Equal((selected, true), reviewed);
    }

    public static TheoryData<Type> Workspaces => new()
    {
        typeof(MarketingOverviewWorkspace), typeof(MarketingStrategyWorkspace), typeof(MarketingSegmentsWorkspace),
        typeof(MarketingIntelligenceWorkspace), typeof(MarketingObjectivesWorkspace), typeof(MarketingPlansWorkspace),
        typeof(MarketingCalendarWorkspace), typeof(MarketingContentWorkspace), typeof(MarketingAudiencesWorkspace),
        typeof(MarketingHandoffsWorkspace), typeof(MarketingExperimentsWorkspace), typeof(MarketingChannelsWorkspace),
        typeof(MarketingJourneysWorkspace), typeof(MarketingPerformanceWorkspace), typeof(MarketingOperationsWorkspace)
    };

    [Theory]
    [MemberData(nameof(Workspaces))]
    public void Marketing_route_composes_the_requested_feature_with_company_context(Type component)
    {
        using var context = Context();
        var company = Guid.NewGuid();
        var section = component.Name["Marketing".Length..^"Workspace".Length];
        var http = new HttpClient(new MarketingReadHandler(company)) { BaseAddress = new("http://localhost/") };
        context.Services.AddSingleton(new MarketingApiClient(new CompanyApiTransport(http), offline: false));
        context.Services.AddSingleton(new GuidedWorkApiClient(new CompanyApiTransport(http), offline: false));
        context.Services.AddSingleton(new OnboardingApiClient(http));
        context.Services.AddSingleton(new SalesApiClient(http));
        context.Services.AddSingleton(new AgentApiClient(http));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/marketing?companyId={company:D}&section={section}");
        // The isolated renderer does not run the application's Router/query supplier.
        context.ComponentFactories.Add<MarketingDashboard>(() => new() { CompanyId = company, Section = section });
        var cut = context.RenderComponent<MarketingDashboard>();
        Assert.True(cut.FindAll(".marketing-state--error").Count == 0, cut.FindAll(".marketing-state--error").FirstOrDefault()?.TextContent);
        cut.WaitForAssertion(() => Assert.Contains(cut.FindComponents<ComponentBase>(), child => child.Instance.GetType() == component));
        Assert.Equal(section, cut.Find("nav[aria-label='Marketing sections'] a.active").TextContent);
        Assert.All(cut.FindAll("nav[aria-label='Marketing sections'] a"), link =>
            Assert.Contains($"companyId={company:D}", link.GetAttribute("href")));
        Assert.Empty(cut.FindAll(".marketing-state--error"));
    }

    [Theory]
    [MemberData(nameof(Workspaces))]
    public void Empty_feature_workspaces_render_without_backend_services_or_state_names_in_copy(Type component)
    {
        using var context = Context();
        var parameters = new Dictionary<string, object>();
        foreach (var property in component.GetProperties())
        {
            if (property.PropertyType.Namespace == typeof(MarketingPlansState).Namespace && property.Name.EndsWith("State"))
                parameters[property.Name] = Activator.CreateInstance(property.PropertyType)!;
            else if (property.Name == "Dashboard") parameters[property.Name] = EmptyDashboard();
            else if (property.Name == "WithCompany") parameters[property.Name] = (Func<string, string>)(path => path);
        }
        var cut = context.RenderComponent<DynamicComponent>(p => p.Add(x => x.Type, component).Add(x => x.Parameters, parameters));
        Assert.NotEmpty(cut.Markup);
        Assert.DoesNotContain("State.", cut.FindAll("section, article, aside").First().TextContent);
        Assert.DoesNotContain("State.", string.Join(" ", cut.FindAll("[class]").Select(x => x.ClassName)));
    }

    private static TestContext Context()
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static AgentOperatingProfileViewModel Profile() => new()
    {
        Id = Guid.NewGuid(), Department = "finance", DisplayName = "Laura", RoleName = "Finance Manager"
    };

    private static MarketingDashboardViewModel EmptyDashboard() => new(Guid.NewGuid(), DateTime.UtcNow, [], [], [], [], [], [], [], [], []);

    private static AgentBriefDocumentViewModel Document(Guid agent, string category, bool shared, string name) => new()
    {
        Id = Guid.NewGuid(), OriginalFileName = name, UpdatedUtc = DateTime.UtcNow,
        Metadata = new()
        {
            ["purpose"] = JsonValue.Create("agent_brief"), ["agentId"] = JsonValue.Create(agent.ToString()),
            ["briefingCategory"] = JsonValue.Create(category), ["shareWithAgentTeam"] = JsonValue.Create(shared)
        }
    };

    private sealed class MarketingReadHandler(Guid company) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal(HttpMethod.Get, request.Method);
            var response = path switch
            {
                "/api/auth/me" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new CurrentUserContextViewModel
                    {
                        ActiveCompany = new() { CompanyId = company, MembershipRole = "owner", Status = "active" }
                    })
                },
                "/api/marketing/dashboard" => new(HttpStatusCode.OK) { Content = JsonContent.Create(EmptyDashboard() with { CompanyId = company }) },
                "/api/marketing/daily-review" => new(HttpStatusCode.NoContent),
                _ when path == $"/api/companies/{company}/guided-work-sessions" => new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new GuidedWorkSessionListViewModel())
                },
                _ => new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) }
            };
            return Task.FromResult(response);
        }
    }
}
