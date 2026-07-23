using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class DefaultAgentCommunicationProfileProvider : IDefaultAgentCommunicationProfileProvider
{
    public AgentCommunicationProfileDto GetDefaultProfile() =>
        new(
            "professional, clear, helpful",
            "reliable business assistant",
            [
                "Be concise and structured.",
                "State assumptions when context is incomplete.",
                "Keep claims factual and avoid unsupported certainty."
            ],
            [
                "Use business-appropriate language.",
                "Focus on the requested outcome and next concrete step."
            ],
            [
                "hostile",
                "manipulative",
                "flippant",
                "overly casual",
                "abusive"
            ],
            AgentCommunicationProfileSources.Fallback,
            true);
}

public sealed class AgentCommunicationProfileResolver : IAgentCommunicationProfileResolver
{
    private readonly IDefaultAgentCommunicationProfileProvider _defaultProfileProvider;
    private readonly ILogger<AgentCommunicationProfileResolver> _logger;

    public AgentCommunicationProfileResolver(
        IDefaultAgentCommunicationProfileProvider defaultProfileProvider,
        ILogger<AgentCommunicationProfileResolver> logger)
    {
        _defaultProfileProvider = defaultProfileProvider;
        _logger = logger;
    }

    public AgentCommunicationProfileDto Resolve(
        IReadOnlyDictionary<string, JsonNode?>? persistedProfile,
        CommunicationProfileResolutionContext context)
    {
        if (AgentCommunicationProfileJsonMapper.HasExplicitProfile(persistedProfile))
        {
            var explicitProfile = AgentCommunicationProfileJsonMapper.ToDto(
                persistedProfile,
                AgentCommunicationProfileSources.Explicit,
                isFallback: false);
            var defaults = _defaultProfileProvider.GetDefaultProfile();

            return explicitProfile with
            {
                Tone = string.IsNullOrWhiteSpace(explicitProfile.Tone) ? defaults.Tone : explicitProfile.Tone,
                Persona = string.IsNullOrWhiteSpace(explicitProfile.Persona) ? defaults.Persona : explicitProfile.Persona,
                StyleDirectives = explicitProfile.StyleDirectives.Count == 0 ? defaults.StyleDirectives : explicitProfile.StyleDirectives,
                CommunicationRules = explicitProfile.CommunicationRules.Count == 0 ? defaults.CommunicationRules : explicitProfile.CommunicationRules,
                ForbiddenToneRules = explicitProfile.ForbiddenToneRules.Count == 0 ? defaults.ForbiddenToneRules : explicitProfile.ForbiddenToneRules
            };
        }

        var fallback = _defaultProfileProvider.GetDefaultProfile();
        _logger.LogInformation(
            "Applied fallback agent communication profile. AgentId={AgentId} CompanyId={CompanyId} GenerationPath={GenerationPath} CorrelationId={CorrelationId}",
            context.AgentId,
            context.CompanyId,
            context.GenerationPath,
            context.CorrelationId);

        return fallback;
    }
}
