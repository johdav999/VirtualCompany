using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class IcpSuggestionService(
    VirtualCompanyDbContext db,
    ICompanyKnowledgeSearchService knowledge,
    IAgentReasoningGateway reasoning) : IIcpSuggestionService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<IcpSuggestionDto> SuggestAsync(
        Guid companyId,
        Guid userId,
        SuggestIcpRequest request,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty || request.AgentId == Guid.Empty)
            throw new LeadGenerationValidationException("A company, user, and Sales agent are required.");

        var agent = await db.Agents.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.AgentId, cancellationToken)
            ?? throw new LeadGenerationValidationException("The Sales agent was not found.");

        if (!string.Equals(agent.Department, "Sales", StringComparison.OrdinalIgnoreCase) ||
            agent.Status != AgentStatus.Active)
            throw new LeadGenerationValidationException("Choose an active Sales agent to suggest an ideal customer profile.");

        var sources = await BuildSourcesAsync(companyId, userId, agent, cancellationToken);
        var focus = NormalizeFocus(request.Focus);
        var instruction = BuildInstruction(focus);

        var result = await reasoning.ReasonAsync(
            new AgentReasoningRequest(
                companyId,
                agent.Id,
                AgentCapabilityIds.SalesLeadIntelligence,
                "1.0.0",
                "sales-icp-suggestion-v1",
                "1.0.0",
                instruction,
                sources,
                ["recommend"],
                [],
                userId,
                IncludeClaims: true),
            cancellationToken);

        if (result.Status is not (AgentAiRunStatuses.Completed or AgentAiRunStatuses.NeedsReview))
            throw new LeadGenerationValidationException(
                result.FailureMessage ?? "Alex could not prepare an ICP suggestion.");

        var payload = ParsePayload(result.Summary);
        ValidatePayload(payload);

        var citedIds = result.SourceIds.ToHashSet(StringComparer.Ordinal);
        var evidence = sources
            .Where(x => citedIds.Contains(x.Id))
            .Select(x => new IcpSuggestionEvidenceDto(x.Id, x.Type, x.Title))
            .ToArray();

        if (evidence.Length == 0)
            throw new LeadGenerationValidationException(
                "Alex returned a suggestion without cited company evidence. Add company or product knowledge and try again.");

        var missing = result.MissingEvidence
            .Concat(payload.MissingEvidence ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var profile = new SaveIcpProfileRequest(
            payload.Name.Trim(),
            NormalizeList(payload.Countries),
            NormalizeList(payload.Industries),
            payload.EmployeeMin,
            payload.EmployeeMax,
            payload.RevenueMin,
            payload.RevenueMax,
            NormalizeList(payload.BuyerRoles),
            NormalizeList(payload.Technologies),
            NormalizeText(payload.PainHypotheses),
            NormalizeText(payload.PositiveCriteria),
            NormalizeText(payload.Disqualifiers));

        return new IcpSuggestionDto(
            result.RunId,
            agent.Id,
            agent.DisplayName,
            profile,
            payload.Rationale.Trim(),
            result.Confidence,
            evidence,
            missing,
            true);
    }

    private async Task<IReadOnlyList<AgentAiSource>> BuildSourcesAsync(
        Guid companyId,
        Guid userId,
        Agent agent,
        CancellationToken cancellationToken)
    {
        var company = await db.Companies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new LeadGenerationValidationException("The company was not found.");

        var sources = new List<AgentAiSource>
        {
            new(
                $"company:{company.Id:N}",
                "company_profile",
                company.Name,
                $"Industry {company.Industry ?? "unknown"}; business type {company.BusinessType ?? "unknown"}; operating region {company.ComplianceRegion ?? "unknown"}; language {company.Language ?? "unknown"}; currency {company.Currency ?? "unknown"}.",
                company.UpdatedUtc)
        };

        AddBriefSource(sources, agent, AgentBriefingCategories.CompanyInformation, "company_brief", "Reviewed company brief");
        AddBriefSource(sources, agent, AgentBriefingCategories.ProductsAndServices, "product_brief", "Reviewed products and services brief");

        var objectives = await db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(8)
            .ToListAsync(cancellationToken);
        foreach (var item in objectives)
        {
            sources.Add(new AgentAiSource(
                $"marketing-objective:{item.Id:N}",
                "market_objective",
                item.Name,
                $"Objective type {item.ObjectiveType}; target {item.TargetValue} {item.Unit}; baseline {item.BaselineValue?.ToString() ?? "unknown"}; status {item.Status}; period {item.PeriodStartUtc:O} to {item.PeriodEndUtc:O}.",
                item.UpdatedUtc));
        }

        var plans = await db.MarketingPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(6)
            .ToListAsync(cancellationToken);
        foreach (var item in plans)
        {
            sources.Add(new AgentAiSource(
                $"marketing-plan:{item.Id:N}",
                "market_plan",
                item.Name,
                Trim(item.Summary, 1200),
                item.UpdatedUtc));
        }

        var audiences = await db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(8)
            .ToListAsync(cancellationToken);
        foreach (var item in audiences)
        {
            sources.Add(new AgentAiSource(
                $"marketing-audience:{item.Id:N}",
                "market_audience",
                item.Title,
                $"Audience {Trim(item.Audience, 700)}; purpose {Trim(item.Purpose, 700)}; channel {item.Channel}; status {item.Status}.",
                item.UpdatedUtc));
        }

        var prospects = await db.ProspectAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                (x.Status == LeadGenerationStatuses.Accepted || x.Status == LeadGenerationStatuses.Converted))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(10)
            .ToListAsync(cancellationToken);
        foreach (var item in prospects)
        {
            sources.Add(new AgentAiSource(
                $"prospect-account:{item.Id:N}",
                "sales_market_evidence",
                item.Name,
                $"Country {item.Country ?? "unknown"}; industry {item.Industry ?? "unknown"}; employees {item.Employees?.ToString() ?? "unknown"}; revenue {item.Revenue?.ToString() ?? "unknown"}; technologies {item.Technologies}; fit score {item.FitScore}; status {item.Status}.",
                item.UpdatedUtc));
        }

        var knowledgeResults = await knowledge.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(
                companyId,
                "company products services value proposition target customers markets industries buyer roles customer problems positioning",
                10,
                new CompanyKnowledgeAccessContext(
                    companyId,
                    UserId: userId,
                    DataScopes: ["sales", "marketing", "knowledge"],
                    AgentId: agent.Id)),
            cancellationToken);

        foreach (var item in knowledgeResults.Where(x => x.Score >= .25d).Take(10))
        {
            sources.Add(new AgentAiSource(
                $"knowledge-chunk:{item.ChunkId:N}",
                "company_knowledge",
                item.DocumentTitle,
                $"Indexed company source; relevance {item.Score:F2}; {Trim(item.Content, 1200)}",
                null));
        }

        return sources.Take(45).ToArray();
    }

    private static void AddBriefSource(
        ICollection<AgentAiSource> sources,
        Agent agent,
        string category,
        string type,
        string title)
    {
        if (!agent.CommunicationProfile.TryGetValue("briefing", out var node) ||
            node is not JsonObject briefing ||
            briefing[category] is not JsonValue value ||
            !value.TryGetValue<string>(out var content) ||
            string.IsNullOrWhiteSpace(content))
            return;

        sources.Add(new AgentAiSource(
            $"agent-brief:{agent.Id:N}:{category}",
            type,
            title,
            Trim(content, 1800),
            agent.UpdatedUtc));
    }

    private static string BuildInstruction(string? focus) =>
        """
        Act as the company's Sales agent and propose one reviewable ideal customer profile from the supplied company, product, and market evidence. Do not invent products, customer wins, market demand, countries, industries, technologies, or buying roles. Prefer reviewed company and product brief content over inferred market patterns. Use accepted or converted prospects only as observed market evidence, not proof of causation. Identify unknowns explicitly.

        Put a compact JSON object in the outer response's summary string, with exactly these fields:
        {"name":"string","countries":"comma-separated string","industries":"comma-separated string","employeeMin":integer|null,"employeeMax":integer|null,"revenueMin":number|null,"revenueMax":number|null,"buyerRoles":"comma-separated string","technologies":"comma-separated string","painHypotheses":"string","positiveCriteria":"string","disqualifiers":"string","rationale":"string","missingEvidence":["string"]}

        The profile must have a useful name and at least one country, industry, or buyer role. Keep each prose field concise and practical. Add confirmed_fact or inference claims with valid source IDs for the evidence that materially supports the suggestion. Recommend only this draft for human review; do not save, activate, search for prospects, or contact anyone.
        """ + (focus is null ? string.Empty : $"\nUser focus: {focus}");

    private static SuggestionPayload ParsePayload(string summary)
    {
        try
        {
            return JsonSerializer.Deserialize<SuggestionPayload>(summary, JsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new LeadGenerationValidationException(
                "Alex returned an ICP suggestion in an unexpected format. Try generating it again.");
        }
    }

    private static void ValidatePayload(SuggestionPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Name) ||
            (string.IsNullOrWhiteSpace(payload.Countries) &&
             string.IsNullOrWhiteSpace(payload.Industries) &&
             string.IsNullOrWhiteSpace(payload.BuyerRoles)))
            throw new LeadGenerationValidationException(
                "Alex returned an incomplete ICP suggestion. Add more company or product context and try again.");

        if (payload.EmployeeMin is < 0 || payload.EmployeeMax is < 0 || payload.EmployeeMin > payload.EmployeeMax ||
            payload.RevenueMin is < 0 || payload.RevenueMax is < 0 || payload.RevenueMin > payload.RevenueMax)
            throw new LeadGenerationValidationException("Alex returned an invalid employee or revenue range.");
    }

    private static string? NormalizeFocus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized[..Math.Min(normalized.Length, 500)];
    }

    private static string NormalizeList(string? value) =>
        string.Join(", ", (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string NormalizeText(string? value) => value?.Trim() ?? string.Empty;

    private static string Trim(string value, int max)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized[..Math.Min(normalized.Length, max)];
    }

    private sealed class SuggestionPayload
    {
        public string Name { get; set; } = string.Empty;
        public string Countries { get; set; } = string.Empty;
        public string Industries { get; set; } = string.Empty;
        public int? EmployeeMin { get; set; }
        public int? EmployeeMax { get; set; }
        public decimal? RevenueMin { get; set; }
        public decimal? RevenueMax { get; set; }
        public string BuyerRoles { get; set; } = string.Empty;
        public string Technologies { get; set; } = string.Empty;
        public string PainHypotheses { get; set; } = string.Empty;
        public string PositiveCriteria { get; set; } = string.Empty;
        public string Disqualifiers { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
        public List<string>? MissingEvidence { get; set; }
    }
}