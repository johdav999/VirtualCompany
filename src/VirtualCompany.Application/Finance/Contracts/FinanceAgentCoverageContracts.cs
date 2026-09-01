using VirtualCompany.Domain.Enums;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public static class FinanceAgentCoverageVersions
{
    public const string V1 = "finance-agent-coverage-v1";
}

public static class FinanceAgentCoverageSupportStates
{
    public const string ImplementedRead = "implemented_read";
    public const string ImplementedRecommendDraft = "implemented_recommend_draft";
    public const string ImplementedExecute = "implemented_execute";
    public const string ConfigurationDependent = "configuration_dependent";
    public const string Unsupported = "unsupported";
    public const string HumanOnly = "human_only";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ImplementedRead,
        ImplementedRecommendDraft,
        ImplementedExecute,
        ConfigurationDependent,
        Unsupported,
        HumanOnly
    };
}

public static class FinanceAgentCoverageAvailabilityReasons
{
    public const string Implemented = "implemented";
    public const string FutureCoverage = "future_agent_coverage";
    public const string IntegrationConfigurationRequired = "integration_configuration_required";
    public const string PermanentHumanAuthority = "permanent_human_authority";
    public const string SegregationOfDuties = "segregation_of_duties";
    public const string AmbiguousExternalOutcome = "ambiguous_external_outcome";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Implemented,
        FutureCoverage,
        IntegrationConfigurationRequired,
        PermanentHumanAuthority,
        SegregationOfDuties,
        AmbiguousExternalOutcome
    };
}

public static class FinanceAgentCoverageCapabilityIds
{
    public const string DailyCash = "finance.daily_cash";
    public const string TransactionReview = "finance.transaction_review";
    public const string InvoiceReview = "finance.invoice_review";
    public const string ManagementReporting = "finance.management_reporting";
    public const string NaturalLanguageQueries = "finance.natural_language_queries";
    public const string RoleAnalysis = "finance.role_analysis";
    public const string PaidBillAccounting = "finance.paid_bill_accounting";
    public const string AccountingProviderMigration = "finance.accounting_provider_migration";
    public const string PayablesOperations = "finance.payables_operations";
    public const string CustomerBillingAndReceivables = "finance.customer_billing_receivables";
    public const string LedgerAndFinancialReporting = "finance.ledger_financial_reporting";
    public const string BankAndReconciliation = "finance.bank_reconciliation";
    public const string AdvancedAccounting = "finance.advanced_accounting";
    public const string CloseAndYearEnd = "finance.close_year_end";
    public const string ComplianceAndAudit = "finance.compliance_audit";
    public const string FinanceAdministration = "finance.administration";
    public const string ApprovalGovernance = "finance.approval_governance";
    public const string ProviderReconciliation = "finance.provider_reconciliation";
}

public sealed record FinanceAgentCoverageOperationManifest(
    string Id,
    string Name,
    string ActionClass,
    string SupportState,
    string RequiredPermission,
    string RequiredScope,
    string RiskTier,
    string ApprovalBehavior,
    IReadOnlyList<string> Integrations,
    IReadOnlyList<string> SourceTypes,
    string AvailabilityReasonCode,
    string SafeExplanation,
    string SafeAlternative,
    string? NavigationPath = null,
    string? ToolName = null,
    IReadOnlyList<string>? PlannerKeywords = null);

public sealed record FinanceAgentCoverageCapabilityManifest(
    string Id,
    string Version,
    string DomainWorkflow,
    string Purpose,
    IReadOnlyList<FinanceAgentCoverageOperationManifest> Operations);

public sealed record FinanceAgentEffectiveCoverageOperationDto(
    string Id,
    string Name,
    string ActionClass,
    string SupportState,
    string EffectiveState,
    string RequiredPermission,
    string RequiredScope,
    string RiskTier,
    string ApprovalBehavior,
    IReadOnlyList<string> Integrations,
    IReadOnlyList<string> SourceTypes,
    string AvailabilityReasonCode,
    string Explanation,
    string SafeAlternative,
    string? NavigationPath,
    string? ToolName);

