namespace VirtualCompany.Domain.Enums;

public enum ApprovalTargetEntityType
{
    Task = 1,
    Workflow = 2,
    Action = 3,
    FinanceIntegrationWrite = 4,
    SalesMeetingInvitation = 5,
    SalesMeetingChangeRequest = 6,
    OperatingPlan = 7,
    OperatingDecision = 8,
    ManualJournalDraft = 9,
    CustomerInvoiceAccounting = 10,
    SupplierBillAccounting = 11,
    AccountingProviderSwitchMappingDecision = 12,
    AccountingProviderSwitchCutoverPlan = 13,
    AccountingProviderSwitchActivation = 14,
    AccountingProviderSwitchClosure = 15,
    VatReturn = 16,
    CustomerInvoiceDraft = 17,
    CustomerInvoiceSchedule = 18,
    CustomerCollectionReminder = 19,
    TreasurySource = 20,
    PaymentBatch = 21,
    CurrencyRevaluationRun = 22,
    AccountingAllocation = 23,
    AccountingSchedule = 24,
    AccountingCloseTask = 25,
    AccountingCloseWaiver = 26
}

public static class ApprovalTargetEntityTypeValues
{
    private static readonly IReadOnlyDictionary<ApprovalTargetEntityType, string> Values = new Dictionary<ApprovalTargetEntityType, string>
    {
        [ApprovalTargetEntityType.Task] = "task",
        [ApprovalTargetEntityType.Workflow] = "workflow",
        [ApprovalTargetEntityType.Action] = "action",
        [ApprovalTargetEntityType.FinanceIntegrationWrite] = "finance_integration_write",
        [ApprovalTargetEntityType.SalesMeetingInvitation] = "sales_meeting_invitation",
        [ApprovalTargetEntityType.SalesMeetingChangeRequest] = "sales_meeting_change_request",
        [ApprovalTargetEntityType.OperatingPlan] = "operating_plan",
        [ApprovalTargetEntityType.OperatingDecision] = "operating_decision",
        [ApprovalTargetEntityType.ManualJournalDraft] = "manual_journal_draft",
        [ApprovalTargetEntityType.CustomerInvoiceAccounting] = "customer_invoice_accounting",
        [ApprovalTargetEntityType.SupplierBillAccounting] = "supplier_bill_accounting",
        [ApprovalTargetEntityType.AccountingProviderSwitchMappingDecision] = "accounting_provider_switch_mapping_decision",
        [ApprovalTargetEntityType.AccountingProviderSwitchCutoverPlan] = "accounting_provider_switch_cutover_plan",
        [ApprovalTargetEntityType.AccountingProviderSwitchActivation] = "accounting_provider_switch_activation",
        [ApprovalTargetEntityType.AccountingProviderSwitchClosure] = "accounting_provider_switch_closure",
        [ApprovalTargetEntityType.VatReturn] = "vat_return",
        [ApprovalTargetEntityType.CustomerInvoiceDraft] = "customer_invoice_draft",
        [ApprovalTargetEntityType.CustomerInvoiceSchedule] = "customer_invoice_schedule",
        [ApprovalTargetEntityType.CustomerCollectionReminder] = "customer_collection_reminder",
        [ApprovalTargetEntityType.TreasurySource] = "treasury_source",
        [ApprovalTargetEntityType.PaymentBatch] = "payment_batch",
        [ApprovalTargetEntityType.CurrencyRevaluationRun] = "currency_revaluation_run",
        [ApprovalTargetEntityType.AccountingAllocation] = "accounting_allocation",
        [ApprovalTargetEntityType.AccountingSchedule] = "accounting_schedule",
        [ApprovalTargetEntityType.AccountingCloseTask] = "accounting_close_task",
        [ApprovalTargetEntityType.AccountingCloseWaiver] = "accounting_close_waiver"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalTargetEntityType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this ApprovalTargetEntityType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported approval target entity type.");

    public static bool TryParse(string? value, out ApprovalTargetEntityType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "fortnox_write", StringComparison.OrdinalIgnoreCase))
        {
            type = ApprovalTargetEntityType.FinanceIntegrationWrite;
            return true;
        }

        if (ReverseValues.TryGetValue(trimmed, out type))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out type) && Values.ContainsKey(type);
    }

    public static ApprovalTargetEntityType Parse(string value) =>
        TryParse(value, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported approval target entity type. Allowed values: {string.Join(", ", AllowedValues)}.");
}

public enum ApprovalStepApproverType
{
    Role = 1,
    User = 2
}

public static class ApprovalStepApproverTypeValues
{
    private static readonly IReadOnlyDictionary<ApprovalStepApproverType, string> Values = new Dictionary<ApprovalStepApproverType, string>
    {
        [ApprovalStepApproverType.Role] = "role",
        [ApprovalStepApproverType.User] = "user"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalStepApproverType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this ApprovalStepApproverType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported approval step approver type.");

    public static ApprovalStepApproverType Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported approval step approver type. Allowed values: {string.Join(", ", AllowedValues)}.");
}

public enum ApprovalStepStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Skipped = 4
}

public static class ApprovalStepStatusValues
{
    private static readonly IReadOnlyDictionary<ApprovalStepStatus, string> Values = new Dictionary<ApprovalStepStatus, string>
    {
        [ApprovalStepStatus.Pending] = "pending",
        [ApprovalStepStatus.Approved] = "approved",
        [ApprovalStepStatus.Rejected] = "rejected",
        [ApprovalStepStatus.Skipped] = "skipped"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalStepStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this ApprovalStepStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported approval step status.");

    public static ApprovalStepStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported approval step status.");
}
