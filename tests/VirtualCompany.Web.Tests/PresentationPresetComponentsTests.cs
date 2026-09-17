using Bunit;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Pages.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class PresentationPresetComponentsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Meeting_selector_uses_server_eligibility_even_with_a_stale_presenting_status(bool allowed)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var transport = new CompanyApiTransport(new HttpClient { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(sp => new SalesPresentationPresetApiClient(transport, true, sp.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(sp => new SalesPresentationRunApiClient(transport, true, sp.GetRequiredService<IApiProblemMessageResolver>()));
        var cut = context.RenderComponent<SalesMeetingPresetSelector>(p => p
            .Add(x => x.CompanyId, Guid.NewGuid()).Add(x => x.InvitationId, Guid.NewGuid())
            .Add(x => x.SessionId, Guid.NewGuid()).Add(x => x.SessionStatus, "presenting")
            .Add(x => x.CanChangePreset, allowed));
        cut.WaitForAssertion(() => Assert.Equal(!allowed, cut.Find("select").HasAttribute("disabled")));
        if (allowed) Assert.DoesNotContain("Leave the meeting room", cut.Markup);
    }

    [Theory]
    [InlineData("draft", "published", true)]
    [InlineData("published", "published", false)]
    [InlineData("draft", "archived", false)]
    public void Overview_offers_edit_only_for_the_selected_editable_draft(string versionState, string presetState, bool editable)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var version = new SalesPresentationPresetVersionViewModel(Guid.NewGuid(), 2, versionState, null, null, "Goal", "Audience",
            30, null, "assisted", "en", ["sales_meeting"], null, null, null, DateTime.UtcNow, DateTime.UtcNow, 1, null);
        var preset = new SalesPresentationPresetViewModel(Guid.NewGuid(), Guid.NewGuid(), "Demo", null, Guid.NewGuid(),
            presetState, null, DateTime.UtcNow, DateTime.UtcNow, null, 1, 0, version, null, [version], []);
        var editRequested = false;
        var cut = context.RenderComponent<PresentationPresetSummary>(p => p.Add(x => x.Preset, preset)
            .Add(x => x.Version, version).Add(x => x.OnEdit, () => editRequested = true));
        Assert.Equal(editable ? 1 : 0, cut.FindAll("button").Count);
        if (!editable) return;
        Assert.Equal("Edit draft", cut.Find("button").TextContent.Trim());
        cut.Find("button").Click();
        Assert.True(editRequested);
        cut.SetParametersAndRender(p => p.Add(x => x.IsBusy, true));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Draft_editor_saves_changed_fields_through_existing_save_action()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var model = PresentationPresetEditModel.Create(Guid.NewGuid(), "You");
        model.Name = "Before";
        SalesPresentationPresetUpdateRequest? saved = null;
        var cut = context.RenderComponent<PresentationPresetEditor>(p => p.Add(x => x.Model, model)
            .Add(x => x.OnSave, () => { saved = model.ToUpdateRequest(3, 7); }));
        cut.Find("#preset-name").Change("Updated draft");
        cut.FindAll("input[type=number]")[0].Change("45");
        cut.FindAll("button").Single(x => x.TextContent == "Save draft").Click();
        Assert.NotNull(saved);
        Assert.Equal("Updated draft", saved.Name);
        Assert.Equal(45, saved.DurationMinutes);
        Assert.Equal(7, saved.ExpectedDraftVersion);
    }

    [Fact]
    public void Cover_uses_scoped_first_slide_and_recovers_when_source_changes()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var company=Guid.NewGuid();var preset=Guid.NewGuid();var slide=Guid.NewGuid();
        var cut=context.RenderComponent<PresentationPresetCover>(p=>p.Add(x=>x.CompanyId,company)
            .Add(x=>x.PresetId,preset).Add(x=>x.SlideId,slide).Add(x=>x.Name,"Executive demo"));
        Assert.Equal(SalesPresentationPresetApiClient.CoverUrl(company,preset,slide),cut.Find("img").GetAttribute("src"));
        Assert.Equal("First slide of Executive demo",cut.Find("img").GetAttribute("alt"));
        Assert.Equal("lazy",cut.Find("img").GetAttribute("loading"));
        cut.Find("img").TriggerEvent("onerror",EventArgs.Empty);
        Assert.Empty(cut.FindAll("img"));
        Assert.Contains("No slide preview",cut.Markup);
        cut.SetParametersAndRender(p=>p.Add(x=>x.SlideId,Guid.NewGuid()));
        Assert.Single(cut.FindAll("img"));
    }

    [Fact]
    public void Cover_explains_processing_without_requesting_a_missing_image()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut=context.RenderComponent<PresentationPresetCover>(p=>p.Add(x=>x.Processing,true));
        Assert.Empty(cut.FindAll("img"));Assert.Contains("Preparing slide preview",cut.Markup);
    }

    [Fact]
    public void Ad_hoc_dialog_exposes_optional_context_and_never_offers_an_unbound_live_presenter()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var company = Guid.NewGuid(); var presetId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var agentId = Guid.NewGuid();
        var handler = new AdHocHandler(agentId, presetId, versionId);
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(serviceProvider => new SalesPresentationAdHocApiClient(transport, false, serviceProvider.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(serviceProvider => new SalesPresentationPresetApiClient(transport, false, serviceProvider.GetRequiredService<IApiProblemMessageResolver>()));
        var asset = new SalesPresentationPresetAssetViewModel(Guid.NewGuid(), "demo.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", 1024, new string('a', 64), "processed", 1, 1, 1, "renderer", "1", "flattened", null, null, false, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, 1);
        var version = new SalesPresentationPresetVersionViewModel(versionId, 2, "published", agentId, null, "Explain the product", "Finance leaders", 30, null, "assisted", "en", ["ad_hoc"], null, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, 1, asset);
        var preset = new SalesPresentationPresetViewModel(presetId, company, "Executive demo", "Reusable approved source", Guid.NewGuid(), "published", versionId, DateTime.UtcNow, DateTime.UtcNow, null, 1, 0, null, version, [version], ["use"]);

        var cut = context.RenderComponent<AdHocPresentationRunDialog>(parameters => parameters.Add(x => x.CompanyId, company).Add(x => x.Preset, preset).Add(x => x.Version, version));
        cut.WaitForAssertion(() => Assert.Contains("Customer context", cut.Markup));
        Assert.Contains("No account", cut.Markup);
        Assert.Contains("Prepare only — no meeting created", cut.Markup);
        Assert.Contains("No customer context selected", cut.Markup);
        Assert.DoesNotContain("Start presentation", cut.Markup);
        Assert.DoesNotContain("Open presenter", cut.Markup);

        cut.FindAll("button").Single(x => x.TextContent.Contains("Prepare presentation")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Schedule or select an authorized meeting", cut.Markup));
        Assert.Equal("preparation_only", handler.RuntimeStrategy);
        Assert.DoesNotContain("Open presenter", cut.Markup);
    }

    [Fact]
    public void Campaign_editor_distinguishes_activity_readiness_projection_and_individual_runs()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var transport = new CompanyApiTransport(http);
        context.Services.AddSingleton(new SalesApiClient(http, useOfflineMode: true));
        context.Services.AddSingleton(serviceProvider => new SalesPresentationPresetApiClient(transport, true,
            serviceProvider.GetRequiredService<IApiProblemMessageResolver>()));
        var activity = new CampaignActivityResponse(Guid.NewGuid(), "Quarterly business review", "presentation", "presentation", "manual", "ready",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(1), Guid.NewGuid(), Guid.NewGuid(), null, null, 0, null, null);
        var config = new CampaignPresentationActivityResponse(Guid.NewGuid(), Guid.NewGuid(), activity.Id, Guid.NewGuid(), "Quarterly business review",
            Guid.NewGuid(), 3, "per_account", "campaign_owner", null, "preparation_task", false, null, 24, 1, false,
            new(18, 7, 7, "account", true),
            [new("campaign.presentation.presenter_unavailable", "Presenter missing for 2 accounts", "Two account owners are unavailable.", "Choose an explicit presenter.", true, true)],
            [new(Guid.NewGuid(), Guid.NewGuid(), "account", Guid.NewGuid(), "prepared", null, null, "preparing", Guid.NewGuid())]);
        var cut = context.RenderComponent<CampaignPresentationActivityEditor>(p => p
            .Add(x => x.CompanyId, Guid.NewGuid()).Add(x => x.CampaignId, config.CampaignId).Add(x => x.Activity, activity).Add(x => x.Config, config));
        Assert.Contains("Pinned version", cut.Markup);
        Assert.Contains("Presenter missing for 2 accounts", cut.Markup);
        Assert.Contains("Preparation runs", cut.Markup);
        Assert.DoesNotContain("Start presentation", cut.Markup);
        Assert.DoesNotContain("Send email", cut.Markup);
    }

    [Fact]
    public void Editor_labels_controls_and_surfaces_validation_without_raw_json()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = context.RenderComponent<PresentationPresetEditor>(p => p
            .Add(x => x.Model, PresentationPresetEditModel.Create(Guid.NewGuid(), "Avery"))
            .Add(x => x.Errors, new Dictionary<string, string[]> { ["name"] = ["Enter a preset name."] }));
        Assert.NotNull(cut.Find("label input#preset-name"));
        Assert.Contains("Name", cut.Markup);
        Assert.Contains("Enter a preset name", cut.Markup);
        Assert.DoesNotContain("BehaviorSettingsJson", cut.Markup);
    }

    [Fact]
    public void Creation_editor_has_production_hierarchy_valid_defaults_and_cancel_path()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var cancelled = false;
        var model = PresentationPresetEditModel.Create(Guid.NewGuid(), "You");
        var cut = context.RenderComponent<PresentationPresetEditor>(parameters => parameters
            .Add(x => x.Model, model)
            .Add(x => x.IsCreation, true)
            .Add(x => x.OnCancel, () => cancelled = true)
            .Add(x => x.SubmitLabel, "Create preset"));

        Assert.Equal("assisted", model.ControlMode);
        Assert.Contains("Reusable by design", cut.Markup);
        Assert.Contains("Identity &amp; ownership", cut.Markup);
        Assert.Contains("Presentation defaults", cut.Markup);
        Assert.Contains("Where this preset can be used", cut.Markup);
        Assert.Contains("Presenter behavior", cut.Markup);
        Assert.Equal("5", cut.Find("input[type=number]").GetAttribute("min"));
        Assert.NotNull(cut.Find("input[readonly]"));
        Assert.Equal(3, cut.FindAll(".preset-choice").Count);
        Assert.DoesNotContain("value=\"guided\"", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "Cancel").Click();
        Assert.True(cancelled);
    }

    [Fact]
    public void Readiness_shows_only_backend_corrective_actions_and_review_state()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var action = "";
        var readiness = new SalesPresentationPresetReadinessViewModel("blocked", false,
            [new("presenter_invalid", "The presenter is unavailable.", ["choose_presenter"], true)], ["choose_presenter"], true);
        var cut = context.RenderComponent<PresentationPresetReadiness>(p => p.Add(x => x.Readiness, readiness).Add(x => x.OnCorrect, value => action = value));
        Assert.Contains("Choose an eligible presenter", cut.Markup);
        Assert.Contains("Review required", cut.Markup);
        cut.Find("button").Click();
        Assert.Equal("choose_presenter", action);
        Assert.DoesNotContain("Publish", cut.Markup);
    }

    [Fact]
    public void Asset_failure_is_safe_and_retry_uses_authoritative_can_retry()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var retries = 0;
        var asset = new SalesPresentationPresetAssetViewModel(Guid.NewGuid(), "review.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", 1024, "hidden-hash", "failed", 1, 2, 0, null, null, null, "render_failed", "Rendering could not finish. Replace the file or retry.", true, DateTime.UtcNow, DateTime.UtcNow, null, 1);
        var version = new SalesPresentationPresetVersionViewModel(Guid.NewGuid(), 2, "draft", null, null, "Goal", "Audience", 30, null, "guided", "en", ["sales_meeting"], null, null, null, DateTime.UtcNow, DateTime.UtcNow, 1, asset);
        var cut = context.RenderComponent<PresentationPresetAssetPanel>(p => p.Add(x => x.Version, version).Add(x => x.OnRetry, () => retries++));
        Assert.Contains("Rendering could not finish", cut.Markup);
        Assert.DoesNotContain("hidden-hash", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent.Contains("Retry processing")).Click();
        Assert.Equal(1, retries);
    }

    [Fact]
    public void Speaker_notes_save_is_scoped_and_shows_audio_update_state()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var company = Guid.NewGuid(); var preset = Guid.NewGuid(); var version = Guid.NewGuid();
        var slide = new SalesPresentationPresetSlideViewModel(Guid.NewGuid(), 1, "Overview", "Source", "Old notes", "", null, 1600, 900, 1600, 900, "hash", "Goal", "Old notes", 60, "Next");
        var handler = new NotesHandler(preset, company);
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(sp => new SalesPresentationPresetApiClient(transport, false, sp.GetRequiredService<IApiProblemMessageResolver>()));
        SalesPresentationPresetViewModel? saved = null;
        var cut = context.RenderComponent<PresentationPresetSpeakerNotes>(p => p.Add(x => x.CompanyId, company)
            .Add(x => x.PresetId, preset).Add(x => x.VersionId, version).Add(x => x.ExpectedVersion, 7)
            .Add(x => x.Slide, slide).Add(x => x.Editable, true).Add(x => x.OnSaved, value => saved = value));
        Assert.Equal(cut.Find("textarea").Id, cut.Find("label").GetAttribute("for"));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
        cut.Find("textarea").Input("Updated notes");
        Assert.Contains("Unsaved changes", cut.Markup);
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.NotNull(saved));
        Assert.Equal($"/api/sales/presentation-presets/{preset}/versions/{version}/slides/{slide.Id}/speaker-notes", handler.Path);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Contains("Updated notes", handler.Body);
        Assert.Contains("\"expectedVersion\":7", handler.Body);
        cut.SetParametersAndRender(p => p.Add(x => x.Slide, slide with { SpeakerNotes = "Updated notes", AudioNeedsUpdate = true }));
        Assert.Contains("Audio needs update", cut.Markup);
        Assert.Contains("Script &amp; audio", cut.Markup);
        cut.Find("textarea").Input("Keep unsaved text");
        cut.SetParametersAndRender(p => p.Add(x => x.ExpectedVersion, 9));
        Assert.Contains("Keep unsaved text", cut.Markup);
        cut.FindAll("button")[1].Click();
        Assert.DoesNotContain("Unsaved changes", cut.Markup);
        cut.SetParametersAndRender(p => p.Add(x => x.Editable, false));
        Assert.True(cut.Find("textarea").HasAttribute("readonly"));
        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("Create a new draft", cut.Markup);
    }

    [Fact]
    public void Speaker_notes_conflict_preserves_unsaved_input()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var handler = new NotesHandler(Guid.NewGuid(), Guid.NewGuid()) { Conflict = true };
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(sp => new SalesPresentationPresetApiClient(transport, false, sp.GetRequiredService<IApiProblemMessageResolver>()));
        var slide = new SalesPresentationPresetSlideViewModel(Guid.NewGuid(), 1, "Overview", "Source", "Old", "", null, 1, 1, 1, 1, "hash", "Goal", "Old", 60, "Next");
        var cut = context.RenderComponent<PresentationPresetSpeakerNotes>(p => p.Add(x => x.Slide, slide).Add(x => x.Editable, true).Add(x => x.CompanyId, Guid.NewGuid()));
        cut.Find("textarea").Input("Keep this edit");
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Contains("Refresh", cut.Find("[role=alert]").TextContent));
        Assert.Contains("Keep this edit", cut.Markup);
        Assert.Contains("Unsaved changes", cut.Markup);
    }

    [Fact]
    public void Slide_card_shows_authorized_image_and_separate_dialogue_and_notes_tabs()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var transport = new CompanyApiTransport(new HttpClient { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(sp => new SalesPresentationPresetApiClient(transport, true, sp.GetRequiredService<IApiProblemMessageResolver>()));
        var company = Guid.NewGuid(); var preset = Guid.NewGuid(); var version = Guid.NewGuid();
        var slide = new SalesPresentationPresetSlideViewModel(Guid.NewGuid(), 1, "Overview", "Extracted words", "Author notes", "private/first.png", null, 1600, 900, 1600, 900, "hash", "Goal", "Notes", 60, "Next", AudioNeedsUpdate: true);
        var narration = new NarrationRevision(Guid.NewGuid(), null, 1, "en", "marin", "speech", null, "draft", 1, DateTime.UtcNow, null,
            [new(Guid.NewGuid(), 1, 1, "Source", "Actual spoken words", "draft", false, 0, 0, 0, null),
             new(Guid.NewGuid(), 2, 1, "Other", "Other slide dialogue", "draft", false, 0, 0, 0, null)], 0, 0, 0, 0, 0, null);
        var cut = context.RenderComponent<PresentationPresetSlideCard>(p => p.Add(x => x.CompanyId, company).Add(x => x.PresetId, preset)
            .Add(x => x.VersionId, version).Add(x => x.Slide, slide).Add(x => x.Narration, narration).Add(x => x.Editable, true));
        Assert.Equal(SalesPresentationPresetApiClient.SlideImageUrl(company, preset, version, slide.Id), cut.Find("img").GetAttribute("src"));
        Assert.DoesNotContain("private/first.png", cut.Markup);
        Assert.Contains("Actual spoken words", cut.Find(".dialogue-panel").TextContent);
        Assert.DoesNotContain("Extracted words", cut.Find(".dialogue-panel").TextContent);
        Assert.DoesNotContain("Other slide dialogue", cut.Markup);
        Assert.True(cut.Find(".notes-panel").HasAttribute("hidden"));
        cut.FindAll("[role=tab]")[1].Click();
        Assert.False(cut.Find(".notes-panel").HasAttribute("hidden"));
        cut.Find("textarea").Input("Keep this unsaved note");
        cut.FindAll("[role=tab]")[0].Click();
        cut.FindAll("[role=tab]")[1].Click();
        Assert.Contains("Keep this unsaved note", cut.Markup);
        Assert.Contains("Audio needs update", cut.Markup);
        cut.Find("img").TriggerEvent("onerror", EventArgs.Empty);
        Assert.Contains("Slide image unavailable", cut.Markup);
        cut.Find(".slide-image button").Click();
        Assert.Single(cut.FindAll("img"));
        cut.SetParametersAndRender(p => p.Add(x => x.Editable, false).Add(x => x.Narration, (NarrationRevision?)null));
        Assert.True(cut.Find("textarea").HasAttribute("readonly"));
        Assert.Contains("No spoken dialogue prepared", cut.Markup);
    }

    private sealed class NotesHandler(Guid preset, Guid company) : HttpMessageHandler
    {
        public bool Conflict { get; init; }
        public string? Path, Body;
        public HttpMethod? Method;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath; Method = request.Method;
            Body = await request.Content!.ReadAsStringAsync(ct);
            if (Conflict) return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { detail = "Refresh the preset and try again." }) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new SalesPresentationPresetViewModel(preset, company,
                "Demo", null, Guid.NewGuid(), "draft", null, DateTime.UtcNow, DateTime.UtcNow, null, 1, 0, null, null, [], [])) };
        }
    }

    private sealed class AdHocHandler(Guid presenterId, Guid presetId, Guid versionId) : HttpMessageHandler
    {
        public string? RuntimeStrategy { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/context-options"))
                return Json(new SalesPresentationAdHocOptionsViewModel([], [], [], [], [new(presenterId, "Avery")]));
            if (request.RequestUri.AbsolutePath.EndsWith("/slides"))
                return Json(new[] { new SalesPresentationPresetSlideViewModel(Guid.NewGuid(), 1, "Overview", "Approved content", null, "slide.png", null, 1600, 900, 1600, 900, new string('b', 64), "Introduce", "Use approved facts", 60, "Next") });
            var body = await request.Content!.ReadFromJsonAsync<CreateAdHocSalesPresentationRequest>(cancellationToken: ct);
            RuntimeStrategy = body!.RuntimeStrategy;
            return Json(new SalesPresentationAdHocRunViewModel(Guid.NewGuid(), presetId, "Executive demo", versionId, 2, presenterId, "Avery", "Explain the product", "Finance leaders", 30, null, "en", "assisted", "preparation_only", null, null, null, null, "needs_review", ["customer_context_missing"], false, null, 1,
                [new(Guid.NewGuid(), "missing_evidence", "No customer context was selected.", "needs_review", null, null, 0, null)]));
        }
        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