public sealed record FinanceAgentEffectiveCoverageCapabilityDto(
    string Id,
    string Version,
    string DomainWorkflow,
    string Purpose,
    IReadOnlyList<string> SupportedOperations,
    IReadOnlyList<string> RequiredPermissions,
    IReadOnlyList<string> RequiredScopes,
    IReadOnlyList<string> RiskTiers,
    IReadOnlyList<string> ApprovalBehaviors,
    IReadOnlyList<string> Integrations,
    IReadOnlyList<string> SourceTypes,
    IReadOnlyList<FinanceAgentEffectiveCoverageOperationDto> Operations);

public sealed record FinanceAgentCoverageCountsDto(
    int TotalCapabilities,
    int TotalOperations,
    int RegisteredTools,
    int ImplementedRead,
    int ImplementedRecommendDraft,
    int ImplementedExecute,
    int ConfigurationDependent,
    int Unsupported,
    int HumanOnly,
    int EffectiveAvailable,
    int EffectiveApprovalRequired,
    int EffectiveGaps);

public sealed record FinanceAgentCoverageGapDto(
    string CapabilityId,
    string OperationId,
    string SupportState,
    string ReasonCode,
    string Explanation,
    string SafeAlternative,
    string? NavigationPath);

public sealed record FinanceAgentEffectiveCoverageDto(
    string CatalogueVersion,
    Guid CompanyId,
    Guid AgentId,
    string AgentName,
    string AgentStatus,
    string AutonomyLevel,
    FinanceAgentCoverageCountsDto Counts,
    IReadOnlyList<FinanceAgentEffectiveCoverageCapabilityDto> Capabilities,
    IReadOnlyList<FinanceAgentCoverageGapDto> Gaps,
    DateTime GeneratedUtc,
    string AuthorityVersion,
    string AuthorityHash);

public interface IFinanceAgentCoverageCatalogue
{
    IReadOnlyList<FinanceAgentCoverageCapabilityManifest> ListManifests();

    Task<FinanceAgentEffectiveCoverageDto> GetEffectiveCoverageAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken);
}

public static class FinanceAgentCoverageCatalogue
{
    private const string Scope = "finance";
    private const string NotApplicable = "not_applicable";
    private const string InternalFinance = "internal_finance";

    public static IReadOnlyList<FinanceAgentCoverageCapabilityManifest> Manifests { get; } = Build();

