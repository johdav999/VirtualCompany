using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class GuidedWorkSurfaceTests
{
    [Fact]
    public void Workspace_is_deep_linkable_accessible_responsive_and_keeps_text_fallback()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");
        var css = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor.css");
        var script = Read("src", "VirtualCompany.Web", "wwwroot", "js", "guided-realtime.js");
        var draft = Read("src", "VirtualCompany.Web", "Components", "GuidedWork", "GuidedDraftPanel.razor");
        var draftCss = Read("src", "VirtualCompany.Web", "Components", "GuidedWork", "GuidedDraftPanel.razor.css");

        Assert.Contains("@page \"/agents/{AgentId:guid}/workshops/{ArtifactType}\"", page, StringComparison.Ordinal);
        Assert.Contains("sessionId", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-live", page, StringComparison.Ordinal);
        Assert.Contains("VoicePrivacy", page, StringComparison.Ordinal);
        Assert.Contains("GuidedConversationPanel", page, StringComparison.Ordinal);
        Assert.Contains("GuidedDraftPanel", page, StringComparison.Ordinal);
        Assert.Contains("guided-field__summary", draft, StringComparison.Ordinal);
        Assert.Contains("aria-expanded", draft, StringComparison.Ordinal);
        Assert.Contains("expandedPaths", draft, StringComparison.Ordinal);
        Assert.Contains("ToggleExpanded", draft, StringComparison.Ordinal);
        Assert.Contains("guided-field__full-value", draft, StringComparison.Ordinal);
        Assert.Contains("DocumentedDetail", draft, StringComparison.Ordinal);
        Assert.Contains("-webkit-line-clamp:2", draftCss, StringComparison.Ordinal);
        Assert.Contains("guided-field--expanded", draftCss, StringComparison.Ordinal);
        Assert.Contains("WorkshopInsights", draft, StringComparison.Ordinal);
        Assert.Contains("NotYetMapped", draft, StringComparison.Ordinal);
        Assert.Contains("guided-field--insight", draftCss, StringComparison.Ordinal);
        Assert.Contains("@media", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns", css, StringComparison.Ordinal);
        Assert.Contains("getUserMedia", script, StringComparison.Ordinal);
        Assert.Contains("guided-realtime.js?v=", page, StringComparison.Ordinal);
        Assert.Contains("export async function setMuted", script, StringComparison.Ordinal);
        Assert.Contains("export async function interrupt", script, StringComparison.Ordinal);
        Assert.Contains("response.cancel", script, StringComparison.Ordinal);
        Assert.Contains("reconnecting", script, StringComparison.Ordinal);
        Assert.Contains("const starts = new Map()", script, StringComparison.Ordinal);
        Assert.Contains("reconnectScheduled", script, StringComparison.Ordinal);
        Assert.Contains("response.status === 429", script, StringComparison.Ordinal);
        Assert.Contains("readProblemDetail", script, StringComparison.Ordinal);
        Assert.Contains("pagehide", script, StringComparison.Ordinal);
        Assert.Contains("notifyServerStop", script, StringComparison.Ordinal);
        Assert.Contains("keepalive", script, StringComparison.Ordinal);
        Assert.Contains("track.stop()", script, StringComparison.Ordinal);
        Assert.Contains("current.pc?.close()", script, StringComparison.Ordinal);
        Assert.Contains("permission denied by system", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("microphone access is blocked by Windows", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("throw new Error(failure.message)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("throw error", script, StringComparison.Ordinal);
        Assert.Contains("remote_track_received", script, StringComparison.Ordinal);
        Assert.Contains("remote_audio_playing", script, StringComparison.Ordinal);
        Assert.Contains("response_audio_started", script, StringComparison.Ordinal);
        Assert.Contains("provider_error", script, StringComparison.Ordinal);
        Assert.Contains("outputAudioActive", script, StringComparison.Ordinal);
        Assert.Contains("ensureRemoteAudioPlayback", script, StringComparison.Ordinal);
        Assert.Contains("response_interrupt_ignored", script, StringComparison.Ordinal);
        Assert.Contains("response_id: current.responseId", script, StringComparison.Ordinal);
        Assert.Contains("speech_detected_during_agent_audio", script, StringComparison.Ordinal);
        Assert.Contains("turnEndToResponseCreatedMs", script, StringComparison.Ordinal);
        Assert.Contains("turnEndToAudioMs", script, StringComparison.Ordinal);
        Assert.Contains("transcriptionDelayMs", script, StringComparison.Ordinal);
        Assert.Contains("response.output_audio_transcript.delta", script, StringComparison.Ordinal);
        Assert.Contains("response.output_audio_transcript.done", script, StringComparison.Ordinal);
        Assert.Contains("OnAgentVoiceTranscript", script, StringComparison.Ordinal);
        Assert.Contains("agentTranscriptUpdateTimer", script, StringComparison.Ordinal);
        Assert.Contains("scrollTranscriptToEnd", script, StringComparison.Ordinal);
        Assert.Contains("LiveAgentTranscript", page, StringComparison.Ordinal);
        Assert.Contains("LiveAgentTranscript=\"@liveAgentTranscript\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveAgentTranscript=\"liveAgentTranscript\"", page, StringComparison.Ordinal);
        Assert.Contains("OnAgentVoiceTranscript", page, StringComparison.Ordinal);
        Assert.Contains("do_not_substitute_model_knowledge", Read("src", "VirtualCompany.Infrastructure.Operations", "Companies", "GuidedRealtimeServices.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("!state.audio.paused) await interrupt", script, StringComparison.Ordinal);
        Assert.DoesNotContain("current.audio.pause()", script, StringComparison.Ordinal);
        Assert.Contains("MapGuidedRealtimeProxyEndpoints", Read("src", "VirtualCompany.Web", "Program.cs"), StringComparison.Ordinal);
        var proxy = Read("src", "VirtualCompany.Web", "Services", "GuidedRealtimeProxyEndpoints.cs");
        Assert.Contains("/voice/calls", proxy, StringComparison.Ordinal);
        Assert.Contains("GuidedWorkApiClient client", proxy, StringComparison.Ordinal);
        Assert.Contains("Retry-After", proxy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guided_voice_client_forwards_bounded_sdp_through_company_scoped_transport()
    {
        var companyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var transport = new VoiceRecordingTransport();
        var client = new GuidedWorkApiClient(transport, offline: false);

        var response = await client.StartVoiceCallAsync(companyId, sessionId, "v=0\r\n");

        Assert.Equal(companyId, transport.CompanyId);
        Assert.Equal(HttpMethod.Post, transport.Method);
        Assert.Equal($"api/companies/{companyId}/guided-work-sessions/{sessionId}/voice/calls", transport.Uri);
        Assert.Equal("application/sdp; charset=utf-8", transport.ContentType);
        Assert.Equal("v=0\r\n", transport.Body);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("binding-1", response.BindingId);
        Assert.Equal("answer-sdp", response.Body);
    }

    [Theory]
    [InlineData("Marketing/MarketingDashboard.razor", "marketing_strategy")]
    [InlineData("Marketing/MarketingDashboard.razor", "marketing_segment")]
    [InlineData("Finance/FinancePage.razor", "finance_budget")]
    [InlineData("Sales/SalesCampaigns.razor", "sales_campaign_plan")]
    [InlineData("Support/SupportSlaSettings.razor", "support_sla_policy")]
    public void Department_surfaces_expose_real_start_or_resume_control(string page, string artifactType)
    {
        var source = Read(["src", "VirtualCompany.Web", "Pages", .. page.Split('/')]);
        Assert.True(source.Contains($"ArtifactType=\"{artifactType}\"", StringComparison.Ordinal) ||
                    source.Contains($"/workshops/{artifactType}", StringComparison.Ordinal),
            $"{page} must expose the {artifactType} workshop route.");
    }

    [Fact]
    public void Launch_component_resolves_active_agent_and_resumes_matching_target()
    {
        var source = Read("src", "VirtualCompany.Web", "Components", "GuidedWork", "GuidedWorkshopLaunch.razor");
        Assert.Contains("GetRosterAsync", source, StringComparison.Ordinal);
        Assert.Contains("ListAsync", source, StringComparison.Ordinal);
        Assert.Contains("x.TargetArtifactId == TargetArtifactId", source, StringComparison.Ordinal);
        Assert.Contains("x.Status == \"active\" || x.Status == \"review_ready\"", source, StringComparison.Ordinal);
        Assert.Contains("sessionId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Marketing_workshops_expose_the_shared_document_upload_surface()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");
        Assert.Contains("session.Capabilities.SupportsDocumentAttachments", source, StringComparison.Ordinal);
        Assert.DoesNotContain("artifactType is \"marketing_strategy\" or \"marketing_segment\"", source, StringComparison.Ordinal);
        Assert.Contains("UploadDocumentAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Onboarding_offers_the_guided_company_workshop_and_preserves_the_form_flow()
    {
        var page=Read("src","VirtualCompany.Web","Pages","Onboarding.razor");
        var client=Read("src","VirtualCompany.Web","Services","OnboardingApiClient.cs");
        Assert.Contains("OnboardingWorkshopTitle",page,StringComparison.Ordinal);
        Assert.Contains("StartWorkshopAsync",page,StringComparison.Ordinal);
        Assert.Contains("PersistProgressAsync",page,StringComparison.Ordinal);
        Assert.Contains("OnboardingWorkshopResume",page,StringComparison.Ordinal);
        Assert.Contains("OnboardingSaveChanges",page,StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(result.Route, forceLoad: true)",page,StringComparison.Ordinal);
        Assert.DoesNotContain("@if (!showCompletedState)\r\n                    {\r\n                        <div class=\"alert alert-light",page,StringComparison.Ordinal);
        Assert.Contains("api/onboarding/workshop",client,StringComparison.Ordinal);
    }

    [Fact]
    public void Workshop_documents_panel_can_be_collapsed_accessibly()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");

        Assert.Contains("documentsCollapsed = !documentsCollapsed", source, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"@(!documentsCollapsed)\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"guided-documents-content\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"guided-documents-content\"", source, StringComparison.Ordinal);
        Assert.Contains("Text[documentsCollapsed ? \"ExpandDocuments\" : \"CollapseDocuments\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Guided_workshop_rejects_a_stale_session_from_another_artifact_route()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");

        Assert.Contains("!session.ArtifactType.Equals(ArtifactType,StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("Api.StartAsync(companyId,AgentId,ArtifactType,TargetArtifactId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Marketing_segments_surface_preserves_unfinished_workshop_drafts_and_links_saved_results()
    {
        var marketing = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");
        var workshop = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");

        Assert.Contains("GuidedWorkApi.ListAsync(companyId, artifactType: \"marketing_segment\")", marketing, StringComparison.Ordinal);
        Assert.Contains("UNSAVED WORKSHOP DRAFT", marketing, StringComparison.Ordinal);
        Assert.Contains("Resume and finish", marketing, StringComparison.Ordinal);
        Assert.Contains("section=Segments", workshop, StringComparison.Ordinal);
        Assert.Contains("Open saved result", workshop, StringComparison.Ordinal);
        Assert.Contains("DraftChangeCount()", workshop, StringComparison.Ordinal);
        Assert.Contains("validationDetails", Read("src", "VirtualCompany.Web", "Services", "GuidedWorkApiClient.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Guided_workshop_send_button_has_a_visible_text_label()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");
        Assert.Contains("<span>@Text[\"Send\"]</span>", source, StringComparison.Ordinal);
        Assert.Contains("@bind:event=\"oninput\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Interactive_pages_expose_connection_loss_and_a_browser_side_reload_action()
    {
        var app = Read("src", "VirtualCompany.Web", "App.razor");
        var css = Read("src", "VirtualCompany.Web", "wwwroot", "css", "app.css");

        Assert.Contains("id=\"components-reconnect-modal\"", app, StringComparison.Ordinal);
        Assert.Contains("components-reconnect-current-attempt", app, StringComparison.Ordinal);
        Assert.Contains("onclick=\"window.location.reload()\"", app, StringComparison.Ordinal);
        Assert.Contains("CommonText[\"ReconnectFailedHelp\"]", app, StringComparison.Ordinal);
        Assert.Contains("components-reconnect-rejected", css, StringComparison.Ordinal);
        Assert.Contains("pointer-events: auto", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_direct_field_confirmation_keeps_the_editor_open_and_surfaces_the_error()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");
        var draft = Read("src", "VirtualCompany.Web", "Components", "GuidedWork", "GuidedDraftPanel.razor");

        Assert.Contains("catch(Exception ex){actionError=ex.Message;throw;}", page, StringComparison.Ordinal);
        const string saveCall = "await OnSave.InvokeAsync((field, ParseEditedValue(field)));";
        Assert.Contains(saveCall, draft, StringComparison.Ordinal);
        Assert.Contains("StopEditing();", draft, StringComparison.Ordinal);
        Assert.True(
            draft.IndexOf(saveCall, StringComparison.Ordinal) <
            draft.IndexOf("StopEditing();", draft.IndexOf(saveCall, StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Fact]
    public void Guided_voice_turn_is_shown_while_the_checkpoint_is_processing()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");
        var conversation = Read("src", "VirtualCompany.Web", "Components", "GuidedWork", "GuidedConversationPanel.razor");
        var realtime = Read("src", "VirtualCompany.Web", "wwwroot", "js", "guided-realtime.js");
        Assert.Contains("liveUserTranscript=transcript.Trim()", page, StringComparison.Ordinal);
        Assert.Contains("LiveUserTranscript=\"@liveUserTranscript\"", page, StringComparison.Ordinal);
        Assert.Contains("guided-message--live-user", conversation, StringComparison.Ordinal);
        Assert.Contains("provider_event_handling_failed", realtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Guided_voice_uses_one_durable_agent_response_and_suspends_queued_input()
    {
        var page=Read("src","VirtualCompany.Web","Pages","GuidedWorkSession.razor");
        var script=Read("src","VirtualCompany.Web","wwwroot","js","guided-realtime.js");
        Assert.Contains("RecordVoiceAgentMessageAsync",page,StringComparison.Ordinal);
        Assert.Contains("setInputSuspended(state, true, \"response.created\")",script,StringComparison.Ordinal);
        Assert.Contains("setInputSuspended(state, false, \"response.done\")",script,StringComparison.Ordinal);
        Assert.Contains("setInputSuspended(current, false, \"explicit_interrupt\")",script,StringComparison.Ordinal);
        Assert.Contains("item?.type === \"function_call\"",script,StringComparison.Ordinal);
        Assert.Contains("if (!state.toolContinuationPending)",script,StringComparison.Ordinal);
        Assert.Contains("await flushAgentTranscript(state, false)",script,StringComparison.Ordinal);
        Assert.Contains("if (hasFunctionCall) resetAgentTranscript(state)",script,StringComparison.Ordinal);
        Assert.Contains("else await flushAgentTranscript(state, true)",script,StringComparison.Ordinal);
    }

    [Fact]
    public void Guided_voice_tool_work_is_silent_and_shows_progress()
    {
        var page=Read("src","VirtualCompany.Web","Pages","GuidedWorkSession.razor");
        var conversation=Read("src","VirtualCompany.Web","Components","GuidedWork","GuidedConversationPanel.razor");
        var script=Read("src","VirtualCompany.Web","wwwroot","js","guided-realtime.js");
        Assert.Contains("response.function_call_arguments.done",script,StringComparison.Ordinal);
        Assert.Contains("output_audio_buffer.clear",script,StringComparison.Ordinal);
        Assert.Contains("state.audio.muted = state.toolWorkActive",script,StringComparison.Ordinal);
        Assert.Contains("OnVoiceWorkState",script,StringComparison.Ordinal);
        Assert.Contains("OnVoiceWorkState",page,StringComparison.Ordinal);
        Assert.Contains("guided-message--work",conversation,StringComparison.Ordinal);
        Assert.Contains("!ShowVoiceWork",conversation,StringComparison.Ordinal);
    }

    [Fact]
    public void Guided_text_turn_shows_non_conversational_work_progress()
    {
        var page=Read("src","VirtualCompany.Web","Pages","GuidedWorkSession.razor");
        var program=Read("src","VirtualCompany.Web","Program.cs");
        Assert.Contains("TextWorkTitle",page,StringComparison.Ordinal);
        Assert.Contains("TextWorkDetail",page,StringComparison.Ordinal);
        Assert.Contains("await InvokeAsync(StateHasChanged)",page,StringComparison.Ordinal);
        Assert.Contains("finally{ClearVoiceWork();}",page,StringComparison.Ordinal);
        Assert.Contains("Timeout = TimeSpan.FromMinutes(3)",program,StringComparison.Ordinal);
    }

    [Fact]
    public void Guided_workshop_refreshes_background_follow_ups_without_blocking_the_composer()
    {
        var page=Read("src","VirtualCompany.Web","Pages","GuidedWorkSession.razor");
        Assert.Contains("new PeriodicTimer(TimeSpan.FromSeconds(5))",page,StringComparison.Ordinal);
        Assert.Contains("RefreshSessionAsync(refreshCancellation.Token)",page,StringComparison.Ordinal);
        Assert.Contains("latest.Version==session.Version",page,StringComparison.Ordinal);
        Assert.Contains("await refreshCancellation.CancelAsync()",page,StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(busy || string.IsNullOrWhiteSpace(message))\"",page,StringComparison.Ordinal);
        Assert.Contains("ResearchInProgress=\"@ResearchInProgress\"",page,StringComparison.Ordinal);
        Assert.Contains("ResearchInProgress ? Text[\"VoiceWorkResearchTitle\"]",Read("src","VirtualCompany.Web","Components","GuidedWork","GuidedConversationPanel.razor"),StringComparison.Ordinal);
    }

    [Fact]
    public void Guided_workshop_panels_have_an_accessible_persistent_resizer()
    {
        var page=Read("src","VirtualCompany.Web","Pages","GuidedWorkSession.razor");
        var css=Read("src","VirtualCompany.Web","Pages","GuidedWorkSession.razor.css");
        var script=Read("src","VirtualCompany.Web","wwwroot","js","guided-workspace-resize.js");
        Assert.Contains("guided-workspace__resizer",page,StringComparison.Ordinal);
        Assert.Contains("role=\"separator\"",page,StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"",page,StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:minmax(340px,var(--conversation-width)) 11px minmax(460px,1fr)",css,StringComparison.Ordinal);
        Assert.Contains("@media(max-width:900px)",css,StringComparison.Ordinal);
        Assert.Contains("ArrowLeft",script,StringComparison.Ordinal);
        Assert.Contains("ArrowRight",script,StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem",script,StringComparison.Ordinal);
        Assert.Contains("setPointerCapture",script,StringComparison.Ordinal);
    }

    [Fact]
    public void Live_draft_has_direct_and_batch_status_review_actions_with_separate_provenance()
    {
        var draft=Read("src","VirtualCompany.Web","Components","GuidedWork","GuidedDraftPanel.razor");
        var page=Read("src","VirtualCompany.Web","Pages","GuidedWorkSession.razor");
        var client=Read("src","VirtualCompany.Web","Services","GuidedWorkApiClient.cs");

        Assert.Contains("ReviewProposals",draft,StringComparison.Ordinal);
        Assert.Contains("ConfirmSelectedAsync",draft,StringComparison.Ordinal);
        Assert.Contains("EditAndConfirm",draft,StringComparison.Ordinal);
        Assert.Contains("NeedsWork",draft,StringComparison.Ordinal);
        Assert.Contains("MarkUnknown",draft,StringComparison.Ordinal);
        Assert.Contains("SourceLabel",draft,StringComparison.Ordinal);
        Assert.Contains("OnStatusChange",draft,StringComparison.Ordinal);
        Assert.Contains("ChangeFieldStatusesAsync",page,StringComparison.Ordinal);
        Assert.Contains("fields/status",client,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guided_client_preserves_company_scope_route_version_and_client_request_id()
    {
        var companyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var transport = new RecordingTransport(new GuidedWorkTurnResultViewModel());
        var client = new GuidedWorkApiClient(transport, offline: false);

        await client.TurnAsync(companyId, sessionId, "A bounded answer", 7, clientRequestId: requestId);

        Assert.Equal(companyId, transport.CompanyId);
        Assert.Equal(HttpMethod.Post, transport.Method);
        Assert.Equal($"api/companies/{companyId}/guided-work-sessions/{sessionId}/turns", transport.Uri);
        Assert.Contains($"\"clientRequestId\":\"{requestId:D}\"", transport.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"expectedVersion\":7", transport.Body, StringComparison.Ordinal);
        Assert.Contains("A bounded answer", transport.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guided_client_maps_empty_backend_failure_to_plain_safe_message()
    {
        var client = new GuidedWorkApiClient(new EmptyFailureTransport(), offline: false);

        var error = await Assert.ThrowsAsync<OnboardingApiException>(() => client.GetAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains("workshop service is unavailable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task Guided_client_preserves_conflict_status_for_bounded_session_reconciliation()
    {
        var client = new GuidedWorkApiClient(new ConflictFailureTransport(), offline: false);

        var error = await Assert.ThrowsAsync<OnboardingApiException>(() => client.GetAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal((int)HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("The session changed. Refresh it and try again.", error.Message);
    }

    [Fact]
    public void Guided_workshop_reconciles_a_stale_version_once_with_the_same_request_id()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "GuidedWorkSession.razor");

        Assert.Contains("ExecuteWithVersionReconciliationAsync", source, StringComparison.Ordinal);
        Assert.Contains("ex.StatusCode==StatusCodes.Status409Conflict", source, StringComparison.Ordinal);
        Assert.Contains("session=await Api.GetAsync(companyId,session.Id)", source, StringComparison.Ordinal);
        Assert.Contains("clientRequestId:clientRequestId", source, StringComparison.Ordinal);
        Assert.Contains("Api.CorrectAsync(companyId,session.Id", source, StringComparison.Ordinal);
        Assert.Contains("Api.ChangeStatusesAsync(companyId,session.Id", source, StringComparison.Ordinal);
        Assert.Contains("Api.ReviewAsync(companyId,session.Id", source, StringComparison.Ordinal);
        Assert.Contains("Api.CancelAsync(companyId,session.Id", source, StringComparison.Ordinal);
    }

    private sealed class RecordingTransport(object response) : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("http://localhost/");
        public Guid CompanyId { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string Uri { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        public async Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken)
        {
            CompanyId = companyId;
            Method = method;
            Uri = uri;
            Body = content is null ? string.Empty : await content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
        }
    }

    private sealed class EmptyFailureTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("http://localhost/");
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(string.Empty) });
    }

    private sealed class ConflictFailureTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("http://localhost/");
        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new
                {
                    title = "Guided session conflict",
                    detail = "The session changed. Refresh it and try again.",
                    status = 409
                })
            });
    }

    private sealed class VoiceRecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("http://localhost/");
        public Guid CompanyId { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string Uri { get; private set; } = string.Empty;
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = string.Empty;

        public async Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken)
        {
            CompanyId = companyId;
            Method = method;
            Uri = uri;
            ContentType = content?.Headers.ContentType?.ToString();
            Body = content is null ? string.Empty : await content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("answer-sdp") };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/sdp");
            response.Headers.TryAddWithoutValidation("X-Guided-Voice-Binding", "binding-1");
            return response;
        }
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
