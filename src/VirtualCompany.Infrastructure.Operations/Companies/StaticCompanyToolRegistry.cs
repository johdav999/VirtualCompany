using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class StaticCompanyToolRegistry : ICompanyToolRegistry
{
    private static readonly IReadOnlySet<ToolActionType> StandardActions = new HashSet<ToolActionType>
    {
        ToolActionType.Read,
        ToolActionType.Recommend,
        ToolActionType.Execute
    };

    private readonly IReadOnlyDictionary<string, TrustedToolRegistration> _tools;
    private readonly IReadOnlyDictionary<string, ToolDefinitionManifest> _definitions;

    public StaticCompanyToolRegistry()
    {
        var taskScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tasks" };
        var approvalScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "approvals" };
        var knowledgeScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "knowledge" };
        var paymentsScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payments" };
        var financeScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "finance" };
        var salesScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sales", "prospecting" };
        var marketingScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "marketing", "sales", "knowledge" };

        var registrations = new[]
        {
            Register("tasks.get", new HashSet<ToolActionType> { ToolActionType.Read }, taskScopes),
            Register("tasks.list", new HashSet<ToolActionType> { ToolActionType.Read }, taskScopes),
            Register("tasks.update_status", new HashSet<ToolActionType> { ToolActionType.Execute }, taskScopes),
            Register("approvals.create_request", new HashSet<ToolActionType> { ToolActionType.Execute }, approvalScopes),
            Register("knowledge.search", new HashSet<ToolActionType> { ToolActionType.Read, ToolActionType.Recommend }, knowledgeScopes),
            Register("erp", new HashSet<ToolActionType> { ToolActionType.Execute }, paymentsScopes)
        }.Concat(FinanceToolDefinitions.Select(definition =>
            Register(
                definition.ToolName,
                new HashSet<ToolActionType> { definition.ActionType },
                financeScopes,
                definition.Version,
                definition.InputSchema,
                definition.OutputSchema,
                definition.SensitiveAction,
                definition.ActionType == ToolActionType.Execute
                    ? FinanceToolRiskPolicyCatalog.GetRequired(definition.ToolName)
                    : null,
                definition.ActionType == ToolActionType.Execute
                    ? FinanceExecuteToolReadinessCatalog.GetRequired(definition.ToolName)
                    : null)))
          .Concat(SalesToolDefinitions.Select(definition => Register(definition.ToolName, new HashSet<ToolActionType> { definition.ActionType }, salesScopes, definition.Version, definition.InputSchema, definition.OutputSchema)))
          .Concat(MarketingToolDefinitions.Select(definition => Register(definition.ToolName, new HashSet<ToolActionType> { definition.ActionType }, marketingScopes, definition.Version, definition.InputSchema, definition.OutputSchema)));

        _tools = registrations.ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
        _definitions = FinanceToolDefinitions.Concat(SalesToolDefinitions).Concat(MarketingToolDefinitions)
            .ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
        ValidateFinanceExecuteReadiness();
    }

    private static IReadOnlyList<ToolDefinitionManifest> FinanceToolDefinitions { get; } =
    [
        FinanceDefinition("get_cash_balance", ToolActionType.Read, FinanceInputSchemas.AsOfDate(), FinanceOutputSchemas.WithDataProperty("cashBalance")),
        FinanceDefinition("list_transactions", ToolActionType.Read, FinanceInputSchemas.ListRange(), FinanceOutputSchemas.WithDataProperty("transactions")),
        FinanceDefinition("resolve_finance_agent_query", ToolActionType.Read, FinanceInputSchemas.AgentQuery(), FinanceOutputSchemas.WithDataProperty("result")),
        FinanceDefinition("list_uncategorized_transactions", ToolActionType.Read, FinanceInputSchemas.ListRange(), FinanceOutputSchemas.WithDataProperty("transactions")),
        FinanceDefinition("list_invoices_awaiting_approval", ToolActionType.Read, FinanceInputSchemas.ListRange(), FinanceOutputSchemas.WithDataProperty("invoices")),
        FinanceDefinition("get_profit_and_loss_summary", ToolActionType.Read, FinanceInputSchemas.ProfitAndLoss(), FinanceOutputSchemas.WithDataProperty("profitAndLossSummary")),
        FinanceDefinition("recommend_transaction_category", ToolActionType.Recommend, FinanceInputSchemas.TransactionRecommendation(), FinanceOutputSchemas.WithDataProperty("recommendation")),
        FinanceDefinition("recommend_invoice_approval_decision", ToolActionType.Recommend, FinanceInputSchemas.InvoiceRecommendation(), FinanceOutputSchemas.WithDataProperty("recommendation")),
        FinanceDefinition("evaluate_transaction_anomaly", ToolActionType.Recommend, FinanceInputSchemas.TransactionAnomalyEvaluation(), FinanceOutputSchemas.WithDataProperty("anomalyEvaluation")),
        FinanceDefinition(FinanceAgentAnalysisToolIds.Analyze, ToolActionType.Recommend, FinanceInputSchemas.AgentAnalysis(), FinanceOutputSchemas.WithDataProperty("analysis")),
        FinanceDefinition("categorize_transaction", ToolActionType.Execute, FinanceInputSchemas.CategorizeTransaction(), FinanceOutputSchemas.WithDataProperty("transaction")),
        FinanceDefinition(FinanceGuardedCommandToolIds.CategorizeTransactions, ToolActionType.Execute,
            FinanceInputSchemas.CategorizeTransactions(), FinanceOutputSchemas.WithDataProperty("categorizationBatch")),
        FinanceDefinition("approve_invoice", ToolActionType.Execute, FinanceInputSchemas.ApproveInvoice(), FinanceOutputSchemas.WithDataProperty("invoice")),
        FinanceDefinition("post_paid_supplier_bill_expense", ToolActionType.Execute, FinanceInputSchemas.PostPaidSupplierBillExpense(), FinanceOutputSchemas.WithDataProperty("expensePosting")),

        ..FinanceLedgerAgentReadToolIds.All.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Read, FinanceInputSchemas.LedgerRead(tool),
                FinanceOutputSchemas.WithDataProperty(LedgerReadProperty(tool)))),

        ..FinanceCloseComplianceAgentToolIds.ReadTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Read, FinanceInputSchemas.CloseCompliance(tool),
                FinanceOutputSchemas.WithDataProperty(CloseComplianceProperty(tool)))),
        ..FinanceCloseComplianceAgentToolIds.RecommendationTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Recommend, FinanceInputSchemas.CloseCompliance(tool),
                FinanceOutputSchemas.WithDataProperty(CloseComplianceProperty(tool)))),

        ..FinanceAdvancedAccountingAgentToolIds.ReadTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Read, FinanceInputSchemas.AdvancedAccounting(tool),
                FinanceOutputSchemas.WithDataProperty(AdvancedAccountingProperty(tool)))),
        ..FinanceAdvancedAccountingAgentToolIds.RecommendationTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Recommend, FinanceInputSchemas.AdvancedAccounting(tool),
                FinanceOutputSchemas.WithDataProperty(AdvancedAccountingProperty(tool)))),

        ..FinanceAccountingDraftAgentToolIds.RecommendationTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Recommend, FinanceInputSchemas.AccountingDraft(tool),
                FinanceOutputSchemas.WithDataProperty(AccountingDraftProperty(tool)))),
        FinanceDefinition(FinanceAccountingDraftAgentToolIds.SubmitForApproval, ToolActionType.Execute,
            FinanceInputSchemas.AccountingDraft(FinanceAccountingDraftAgentToolIds.SubmitForApproval),
            FinanceOutputSchemas.WithDataProperty("accountingDraftSubmission"), sensitiveAction: true),

        ..FinanceOperationalProposalAgentToolIds.RecommendationTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Recommend, FinanceInputSchemas.OperationalProposal(tool),
                FinanceOutputSchemas.WithDataProperty("operationalProposal"))),
        ..FinanceOperationalProposalAgentToolIds.ExecuteTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Execute, FinanceInputSchemas.OperationalProposal(tool),
                FinanceOutputSchemas.WithDataProperty("proposalExecution"), sensitiveAction: true)),

        ..AccountingProviderSwitchAgentToolIds.ReadTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Read, FinanceInputSchemas.MigrationRead(), FinanceOutputSchemas.WithDataProperty(MigrationReadProperty(tool)))),
        ..AccountingProviderSwitchAgentToolIds.RecommendationTools.Select(tool =>
            FinanceDefinition(tool, ToolActionType.Recommend, FinanceInputSchemas.MigrationRecommendation(), FinanceOutputSchemas.WithDataProperty("recommendation"))),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.StartAssessment, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(), FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.StartRehearsal, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(), FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.StartPreparation, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(("planId", FinanceInputSchemas.Uuid())), FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.ApplyApprovedMapping, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(
                ("stagedRecordId", FinanceInputSchemas.Uuid()),
                ("mappingDecisionId", FinanceInputSchemas.Uuid()),
                ("expectedRecordVersion", FinanceInputSchemas.PositiveInteger()),
                ("disposition", FinanceInputSchemas.StringEnum("mapped", "transformed", "ready", "opening_balance_representation"))),
            FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.CreateFollowUpTask, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(
                ("title", FinanceInputSchemas.String(1, 200)),
                ("description", FinanceInputSchemas.String(1, 2000)),
                ("priority", FinanceInputSchemas.StringEnum("low", "normal", "high", "urgent"))),
            FinanceOutputSchemas.WithDataProperty("task"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.RequestPlanApproval, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(("planId", FinanceInputSchemas.Uuid())), FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.StartApprovedFreeze, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(("cutoverExecutionId", FinanceInputSchemas.Uuid()), ("expectedExecutionVersion", FinanceInputSchemas.PositiveInteger())),
            FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.RequestActivationApproval, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(("cutoverExecutionId", FinanceInputSchemas.Uuid()), ("expectedExecutionVersion", FinanceInputSchemas.PositiveInteger())),
            FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true),
        FinanceDefinition(AccountingProviderSwitchAgentToolIds.ResumeRecovery, ToolActionType.Execute,
            FinanceInputSchemas.MigrationExecute(("cutoverExecutionId", FinanceInputSchemas.Uuid()), ("expectedExecutionVersion", FinanceInputSchemas.PositiveInteger())),
            FinanceOutputSchemas.WithDataProperty("commandResult"), sensitiveAction: true)
    ];

    private static string MigrationReadProperty(string toolName) =>
        string.Equals(toolName, AccountingProviderSwitchAgentToolIds.ReadBriefing, StringComparison.OrdinalIgnoreCase)
            ? "briefing"
            : "evidence";

    private static string LedgerReadProperty(string toolName) => toolName switch
    {
        FinanceLedgerAgentReadToolIds.LookupAccounts => "accounts",
        FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => "fiscalYears",
        FinanceLedgerAgentReadToolIds.SearchJournals => "journals",
        FinanceLedgerAgentReadToolIds.ReadGeneralLedger => "generalLedger",
        FinanceLedgerAgentReadToolIds.ReadTrialBalance => "trialBalance",
        FinanceLedgerAgentReadToolIds.ReadStatement => "statement",
        FinanceLedgerAgentReadToolIds.ReadReportDefinitions => "reportDefinitions",
        FinanceLedgerAgentReadToolIds.ReadReportSnapshot => "reportSnapshot",
        FinanceLedgerAgentReadToolIds.ReadSourceDrilldown => "drilldown",
        _ => "result"
    };

    private static string CloseComplianceProperty(string toolName) => toolName switch
    {
        FinanceCloseComplianceAgentToolIds.ReadTemplates => "closeTemplates",
        FinanceCloseComplianceAgentToolIds.ReadInstance => "closeInstance",
        FinanceCloseComplianceAgentToolIds.ReadReadiness => "closeReadiness",
        FinanceCloseComplianceAgentToolIds.ReadPeriodLockHistory => "periodLockHistory",
        FinanceCloseComplianceAgentToolIds.ReadComplianceObligations => "complianceObligations",
        FinanceCloseComplianceAgentToolIds.ReadAuditPackages => "auditPackages",
        FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity => "accountantAccessActivity",
        FinanceCloseComplianceAgentToolIds.ReadYearEnd => "yearEnd",
        FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers => "closeRecommendation",
        FinanceCloseComplianceAgentToolIds.ExplainCompliancePreparation => "complianceRecommendation",
        FinanceCloseComplianceAgentToolIds.ExplainAuditPackageCompleteness => "auditRecommendation",
        FinanceCloseComplianceAgentToolIds.ExplainYearEndPrerequisites => "yearEndRecommendation",
        _ => "result"
    };

    private static string AdvancedAccountingProperty(string toolName) => toolName switch
    {
        FinanceAdvancedAccountingAgentToolIds.ReadStatementImports => "statementImports",
        FinanceAdvancedAccountingAgentToolIds.ReadReconciliation => "reconciliation",
        FinanceAdvancedAccountingAgentToolIds.ReadSubledgerSettlement => "subledgerSettlement",
        FinanceAdvancedAccountingAgentToolIds.ReadPaymentBatches => "paymentBatches",
        FinanceAdvancedAccountingAgentToolIds.ReadExchangeRates => "exchangeRates",
        FinanceAdvancedAccountingAgentToolIds.ReadRevaluation => "revaluation",
        FinanceAdvancedAccountingAgentToolIds.ReadDimensions => "dimensions",
        FinanceAdvancedAccountingAgentToolIds.ReadSchedules => "schedules",
        FinanceAdvancedAccountingAgentToolIds.ReadFixedAssets => "fixedAssets",
        FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary => "inventoryBoundary",
        FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview => "reconciliationRecommendation",
        FinanceAdvancedAccountingAgentToolIds.RecommendRateEvidenceRemediation => "rateEvidenceRecommendation",
        FinanceAdvancedAccountingAgentToolIds.RecommendScheduleAssetReview => "scheduleAssetRecommendation",
        FinanceAdvancedAccountingAgentToolIds.PrioritizeSubledgerExceptions => "subledgerExceptionRecommendation",
        _ => "result"
    };

    private static string AccountingDraftProperty(string toolName) =>
        string.Equals(toolName, FinanceAccountingDraftAgentToolIds.CreateReconciliationDecisionDraft, StringComparison.OrdinalIgnoreCase)
            ? "reconciliationDraft"
            : "accountingDraft";

    private static IReadOnlyList<ToolDefinitionManifest> SalesToolDefinitions { get; } =
    [
        SalesDefinition("sales.plan_prospecting_run", ToolActionType.Recommend, """{"type":"object","additionalProperties":false,"required":["icpProfileId","name","accountLimit","sources"],"properties":{"icpProfileId":{"type":"string","format":"uuid"},"name":{"type":"string","minLength":1,"maxLength":160},"accountLimit":{"type":"integer","minimum":1,"maximum":10000},"contactLimit":{"type":"integer","minimum":0,"maximum":50000},"sources":{"type":"string"},"geography":{"type":"string"},"freshnessDays":{"type":"integer","minimum":1,"maximum":365},"estimatedCost":{"type":"number","minimum":0},"schedule":{"type":"string"}}}""", "prospectingRun"),
        SalesDefinition("sales.start_prospecting_run", ToolActionType.Execute, """{"type":"object","additionalProperties":false,"required":["runId"],"properties":{"runId":{"type":"string","format":"uuid"}}}""", "prospectingRun"),
        SalesDefinition("sales.list_prospects", ToolActionType.Read, """{"type":"object","additionalProperties":false,"properties":{"search":{"type":"string"},"status":{"type":"string"},"country":{"type":"string"},"source":{"type":"string"},"page":{"type":"integer","minimum":1},"pageSize":{"type":"integer","minimum":1,"maximum":100}}}""", "prospects"),
        SalesDefinition("sales.research_prospect", ToolActionType.Recommend, """{"type":"object","additionalProperties":false,"required":["prospectId"],"properties":{"prospectId":{"type":"string","format":"uuid"}}}""", "prospect"),
        SalesDefinition("sales.recommend_prospect_decision", ToolActionType.Recommend, """{"type":"object","additionalProperties":false,"required":["prospectId"],"properties":{"prospectId":{"type":"string","format":"uuid"}}}""", "recommendation")
    ];

    private static IReadOnlyList<ToolDefinitionManifest> MarketingToolDefinitions { get; } =
    [
        MarketingDefinition(MarketingToolIds.ReadWorkspace, ToolActionType.Read, "workspace"),
        MarketingDefinition(MarketingToolIds.ReadObjectives, ToolActionType.Read, "objectives"),
        MarketingDefinition(MarketingToolIds.ReadCampaigns, ToolActionType.Read, "campaigns"),
        MarketingDefinition(MarketingToolIds.ReadContentCalendar, ToolActionType.Read, "contentCalendar"),
        MarketingDefinition(MarketingToolIds.ReadAudienceEvidence, ToolActionType.Read, "audienceEvidence"),
        MarketingDefinition(MarketingToolIds.ReadChannelObservations, ToolActionType.Read, "channelObservations"),
        MarketingDefinition(MarketingToolIds.ReadAttributionSummary, ToolActionType.Read, "attributionSummary"),
        MarketingDefinition(MarketingToolIds.SearchApprovedKnowledge, ToolActionType.Read, "knowledgeResults"),
        MarketingDefinition(MarketingToolIds.ReadSegments, ToolActionType.Read, "segments"),
        MarketingDefinition(MarketingToolIds.ReadSegmentEvidence, ToolActionType.Read, "segmentEvidence"),
        MarketingDefinition(MarketingToolIds.ReadStrategies, ToolActionType.Read, "strategies"),
        MarketingDefinition(MarketingToolIds.ReadPlans, ToolActionType.Read, "plans"),
        MarketingDefinition(MarketingToolIds.ReadPlanCoverage, ToolActionType.Read, "planCoverage"),
        MarketingDefinition(MarketingToolIds.ReadCampaignReadiness, ToolActionType.Read, "campaignReadiness"),
        MarketingDefinition(MarketingToolIds.PreparePlan, ToolActionType.Recommend, "planProposal"),
        MarketingDefinition(MarketingToolIds.AnalyzeAudience, ToolActionType.Recommend, "audienceAnalysis"),
        MarketingDefinition(MarketingToolIds.PrepareContentBrief, ToolActionType.Recommend, "contentBrief"),
        MarketingDefinition(MarketingToolIds.RecommendCampaignChange, ToolActionType.Recommend, "campaignRecommendation"),
        MarketingDefinition(MarketingToolIds.PreparePerformanceReview, ToolActionType.Recommend, "performanceReview"),
        MarketingDefinition(MarketingToolIds.PrepareExperiment, ToolActionType.Recommend, "experimentProposal"),
        MarketingDefinition(MarketingToolIds.PrepareOperatingReview, ToolActionType.Recommend, "operatingReview"),
        MarketingDefinition(MarketingToolIds.PrepareSegmentation, ToolActionType.Recommend, "segmentationProposal"),
        MarketingDefinition(MarketingToolIds.RecommendTargetSegments, ToolActionType.Recommend, "targetRecommendation"),
        MarketingDefinition(MarketingToolIds.AssessSegmentStrategyImpact, ToolActionType.Recommend, "strategyImpact"),
        MarketingDefinition(MarketingToolIds.PrepareCampaignPortfolio, ToolActionType.Recommend, "campaignPortfolio"),
        MarketingDefinition(MarketingToolIds.AssessPlanCoverage, ToolActionType.Recommend, "planCoverage"),
        MarketingDefinition(MarketingToolIds.CreatePlanDraft, ToolActionType.Execute, "plan"),
        MarketingDefinition(MarketingToolIds.CreateCampaignDrafts, ToolActionType.Execute, "campaigns"),
        MarketingDefinition(MarketingToolIds.PopulateCampaignDraft, ToolActionType.Execute, "campaign"),
        MarketingDefinition(MarketingToolIds.SubmitPlanForReview, ToolActionType.Execute, "plan"),
        MarketingDefinition(MarketingToolIds.SubmitCampaignForReadiness, ToolActionType.Execute, "campaign")
    ];

    private static ToolDefinitionManifest MarketingDefinition(string name, ToolActionType action, string property) =>
        new(name, "1.0.0", action, ParseSchema(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "objective": { "type": "string", "maxLength": 2000 },
                "asOfUtc": { "type": "string", "format": "date-time" },
                "horizonDays": { "type": "integer", "minimum": 1, "maximum": 365 },
                "entityId": { "type": "string", "format": "uuid" },
                "query": { "type": "string", "maxLength": 500 },
                "limit": { "type": "integer", "minimum": 1, "maximum": 100 }
              }
            }
            """), new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("schemaVersion", "status", "success", "data"),
            ["properties"] = new JsonObject
            {
                ["schemaVersion"] = new JsonObject { ["type"] = "string" },
                ["status"] = new JsonObject { ["type"] = "string" },
                ["success"] = new JsonObject { ["type"] = "boolean" },
                ["data"] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray(property) }
            }
        });

    private static ToolDefinitionManifest SalesDefinition(string name, ToolActionType action, string input, string property) =>
        new(name, "1.0.0", action, ParseSchema(input), new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("schemaVersion", "status", "success", "data"),
            ["properties"] = new JsonObject
            {
                ["schemaVersion"] = new JsonObject { ["type"] = "string" },
                ["status"] = new JsonObject { ["type"] = "string" },
                ["success"] = new JsonObject { ["type"] = "boolean" },
                ["data"] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray(property) }
            }
        });

    public bool TryGetToolDefinition(string toolName, out ToolDefinitionManifest definition)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            definition = default!;
            return false;
        }

        return _definitions.TryGetValue(toolName.Trim(), out definition!);
    }

    public bool TryGetTool(string toolName, out TrustedToolRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            registration = default!;
            return false;
        }

        return _tools.TryGetValue(toolName.Trim(), out registration!);
    }

    public IReadOnlyList<TrustedToolRegistration> ListTools() =>
        _tools.Values.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<ToolDefinitionManifest> ListToolDefinitions() =>
        _definitions.Values.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToArray();

    private static TrustedToolRegistration Register(
        string toolName,
        IReadOnlySet<ToolActionType>? supportedActions = null,
        IReadOnlySet<string>? scopes = null,
        string version = "1.0.0",
        JsonObject? inputSchema = null,
        JsonObject? outputSchema = null,
        bool sensitiveAction = false,
        FinanceToolRiskClassification? financeRiskClassification = null,
        FinanceExecuteToolReadinessContract? financeExecuteReadiness = null) =>
        new(
            toolName,
            supportedActions ?? StandardActions,
            scopes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            version,
            inputSchema?.DeepClone().AsObject(),
            outputSchema?.DeepClone().AsObject(),
            sensitiveAction,
            financeRiskClassification,
            financeExecuteReadiness);

    private void ValidateFinanceExecuteReadiness()
    {
        var financeExecuteTools = _definitions.Values
            .Where(definition => definition.ActionType == ToolActionType.Execute &&
                                 _tools[definition.ToolName].Scopes.Contains("finance"))
            .Select(definition => definition.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = financeExecuteTools.Except(FinanceExecuteToolReadinessCatalog.All.Select(x => x.ToolName),
            StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var orphaned = FinanceExecuteToolReadinessCatalog.All.Select(x => x.ToolName).Except(financeExecuteTools,
            StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || orphaned.Length > 0)
            throw new InvalidOperationException($"Finance execute readiness catalogue mismatch. Missing: [{string.Join(", ", missing)}]. Orphaned: [{string.Join(", ", orphaned)}].");

        foreach (var toolName in financeExecuteTools)
        {
            var readiness = FinanceExecuteToolReadinessCatalog.GetRequired(toolName);
            var risk = FinanceToolRiskPolicyCatalog.GetRequired(toolName);
            var registration = _tools[toolName];
            if (registration.FinanceExecuteReadiness is null ||
                !string.Equals(registration.FinanceExecuteReadiness.ContractVersion, readiness.ContractVersion, StringComparison.Ordinal) ||
                !string.Equals(readiness.RiskTier, risk.RiskTier, StringComparison.Ordinal) ||
                !string.Equals(readiness.RequiredActorPermission, risk.RequiredActorPermission, StringComparison.Ordinal) ||
                !string.Equals(readiness.ApprovalBehavior, risk.DefaultApprovalBehavior, StringComparison.Ordinal))
                throw new InvalidOperationException($"Finance execute tool '{toolName}' has inconsistent readiness and risk metadata.");
        }
    }

    private static ToolDefinitionManifest FinanceDefinition(
        string toolName,
        ToolActionType actionType,
        JsonObject inputSchema,
        JsonObject outputSchema,
        bool sensitiveAction = false) =>
        new(toolName, "1.0.0", actionType, inputSchema, outputSchema,
            actionType == ToolActionType.Execute
                ? FinanceToolRiskPolicyCatalog.GetRequired(toolName).IsSensitiveByDefault
                : sensitiveAction,
            FinanceSelectionMetadata(toolName, actionType));

    private static ToolSelectionMetadata FinanceSelectionMetadata(string toolName, ToolActionType actionType)
    {
        var action = actionType.ToStorageValue();
        var isExecute = actionType == ToolActionType.Execute;
        var isAccountingDraft = FinanceAccountingDraftAgentToolIds.Contains(toolName);
        var isOperationalProposal = FinanceOperationalProposalAgentToolIds.Contains(toolName);
        var isMigration = toolName.StartsWith("accounting_provider_switch.", StringComparison.OrdinalIgnoreCase);
        string[] targetTypes = toolName switch
        {
            "list_transactions" or "list_uncategorized_transactions" or "recommend_transaction_category" or
                "evaluate_transaction_anomaly" or "categorize_transaction" or FinanceGuardedCommandToolIds.CategorizeTransactions => ["transaction"],
            "list_invoices_awaiting_approval" or "recommend_invoice_approval_decision" or "approve_invoice" =>
                [FinancePlanningReferenceTypes.Invoice, FinancePlanningReferenceTypes.Customer],
            "post_paid_supplier_bill_expense" => [FinancePlanningReferenceTypes.Bill, FinancePlanningReferenceTypes.Supplier],
            FinanceAgentAnalysisToolIds.Analyze => [FinancePlanningReferenceTypes.FiscalPeriod],
            "get_profit_and_loss_summary" => [FinancePlanningReferenceTypes.FiscalPeriod],
            FinanceLedgerAgentReadToolIds.LookupAccounts => [FinancePlanningReferenceTypes.Account],
            FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => [FinancePlanningReferenceTypes.FiscalPeriod],
            FinanceLedgerAgentReadToolIds.SearchJournals => [FinancePlanningReferenceTypes.Journal, FinancePlanningReferenceTypes.VoucherSeries],
            FinanceLedgerAgentReadToolIds.ReadGeneralLedger or FinanceLedgerAgentReadToolIds.ReadTrialBalance =>
                [FinancePlanningReferenceTypes.Account, FinancePlanningReferenceTypes.FiscalPeriod],
            FinanceLedgerAgentReadToolIds.ReadStatement =>
                [FinancePlanningReferenceTypes.FiscalPeriod, FinancePlanningReferenceTypes.ReportDefinition],
            FinanceLedgerAgentReadToolIds.ReadReportDefinitions => [FinancePlanningReferenceTypes.ReportDefinition],
            FinanceLedgerAgentReadToolIds.ReadReportSnapshot => [FinancePlanningReferenceTypes.ReportDefinition, FinancePlanningReferenceTypes.FiscalPeriod],
            FinanceLedgerAgentReadToolIds.ReadSourceDrilldown =>
                [FinancePlanningReferenceTypes.ReportLine, FinancePlanningReferenceTypes.Journal, FinancePlanningReferenceTypes.FiscalPeriod],
            _ when FinanceCloseComplianceAgentToolIds.Contains(toolName) => [FinancePlanningReferenceTypes.FiscalPeriod],
            _ when FinanceAdvancedAccountingAgentToolIds.Contains(toolName) => [FinancePlanningReferenceTypes.FiscalPeriod],
            _ when isAccountingDraft => [FinancePlanningReferenceTypes.Journal, FinancePlanningReferenceTypes.Account,
                FinancePlanningReferenceTypes.FiscalPeriod],
            _ when isOperationalProposal => [FinancePlanningReferenceTypes.FiscalPeriod, FinancePlanningReferenceTypes.Account],
            _ when isMigration => [FinancePlanningReferenceTypes.Migration, FinancePlanningReferenceTypes.FiscalPeriod],
            _ => Array.Empty<string>()
        };
        string[] evidence = toolName switch
        {
            "get_cash_balance" => ["authoritative_cash_snapshot"],
            "get_profit_and_loss_summary" => ["posted_ledger_entries", "fiscal_period"],
            "recommend_transaction_category" or "categorize_transaction" or FinanceGuardedCommandToolIds.CategorizeTransactions or "evaluate_transaction_anomaly" =>
                ["finance_transaction"],
            "recommend_invoice_approval_decision" or "approve_invoice" or "list_invoices_awaiting_approval" =>
                ["finance_invoice"],
            "post_paid_supplier_bill_expense" => ["supplier_bill", "posting_eligibility"],
            FinanceAgentAnalysisToolIds.Analyze => ["authoritative_finance_analysis_evidence"],
            _ when FinanceLedgerAgentReadToolIds.Contains(toolName) => ["posted_ledger_entries", "fiscal_period", "report_snapshot"],
            _ when FinanceCloseComplianceAgentToolIds.Contains(toolName) => ["close_evidence", "compliance_evidence", "audit_package_metadata", "year_end_readiness"],
            _ when FinanceAdvancedAccountingAgentToolIds.Contains(toolName) => ["advanced_accounting_state", "object_version", "source_evidence"],
            _ when isAccountingDraft => ["source_record_version", "manual_journal_validation", "accounting_policy", "approval_workflow"],
            _ when isOperationalProposal => ["target_version", "source_evidence", "owning_workflow_validation", "segregation_of_duties"],
            _ when isMigration => ["accounting_provider_switch"],
            _ => ["authoritative_finance_records"]
        };
        var purpose = toolName switch
        {
            "get_cash_balance" => "Read the current authoritative cash balance.",
            "list_transactions" => "List bounded Finance transactions for a requested period.",
            "resolve_finance_agent_query" => "Resolve a supported Finance analysis question from authoritative data.",
            "list_uncategorized_transactions" => "List transactions that still require categorization.",
            "list_invoices_awaiting_approval" => "List invoices currently awaiting review.",
            "get_profit_and_loss_summary" => "Read a profit-and-loss summary for one fiscal month.",
            "recommend_transaction_category" => "Recommend a category for one grounded transaction.",
            "recommend_invoice_approval_decision" => "Recommend a review decision for one grounded invoice.",
            "evaluate_transaction_anomaly" => "Evaluate one grounded transaction for anomaly evidence.",
            FinanceAgentAnalysisToolIds.Analyze => "Run one of the six existing read-only Finance analysis capabilities over authoritative evidence.",
            FinanceLedgerAgentReadToolIds.LookupAccounts => "Look up authoritative chart and account metadata.",
            FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => "Read fiscal-period identity and close or reporting-lock state.",
            FinanceLedgerAgentReadToolIds.SearchJournals => "Search a bounded journal register with source and voucher evidence.",
            FinanceLedgerAgentReadToolIds.ReadGeneralLedger => "Read bounded general-ledger detail from immutable posted journals.",
            FinanceLedgerAgentReadToolIds.ReadTrialBalance => "Read the reconciled trial balance and control totals for one period.",
            FinanceLedgerAgentReadToolIds.ReadStatement => "Read a supported financial statement with mapping, checksum, and provenance.",
            FinanceLedgerAgentReadToolIds.ReadReportDefinitions => "Read report definitions and immutable version identities.",
            FinanceLedgerAgentReadToolIds.ReadReportSnapshot => "Read an immutable report snapshot and verify its checksum.",
            FinanceLedgerAgentReadToolIds.ReadSourceDrilldown => "Drill from one report line to bounded posted-journal sources.",
            _ when FinanceCloseComplianceAgentToolIds.Contains(toolName) => "Read or explain authoritative close, compliance, audit-package, accountant, or year-end evidence without final authority.",
            _ when FinanceAdvancedAccountingAgentToolIds.Contains(toolName) => "Read or prioritize bounded advanced-accounting and subledger evidence without changing authoritative state.",
            _ when isAccountingDraft => "Create or submit a reviewable accounting draft without posting or applying accounting.",
            _ when isOperationalProposal => "Prepare or advance a current operational Finance proposal without final posting, sign-off, filing, or self-approval.",
            "categorize_transaction" => "Change the category of one grounded transaction after supervision.",
            FinanceGuardedCommandToolIds.CategorizeTransactions => "Categorize a bounded set of current transactions with an explicit decision for every item.",
            "approve_invoice" => "Change one grounded invoice review status after supervision.",
            "post_paid_supplier_bill_expense" => "Post an eligible paid supplier bill expense after supervision.",
            _ when isMigration => "Read, assess, or advance one governed accounting migration.",
            _ => "Use an authoritative Finance capability for its declared action."
        };
        var example = toolName switch
        {
            "get_cash_balance" => "How much cash do we have today?",
            "list_transactions" => "Show this month's transactions.",
            "list_uncategorized_transactions" => "Which transactions need categorization?",
            "list_invoices_awaiting_approval" => "Which invoices await approval?",
            "get_profit_and_loss_summary" => "Show the P&L for August 2026.",
            "recommend_transaction_category" => "Suggest a category for this transaction.",
            "recommend_invoice_approval_decision" => "Review invoice 1042.",
            "evaluate_transaction_anomaly" => "Is this transaction unusual?",
            FinanceAgentAnalysisToolIds.Analyze => "Analyze cash, payables, receivables, accounting treatment, close blockers, or operating cadence.",
            FinanceLedgerAgentReadToolIds.LookupAccounts => "Find account 6540.",
            FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => "Is August 2026 closed?",
            FinanceLedgerAgentReadToolIds.SearchJournals => "Show August journals in voucher series A.",
            FinanceLedgerAgentReadToolIds.ReadGeneralLedger => "Show account 6540 ledger detail for August 2026.",
            FinanceLedgerAgentReadToolIds.ReadTrialBalance => "Show the August 2026 trial balance.",
            FinanceLedgerAgentReadToolIds.ReadStatement => "Show the closed-period balance sheet snapshot.",
            FinanceLedgerAgentReadToolIds.ReadReportDefinitions => "Which report definition version is active?",
            FinanceLedgerAgentReadToolIds.ReadReportSnapshot => "Read this report snapshot using its checksum.",
            FinanceLedgerAgentReadToolIds.ReadSourceDrilldown => "Show the journals behind this report line.",
            FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers => "What blocks the August close?",
            FinanceCloseComplianceAgentToolIds.ExplainCompliancePreparation => "What evidence is missing for this filing obligation?",
            FinanceCloseComplianceAgentToolIds.ExplainAuditPackageCompleteness => "Is this audit package technically complete?",
            FinanceCloseComplianceAgentToolIds.ExplainYearEndPrerequisites => "What still blocks year-end rollover?",
            _ when FinanceCloseComplianceAgentToolIds.Contains(toolName) => "Show the current close and compliance evidence.",
            FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary => "How should we value our inventory and calculate COGS?",
            FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview => "Explain this reconciliation exception and its confidence evidence.",
            FinanceAdvancedAccountingAgentToolIds.RecommendRateEvidenceRemediation => "Which stale or unapproved exchange-rate evidence needs review?",
            FinanceAdvancedAccountingAgentToolIds.RecommendScheduleAssetReview => "What needs review for this schedule or fixed asset?",
            FinanceAdvancedAccountingAgentToolIds.PrioritizeSubledgerExceptions => "Prioritize the settlement exceptions for this invoice.",
            FinanceAccountingDraftAgentToolIds.CreateManualJournalDraft => "Prepare a balanced manual journal draft from these current source records.",
            FinanceAccountingDraftAgentToolIds.CreateCorrectionDraft => "Prepare a correction draft linked to this original journal.",
            FinanceAccountingDraftAgentToolIds.CreateReconciliationDecisionDraft => "Prepare a reconciliation decision draft without applying the match.",
            FinanceAccountingDraftAgentToolIds.CreateAccountingTreatmentDraft => "Prepare a reviewable accounting-treatment draft for this bill.",
            FinanceAccountingDraftAgentToolIds.SubmitForApproval => "Submit this reviewed current draft for approval without posting it.",
            FinanceOperationalProposalAgentToolIds.ProposeCloseTaskAssignment => "Propose assigning this close task to its responsible owner.",
            FinanceOperationalProposalAgentToolIds.ProposeEvidenceRequest => "Prepare an evidence request for this close or compliance blocker.",
            FinanceOperationalProposalAgentToolIds.ProposeComplianceChecklist => "Prepare the current compliance evidence checklist without filing it.",
            FinanceOperationalProposalAgentToolIds.PreviewAuditPackage => "Preview the audit-package definition without generating an artifact.",
            FinanceOperationalProposalAgentToolIds.ProposeAccountingSchedule => "Prepare and validate an unposted accounting schedule proposal.",
            FinanceOperationalProposalAgentToolIds.PreviewCurrencyRevaluation => "Calculate an unposted currency-revaluation proposal.",
            FinanceOperationalProposalAgentToolIds.ProposeFixedAssetAddition => "Validate a fixed-asset addition proposal without registering it.",
            FinanceOperationalProposalAgentToolIds.ProposeFixedAssetDisposal => "Calculate a fixed-asset disposal proposal without posting it.",
            FinanceOperationalProposalAgentToolIds.PreviewFixedAssetDepreciation => "Calculate fixed-asset depreciation without posting it.",
            FinanceOperationalProposalAgentToolIds.SubmitForApproval => "Submit a current schedule or revaluation proposal for independent approval.",
            FinanceOperationalProposalAgentToolIds.AssignCloseTask => "Assign the current eligible close task without signing it off.",
            FinanceOperationalProposalAgentToolIds.RequestEvidence => "Create a typed evidence task and optional handoff without completing evidence.",
            FinanceOperationalProposalAgentToolIds.RequestAuditPackageGeneration => "Request approval-gated background audit-package generation.",
            _ when FinanceAdvancedAccountingAgentToolIds.Contains(toolName) => "Show the current advanced-accounting evidence and versions.",
            "categorize_transaction" => "Categorize this transaction as office costs.",
            FinanceGuardedCommandToolIds.CategorizeTransactions => "Categorize these reviewed transactions and report every accepted or rejected item.",
            "approve_invoice" => "Approve invoice 1042.",
            "post_paid_supplier_bill_expense" => "Post the expense for paid bill 88.",
            _ when isMigration => "Show the current migration status.",
            _ => "Answer this supported Finance request."
        };
        var intents = toolName == FinanceAgentAnalysisToolIds.Analyze
            ? new[] { "cash", "liquidity", "payables", "receivables", "accounting", "treatment", "close", "cadence" }
            : toolName.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();

        return new ToolSelectionMetadata(
            purpose,
            action,
            targetTypes,
            isExecute ? "May change Finance state only after current policy checkpoints."
                : isAccountingDraft ? "Creates reviewable internal draft state but never posts or applies accounting."
                : "Does not change Finance state.",
            evidence,
            toolName is "get_cash_balance" or "list_transactions" or "list_uncategorized_transactions" ? 300 : 86_400,
            isExecute ? "explicit_confirmation" : "not_required",
            isExecute ? "policy_determined" : "not_required",
            actionType == ToolActionType.Recommend
                ? "Returns a reviewable recommendation, not an executed action."
                : isExecute
                    ? "Returns the authoritative post-action state or a non-success status."
                    : "Returns bounded authoritative Finance data with source semantics.",
            [example],
            intents);
    }

    private static class FinanceInputSchemas
    {
        public static JsonObject AgentAnalysis() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "analysisType" ],
                  "properties": {
                    "analysisType": { "type": "string", "enum": [ "cash_liquidity", "payables", "receivables", "accounting_treatment", "close_analysis", "operating_cadence" ] },
                    "subjectId": { "type": "string", "format": "uuid" },
                    "horizonDays": { "type": "integer", "minimum": 1, "maximum": 365 },
                    "objective": { "type": "string", "maxLength": 2000 },
                    "asOfUtc": { "type": "string", "format": "date-time" },
                    "cadence": { "type": "string", "enum": [ "on_demand", "daily", "weekly", "monthly" ] }
                  }
                }
                """);

        public static JsonObject AsOfDate() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "asOfUtc": { "type": "string", "format": "date-time" }
                  }
                }
                """);

        public static JsonObject ListRange() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "startUtc": { "type": "string", "format": "date-time" },
                    "endUtc": { "type": "string", "format": "date-time" },
                    "limit": { "type": "integer", "minimum": 1, "maximum": 500 }
                  }
                }
                """);

        public static JsonObject AgentQuery() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "queryText" ],
                  "properties": {
                    "queryText": { "type": "string", "minLength": 1, "maxLength": 200 },
                    "asOfUtc": { "type": "string", "format": "date-time" }
                  }
                }
                """);

        public static JsonObject ProfitAndLoss() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "year", "month" ],
                  "properties": {
                    "year": { "type": "integer", "minimum": 2000, "maximum": 2100 },
                    "month": { "type": "integer", "minimum": 1, "maximum": 12 }
                  }
                }
                """);

        public static JsonObject TransactionRecommendation() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "transactionId" ],
                  "properties": {
                    "transactionId": { "type": "string", "format": "uuid" },
                    "candidateCategory": { "type": "string", "minLength": 1, "maxLength": 64 }
                  }
                }
                """);

        public static JsonObject InvoiceRecommendation() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "invoiceId" ],
                  "properties": {
                    "invoiceId": { "type": "string", "format": "uuid" },
                    "candidateStatus": {
                      "type": "string",
                      "enum": [ "approved", "rejected" ]
                    }
                  }
                }
                """);

        public static JsonObject TransactionAnomalyEvaluation() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "transactionId" ],
                  "properties": {
                    "transactionId": { "type": "string", "format": "uuid" },
                    "workflowInstanceId": { "type": "string", "format": "uuid" }
                  }
                }
                """);

        public static JsonObject CategorizeTransaction() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "transactionId", "category" ],
                  "properties": {
                    "transactionId": { "type": "string", "format": "uuid" },
                    "category": { "type": "string", "minLength": 1, "maxLength": 64 }
                  }
                }
                """);

        public static JsonObject ApproveInvoice() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "invoiceId" ],
                  "properties": {
                    "invoiceId": { "type": "string", "format": "uuid" },
                    "status": {
                      "type": "string",
                      "enum": [ "approved", "rejected" ]
                    }
                  }
                }
                """);

        public static JsonObject PostPaidSupplierBillExpense() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "billId" ],
                  "properties": {
                    "billId": { "type": "string", "format": "uuid" },
                    "providerKey": {
                      "type": "string",
                      "enum": [ "fortnox" ]
                    }
                  }
                }
                """);

        public static JsonObject CategorizeTransactions() => Object(["idempotencyKey", "items"], new()
        {
            ["idempotencyKey"] = String(8, 200),
            ["items"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = FinanceGuardedCommandContract.MaximumCategorizationBatchSize,
                ["items"] = Object(["transactionId", "expectedCategory", "category"], new()
                {
                    ["transactionId"] = Uuid(),
                    ["expectedCategory"] = String(1, 64),
                    ["category"] = String(1, 64)
                })
            }
        });

        public static JsonObject LedgerRead(string toolName) => toolName switch
        {
            FinanceLedgerAgentReadToolIds.LookupAccounts => Object([], new()
            {
                ["accountId"] = Uuid(), ["search"] = String(1, 128), ["accountClass"] = String(1, 50),
                ["status"] = String(1, 30), ["requireUnique"] = Boolean(), ["catalogKey"] = String(1, 100),
                ["catalogVersion"] = String(1, 100), ["groupCode"] = String(1, 50), ["k2Only"] = Boolean(),
                ["excludeExisting"] = Boolean(), ["skip"] = Integer(0, 10_000),
                ["take"] = Integer(1, FinanceLedgerAgentReadContract.MaximumLookupPageSize)
            }),
            FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => Object([], new()
            {
                ["fiscalPeriodId"] = Uuid(), ["reference"] = String(1, 128)
            }),
            FinanceLedgerAgentReadToolIds.SearchJournals => Object([], new()
            {
                ["ledgerEntryId"] = Uuid(), ["from"] = Date(), ["to"] = Date(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceLedgerAgentReadContract.MaximumJournalPageSize),
                ["search"] = String(1, 128), ["sourceType"] = String(1, 80),
                ["sourceId"] = String(1, 128), ["sourceVersion"] = String(1, 80),
                ["postingType"] = String(1, 80), ["voucherSeriesCode"] = String(1, 20)
            }),
            FinanceLedgerAgentReadToolIds.ReadGeneralLedger => Object(["fiscalPeriodId"], new()
            {
                ["fiscalPeriodId"] = Uuid(), ["accountId"] = Uuid(), ["page"] = Integer(1, 100_000),
                ["pageSize"] = Integer(1, FinanceLedgerAgentReadContract.MaximumPageSize)
            }),
            FinanceLedgerAgentReadToolIds.ReadTrialBalance => Object(["fiscalPeriodId"], new()
            {
                ["fiscalPeriodId"] = Uuid()
            }),
            FinanceLedgerAgentReadToolIds.ReadStatement => Object(["fiscalPeriodId", "reportKind"], new()
            {
                ["fiscalPeriodId"] = Uuid(), ["reportKind"] = String(1, 80), ["cashFlowMethod"] = StringEnum("indirect", "direct"),
                ["comparisonFiscalPeriodId"] = Uuid(), ["rollingPeriodCount"] = Integer(1, 60), ["asOfDate"] = Date(),
                ["dimensionTypeId"] = Uuid(), ["dimensionMemberId"] = Uuid(), ["page"] = Integer(1, 100_000),
                ["pageSize"] = Integer(1, FinanceLedgerAgentReadContract.MaximumPageSize), ["definitionVersionId"] = Uuid()
            }),
            FinanceLedgerAgentReadToolIds.ReadReportDefinitions => Object([], new()
            {
                ["definitionVersionId"] = Uuid(), ["reference"] = String(1, 128), ["requireUnique"] = Boolean(),
                ["skip"] = Integer(0, 100_000), ["take"] = Integer(1, FinanceLedgerAgentReadContract.MaximumLookupPageSize)
            }),
            FinanceLedgerAgentReadToolIds.ReadReportSnapshot => Object(["snapshotId"], new()
            {
                ["snapshotId"] = Uuid(), ["expectedChecksum"] = String(1, 128), ["definitionVersionId"] = Uuid()
            }),
            FinanceLedgerAgentReadToolIds.ReadSourceDrilldown => Object(["fiscalPeriodId", "reportKind", "lineKey"], new()
            {
                ["fiscalPeriodId"] = Uuid(), ["reportKind"] = String(1, 80), ["lineKey"] = String(1, 128),
                ["snapshotId"] = Uuid(), ["page"] = Integer(1, 100_000),
                ["pageSize"] = Integer(1, FinanceLedgerAgentReadContract.MaximumPageSize)
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName))
        };

        public static JsonObject CloseCompliance(string toolName) => toolName switch
        {
            FinanceCloseComplianceAgentToolIds.ReadTemplates => Object([], new()
            {
                ["templateId"] = Uuid(), ["status"] = String(1, 30), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ReadInstance => Object(["closeInstanceId"], new()
            {
                ["closeInstanceId"] = Uuid()
            }),
            FinanceCloseComplianceAgentToolIds.ReadReadiness => Object([], new()
            {
                ["closeInstanceId"] = Uuid(), ["fiscalPeriodId"] = Uuid()
            }),
            FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers => Object([], new()
            {
                ["closeInstanceId"] = Uuid(), ["fiscalPeriodId"] = Uuid(),
                ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ReadPeriodLockHistory => Object(["fiscalPeriodId"], new()
            {
                ["fiscalPeriodId"] = Uuid()
            }),
            FinanceCloseComplianceAgentToolIds.ReadComplianceObligations => Object([], new()
            {
                ["obligationId"] = Uuid(), ["from"] = Date(), ["to"] = Date(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ExplainCompliancePreparation => Object(["obligationId"], new()
            {
                ["obligationId"] = Uuid(), ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ReadAuditPackages => Object([], new()
            {
                ["packageId"] = Uuid(), ["fiscalPeriodId"] = Uuid(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ExplainAuditPackageCompleteness => Object(["packageId"], new()
            {
                ["packageId"] = Uuid(), ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity => Object([], new()
            {
                ["grantId"] = Uuid(), ["engagementId"] = Uuid(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ReadYearEnd => Object([], new()
            {
                ["runId"] = Uuid(), ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ExplainYearEndPrerequisites => Object(["runId"], new()
            {
                ["runId"] = Uuid(), ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName))
        };

        public static JsonObject AdvancedAccounting(string toolName) => toolName switch
        {
            FinanceAdvancedAccountingAgentToolIds.ReadStatementImports => Object([], new()
            {
                ["jobId"] = Uuid(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadReconciliation => Object([], new()
            {
                ["groupId"] = Uuid(), ["bankTransactionId"] = Uuid(),
                ["reconciliationKind"] = StringEnum("advanced", "bank"), ["status"] = String(1, 40), ["search"] = String(1, 160),
                ["maximumConfidence"] = Number(0, 1), ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview => Object(["groupId"], new()
            {
                ["groupId"] = Uuid(), ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadSubledgerSettlement or
                FinanceAdvancedAccountingAgentToolIds.PrioritizeSubledgerExceptions => Object([], new()
            {
                ["allocationId"] = Uuid(), ["paymentId"] = Uuid(), ["invoiceId"] = Uuid(), ["billId"] = Uuid(),
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadPaymentBatches => Object([], new()
            {
                ["batchId"] = Uuid(), ["executionId"] = Uuid(), ["status"] = String(1, 40),
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadExchangeRates => Object([], new()
            {
                ["observationId"] = Uuid(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadRevaluation => Object([], new()
            {
                ["runId"] = Uuid(), ["fiscalPeriodId"] = Uuid(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.RecommendRateEvidenceRemediation => Object([], new()
            {
                ["fiscalPeriodId"] = Uuid(), ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadDimensions => Object([], new()
            {
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadSchedules => Object([], new()
            {
                ["scheduleId"] = Uuid(), ["status"] = String(1, 40), ["includePreview"] = Boolean(),
                ["skip"] = Integer(0, 100_000), ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadFixedAssets => Object([], new()
            {
                ["assetId"] = Uuid(), ["assetClassId"] = Uuid(), ["status"] = String(1, 40), ["search"] = String(1, 160),
                ["periodStart"] = Date(), ["periodEnd"] = Date(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.RecommendScheduleAssetReview => Object([], new()
            {
                ["scheduleId"] = Uuid(), ["assetId"] = Uuid(),
                ["take"] = Integer(1, FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            }),
            FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary => Object([], new()),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName))
        };

        public static JsonObject AccountingDraft(string toolName)
        {
            if (string.Equals(toolName, FinanceAccountingDraftAgentToolIds.SubmitForApproval, StringComparison.OrdinalIgnoreCase))
                return Object(["draftId", "expectedVersion", "expectedPayloadHash", "idempotencyKey", "reviewed"], new()
                {
                    ["draftId"] = Uuid(), ["expectedVersion"] = PositiveInteger(),
                    ["expectedPayloadHash"] = String(64, 64), ["idempotencyKey"] = String(8, 200),
                    ["reviewed"] = Boolean()
                });

            if (string.Equals(toolName, FinanceAccountingDraftAgentToolIds.CreateReconciliationDecisionDraft, StringComparison.OrdinalIgnoreCase))
                return Object(["draft", "idempotencyKey", "rationale", "sourceRecords"], new()
                {
                    ["draft"] = ReconciliationDraft(), ["idempotencyKey"] = String(8, 200),
                    ["rationale"] = String(1, 1000), ["sourceRecords"] = SourceRecords()
                });

            var required = new List<string> { "draft", "idempotencyKey", "rationale" };
            var properties = new JsonObject
            {
                ["draft"] = ManualJournalDraft(), ["idempotencyKey"] = String(8, 200),
                ["rationale"] = String(1, 1000), ["modelProposedFields"] = StringArray(25, 80)
            };
            if (string.Equals(toolName, FinanceAccountingDraftAgentToolIds.CreateAccountingTreatmentDraft, StringComparison.OrdinalIgnoreCase))
            {
                required.Add("billId"); required.Add("selectedAccountId");
                properties["billId"] = Uuid(); properties["selectedAccountId"] = Uuid();
            }
            return Object(required, properties);
        }

        public static JsonObject OperationalProposal(string toolName)
        {
            var reviewed = new JsonObject
            {
                ["expectedProposalHash"] = String(64, 64), ["reviewed"] = Boolean(),
                ["idempotencyKey"] = String(8, 200)
            };
            return toolName switch
            {
                FinanceOperationalProposalAgentToolIds.ProposeCloseTaskAssignment => Object(
                    ["closeInstanceId", "closeTaskId", "ownerUserId"], new()
                    {
                        ["closeInstanceId"] = Uuid(), ["closeTaskId"] = Uuid(), ["ownerUserId"] = Uuid()
                    }),
                FinanceOperationalProposalAgentToolIds.AssignCloseTask => Object(
                    ["closeInstanceId", "closeTaskId", "ownerUserId", "expectedVersion", "expectedProposalHash", "idempotencyKey", "reviewed"],
                    Merge(reviewed, ("closeInstanceId", Uuid()), ("closeTaskId", Uuid()),
                        ("ownerUserId", Uuid()), ("expectedVersion", NonNegativeInteger()))),
                FinanceOperationalProposalAgentToolIds.ProposeEvidenceRequest => EvidenceRequest(execute: false),
                FinanceOperationalProposalAgentToolIds.RequestEvidence => EvidenceRequest(execute: true),
                FinanceOperationalProposalAgentToolIds.ProposeComplianceChecklist => Object(["obligationId"], new()
                    { ["obligationId"] = Uuid() }),
                FinanceOperationalProposalAgentToolIds.PreviewAuditPackage => AuditPackage(execute: false),
                FinanceOperationalProposalAgentToolIds.RequestAuditPackageGeneration => AuditPackage(execute: true),
                FinanceOperationalProposalAgentToolIds.ProposeAccountingSchedule => Object(["schedule", "idempotencyKey"], new()
                {
                    ["schedule"] = OpenObject(), ["idempotencyKey"] = String(8, 200),
                    ["scheduleId"] = Uuid(), ["expectedVersion"] = NonNegativeInteger()
                }),
                FinanceOperationalProposalAgentToolIds.PreviewCurrencyRevaluation => Object(
                    ["fiscalPeriodId", "voucherSeriesCode", "idempotencyKey"], new()
                    {
                        ["fiscalPeriodId"] = Uuid(), ["voucherSeriesCode"] = String(1, 30),
                        ["idempotencyKey"] = String(8, 200)
                    }),
                FinanceOperationalProposalAgentToolIds.ProposeFixedAssetAddition => Object(["asset"], new()
                    { ["asset"] = OpenObject() }),
                FinanceOperationalProposalAgentToolIds.ProposeFixedAssetDisposal => Object(
                    ["assetId", "disposalDate", "fiscalPeriodId", "proceedsAccountId", "proceeds", "expectedVersion", "sourceVersion"], new()
                    {
                        ["assetId"] = Uuid(), ["disposalDate"] = Date(), ["fiscalPeriodId"] = Uuid(),
                        ["proceedsAccountId"] = Uuid(), ["proceeds"] = Number(0, decimal.MaxValue),
                        ["expectedVersion"] = NonNegativeInteger(), ["sourceVersion"] = String(1, 200)
                    }),
                FinanceOperationalProposalAgentToolIds.PreviewFixedAssetDepreciation => Object(
                    ["fiscalPeriodId", "periodStart", "periodEnd"], new()
                    { ["fiscalPeriodId"] = Uuid(), ["periodStart"] = Date(), ["periodEnd"] = Date() }),
                FinanceOperationalProposalAgentToolIds.SubmitForApproval => Object(
                    ["proposalKind", "targetId", "expectedVersion", "expectedProposalHash", "idempotencyKey", "reviewed"],
                    Merge(reviewed, ("proposalKind", StringEnum("accounting_schedule", "currency_revaluation")),
                        ("targetId", Uuid()), ("expectedVersion", NonNegativeInteger()))),
                _ => throw new ArgumentOutOfRangeException(nameof(toolName))
            };
        }

        private static JsonObject EvidenceRequest(bool execute)
        {
            var properties = new JsonObject
            {
                ["scopeType"] = StringEnum("close_task", "compliance_obligation"),
                ["targetId"] = Uuid(), ["closeInstanceId"] = Uuid(), ["title"] = String(1, 200),
                ["description"] = String(1, 2000), ["priority"] = StringEnum("low", "normal", "high", "urgent"),
                ["dueAt"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["assignedAgentId"] = Uuid(), ["receivingAgentId"] = Uuid()
            };
            var required = new List<string> { "scopeType", "targetId", "title", "description" };
            if (execute)
            {
                required.Add("expectedProposalHash"); required.Add("reviewed");
                properties["expectedProposalHash"] = String(64, 64); properties["reviewed"] = Boolean();
            }
            return Object(required, properties);
        }

        private static JsonObject AuditPackage(bool execute)
        {
            var properties = new JsonObject
            {
                ["fiscalPeriodId"] = Uuid(), ["scopeKey"] = String(1, 100),
                ["scopeVersion"] = String(1, 64)
            };
            var required = new List<string> { "fiscalPeriodId" };
            if (execute)
            {
                required.AddRange(["expectedProposalHash", "idempotencyKey", "reviewed"]);
                properties["expectedProposalHash"] = String(64, 64);
                properties["idempotencyKey"] = String(8, 200); properties["reviewed"] = Boolean();
            }
            return Object(required, properties);
        }

        private static JsonObject Merge(JsonObject source, params (string Name, JsonObject Schema)[] items)
        {
            var result = source.DeepClone().AsObject();
            foreach (var item in items) result[item.Name] = item.Schema;
            return result;
        }

        private static JsonObject OpenObject() => new() { ["type"] = "object", ["additionalProperties"] = true };
        private static JsonObject NonNegativeInteger() => Integer(0, int.MaxValue);

        private static JsonObject ManualJournalDraft() => Object(
            ["fiscalPeriodId", "voucherSeriesCode", "documentDate", "postingDate", "explanation", "currency", "lines", "evidenceDocumentIds", "sourceRecords"],
            new()
            {
                ["fiscalPeriodId"] = Uuid(), ["voucherSeriesCode"] = String(1, 32),
                ["documentDate"] = Date(), ["postingDate"] = Date(), ["explanation"] = String(1, 1000),
                ["currency"] = String(3, 3), ["lines"] = new JsonObject
                {
                    ["type"] = "array", ["minItems"] = 1, ["maxItems"] = FinanceAccountingDraftAgentContract.MaximumLines,
                    ["items"] = Object(["financeAccountId", "debitAmount", "creditAmount"], new()
                    {
                        ["financeAccountId"] = Uuid(), ["debitAmount"] = Number(0, decimal.MaxValue),
                        ["creditAmount"] = Number(0, decimal.MaxValue), ["description"] = String(1, 500),
                        ["costCenterId"] = Uuid(), ["dimensionMemberIds"] = UuidArray(100),
                        ["taxFacts"] = StringMap(50, 100), ["dimensionFacts"] = StringMap(100, 200)
                    })
                },
                ["evidenceDocumentIds"] = UuidArray(FinanceAccountingDraftAgentContract.MaximumEvidenceRecords),
                ["originalLedgerEntryId"] = Uuid(), ["correctionReason"] = String(1, 1000),
                ["sourceRecords"] = SourceRecords()
            });

        private static JsonObject SourceRecords() => new()
        {
            ["type"] = "array", ["minItems"] = 1,
            ["maxItems"] = FinanceAccountingDraftAgentContract.MaximumEvidenceRecords,
            ["items"] = Object(["sourceType", "recordId", "sourceVersion"], new()
            {
                ["sourceType"] = StringEnum("ledger_journal", "bank_transaction", "payment", "invoice", "bill"),
                ["recordId"] = Uuid(), ["sourceVersion"] = String(1, 200)
            })
        };

        private static JsonObject ReconciliationDraft() => Object(
            ["reference", "counterparty", "currency", "nodes", "edges"], new()
            {
                ["reference"] = String(1, 200), ["counterparty"] = String(1, 200), ["currency"] = String(3, 3),
                ["ruleVersion"] = PositiveInteger(), ["correctionOfGroupId"] = Uuid(),
                ["nodes"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 100, ["items"] = new JsonObject { ["type"] = "object" } },
                ["edges"] = new JsonObject { ["type"] = "array", ["maxItems"] = 100, ["items"] = new JsonObject { ["type"] = "object" } }
            });

        private static JsonObject UuidArray(int maximum) => new()
        {
            ["type"] = "array", ["maxItems"] = maximum, ["items"] = Uuid()
        };
        private static JsonObject StringArray(int maximum, int maxLength) => new()
        {
            ["type"] = "array", ["maxItems"] = maximum, ["items"] = String(1, maxLength)
        };
        private static JsonObject StringMap(int maximum, int maxLength) => new()
        {
            ["type"] = "object", ["maxProperties"] = maximum,
            ["additionalProperties"] = String(1, maxLength)
        };

        private static JsonObject Object(IEnumerable<string> required, JsonObject properties) => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(required.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["properties"] = properties
        };

        private static JsonObject Boolean() => new() { ["type"] = "boolean" };
        private static JsonObject Integer(int minimum, int maximum) => new()
        {
            ["type"] = "integer", ["minimum"] = minimum, ["maximum"] = maximum
        };
        private static JsonObject Number(decimal minimum, decimal maximum) => new()
        {
            ["type"] = "number", ["minimum"] = minimum, ["maximum"] = maximum
        };
        private static JsonObject Date() => new() { ["type"] = "string", ["format"] = "date" };

        public static JsonObject MigrationRead() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "switchId" ],
                  "properties": {
                    "switchId": { "type": "string", "format": "uuid" },
                    "limit": { "type": "integer", "minimum": 1, "maximum": 50 }
                  }
                }
                """);

        public static JsonObject MigrationRecommendation() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "switchId": { "type": "string", "format": "uuid" },
                    "sourceKind": { "type": "string", "enum": [ "internal", "external" ] },
                    "sourceProviderKey": { "type": "string", "minLength": 1, "maxLength": 64 },
                    "targetKind": { "type": "string", "enum": [ "internal", "external" ] },
                    "targetProviderKey": { "type": "string", "minLength": 1, "maxLength": 64 },
                    "requestedStrategy": { "type": "string", "enum": [ "opening_balances_and_open_items", "current_fiscal_year", "full_history" ] },
                    "focusRecordId": { "type": "string", "format": "uuid" },
                    "gapId": { "type": "string", "format": "uuid" },
                    "limit": { "type": "integer", "minimum": 1, "maximum": 50 }
                  }
                }
                """);

        public static JsonObject MigrationExecute(params (string Name, JsonObject Schema)[] extraProperties)
        {
            var required = new JsonArray("switchId", "expectedSwitchVersion", "idempotencyKey");
            var properties = new JsonObject
            {
                ["switchId"] = Uuid(),
                ["expectedSwitchVersion"] = PositiveInteger(),
                ["idempotencyKey"] = String(8, 200)
            };
            foreach (var (name, schema) in extraProperties)
            {
                required.Add(name);
                properties[name] = schema.DeepClone();
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = required,
                ["properties"] = properties
            };
        }

        public static JsonObject Uuid() => new() { ["type"] = "string", ["format"] = "uuid" };
        public static JsonObject PositiveInteger() => new() { ["type"] = "integer", ["minimum"] = 1 };
        public static JsonObject String(int minLength, int maxLength) => new()
        {
            ["type"] = "string", ["minLength"] = minLength, ["maxLength"] = maxLength
        };
        public static JsonObject StringEnum(params string[] values) => new()
        {
            ["type"] = "string",
            ["enum"] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
        };
    }

    private static class FinanceOutputSchemas
    {
        public static JsonObject WithDataProperty(string dataProperty) =>
            ParseSchema(
                $$"""
                {
                  "type": "object",
                  "required": [ "schemaVersion", "status", "success", "userSafeSummary", "data" ],
                  "properties": {
                    "schemaVersion": { "type": "string" },
                    "status": { "type": "string" },
                    "success": { "type": "boolean" },
                    "userSafeSummary": { "type": "string" },
                    "data": {
                      "type": "object",
                      "required": [ "{{dataProperty}}" ],
                      "properties": {
                        "{{dataProperty}}": { "type": [ "object", "array" ] }
                      }
                    }
                  }
                }
                """);
    }

    private static JsonObject ParseSchema(string schema) =>
        JsonNode.Parse(schema)!.AsObject();
}
