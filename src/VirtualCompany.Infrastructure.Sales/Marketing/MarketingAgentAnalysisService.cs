using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingAgentAnalysisService(
    VirtualCompanyDbContext db,
    ICompanyKnowledgeSearchService knowledge,
    IAgentReasoningGateway reasoning) : IMarketingAgentAnalysisService
{
    public async Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken ct)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty || !MarketingAgentAnalysisTypes.All.Contains(request.AnalysisType))
            throw new ArgumentException("A valid company, Marketing agent, and analysis type are required.");
        var now = request.AsOfUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var horizon = Math.Clamp(request.HorizonDays, 1, 365);
        var sources = new List<AgentAiSource>();
        var metrics = new List<RoleAgentMetric>();
        var priorities = new List<RoleAgentPriority>();
        var missing = new List<string>();

        var objectives = await db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PeriodEndUtc >= now.AddDays(-30))
            .OrderBy(x => x.PeriodEndUtc).Take(20).ToListAsync(ct);
        foreach (var item in objectives)
        {
            var sourceId = $"marketing-objective:{item.Id:N}";
            sources.Add(new AgentAiSource(sourceId, "marketing_objective", item.Name,
                $"Type {item.ObjectiveType}; baseline {item.BaselineValue?.ToString() ?? "unknown"}; target {item.TargetValue} {item.Unit}; period {item.PeriodStartUtc:O} to {item.PeriodEndUtc:O}; status {item.Status}.", item.UpdatedUtc));
            priorities.Add(new RoleAgentPriority("marketing_objective", item.Id, item.Name,
                item.Status == "active" ? 70 : 35, item.Status,
                [item.BaselineValue.HasValue ? "baseline_available" : "baseline_missing"], sourceId));
        }
        if (objectives.Count == 0) missing.Add("Current marketing objectives");

        var campaigns = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.UpdatedUtc).Take(20).ToListAsync(ct);
        foreach (var item in campaigns)
        {
            var sourceId = $"sales-campaign:{item.Id:N}";
            sources.Add(new AgentAiSource(sourceId, "sales_campaign", item.Name,
                $"Status {item.Status}; audience {item.AudienceType}; approval {item.ApprovalStatus ?? "not set"}; scheduled launch {item.ScheduledLaunchUtc?.ToString("O") ?? "not set"}; outbound enabled {item.OutboundEnabled}.", item.UpdatedUtc));
            priorities.Add(new RoleAgentPriority("sales_campaign", item.Id, item.Name,
                item.Status == "waiting_for_approval" ? 85 : item.Status == "active" ? 65 : 30,
                item.Status, [item.ApprovalRequired ? "approval_policy_applies" : "approval_not_required"], sourceId));
        }

        var observations = await db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PeriodEndUtc >= now.AddDays(-horizon))
            .OrderByDescending(x => x.PeriodEndUtc).Take(50).ToListAsync(ct);
        foreach (var group in observations.GroupBy(x => new { x.MetricCode, x.Unit }))
        {
            var latest = group.OrderByDescending(x => x.PeriodEndUtc).First();
            var sourceId = $"marketing-observation:{latest.Id:N}";
            sources.Add(new AgentAiSource(sourceId, "marketing_channel_observation", latest.MetricCode,
                $"Observed {latest.Value} {latest.Unit} from {latest.Provider}; period {latest.PeriodStartUtc:O} to {latest.PeriodEndUtc:O}; source {latest.SourceReference}.", latest.RetrievedUtc));
            metrics.Add(new RoleAgentMetric(latest.MetricCode, latest.MetricCode.Replace('_', ' '),
                latest.Value, latest.Unit, sourceId, latest.PeriodEndUtc));
        }
        if (request.AnalysisType is MarketingAgentAnalysisTypes.PerformanceAnalysis or MarketingAgentAnalysisTypes.OperatingCadence &&
            observations.Count == 0) missing.Add("Source-linked channel observations");

        var content = await db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (x.Status == "draft" || x.Status == "submitted"))
            .OrderBy(x => x.DueUtc).Take(20).ToListAsync(ct);
        foreach (var item in content)
        {
            var sourceId = $"marketing-content:{item.Id:N}";
            sources.Add(new AgentAiSource(sourceId, "marketing_content_brief", item.Title,
                $"Purpose {item.Purpose}; audience {item.Audience}; channel {item.Channel}; language {item.Language}; CTA {item.CallToAction}; status {item.Status}; due {item.DueUtc?.ToString("O") ?? "not set"}.", item.UpdatedUtc));
            priorities.Add(new RoleAgentPriority("marketing_content", item.Id, item.Title,
                item.Status == "submitted" ? 80 : item.DueUtc < now.AddDays(3) ? 70 : 40,
                item.Status, [item.Status == "submitted" ? "human_review_required" : "draft_not_submitted"], sourceId));
        }

        if (request.AnalysisType is MarketingAgentAnalysisTypes.ContentAdvice or MarketingAgentAnalysisTypes.Planning or MarketingAgentAnalysisTypes.OperatingCadence)
        {
            var query = string.IsNullOrWhiteSpace(request.Objective)
                ? "company products services brand policies audience marketing claims"
                : request.Objective.Trim();
            var results = await knowledge.SearchAsync(new CompanyKnowledgeSemanticSearchQuery(companyId, query, 10,
                new CompanyKnowledgeAccessContext(companyId, DataScopes: ["marketing", "sales", "knowledge"])), ct);
            foreach (var item in results.Where(x => x.Score >= .25d).Take(10))
                sources.Add(new AgentAiSource($"knowledge-chunk:{item.ChunkId:N}", "company_knowledge", item.DocumentTitle,
                    $"Indexed company source; relevance {item.Score:F2}; {Trim(item.Content, 1200)}", null));
            if (results.Count == 0) missing.Add("Accessible product, policy, and brand knowledge");
        }

        if (sources.Count == 0)
            sources.Add(new AgentAiSource("marketing-state:empty", "marketing_state", "Marketing evidence state",
                "No authoritative Marketing records matched this bounded analysis request.", now));
        var capability = CapabilityId(request.AnalysisType);
        var result = await reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, capability, "1.0.0",
            $"marketing-role-v1:{NormalizeCadence(request.Cadence)}", "1.0.0",
            $"Act as a Marketing analysis adviser. Analyze '{request.AnalysisType}' over {horizon} days. Separate observed facts, attributed outcomes, inferences, and missing evidence. Recommend only internal review actions. Never publish content, spend budget, contact a person, launch a campaign, or modify Sales state. Objective: {request.Objective ?? "none"}.",
            sources.Take(60).ToArray(), ["recommend"], [], actorUserId), ct);
        var allMissing = missing.Concat(result.MissingEvidence).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new RoleAgentAnalysisResult(result.RunId, capability, result.Status, result.Summary, result.Confidence,
            now, metrics, priorities.OrderByDescending(x => x.Score).Take(30).ToArray(), result.Claims,
            sources.Take(60).ToArray(), allMissing, result.NextActions,
            result.Status != AgentAiRunStatuses.Completed || allMissing.Length > 0);
    }

    private static string CapabilityId(string type) => type.Trim().ToLowerInvariant() switch
    {
        MarketingAgentAnalysisTypes.Planning => AgentCapabilityIds.MarketingPlanning,
        MarketingAgentAnalysisTypes.AudienceIntelligence => AgentCapabilityIds.MarketingAudienceIntelligence,
        MarketingAgentAnalysisTypes.ContentAdvice => AgentCapabilityIds.MarketingContentAdvice,
        MarketingAgentAnalysisTypes.CampaignCoordination => AgentCapabilityIds.MarketingCampaignCoordination,
        MarketingAgentAnalysisTypes.PerformanceAnalysis => AgentCapabilityIds.MarketingPerformanceAnalysis,
        MarketingAgentAnalysisTypes.ExperimentAdvice => AgentCapabilityIds.MarketingExperimentAdvice,
        MarketingAgentAnalysisTypes.OperatingCadence => AgentCapabilityIds.MarketingOperatingCadence,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    private static string NormalizeCadence(string? value) => value?.Trim().ToLowerInvariant() is "daily" or "weekly" or "monthly" ? value.Trim().ToLowerInvariant() : "on_demand";
    private static string Trim(string value, int max) { var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); return text.Length <= max ? text : text[..max]; }
}
