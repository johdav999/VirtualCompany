using System.Text.Json.Nodes;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class PaidSupplierBillExpensePostingEligibilityTests
{
    [Fact]
    public void Fully_paid_supplier_invoice_with_expense_account_is_allowed()
    {
        var decision = PaidSupplierBillExpensePostingEligibility.Evaluate(
            "supplier_invoice", "draft", "paid", "paid", true, "6540", []);

        Assert.True(decision.CanPost);
        Assert.True(decision.RequiresApproval);
        Assert.Empty(decision.ReasonCodes ?? []);
    }

    [Theory]
    [InlineData("customer_invoice", "draft", "paid", "paid", true, "6540", "document_not_supplier_invoice")]
    [InlineData("supplier_invoice", "booked", "paid", "booked", true, "6540", "already_booked")]
    [InlineData("supplier_invoice", "draft", "open", "open", false, "6540", "bill_not_fully_paid")]
    [InlineData("supplier_invoice", "draft", "paid", "paid", true, "2000", "expense_account_required")]
    public void Invalid_state_returns_stable_reason_code(
        string documentKind,
        string postingStatus,
        string settlementStatus,
        string fallbackStatus,
        bool isFullyPaid,
        string accountCode,
        string expectedReasonCode)
    {
        var decision = PaidSupplierBillExpensePostingEligibility.Evaluate(
            documentKind, postingStatus, settlementStatus, fallbackStatus, isFullyPaid, accountCode, []);

        Assert.False(decision.CanPost);
        Assert.Contains(expectedReasonCode, decision.ReasonCodes ?? []);
    }

    [Fact]
    public void Blocking_reconciliation_warning_is_explainable()
    {
        var warnings = new[]
        {
            new JsonObject { ["code"] = "duplicate_payment", ["message"] = "Review duplicate payment." }
        };

        var decision = PaidSupplierBillExpensePostingEligibility.Evaluate(
            "supplier_invoice", "draft", "paid", "paid", true, "6540", warnings);

        Assert.False(decision.CanPost);
        Assert.Contains("reconciliation_duplicate_payment", decision.ReasonCodes ?? []);
        Assert.Contains("Review duplicate payment.", decision.BlockingReasons ?? []);
    }
}
