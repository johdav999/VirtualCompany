using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class AdvancedReconciliationGraphTests
{
    [Fact]
    public void One_bank_row_can_settle_multiple_documents_with_exact_control_totals()
    {
        var bank = Node("bank_transaction", 100m, "incoming");
        var payment = Node("payment", 100m);
        var invoiceOne = Node("invoice", 60m);
        var invoiceTwo = Node("invoice", 40m);

        var result = AdvancedReconciliationGraphPolicy.Evaluate(
            [bank, payment, invoiceOne, invoiceTwo],
            [Edge(bank, payment, "bank_payment", 100m), Edge(payment, invoiceOne, "payment_document", 60m), Edge(payment, invoiceTwo, "payment_document", 40m)]);

        Assert.True(result.IsBalanced);
        Assert.Equal("one_to_many", result.Cardinality);
        Assert.Equal(100m, result.ExpectedBankTotal);
        Assert.Equal(100m, result.AllocatedAmount);
    }

    [Fact]
    public void Multiple_bank_rows_can_settle_one_document_through_one_payment()
    {
        var firstBank = Node("bank_transaction", 40m, "incoming");
        var secondBank = Node("bank_transaction", 60m, "incoming");
        var payment = Node("payment", 100m);
        var invoice = Node("invoice", 100m);

        var result = AdvancedReconciliationGraphPolicy.Evaluate(
            [firstBank, secondBank, payment, invoice],
            [Edge(firstBank, payment, "bank_payment", 40m), Edge(secondBank, payment, "bank_payment", 60m), Edge(payment, invoice, "payment_document", 100m)]);

        Assert.True(result.IsBalanced);
        Assert.Equal("many_to_one", result.Cardinality);
        Assert.Equal(100m, result.ExpectedBankTotal);
    }

    [Fact]
    public void Incoming_partial_payment_with_withheld_fee_balances_gross_payment_to_net_bank_amount()
    {
        var bank = Node("bank_transaction", 95m, "incoming");
        var payment = Node("payment", 100m);
        var fee = Adjustment("adjustment", "bank_fee", debit: 5m);

        var result = AdvancedReconciliationGraphPolicy.Evaluate(
            [bank, payment, fee],
            [Edge(bank, payment, "bank_payment", 100m), Edge(bank, fee, "bank_adjustment", 0m)]);

        Assert.True(result.IsBalanced);
        Assert.Equal(5m, result.FeeAmount);
        Assert.Equal(0m, result.ResidualAmount);
    }

    [Fact]
    public void Outgoing_payment_fee_and_rounding_lines_balance_explicitly()
    {
        var bank = Node("bank_transaction", 105.01m, "outgoing");
        var payment = Node("payment", 100m);
        var fee = Adjustment("adjustment", "bank_fee", debit: 5m);
        var rounding = Adjustment("adjustment", "rounding_difference", debit: .01m);

        var result = AdvancedReconciliationGraphPolicy.Evaluate(
            [bank, payment, fee, rounding],
            [Edge(bank, payment, "bank_payment", 100m), Edge(bank, fee, "bank_adjustment", 0m), Edge(bank, rounding, "bank_adjustment", 0m)]);

        Assert.True(result.IsBalanced);
        Assert.Equal(5m, result.FeeAmount);
        Assert.Equal(.01m, result.RoundingAmount);
    }

    [Fact]
    public void Explicit_suspense_residual_closes_the_control_total_and_remains_visible()
    {
        var bank = Node("bank_transaction", 100m, "incoming");
        var payment = Node("payment", 80m);
        var residual = Adjustment("residual", "suspense", credit: 20m);

        var result = AdvancedReconciliationGraphPolicy.Evaluate(
            [bank, payment, residual],
            [Edge(bank, payment, "bank_payment", 80m), Edge(bank, residual, "bank_adjustment", 0m)]);

        Assert.True(result.IsBalanced);
        Assert.Equal(20m, result.ResidualAmount);
    }

    [Fact]
    public void Hidden_residual_is_rejected_as_unbalanced()
    {
        var bank = Node("bank_transaction", 100m, "incoming");
        var payment = Node("payment", 80m);

        var result = AdvancedReconciliationGraphPolicy.Evaluate(
            [bank, payment],
            [Edge(bank, payment, "bank_payment", 80m)]);

        Assert.False(result.IsBalanced);
        Assert.Contains(result.Errors, x => x.Contains("does not balance", StringComparison.Ordinal));
    }

    [Fact]
    public void Stale_version_and_replayed_decision_are_rejected_without_mutating_history()
    {
        var company = Guid.NewGuid(); var user = Guid.NewGuid(); var rule = Guid.NewGuid(); var now = DateTime.UtcNow;
        var group = new AdvancedReconciliationGroup(Guid.NewGuid(), company, rule, 7, null, "DEP-1", "Acme", "SEK", 100m, .92m, true, user, now);

        Assert.Throws<InvalidOperationException>(() => group.Accept(2, user, "stale", now.AddMinutes(1)));
        Assert.Equal(AdvancedReconciliationGroupStatuses.Proposed, group.Status);
        Assert.Equal(1, group.Version);

        group.Reject(1, user, "Reference belongs to another settlement.", now.AddMinutes(2));
        Assert.Equal(AdvancedReconciliationGroupStatuses.Rejected, group.Status);
        Assert.Throws<InvalidOperationException>(() => group.Accept(2, user, "replay", now.AddMinutes(3)));
        Assert.Equal(AdvancedReconciliationGroupStatuses.Rejected, group.Status);
    }

    [Fact]
    public void Accepted_result_can_only_receive_one_linked_reversal_transition()
    {
        var company = Guid.NewGuid(); var user = Guid.NewGuid(); var now = DateTime.UtcNow;
        var group = new AdvancedReconciliationGroup(Guid.NewGuid(), company, Guid.NewGuid(), 3, null,
            "BATCH-1", "Acme", "SEK", 250m, .95m, true, user, now);
        group.Accept(1, user, "Approved balanced batch.", now.AddMinutes(1));
        group.Reverse(2, user, "Correction required.", now.AddMinutes(2));

        Assert.Equal(AdvancedReconciliationGroupStatuses.Reversed, group.Status);
        Assert.Equal(3, group.Version);
        Assert.Throws<InvalidOperationException>(() => group.Reverse(3, user, "replay", now.AddMinutes(3)));
    }

    private static AdvancedReconciliationGraphNode Node(string type, decimal amount, string? direction = null) =>
        new(Guid.NewGuid(), type, amount, "SEK", direction);

    private static AdvancedReconciliationGraphNode Adjustment(string type, string kind, decimal debit = 0m, decimal credit = 0m) =>
        new(Guid.NewGuid(), type, Math.Max(debit, credit), "SEK", AdjustmentKind: kind, DebitAmount: debit, CreditAmount: credit);

    private static AdvancedReconciliationGraphEdge Edge(AdvancedReconciliationGraphNode source,
        AdvancedReconciliationGraphNode target, string type, decimal amount) => new(Guid.NewGuid(), source.Id, target.Id, type, amount);
}

