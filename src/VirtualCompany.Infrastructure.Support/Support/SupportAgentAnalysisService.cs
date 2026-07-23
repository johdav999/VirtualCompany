using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Support;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportAgentAnalysisService(
    VirtualCompanyDbContext db,
    ISupportKnowledgeContextProvider knowledge,
    ISupportAnalyticsService analytics,
    IAgentReasoningGateway reasoning) : ISupportAgentAnalysisService
{
    public async Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken cancellationToken)
    {
        Validate(companyId, agentId, request);
        var now = request.AsOfUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var evidence = await BuildEvidenceAsync(companyId, request, now, cancellationToken);
        var capabilityId = CapabilityId(request.AnalysisType);
        var result = await reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, capabilityId, "1.0.0",
            $"support-role-v1:{NormalizeCadence(request.Cadence)}", "1.0.0", Instruction(request.AnalysisType, request.Objective), evidence.Sources,
            ["recommend"], [], actorUserId), cancellationToken);
        var missing = evidence.Missing.Concat(result.MissingEvidence).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var severe = evidence.Priorities.Any(x => x.ReasonCodes.Contains("mandatory_escalation"));
        return new RoleAgentAnalysisResult(result.RunId, capabilityId, result.Status, result.Summary, result.Confidence,
            now, evidence.Metrics, evidence.Priorities, result.Claims, evidence.Sources, missing, result.NextActions,
            severe || result.Status != AgentAiRunStatuses.Completed || missing.Length > 0);
    }

    private async Task<Evidence> BuildEvidenceAsync(Guid companyId, RoleAgentAnalysisRequest request, DateTime now, CancellationToken ct)
    {
        var type = request.AnalysisType.Trim().ToLowerInvariant();
        var sources = new List<AgentAiSource>();
        var metrics = new List<RoleAgentMetric>();
        var priorities = new List<RoleAgentPriority>();
        var missing = new List<string>();

        if (type is SupportAgentAnalysisTypes.TriageAnalysis or SupportAgentAnalysisTypes.RiskEscalation or SupportAgentAnalysisTypes.OperatingCadence)
        {
            var query = db.SupportCases.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.Status != "closed");
            if (request.SubjectId.HasValue && type != SupportAgentAnalysisTypes.OperatingCadence)
                query = query.Where(x => x.Id == request.SubjectId.Value);
            var cases = await query.OrderBy(x => x.ResolutionDueUtc).ThenBy(x => x.CreatedUtc).Take(30).ToListAsync(ct);
            foreach (var supportCase in cases)
            {
                var severe = supportCase.Category is "security" or "privacy" or "legal" || supportCase.IsSlaBreached;
                var minutesToDeadline = supportCase.ResolutionDueUtc.HasValue ? (supportCase.ResolutionDueUtc.Value - now).TotalMinutes : double.MaxValue;
                var score = Math.Clamp((severe ? 100 : 25) + (supportCase.IsSlaRisk ? 35 : 0) +
                    (supportCase.Priority is "urgent" ? 30 : supportCase.Priority is "high" ? 20 : 0) +
                    (minutesToDeadline <= 240 ? 20 : 0), 0, 100);
                var sourceId = $"support-case:{supportCase.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "support_case", $"Case {supportCase.CaseNumber}: {supportCase.Subject}",
                    $"Status {supportCase.Status}; category {supportCase.Category}; priority {supportCase.Priority}; sentiment {supportCase.Sentiment ?? "unknown"}; SLA risk {supportCase.IsSlaRisk}; breached {supportCase.IsSlaBreached}; churn risk {supportCase.IsChurnRisk}; first-response due {supportCase.FirstResponseDueUtc?.ToString("O") ?? "unset"}; resolution due {supportCase.ResolutionDueUtc?.ToString("O") ?? "unset"}.", supportCase.UpdatedUtc));
                var reasons = new List<string>();
                if (severe) reasons.Add("mandatory_escalation");
                if (supportCase.IsSlaRisk) reasons.Add("sla_risk");
                if (supportCase.IsChurnRisk) reasons.Add("churn_hypothesis");
                if (!supportCase.ContactId.HasValue) reasons.Add("customer_context_missing");
                priorities.Add(new RoleAgentPriority("support_case", supportCase.Id, supportCase.Subject, score,
                    severe ? "critical" : score >= 75 ? "urgent" : score >= 50 ? "high" : "normal", reasons, sourceId));
            }
            if (request.SubjectId.HasValue && cases.Count == 0) throw new KeyNotFoundException("Support case not found.");
        }

        if (type == SupportAgentAnalysisTypes.GroundedReply)
        {
            if (!request.SubjectId.HasValue) missing.Add("Support case subject ID");
            else
            {
                var context = await knowledge.RetrieveAsync(companyId, request.SubjectId.Value, ct);
                foreach (var item in context.Sources.Take(20))
                {
                    var id = item.EntityId.HasValue ? $"{item.Type}:{item.EntityId:N}" : $"{item.Type}:{sources.Count + 1}";
                    sources.Add(new AgentAiSource(id, item.Type, item.Label,
                        $"Trust state: {(item.IsTrusted ? "trusted" : "context only")}; relevance {item.Relevance}; excerpt: {item.Excerpt ?? "none"}", now));
                }
                metrics.Add(new RoleAgentMetric("answerability", "Grounding confidence", context.RetrievalConfidence, "ratio",
                    sources.FirstOrDefault()?.Id ?? "support-grounding:none", now));
                if (!context.HasTrustedGrounding) missing.Add("Processed, indexed, accessible Support knowledge");
            }
        }

        if (type is SupportAgentAnalysisTypes.RootCauseAnalysis or SupportAgentAnalysisTypes.OperatingCadence)
        {
            var dashboard = await analytics.GetDashboardAsync(companyId, ct);
            foreach (var insight in dashboard.Insights.Take(15))
            {
                var id = $"support-root-cause:{NormalizeId(insight.Category)}:{sources.Count + 1}";
                sources.Add(new AgentAiSource(id, "support_analytics", insight.Title,
                    $"Category {insight.Category}; case count {insight.CaseCount}; deterministic summary {insight.Summary}; suggested investigation {insight.SuggestedAction}.", now));
                metrics.Add(new RoleAgentMetric($"cases_{NormalizeId(insight.Category)}", $"Cases: {insight.Category}", insight.CaseCount, "cases", id, now));
            }
            var slaId = "support-analytics:sla";
            sources.Add(new AgentAiSource(slaId, "support_analytics", "SLA performance",
                $"Open at risk {dashboard.SlaPerformance.OpenAtRisk}; open breached {dashboard.SlaPerformance.OpenBreached}; first responses missed {dashboard.SlaPerformance.FirstResponsesMissed}; resolutions missed {dashboard.SlaPerformance.ResolutionsMissed}; {dashboard.SlaPerformance.Rationale}", now));
        }

        if (type is SupportAgentAnalysisTypes.KnowledgeCoverage or SupportAgentAnalysisTypes.OperatingCadence)
        {
            var gaps = await db.SupportKnowledgeGaps.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.FrequencyCount).ThenByDescending(x => x.UpdatedUtc).Take(20).ToListAsync(ct);
            foreach (var gap in gaps)
            {
                var sourceId = $"support-knowledge-gap:{gap.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "support_knowledge_gap", gap.QuestionSummary,
                    $"Category {gap.Category}; status {gap.Status}; frequency {gap.FrequencyCount}; missing information {gap.MissingInformationSummary}; linked document {(gap.LinkedKnowledgeDocumentId?.ToString("N") ?? "none")}.", gap.UpdatedUtc));
                priorities.Add(new RoleAgentPriority("support_knowledge_gap", gap.Id, gap.QuestionSummary,
                    Math.Clamp(30 + gap.FrequencyCount * 10, 0, 100), gap.Status,
                    [gap.Status == "resolved" ? "review_resolution_grounding" : "documentation_gap"], sourceId));
            }
            if (gaps.Count == 0) missing.Add("Recorded Support knowledge-gap outcomes");
        }

        if (sources.Count == 0)
            sources.Add(new AgentAiSource("support-state:empty", "support_state", "Support evidence state", "No authoritative records matched this bounded analysis request.", now));
        var boundedSources = sources.Take(50).ToArray();
        var sourceIds = boundedSources.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return new Evidence(boundedSources, metrics.Where(x => sourceIds.Contains(x.SourceId)).ToArray(),
            priorities.Where(x => sourceIds.Contains(x.SourceId)).OrderByDescending(x => x.Score).Take(30).ToArray(), missing);
    }

    private static string Instruction(string type, string? objective) =>
        $"Act as a Support analysis adviser. Analyze '{type}'. Case status, identity, SLA deadlines, access, safety, refund eligibility, approval, and delivery state are authoritative and immutable. Treat only explicitly trusted knowledge as customer-facing evidence. Mark sentiment, churn, similarity, and root cause as hypotheses unless confirmed. Recommend review or escalation only; never send, promise, refund, merge, or disclose another customer's data. Objective: {objective ?? "none"}.";

    private static string CapabilityId(string type) => type.Trim().ToLowerInvariant() switch
    {
        SupportAgentAnalysisTypes.TriageAnalysis => AgentCapabilityIds.SupportTriageAnalysis,
        SupportAgentAnalysisTypes.GroundedReply => AgentCapabilityIds.SupportGroundedReply,
        SupportAgentAnalysisTypes.RiskEscalation => AgentCapabilityIds.SupportRiskEscalation,
        SupportAgentAnalysisTypes.RootCauseAnalysis => AgentCapabilityIds.SupportRootCauseAnalysis,
        SupportAgentAnalysisTypes.KnowledgeCoverage => AgentCapabilityIds.SupportKnowledgeCoverage,
        SupportAgentAnalysisTypes.OperatingCadence => AgentCapabilityIds.SupportOperatingCadence,
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported Support analysis type.")
    };

    private static string NormalizeId(string value) => new(value.ToLowerInvariant().Select(x => char.IsLetterOrDigit(x) ? x : '_').ToArray());

    private static void Validate(Guid companyId, Guid agentId, RoleAgentAnalysisRequest request)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        if (string.IsNullOrWhiteSpace(request.AnalysisType) || !SupportAgentAnalysisTypes.All.Contains(request.AnalysisType))
            throw new ArgumentOutOfRangeException(nameof(request), "Unsupported Support analysis type.");
    }

    private static string NormalizeCadence(string? value) => value?.Trim().ToLowerInvariant() is "daily" or "weekly" ? value.Trim().ToLowerInvariant() : "on_demand";

    private sealed record Evidence(IReadOnlyList<AgentAiSource> Sources, IReadOnlyList<RoleAgentMetric> Metrics,
        IReadOnlyList<RoleAgentPriority> Priorities, IReadOnlyList<string> Missing);
}
