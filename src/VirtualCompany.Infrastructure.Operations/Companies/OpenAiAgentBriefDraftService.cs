using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentBriefDraftOptions
{
    public const string SectionName = "AgentBriefDraft";

    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4.1-mini";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxOutputTokens { get; set; } = 900;
}

public sealed class OpenAiAgentBriefDraftService : IAgentBriefDraftService
{
    public const string ClientName = "agent-brief-draft";
    private const int ExistingTextMaxLength = 12000;
    private const int GroundingDocumentLimit = 6;
    private const int GroundingExcerptMaxLength = 4000;
    private const int GroundingCandidateLimit = 100;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly AgentBriefDraftOptions _options;
    private readonly IAgentReasoningGateway _reasoning;
    private readonly ICurrentUserAccessor _currentUser;

    public OpenAiAgentBriefDraftService(
        VirtualCompanyDbContext dbContext,
        IOptions<AgentBriefDraftOptions> options,
        IAgentReasoningGateway reasoning,
        ICurrentUserAccessor currentUser)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _reasoning = reasoning;
        _currentUser = currentUser;
    }

    public async Task<AgentBriefDraftDto> GenerateAsync(
        Guid companyId,
        Guid agentId,
        GenerateAgentBriefDraftCommand command,
        CancellationToken cancellationToken)
    {
        if (!AgentBriefingCategories.IsSupported(command.Category))
        {
            throw new ArgumentException("A supported briefing category is required.", nameof(command.Category));
        }

        if (!_options.Enabled)
        {
            throw new AgentBriefDraftUnavailableException(
                "OpenAI draft generation is not configured. Add an API key to AgentBriefDraft:ApiKey or OPENAI_API_KEY.");
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var agent = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken)
            ?? throw new KeyNotFoundException("Agent not found.");

        var documentCandidates = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ExtractedText != null)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(GroundingCandidateLimit)
            .ToListAsync(cancellationToken);
        var groundingDocuments = SelectGroundingDocuments(
            documentCandidates,
            agentId,
            command.Category);

        var sources = new List<AgentAiSource>
        {
            new("company-profile", "company_record", company.Name,
                $"Company name: {company.Name}; industry: {company.Industry ?? "not provided"}; business type: {company.BusinessType ?? "not provided"}; compliance region: {company.ComplianceRegion ?? "not provided"}; timezone: {company.Timezone ?? "not provided"}; currency: {company.Currency ?? "not provided"}.", company.UpdatedUtc)
        };
        sources.AddRange(groundingDocuments.Select((document, index) =>
            new AgentAiSource($"brief-document-{index + 1}", "knowledge_document", document.Title, document.Excerpt)));
        var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, AgentCapabilityIds.RoleBriefing,
            "1.0.0", "editable-agent-brief-v2", "1.0",
            $"Create a concise editable English briefing for category '{command.Category}' and agent {agent.DisplayName}, {agent.RoleName}. " +
            "Use only supplied sources. Rewrite unsupported existing claims, do not invent facts, and state a specific bracketed question for important missing information. " +
            $"Existing untrusted draft: {Truncate(command.ExistingText ?? string.Empty, ExistingTextMaxLength)}",
            sources, ["read", "recommend"], [], _currentUser.UserId), cancellationToken);

        if (result.FailureCode is not null || string.IsNullOrWhiteSpace(result.Summary))
        {
            throw new AgentBriefDraftUnavailableException(result.FailureMessage ?? "OpenAI returned no draft content.");
        }
        var content = result.Claims.Count == 0 ? result.Summary : string.Join(Environment.NewLine + Environment.NewLine,
            new[] { result.Summary }.Concat(result.Claims.Select(x => x.Text)).Distinct(StringComparer.OrdinalIgnoreCase));
        return new AgentBriefDraftDto(command.Category.Trim(), content, "shared-agent-ai", DateTime.UtcNow);
    }

    internal static string BuildPrompt(
        Company company,
        Agent agent,
        GenerateAgentBriefDraftCommand command,
        IReadOnlyList<AgentBriefGroundingDocument> groundingDocuments)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Briefing category: {command.Category}");
        builder.AppendLine($"Category objective: {GetCategoryObjective(command.Category)}");
        builder.AppendLine($"Agent: {agent.DisplayName}, {agent.RoleName}, {agent.Department}");
        builder.AppendLine("Authoritative company facts:");
        builder.AppendLine($"Company name: {company.Name}");
        builder.AppendLine($"Industry: {company.Industry ?? "Not provided"}");
        builder.AppendLine($"Business type: {company.BusinessType ?? "Not provided"}");
        AppendCategorySpecificCompanyFacts(builder, company, command.Category);
        builder.AppendLine("Grounded excerpts from documents attached to this agent and category:");
        if (groundingDocuments.Count == 0)
        {
            builder.AppendLine("None. Do not infer facts from document names or from other company documents.");
        }
        else
        {
            foreach (var document in groundingDocuments)
            {
                builder.AppendLine($"--- Source: {document.Title} ---");
                builder.AppendLine(document.Excerpt);
                builder.AppendLine("--- End source ---");
            }
        }

        builder.AppendLine("Existing editable draft (not authoritative; retain only supported claims):");
        builder.AppendLine(string.IsNullOrWhiteSpace(command.ExistingText)
            ? "None"
            : command.ExistingText.Trim()[..Math.Min(command.ExistingText.Trim().Length, ExistingTextMaxLength)]);
        return builder.ToString();
    }

    internal static IReadOnlyList<AgentBriefGroundingDocument> SelectGroundingDocuments(
        IEnumerable<CompanyKnowledgeDocument> candidates,
        Guid agentId,
        string category) =>
        candidates
            .Where(document =>
                HasMetadataValue(document, "purpose", "agent_brief") &&
                HasMetadataValue(document, "briefingCategory", category) &&
                (HasMetadataValue(document, "agentId", agentId.ToString("D")) ||
                 HasMetadataBoolean(document, "shareWithAgentTeam", true)) &&
                !string.IsNullOrWhiteSpace(document.ExtractedText))
            .GroupBy(BuildGroundingIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(document => document.UpdatedUtc)
                .ThenByDescending(document => document.Id)
                .First())
            .OrderByDescending(document => document.UpdatedUtc)
            .Take(GroundingDocumentLimit)
            .Select(document => new AgentBriefGroundingDocument(
                document.Title,
                Truncate(document.ExtractedText!, GroundingExcerptMaxLength)))
            .ToArray();

    private static string BuildGroundingIdentity(CompanyKnowledgeDocument document) =>
        HasMetadataValue(document, "checksum_sha256", out var checksum)
            ? checksum
            : $"{document.OriginalFileName.Trim().ToLowerInvariant()}:{document.FileSizeBytes}";

    private static bool HasMetadataValue(CompanyKnowledgeDocument document, string key, string expected) =>
        document.Metadata.TryGetValue(key, out var node) &&
        node is System.Text.Json.Nodes.JsonValue value &&
        value.TryGetValue<string>(out var actual) &&
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool HasMetadataValue(CompanyKnowledgeDocument document, string key, out string actual)
    {
        actual = string.Empty;
        if (!document.Metadata.TryGetValue(key, out var node) ||
            node is not System.Text.Json.Nodes.JsonValue value ||
            !value.TryGetValue<string>(out var candidate) ||
            string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        actual = candidate;
        return true;
    }

    private static bool HasMetadataBoolean(CompanyKnowledgeDocument document, string key, bool expected) =>
        document.Metadata.TryGetValue(key, out var node) &&
        node is System.Text.Json.Nodes.JsonValue value &&
        value.TryGetValue<bool>(out var actual) &&
        actual == expected;

    private static void AppendCategorySpecificCompanyFacts(StringBuilder builder, Company company, string category)
    {
        if (string.Equals(category, AgentBriefingCategories.Policies, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"Compliance region: {company.ComplianceRegion ?? "Not provided"}");
        }

        if (string.Equals(category, AgentBriefingCategories.OtherInstructions, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"Operating timezone: {company.Timezone ?? "Not provided"}");
            builder.AppendLine($"Operating currency: {company.Currency ?? "Not provided"}");
        }
    }

    private static string GetCategoryObjective(string category) => category.Trim().ToLowerInvariant() switch
    {
        AgentBriefingCategories.CompanyInformation => "Describe what the company does, who it serves, its market, mission, structure, and important operating facts.",
        AgentBriefingCategories.ProductsAndServices => "Describe only evidenced products and services, target customers, positioning, pricing principles, and limitations.",
        AgentBriefingCategories.Policies => "Summarize evidenced company policies, decision boundaries, approvals, compliance obligations, and escalation rules.",
        AgentBriefingCategories.CustomerSupport => "Summarize evidenced customer promises, response guidance, service standards, and escalation paths.",
        AgentBriefingCategories.OtherInstructions => "Summarize evidenced role-specific context and instructions not covered by the other briefing categories.",
        _ => throw new ArgumentException("A supported briefing category is required.", nameof(category))
    };

    private static string Truncate(string value, int maxLength)
    {
        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    internal sealed record AgentBriefGroundingDocument(string Title, string Excerpt);

}
