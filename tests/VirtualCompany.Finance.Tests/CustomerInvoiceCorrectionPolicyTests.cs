using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class CustomerInvoiceCorrectionPolicyTests
{
    [Fact]
    public async Task Partial_payment_caps_refund_and_write_off_without_cross_company_disclosure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid(); var customerId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc);
        var invoice = new FinanceInvoice(Guid.NewGuid(), companyId, customerId, "INV-100",
            now, now.AddDays(30), 100m, "SEK", "approved", authority: "native");
        var payment = new Payment(Guid.NewGuid(), companyId, PaymentTypes.Incoming, 40m, "SEK", now,
            PaymentMethods.BankTransfer, PaymentStatuses.Completed, "BANK-40");
        db.Companies.AddRange(new Company(companyId, "Correction Company"), new Company(otherCompanyId, "Other Company"));
        db.FinanceCounterparties.Add(new FinanceCounterparty(customerId, companyId, "Customer", "customer"));
        db.FinanceInvoices.Add(invoice); db.Payments.Add(payment);
        db.PaymentAllocations.Add(new PaymentAllocation(Guid.NewGuid(), companyId, payment.Id,
            invoice.Id, null, 40m, "SEK"));
        await db.SaveChangesAsync();
        var policy = new CustomerInvoiceCorrectionPolicy(db);

        var refund = await policy.EvaluateAsync(new(companyId, invoice.Id,
            CustomerInvoiceCorrectionTypes.Refund, 41m, "SEK"), default);
        var writeOff = await policy.EvaluateAsync(new(companyId, invoice.Id,
            CustomerInvoiceCorrectionTypes.BadDebt, 61m, "SEK"), default);
        var hidden = await policy.EvaluateAsync(new(otherCompanyId, invoice.Id,
            CustomerInvoiceCorrectionTypes.Refund, 1m, "SEK"), default);

        Assert.False(refund.IsAllowed);
        Assert.Equal(CustomerInvoiceCorrectionReasonCodes.RefundExceedsPaid, refund.ReasonCode);
        Assert.Equal(40m, refund.MaximumAllowedAmount);
        Assert.Equal(CustomerInvoiceCorrectionReasonCodes.WriteOffExceedsOutstanding, writeOff.ReasonCode);
        Assert.Equal(60m, writeOff.MaximumAllowedAmount);
        Assert.Equal(CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound, hidden.ReasonCode);
        Assert.DoesNotContain(hidden.Evidence, x => x.Value.Contains("INV-100", StringComparison.Ordinal));
    }

    [Fact]
    public void Refund_execution_stops_ambiguous_outcomes_and_adjustments_preserve_original_allocation()
    {
        var now = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc);
        var companyId = Guid.NewGuid(); var correctionId = Guid.NewGuid(); var allocationId = Guid.NewGuid();
        var execution = new CustomerInvoiceRefundExecution(Guid.NewGuid(), companyId, correctionId,
            "bank", "refund-key", "beneficiary", "payment-proof", false, now);

        Assert.True(execution.TryClaim("claim", now, TimeSpan.FromMinutes(1)));
        execution.MarkReconciliationRequired("ambiguous_provider_outcome",
            "Check the provider before retrying.", null, now.AddMinutes(1));
        var adjustment = new CustomerInvoiceCorrectionAllocationAdjustment(Guid.NewGuid(), companyId,
            correctionId, allocationId, 25m, "SEK", now);

        Assert.Equal(CustomerInvoiceRefundExecutionStatuses.ReconciliationRequired, execution.Status);
        Assert.Equal(1, execution.AttemptCount);
        Assert.Equal(25m, adjustment.ReleasedAmount);
        Assert.Equal(allocationId, adjustment.PaymentAllocationId);
    }

    [Fact]
    public void Refund_worker_requires_an_approved_exact_source_and_payload_before_provider_execution()
    {
        const string sourceVersion = "invoice-v4";
        const string sourceHash = "source-hash";
        const string payloadHash = "payload-hash";
        var actorId = Guid.NewGuid();
        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), Guid.NewGuid(),
            ApprovalTargetEntityType.Task, Guid.NewGuid(), "user", actorId, "customer_invoice_refund",
            new Dictionary<string, JsonNode?>
            {
                ["sourceVersion"] = sourceVersion,
                ["sourceHash"] = sourceHash,
                ["payloadHash"] = payloadHash
            }, "finance_approver", null, []);

        Assert.False(CustomerInvoiceRefundExecutionRunner.HasCurrentApproval(
            approval, sourceVersion, sourceHash, payloadHash));
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, actorId, "Approved from current evidence.");

        Assert.True(CustomerInvoiceRefundExecutionRunner.HasCurrentApproval(
            approval, sourceVersion, sourceHash, payloadHash));
        Assert.False(CustomerInvoiceRefundExecutionRunner.HasCurrentApproval(
            approval, "invoice-v5", sourceHash, payloadHash));
        Assert.False(CustomerInvoiceRefundExecutionRunner.HasCurrentApproval(
            approval, sourceVersion, "changed-source", payloadHash));
        Assert.False(CustomerInvoiceRefundExecutionRunner.HasCurrentApproval(
            approval, sourceVersion, sourceHash, "changed-payload"));
    }
}
