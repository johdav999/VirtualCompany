namespace VirtualCompany.Application.Agents;

public sealed record ApprovedSpeechProfile(bool Available, string Model, string Voice, string ConfigurationVersion);
public sealed record ApprovedSpeechRequest(Guid CompanyId, Guid UserId, Guid AgentId, string Text,
    string Language, string Voice, string ConfigurationVersion, string OperationId);
public sealed record ApprovedSpeechResult(byte[] Pcm, string Transcript, string Model, string ProviderResponseId,
    int InputTokens, int OutputTokens, string UsageJson, bool ContentMatches);

/// <summary>Shared, bounded synthesis of reviewed text. No tools or microphone input.</summary>
public interface IApprovedSpeechGateway
{
    Task<ApprovedSpeechProfile> GetProfileAsync(CancellationToken ct);
    Task<ApprovedSpeechResult> GenerateAsync(ApprovedSpeechRequest request, CancellationToken ct);
}

