using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
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
        RoleCapability(AgentCapabilityIds.MarketingPlanning, "Marketing planning", "Turn company goals into reviewable marketing objectives, plans, budgets, and calendars.", "Marketing"),
        RoleCapability(AgentCapabilityIds.MarketingAudienceIntelligence, "Audience intelligence", "Explain audience fit, qualification evidence, exclusions, consent, and freshness.", "Marketing"),
        RoleCapability(AgentCapabilityIds.MarketingContentAdvice, "Content advice", "Prepare grounded content briefs and variants for human review without publishing.", "Marketing"),
        RoleCapability(AgentCapabilityIds.MarketingCampaignCoordination, "Campaign coordination", "Coordinate approved campaign activities and bounded handoffs to Sales.", "Marketing"),
        RoleCapability(AgentCapabilityIds.MarketingPerformanceAnalysis, "Marketing performance analysis", "Explain observed campaign outcomes, costs, attribution limits, and evidence gaps.", "Marketing"),
        RoleCapability(AgentCapabilityIds.MarketingExperimentAdvice, "Marketing experiment advice", "Recommend bounded experiments with explicit hypotheses, metrics, and guardrails.", "Marketing"),
        RoleCapability(AgentCapabilityIds.MarketingOperatingCadence, "Marketing operating cadence", "Prepare continuous, daily, weekly, and monthly Marketing management priorities.", "Marketing")
    ];

    private static AgentCapabilityManifest RoleCapability(string id, string name, string description, string category) =>
        new(id, "1.0.0", name, description, category, ToolActionType.Recommend, [], [], [SharedAiProviderSignal],
            AgentAutonomyLevel.Level0, "none", true);

    private readonly ICompanyAgentService _agentService;
    private readonly ICompanyToolRegistry _toolRegistry;
    private readonly KnowledgeIndexingOptions _knowledgeIndexingOptions;
    private readonly BriefingSchedulerOptions _briefingSchedulerOptions;
    private readonly SharedAgentAiOptions _sharedAiOptions;

    public AgentCapabilityCatalog(
        ICompanyAgentService agentService,
        ICompanyToolRegistry toolRegistry,
        IOptions<KnowledgeIndexingOptions> knowledgeIndexingOptions,
        IOptions<BriefingSchedulerOptions> briefingSchedulerOptions,
        IOptions<SharedAgentAiOptions> sharedAiOptions)
    {
        _agentService = agentService;
        _toolRegistry = toolRegistry;
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

        var profile = await _agentService.GetOperatingProfileAsync(companyId, agentId, cancellationToken);
        var autonomy = AgentAutonomyLevelValues.Parse(profile.AutonomyLevel);
        var capabilities = Manifests
            .Select(manifest => Resolve(manifest, profile, autonomy))
            .ToArray();

        return new AgentCapabilityCatalogDto(
            companyId,
            agentId,
            profile.DisplayName,
            profile.Status,
            profile.AutonomyLevel,
            capabilities,
            DateTime.UtcNow);
    }

    private AgentCapabilityDto Resolve(
        AgentCapabilityManifest manifest,
        AgentOperatingProfileDto profile,
        AgentAutonomyLevel autonomy)
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

        foreach (var toolName in manifest.RequiredTools)
        {
            if (!_toolRegistry.TryGetTool(toolName, out var registration) ||
                !registration.SupportedActions.Contains(manifest.ActionType))
            {
                missing.Add($"tool:{toolName}");
            }
        }

        if (missing.Count > 0)
        {
            return ToDto(
                manifest,
                AgentCapabilityStates.ConfigurationRequired,
                "required_tool_unregistered",
                "One or more trusted tools required by this capability are not registered.",
                missing);
        }

        var allowedTools = ReadStringSet(profile.ToolPermissions, "allowed", out var allowedToolsConfigured);
        var deniedTools = ReadStringSet(profile.ToolPermissions, "denied", out _);
        var allowedActions = ReadStringSet(profile.ToolPermissions, "actions", out var allowedActionsConfigured);
        var deniedActions = ReadStringSet(profile.ToolPermissions, "deniedActions", out _);
        var actionName = manifest.ActionType.ToStorageValue();

        if (!allowedToolsConfigured || manifest.RequiredTools.Any(tool => !allowedTools.Contains(tool) || deniedTools.Contains(tool)))
        {
            missing.AddRange(manifest.RequiredTools
                .Where(tool => !allowedTools.Contains(tool) || deniedTools.Contains(tool))
                .Select(tool => $"permission:tool:{tool}"));
        }

        if ((allowedActionsConfigured && !allowedActions.Contains(actionName)) || deniedActions.Contains(actionName))
        {
            missing.Add($"permission:action:{actionName}");
        }

        var scopeBucket = manifest.ActionType switch
        {
            ToolActionType.Read => "read",
            ToolActionType.Recommend => "recommend",
            ToolActionType.Execute => "execute",
            _ => "read"
        };
        var allowedScopes = ReadStringSet(profile.DataScopes, scopeBucket, out var scopeConfigured);
        if (manifest.RequiredDataScopes.Count > 0 &&
            (!scopeConfigured || manifest.RequiredDataScopes.Any(scope => !allowedScopes.Contains(scope))))
        {
            missing.AddRange(manifest.RequiredDataScopes
                .Where(scope => !allowedScopes.Contains(scope))
                .Select(scope => $"permission:scope:{scopeBucket}:{scope}"));
        }

        if (missing.Count > 0)
        {
            return ToDto(
                manifest,
                AgentCapabilityStates.PermissionDenied,
                "agent_permission_missing",
                "The agent's tool permissions or data scopes do not allow this capability.",
                missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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

        var approvalRequired = manifest.ApprovalBehavior is "always" or "commit_requires_review" or "policy_dependent";
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

    private static HashSet<string> ReadStringSet(
        IReadOnlyDictionary<string, JsonNode?> source,
        string key,
        out bool configured)
    {
        configured = source.TryGetValue(key, out var node) && node is JsonArray;
        if (node is not JsonArray array)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return array
            .Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text?.Trim() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
