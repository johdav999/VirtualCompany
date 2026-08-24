using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Chat;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class GuidedWorkSessionIntegrationTests : IDisposable
{
    private readonly DeterministicCheckpointProvider _provider = new();
    private readonly GuidedFactory _factory;

    public GuidedWorkSessionIntegrationTests() => _factory = new GuidedFactory(_provider);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Agent_brief_lifecycle_is_durable_idempotent_audited_and_has_no_unrelated_side_effects()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);

        var startResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions",
            new StartGuidedWorkSessionCommand(GuidedArtifactTypes.AgentOperatingBrief, seed.AgentId));
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<GuidedWorkSessionDto>();
        Assert.NotNull(started);
        Assert.Equal(GuidedWorkSessionStatuses.Active, started!.Status);
        Assert.Equal(seed.AgentId, started.TargetArtifactId);

        var repeatedStartResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions",
            new StartGuidedWorkSessionCommand(GuidedArtifactTypes.AgentOperatingBrief, seed.AgentId));
        var repeatedStart = await repeatedStartResponse.Content.ReadFromJsonAsync<GuidedWorkSessionDto>();
        Assert.Equal(HttpStatusCode.OK, repeatedStartResponse.StatusCode);
        Assert.Equal(started.Id, repeatedStart!.Id);

        var turnRequestId = Guid.NewGuid();
        var turnCommand = new AddGuidedWorkTurnCommand("Use these confirmed operating instructions.", turnRequestId, started.Version);
        var turnResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns", turnCommand);
        Assert.Equal(HttpStatusCode.OK, turnResponse.StatusCode);
        var turn = await turnResponse.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>();
        Assert.NotNull(turn);
        Assert.Equal(4, turn!.Changes.Count);
        Assert.Equal(4, turn.Session.ReadyFieldCount);
        Assert.All(turn.Changes, change => Assert.Equal(GuidedDraftFieldStatuses.Confirmed, change.Status));

        var repeatedTurnResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns", turnCommand);
        Assert.Equal(HttpStatusCode.OK, repeatedTurnResponse.StatusCode);
        var repeatedTurn = await repeatedTurnResponse.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>();
        Assert.Equal(turn.UserMessage.Id, repeatedTurn!.UserMessage.Id);
        Assert.Equal(turn.AgentMessage.Id, repeatedTurn.AgentMessage.Id);
        Assert.Equal(1, _provider.CallCount);

        var reviewRequestId = Guid.NewGuid();
        var reviewResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/review",
            new PrepareGuidedWorkReviewCommand(reviewRequestId, turn.Session.Version));
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var review = await reviewResponse.Content.ReadFromJsonAsync<GuidedWorkReviewDto>();
        Assert.NotNull(review);
        Assert.NotEmpty(review!.ReviewToken);
        Assert.Empty(review.MissingFields);
        Assert.Empty(review.Conflicts);

        var commitRequestId = Guid.NewGuid();
        var commitCommand = new ConfirmGuidedWorkCommitCommand(review.ReviewToken, commitRequestId, review.Session.Version);
        var commitResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/commit", commitCommand);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        var committed = await commitResponse.Content.ReadFromJsonAsync<GuidedWorkCommitResultDto>();
        Assert.NotNull(committed);
        Assert.Equal(GuidedWorkSessionStatuses.Completed, committed!.Session.Status);

        var repeatedCommitResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/commit", commitCommand);
        Assert.Equal(HttpStatusCode.OK, repeatedCommitResponse.StatusCode);
        var repeatedCommit = await repeatedCommitResponse.Content.ReadFromJsonAsync<GuidedWorkCommitResultDto>();
        Assert.Equal(committed.ArtifactVersion, repeatedCommit!.ArtifactVersion);

        await _factory.ExecuteDbContextAsync(async db =>
        {
            var agent = await db.Agents.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.AgentId);
            Assert.Equal(seed.AutonomyLevel, agent.AutonomyLevel);
            Assert.Equal(seed.Status, agent.Status);
            Assert.True(JsonNode.DeepEquals(seed.Tools, JsonObject.Create(System.Text.Json.JsonSerializer.SerializeToElement(agent.Tools))));
            Assert.True(JsonNode.DeepEquals(seed.Scopes, JsonObject.Create(System.Text.Json.JsonSerializer.SerializeToElement(agent.Scopes))));
            Assert.True(agent.CommunicationProfile.TryGetValue("briefing", out var briefing));
            Assert.NotNull(briefing);

            Assert.Equal(2, await db.Messages.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.ConversationId == started.ConversationId));
            Assert.Equal(1, await db.GuidedWorkSessions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.Id == started.Id));
            Assert.Equal(3, await db.GuidedSessionOperations.IgnoreQueryFilters().CountAsync(x => x.SessionId == started.Id));
            var actions = await db.AuditEvents.IgnoreQueryFilters().Where(x => x.TargetId == started.Id.ToString("N")).Select(x => x.Action).ToListAsync();
            Assert.Contains("guided_session.started", actions);
            Assert.Contains("guided_session.turn_recorded", actions);
            Assert.Contains("guided_session.review_prepared", actions);
            Assert.Contains("guided_session.committed", actions);
        });
    }

    [Fact]
    public async Task Direct_field_confirmation_is_durable_idempotent_and_audited()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);
        var started = await StartAsync(client, seed);
        var field = started.Fields.First(x => x.Path == AgentBriefingCategories.CompanyInformation);
        var requestId = Guid.NewGuid();
        var command = new CorrectGuidedDraftFieldCommand(
            JsonValue.Create("A confirmed description supplied directly by the user."),
            GuidedDraftFieldStatuses.Confirmed,
            requestId,
            started.Version);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/fields/{Uri.EscapeDataString(field.Path)}",
            command);
        response.EnsureSuccessStatusCode();
        var corrected = await response.Content.ReadFromJsonAsync<GuidedWorkSessionDto>();
        Assert.NotNull(corrected);
        var correctedField = corrected!.Fields.Single(x => x.Path == field.Path);
        Assert.Equal(GuidedDraftFieldStatuses.Confirmed, correctedField.Status);
        Assert.Equal("user", correctedField.SourceType);
        Assert.Equal("A confirmed description supplied directly by the user.", correctedField.Value!.GetValue<string>());

        var repeatedResponse = await client.PutAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/fields/{Uri.EscapeDataString(field.Path)}",
            command);
        repeatedResponse.EnsureSuccessStatusCode();
        var repeated = await repeatedResponse.Content.ReadFromJsonAsync<GuidedWorkSessionDto>();
        Assert.Equal(corrected.Version, repeated!.Version);

        await _factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(1, await db.GuidedSessionOperations.IgnoreQueryFilters().CountAsync(x =>
                x.SessionId == started.Id && x.ClientRequestId == requestId && x.OperationType == "correct"));
            Assert.Contains("guided_session.field_corrected", await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.TargetId == started.Id.ToString("N"))
                .Select(x => x.Action)
                .ToListAsync());
        });
    }

    [Fact]
    public async Task Checkpoint_combines_a_value_patch_with_an_explicit_status_change_for_the_same_field()
    {
        var provider = new DeterministicCheckpointProvider
        {
            ResultFactory = request =>
            {
                var field = request.Fields.First(x => x.IsRequired);
                return new GuidedCheckpointResult(
                    "I recorded the market strategy and its requested review status.",
                    [new GuidedPatchOperation(field.Path, JsonValue.Create("Focus on price-sensitive Scandinavian SMEs."),
                        GuidedDraftFieldStatuses.Proposed, "user", "The user supplied the strategy direction.")],
                    [], [], [], [], "The strategy direction is ready for review.", null, false, null,
                    [new GuidedFieldStatusChangeOperation(field.Path, GuidedDraftFieldStatuses.Confirmed,
                        "Explicitly confirmed by the user.")]);
            }
        };
        using var factory = new GuidedFactory(provider);
        var seed = await SeedAsync(factory);
        using var client = CreateClient(factory, seed.Subject, seed.Email);
        var started = await StartAsync(client, seed);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",
            new AddGuidedWorkTurnCommand("Use this strategy direction and confirm it.", Guid.NewGuid(), started.Version));

        response.EnsureSuccessStatusCode();
        var turn = await response.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>();
        Assert.NotNull(turn);
        var updated = turn!.Session.Fields.Single(x => x.Path == turn.Changes.Single().Path);
        Assert.Single(turn.Changes);
        Assert.Equal(GuidedDraftFieldStatuses.Confirmed, updated.Status);
        Assert.Equal("user", updated.SourceType);
        Assert.Contains("Explicitly confirmed by the user.", updated.Explanation);
    }

    [Fact]
    public async Task Finalized_voice_turn_stores_bounded_sanitized_metadata_without_provider_identity()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);
        var started = await StartAsync(client, seed);
        const string providerEvent = "provider-event-sensitive-123";
        var command = new AddGuidedWorkTurnCommand("Finalized voice answer.", Guid.NewGuid(), started.Version,
            "voice", providerEvent, true, 4321, "realtime-webrtc-v1");

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns", command);
        response.EnsureSuccessStatusCode();
        var turn = await response.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>();

        Assert.NotNull(turn);
        Assert.DoesNotContain(turn!.Session.Messages, x => x.SenderType == ChatSenderTypes.Agent);

        await _factory.ExecuteDbContextAsync(async db =>
        {
            var message = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.ConversationId == started.ConversationId && x.SenderType == ChatSenderTypes.User);
            Assert.Equal("voice", message.StructuredPayload["modality"]!.GetValue<string>());
            Assert.True(message.StructuredPayload["interrupted"]!.GetValue<bool>());
            Assert.True(message.StructuredPayload["final"]!.GetValue<bool>());
            Assert.Equal(4321, message.StructuredPayload["duration_ms"]!.GetValue<int>());
            Assert.Equal("realtime-webrtc-v1", message.StructuredPayload["transport_version"]!.GetValue<string>());
            Assert.DoesNotContain(providerEvent, System.Text.Json.JsonSerializer.Serialize(message.StructuredPayload), StringComparison.Ordinal);
            Assert.Equal(64, message.StructuredPayload["provider_event_hash"]!.GetValue<string>().Length);
            Assert.Contains(await db.Messages.IgnoreQueryFilters().Where(x => x.ConversationId == started.ConversationId).ToListAsync(),
                x => x.SenderType == ChatSenderTypes.Agent && x.StructuredPayload["guided_message_kind"]!.GetValue<string>() == "guided_checkpoint_internal");
        });
    }

    [Fact]
    public async Task Checkpoint_receives_bounded_recent_dialogue_to_preserve_documentation_detail()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);
        var started = await StartAsync(client, seed);
        const string earlierDetail = "The owner needs weekly finance analysis with examples and decision context.";
        const string currentTurn = "Also document why those weekly updates reduce delayed decisions.";

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",
            new AddGuidedWorkTurnCommand(earlierDetail, Guid.NewGuid(), started.Version));
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>();

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",
            new AddGuidedWorkTurnCommand(currentTurn, Guid.NewGuid(), first!.Session.Version));
        secondResponse.EnsureSuccessStatusCode();

        var request = Assert.IsType<GuidedCheckpointRequest>(_provider.LastRequest);
        Assert.Equal("No additional company material is available for this workshop.", request.CompanyReferenceContext);
        Assert.Contains(request.RecentConversation, x => x.SenderType == ChatSenderTypes.User && x.Body == earlierDetail);
        Assert.Contains(request.RecentConversation, x => x.SenderType == ChatSenderTypes.Agent);
        Assert.DoesNotContain(request.RecentConversation, x => x.Body == currentTurn);
        Assert.True(request.RecentConversation.Sum(x => x.Body.Length) <= 12000);
    }

    [Fact]
    public async Task Text_research_acknowledges_immediately_then_continues_through_the_durable_outbox()
    {
        var provider=new DeterministicCheckpointProvider{ResearchQueryNext="Current evidence about Scandinavian SME price sensitivity"};
        var research=new DeterministicResearchService();
        using var factory=new GuidedFactory(provider,research);
        var seed=await SeedAsync(factory);
        using var client=CreateClient(factory,seed.Subject,seed.Email);
        var started=await StartAsync(client,seed);
        var requestId=Guid.NewGuid();
        var command=new AddGuidedWorkTurnCommand("Research current SME price sensitivity and document the evidence.",requestId,started.Version);

        var response=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",command);
        response.EnsureSuccessStatusCode();
        var turn=await response.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>();
        Assert.NotNull(turn);
        Assert.Equal(0,research.CallCount);
        Assert.Equal(1,provider.CallCount);
        Assert.DoesNotContain(turn!.Session.Fields,x=>x.SourceType=="observation");

        await factory.ExecuteScopeAsync(async scope =>
        {
            var processor=scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(default);
        });
        var completed=await client.GetFromJsonAsync<GuidedWorkSessionDto>($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}");
        Assert.NotNull(completed);
        Assert.Equal(1,research.CallCount);
        Assert.Equal(2,provider.CallCount);
        Assert.Contains("public_web_research",provider.LastRequest!.PublicResearchContext,StringComparison.Ordinal);
        var evidenceFields=completed!.Fields.Where(x=>x.SourceType=="observation").ToArray();
        Assert.NotEmpty(evidenceFields);
        Assert.All(evidenceFields,field=>
        {
            Assert.Equal("https://example.test/sme-pricing",field.SourceMetadata["source_urls"]![0]!.GetValue<string>());
            Assert.Equal(GuidedDraftFieldStatuses.Proposed,field.Status);
        });

        await factory.ExecuteScopeAsync(async scope =>
        {
            var processor=scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(default);
        });
        var repeated=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",command);
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(1,research.CallCount);
        Assert.Equal(2,provider.CallCount);
    }

    [Fact]
    public async Task Batch_status_command_confirms_matching_proposals_without_losing_research_provenance()
    {
        var provider=new DeterministicCheckpointProvider{ResearchQueryNext="Current evidence about Scandinavian SME price sensitivity"};
        using var factory=new GuidedFactory(provider,new DeterministicResearchService());
        var seed=await SeedAsync(factory);
        using var client=CreateClient(factory,seed.Subject,seed.Email);
        var started=await StartAsync(client,seed);
        var turnResponse=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",
            new AddGuidedWorkTurnCommand("Research current evidence.",Guid.NewGuid(),started.Version));
        turnResponse.EnsureSuccessStatusCode();
        var turn=(await turnResponse.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>())!;
        await factory.ExecuteScopeAsync(async scope =>
        {
            var processor=scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(default);
        });
        var researched=await client.GetFromJsonAsync<GuidedWorkSessionDto>($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}");
        Assert.NotNull(researched);
        var requestId=Guid.NewGuid();
        var command=new ChangeGuidedDraftFieldStatusesCommand([],GuidedDraftFieldStatuses.Proposed,
            GuidedDraftFieldStatuses.Confirmed,"Confirmed after reviewing the research.",requestId,researched!.Version);

        var response=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/fields/status",command);
        response.EnsureSuccessStatusCode();
        var confirmed=(await response.Content.ReadFromJsonAsync<GuidedWorkSessionDto>())!;
        var researchFields=confirmed.Fields.Where(x=>x.SourceType=="observation").ToArray();
        Assert.NotEmpty(researchFields);
        Assert.All(researchFields,x=>
        {
            Assert.Equal(GuidedDraftFieldStatuses.Confirmed,x.Status);
            Assert.Equal("https://example.test/sme-pricing",x.SourceMetadata["source_urls"]![0]!.GetValue<string>());
        });

        var repeated=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/fields/status",command);
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(confirmed.Version,(await repeated.Content.ReadFromJsonAsync<GuidedWorkSessionDto>())!.Version);
    }

    [Fact]
    public async Task Explicit_text_instruction_changes_every_matching_field_status()
    {
        var seed=await SeedAsync(_factory);
        using var client=CreateClient(_factory,seed.Subject,seed.Email);
        var started=await StartAsync(client,seed);
        var firstResponse=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",
            new AddGuidedWorkTurnCommand("Populate the operating brief.",Guid.NewGuid(),started.Version));
        firstResponse.EnsureSuccessStatusCode();
        var populated=(await firstResponse.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>())!.Session;
        var proposeResponse=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/fields/status",
            new ChangeGuidedDraftFieldStatusesCommand([],GuidedDraftFieldStatuses.Confirmed,GuidedDraftFieldStatuses.Proposed,
                "Prepare all fields for another review.",Guid.NewGuid(),populated.Version));
        proposeResponse.EnsureSuccessStatusCode();
        var proposed=(await proposeResponse.Content.ReadFromJsonAsync<GuidedWorkSessionDto>())!;

        var confirmResponse=await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns",
            new AddGuidedWorkTurnCommand("Set all fields that are in proposal state to Confirmed.",Guid.NewGuid(),proposed.Version));
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed=(await confirmResponse.Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>())!;

        Assert.All(confirmed.Session.Fields.Where(x=>x.IsRequired),x=>Assert.Equal(GuidedDraftFieldStatuses.Confirmed,x.Status));
        Assert.All(confirmed.Session.Fields.Where(x=>x.IsRequired),x=>Assert.Equal("user",x.SourceType));
        Assert.Equal(4,confirmed.Changes.Count);
    }

    [Fact]
    public async Task Capability_catalog_supports_company_scoped_background_execution_without_an_http_identity()
    {
        var seed = await SeedAsync(_factory);

        await _factory.ExecuteScopeAsync(async scope =>
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IAgentCapabilityCatalog>();
            var result = await catalog.GetEffectiveCatalogAsync(seed.CompanyId, seed.AgentId, default);

            Assert.Equal(seed.CompanyId, result.CompanyId);
            Assert.Equal(seed.AgentId, result.AgentId);
            Assert.Equal("Olivia", result.AgentName);

            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                catalog.GetEffectiveCatalogAsync(Guid.NewGuid(), seed.AgentId, default));
        });
    }

    [Fact]
    public async Task Sideband_tools_are_bounded_idempotent_audited_and_never_commit()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);
        var started = await StartAsync(client, seed);
        var providerCallId = $"call_{Guid.NewGuid():N}";
        await _factory.ExecuteDbContextAsync(async db =>
        {
            var userId = await db.Users.SingleAsync(x => x.Email == seed.Email);
            var binding = new GuidedVoiceBinding(Guid.NewGuid(), seed.CompanyId, started.Id, userId.Id, providerCallId, DateTime.UtcNow.AddMinutes(5));
            binding.Connected();
            db.GuidedVoiceBindings.Add(binding);
            await db.SaveChangesAsync();
        });

        await _factory.ExecuteScopeAsync(async scope =>
        {
            var tools = scope.ServiceProvider.GetRequiredService<IGuidedVoiceToolService>();
            var safeDraft = await tools.ExecuteAsync(providerCallId, "tool-safe-draft", "get_current_safe_draft", "{}", default);
            Assert.Contains("\"committed\":false", safeDraft, StringComparison.Ordinal);
            Assert.Contains("\"company_reference_context\"", safeDraft, StringComparison.Ordinal);

            var documentSearch = await tools.ExecuteAsync(providerCallId, "tool-document-search", "search_workshop_documents", "{\"query\":\"What does the attached document say?\"}", default);
            Assert.Contains("\"available\":false", documentSearch, StringComparison.Ordinal);
            Assert.Contains("No attached workshop documents are ready", documentSearch, StringComparison.Ordinal);

            var arguments = $$"""{"expected_version":{{started.Version}},"patches":[{"path":"company_information","value":"A bounded proposed company description.","status":"proposed","explanation":"Proposed from finalized speech for user review."}]}""";
            var first = await tools.ExecuteAsync(providerCallId, "tool-propose-1", "propose_draft_patch", arguments, default);
            var repeated = await tools.ExecuteAsync(providerCallId, "tool-propose-1", "propose_draft_patch", arguments, default);
            Assert.Equal(first, repeated);
            Assert.Contains("\"committed\":false", first, StringComparison.Ordinal);

            var statusArguments = $$"""{"expected_version":{{started.Version + 1}},"paths":[],"from_status":"proposed","status":"confirmed","explanation":"The user explicitly confirmed every proposal."}""";
            var statusResult = await tools.ExecuteAsync(providerCallId, "tool-status-1", "set_draft_field_status", statusArguments, default);
            Assert.Contains("\"changed_fields\":[\"company_information\"]",statusResult,StringComparison.Ordinal);
            Assert.Contains("\"status\":\"confirmed\"",statusResult,StringComparison.Ordinal);

            var stale = $$"""{"expected_version":{{started.Version}},"patches":[{"path":"company_information","value":"A stale proposal.","status":"proposed","explanation":"This request used an earlier draft version."}]}""";
            var refreshRequired = await tools.ExecuteAsync(providerCallId, "tool-stale-1", "propose_draft_patch", stale, default);
            Assert.Contains("\"error\":\"draft_version_stale\"", refreshRequired, StringComparison.Ordinal);
            Assert.Contains("\"retryable\":true", refreshRequired, StringComparison.Ordinal);
            Assert.Contains($"\"current_version\":{started.Version + 2}", refreshRequired, StringComparison.Ordinal);

            var invalid = $$"""{"expected_version":{{started.Version + 2}},"patches":[{"path":"company_information","value":"Unsafe confirmation.","status":"confirmed","explanation":"Voice cannot confirm."}]}""";
            await Assert.ThrowsAsync<InvalidOperationException>(() => tools.ExecuteAsync(providerCallId, "tool-invalid-1", "propose_draft_patch", invalid, default));
        });

        await _factory.ExecuteDbContextAsync(async db =>
        {
            var session = await db.GuidedWorkSessions.IgnoreQueryFilters().Include(x => x.Fields).SingleAsync(x => x.Id == started.Id);
            Assert.Equal(GuidedWorkSessionStatuses.Active, session.Status);
            Assert.Null(session.CompletedUtc);
            Assert.Equal(GuidedDraftFieldStatuses.Confirmed, session.Fields.Single(x => x.Path == "company_information").Status);
            Assert.Equal(5, await db.GuidedSessionOperations.IgnoreQueryFilters().CountAsync(x => x.SessionId == started.Id && x.OperationType == "voice_tool"));
            var outcomes = await db.AuditEvents.IgnoreQueryFilters().Where(x => x.TargetId == started.Id.ToString("N") && x.Action == "guided_session.voice_tool").Select(x => x.Outcome).ToListAsync();
            Assert.Contains("succeeded", outcomes);
            Assert.Contains("refresh_required", outcomes);
            Assert.Contains("rejected", outcomes);
        });
    }

    [Fact]
    public async Task Provider_failure_persists_one_user_turn_and_retry_recovers_without_duplication()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);
        var started = await StartAsync(client, seed);
        var requestId = Guid.NewGuid();
        var command = new AddGuidedWorkTurnCommand("Keep this finalized input even if extraction fails.", requestId, started.Version);
        _provider.FailNext = true;

        var failed = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns", command);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(1, await CountGuidedMessagesAsync(started.Id, ChatSenderTypes.User));
        Assert.Equal(0, await CountGuidedMessagesAsync(started.Id, ChatSenderTypes.Agent));

        var recovered = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}/turns", command);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(1, await CountGuidedMessagesAsync(started.Id, ChatSenderTypes.User));
        Assert.Equal(1, await CountGuidedMessagesAsync(started.Id, ChatSenderTypes.Agent));
    }

    [Fact]
    public async Task Session_isolated_from_other_users_and_companies()
    {
        var seed = await SeedAsync(_factory);
        using var ownerClient = CreateClient(_factory, seed.Subject, seed.Email);
        var started = await StartAsync(ownerClient, seed);

        using var colleagueClient = CreateClient(_factory, seed.ColleagueSubject, seed.ColleagueEmail);
        Assert.Equal(HttpStatusCode.NotFound,
            (await colleagueClient.GetAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}")).StatusCode);

        using var outsiderClient = CreateClient(_factory, seed.OutsiderSubject, seed.OutsiderEmail);
        var crossCompany = await outsiderClient.GetAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions/{started.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, crossCompany.StatusCode);
    }

    [Fact]
    public async Task Disabled_feature_rejects_new_workshop_but_preserves_ordinary_chat()
    {
        using var disabledFactory = new TestWebApplicationFactory(new Dictionary<string, string?>
        {
            [$"{GuidedDialogueOptions.SectionName}:Enabled"] = "false"
        });
        var seed = await SeedAsync(disabledFactory);
        using var client = CreateClient(disabledFactory, seed.Subject, seed.Email);

        var workshop = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions",
            new StartGuidedWorkSessionCommand(GuidedArtifactTypes.AgentOperatingBrief, seed.AgentId));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, workshop.StatusCode);

        var chat = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
    }

    [Fact]
    public async Task Ineligible_agent_returns_an_actionable_permission_problem()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions",
            new StartGuidedWorkSessionCommand(GuidedArtifactTypes.MarketingSegment, seed.AgentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("Workshop permission required", problem!.Title);
        Assert.Contains("tools and data permissions", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retention_removes_expired_completed_sessions_but_preserves_active_work()
    {
        var seed = await SeedAsync(_factory);
        using var client = CreateClient(_factory, seed.Subject, seed.Email);
        var completed = await StartAsync(client, seed);

        var turn = await (await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{completed.Id}/turns",
            new AddGuidedWorkTurnCommand("Confirm this brief.", Guid.NewGuid(), completed.Version)))
            .Content.ReadFromJsonAsync<GuidedWorkTurnResultDto>();
        var review = await (await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{completed.Id}/review",
            new PrepareGuidedWorkReviewCommand(Guid.NewGuid(), turn!.Session.Version)))
            .Content.ReadFromJsonAsync<GuidedWorkReviewDto>();
        var commitResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/guided-work-sessions/{completed.Id}/commit",
            new ConfirmGuidedWorkCommitCommand(review!.ReviewToken, Guid.NewGuid(), review.Session.Version));
        commitResponse.EnsureSuccessStatusCode();

        var active = await StartAsync(client, seed);
        var expiredUtc = DateTime.UtcNow.AddDays(-30);
        await _factory.ExecuteDbContextAsync(async db =>
        {
            await db.GuidedWorkSessions.IgnoreQueryFilters().Where(x => x.Id == completed.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedUtc, expiredUtc));
        });

        await _factory.ExecuteScopeAsync(async scope =>
        {
            var worker = new GuidedWorkRetentionWorker(
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new GuidedDialogueOptions { RetentionDays = 7 }),
                NullLogger<GuidedWorkRetentionWorker>.Instance);
            await worker.RunOnceAsync(default);
        });

        await _factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.GuidedWorkSessions.IgnoreQueryFilters().AnyAsync(x => x.Id == completed.Id));
            Assert.True(await db.GuidedWorkSessions.IgnoreQueryFilters().AnyAsync(x => x.Id == active.Id));
        });
    }

    [Fact]
    public async Task Guided_foreign_keys_have_one_SQL_Server_safe_company_cascade_path()
    {
        await _factory.ExecuteDbContextAsync(db =>
        {
            var model = db.Model;
            var sessionType = model.FindEntityType(typeof(GuidedWorkSession))!;
            Assert.Equal(DeleteBehavior.Cascade, sessionType.GetForeignKeys()
                .Single(x => x.PrincipalEntityType.ClrType == typeof(Company)).DeleteBehavior);

            foreach (var childType in new[]
                     {
                         typeof(GuidedDraftField), typeof(GuidedSessionOperation), typeof(GuidedVoiceBinding)
                     })
            {
                var entityType = model.FindEntityType(childType)!;
                Assert.Equal(DeleteBehavior.NoAction, entityType.GetForeignKeys()
                    .Single(x => x.PrincipalEntityType.ClrType == typeof(Company)).DeleteBehavior);
                Assert.Equal(DeleteBehavior.Cascade, entityType.GetForeignKeys()
                    .Single(x => x.PrincipalEntityType.ClrType == typeof(GuidedWorkSession)).DeleteBehavior);
            }

            return Task.CompletedTask;
        });
    }

    private async Task<int> CountGuidedMessagesAsync(Guid sessionId, string senderType) =>
        await _factory.ExecuteDbContextAsync(async db =>
        {
            var session = await db.GuidedWorkSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == sessionId);
            var messages = await db.Messages.IgnoreQueryFilters().Where(x => x.ConversationId == session.ConversationId && x.SenderType == senderType).ToListAsync();
            return messages.Count(x => x.StructuredPayload.TryGetValue("guided_session_id", out var value) &&
                                       Guid.TryParse(value?.GetValue<string>(), out var parsed) && parsed == sessionId);
        });

    private static async Task<GuidedWorkSessionDto> StartAsync(HttpClient client, Seed seed)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/guided-work-sessions",
            new StartGuidedWorkSessionCommand(GuidedArtifactTypes.AgentOperatingBrief, seed.AgentId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GuidedWorkSessionDto>())!;
    }

    private static HttpClient CreateClient(TestWebApplicationFactory factory, string subject, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Guided Test User");
        return client;
    }

    private static async Task<Seed> SeedAsync(TestWebApplicationFactory factory)
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var colleagueId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var subject = $"guided-{Guid.NewGuid():N}";
        var colleagueSubject = $"guided-colleague-{Guid.NewGuid():N}";
        var outsiderSubject = $"guided-outsider-{Guid.NewGuid():N}";
        var tools = new Dictionary<string, JsonNode?> { ["knowledge.search"] = JsonValue.Create(true) };
        var scopes = new Dictionary<string, JsonNode?> { ["read"] = new JsonArray("company", "documents", "knowledge") };
        var status = AgentStatus.Active;
        var autonomy = AgentAutonomyLevel.Guided;

        await factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(userId, $"{subject}@example.com", "Guided Owner", "dev-header", subject),
                new User(colleagueId, $"{colleagueSubject}@example.com", "Guided Colleague", "dev-header", colleagueSubject),
                new User(outsiderId, $"{outsiderSubject}@example.com", "Guided Outsider", "dev-header", outsiderSubject));
            db.Companies.AddRange(new Company(companyId, "Guided Company"), new Company(otherCompanyId, "Other Company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, colleagueId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, outsiderId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.Agents.Add(new Agent(agentId, companyId, "operations", "Olivia", "Operations lead", "Operations", null,
                AgentSeniority.Lead, status, autonomy, tools: tools, scopes: scopes, roleBrief: "Original role brief."));
            return Task.CompletedTask;
        });

        return new Seed(companyId, agentId, subject, $"{subject}@example.com", colleagueSubject,
            $"{colleagueSubject}@example.com", outsiderSubject, $"{outsiderSubject}@example.com", status, autonomy,
            new JsonObject(tools.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Key, x.Value?.DeepClone()))),
            new JsonObject(scopes.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Key, x.Value?.DeepClone()))));
    }

    private sealed record Seed(Guid CompanyId, Guid AgentId, string Subject, string Email,
        string ColleagueSubject, string ColleagueEmail, string OutsiderSubject, string OutsiderEmail,
        AgentStatus Status, AgentAutonomyLevel AutonomyLevel, JsonObject Tools, JsonObject Scopes);

    private sealed class GuidedFactory(IGuidedCheckpointProvider provider,IGuidedEvidenceResearchService? research=null) : TestWebApplicationFactory(
        new Dictionary<string, string?>
        {
            [$"{GuidedDialogueOptions.SectionName}:Enabled"] = "true",
            [$"{GuidedDialogueOptions.SectionName}:RealtimeEnabled"] = "false"
        })
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGuidedCheckpointProvider>();
                services.AddSingleton(provider);
                if(research is not null){services.RemoveAll<IGuidedEvidenceResearchService>();services.AddSingleton(research);}
            });
        }
    }

    private sealed class DeterministicCheckpointProvider : IGuidedCheckpointProvider
    {
        public int CallCount { get; private set; }
        public bool FailNext { get; set; }
        public string? ResearchQueryNext { get; set; }
        public Func<GuidedCheckpointRequest, GuidedCheckpointResult>? ResultFactory { get; set; }
        public GuidedCheckpointRequest? LastRequest { get; private set; }

        public Task<GuidedCheckpointResult> CreateCheckpointAsync(GuidedCheckpointRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            CallCount++;
            if (FailNext)
            {
                FailNext = false;
                throw new GuidedCheckpointUnavailableException("Configured checkpoint failure.");
            }
            if(ResultFactory is not null)return Task.FromResult(ResultFactory(request));

            if(ResearchQueryNext is not null&&request.PublicResearchContext.Contains("not been performed",StringComparison.OrdinalIgnoreCase))
            {
                var query=ResearchQueryNext;ResearchQueryNext=null;
                return Task.FromResult(new GuidedCheckpointResult("",[],[],[],[],[],"Research requested.",null,false,query));
            }

            if(request.UserMessage.Contains("proposal state to Confirmed",StringComparison.OrdinalIgnoreCase))
            {
                var statusChanges=request.Fields.Where(x=>x.Status==GuidedDraftFieldStatuses.Proposed&&x.Value is not null)
                    .Select(x=>new GuidedFieldStatusChangeOperation(x.Path,GuidedDraftFieldStatuses.Confirmed,"Explicitly confirmed by the user.")).ToArray();
                return Task.FromResult(new GuidedCheckpointResult(
                    $"I confirmed {statusChanges.Length} proposed fields.",[],statusChanges.Select(x=>x.Path).ToArray(),[],[],[],
                    "The requested proposed fields are confirmed.",null,true,null,statusChanges));
            }

            var isResearch=request.PublicResearchContext.Contains("public_web_research",StringComparison.Ordinal);
            var patches = request.Fields.Where(x => x.IsRequired).Select(x => new GuidedPatchOperation(
                x.Path, JsonValue.Create($"Confirmed guidance for {x.Label}."), isResearch?GuidedDraftFieldStatuses.Proposed:GuidedDraftFieldStatuses.Confirmed,
                isResearch?"evidence":"user", isResearch?"Supported by current public research.":"Confirmed directly in this turn.",isResearch?new Dictionary<string,JsonNode?>{{"source_titles",new JsonArray("SME pricing study")},{"source_urls",new JsonArray("https://example.test/sme-pricing")},{"research_failure_code",null}}:null)).ToArray();
            return Task.FromResult(new GuidedCheckpointResult(
                "I recorded the confirmed operating guidance.", patches, patches.Select(x => x.Path).ToArray(), [], [], [],
                "All required operating-brief sections are confirmed.", null, true));
        }
    }

    private sealed class DeterministicResearchService:IGuidedEvidenceResearchService
    {
        public int CallCount{get;private set;}
        public Task<GuidedEvidenceResearchResult> ResearchAsync(Guid companyId,Guid agentId,string query,CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new GuidedEvidenceResearchResult(true,"Current evidence indicates value-based purchasing with meaningful price sensitivity.",[new GuidedEvidenceSource("SME pricing study","https://example.test/sme-pricing")]));
        }
    }
}
