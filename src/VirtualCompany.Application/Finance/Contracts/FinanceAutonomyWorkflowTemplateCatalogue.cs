using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Finance;

public static class FinanceAutonomyWorkflowTemplateCatalogue
{
    private static readonly string[] UnsupportedSensitiveEffects =
    [
        "accounting_posting", "payment_or_money_movement", "final_close_or_year_end",
        "statutory_filing_or_signoff", "provider_or_credential_change", "external_communication",
        "self_approval", "ambiguous_provider_outcome_resolution"
    ];

    public static IReadOnlyList<FinanceAutonomyWorkflowTemplate> All { get; } =
    [
        Template(
            FinanceAutonomyWorkflowTemplateCodes.StaleCashEvidence,
            "Stale cash and bank evidence monitoring", "Bevakning av inaktuella kassa- och bankunderlag",
            "Checks that cash evidence remains current and routes stale or missing evidence for review.",
            "Kontrollerar att kassaunderlag är aktuella och skickar inaktuella eller saknade underlag till granskning.",
            FinanceAgentCoverageCapabilityIds.DailyCash,
            [FinanceAutonomyTriggers.Schedule, FinanceAutonomyTriggers.BusinessEvent],
            [FinanceAutonomyEventTypes.StaleCashEvidence], "15 8 * * 1-5",
            ["authoritative_cash_snapshot", "cash_account", "evidence_observed_utc"],
            "read", "get_cash_balance", new Dictionary<string, JsonNode?>(),
            Limits(100, 1, 1, 240, 30, 180, 1440),
            ["healthy_no_action_audit", "source_linked_review_task"],
            ["cash evidence is current", "review task already exists", "grant or evidence is stale"],
            ["cash evidence review task"], "finance_manager", "No approval is required for reading or task creation.",
            "Review stale cash or bank evidence", "Granska inaktuella kassa- eller bankunderlag",
            "Confirm the latest bank and cash evidence before relying on the balance.",
            "Bekräfta de senaste bank- och kassaunderlagen innan saldot används."),

        Template(
            FinanceAutonomyWorkflowTemplateCodes.UncategorizedTransactions,
            "Uncategorized transaction review preparation", "Förberedelse för granskning av okategoriserade transaktioner",
            "Finds bounded uncategorized transactions and prepares one review queue item.",
            "Hittar ett begränsat antal okategoriserade transaktioner och förbereder en granskningsuppgift.",
            FinanceAgentCoverageCapabilityIds.TransactionReview,
            [FinanceAutonomyTriggers.Schedule, FinanceAutonomyTriggers.BusinessEvent],
            [FinanceAutonomyEventTypes.NewUncategorizedTransaction], "30 8 * * 1-5",
            ["finance_transaction", "transaction_version", "category_state"],
            "read", "list_uncategorized_transactions", Payload(("limit", 100)),
            Limits(100, 1, 2, 60, 10, 1440, 1440),
            ["healthy_no_action_audit", "transaction_review_task", "review_recommendation_draft"],
            ["no uncategorized transactions", "maximum record limit reached", "source version changed"],
            ["transaction review task", "non-posting categorization recommendation"], "finance_operator",
            "Recommendations remain reviewable; categorization is not executed by this template.",
            "Review uncategorized transactions", "Granska okategoriserade transaktioner",
            "Review the bounded transaction set and decide each category.",
            "Granska den begränsade transaktionsmängden och besluta kategori för varje post."),

        Template(
            FinanceAutonomyWorkflowTemplateCodes.OverdueReceivables,
            "Overdue receivables plan refresh", "Uppdatering av plan för förfallna kundfordringar",
            "Refreshes overdue receivable evidence and prepares internal collections follow-up.",
            "Uppdaterar underlag för förfallna kundfordringar och förbereder intern uppföljning.",
            FinanceAgentCoverageCapabilityIds.NaturalLanguageQueries,
            [FinanceAutonomyTriggers.Schedule, FinanceAutonomyTriggers.BusinessEvent],
            [FinanceAutonomyEventTypes.OverdueReceivable], "0 9 * * 1-5",
            ["finance_invoice", "receivable_balance", "due_date", "customer"],
            "read", "resolve_finance_agent_query",
            Payload(("queryText", "which customers are overdue")),
            Limits(100, 1, 1, 240, 30, 1440, 1440),
            ["healthy_no_action_audit", "collections_review_task", "internal_follow_up_plan"],
            ["no overdue balance", "invoice version changed", "customer communication would be required"],
            ["collections review task", "internal collections plan draft"], "accounts_receivable_owner",
            "Any customer communication requires a separate human-approved workflow.",
            "Refresh overdue receivables plan", "Uppdatera plan för förfallna kundfordringar",
            "Confirm priorities and choose the next internal collections action.",
            "Bekräfta prioriteringar och välj nästa interna inkassoåtgärd."),

        Template(
            FinanceAutonomyWorkflowTemplateCodes.DuePayables,
            "Due payables and cash-reserve review", "Granskning av förfallande leverantörsskulder och kassareserv",
            "Reviews near-term payables against cash constraints without initiating payment.",
            "Granskar kommande leverantörsskulder mot kassabegränsningar utan att initiera betalning.",
            FinanceAgentCoverageCapabilityIds.RoleAnalysis,
            [FinanceAutonomyTriggers.Schedule], [], "0 10 * * 1-5",
            ["supplier_bill", "payment_state", "cash_balance", "cash_reserve_policy"],
            "recommend", FinanceAgentAnalysisToolIds.Analyze,
            Payload(("analysisType", FinanceAgentAnalysisTypes.Payables), ("horizonDays", 14),
                ("cadence", "daily"), ("objective", "Review due payables against the protected cash reserve; do not initiate payment.")),
            Limits(100, 1, 1, 240, 30, 1440, 1440),
            ["healthy_no_action_audit", "payables_review_task", "payment_priority_draft"],
            ["no due payables", "cash evidence is stale", "payment initiation would be required"],
            ["payables review task", "non-executing payment priority draft"], "accounts_payable_owner",
            "Payment initiation, release, or bank submission always remains human-controlled.",
            "Review due payables and cash reserve", "Granska förfallande leverantörsskulder och kassareserv",
            "Confirm the payment priority plan and protected cash reserve.",
            "Bekräfta betalningsprioriteringen och den skyddade kassareserven."),

        Template(
            FinanceAutonomyWorkflowTemplateCodes.CloseBlockers,
            "Close blocker refresh", "Uppdatering av stängningshinder",
            "Refreshes close readiness and creates one bounded blocker review item.",
            "Uppdaterar stängningsberedskap och skapar en begränsad granskningsuppgift för hinder.",
            FinanceAgentCoverageCapabilityIds.CloseAndYearEnd,
            [FinanceAutonomyTriggers.Schedule, FinanceAutonomyTriggers.BusinessEvent],
            [FinanceAutonomyEventTypes.CloseTaskBlockerChanged], "0 11 * * 1-5",
            ["close_instance", "close_task", "readiness_snapshot", "fiscal_period"],
            "read", FinanceCloseComplianceAgentToolIds.ReadReadiness,
            new Dictionary<string, JsonNode?>(), Limits(100, 1, 2, 60, 10, 720, 1440),
            ["healthy_no_action_audit", "close_blocker_review_task", "assignment_recommendation"],
            ["close is ready", "evidence is stale or missing", "final close authority would be required"],
            ["close blocker review task", "non-executing assignment recommendation"], "accounting_close_owner",
            "Final close, lock, reopen, and year-end remain human-only.",
            "Review current close blockers", "Granska aktuella stängningshinder",
            "Resolve or assign the current blocker using the linked close evidence.",
            "Lös eller tilldela det aktuella hindret med hjälp av länkat stängningsunderlag."),

        Template(
            FinanceAutonomyWorkflowTemplateCodes.ReconciliationExceptions,
            "Reconciliation and import exception review", "Granskning av avstämnings- och importundantag",
            "Collects reconciliation or import failures for bounded internal review.",
            "Samlar avstämnings- eller importfel för begränsad intern granskning.",
            FinanceAgentCoverageCapabilityIds.BankAndReconciliation,
            [FinanceAutonomyTriggers.Schedule, FinanceAutonomyTriggers.BusinessEvent],
            [FinanceAutonomyEventTypes.ReconciliationFailed, FinanceAutonomyEventTypes.ImportFailed], "30 11 * * 1-5",
            ["statement_import", "reconciliation_group", "source_record_version", "failure_evidence"],
            "read", FinanceAdvancedAccountingAgentToolIds.ReadReconciliation,
            Payload(("status", "failed"), ("take", 100)), Limits(100, 1, 3, 30, 10, 720, 1440),
            ["healthy_no_action_audit", "reconciliation_exception_task", "review_recommendation"],
            ["no exceptions", "source changed", "provider outcome is ambiguous"],
            ["reconciliation exception task", "non-applying review recommendation"], "finance_reconciliation_owner",
            "Ambiguous provider outcomes and reconciliation application require human resolution.",
            "Review reconciliation or import exception", "Granska avstämnings- eller importundantag",
            "Inspect the linked source version and choose a safe reconciliation action.",
            "Kontrollera den länkade källversionen och välj en säker avstämningsåtgärd."),

        Template(
            FinanceAutonomyWorkflowTemplateCodes.ExpiringComplianceEvidence,
            "Expiring compliance evidence reminder", "Påminnelse om utgående efterlevnadsunderlag",
            "Monitors compliance due dates and prepares evidence follow-up without filing.",
            "Bevakar förfallodatum för efterlevnad och förbereder underlagsuppföljning utan inlämning.",
            FinanceAgentCoverageCapabilityIds.ComplianceAndAudit,
            [FinanceAutonomyTriggers.Schedule, FinanceAutonomyTriggers.BusinessEvent],
            [FinanceAutonomyEventTypes.ComplianceObligationExpiring], "0 12 * * 1-5",
            ["compliance_obligation", "submission_evidence", "provider_acknowledgement", "policy_pack"],
            "read", FinanceCloseComplianceAgentToolIds.ReadComplianceObligations,
            Payload(("take", 100)), Limits(100, 1, 1, 240, 30, 1440, 1440),
            ["healthy_no_action_audit", "compliance_evidence_task", "evidence_checklist_draft"],
            ["no expiring obligation", "evidence is stale or missing", "filing or sign-off would be required"],
            ["compliance evidence task", "non-filing evidence checklist"], "compliance_owner",
            "Filing, declaration, professional approval, and sign-off remain human-only.",
            "Review expiring compliance evidence", "Granska efterlevnadsunderlag som snart går ut",
            "Confirm the evidence checklist and assign any missing item.",
            "Bekräfta underlagslistan och tilldela eventuella saknade poster."),

        Template(
            FinanceAutonomyWorkflowTemplateCodes.FailedBackgroundWork,
            "Failed background Finance work escalation", "Eskalering av misslyckat Finance-bakgrundsarbete",
            "Turns a failed durable Finance job into one source-linked operator escalation.",
            "Gör ett misslyckat varaktigt Finance-jobb till en källänkad operatörseskalering.",
            FinanceAgentCoverageCapabilityIds.AdvancedAccounting,
            [FinanceAutonomyTriggers.BusinessEvent], [FinanceAutonomyEventTypes.BackgroundWorkCompleted], null,
            ["background_execution", "failure_code", "attempt_state", "source_record_version"],
            "read", FinanceAdvancedAccountingAgentToolIds.ReadSchedules,
            Payload(("status", "failed"), ("take", 25)), Limits(25, 1, 3, 15, 5, 60, 1440),
            ["background_failure_task", "operator_escalation"],
            ["work completed successfully", "duplicate failure version", "unsafe retry or ambiguous outcome"],
            ["failed work escalation task"], "finance_operations_owner",
            "The template does not retry, reconcile, or declare an ambiguous provider result.",
            "Escalate failed Finance background work", "Eskalera misslyckat Finance-bakgrundsarbete",
            "Inspect the failure evidence and decide whether a bounded retry or reconciliation is safe.",
            "Granska felunderlaget och avgör om ett begränsat återförsök eller en avstämning är säker.")
    ];