    public static IReadOnlySet<string> OwnedToolNames { get; } = Manifests
        .SelectMany(capability => capability.Operations)
        .Where(operation => !string.IsNullOrWhiteSpace(operation.ToolName))
        .Select(operation => operation.ToolName!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsOwnedTool(string toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && OwnedToolNames.Contains(toolName.Trim());

    public static FinanceAgentCoverageOperationManifest? MatchHumanOnlyOperation(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return null;

        return Manifests
            .SelectMany(capability => capability.Operations)
            .Where(operation => operation.SupportState == FinanceAgentCoverageSupportStates.HumanOnly)
            .FirstOrDefault(operation => MatchesHumanOnlyIntent(operation, request));
    }

    private static bool MatchesHumanOnlyIntent(FinanceAgentCoverageOperationManifest operation, string request)
    {
        var matchedKeyword = operation.PlannerKeywords?.FirstOrDefault(keyword =>
            request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (matchedKeyword is null) return false;

        // "Close period" is also ordinary Finance vocabulary for status/readiness reads. Keep the
        // permanent boundary deterministic without shadowing those implemented read operations.
        if (operation.Id == "final_close_year_end_authority" &&
            matchedKeyword.Equals("close period", StringComparison.OrdinalIgnoreCase) &&
            CloseReadIntentMarkers.Any(marker => request.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> CloseReadIntentMarkers { get; } =
    [
        "status",
        "readiness",
        "ready",
        "blocker",
        "history",
        "show",
        "list",
        "explain",
        "what",
        "why",
        "when",
        "is closed",
        "was closed"
    ];

    private static IReadOnlyList<FinanceAgentCoverageCapabilityManifest> Build()
    {
        var capabilities = new List<FinanceAgentCoverageCapabilityManifest>
        {
            Capability(FinanceAgentCoverageCapabilityIds.DailyCash, "Daily cash and liquidity",
                "Inspect current cash facts used by daily Finance operations.",
                Read("get_cash_balance", "Read cash balance", ["cash_account", "cash_balance"])),

            Capability(FinanceAgentCoverageCapabilityIds.TransactionReview, "Transaction review and categorization",
                "Inspect transactions, identify review needs, recommend categorization, and apply a supervised category change.",
                Read("list_transactions", "List transactions", ["finance_transaction"]),
                Read("list_uncategorized_transactions", "List uncategorized transactions", ["finance_transaction"]),
                Recommend("recommend_transaction_category", "Recommend transaction category", ["finance_transaction", "accounting_policy"]),
                Recommend("evaluate_transaction_anomaly", "Evaluate transaction anomaly", ["finance_transaction", "historical_transaction"]),
                Execute("categorize_transaction", "Categorize transaction", ["finance_transaction"], "/finance/transactions"),
                Execute(FinanceGuardedCommandToolIds.CategorizeTransactions, "Categorize a bounded transaction batch",
                    ["finance_transaction", "expected_category", "per_item_decision"], "/finance/transactions")),

            Capability(FinanceAgentCoverageCapabilityIds.InvoiceReview, "Invoice review",
                "Inspect invoice-review queues, prepare a decision, and invoke the governed approval-status command.",
                Read("list_invoices_awaiting_approval", "List invoices awaiting approval", ["finance_invoice"]),
                Recommend("recommend_invoice_approval_decision", "Recommend invoice approval decision", ["finance_invoice", "approval_policy"]),
                Execute("approve_invoice", "Change invoice approval status", ["finance_invoice", "approval"], "/finance/reviews")),

            Capability(FinanceAgentCoverageCapabilityIds.ManagementReporting, "Management reporting",
                "Inspect bounded current management-report facts.",
                Read("get_profit_and_loss_summary", "Read profit and loss summary", ["posted_journal", "fiscal_period"])),

            Capability(FinanceAgentCoverageCapabilityIds.NaturalLanguageQueries, "Bounded Finance questions",
                "Resolve the currently supported deterministic Finance questions from authoritative facts.",
                Read("resolve_finance_agent_query", "Resolve supported Finance query", ["cash", "payable", "receivable", "fiscal_period"])),

            Capability(FinanceAgentCoverageCapabilityIds.RoleAnalysis, "Finance role analysis",
                "Prepare evidence-backed cash, payables, receivables, accounting-treatment, close, and cadence analysis.",
                Recommend(FinanceAgentAnalysisToolIds.Analyze, "Analyze Finance evidence", ["finance_analysis_evidence"], [InternalFinance, "shared_ai"])),

            Capability(FinanceAgentCoverageCapabilityIds.PaidBillAccounting, "Paid supplier-bill accounting",
                "Invoke the authoritative eligible paid-bill expense-posting command after supervision.",
                Execute("post_paid_supplier_bill_expense", "Post eligible paid supplier-bill expense", ["supplier_bill", "posting_eligibility"], "/finance/bills")),

            Capability(FinanceAgentCoverageCapabilityIds.AccountingProviderMigration, "Accounting-provider migration",
                "Inspect, recommend, and advance the governed accounting-provider switch workflow.",
                AccountingProviderSwitchAgentToolIds.ReadTools
                    .Select(tool => Read(tool, Humanize(tool), ["accounting_provider_switch"], [InternalFinance, "accounting_provider"]))
                    .Concat(AccountingProviderSwitchAgentToolIds.RecommendationTools.Select(tool =>
                        Recommend(tool, Humanize(tool), ["accounting_provider_switch"], [InternalFinance, "accounting_provider", "shared_ai"])))
                    .Concat(AccountingProviderSwitchAgentToolIds.ExecuteTools.Select(tool =>
                        Execute(tool, Humanize(tool), ["accounting_provider_switch"], "/finance/accounting/connections", [InternalFinance, "accounting_provider"])))
                    .ToArray()),

            Capability(FinanceAgentCoverageCapabilityIds.PayablesOperations, "Payables, bills, and subscriptions",
                "Inspect and coordinate the full supplier-bill, payable, allocation, and subscription lifecycle.",
                Gap("payables_full_lifecycle", "Full payables lifecycle agent coverage", FinanceAgentCoverageSupportStates.Unsupported,
                    "Broad payables tools are scheduled for a later P2 slice.", "Use the Bills, Payments, and Supplier subscriptions workspaces.", "/finance/bills")),

            Capability(FinanceAgentCoverageCapabilityIds.CustomerBillingAndReceivables, "Customer billing and receivables",
                "Inspect and coordinate customer drafts, delivery, collections, allocations, and corrections.",
                Gap("customer_billing_full_lifecycle", "Full billing and receivables agent coverage", FinanceAgentCoverageSupportStates.Unsupported,
                    "Broad customer billing and receivables tools are not registered yet.", "Use the Invoices and Receivables workspaces.", "/finance/invoices")),

            Capability(FinanceAgentCoverageCapabilityIds.LedgerAndFinancialReporting, "Ledger, periods, and financial reporting",
                "Inspect journals, accounts, periods, trial balance, statements, report definitions, and immutable snapshots.",
                FinanceLedgerAgentReadToolIds.All.Select(tool => Read(tool, Humanize(tool), tool switch
                {
                    FinanceLedgerAgentReadToolIds.LookupAccounts => ["accounting_chart", "finance_account"],
                    FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => ["fiscal_period"],
                    FinanceLedgerAgentReadToolIds.SearchJournals => ["posted_journal", "voucher_series", "source_evidence"],
                    FinanceLedgerAgentReadToolIds.ReadGeneralLedger => ["posted_journal", "general_ledger"],
                    FinanceLedgerAgentReadToolIds.ReadTrialBalance => ["posted_journal", "trial_balance", "control_totals"],
                    FinanceLedgerAgentReadToolIds.ReadStatement => ["posted_journal", "financial_statement", "report_mapping"],
                    FinanceLedgerAgentReadToolIds.ReadReportDefinitions => ["report_definition", "report_definition_version"],
                    FinanceLedgerAgentReadToolIds.ReadReportSnapshot => ["immutable_report_snapshot", "checksum"],
                    FinanceLedgerAgentReadToolIds.ReadSourceDrilldown => ["report_line", "posted_journal", "source_evidence"],
                    _ => ["authoritative_finance_records"]
                })).ToArray()),

            Capability(FinanceAgentCoverageCapabilityIds.BankAndReconciliation, "Banking, statement imports, and reconciliation",
                "Inspect bank connectivity and reconcile imported statement evidence.",
                FinanceAdvancedAccountingAgentToolIds.All
                    .Where(tool => tool is FinanceAdvancedAccountingAgentToolIds.ReadStatementImports or
                        FinanceAdvancedAccountingAgentToolIds.ReadReconciliation or
                        FinanceAdvancedAccountingAgentToolIds.ReadSubledgerSettlement or
                        FinanceAdvancedAccountingAgentToolIds.ReadPaymentBatches or
                        FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview or
                        FinanceAdvancedAccountingAgentToolIds.PrioritizeSubledgerExceptions)
                    .Select(tool => FinanceAdvancedAccountingAgentToolIds.RecommendationTools.Contains(tool)
                        ? Recommend(tool, Humanize(tool), ["statement_row", "reconciliation_group", "payment_allocation", "settlement_evidence"])
                        : Read(tool, Humanize(tool), ["statement_import", "reconciliation_group", "payment_allocation", "payment_batch"]))
                    .Prepend(Gap("banking_read", "Connected-bank inspection", FinanceAgentCoverageSupportStates.ConfigurationDependent,
                        "Banking coverage depends on a configured, healthy bank connection and later agent tools.", "Configure and inspect bank connections in Finance settings.", "/finance/settings/bank-connections", ["bank connection", "bank feed"]))
                    .Append(HumanOnly("payment_initiation", "Initiate or release a payment",
                        "Payment initiation and release remain a human Finance operation.",
                        "Laura may prepare evidence or a proposal; continue in Payment batches for human review and release.",
                        "/finance/payments/batches", ["initiate payment", "send payment", "pay supplier", "release payment", "transfer money"],
                        FinancePermissions.Approve))
                    .Append(AccountingDraftRecommend(FinanceAccountingDraftAgentToolIds.CreateReconciliationDecisionDraft,
                        "Create reconciliation decision draft", ["reconciliation_record_version", "reconciliation_rule", "source_evidence"]))
                    .ToArray()),

            Capability(FinanceAgentCoverageCapabilityIds.AdvancedAccounting, "Advanced accounting",
                "Inspect and prepare dimensions, foreign currency, schedules, fixed assets, and manual accounting drafts.",
                FinanceAdvancedAccountingAgentToolIds.All
                    .Where(tool => tool is not FinanceAdvancedAccountingAgentToolIds.ReadStatementImports and
                        not FinanceAdvancedAccountingAgentToolIds.ReadReconciliation and
                        not FinanceAdvancedAccountingAgentToolIds.ReadSubledgerSettlement and
                        not FinanceAdvancedAccountingAgentToolIds.ReadPaymentBatches and
                        not FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview and
                        not FinanceAdvancedAccountingAgentToolIds.PrioritizeSubledgerExceptions)
                    .Select(tool => FinanceAdvancedAccountingAgentToolIds.RecommendationTools.Contains(tool)
                        ? Recommend(tool, Humanize(tool), ["exchange_rate_evidence", "revaluation", "dimension", "schedule", "fixed_asset"])
                        : Read(tool, Humanize(tool), tool == FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary
                            ? ["unsupported_inventory_accounting_boundary"]
                            : ["exchange_rate_set", "revaluation", "dimension", "schedule", "fixed_asset"]))
                    .Concat(FinanceAccountingDraftAgentToolIds.RecommendationTools
                        .Where(tool => tool != FinanceAccountingDraftAgentToolIds.CreateReconciliationDecisionDraft)
                        .Select(tool => AccountingDraftRecommend(tool, Humanize(tool),
                            ["manual_journal_draft", "source_record_version", "accounting_policy", "evidence_document"])))
                    .Append(Execute(FinanceAccountingDraftAgentToolIds.SubmitForApproval,
                        "Submit reviewed accounting draft for approval",
                        ["manual_journal_draft", "source_record_version", "approval_policy"],
                        "/finance/accounting/manual-journals"))
                    .Concat(FinanceOperationalProposalAgentToolIds.RecommendationTools
                        .Where(tool => tool is FinanceOperationalProposalAgentToolIds.ProposeAccountingSchedule or
                            FinanceOperationalProposalAgentToolIds.PreviewCurrencyRevaluation or
                            FinanceOperationalProposalAgentToolIds.ProposeFixedAssetAddition or
                            FinanceOperationalProposalAgentToolIds.ProposeFixedAssetDisposal or
                            FinanceOperationalProposalAgentToolIds.PreviewFixedAssetDepreciation)
                        .Select(tool => AccountingDraftRecommend(tool, Humanize(tool),
                            ["target_version", "deterministic_calculation", "source_evidence", "proposal_checksum"])))
                    .Append(Execute(FinanceOperationalProposalAgentToolIds.SubmitForApproval,
                        "Submit current operational proposal for approval",
                        ["target_version", "proposal_checksum", "approval_policy"],
                        "/finance/accounting"))
                    .ToArray()),

            Capability(FinanceAgentCoverageCapabilityIds.CloseAndYearEnd, "Close and year-end",
                "Inspect close readiness and coordinate period and year-end work without acquiring final authority.",
                FinanceCloseComplianceAgentToolIds.All
                    .Where(tool => tool.StartsWith("finance.close.", StringComparison.Ordinal) || tool.StartsWith("finance.year_end.", StringComparison.Ordinal))
                    .Select(tool => FinanceCloseComplianceAgentToolIds.RecommendationTools.Contains(tool)
                        ? Recommend(tool, Humanize(tool), ["close_readiness", "year_end_readiness", "evidence_hash"])
                        : Read(tool, Humanize(tool), ["close_instance", "close_task", "readiness_snapshot", "period_history", "year_end_run"]))
                    .Concat(new[]
                    {
                        AccountingDraftRecommend(FinanceOperationalProposalAgentToolIds.ProposeCloseTaskAssignment,
                            "Propose close task assignment", ["close_task", "target_version", "materiality_policy"]),
                        AccountingDraftRecommend(FinanceOperationalProposalAgentToolIds.ProposeEvidenceRequest,
                            "Propose evidence request", ["close_task", "compliance_obligation", "source_evidence"]),
                        Execute(FinanceOperationalProposalAgentToolIds.AssignCloseTask,
                            "Assign eligible close task", ["close_task", "target_version", "segregation_of_duties"],
                            "/finance/accounting/close-workspace"),
                        Execute(FinanceOperationalProposalAgentToolIds.RequestEvidence,
                            "Create typed Finance evidence task", ["source_evidence", "responsible_owner", "proposal_checksum"],
                            "/tasks")
                    })
                    .Append(HumanOnly("final_close_year_end_authority", "Final close, lock, reopen, or year-end authority",
                        "Final period close, lock, reopen, and year-end rollover authority remains human-only.",
                        "Laura may prepare readiness evidence; an authorized human must continue in the Close or Year-end workspace.",
                        "/finance/accounting/close-workspace", ["close period", "lock period", "reopen period", "finalize close", "year-end rollover", "roll over year"]))
                    .ToArray()),

            Capability(FinanceAgentCoverageCapabilityIds.ComplianceAndAudit, "Compliance, statutory evidence, and audit",
                "Inspect compliance obligations and audit evidence while preserving statutory and professional authority.",
                FinanceCloseComplianceAgentToolIds.All
                    .Where(tool => tool.StartsWith("finance.compliance.", StringComparison.Ordinal) ||
                                   tool.StartsWith("finance.audit.", StringComparison.Ordinal) ||
                                   tool.StartsWith("finance.accountant.", StringComparison.Ordinal))
                    .Select(tool => FinanceCloseComplianceAgentToolIds.RecommendationTools.Contains(tool)
                        ? Recommend(tool, Humanize(tool), ["compliance_evidence", "audit_package_metadata", "accountant_grant"])
                        : Read(tool, Humanize(tool), ["compliance_obligation", "submission_evidence", "provider_acknowledgement", "audit_package", "accountant_grant"]))
                    .Concat(new[]
                    {
                        AccountingDraftRecommend(FinanceOperationalProposalAgentToolIds.ProposeComplianceChecklist,
                            "Prepare compliance evidence checklist", ["compliance_obligation", "policy_pack", "submission_evidence"]),
                        AccountingDraftRecommend(FinanceOperationalProposalAgentToolIds.PreviewAuditPackage,
                            "Preview audit package definition", ["frozen_scope", "snapshot_versions", "scope_checksum"]),
                        Execute(FinanceOperationalProposalAgentToolIds.RequestAuditPackageGeneration,
                            "Request audit package generation", ["frozen_scope", "approval_policy", "background_execution", "object_checksum"],
                            "/finance/accounting/audit-packages")
                    })
                    .Append(HumanOnly("final_statutory_filing", "Final statutory filing or sign-off",
                        "Final statutory filing, declaration, professional approval, and sign-off remain human-only.",
                        "Laura may prepare a checklist and evidence; an authorized human or qualified professional must file or sign off.",
                        "/finance/accounting/compliance-calendar", ["file vat", "submit vat", "file tax", "statutory filing", "sign tax return", "statutory sign-off"]))
                    .ToArray()),

            Capability(FinanceAgentCoverageCapabilityIds.FinanceAdministration, "Finance integrations and administration",
                "Inspect and configure provider, accounting, mailbox, and Finance administration safely.",
                Gap("finance_integration_setup", "Finance integration availability", FinanceAgentCoverageSupportStates.ConfigurationDependent,
                    "Several Finance workflows require configured providers, connections, or mailboxes.", "Review Finance settings and provider health.", "/finance/settings"),
                HumanOnly("provider_credentials", "Create or change provider credentials",
                    "Credential entry, rotation, consent, and secret changes remain human-only.",
                    "Laura may explain required setup; an administrator must complete it in Finance settings.",
                    "/finance/settings", ["change credentials", "update credentials", "set api key", "rotate secret", "enter password", "bank consent"],
                    FinancePermissions.ManageIntegrations)),

            Capability(FinanceAgentCoverageCapabilityIds.ApprovalGovernance, "Approval governance",
                "Preserve independent review and segregation of duties for Finance actions.",
                HumanOnly("self_approval", "Approve the agent's own work",
                    "Self-approval is permanently prohibited by segregation-of-duties policy.",
                    "Laura may request review from an eligible independent approver.",
                    null, ["approve my own", "self approve", "approve your own", "bypass approval"],
                    FinancePermissions.Approve)),

            Capability(FinanceAgentCoverageCapabilityIds.ProviderReconciliation, "Ambiguous provider outcomes",
                "Keep uncertain provider outcomes visible for evidence-backed reconciliation.",
                HumanOnly("ambiguous_provider_resolution", "Resolve an ambiguous provider outcome",
                    "An ambiguous provider outcome cannot be declared successful or failed by the agent.",
                    "Laura may collect evidence; an authorized operator must reconcile the provider result.",
                    "/system/admin/finance-work", ["resolve ambiguous provider", "force provider outcome", "mark provider succeeded", "ignore provider ambiguity"],
                    FinancePermissions.ManageIntegrations)),
        };

        return capabilities;
    }

    private static FinanceAgentCoverageCapabilityManifest Capability(
        string id,
        string workflow,
        string purpose,
        params FinanceAgentCoverageOperationManifest[] operations) =>
        new(id, "1.0.0", workflow, purpose, operations);

    private static FinanceAgentCoverageOperationManifest Read(
        string toolName,
        string name,
        IReadOnlyList<string> sources,
        IReadOnlyList<string>? integrations = null) =>
        Implemented(toolName, name, ToolActionType.Read, FinancePermissions.View, "read_only", NotApplicable,
            sources, integrations ?? [InternalFinance]);

    private static FinanceAgentCoverageOperationManifest Recommend(
        string toolName,
        string name,
        IReadOnlyList<string> sources,
        IReadOnlyList<string>? integrations = null) =>
        Implemented(toolName, name, ToolActionType.Recommend, FinancePermissions.View, "advisory", NotApplicable,
            sources, integrations ?? [InternalFinance, "shared_ai"]);

    private static FinanceAgentCoverageOperationManifest AccountingDraftRecommend(
        string toolName,
        string name,
        IReadOnlyList<string> sources) =>
        Implemented(toolName, name, ToolActionType.Recommend, FinancePermissions.AccountingAdmin, "advisory", NotApplicable,
            sources, [InternalFinance, "shared_ai"]);

    private static FinanceAgentCoverageOperationManifest Execute(
        string toolName,
        string name,
        IReadOnlyList<string> sources,
        string navigationPath,
        IReadOnlyList<string>? integrations = null)
    {
        var risk = FinanceToolRiskPolicyCatalog.GetRequired(toolName);
        return Implemented(toolName, name, ToolActionType.Execute, risk.RequiredActorPermission, risk.RiskTier,
            risk.DefaultApprovalBehavior, sources, integrations ?? [InternalFinance], navigationPath);
    }

    private static FinanceAgentCoverageOperationManifest Implemented(
        string toolName,
        string name,
        ToolActionType actionType,
        string permission,
        string riskTier,
        string approvalBehavior,
        IReadOnlyList<string> sources,
        IReadOnlyList<string> integrations,
        string? navigationPath = null) =>
        new(
            toolName,
            name,
            actionType.ToStorageValue(),
            actionType switch
            {
                ToolActionType.Read => FinanceAgentCoverageSupportStates.ImplementedRead,
                ToolActionType.Recommend => FinanceAgentCoverageSupportStates.ImplementedRecommendDraft,
                _ => FinanceAgentCoverageSupportStates.ImplementedExecute
            },
            permission,
            Scope,
            riskTier,
            approvalBehavior,
            integrations,
            sources,
            FinanceAgentCoverageAvailabilityReasons.Implemented,
            "This operation has a registered, versioned Finance tool. Effective authority is evaluated separately.",
            navigationPath is null
                ? "Use the owning Finance workspace if the effective authority or integration is unavailable."
                : "Continue in the owning Finance workspace when supervised execution is unavailable.",
            navigationPath,
            toolName);

    private static FinanceAgentCoverageOperationManifest Gap(
        string id,
        string name,
        string supportState,
        string explanation,
        string alternative,
        string? navigationPath,
        IReadOnlyList<string>? keywords = null) =>
        new(
            id,
            name,
            ToolActionType.Read.ToStorageValue(),
            supportState,
            FinancePermissions.View,
            Scope,
            "not_available",
            NotApplicable,
            supportState == FinanceAgentCoverageSupportStates.ConfigurationDependent ? ["provider_configuration"] : [InternalFinance],
            [$"{id}_state"],
            supportState == FinanceAgentCoverageSupportStates.ConfigurationDependent
                ? FinanceAgentCoverageAvailabilityReasons.IntegrationConfigurationRequired
                : FinanceAgentCoverageAvailabilityReasons.FutureCoverage,
            explanation,
            alternative,
            navigationPath,
            PlannerKeywords: keywords);

    private static FinanceAgentCoverageOperationManifest HumanOnly(
        string id,
        string name,
        string explanation,
        string alternative,
        string? navigationPath,
        IReadOnlyList<string> keywords,
        string requiredPermission = FinancePermissions.AccountingAdmin) =>
        new(
            id,
            name,
            ToolActionType.Execute.ToStorageValue(),
            FinanceAgentCoverageSupportStates.HumanOnly,
            requiredPermission,
            Scope,
            FinanceToolRiskTiers.Critical,
            "human_authority_required",
            [InternalFinance],
            [$"{id}_evidence"],
            id == "self_approval"
                ? FinanceAgentCoverageAvailabilityReasons.SegregationOfDuties
                : id == "ambiguous_provider_resolution"
                    ? FinanceAgentCoverageAvailabilityReasons.AmbiguousExternalOutcome
                    : FinanceAgentCoverageAvailabilityReasons.PermanentHumanAuthority,
            explanation,
            alternative,
            navigationPath,
            PlannerKeywords: keywords);

    private static string Humanize(string toolName)
    {
        var value = toolName[(toolName.LastIndexOf('.') + 1)..].Replace('_', ' ');
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
