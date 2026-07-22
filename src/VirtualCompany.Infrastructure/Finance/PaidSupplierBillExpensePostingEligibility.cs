using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

internal static class PaidSupplierBillExpensePostingEligibility
{
    private static readonly HashSet<string> BlockingWarningCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "duplicate_payment",
        "wrong_amount",
        "paid_without_approval"
    };

    public static PaidSupplierBillExpenseAvailabilityDto Evaluate(
        string? documentKind,
        string? postingStatus,
        string? settlementStatus,
        string? fallbackStatus,
        bool isFullyPaid,
        string? accountCode,
        IReadOnlyList<JsonObject>? reconciliationWarnings)
    {
        var blockingReasons = new List<string>();
        var reasonCodes = new List<string>();
        void Block(string code, string explanation)
        {
            reasonCodes.Add(code);
            blockingReasons.Add(explanation);
        }

        var kind = NormalizeStatusToken(documentKind);
        var posting = NormalizeStatusToken(postingStatus);
        var settlement = NormalizeStatusToken(settlementStatus);
        var fallback = NormalizeStatusToken(fallbackStatus);
        var normalizedAccountCode = FinanceAccountCodePolicy.NormalizeAccountCode(accountCode);

        if (kind != FinanceDocumentKinds.SupplierInvoice)
        {
            Block("document_not_supplier_invoice", "Only supplier invoices can be posted as expenses.");
        }

        if (posting == FinanceDocumentPostingStatuses.Booked || fallback == "booked")
        {
            Block("already_booked", "This supplier bill already appears booked in Fortnox.");
        }

        if (posting == FinanceDocumentPostingStatuses.Cancelled || fallback is "cancelled" or "canceled" or "void")
        {
            Block("bill_cancelled", "Cancelled supplier bills cannot be posted as expenses.");
        }

        if (settlement == FinanceSettlementStatuses.Credited)
        {
            Block("bill_credited", "Credited supplier bills cannot be posted as expenses.");
        }

        if (settlement != FinanceSettlementStatuses.Paid || !isFullyPaid)
        {
            Block("bill_not_fully_paid", "The supplier bill must be fully paid before Laura can post the expense.");
        }

        if (!FinanceAccountCodePolicy.IsSupplierExpenseAccount(normalizedAccountCode))
        {
            Block("expense_account_required", "Choose a supplier expense account before Laura posts this bill.");
        }

        foreach (var warning in reconciliationWarnings ?? [])
        {
            var code = ReadString(warning, "code");
            if (!string.IsNullOrWhiteSpace(code) && BlockingWarningCodes.Contains(code))
            {
                Block(
                    $"reconciliation_{code.Trim().ToLowerInvariant()}",
                    ReadString(warning, "message") ?? "Resolve the payment review warning before posting this expense.");
            }
        }

        if (blockingReasons.Count == 0)
        {
            return new PaidSupplierBillExpenseAvailabilityDto(
                true,
                "Ready to post",
                "success",
                "Laura can post this paid supplier bill as an expense.",
                normalizedAccountCode,
                [],
                [],
                RequiresApproval: true);
        }

        return new PaidSupplierBillExpenseAvailabilityDto(
            false,
            ResolveBlockedLabel(blockingReasons),
            ResolveBlockedTone(blockingReasons),
            blockingReasons[0],
            normalizedAccountCode,
            blockingReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            reasonCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RequiresApproval: true);
    }

    public static IReadOnlyList<JsonObject> ExtractWarnings(SupplierInvoiceEnrichmentActionDto? action)
    {
        if (action?.ReconciliationWarnings is null)
        {
            return [];
        }

        return action.ReconciliationWarnings
            .OfType<JsonObject>()
            .ToArray();
    }

    public static string? ResolveExpenseAccountCode(SupplierInvoiceEnrichmentActionDto? enrichmentAction, string? supplierDefaultAccount)
    {
        var suggestedAccount = ReadString(enrichmentAction?.SuggestionPayload?["coding"] as JsonObject, "ledgerAccount");
        if (FinanceAccountCodePolicy.IsSupplierExpenseAccount(suggestedAccount))
        {
            return FinanceAccountCodePolicy.NormalizeAccountCode(suggestedAccount);
        }

        if (FinanceAccountCodePolicy.IsSupplierExpenseAccount(supplierDefaultAccount))
        {
            return FinanceAccountCodePolicy.NormalizeAccountCode(supplierDefaultAccount);
        }

        return null;
    }

    private static string ResolveBlockedLabel(IReadOnlyList<string> reasons)
    {
        if (reasons.Any(reason => reason.Contains("already appears booked", StringComparison.OrdinalIgnoreCase)))
        {
            return "Already booked";
        }

        if (reasons.Any(reason => reason.Contains("payment", StringComparison.OrdinalIgnoreCase) || reason.Contains("paid", StringComparison.OrdinalIgnoreCase)))
        {
            return "Needs payment review";
        }

        if (reasons.Any(reason => reason.Contains("expense account", StringComparison.OrdinalIgnoreCase)))
        {
            return "Needs account review";
        }

        return "Not ready";
    }

    private static string ResolveBlockedTone(IReadOnlyList<string> reasons) =>
        reasons.Any(reason => reason.Contains("already appears booked", StringComparison.OrdinalIgnoreCase))
            ? "success"
            : "warning";

    private static string? ReadString(JsonObject? node, string propertyName) =>
        node is not null &&
        node.TryGetPropertyValue(propertyName, out var value) &&
        value is not null
            ? value.ToString()
            : null;

    private static string NormalizeStatusToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
}
