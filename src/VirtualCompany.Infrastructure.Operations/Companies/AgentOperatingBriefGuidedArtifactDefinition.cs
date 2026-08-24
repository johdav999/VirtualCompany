using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.GuidedWork;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentOperatingBriefGuidedArtifactDefinition : IGuidedArtifactDefinition
{
    private readonly ICompanyAgentService _agents;
    public AgentOperatingBriefGuidedArtifactDefinition(ICompanyAgentService agents) => _agents = agents;

    public string ArtifactType => GuidedArtifactTypes.AgentOperatingBrief;
    public string SchemaVersion => "1.0";
    public string DisplayName => "Agent operating brief";
    public GuidedArtifactCapabilities Capabilities { get; } = new(
        SupportsDocumentAttachments: true,
        AllowedDocumentExtensions: [".pdf", ".docx", ".pptx", ".xlsx", ".csv", ".txt", ".md"],
        DocumentDataScopes: ["knowledge", "operations", "sales", "marketing", "support"],
        SupportsVoiceDocumentSearch: true,
        SupportsExternalResearch: true);
    public IReadOnlyList<string> QuestionPriorities =>
        [AgentBriefingCategories.CompanyInformation, AgentBriefingCategories.ProductsAndServices, AgentBriefingCategories.Policies, AgentBriefingCategories.CustomerSupport, AgentBriefingCategories.OtherInstructions];
    public IReadOnlyList<GuidedFieldDefinition> Fields { get; } =
    [
        Field(AgentBriefingCategories.CompanyInformation, "Company information", "What the company does, who it serves, and the operating context."),
        Field(AgentBriefingCategories.ProductsAndServices, "Products and services", "Products, services, positioning, pricing principles, and limitations."),
        Field(AgentBriefingCategories.Policies, "Policies", "Decision boundaries, approvals, compliance obligations, and escalation guidance."),
        Field(AgentBriefingCategories.CustomerSupport, "Customer support", "Customer promises, response guidance, service standards, and escalation paths."),
        Field(AgentBriefingCategories.OtherInstructions, "Other instructions", "Role-specific instructions not covered by the other sections.", false)
    ];

    public async Task EnsureEligibleAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
    {
        var profile = await _agents.GetOperatingProfileAsync(companyId, agentId, cancellationToken);
        if (!profile.Visibility.CanEditAgent)
            throw new UnauthorizedAccessException("The current user cannot edit this agent's brief.");
    }

    public async Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId, Guid agentId, Guid? targetArtifactId, CancellationToken cancellationToken)
    {
        if (targetArtifactId is not null && targetArtifactId != agentId)
            throw new GuidedWorkValidationException(new Dictionary<string, string[]> { [nameof(targetArtifactId)] = ["The brief target must be the selected agent."] });
        var profile = await _agents.GetOperatingProfileAsync(companyId, agentId, cancellationToken);
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (profile.CommunicationProfile.TryGetValue("briefing", out var briefingNode) && briefingNode is JsonObject briefing)
        {
            foreach (var category in AgentBriefingCategories.All)
                if (briefing[category]?.GetValue<string>() is { Length: > 0 } content) values[category] = JsonValue.Create(content);
        }
        return new GuidedArtifactInitialization(DisplayName, agentId, profile.UpdatedUtc.ToString("O"), values,
            OpeningSummary: $"Build a clear operating brief for {profile.DisplayName}.",
            OpeningQuestion: "Let’s start with company context: what should this agent understand about the business and its customers?");
    }

    public Task<IReadOnlyList<string>> ValidateAsync(Guid companyId, Guid agentId, Guid? targetArtifactId,
        IReadOnlyDictionary<string, JsonNode?> values, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var field in Fields.Where(x => x.IsRequired))
            if (!values.TryGetValue(field.Path, out var value) || value is null || string.IsNullOrWhiteSpace(value.GetValue<string>()))
                errors.Add(field.Label);
        return Task.FromResult<IReadOnlyList<string>>(errors);
    }

    public Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid companyId, Guid agentId, Guid? targetArtifactId,
        IReadOnlyDictionary<string, JsonNode?> values, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>
        ([
            new("Agent settings affected", "Operating brief only", "Confirmation does not change autonomy, permissions, tools, status, or unrelated profile settings."),
            new("Required sections", $"{Fields.Count(x => x.IsRequired)} confirmed sections", "The selected agent will use this brief as governed operating context after confirmation.")
        ]);

    public async Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext context, CancellationToken cancellationToken)
    {
        var current = await _agents.GetOperatingProfileAsync(context.CompanyId, context.AgentId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(context.TargetArtifactVersion) &&
            !string.Equals(current.UpdatedUtc.ToString("O"), context.TargetArtifactVersion, StringComparison.Ordinal))
            throw new GuidedWorkConflictException("The agent profile changed after this workshop started. Refresh the draft and review again.");
        var sections = Fields.ToDictionary(x => x.Path,
            x => context.Values.TryGetValue(x.Path, out var value) && value is not null ? value.GetValue<string>().Trim() : string.Empty,
            StringComparer.OrdinalIgnoreCase);
        var result = await _agents.UpdateBriefAsync(context.CompanyId, context.AgentId, new UpdateAgentBriefCommand(sections), cancellationToken);
        return new GuidedArtifactCommitResult(result.Id, result.UpdatedUtc.ToString("O"), $"Updated the operating brief for {result.DisplayName}.");
    }

    private static GuidedFieldDefinition Field(string path, string label, string description, bool required = true) =>
        new(path, label, description, GuidedFieldValueTypes.Text, required, MaxLength: 12000, AllowsEvidence: true);
}
