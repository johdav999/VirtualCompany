using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesAgentAnalysisService(
    VirtualCompanyDbContext db,
    ICompanyKnowledgeSearchService knowledge,
    IAgentReasoningGateway reasoning) : ISalesAgentAnalysisService
{
    public async Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken cancellationToken)
    {
        Validate(companyId, agentId, request);
        var now = request.AsOfUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var horizon = Math.Clamp(request.HorizonDays, 1, 365);
        var evidence = await BuildEvidenceAsync(companyId, request, now, horizon, cancellationToken);
        var capabilityId = CapabilityId(request.AnalysisType);
        var result = await reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, capabilityId, "1.0.0",
            $"sales-role-v1:{NormalizeCadence(request.Cadence)}", "1.0.0", Instruction(request.AnalysisType, horizon, request.Objective), evidence.Sources,
            ["recommend"], [], actorUserId), cancellationToken);
        var missing = evidence.Missing.Concat(result.MissingEvidence).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new RoleAgentAnalysisResult(result.RunId, capabilityId, result.Status, result.Summary, result.Confidence,
            now, evidence.Metrics, evidence.Priorities, result.Claims, evidence.Sources, missing, result.NextActions,
            result.Status != AgentAiRunStatuses.Completed || missing.Length > 0);
    }

    private async Task<Evidence> BuildEvidenceAsync(Guid companyId, RoleAgentAnalysisRequest request, DateTime now,
        int horizon, CancellationToken ct)
    {
        var type = request.AnalysisType.Trim().ToLowerInvariant();
        var sources = new List<AgentAiSource>();
        var metrics = new List<RoleAgentMetric>();
        var priorities = new List<RoleAgentPriority>();
        var missing = new List<string>();

        if (type is SalesAgentAnalysisTypes.LeadIntelligence or SalesAgentAnalysisTypes.NextBestAction or SalesAgentAnalysisTypes.OperatingCadence)
        {
            var leads = await db.Leads.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.UpdatedUtc).Take(30).ToListAsync(ct);
            foreach (var lead in leads)
            {
                var ageDays = Math.Max(0, (now.Date - lead.UpdatedUtc.Date).Days);
                var score = Math.Clamp(35 + (lead.Priority?.Equals("high", StringComparison.OrdinalIgnoreCase) == true ? 25 : 0) +
                    (lead.Temperature?.Equals("hot", StringComparison.OrdinalIgnoreCase) == true ? 25 : 0) - Math.Min(ageDays, 25), 0, 100);
                var sourceId = $"sales-lead:{lead.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "sales_lead", lead.Title,
                    $"Status {lead.Status}; value {lead.EstimatedValue?.ToString() ?? "unknown"} {lead.Currency ?? "unknown"}; fit {lead.Fit ?? "unknown"}; temperature {lead.Temperature ?? "unknown"}; priority {lead.Priority ?? "unknown"}; reviewed next action {lead.SuggestedNextAction ?? "none"}.", lead.UpdatedUtc));
                priorities.Add(new RoleAgentPriority("lead", lead.Id, lead.Title, score, score >= 75 ? "high" : score >= 50 ? "medium" : "low",
                    [ageDays > 7 ? "stale_activity" : "recent_activity", lead.Temperature is null ? "qualification_incomplete" : "qualification_available"], sourceId));
            }
            if (leads.Count == 0) missing.Add("Current leads");
        }

        if (type is SalesAgentAnalysisTypes.DealRisk or SalesAgentAnalysisTypes.NextBestAction or SalesAgentAnalysisTypes.ProposalAdvice or SalesAgentAnalysisTypes.OperatingCadence)
        {
            var dealsQuery = db.Deals.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.DeletedUtc == null);
            if (request.SubjectId.HasValue && type == SalesAgentAnalysisTypes.ProposalAdvice)
                dealsQuery = dealsQuery.Where(x => x.Id == request.SubjectId.Value);
            var deals = await dealsQuery.OrderBy(x => x.ExpectedCloseUtc).Take(30).ToListAsync(ct);
            var dealIds = deals.Select(x => x.Id).ToArray();
            var risk = await db.DealRiskScoreSnapshots.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && dealIds.Contains(x.DealId))
                .OrderByDescending(x => x.CalculatedUtc).ToListAsync(ct);
            foreach (var deal in deals)
            {
                var latestRisk = risk.FirstOrDefault(x => x.DealId == deal.Id);
                var closeOverdue = deal.ExpectedCloseUtc.HasValue && deal.ExpectedCloseUtc.Value < now;
                var score = latestRisk is null ? (closeOverdue ? 80 : 45) : Math.Clamp((int)Math.Round(latestRisk.Score * 100m), 0, 100);
                var sourceId = $"sales-deal:{deal.Id:N}";
                var riskText = latestRisk is null ? "no authoritative risk snapshot" : $"risk {latestRisk.Band} ({latestRisk.Score}); factors {latestRisk.FactorsSummary}";
                sources.Add(new AgentAiSource(sourceId, "sales_deal", deal.Title,
                    $"Amount {deal.Amount} {deal.Currency}; status {deal.Status}; expected close {deal.ExpectedCloseUtc?.ToString("O") ?? "unknown"}; {riskText}.", latestRisk?.CalculatedUtc ?? deal.UpdatedUtc));
                priorities.Add(new RoleAgentPriority("deal", deal.Id, deal.Title, score, score >= 75 ? "high_risk" : score >= 50 ? "review" : "monitor",
                    [latestRisk is null ? "risk_snapshot_missing" : "authoritative_risk_snapshot", closeOverdue ? "expected_close_overdue" : "close_date_current"], sourceId));
            }
            if (request.SubjectId.HasValue && deals.Count == 0) throw new KeyNotFoundException("Sales deal not found.");
        }

        if (type is SalesAgentAnalysisTypes.ForecastAnalysis or SalesAgentAnalysisTypes.OperatingCadence)
        {
            var forecasts = await db.RevenueForecastSnapshots.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.AsOfUtc <= now)
                .OrderByDescending(x => x.AsOfUtc).Take(10).ToListAsync(ct);
            foreach (var forecast in forecasts.GroupBy(x => x.Currency).Select(x => x.First()))
            {
                var sourceId = $"sales-forecast:{forecast.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "sales_forecast", $"Revenue forecast {forecast.Currency}",
                    $"30d gross {forecast.GrossPipeline30Days}; expected {forecast.ExpectedRevenue30Days}; deals {forecast.DealCount30Days}. 60d expected {forecast.ExpectedRevenue60Days}. 90d expected {forecast.ExpectedRevenue90Days}. High-risk deals {forecast.HighRiskDeals}; unknown-risk deals {forecast.UnknownRiskDeals}.", forecast.CalculatedUtc));
                metrics.Add(new RoleAgentMetric($"expected_revenue_30d_{forecast.Currency.ToLowerInvariant()}", $"Expected revenue, 30 days ({forecast.Currency})",
                    forecast.ExpectedRevenue30Days, forecast.Currency, sourceId, forecast.AsOfUtc));
                if (forecast.CalculatedUtc < now.AddDays(-2)) missing.Add($"Fresh {forecast.Currency} revenue forecast");
            }
            if (forecasts.Count == 0) missing.Add("Revenue forecast snapshot");
        }

        if (type is SalesAgentAnalysisTypes.CampaignOptimization or SalesAgentAnalysisTypes.OperatingCadence)
        {
            var campaigns = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.UpdatedUtc).Take(20).ToListAsync(ct);
            foreach (var campaign in campaigns)
            {
                var sourceId = $"sales-campaign:{campaign.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "sales_campaign", campaign.Name,
                    $"Status {campaign.Status}; audience {campaign.AudienceType}; outbound enabled {campaign.OutboundEnabled}; daily cap {campaign.MaxEmailsPerDay}; approval required {campaign.ApprovalRequired}; approval status {campaign.ApprovalStatus ?? "not set"}.", campaign.UpdatedUtc));
                priorities.Add(new RoleAgentPriority("sales_campaign", campaign.Id, campaign.Name,
                    campaign.Status == "waiting_for_approval" ? 80 : campaign.Status == "active" ? 55 : 25,
                    campaign.Status, [campaign.ApprovalRequired ? "approval_policy_applies" : "approval_not_required"], sourceId));
            }
        }

        if (type is SalesAgentAnalysisTypes.ProposalAdvice or SalesAgentAnalysisTypes.CampaignOptimization)
        {
            var queryText = string.IsNullOrWhiteSpace(request.Objective)
                ? "products services pricing proposal terms sales policy"
                : request.Objective.Trim();
            var results = await knowledge.SearchAsync(new CompanyKnowledgeSemanticSearchQuery(companyId, queryText, 8,
                new CompanyKnowledgeAccessContext(companyId, DataScopes: ["sales", "knowledge"])), ct);
            foreach (var item in results.Where(x => x.Score >= .25d).Take(8))
            {
                sources.Add(new AgentAiSource($"knowledge-chunk:{item.ChunkId:N}", "company_knowledge", item.DocumentTitle,
                    $"Processed indexed company source; relevance {item.Score:F2}; {Trim(item.Content, 1200)}", null));
            }
            if (results.Count == 0) missing.Add("Accessible product, pricing, and sales policy knowledge");
        }

        if (sources.Count == 0)
            sources.Add(new AgentAiSource("sales-state:empty", "sales_state", "Sales evidence state", "No authoritative records matched this bounded analysis request.", now));
        var boundedSources = sources.Take(50).ToArray();
        var sourceIds = boundedSources.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return new Evidence(boundedSources, metrics.Where(x => sourceIds.Contains(x.SourceId)).ToArray(),
            priorities.Where(x => sourceIds.Contains(x.SourceId)).OrderByDescending(x => x.Score).Take(30).ToArray(), missing);
    }

    private static string Instruction(string type, int horizon, string? objective) =>
        $"Act as a Sales analysis adviser. Analyze '{type}' over {horizon} days. Treat pipeline, forecast, risk, consent, campaign, approval, and delivery state as authoritative and immutable. Separate facts, inferences, and unknowns. Recommend only internal review actions; never send, launch, change terms, or claim customer intent without evidence. Objective: {objective ?? "none"}.";

    private static string Trim(string value, int max) { var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); return normalized.Length <= max ? normalized : normalized[..max]; }

    private static string CapabilityId(string type) => type.Trim().ToLowerInvariant() switch
    {
        SalesAgentAnalysisTypes.LeadIntelligence => AgentCapabilityIds.SalesLeadIntelligence,
        SalesAgentAnalysisTypes.NextBestAction => AgentCapabilityIds.SalesNextBestAction,
        SalesAgentAnalysisTypes.DealRisk => AgentCapabilityIds.SalesDealRisk,
        SalesAgentAnalysisTypes.ForecastAnalysis => AgentCapabilityIds.SalesForecastAnalysis,
        SalesAgentAnalysisTypes.CampaignOptimization => AgentCapabilityIds.SalesCampaignOptimization,
        SalesAgentAnalysisTypes.ProposalAdvice => AgentCapabilityIds.SalesProposalAdvice,
        SalesAgentAnalysisTypes.OperatingCadence => AgentCapabilityIds.SalesOperatingCadence,
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported Sales analysis type.")
    };

    private static void Validate(Guid companyId, Guid agentId, RoleAgentAnalysisRequest request)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        if (string.IsNullOrWhiteSpace(request.AnalysisType) || !SalesAgentAnalysisTypes.All.Contains(request.AnalysisType))
            throw new ArgumentOutOfRangeException(nameof(request), "Unsupported Sales analysis type.");
    }

    private static string NormalizeCadence(string? value) => value?.Trim().ToLowerInvariant() is "daily" or "weekly" ? value.Trim().ToLowerInvariant() : "on_demand";

    private sealed record Evidence(IReadOnlyList<AgentAiSource> Sources, IReadOnlyList<RoleAgentMetric> Metrics,
        IReadOnlyList<RoleAgentPriority> Priorities, IReadOnlyList<string> Missing);
}
