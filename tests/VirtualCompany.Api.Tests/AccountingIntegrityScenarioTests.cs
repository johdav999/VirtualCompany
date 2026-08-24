using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

/// <summary>
/// Production-shaped accounting proof from setup through recovery. The builder below owns only
/// external/source inputs; every accounting decision is made by the registered production services.
/// </summary>
public sealed class AccountingIntegrityScenarioTests
{
    private static readonly DateTimeOffset ScenarioNow = new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Operational_accounting_truth_remains_balanced_idempotent_closed_exported_and_recoverable()
    {
        var failure = new FailNextSaveChangesInterceptor();
        using var factory = new TestWebApplicationFactory(new FixedTimeProvider(ScenarioNow), null, false, [failure]);
        await RunScenarioAsync(factory, failure);
    }

    [ApiSqlServerFact]
    [Trait("Category", "SqlServer")]
    public async Task Sql_server_operational_accounting_truth_remains_balanced_idempotent_closed_exported_and_recoverable()
    {
        var failure = new FailNextSaveChangesInterceptor();
        using var factory = TestWebApplicationFactory.CreateSqlServer(new FixedTimeProvider(ScenarioNow), [failure]);
        await RunScenarioAsync(factory, failure);
    }

    private static async Task RunScenarioAsync(TestWebApplicationFactory factory, FailNextSaveChangesInterceptor failure)
    {
        var input = await AccountingIntegrityInputBuilder.CreateAsync(factory);
        using var owner = CreateClient(factory, input.OwnerSubject, input.OwnerEmail);
        using var approver = CreateClient(factory, input.ApproverSubject, input.ApproverEmail);
        using var outsider = CreateClient(factory, input.OutsiderSubject, input.OutsiderEmail);

        await CompleteSetupAndReplayAsync(owner, input);
        var accounts = await AccountingIntegrityInputBuilder.AddOperationalInputsAsync(factory, input);
        SeedEvidenceObjects(factory, input);

        var invoice = await SubmitApprovePostAsync(owner, approver, input, true, accounts.ExpenseId);
        var bill = await SubmitApprovePostAsync(owner, approver, input, false, accounts.ExpenseId);

        using (var stale = await owner.PostAsJsonAsync(InvoiceRoute(input, "accounting/post"),
                   new { expectedVersion = invoice.SourceVersion + 1, idempotencyKey = "scenario:invoice:stale" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        using (var crossCompany = await outsider.PostAsJsonAsync(InvoiceRoute(input, "accounting/post"),
                   new { expectedVersion = invoice.SourceVersion, idempotencyKey = "scenario:cross-company" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossCompany.StatusCode);
        }

        failure.Arm();
        var failedAllocation = await owner.PostAsJsonAsync(AllocationRoute(input, input.IncomingPaymentAId),
            Allocation(input.InvoiceId, null, 40m, "scenario:invoice:allocation:1"));
        Assert.Equal(HttpStatusCode.InternalServerError, failedAllocation.StatusCode);
        failedAllocation.Dispose();
        var afterRollback = await CountsAsync(factory, input.CompanyId);
        Assert.Equal(2, afterRollback.JournalCount);
        Assert.Equal(0, afterRollback.AllocationCount);

        await AllocateAndReplayAsync(owner, input, input.IncomingPaymentAId, input.InvoiceId, null, 40m, "scenario:invoice:allocation:1");
        await AllocateAndReplayAsync(owner, input, input.IncomingPaymentBId, input.InvoiceId, null, 60m, "scenario:invoice:allocation:2");
        await AllocateAndReplayAsync(owner, input, input.OutgoingPaymentAId, null, input.BillId, 25m, "scenario:bill:allocation:1");
        await AllocateAndReplayAsync(owner, input, input.OutgoingPaymentBId, null, input.BillId, 75m, "scenario:bill:allocation:2");

        var bankTransactions = await ImportStatementAndReplayAsync(owner, factory, input, accounts.BankAccountId);
        await ReconcilePaymentAndReplayAsync(owner, input, bankTransactions.IncomingAId,
            [(input.IncomingPaymentAId, 40m)], "scenario:bank:incoming:a");
        await ReconcilePaymentAndReplayAsync(owner, input, bankTransactions.IncomingBId,
            [(input.IncomingPaymentBId, 60m)], "scenario:bank:incoming:b");
        await ReconcilePaymentAndReplayAsync(owner, input, bankTransactions.OutgoingAId,
            [(input.OutgoingPaymentAId, 25m)], "scenario:bank:outgoing:a");
        await ReconcilePaymentAndReplayAsync(owner, input, bankTransactions.OutgoingBId,
            [(input.OutgoingPaymentBId, 75m)], "scenario:bank:outgoing:b");
        await SuspenseAndCorrectAsync(owner, input, bankTransactions.SuspenseId, accounts.RevenueId);

        var trial = await GetJsonAsync(owner,
            $"/internal/companies/{input.CompanyId:D}/finance/accounting/reports/trial-balance?fiscalPeriodId={input.PeriodId:D}");
        Assert.True(trial.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(trial.GetProperty("totalDebits").GetDecimal(), trial.GetProperty("totalCredits").GetDecimal());
        Assert.Equal(475m, trial.GetProperty("totalDebits").GetDecimal());
        var trialChecksum = trial.GetProperty("checksum").GetString();
        Assert.Equal(64, trialChecksum!.Length);

        var ledger = await GetJsonAsync(owner,
            $"/internal/companies/{input.CompanyId:D}/finance/accounting/reports/general-ledger?fiscalPeriodId={input.PeriodId:D}");
        Assert.NotEmpty(ledger.GetProperty("accounts").EnumerateArray());
        Assert.Contains(ledger.GetProperty("accounts").EnumerateArray().SelectMany(x => x.GetProperty("lines").EnumerateArray()),
            x => x.GetProperty("evidence").GetArrayLength() > 0);

        var taxBefore = await GetJsonAsync(owner,
            $"/internal/companies/{input.CompanyId:D}/finance/accounting/reports/tax-summary?fiscalPeriodId={input.PeriodId:D}");
        using (var review = await owner.PostAsJsonAsync(
                   $"/internal/companies/{input.CompanyId:D}/finance/accounting/reports/tax-summary/review",
                   new { fiscalPeriodId = input.PeriodId }))
        {
            Assert.True(review.IsSuccessStatusCode, await review.Content.ReadAsStringAsync());
        }
        using (var replayReview = await owner.PostAsJsonAsync(
                   $"/internal/companies/{input.CompanyId:D}/finance/accounting/reports/tax-summary/review",
                   new { fiscalPeriodId = input.PeriodId }))
        {
            Assert.True(replayReview.IsSuccessStatusCode, await replayReview.Content.ReadAsStringAsync());
        }
        Assert.False(taxBefore.GetProperty("isStatutoryComplianceValidated").GetBoolean());
        Assert.Contains("not a statutory return", taxBefore.GetProperty("complianceNotice").GetString(), StringComparison.OrdinalIgnoreCase);

        var control = await GetJsonAsync(owner,
            $"/internal/companies/{input.CompanyId:D}/finance/accounting/reports/control-reconciliation?fiscalPeriodId={input.PeriodId:D}");
        Assert.True(control.GetProperty("isReconciled").GetBoolean());
        Assert.All(control.GetProperty("accounts").EnumerateArray(), row => Assert.Equal(0m, row.GetProperty("difference").GetDecimal()));

        var profitLoss = await GetJsonAsync(owner,
            $"/internal/companies/{input.CompanyId:D}/finance/reports/profit-loss?fiscalPeriodId={input.PeriodId:D}");
        var balanceSheet = await GetJsonAsync(owner,
            $"/internal/companies/{input.CompanyId:D}/finance/reports/balance-sheet?fiscalPeriodId={input.PeriodId:D}");
        Assert.Equal(25m, profitLoss.GetProperty("netIncome").GetDecimal());
        Assert.True(balanceSheet.GetProperty("isBalanced").GetBoolean());

        using (var validation = await owner.PostAsync(
                   $"/internal/companies/{input.CompanyId:D}/finance/fiscal-periods/{input.PeriodId:D}/reporting/validation", null))
        {
            var body = await validation.Content.ReadAsStringAsync();
            Assert.True(validation.IsSuccessStatusCode, body);
            using var json = JsonDocument.Parse(body);
            Assert.True(json.RootElement.GetProperty("isReadyToClose").GetBoolean(), body);
        }
        using (var close = await owner.PostAsJsonAsync(
                   $"/internal/companies/{input.CompanyId:D}/finance/fiscal-periods/{input.PeriodId:D}/reporting/close-and-lock",
                   new { reason = "Deterministic accounting integrity scenario completed." }))
        {
            Assert.True(close.IsSuccessStatusCode, await close.Content.ReadAsStringAsync());
        }

        Guid exportId;
        var exportRequest = new { fiscalPeriodId = input.PeriodId, idempotencyKey = "scenario:period:export:v1" };
        using (var requested = await owner.PostAsJsonAsync($"/internal/companies/{input.CompanyId:D}/finance/accounting/exports", exportRequest))
        {
            var body = await requested.Content.ReadAsStringAsync();
            Assert.True(requested.IsSuccessStatusCode, body);
            using var json = JsonDocument.Parse(body);
            exportId = json.RootElement.GetProperty("id").GetGuid();
        }
        using (var replay = await owner.PostAsJsonAsync($"/internal/companies/{input.CompanyId:D}/finance/accounting/exports", exportRequest))
        {
            using var json = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.Equal(exportId, json.RootElement.GetProperty("id").GetGuid());
        }
        await factory.ExecuteScopeAsync(async scope =>
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IAccountingReportingService>().RunDueExportsAsync(CancellationToken.None)));
        using var download = await owner.GetAsync($"/internal/companies/{input.CompanyId:D}/finance/accounting/exports/{exportId:D}/download");
        var exportBytes = await download.Content.ReadAsByteArrayAsync();
        Assert.True(download.IsSuccessStatusCode, Encoding.UTF8.GetString(exportBytes));
        var exportChecksum = Convert.ToHexString(SHA256.HashData(exportBytes)).ToLowerInvariant();
        Assert.Equal($"\"{exportChecksum}\"", download.Headers.ETag?.Tag);

        using var recoveryResponse = await owner.PostAsJsonAsync(
            $"/internal/companies/{input.CompanyId:D}/finance/accounting/operations/recovery-verification",
            new { fiscalPeriodId = input.PeriodId, verifyObjectContent = true });
        var recoveryBody = await recoveryResponse.Content.ReadAsStringAsync();
        Assert.True(recoveryResponse.IsSuccessStatusCode, recoveryBody);
        using var recovery = JsonDocument.Parse(recoveryBody);
        Assert.True(recovery.RootElement.GetProperty("isValid").GetBoolean(), recoveryBody);
        Assert.True(recovery.RootElement.GetProperty("objectContentVerified").GetBoolean());
        Assert.Equal(recovery.RootElement.GetProperty("totalDebit").GetDecimal(), recovery.RootElement.GetProperty("totalCredit").GetDecimal());
        Assert.Equal(64, recovery.RootElement.GetProperty("evidenceChecksum").GetString()!.Length);

        var evidence = await CaptureEvidenceAsync(factory, input.CompanyId, input.PeriodId);
        Assert.Equal(9, evidence.JournalCount);
        Assert.Equal(4, evidence.AllocationCount);
        Assert.Equal(5, evidence.BankRowCount);
        Assert.Equal(2, evidence.ApprovalCount);
        Assert.Equal(2, evidence.EvidenceLinkCount);
        Assert.Equal(1, evidence.TaxReviewAuditCount);
        Assert.Equal(FinanceSettlementStatuses.Paid, evidence.InvoiceSettlementStatus);
        Assert.Equal(FinanceSettlementStatuses.Paid, evidence.BillSettlementStatus);
        Assert.Equal(64, evidence.ClosedSnapshotChecksum.Length);
        Assert.Equal(exportChecksum, evidence.ExportChecksum);
    }

    private static async Task CompleteSetupAndReplayAsync(HttpClient owner, ScenarioInput input)
    {
        var request = new
        {
            baseCurrency = "USD",
            fiscalYearStart = new DateOnly(2026, 1, 1),
            policyPackKey = AccountingPolicyPackDefaults.CountryNeutralPackKey,
            policyPackVersion = AccountingPolicyPackDefaults.CountryNeutralBankingVersion,
            chartTemplateKey = "generic-accrual",
            accountRoleCodeAssignments = new Dictionary<string, string>
            {
                [AccountingAccountRoleKeys.Cash] = "1000",
                [AccountingAccountRoleKeys.Bank] = "1000",
                [AccountingAccountRoleKeys.AccountsReceivable] = "1100",
                [AccountingAccountRoleKeys.AccountsPayable] = "2000",
                ["equity"] = "3000",
                ["revenue"] = "4000",
                ["operating_expense"] = "5000",
                [AccountingAccountRoleKeys.Suspense] = "1900",
                [AccountingAccountRoleKeys.BankFee] = "5100",
                [AccountingAccountRoleKeys.RoundingDifference] = "5200",
                [AccountingAccountRoleKeys.ExchangeLoss] = "5300",
                [AccountingAccountRoleKeys.ExchangeGain] = "4100",
                [AccountingAccountRoleKeys.SettlementDiscount] = "5400"
            },
            idempotencyKey = "scenario:accounting-setup:v1"
        };
        using var first = await owner.PostAsJsonAsync($"/internal/companies/{input.CompanyId:D}/finance/accounting/setup/complete", request);
        using var replay = await owner.PostAsJsonAsync($"/internal/companies/{input.CompanyId:D}/finance/accounting/setup/complete", request);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
    }

    private static async Task<PostingResult> SubmitApprovePostAsync(
        HttpClient owner, HttpClient approver, ScenarioInput input, bool invoice, Guid expenseId)
    {
        var route = invoice ? InvoiceRoute(input, "accounting") : BillRoute(input, "accounting");
        var lines = invoice
            ? new[] { new { description = "Consulting", amount = 100m, costAccountId = (Guid?)null, taxRuleKey = "generic-exempt" } }
            : new[] { new { description = "Office services", amount = 100m, costAccountId = (Guid?)expenseId, taxRuleKey = "generic-exempt" } };
        var accounting = new { fiscalPeriodId = input.PeriodId, voucherSeriesCode = "G", exchangeRate = (decimal?)null, lines };
        using var preview = await owner.PostAsJsonAsync($"{route}/preview", accounting);
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var submitRequest = new
        {
            accounting.fiscalPeriodId, accounting.voucherSeriesCode, accounting.exchangeRate, accounting.lines,
            expectedVersion = (long?)null, idempotencyKey = $"scenario:{(invoice ? "invoice" : "bill")}:submit"
        };
        using var submitted = await owner.PostAsJsonAsync($"{route}/submit", submitRequest);
        var submittedBody = await submitted.Content.ReadAsStringAsync();
        Assert.True(submitted.IsSuccessStatusCode, submittedBody);
        using var submittedJson = JsonDocument.Parse(submittedBody);
        var approvalId = submittedJson.RootElement.GetProperty("approvalRequestId").GetGuid();
        var version = submittedJson.RootElement.GetProperty("state").GetProperty("sourceVersion").GetInt64();
        using (var submitReplay = await owner.PostAsJsonAsync($"{route}/submit", submitRequest))
        {
            using var submitReplayJson = JsonDocument.Parse(await submitReplay.Content.ReadAsStringAsync());
            Assert.True(submitReplay.IsSuccessStatusCode);
            Assert.Equal(approvalId, submitReplayJson.RootElement.GetProperty("approvalRequestId").GetGuid());
        }
        await ApproveAsync(approver, input.CompanyId, approvalId);
        var postRequest = new { expectedVersion = version, idempotencyKey = $"scenario:{(invoice ? "invoice" : "bill")}:post" };
        using var posted = await owner.PostAsJsonAsync($"{route}/post", postRequest);
        var postedBody = await posted.Content.ReadAsStringAsync();
        Assert.True(posted.IsSuccessStatusCode, postedBody);
        using var postedJson = JsonDocument.Parse(postedBody);
        var journalId = postedJson.RootElement.GetProperty("journal").GetProperty("id").GetGuid();
        using var replay = await owner.PostAsJsonAsync($"{route}/post", postRequest);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayJson.RootElement.GetProperty("isIdempotentReplay").GetBoolean());
        return new PostingResult(version, journalId);
    }

    private static async Task ApproveAsync(HttpClient approver, Guid companyId, Guid approvalId)
    {
        using var approval = await approver.GetAsync($"/api/companies/{companyId:D}/approvals/{approvalId:D}");
        using var json = JsonDocument.Parse(await approval.Content.ReadAsStringAsync());
        var stepId = json.RootElement.GetProperty("steps")[0].GetProperty("id").GetGuid();
        using var decision = await approver.PostAsJsonAsync($"/api/companies/{companyId:D}/approvals/{approvalId:D}/decisions",
            new { decision = "approve", stepId, comment = "Accounting evidence reviewed." });
        Assert.True(decision.IsSuccessStatusCode, await decision.Content.ReadAsStringAsync());
    }

    private static object Allocation(Guid? invoiceId, Guid? billId, decimal amount, string key) =>
        new { invoiceId, billId, allocatedAmount = amount, currency = "USD", idempotencyKey = key };

    private static async Task AllocateAndReplayAsync(HttpClient owner, ScenarioInput input, Guid paymentId,
        Guid? invoiceId, Guid? billId, decimal amount, string key)
    {
        var route = AllocationRoute(input, paymentId);
        var request = Allocation(invoiceId, billId, amount, key);
        using var first = await owner.PostAsJsonAsync(route, request);
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.IsSuccessStatusCode, firstBody);
        using var firstJson = JsonDocument.Parse(firstBody);
        using var replay = await owner.PostAsJsonAsync(route, request);
        var replayBody = await replay.Content.ReadAsStringAsync();
        Assert.True(replay.IsSuccessStatusCode, replayBody);
        using var replayJson = JsonDocument.Parse(replayBody);
        Assert.Equal(firstJson.RootElement.GetProperty("id").GetGuid(), replayJson.RootElement.GetProperty("id").GetGuid());
        Assert.True(replayJson.RootElement.GetProperty("isIdempotentReplay").GetBoolean());
    }

    private static async Task<ImportedBankTransactions> ImportStatementAndReplayAsync(
        HttpClient owner, TestWebApplicationFactory factory, ScenarioInput input, Guid bankAccountId)
    {
        var rows = new[]
        {
            new { rowIdentity = "scenario-bank-in-a", bookingDateUtc = input.BookingUtc, valueDateUtc = input.BookingUtc, amount = 40m, currency = "USD", referenceText = "Customer settlement A", counterparty = "Scenario customer", externalReference = "BANK-IN-A" },
            new { rowIdentity = "scenario-bank-in-b", bookingDateUtc = input.BookingUtc, valueDateUtc = input.BookingUtc, amount = 60m, currency = "USD", referenceText = "Customer settlement B", counterparty = "Scenario customer", externalReference = "BANK-IN-B" },
            new { rowIdentity = "scenario-bank-out-a", bookingDateUtc = input.BookingUtc, valueDateUtc = input.BookingUtc, amount = -25m, currency = "USD", referenceText = "Supplier settlement A", counterparty = "Scenario supplier", externalReference = "BANK-OUT-A" },
            new { rowIdentity = "scenario-bank-out-b", bookingDateUtc = input.BookingUtc, valueDateUtc = input.BookingUtc, amount = -75m, currency = "USD", referenceText = "Supplier settlement B", counterparty = "Scenario supplier", externalReference = "BANK-OUT-B" },
            new { rowIdentity = "scenario-bank-suspense", bookingDateUtc = input.BookingUtc, valueDateUtc = input.BookingUtc, amount = 25m, currency = "USD", referenceText = "Unclassified receipt", counterparty = "Unknown", externalReference = "BANK-SUSPENSE" }
        };
        var request = new { bankAccountId, sourceKey = "scenario-csv", statementIdentity = "scenario-statement-001", contentHash = new string('a', 64), rows };
        using var first = await owner.PostAsJsonAsync($"/internal/companies/{input.CompanyId:D}/finance/bank-transactions/imports", request);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        using var replay = await owner.PostAsJsonAsync($"/internal/companies/{input.CompanyId:D}/finance/bank-transactions/imports", request);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayJson.RootElement.GetProperty("isIdempotentReplay").GetBoolean());
        using var overlap = await owner.PostAsJsonAsync($"/internal/companies/{input.CompanyId:D}/finance/bank-transactions/imports",
            new { bankAccountId, sourceKey = "scenario-csv", statementIdentity = "scenario-statement-002", contentHash = new string('b', 64), rows = new[] { rows[0] } });
        using var overlapJson = JsonDocument.Parse(await overlap.Content.ReadAsStringAsync());
        Assert.Equal(1, overlapJson.RootElement.GetProperty("duplicateCount").GetInt32());
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var imported = await db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == input.CompanyId && x.ImportSource == "scenario-csv")
                .ToDictionaryAsync(x => x.RowIdentity!, x => x.Id);
            return new ImportedBankTransactions(imported["scenario-bank-in-a"], imported["scenario-bank-in-b"],
                imported["scenario-bank-out-a"], imported["scenario-bank-out-b"], imported["scenario-bank-suspense"]);
        });
    }

    private static async Task ReconcilePaymentAndReplayAsync(HttpClient owner, ScenarioInput input, Guid transactionId,
        IReadOnlyList<(Guid PaymentId, decimal Amount)> matches, string key)
    {
        var request = new
        {
            payments = matches.Select(x => new { paymentId = x.PaymentId, allocatedAmount = x.Amount }).ToArray(),
            expectedSourceVersion = 1L,
            handlingMode = "payment",
            idempotencyKey = key
        };
        var route = $"/internal/companies/{input.CompanyId:D}/finance/bank-transactions/{transactionId:D}/reconcile";
        using var first = await owner.PostAsJsonAsync(route, request);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        using var replay = await owner.PostAsJsonAsync(route, request);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
    }

    private static async Task SuspenseAndCorrectAsync(HttpClient owner, ScenarioInput input, Guid transactionId, Guid revenueId)
    {
        var route = $"/internal/companies/{input.CompanyId:D}/finance/bank-transactions/{transactionId:D}";
        var suspenseRequest = new
        {
            payments = Array.Empty<object>(), expectedSourceVersion = 1L, handlingMode = "suspense",
            reviewReason = "Counterparty evidence is incomplete.", idempotencyKey = "scenario:bank:suspense"
        };
        using var suspense = await owner.PostAsJsonAsync($"{route}/reconcile", suspenseRequest);
        Assert.True(suspense.IsSuccessStatusCode, await suspense.Content.ReadAsStringAsync());
        using (var suspenseReplay = await owner.PostAsJsonAsync($"{route}/reconcile", suspenseRequest))
            Assert.True(suspenseReplay.IsSuccessStatusCode, await suspenseReplay.Content.ReadAsStringAsync());
        var suspenseDetail = await GetJsonAsync(owner, $"{route}/reconciliation");
        Assert.Equal("suspense", suspenseDetail.GetProperty("state").GetString());
        Assert.Equal("open", suspenseDetail.GetProperty("followUp").GetProperty("status").GetString());
        using var correction = await owner.PostAsJsonAsync($"{route}/reclassify-suspense", new
        {
            targetFinanceAccountId = revenueId, fiscalPeriodId = input.PeriodId,
            postingDate = DateOnly.FromDateTime(input.BookingUtc), reason = "Evidence confirms other operating income.",
            expectedSourceVersion = 1L, idempotencyKey = "scenario:bank:suspense:correction"
        });
        Assert.True(correction.IsSuccessStatusCode, await correction.Content.ReadAsStringAsync());
        using var replay = await owner.PostAsJsonAsync($"{route}/reclassify-suspense", new
        {
            targetFinanceAccountId = revenueId, fiscalPeriodId = input.PeriodId,
            postingDate = DateOnly.FromDateTime(input.BookingUtc), reason = "Evidence confirms other operating income.",
            expectedSourceVersion = 1L, idempotencyKey = "scenario:bank:suspense:correction"
        });
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    private static void SeedEvidenceObjects(TestWebApplicationFactory factory, ScenarioInput input)
    {
        factory.DocumentStorage.Seed(input.InvoiceStorageKey, input.InvoiceEvidence);
        factory.DocumentStorage.Seed(input.BillStorageKey, input.BillEvidence);
    }

    private static Task<(int JournalCount, int AllocationCount)> CountsAsync(TestWebApplicationFactory factory, Guid companyId) =>
        factory.ExecuteDbContextAsync(async db => (
            await db.LedgerEntries.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await db.PaymentAllocations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId)));

    private static Task<ScenarioEvidence> CaptureEvidenceAsync(TestWebApplicationFactory factory, Guid companyId, Guid periodId) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var invoice = await db.FinanceInvoices.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            var bill = await db.FinanceBills.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            var history = await db.AccountingPeriodHistory.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == companyId && x.FiscalPeriodId == periodId && x.Action == AccountingPeriodHistoryActions.ClosedAndLocked);
            var export = await db.AccountingExportJobs.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            return new ScenarioEvidence(
                await db.LedgerEntries.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
                await db.PaymentAllocations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
                await db.BankTransactions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.ImportSource == "scenario-csv"),
                await db.ApprovalRequests.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
                await db.LedgerEntryEvidenceLinks.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
                await db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.Action == AuditEventActions.AccountingTaxSummaryReviewed),
                invoice.SettlementStatus, bill.SettlementStatus, history.SnapshotChecksum!, export.Checksum!);
        });

    private static HttpClient CreateClient(TestWebApplicationFactory factory, string subject, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private static string InvoiceRoute(ScenarioInput input, string suffix) =>
        $"/internal/companies/{input.CompanyId:D}/finance/invoices/{input.InvoiceId:D}/{suffix}";
    private static string BillRoute(ScenarioInput input, string suffix) =>
        $"/internal/companies/{input.CompanyId:D}/finance/bills/{input.BillId:D}/{suffix}";
    private static string AllocationRoute(ScenarioInput input, Guid paymentId) =>
        $"/internal/companies/{input.CompanyId:D}/finance/payments/{paymentId:D}/allocations";

    private sealed class AccountingIntegrityInputBuilder
    {
        public static async Task<ScenarioInput> CreateAsync(TestWebApplicationFactory factory)
        {
            var input = ScenarioInput.Create();
            await factory.SeedAsync(db =>
            {
                db.Companies.AddRange(new Company(input.CompanyId, "Accounting Integrity Company"), new Company(input.OtherCompanyId, "Other Company"));
                db.Users.AddRange(
                    new User(input.OwnerId, input.OwnerEmail, "Owner", "dev-header", input.OwnerSubject),
                    new User(input.ApproverId, input.ApproverEmail, "Approver", "dev-header", input.ApproverSubject),
                    new User(input.OutsiderId, input.OutsiderEmail, "Outsider", "dev-header", input.OutsiderSubject));
                db.CompanyMemberships.AddRange(
                    new CompanyMembership(Stable("membership-owner"), input.CompanyId, input.OwnerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                    new CompanyMembership(Stable("membership-approver"), input.CompanyId, input.ApproverId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active),
                    new CompanyMembership(Stable("membership-outsider"), input.OtherCompanyId, input.OutsiderId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
                return Task.CompletedTask;
            });
            return input;
        }

        public static async Task<ScenarioAccounts> AddOperationalInputsAsync(TestWebApplicationFactory factory, ScenarioInput input)
        {
            return await factory.ExecuteDbContextAsync(async db =>
            {
                var configuration = await db.AccountingConfigurations.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == input.CompanyId);
                var roles = await db.AccountingConfigurationAccountRoles.IgnoreQueryFilters()
                    .Where(x => x.CompanyId == input.CompanyId).ToDictionaryAsync(x => x.RoleKey, x => x.FinanceAccountId);
                roles = new Dictionary<string, Guid>(roles, StringComparer.OrdinalIgnoreCase);
                var period = await db.FiscalPeriods.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == input.CompanyId && x.StartUtc <= input.BookingUtc && x.EndUtc > input.BookingUtc);
                input.PeriodId = period.Id;
                var bankId = roles[AccountingAccountRoleKeys.Bank];
                var revenueId = roles["revenue"];
                var expenseId = roles["operating_expense"];
                db.FinanceCounterparties.AddRange(
                    new FinanceCounterparty(input.CustomerId, input.CompanyId, "Scenario Customer", "customer", createdUtc: input.BookingUtc),
                    new FinanceCounterparty(input.SupplierId, input.CompanyId, "Scenario Supplier", "supplier", createdUtc: input.BookingUtc));
                db.CompanyKnowledgeDocuments.AddRange(
                    Document(input.InvoiceDocumentId, input.CompanyId, "Invoice evidence", input.InvoiceStorageKey, "invoice.txt", input.InvoiceEvidence),
                    Document(input.BillDocumentId, input.CompanyId, "Bill evidence", input.BillStorageKey, "bill.txt", input.BillEvidence));
                db.FinanceInvoices.Add(new FinanceInvoice(input.InvoiceId, input.CompanyId, input.CustomerId, "INV-SCENARIO-001",
                    input.BookingUtc, input.BookingUtc.AddDays(30), 100m, "USD", "approved", input.InvoiceDocumentId, input.BookingUtc, input.BookingUtc));
                db.FinanceBills.Add(new FinanceBill(input.BillId, input.CompanyId, input.SupplierId, "BILL-SCENARIO-001",
                    input.BookingUtc, input.BookingUtc.AddDays(30), 100m, "USD", "approved", input.BillDocumentId, input.BookingUtc, input.BookingUtc));
                db.Payments.AddRange(
                    Payment(input.IncomingPaymentAId, input.CompanyId, PaymentTypes.Incoming, 40m, input.BookingUtc, "INV-SCENARIO-001"),
                    Payment(input.IncomingPaymentBId, input.CompanyId, PaymentTypes.Incoming, 60m, input.BookingUtc, "INV-SCENARIO-001"),
                    Payment(input.OutgoingPaymentAId, input.CompanyId, PaymentTypes.Outgoing, 25m, input.BookingUtc, "BILL-SCENARIO-001"),
                    Payment(input.OutgoingPaymentBId, input.CompanyId, PaymentTypes.Outgoing, 75m, input.BookingUtc, "BILL-SCENARIO-001"));
                var bankAccount = new CompanyBankAccount(Stable("bank-account"), input.CompanyId, bankId, "Operating Account",
                    "Scenario Bank", "**** 2026", "USD", "operating", true, true, input.BookingUtc, input.BookingUtc);
                db.CompanyBankAccounts.Add(bankAccount);
                await db.SaveChangesAsync();
                return new ScenarioAccounts(configuration.Id, bankAccount.Id, revenueId, expenseId);
            });
        }

        private static CompanyKnowledgeDocument Document(Guid id, Guid companyId, string title, string storageKey, string fileName, string content)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            return new CompanyKnowledgeDocument(id, companyId, title, CompanyKnowledgeDocumentType.Reference, storageKey, null,
                fileName, "text/plain", ".txt", Encoding.UTF8.GetByteCount(content),
                new Dictionary<string, JsonNode?> { ["checksum_sha256"] = JsonValue.Create(hash) },
                new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
        }

        private static Payment Payment(Guid id, Guid companyId, string type, decimal amount, DateTime date, string reference) =>
            new(id, companyId, type, amount, "USD", date, "bank_transfer", PaymentStatuses.Completed, reference);

    }

    private sealed class FailNextSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _armed;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var isAllocationWrite = eventData.Context?.ChangeTracker.Entries<PaymentAllocation>()
                .Any(x => x.State == EntityState.Added) == true;
            if (isAllocationWrite && Interlocked.Exchange(ref _armed, 0) == 1)
                throw new InjectedSaveFailureException();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // A timeout is transient to SQL Server's execution strategy and is retried by design. This
    // deterministic non-transient failure verifies rollback through the same path on SQLite and
    // SQL Server without the provider converting the intended failure into a retry outcome.
    private sealed class InjectedSaveFailureException : Exception
    {
        public InjectedSaveFailureException() : base("Injected transactional rollback.") { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private static Guid Stable(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"accounting-integrity:{value}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed record PostingResult(long SourceVersion, Guid JournalId);
    private sealed record ImportedBankTransactions(Guid IncomingAId, Guid IncomingBId, Guid OutgoingAId, Guid OutgoingBId, Guid SuspenseId);
    private sealed record ScenarioAccounts(Guid ConfigurationId, Guid BankAccountId, Guid RevenueId, Guid ExpenseId);
    private sealed record ScenarioEvidence(int JournalCount, int AllocationCount, int BankRowCount, int ApprovalCount,
        int EvidenceLinkCount, int TaxReviewAuditCount, string InvoiceSettlementStatus, string BillSettlementStatus,
        string ClosedSnapshotChecksum, string ExportChecksum);

    private sealed class ScenarioInput
    {
        public Guid CompanyId { get; } = Stable("company");
        public Guid OtherCompanyId { get; } = Stable("other-company");
        public Guid OwnerId { get; } = Stable("owner");
        public Guid ApproverId { get; } = Stable("approver");
        public Guid OutsiderId { get; } = Stable("outsider");
        public Guid CustomerId { get; } = Stable("customer");
        public Guid SupplierId { get; } = Stable("supplier");
        public Guid InvoiceId { get; } = Stable("invoice");
        public Guid BillId { get; } = Stable("bill");
        public Guid InvoiceDocumentId { get; } = Stable("invoice-document");
        public Guid BillDocumentId { get; } = Stable("bill-document");
        public Guid IncomingPaymentAId { get; } = Stable("incoming-a");
        public Guid IncomingPaymentBId { get; } = Stable("incoming-b");
        public Guid OutgoingPaymentAId { get; } = Stable("outgoing-a");
        public Guid OutgoingPaymentBId { get; } = Stable("outgoing-b");
        public Guid PeriodId { get; set; }
        public DateTime BookingUtc { get; } = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        public string OwnerSubject { get; } = "accounting-integrity-owner";
        public string OwnerEmail { get; } = "accounting-integrity-owner@example.com";
        public string ApproverSubject { get; } = "accounting-integrity-approver";
        public string ApproverEmail { get; } = "accounting-integrity-approver@example.com";
        public string OutsiderSubject { get; } = "accounting-integrity-outsider";
        public string OutsiderEmail { get; } = "accounting-integrity-outsider@example.com";
        public string InvoiceStorageKey { get; } = "accounting-integrity/invoice.txt";
        public string BillStorageKey { get; } = "accounting-integrity/bill.txt";
        public string InvoiceEvidence { get; } = "Immutable invoice evidence for INV-SCENARIO-001.";
        public string BillEvidence { get; } = "Immutable supplier bill evidence for BILL-SCENARIO-001.";
        public static ScenarioInput Create() => new();
    }
}
