using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Documents;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentCapabilityCatalog : IAgentCapabilityCatalog
{
    private const string KnowledgeIndexingSignal = "knowledge_indexing_enabled";
    private const string BriefingSchedulerSignal = "briefing_scheduler_enabled";
    private const string SharedAiProviderSignal = "shared_ai_provider";

    private static readonly IReadOnlyList<AgentCapabilityManifest> Manifests =
    [
        new(
            AgentCapabilityIds.GroundedQuestionAnswering,
            "1.0.0",
            "Grounded questions",
            "Answer company questions using permitted records and indexed documents with source references.",
            "Knowledge",
            ToolActionType.Read,
            ["knowledge.search"],
            ["knowledge"],
            [KnowledgeIndexingSignal, SharedAiProviderSignal],
            AgentAutonomyLevel.Level0,
            "none",
            true),
        new(
            AgentCapabilityIds.RoleBriefing,
            "1.0.0",
            "Role briefings",
            "Prepare role-specific daily, weekly, and event-driven briefings from current company work.",
            "Briefings",
            ToolActionType.Read,
            ["tasks.list"],
            ["tasks"],
            [BriefingSchedulerSignal],
            AgentAutonomyLevel.Level0,
            "none",
            true),
        new(
            AgentCapabilityIds.WorkPrioritization,
            "1.0.0",
            "Work prioritization",
            "Rank permitted work using deterministic deadlines, risk signals, impact, and dependencies.",
            "Work management",
            ToolActionType.Recommend,
            ["tasks.list"],
            ["tasks"],
            [],
            AgentAutonomyLevel.Level0,
            "none",
            true),
        new(
            AgentCapabilityIds.Planning,
            "1.0.0",
            "Planning",
            "Turn an objective into a bounded, reviewable plan of durable tasks and approvals.",
            "Work management",
            ToolActionType.Recommend,
            ["tasks.list"],
            ["tasks"],
            [SharedAiProviderSignal],
            AgentAutonomyLevel.Level0,
            "commit_requires_review",
            true),
        new(
            AgentCapabilityIds.ExceptionInterpretation,
            "1.0.0",
            "Exception interpretation",
            "Explain anomalies and failed workflows with evidence-backed hypotheses and safe next steps.",
            "Operations",
            ToolActionType.Recommend,
            [],
            [],
            [SharedAiProviderSignal],
            AgentAutonomyLevel.Level0,
            "none",
            true),
        new(
            AgentCapabilityIds.CrossAgentHandoff,
            "1.0.0",
            "Cross-agent handoffs",
            "Request a bounded outcome from another agent with scoped evidence and durable ownership.",
            "Collaboration",
            ToolActionType.Recommend,
            ["tasks.list"],
            ["tasks"],
            [],
            AgentAutonomyLevel.Level1,
            "sensitive_handoffs_require_review",
            true),
        new(
            AgentCapabilityIds.MemoryProposal,
            "1.0.0",
            "Memory proposals",
            "Propose bounded reusable observations for deterministic review before activation.",
            "Learning",
            ToolActionType.Recommend,
            [],
            ["knowledge"],
            [],
            AgentAutonomyLevel.Level0,
            "policy_dependent",
            true),
        RoleCapability(AgentCapabilityIds.FinanceCashLiquidity, "Cash and liquidity advice", "Explain authoritative cash, obligations, runway pressure, and scenarios.", "Finance"),
        RoleCapability(AgentCapabilityIds.FinancePayables, "Payables advice", "Prioritize supplier obligations within deterministic payment and approval policy.", "Finance"),
        RoleCapability(AgentCapabilityIds.FinanceReceivables, "Collections advice", "Prioritize receivables and explain collection risk from authoritative history.", "Finance"),
        RoleCapability(AgentCapabilityIds.FinanceAccountingTreatment, "Accounting treatment advice", "Recommend reviewable account and treatment candidates within accounting policy.", "Finance"),
        RoleCapability(AgentCapabilityIds.FinanceCloseAnalysis, "Close analysis", "Explain statement variance, anomalies, and close readiness.", "Finance"),
        RoleCapability(AgentCapabilityIds.FinanceOperatingCadence, "Finance operating cadence", "Prepare continuous, daily, and weekly Finance management priorities.", "Finance"),
        RoleCapability(AgentCapabilityIds.SalesLeadIntelligence, "Lead intelligence", "Explain lead fit, intent, and evidence gaps.", "Sales"),
        RoleCapability(AgentCapabilityIds.SalesNextBestAction, "Sales next best action", "Recommend bounded relationship actions from current Sales evidence.", "Sales"),
        RoleCapability(AgentCapabilityIds.SalesDealRisk, "Deal risk advice", "Explain deterministic deal risk and recovery priorities.", "Sales"),
        RoleCapability(AgentCapabilityIds.SalesForecastAnalysis, "Forecast analysis", "Explain authoritative forecast scenarios, concentration, and uncertainty.", "Sales"),
        RoleCapability(AgentCapabilityIds.SalesCampaignOptimization, "Campaign optimization", "Analyze reviewed campaign outcomes and recommend policy-safe improvements.", "Sales"),
        RoleCapability(AgentCapabilityIds.SalesProposalAdvice, "Proposal advice", "Recommend source-backed product, proposal, and terms guidance for review.", "Sales"),
        RoleCapability(AgentCapabilityIds.SalesOperatingCadence, "Sales operating cadence", "Prepare continuous, daily, and weekly Sales management priorities.", "Sales"),
        RoleCapability(AgentCapabilityIds.SupportTriageAnalysis, "Support triage analysis", "Explain deterministic case priority, context ambiguity, and assignment needs.", "Support"),
        RoleCapability(AgentCapabilityIds.SupportGroundedReply, "Grounded reply advice", "Assess answerability and provide cited reply advice without sending.", "Support"),
        RoleCapability(AgentCapabilityIds.SupportRiskEscalation, "Support risk and escalation", "Explain SLA and severe-risk evidence while deterministic policy controls escalation.", "Support"),
        RoleCapability(AgentCapabilityIds.SupportRootCauseAnalysis, "Support root-cause analysis", "Explain recurring issue clusters and reviewable root-cause hypotheses.", "Support"),
        RoleCapability(AgentCapabilityIds.SupportKnowledgeCoverage, "Support knowledge coverage", "Identify repeated gaps, stale evidence, and documentation priorities.", "Support"),
        RoleCapability(AgentCapabilityIds.SupportOperatingCadence, "Support operating cadence", "Prepare continuous, daily, and weekly Support management priorities.", "Support"),
        MarketingCapability(AgentCapabilityIds.MarketingPlanning, "Marketing planning", "Turn company goals into reviewable marketing objectives, plans, budgets, and calendars.", MarketingToolIds.PreparePlan, ["marketing"]),
        MarketingCapability(AgentCapabilityIds.MarketingAudienceIntelligence, "Audience intelligence", "Explain audience fit, qualification evidence, exclusions, consent, and freshness.", MarketingToolIds.AnalyzeAudience, ["marketing", "sales"]),
        MarketingCapability(AgentCapabilityIds.MarketingContentAdvice, "Content advice", "Prepare grounded content briefs and variants for human review without publishing.", MarketingToolIds.PrepareContentBrief, ["marketing", "knowledge"]),
        MarketingCapability(AgentCapabilityIds.MarketingCampaignCoordination, "Campaign coordination", "Coordinate approved campaign activities and bounded handoffs to Sales.", MarketingToolIds.RecommendCampaignChange, ["marketing", "sales"]),
        MarketingCapability(AgentCapabilityIds.MarketingPerformanceAnalysis, "Marketing performance analysis", "Explain observed campaign outcomes, costs, attribution limits, and evidence gaps.", MarketingToolIds.PreparePerformanceReview, ["marketing"]),
        MarketingCapability(AgentCapabilityIds.MarketingExperimentAdvice, "Marketing experiment advice", "Recommend bounded experiments with explicit hypotheses, metrics, and guardrails.", MarketingToolIds.PrepareExperiment, ["marketing"]),
        MarketingCapability(AgentCapabilityIds.MarketingOperatingCadence, "Marketing operating cadence", "Prepare continuous, daily, weekly, and monthly Marketing management priorities.", MarketingToolIds.PrepareOperatingReview, ["marketing", "sales", "knowledge"]),
        new AgentCapabilityManifest(AgentCapabilityIds.MarketingPlanDraftExecution, "1.0.0", "Create Marketing plan drafts",
            "Create grounded internal plan drafts after deterministic policy checks.", "Marketing", ToolActionType.Execute,
            [MarketingToolIds.CreatePlanDraft], ["marketing"], [], AgentAutonomyLevel.Level3, "policy_dependent", true),
        new AgentCapabilityManifest(AgentCapabilityIds.MarketingCampaignDraftExecution, "1.0.0", "Create campaign portfolio drafts",
            "Create incomplete internal Sales campaign drafts without launch or contact.", "Marketing", ToolActionType.Execute,
            [MarketingToolIds.CreateCampaignDrafts, MarketingToolIds.PopulateCampaignDraft], ["marketing", "sales"], [],
            AgentAutonomyLevel.Level3, "policy_dependent", true)
    ];

    private static AgentCapabilityManifest RoleCapability(string id, string name, string description, string category) =>
        new(id, "1.0.0", name, description, category, ToolActionType.Recommend, [], [], [SharedAiProviderSignal],
            AgentAutonomyLevel.Level0, "none", true);

    private static AgentCapabilityManifest MarketingCapability(
        string id,
        string name,
        string description,
        string tool,
        IReadOnlyList<string> scopes) =>
        new(id, "1.0.0", name, description, "Marketing", ToolActionType.Recommend, [tool], scopes,
            [SharedAiProviderSignal], AgentAutonomyLevel.Level0, "none", true);

    private readonly IAgentEffectiveAuthorityResolver _authorityResolver;
    private readonly KnowledgeIndexingOptions _knowledgeIndexingOptions;
    private readonly BriefingSchedulerOptions _briefingSchedulerOptions;
    private readonly SharedAgentAiOptions _sharedAiOptions;

    public AgentCapabilityCatalog(
        IAgentEffectiveAuthorityResolver authorityResolver,
        IOptions<KnowledgeIndexingOptions> knowledgeIndexingOptions,
        IOptions<BriefingSchedulerOptions> briefingSchedulerOptions,
        IOptions<SharedAgentAiOptions> sharedAiOptions)
    {
        _authorityResolver = authorityResolver;
        _knowledgeIndexingOptions = knowledgeIndexingOptions.Value;
        _briefingSchedulerOptions = briefingSchedulerOptions.Value;
        _sharedAiOptions = sharedAiOptions.Value;
    }

    public IReadOnlyList<AgentCapabilityManifest> ListManifests() => Manifests;

    public async Task<AgentCapabilityCatalogDto> GetEffectiveCatalogAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        var authority = await _authorityResolver.ResolveAsync(companyId, agentId, cancellationToken);
        var profile = new CapabilityProfile(
            authority.AgentName,
            authority.Department,
            authority.AgentStatus,
            authority.CanReceiveAssignments,
            AgentAutonomyLevelValues.Parse(authority.AutonomyLevel));
        var autonomy = profile.AutonomyLevel;
        var capabilities = Manifests
            .Select(manifest => Resolve(manifest, profile, autonomy, authority))
            .ToArray();

        return new AgentCapabilityCatalogDto(
            companyId,
            agentId,
            profile.DisplayName,
            profile.Status,
            profile.AutonomyLevel.ToStorageValue(),
            capabilities,
            authority.GeneratedUtc,
            authority.AuthorityVersion,
            authority.AuthorityHash,
            authority.Tools);
    }

    private AgentCapabilityDto Resolve(
        AgentCapabilityManifest manifest,
        CapabilityProfile profile,
        AgentAutonomyLevel autonomy,
        AgentEffectiveAuthorityDto authority)
    {
        var missing = new List<string>();

        if (manifest.Category is "Finance" or "Sales" or "Support" or "Marketing" &&
            !DepartmentMatches(manifest.Category, profile.Department))
        {
            return ToDto(manifest, AgentCapabilityStates.PermissionDenied, "role_scope_mismatch",
                "This capability belongs to another agent role.", [$"role:{manifest.Category.ToLowerInvariant()}"]);
        }

        if (!manifest.IsImplemented)
        {
            return ToDto(
                manifest,
                AgentCapabilityStates.NotImplemented,
                "capability_not_implemented",
                "This capability is defined but is not available in the current product version.",
                missing);
        }

        if (!profile.CanReceiveAssignments || !string.Equals(profile.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return ToDto(
                manifest,
                AgentCapabilityStates.PermissionDenied,
                "agent_not_active",
                "The agent must be active and able to receive work before this capability is available.",
                missing);
        }

        var requiredAuthorities = manifest.RequiredTools.Select(tool => authority.Find(tool, manifest.ActionType,
            manifest.RequiredDataScopes.FirstOrDefault())).ToArray();
        if (requiredAuthorities.Any(item => item is null || !item.IsUsable))
        {
            var unavailable = requiredAuthorities.FirstOrDefault(item => item is not null && !item.IsUsable);
            missing.AddRange(manifest.RequiredTools.Where((_, index) => requiredAuthorities[index] is null)
                .Select(tool => $"tool:{tool}"));
            if (unavailable is not null) missing.Add($"authority:{unavailable.ToolName}:{unavailable.ReasonCode}");
            return ToDto(manifest,
                unavailable?.State ?? AgentCapabilityStates.ConfigurationRequired,
                unavailable?.ReasonCode ?? "required_tool_unregistered",
                unavailable?.Explanation ?? "One or more trusted tools required by this capability are not registered.",
                missing);
        }

        if (autonomy < manifest.MinimumAutonomy)
        {
            return ToDto(
                manifest,
                AgentCapabilityStates.PermissionDenied,
                "autonomy_level_too_low",
                "The agent's configured autonomy level is below this capability's minimum.",
                [$"autonomy:{manifest.MinimumAutonomy.ToStorageValue()}"]);
        }

        foreach (var signal in manifest.RequiredConfigurationSignals)
        {
            if (!IsConfigurationSignalReady(signal))
            {
                missing.Add($"configuration:{signal}");
            }
        }

        if (missing.Count > 0)
        {
            var providerMissing = missing.Contains($"configuration:{SharedAiProviderSignal}", StringComparer.OrdinalIgnoreCase);
            return ToDto(
                manifest,
                providerMissing ? AgentCapabilityStates.IntegrationUnavailable : AgentCapabilityStates.ConfigurationRequired,
                providerMissing ? "ai_provider_unavailable" : "required_configuration_missing",
                providerMissing ? "The shared AI provider is not configured." : "Required background processing or configuration is not enabled.",
                missing);
        }

        var approvalRequired = manifest.ApprovalBehavior is "always" or "commit_requires_review" or "policy_dependent" ||
                               requiredAuthorities.Any(item => item?.State == AgentCapabilityStates.ApprovalRequired);
        return ToDto(
            manifest,
            approvalRequired ? AgentCapabilityStates.ApprovalRequired : AgentCapabilityStates.Available,
            approvalRequired ? "approval_required" : "capability_available",
            approvalRequired
                ? "The capability is configured, but every use requires approval."
                : "The capability is configured and available within the agent's current permissions.",
            []);
    }

    private bool IsConfigurationSignalReady(string signal) => signal switch
    {
        KnowledgeIndexingSignal => _knowledgeIndexingOptions.Enabled,
        BriefingSchedulerSignal => _briefingSchedulerOptions.Enabled,
        SharedAiProviderSignal => _sharedAiOptions.Enabled && !string.IsNullOrWhiteSpace(_sharedAiOptions.ApiKey),
        _ => false
    };

    private static bool DepartmentMatches(string category, string department) => category switch
    {
        "Support" => department.Equals("Support", StringComparison.OrdinalIgnoreCase) ||
                     department.Equals("Customer Support", StringComparison.OrdinalIgnoreCase),
        _ => department.Equals(category, StringComparison.OrdinalIgnoreCase)
    };

    private static AgentCapabilityDto ToDto(
        AgentCapabilityManifest manifest,
        string state,
        string reasonCode,
        string explanation,
        IReadOnlyList<string> missingRequirements) =>
        new(
            manifest.Id,
            manifest.Version,
            manifest.Name,
            manifest.Description,
            manifest.Category,
            manifest.ActionType.ToStorageValue(),
            state,
            reasonCode,
            explanation,
            manifest.RequiredTools,
            manifest.RequiredDataScopes,
            missingRequirements,
            manifest.ApprovalBehavior);

    private sealed record CapabilityProfile(
        string DisplayName,
        string Department,
        string Status,
        bool CanReceiveAssignments,
        AgentAutonomyLevel AutonomyLevel);
}
