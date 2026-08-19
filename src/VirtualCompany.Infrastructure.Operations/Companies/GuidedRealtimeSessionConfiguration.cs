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
        ["turn_detection"] = BuildTurnDetection(options)
    };

    internal static JsonObject BuildTurnDetection(GuidedDialogueOptions options) =>
        NormalizeTurnDetection(options.RealtimeTurnDetection) == "semantic_vad"
            ? new JsonObject
            {
                ["type"] = "semantic_vad",
                ["eagerness"] = NormalizeEagerness(options.RealtimeTurnEagerness),
                ["create_response"] = true,
                ["interrupt_response"] = options.RealtimeAutomaticInterruption
            }
            : new JsonObject
            {
                ["type"] = "server_vad",
                ["threshold"] = NormalizeVadThreshold(options.RealtimeVadThreshold),
                ["prefix_padding_ms"] = 300,
                ["silence_duration_ms"] = 650,
                ["create_response"] = true,
                ["interrupt_response"] = options.RealtimeAutomaticInterruption
            };

    internal static string NormalizeTurnDetection(string? value) =>
        value?.Trim().ToLowerInvariant() == "semantic_vad" ? "semantic_vad" : "server_vad";

    internal static string NormalizeEagerness(string? value) =>
        value?.Trim().ToLowerInvariant() is "low" or "medium" or "high" ? value.Trim().ToLowerInvariant() : "high";

    internal static double NormalizeVadThreshold(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.05, 1) : 0.15;

    internal static string NormalizeNoiseReduction(string? value) =>
        value?.Trim().ToLowerInvariant() is "near_field" or "far_field" ? value.Trim().ToLowerInvariant() : "near_field";
}
