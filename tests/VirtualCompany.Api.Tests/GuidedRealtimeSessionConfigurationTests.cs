using System.Text.Json.Nodes;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class GuidedRealtimeSessionConfigurationTests
{
    [Fact]
    public void Audio_input_uses_conservative_semantic_turn_detection_with_application_response_control()
    {
        var input = GuidedRealtimeSessionConfiguration.BuildAudioInput(new GuidedDialogueOptions());

        Assert.Equal("near_field", input["noise_reduction"]!["type"]!.GetValue<string>());
        Assert.Equal("semantic_vad", input["turn_detection"]!["type"]!.GetValue<string>());
        Assert.Equal("low", input["turn_detection"]!["eagerness"]!.GetValue<string>());
        Assert.False(input["turn_detection"]!["create_response"]!.GetValue<bool>());
        Assert.False(input["turn_detection"]!["interrupt_response"]!.GetValue<bool>());
    }

    [Fact]
    public void Server_turn_detection_remains_a_bounded_supported_fallback()
    {
        var input = GuidedRealtimeSessionConfiguration.BuildAudioInput(new GuidedDialogueOptions
        {
            RealtimeTurnDetection = "server_vad",
            RealtimeVadSilenceDurationMs = 1200
        });

        Assert.Equal("server_vad", input["turn_detection"]!["type"]!.GetValue<string>());
        Assert.Equal(0.15, input["turn_detection"]!["threshold"]!.GetValue<double>());
        Assert.Equal(300, input["turn_detection"]!["prefix_padding_ms"]!.GetValue<int>());
        Assert.Equal(1200, input["turn_detection"]!["silence_duration_ms"]!.GetValue<int>());
        Assert.False(input["turn_detection"]!["create_response"]!.GetValue<bool>());
    }

    [Fact]
    public void Invalid_turn_detection_values_fall_back_to_safe_low_latency_defaults()
    {
        var input = GuidedRealtimeSessionConfiguration.BuildAudioInput(new GuidedDialogueOptions
        {
            RealtimeTurnEagerness = "unexpected",
            RealtimeTurnDetection = "unexpected",
            RealtimeNoiseReduction = "unexpected"
        });

        Assert.Equal("server_vad", input["turn_detection"]!["type"]!.GetValue<string>());
        Assert.Equal("near_field", input["noise_reduction"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(0.01, 0.05)]
    [InlineData(0.2, 0.2)]
    [InlineData(2, 1)]
    public void Server_vad_threshold_is_configurable_and_bounded(double configured,double expected)
    {
        var input=GuidedRealtimeSessionConfiguration.BuildAudioInput(new GuidedDialogueOptions { RealtimeTurnDetection="server_vad",RealtimeVadThreshold=configured });

        Assert.Equal(expected,input["turn_detection"]!["threshold"]!.GetValue<double>());
    }

    [Theory]
    [InlineData(100,500)]
    [InlineData(1500,1500)]
    [InlineData(9000,3000)]
    public void Server_vad_silence_duration_is_configurable_and_bounded(int configured,int expected)
    {
        var input=GuidedRealtimeSessionConfiguration.BuildAudioInput(new GuidedDialogueOptions { RealtimeTurnDetection="server_vad",RealtimeVadSilenceDurationMs=configured });

        Assert.Equal(expected,input["turn_detection"]!["silence_duration_ms"]!.GetValue<int>());
    }

    [Fact]
    public void Guided_research_is_enabled_with_a_bounded_default_model_budget()
    {
        var options = new GuidedDialogueOptions();

        Assert.True(options.ResearchEnabled);
        Assert.Equal("gpt-5.4-mini", options.ResearchModel);
        Assert.InRange(options.ResearchMaxOutputTokens, 300, 3000);
    }

    [Fact]
    public void Realtime_session_exposes_a_bounded_attached_document_search_tool()
    {
        var update=GuidedRealtimeSidebandRegistry.BuildSessionUpdate(new GuidedDialogueOptions());

        Assert.Contains("search_workshop_documents",update,StringComparison.Ordinal);
        Assert.Contains("attached to this workshop",update,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Realtime_session_exposes_generic_explicit_status_change_tool()
    {
        var update=GuidedRealtimeSidebandRegistry.BuildSessionUpdate(new GuidedDialogueOptions());

        Assert.Contains("set_draft_field_status",update,StringComparison.Ordinal);
        Assert.Contains("from_status",update,StringComparison.Ordinal);
        Assert.Contains("needs_work",update,StringComparison.Ordinal);
        Assert.Contains("explicitly requests",update,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Realtime_navigation_reads_the_next_question_and_never_narrates_internal_tool_failures()
    {
        Assert.Contains("read-only navigation requests",GuidedRealtimeCallService.TurnControlInstructions,StringComparison.Ordinal);
        Assert.Contains("ask its next_question",GuidedRealtimeCallService.TurnControlInstructions,StringComparison.Ordinal);
        Assert.Contains("remain silent",GuidedRealtimeCallService.TurnControlInstructions,StringComparison.Ordinal);
        Assert.Contains("Never mention patches",GuidedRealtimeCallService.TurnControlInstructions,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"query\":\"SME price sensitivity\"}", "SME price sensitivity")]
    [InlineData("{\"question\":\"SME price sensitivity\"}", "SME price sensitivity")]
    public void Research_query_accepts_bounded_realtime_argument_aliases(string json,string expected)
    {
        using var document=System.Text.Json.JsonDocument.Parse(json);
        var method=typeof(GuidedVoiceToolService).GetMethod("TryReadResearchQuery",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        var arguments=new object?[]{document.RootElement,null};

        var accepted=(bool)method.Invoke(null,arguments)!;

        Assert.True(accepted);
        Assert.Equal(expected,arguments[1]);
    }

    [Fact]
    public void Voice_patch_normalizes_a_numeric_string_to_the_field_number_contract()
    {
        var field=new GuidedFieldDefinition("confidence","Confidence","Confidence score",GuidedFieldValueTypes.Number,true,Minimum:0,Maximum:1);

        var accepted=GuidedVoiceToolService.TryNormalizeFieldValue(field,JsonValue.Create("0.65"),out var normalized,out var error);

        Assert.True(accepted);
        Assert.Null(error);
        Assert.Equal(0.65m,normalized!.GetValue<decimal>());
    }

    [Fact]
    public void Voice_patch_returns_actionable_feedback_for_a_number_label()
    {
        var field=new GuidedFieldDefinition("confidence","Confidence","Confidence score",GuidedFieldValueTypes.Number,true,Minimum:0,Maximum:1);

        var accepted=GuidedVoiceToolService.TryNormalizeFieldValue(field,JsonValue.Create("medium"),out _,out var error);

        Assert.False(accepted);
        Assert.Contains("JSON number",error,StringComparison.Ordinal);
    }

    [Fact]
    public void Replayed_tool_completion_is_dispatched_only_once()
    {
        var handled=new HashSet<string>(StringComparer.Ordinal);

        Assert.True(GuidedRealtimeSidebandRegistry.TryBeginToolCall(handled,"call-1"));
        Assert.False(GuidedRealtimeSidebandRegistry.TryBeginToolCall(handled,"call-1"));
    }

    [Fact]
    public void New_user_speech_invalidates_an_older_tool_continuation_epoch()
    {
        var epoch=new GuidedRealtimeTurnEpoch();
        var toolEpoch=epoch.Current;

        epoch.Advance();

        Assert.False(epoch.IsCurrent(toolEpoch));
        Assert.True(epoch.IsCurrent(epoch.Current));
    }

    [Fact]
    public void Workshop_insights_are_a_bounded_non_artifact_field()
    {
        var field=GuidedWorkshopFields.Insights;

        Assert.Equal("workshop_insights",field.Path);
        Assert.False(field.IsRequired);
        Assert.Equal(GuidedFieldValueTypes.Text,field.ValueType);
        Assert.Equal(8000,field.MaxLength);
        Assert.Contains("not committed",field.Description,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workshop_insights",OpenAiGuidedCheckpointProvider.CheckpointInstructions,StringComparison.Ordinal);
    }

    [Fact]
    public void Off_schema_checkpoint_patch_is_retained_as_a_workshop_insight()
    {
        var checkpoint=Checkpoint(new GuidedPatchOperation("core_values",JsonValue.Create("Customers value reliability."),"confirmed","user","Useful for positioning."));

        var normalized=GuidedWorkSessionService.NormalizeCheckpoint(new TestArtifactDefinition(),checkpoint);

        var patch=Assert.Single(normalized.Patches);
        Assert.Equal(GuidedWorkshopFields.InsightsPath,patch.Path);
        Assert.Contains("Customers value reliability",patch.Value!.GetValue<string>(),StringComparison.Ordinal);
        Assert.Contains("core_values",patch.Value!.GetValue<string>(),StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_checkpoint_confidence_is_downgraded_instead_of_rejecting_the_turn()
    {
        var checkpoint=Checkpoint(new GuidedPatchOperation(GuidedWorkshopFields.InsightsPath,JsonValue.Create("Possible buying value."),"confirmed","assumption","Inferred from the discussion."));

        var normalized=GuidedWorkSessionService.NormalizeCheckpoint(new TestArtifactDefinition(),checkpoint);

        var patch=Assert.Single(normalized.Patches);
        Assert.Equal("proposed",patch.Status);
        Assert.Equal("assumption",patch.SourceType);
    }

    [Fact]
    public void Unexpected_checkpoint_status_and_source_are_normalized_instead_of_rejecting_the_turn()
    {
        var checkpoint=Checkpoint(new GuidedPatchOperation("needs",JsonValue.Create("Fast onboarding."),"accepted","external_research",""));

        var normalized=GuidedWorkSessionService.NormalizeCheckpoint(new TestArtifactDefinition(),checkpoint);

        var patch=Assert.Single(normalized.Patches);
        Assert.Equal("proposed",patch.Status);
        Assert.Equal("observation",patch.SourceType);
        Assert.False(string.IsNullOrWhiteSpace(patch.Explanation));
    }

    [Fact]
    public void Concurrent_workshop_insights_are_merged_without_discarding_either_update()
    {
        var merged=GuidedWorkSessionService.MergeWorkshopInsights(JsonValue.Create("Earlier insight."),JsonValue.Create("New checkpoint detail."),8000);

        Assert.Contains("Earlier insight",merged!.GetValue<string>(),StringComparison.Ordinal);
        Assert.Contains("New checkpoint detail",merged.GetValue<string>(),StringComparison.Ordinal);
    }

    private static GuidedCheckpointResult Checkpoint(GuidedPatchOperation patch)=>new("Recorded.",[patch],[],[],[],[],"Draft updated.",null,false);

    private sealed class TestArtifactDefinition : IGuidedArtifactDefinition
    {
        public string ArtifactType=>"test";public string SchemaVersion=>"1";public string DisplayName=>"Test";
        public IReadOnlyList<GuidedFieldDefinition> Fields{get;}=[new("needs","Needs","Customer needs",GuidedFieldValueTypes.Text,true,MaxLength:8000)];
        public Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId,Guid agentId,Guid? targetArtifactId,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task EnsureEligibleAsync(Guid companyId,Guid agentId,CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<IReadOnlyList<string>> ValidateAsync(Guid companyId,Guid agentId,Guid? targetArtifactId,IReadOnlyDictionary<string,JsonNode?> values,CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<string>>([]);
        public Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext context,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
}
