using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public static class AgentCapabilityIds
{
    public const string GroundedQuestionAnswering = "grounded_question_answering";
    public const string RoleBriefing = "role_briefing";
    public const string WorkPrioritization = "work_prioritization";
    public const string Planning = "planning";
    public const string ExceptionInterpretation = "exception_interpretation";
    public const string CrossAgentHandoff = "cross_agent_handoff";
    public const string MemoryProposal = "memory_proposal";
    public const string FinanceCashLiquidity = "finance_cash_liquidity";
    public const string FinancePayables = "finance_payables";
    public const string FinanceReceivables = "finance_receivables";
    public const string FinanceAccountingTreatment = "finance_accounting_treatment";
    public const string FinanceCloseAnalysis = "finance_close_analysis";
    public const string FinanceOperatingCadence = "finance_operating_cadence";
    public const string FinanceToolPlanning = "finance_tool_planning";
    public const string FinanceConversationExecution = "finance_conversation_execution";
    public const string SalesLeadIntelligence = "sales_lead_intelligence";
    public const string SalesNextBestAction = "sales_next_best_action";
    public const string SalesDealRisk = "sales_deal_risk";
    public const string SalesForecastAnalysis = "sales_forecast_analysis";
    public const string SalesCampaignOptimization = "sales_campaign_optimization";
    public const string SalesProposalAdvice = "sales_proposal_advice";
    public const string SalesOperatingCadence = "sales_operating_cadence";
    public const string SupportTriageAnalysis = "support_triage_analysis";
    public const string SupportGroundedReply = "support_grounded_reply";
    public const string SupportRiskEscalation = "support_risk_escalation";
    public const string SupportRootCauseAnalysis = "support_root_cause_analysis";
    public const string SupportKnowledgeCoverage = "support_knowledge_coverage";
    public const string SupportOperatingCadence = "support_operating_cadence";
    public const string MarketingPlanning = "marketing_planning";
    public const string MarketingPlanDraftExecution = "marketing_plan_draft_execution";
    public const string MarketingCampaignDraftExecution = "marketing_campaign_draft_execution";
    public const string MarketingAudienceIntelligence = "marketing_audience_intelligence";
    public const string MarketingContentAdvice = "marketing_content_advice";
    public const string MarketingCampaignCoordination = "marketing_campaign_coordination";
    public const string MarketingPerformanceAnalysis = "marketing_performance_analysis";
    public const string MarketingExperimentAdvice = "marketing_experiment_advice";
    public const string MarketingOperatingCadence = "marketing_operating_cadence";
}

public static class AgentCapabilityStates
{
    public const string Available = "available";
    public const string ApprovalRequired = "approval_required";
    public const string ConfigurationRequired = "configuration_required";
    public const string PermissionDenied = "permission_denied";
    public const string IntegrationUnavailable = "integration_unavailable";
    public const string NotImplemented = "not_implemented";
}

public sealed record AgentCapabilityManifest(
    string Id,
    string Version,
    string Name,
    string Description,
    string Category,
    ToolActionType ActionType,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> RequiredDataScopes,
    IReadOnlyList<string> RequiredConfigurationSignals,
    AgentAutonomyLevel MinimumAutonomy,
    string ApprovalBehavior,
    bool IsImplemented);

public sealed record AgentCapabilityDto(
    string Id,
    string Version,
    string Name,
    string Description,
    string Category,
    string ActionType,
    string State,
    string ReasonCode,
    string Explanation,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> RequiredDataScopes,
    IReadOnlyList<string> MissingRequirements,
    string ApprovalBehavior);

public sealed record AgentCapabilityCatalogDto(
    Guid CompanyId,
    Guid AgentId,
    string AgentName,
    string AgentStatus,
    string AutonomyLevel,
    IReadOnlyList<AgentCapabilityDto> Capabilities,
    DateTime GeneratedUtc,
    string AuthorityVersion,
    string AuthorityHash,
    IReadOnlyList<EffectiveAgentToolAuthorityDto> EffectiveTools);

public interface IAgentCapabilityCatalog
{
    IReadOnlyList<AgentCapabilityManifest> ListManifests();

    Task<AgentCapabilityCatalogDto> GetEffectiveCatalogAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken);
}
