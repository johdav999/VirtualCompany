using System.Text.Json.Nodes;

namespace VirtualCompany.Infrastructure.Companies;

internal static class GuidedRealtimeSessionConfiguration
{
    internal static JsonObject BuildAudio(GuidedDialogueOptions options) => new()
    {
        ["input"] = BuildAudioInput(options),
        ["output"] = new JsonObject
        {
            ["voice"] = options.RealtimeVoice
        }
    };

    internal static JsonObject BuildAudioInput(GuidedDialogueOptions options) => new()
    {
        ["noise_reduction"] = new JsonObject
        {
            ["type"] = NormalizeNoiseReduction(options.RealtimeNoiseReduction)
        },
        ["transcription"] = new JsonObject
        {
            ["model"] = options.RealtimeTranscriptionModel
        },
        ["turn_detection"] = new JsonObject
        {
            ["type"] = "semantic_vad",
            ["eagerness"] = NormalizeEagerness(options.RealtimeTurnEagerness),
            ["create_response"] = true,
            ["interrupt_response"] = options.RealtimeAutomaticInterruption
        }
    };

    internal static string NormalizeEagerness(string? value) =>
        value?.Trim().ToLowerInvariant() is "low" or "medium" or "high" ? value.Trim().ToLowerInvariant() : "high";

    internal static string NormalizeNoiseReduction(string? value) =>
        value?.Trim().ToLowerInvariant() is "near_field" or "far_field" ? value.Trim().ToLowerInvariant() : "far_field";
}
