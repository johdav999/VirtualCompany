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
                    : null)))
          .Concat(SalesToolDefinitions.Select(definition => Register(definition.ToolName, new HashSet<ToolActionType> { definition.ActionType }, salesScopes, definition.Version, definition.InputSchema, definition.OutputSchema)))
          .Concat(MarketingToolDefinitions.Select(definition => Register(definition.ToolName, new HashSet<ToolActionType> { definition.ActionType }, marketingScopes, definition.Version, definition.InputSchema, definition.OutputSchema)));

        _tools = registrations.ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
        _definitions = FinanceToolDefinitions.Concat(SalesToolDefinitions).Concat(MarketingToolDefinitions)
            .ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
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
        FinanceToolRiskClassification? financeRiskClassification = null) =>
        new(
            toolName,
            supportedActions ?? StandardActions,
            scopes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            version,
            inputSchema?.DeepClone().AsObject(),
            outputSchema?.DeepClone().AsObject(),
            sensitiveAction,
            financeRiskClassification);

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
        var isMigration = toolName.StartsWith("accounting_provider_switch.", StringComparison.OrdinalIgnoreCase);
        string[] targetTypes = toolName switch
        {
            "list_transactions" or "list_uncategorized_transactions" or "recommend_transaction_category" or
                "evaluate_transaction_anomaly" or "categorize_transaction" => ["transaction"],
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
            _ when isMigration => [FinancePlanningReferenceTypes.Migration, FinancePlanningReferenceTypes.FiscalPeriod],
            _ => Array.Empty<string>()
        };
        string[] evidence = toolName switch
        {
            "get_cash_balance" => ["authoritative_cash_snapshot"],
            "get_profit_and_loss_summary" => ["posted_ledger_entries", "fiscal_period"],
            "recommend_transaction_category" or "categorize_transaction" or "evaluate_transaction_anomaly" =>
                ["finance_transaction"],
            "recommend_invoice_approval_decision" or "approve_invoice" or "list_invoices_awaiting_approval" =>
                ["finance_invoice"],
            "post_paid_supplier_bill_expense" => ["supplier_bill", "posting_eligibility"],
            FinanceAgentAnalysisToolIds.Analyze => ["authoritative_finance_analysis_evidence"],
            _ when FinanceLedgerAgentReadToolIds.Contains(toolName) => ["posted_ledger_entries", "fiscal_period", "report_snapshot"],
            _ when FinanceCloseComplianceAgentToolIds.Contains(toolName) => ["close_evidence", "compliance_evidence", "audit_package_metadata", "year_end_readiness"],
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
            "categorize_transaction" => "Change the category of one grounded transaction after supervision.",
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
            "categorize_transaction" => "Categorize this transaction as office costs.",
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
            isExecute ? "May change Finance state only after current policy checkpoints." : "Does not change Finance state.",
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

        public static JsonObject LedgerRead(string toolName) => toolName switch
        {
            FinanceLedgerAgentReadToolIds.LookupAccounts => Object([], new()
            {
                ["accountId"] = Uuid(), ["search"] = String(1, 128), ["accountClass"] = String(1, 50),
                ["status"] = String(1, 30), ["requireUnique"] = Boolean(), ["catalogKey"] = String(1, 100),
                ["catalogVersion"] = String(1, 100), ["groupCode"] = String(1, 50), ["k2Only"] = Boolean(),
                ["excludeExisting"] = Boolean(), ["skip"] = Integer(0, 10_000), ["take"] = Integer(1, 100)
            }),
            FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => Object([], new()
            {
                ["fiscalPeriodId"] = Uuid(), ["reference"] = String(1, 128)
            }),
            FinanceLedgerAgentReadToolIds.SearchJournals => Object([], new()
            {
                ["ledgerEntryId"] = Uuid(), ["from"] = Date(), ["to"] = Date(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, 100), ["search"] = String(1, 128), ["sourceType"] = String(1, 80),
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
                ["definitionVersionId"] = Uuid(), ["reference"] = String(1, 128), ["requireUnique"] = Boolean()
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
            FinanceCloseComplianceAgentToolIds.ReadReadiness or FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers =>
                Object([], new() { ["closeInstanceId"] = Uuid(), ["fiscalPeriodId"] = Uuid() }),
            FinanceCloseComplianceAgentToolIds.ReadPeriodLockHistory => Object(["fiscalPeriodId"], new()
            {
                ["fiscalPeriodId"] = Uuid()
            }),
            FinanceCloseComplianceAgentToolIds.ReadComplianceObligations => Object([], new()
            {
                ["obligationId"] = Uuid(), ["from"] = Date(), ["to"] = Date()
            }),
            FinanceCloseComplianceAgentToolIds.ExplainCompliancePreparation => Object(["obligationId"], new()
            {
                ["obligationId"] = Uuid()
            }),
            FinanceCloseComplianceAgentToolIds.ReadAuditPackages => Object([], new()
            {
                ["packageId"] = Uuid(), ["fiscalPeriodId"] = Uuid(), ["skip"] = Integer(0, 100_000),
                ["take"] = Integer(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            }),
            FinanceCloseComplianceAgentToolIds.ExplainAuditPackageCompleteness => Object(["packageId"], new()
            {
                ["packageId"] = Uuid()
            }),
            FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity => Object([], new()
            {
                ["grantId"] = Uuid(), ["engagementId"] = Uuid()
            }),
            FinanceCloseComplianceAgentToolIds.ReadYearEnd => Object([], new()
            {
                ["runId"] = Uuid(), ["take"] = Integer(1, 100)
            }),
            FinanceCloseComplianceAgentToolIds.ExplainYearEndPrerequisites => Object(["runId"], new()
            {
                ["runId"] = Uuid()
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName))
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