    public static FinanceAutonomyWorkflowTemplate? Find(string? code) =>
        All.SingleOrDefault(x => string.Equals(x.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static FinanceAutonomyWorkflowTemplate? Resolve(
        string capabilityId, string trigger, string? eventType)
    {
        var candidates = All.Where(x =>
            string.Equals(x.CapabilityId, capabilityId, StringComparison.OrdinalIgnoreCase) &&
            x.Triggers.Contains(trigger, StringComparer.Ordinal)).ToArray();
        if (trigger == FinanceAutonomyTriggers.BusinessEvent)
            return candidates.SingleOrDefault(x => x.EventTypes.Contains(eventType ?? string.Empty, StringComparer.Ordinal));
        return candidates.SingleOrDefault();
    }

    private static FinanceAutonomyWorkflowTemplate Template(
        string code, string nameEn, string nameSv, string descriptionEn, string descriptionSv,
        string capabilityId, IReadOnlyList<string> triggers, IReadOnlyList<string> eventTypes,
        string? schedule, IReadOnlyList<string> evidence, string actionClass, string toolName,
        IReadOnlyDictionary<string, JsonNode?> requestPayload, FinanceAutonomyWorkflowLimits limits,
        IReadOnlyList<string> outputs, IReadOnlyList<string> stopConditions,
        IReadOnlyList<string> tasks, string ownerRole, string approvalBehavior,
        string taskTitleEn, string taskTitleSv, string nextActionEn, string nextActionSv) =>
        new(code, FinanceAutonomyWorkflowTemplateVersions.V1,
            new(nameEn, nameSv), new(descriptionEn, descriptionSv), capabilityId, triggers, eventTypes,
            schedule, evidence, actionClass, toolName, requestPayload, limits, outputs, stopConditions,
            tasks, ownerRole, approvalBehavior, UnsupportedSensitiveEffects,
            new(taskTitleEn, taskTitleSv), new(nextActionEn, nextActionSv));

    private static FinanceAutonomyWorkflowLimits Limits(
        int records, int actions, int runs, int interval, int debounce, int freshness, int reviewWindow) =>
        new(records, actions, runs, interval, debounce, freshness, reviewWindow);

    private static IReadOnlyDictionary<string, JsonNode?> Payload(params (string Key, object Value)[] values) =>
        values.ToDictionary(x => x.Key, x => (JsonNode?)JsonValue.Create(x.Value), StringComparer.OrdinalIgnoreCase);
}
