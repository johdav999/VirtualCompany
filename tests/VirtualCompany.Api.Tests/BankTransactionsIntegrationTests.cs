using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BankTransactionsIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Bank_transaction_endpoints_filter_and_isolate_by_company()
    {
        var seed = await SeedFinanceBankTransactionsAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var listResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions?bankAccountId={seed.BankAccountId}&status=reconciled&bookingDateFromUtc=2026-01-01T00:00:00Z&minAmount=1&limit=50");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var items = await listResponse.Content.ReadFromJsonAsync<List<BankTransactionResponse>>();
        Assert.NotNull(items);
        Assert.NotEmpty(items!);
        Assert.All(items!, item =>
        {
            Assert.Equal(seed.BankAccountId, item.BankAccountId);
            Assert.Equal("reconciled", item.Status);
            Assert.True(item.Amount > 0m);
        });

        var detailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{items![0].Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<BankTransactionDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(items[0].Id, detail!.Id);
        Assert.NotNull(detail.BankAccount);
        Assert.Equal(items[0].Status, detail.Status);
        Assert.Equal(seed.BankAccountId, detail.BankAccountId);
        Assert.False(string.IsNullOrWhiteSpace(detail.BankAccount!.DisplayName));
        
        var crossTenantDetail = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.OtherCompanyTransactionId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDetail.StatusCode);
    }

    [Fact]
    public async Task Reconcile_endpoint_replays_bank_event_without_duplicate_cash_ledger_posting()
    {
        var seed = await SeedReconciliationScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var firstResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest
            {
                Payments =
                [
                    new ReconcileBankTransactionPaymentRequest
                    {
                        PaymentId = seed.FirstPaymentId,
                        AllocatedAmount = 100m
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<BankTransactionDetailResponse>();
        Assert.NotNull(firstResult);
        Assert.Equal("partially_reconciled", firstResult!.Status);
        Assert.Equal(100m, firstResult.ReconciledAmount);
        Assert.Single(firstResult.LinkedPayments);
        Assert.Null(firstResult.CashLedgerEntryId);

        var retryResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest
            {
                Payments =
                [
                    new ReconcileBankTransactionPaymentRequest
                    {
                        PaymentId = seed.FirstPaymentId,
                        AllocatedAmount = 100m
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retriedResult = await retryResponse.Content.ReadFromJsonAsync<BankTransactionDetailResponse>();
        Assert.NotNull(retriedResult);
        Assert.Equal("partially_reconciled", retriedResult!.Status);
        Assert.Single(retriedResult.LinkedPayments);
        Assert.Null(retriedResult.CashLedgerEntryId);

        var secondResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest
            {
                Payments =
                [
                    new ReconcileBankTransactionPaymentRequest
                    {
                        PaymentId = seed.SecondPaymentId,
                        AllocatedAmount = 50m
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<BankTransactionDetailResponse>();
        Assert.NotNull(secondResult);
        Assert.Equal("reconciled", secondResult!.Status);
        Assert.Equal(150m, secondResult.ReconciledAmount);
        Assert.Equal(2, secondResult.LinkedPayments.Count);
        Assert.NotNull(secondResult.CashLedgerEntryId);

        var counts = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var paymentLinks = await dbContext.BankTransactionPaymentLinks
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == seed.CompanyId && x.BankTransactionId == seed.BankTransactionId);
            var cashLedgerLinks = await dbContext.BankTransactionCashLedgerLinks
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == seed.CompanyId && x.BankTransactionId == seed.BankTransactionId);
            var ledgerEntries = await dbContext.LedgerEntries
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.SourceType == FinanceCashPostingSourceTypes.BankTransaction &&
                    x.SourceId == seed.BankTransactionId.ToString("D"))
                .ToListAsync();
            var ledgerLines = await dbContext.LedgerEntryLines
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.LedgerEntryId == secondResult.CashLedgerEntryId)
                .OrderBy(x => x.DebitAmount == 0m)
                .ToListAsync();
            var sourceMappings = await dbContext.LedgerEntrySourceMappings
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.SourceType == FinanceCashPostingSourceTypes.BankTransaction &&
                    x.SourceId == seed.BankTransactionId.ToString("D"))
                .OrderBy(x => x.PostedAtUtc)
                .ToListAsync();
            var reconciliationAudits = await dbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.Action == AuditEventActions.AccountingBankReconciliationReviewed &&
                x.TargetId == seed.BankTransactionId.ToString("N"));

            return new
            {
                paymentLinks,
                cashLedgerLinks,
                ledgerEntry = ledgerEntries.Single(),
                ledgerEntryCount = ledgerEntries.Count,
                ledgerLines,
                reconciliationAudits,
                sourceMappings = sourceMappings.Count,
                sourceMapping = sourceMappings.Single()
            };
        });

        Assert.Equal(2, counts.paymentLinks);
        Assert.Equal(1, counts.cashLedgerLinks);
        Assert.Equal(1, counts.ledgerEntryCount);
        Assert.Equal(1, counts.sourceMappings);
        Assert.Equal(2, counts.reconciliationAudits);
        Assert.Equal(seed.CompanyId, counts.ledgerEntry.CompanyId);
        Assert.Equal(LedgerEntryStatuses.Posted, counts.ledgerEntry.Status);
        Assert.Equal(FinanceCashPostingSourceTypes.BankTransaction, counts.ledgerEntry.SourceType);
        Assert.Equal(seed.BankTransactionId.ToString("D"), counts.ledgerEntry.SourceId);
        Assert.Equal(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), counts.ledgerEntry.PostedAtUtc);
        Assert.Equal(secondResult.CashLedgerEntryId, counts.ledgerEntry.Id);
        Assert.Equal(2, counts.ledgerLines.Count);
        Assert.Equal(seed.CashAccountId, counts.ledgerLines[0].FinanceAccountId);
        Assert.Equal(150m, counts.ledgerLines[0].DebitAmount);
        Assert.Equal(0m, counts.ledgerLines[0].CreditAmount);
        Assert.Equal(seed.ReceivablesAccountId, counts.ledgerLines[1].FinanceAccountId);
        Assert.Equal(0m, counts.ledgerLines[1].DebitAmount);
        Assert.Equal(150m, counts.ledgerLines[1].CreditAmount);
        Assert.Equal(counts.ledgerLines.Sum(x => x.DebitAmount), counts.ledgerLines.Sum(x => x.CreditAmount));
        Assert.Equal(secondResult.CashLedgerEntryId, counts.sourceMapping.LedgerEntryId);
        Assert.Equal(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), counts.sourceMapping.PostedAtUtc);
    }

    [Fact]
    public async Task Reconcile_endpoint_rejects_pending_and_overallocated_payments()
    {
        var seed = await SeedReconciliationScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var pendingResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest(
            [
                new ReconcileBankTransactionPaymentRequest(seed.PendingPaymentId, 25m)
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, pendingResponse.StatusCode);
        var pendingProblem = await pendingResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(pendingProblem);
        Assert.Contains("PaymentId", pendingProblem!.Errors.Keys);

        var overallocatedResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest(
            [
                new ReconcileBankTransactionPaymentRequest(seed.FirstPaymentId, 125m)
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, overallocatedResponse.StatusCode);
        var overallocatedProblem = await overallocatedResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(overallocatedProblem);
        Assert.Contains("AllocatedAmount", overallocatedProblem!.Errors.Keys);

        var wrongDirectionResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest([
                new ReconcileBankTransactionPaymentRequest(seed.WrongDirectionPaymentId, 25m)
            ]));
        Assert.Equal(HttpStatusCode.BadRequest, wrongDirectionResponse.StatusCode);

        var wrongCurrencyResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest([
                new ReconcileBankTransactionPaymentRequest(seed.WrongCurrencyPaymentId, 25m)
            ]));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrencyResponse.StatusCode);
    }

    [Fact]
    public async Task Reconciliation_reuses_existing_payment_cash_posting_and_posts_only_the_uncovered_remainder()
    {
        var seed = await SeedReconciliationScenarioAsync();
        var existingCashJournalId = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var journalId = Guid.NewGuid();
            var postedUtc = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc);
            var fiscalPeriodId = await dbContext.FiscalPeriods.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.StartUtc <= postedUtc && x.EndUtc > postedUtc)
                .Select(x => x.Id)
                .SingleAsync();
            var journal = new LedgerEntry(journalId, seed.CompanyId, fiscalPeriodId, "B-EXISTING-000001", postedUtc,
                LedgerEntryStatuses.Posted, "Existing governed payment settlement", FinanceCashPostingSourceTypes.PaymentSettlement,
                "existing-payment-settlement", postedUtc, postedUtc, postedUtc, documentDate: DateOnly.FromDateTime(postedUtc),
                postingDate: DateOnly.FromDateTime(postedUtc), baseCurrency: "USD", postingType: LedgerPostingTypeValues.CashSettlement,
                sourceVersion: "1", idempotencyKey: "existing-payment-settlement");
            journal.Lines.Add(new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, journalId, seed.CashAccountId, 100m, 0m, "USD", createdUtc: postedUtc));
            journal.Lines.Add(new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, journalId, seed.ReceivablesAccountId, 0m, 100m, "USD", createdUtc: postedUtc));
            dbContext.LedgerEntries.Add(journal);
            dbContext.LedgerPostingIdentities.Add(new LedgerPostingIdentity(Guid.NewGuid(), seed.CompanyId, journalId, "post",
                FinanceCashPostingSourceTypes.PaymentSettlement, "existing-payment-settlement", "1", "existing-payment-settlement",
                new string('a', 64), postedUtc));
            dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(Guid.NewGuid(), seed.CompanyId, seed.FirstPaymentId,
                journalId, FinanceCashPostingSourceTypes.PaymentSettlement, "existing-payment-settlement", postedUtc, postedUtc));
            await dbContext.SaveChangesAsync();
            return journalId;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest
            {
                Payments =
                [
                    new(seed.FirstPaymentId, 100m),
                    new(seed.SecondPaymentId, 50m)
                ]
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reconciled = await response.Content.ReadFromJsonAsync<BankTransactionDetailResponse>();
        Assert.NotNull(reconciled?.CashLedgerEntryId);

        var accounting = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var linkedJournalIds = await dbContext.BankTransactionCashLedgerLinks.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.BankTransactionId == seed.BankTransactionId)
                .Select(x => x.LedgerEntryId)
                .ToListAsync();
            var lines = await dbContext.LedgerEntryLines.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && linkedJournalIds.Contains(x.LedgerEntryId))
                .ToListAsync();
            return new { linkedJournalIds, lines };
        });

        Assert.Equal(2, accounting.linkedJournalIds.Count);
        Assert.Contains(existingCashJournalId, accounting.linkedJournalIds);
        Assert.Contains(reconciled!.CashLedgerEntryId!.Value, accounting.linkedJournalIds);
        Assert.Equal(150m, accounting.lines.Where(x => x.FinanceAccountId == seed.CashAccountId).Sum(x => x.DebitAmount - x.CreditAmount));
        Assert.Equal(-150m, accounting.lines.Where(x => x.FinanceAccountId == seed.ReceivablesAccountId).Sum(x => x.DebitAmount - x.CreditAmount));
        Assert.Equal(accounting.lines.Sum(x => x.DebitAmount), accounting.lines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task Statement_import_replays_and_overlaps_without_duplicating_rows_and_reports_content_conflicts()
    {
        var seed = await SeedReconciliationScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var row = new
        {
            RowIdentity = "row-stable-001",
            BookingDateUtc = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc),
            ValueDateUtc = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc),
            Amount = 42.50m,
            Currency = "USD",
            ReferenceText = "Imported receipt",
            Counterparty = "Contoso",
            ExternalReference = "BANK-ROW-001"
        };

        var firstRequest = new { seed.BankAccountId, SourceKey = "csv", StatementIdentity = "statement-001", ContentHash = new string('a', 64), Rows = new[] { row } };
        var first = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/bank-transactions/imports", firstRequest);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<BankStatementImportResponse>();
        Assert.NotNull(firstResult);
        Assert.Equal(1, firstResult!.ImportedCount);

        var replay = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/bank-transactions/imports", firstRequest);
        var replayResult = await replay.Content.ReadFromJsonAsync<BankStatementImportResponse>();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replayResult!.IsIdempotentReplay);

        var overlap = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/bank-transactions/imports",
            new { seed.BankAccountId, SourceKey = "csv", StatementIdentity = "statement-002", ContentHash = new string('b', 64), Rows = new[] { row } });
        var overlapResult = await overlap.Content.ReadFromJsonAsync<BankStatementImportResponse>();
        Assert.Equal(HttpStatusCode.OK, overlap.StatusCode);
        Assert.Equal(1, overlapResult!.DuplicateCount);

        var conflict = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/bank-transactions/imports",
            new
            {
                seed.BankAccountId,
                SourceKey = "csv",
                StatementIdentity = "statement-003",
                ContentHash = new string('c', 64),
                Rows = new[] { new { row.RowIdentity, row.BookingDateUtc, row.ValueDateUtc, Amount = 99m, row.Currency, row.ReferenceText, row.Counterparty, row.ExternalReference } }
            });
        var conflictResult = await conflict.Content.ReadFromJsonAsync<BankStatementImportResponse>();
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        Assert.Equal(1, conflictResult!.ConflictCount);
        Assert.Contains("row-stable-001", conflictResult.ConflictRowIdentities);

        var importedRows = await _factory.ExecuteDbContextAsync(db => db.BankTransactions.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.ImportSource == "csv"));
        Assert.Equal(1, importedRows);
    }

    [Fact]
    public async Task Explicit_exchange_adjustment_completes_a_balanced_reconciliation_through_configured_roles()
    {
        var seed = await SeedReconciliationScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest
            {
                Payments =
                [
                    new(seed.FirstPaymentId, 100m),
                    new(seed.SecondPaymentId, 45m)
                ],
                Adjustments = [new() { Kind = "exchange_gain", CreditAmount = 5m, Explanation = "Bank conversion difference" }]
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BankTransactionDetailResponse>();
        Assert.NotNull(result?.CashLedgerEntryId);
        Assert.Equal(150m, result!.ReconciledAmount);
        var lines = await _factory.ExecuteDbContextAsync(db => db.LedgerEntryLines.IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId && x.LedgerEntryId == result.CashLedgerEntryId)
            .ToListAsync());
        Assert.Equal(3, lines.Count);
        var differenceLine = Assert.Single(lines, x => x.FinanceAccountId == seed.ExchangeGainAccountId);
        Assert.Equal(5m, differenceLine.CreditAmount);
        Assert.Equal(lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task Suspense_reclassification_keeps_original_immutable_and_creates_linked_balanced_corrections()
    {
        var seed = await SeedReconciliationScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var suspense = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconcile",
            new ReconcileBankTransactionRequest
            {
                HandlingMode = "suspense",
                ReviewReason = "Counterparty evidence is incomplete; finance will follow up."
            });
        Assert.Equal(HttpStatusCode.OK, suspense.StatusCode);
        var suspenseTransaction = await suspense.Content.ReadFromJsonAsync<BankTransactionDetailResponse>();
        Assert.NotNull(suspenseTransaction?.CashLedgerEntryId);

        var originalBefore = await _factory.ExecuteDbContextAsync(async db =>
        {
            var entry = await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == suspenseTransaction!.CashLedgerEntryId);
            var lines = await db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.LedgerEntryId == entry.Id)
                .OrderBy(x => x.FinanceAccountId).Select(x => new { x.FinanceAccountId, x.DebitAmount, x.CreditAmount }).ToListAsync();
            return new { entry.EntryNumber, entry.Status, Lines = lines };
        });

        var detailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reconciliation");
        var suspenseDetail = await detailResponse.Content.ReadFromJsonAsync<BankReconciliationDetailResponse>();
        Assert.Equal("suspense", suspenseDetail!.State);
        Assert.Equal("open", suspenseDetail.FollowUp!.Status);

        var correction = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reclassify-suspense",
            new
            {
                TargetFinanceAccountId = seed.CategoryAccountId,
                FiscalPeriodId = seed.FiscalPeriodId,
                PostingDate = new DateOnly(2026, 4, 22),
                Reason = "Evidence confirms other operating income.",
                ExpectedSourceVersion = 1,
                IdempotencyKey = $"suspense-correction:{seed.BankTransactionId:N}"
            });
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        var corrected = await correction.Content.ReadFromJsonAsync<BankReconciliationDetailResponse>();
        Assert.Equal("correction", corrected!.State);
        Assert.Equal("resolved", corrected.FollowUp!.Status);
        Assert.Equal(3, corrected.Journals.Count);

        var correctionReplay = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bank-transactions/{seed.BankTransactionId}/reclassify-suspense",
            new
            {
                TargetFinanceAccountId = seed.CategoryAccountId,
                FiscalPeriodId = seed.FiscalPeriodId,
                PostingDate = new DateOnly(2026, 4, 22),
                Reason = "Evidence confirms other operating income.",
                ExpectedSourceVersion = 1,
                IdempotencyKey = $"suspense-correction:{seed.BankTransactionId:N}"
            });
        Assert.Equal(HttpStatusCode.OK, correctionReplay.StatusCode);
        var replayed = await correctionReplay.Content.ReadFromJsonAsync<BankReconciliationDetailResponse>();
        Assert.Equal(corrected.Journals.Select(x => x.LedgerEntryId), replayed!.Journals.Select(x => x.LedgerEntryId));

        var originalAfter = await _factory.ExecuteDbContextAsync(async db =>
        {
            var entry = await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == suspenseTransaction!.CashLedgerEntryId);
            var lines = await db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.LedgerEntryId == entry.Id)
                .OrderBy(x => x.FinanceAccountId).Select(x => new { x.FinanceAccountId, x.DebitAmount, x.CreditAmount }).ToListAsync();
            return new { entry.EntryNumber, entry.Status, Lines = lines };
        });
        Assert.Equal(originalBefore.EntryNumber, originalAfter.EntryNumber);
        Assert.Equal(originalBefore.Status, originalAfter.Status);
        Assert.Equal(originalBefore.Lines, originalAfter.Lines);
    }

    private async Task<BankTransactionListSeed> SeedFinanceBankTransactionsAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"bank-transactions-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Owner";
        var bankAccountId = Guid.Empty;
        var otherCompanyTransactionId = Guid.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(ownerUserId, email, displayName, "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Bank Transaction Company"),
                new Company(otherCompanyId, "Other Bank Transaction Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            FinanceSeedData.AddMockFinanceData(dbContext, companyId, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

            bankAccountId = dbContext.CompanyBankAccounts.Local.First(x => x.CompanyId == companyId && x.IsPrimary).Id;
            otherCompanyTransactionId = dbContext.BankTransactions.Local.First(x => x.CompanyId == otherCompanyId).Id;
            return Task.CompletedTask;
        });

        return new BankTransactionListSeed(companyId, subject, email, displayName, bankAccountId, otherCompanyTransactionId);
    }

    private async Task<BankTransactionReconcileSeed> SeedReconciliationScenarioAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var subject = $"bank-reconcile-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Owner";
        var bankTransactionId = Guid.Empty;
        var firstPaymentId = Guid.Empty;
        var secondPaymentId = Guid.Empty;
        var pendingPaymentId = Guid.Empty;
        var wrongDirectionPaymentId = Guid.Empty;
        var wrongCurrencyPaymentId = Guid.Empty;
        var cashAccountId = Guid.Empty;
        var receivablesAccountId = Guid.Empty;
        var suspenseAccountId = Guid.Empty;
        var categoryAccountId = Guid.Empty;
        var exchangeGainAccountId = Guid.Empty;
        var fiscalPeriodId = Guid.Empty;
        var bankAccountId = Guid.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(ownerUserId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Bank Reconcile Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            var configuredAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cashAccount = new FinanceAccount(
                Guid.NewGuid(), companyId, "1000", "Operating Cash", "asset", "USD", 0m, configuredAtUtc,
                accountClass: FinanceAccountClassValues.Asset,
                normalBalance: FinanceNormalBalanceValues.Debit,
                effectiveFrom: new DateOnly(2026, 1, 1),
                isPostingEnabled: true,
                controlAccountRole: AccountingAccountRoleKeys.Bank);
            var receivablesAccount = new FinanceAccount(
                Guid.NewGuid(), companyId, "1100", "Receivables", "asset", "USD", 0m, configuredAtUtc,
                accountClass: FinanceAccountClassValues.Asset,
                normalBalance: FinanceNormalBalanceValues.Debit,
                effectiveFrom: new DateOnly(2026, 1, 1),
                isPostingEnabled: true,
                controlAccountRole: AccountingAccountRoleKeys.AccountsReceivable);
            var suspenseAccount = new FinanceAccount(
                Guid.NewGuid(), companyId, "1900", "Bank suspense", "asset", "USD", 0m, configuredAtUtc,
                accountClass: FinanceAccountClassValues.Asset, normalBalance: FinanceNormalBalanceValues.Debit,
                effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true,
                controlAccountRole: AccountingAccountRoleKeys.Suspense, restrictManualPosting: true);
            var categoryAccount = new FinanceAccount(
                Guid.NewGuid(), companyId, "4100", "Other operating income", "revenue", "USD", 0m, configuredAtUtc,
                accountClass: FinanceAccountClassValues.Income, normalBalance: FinanceNormalBalanceValues.Credit,
                effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);
            var exchangeGainAccount = new FinanceAccount(
                Guid.NewGuid(), companyId, "4110", "Exchange gains", "revenue", "USD", 0m, configuredAtUtc,
                accountClass: FinanceAccountClassValues.Income, normalBalance: FinanceNormalBalanceValues.Credit,
                effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true,
                controlAccountRole: AccountingAccountRoleKeys.ExchangeGain);
            cashAccountId = cashAccount.Id;
            receivablesAccountId = receivablesAccount.Id;
            suspenseAccountId = suspenseAccount.Id;
            categoryAccountId = categoryAccount.Id;
            exchangeGainAccountId = exchangeGainAccount.Id;
            dbContext.FinanceAccounts.AddRange(cashAccount, receivablesAccount, suspenseAccount, categoryAccount, exchangeGainAccount);

            var configuration = new AccountingConfiguration(
                Guid.NewGuid(), companyId, "USD", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey,
                AccountingPolicyPackDefaults.CountryNeutralBankingVersion,
                new DateOnly(2026, 1, 1), 2,
                AccountingRoundingModeValues.AwayFromZero,
                ownerUserId, configuredAtUtc);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, ownerUserId, configuredAtUtc);
            dbContext.AccountingConfigurations.Add(configuration);
            dbContext.AccountingConfigurationAccountRoles.AddRange(
                new AccountingConfigurationAccountRole(Guid.NewGuid(), companyId, configuration.Id, AccountingAccountRoleKeys.Bank, cashAccount.Id, configuredAtUtc),
                new AccountingConfigurationAccountRole(Guid.NewGuid(), companyId, configuration.Id, AccountingAccountRoleKeys.AccountsReceivable, receivablesAccount.Id, configuredAtUtc),
                new AccountingConfigurationAccountRole(Guid.NewGuid(), companyId, configuration.Id, AccountingAccountRoleKeys.Suspense, suspenseAccount.Id, configuredAtUtc),
                new AccountingConfigurationAccountRole(Guid.NewGuid(), companyId, configuration.Id, AccountingAccountRoleKeys.ExchangeGain, exchangeGainAccount.Id, configuredAtUtc));
            dbContext.VoucherSeries.AddRange(
                new VoucherSeries(Guid.NewGuid(), companyId, "B", "Bank", "B", true, configuredAtUtc),
                new VoucherSeries(Guid.NewGuid(), companyId, "CR", "Corrections", "CR", true, configuredAtUtc));

            var fiscalPeriod = new FiscalPeriod(
                Guid.NewGuid(),
                companyId,
                "FY 2026",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            dbContext.FiscalPeriods.Add(fiscalPeriod);
            fiscalPeriodId = fiscalPeriod.Id;

            var bankAccount = new CompanyBankAccount(
                Guid.NewGuid(),
                companyId,
                cashAccount.Id,
                "Operating Account",
                "Northwind Bank",
                "**** 7781",
                "USD",
                "operating",
                true,
                true,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            dbContext.CompanyBankAccounts.Add(bankAccount);
            bankAccountId = bankAccount.Id;

            var firstPayment = new Payment(
                Guid.NewGuid(),
                companyId,
                PaymentTypes.Incoming,
                100m,
                "USD",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                "completed",
                "INV-001");
            var secondPayment = new Payment(
                Guid.NewGuid(),
                companyId,
                PaymentTypes.Incoming,
                50m,
                "USD",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                PaymentStatuses.Completed,
                "INV-002");
            var pendingPayment = new Payment(
                Guid.NewGuid(),
                companyId,
                PaymentTypes.Incoming,
                60m,
                "USD",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                PaymentStatuses.Pending,
                "INV-002");
            var wrongDirectionPayment = new Payment(
                Guid.NewGuid(), companyId, PaymentTypes.Outgoing, 25m, "USD",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), "bank_transfer", PaymentStatuses.Completed, "BILL-WRONG-DIRECTION");
            var wrongCurrencyPayment = new Payment(
                Guid.NewGuid(), companyId, PaymentTypes.Incoming, 25m, "EUR",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), "bank_transfer", PaymentStatuses.Completed, "INV-WRONG-CURRENCY");
            dbContext.Payments.AddRange(firstPayment, secondPayment, pendingPayment, wrongDirectionPayment, wrongCurrencyPayment);

            var bankTransaction = new BankTransaction(
                Guid.NewGuid(),
                companyId,
                bankAccount.Id,
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                150m,
                "USD",
                "Batch customer remittance",
                "Northwind Analytics");
            dbContext.BankTransactions.Add(bankTransaction);

            bankTransactionId = bankTransaction.Id;
            firstPaymentId = firstPayment.Id;
            secondPaymentId = secondPayment.Id;
            pendingPaymentId = pendingPayment.Id;
            wrongDirectionPaymentId = wrongDirectionPayment.Id;
            wrongCurrencyPaymentId = wrongCurrencyPayment.Id;
            return Task.CompletedTask;
        });

        return new BankTransactionReconcileSeed(
            companyId,
            subject,
            email,
            displayName,
            bankTransactionId,
            cashAccountId,
            receivablesAccountId,
            suspenseAccountId,
            categoryAccountId,
            exchangeGainAccountId,
            fiscalPeriodId,
            bankAccountId,
            firstPaymentId, secondPaymentId, pendingPaymentId, wrongDirectionPaymentId, wrongCurrencyPaymentId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record BankTransactionListSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid BankAccountId,
        Guid OtherCompanyTransactionId);

    private sealed record BankTransactionReconcileSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid BankTransactionId,
        Guid CashAccountId,
        Guid ReceivablesAccountId,
        Guid SuspenseAccountId,
        Guid CategoryAccountId,
        Guid ExchangeGainAccountId,
        Guid FiscalPeriodId,
        Guid BankAccountId,
        Guid FirstPaymentId,
        Guid SecondPaymentId,
        Guid PendingPaymentId,
        Guid WrongDirectionPaymentId,
        Guid WrongCurrencyPaymentId);

    private class BankTransactionResponse
    {
        public Guid Id { get; set; }
        public Guid BankAccountId { get; set; }
        public DateTime BookingDate { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    private sealed class BankTransactionDetailResponse : BankTransactionResponse
    {
        public decimal ReconciledAmount { get; set; }
        public Guid? CashLedgerEntryId { get; set; }
        public List<BankTransactionPaymentLinkResponse> LinkedPayments { get; set; } = [];
        public CompanyBankAccountResponse? BankAccount { get; set; }
    }

    private sealed class BankTransactionPaymentLinkResponse
    {
        public Guid PaymentId { get; set; }
        public decimal AllocatedAmount { get; set; }
    }

    private sealed class CompanyBankAccountResponse
    {
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class ReconcileBankTransactionRequest
    {
        public ReconcileBankTransactionRequest()
        {
        }

        public ReconcileBankTransactionRequest(List<ReconcileBankTransactionPaymentRequest> payments) => Payments = payments;

        public List<ReconcileBankTransactionPaymentRequest> Payments { get; init; } = [];
        public long ExpectedSourceVersion { get; init; } = 1;
        public string HandlingMode { get; init; } = "payment";
        public string? ReviewReason { get; init; }
        public Guid? CategorizationFinanceAccountId { get; init; }
        public List<BankReconciliationAdjustmentRequest> Adjustments { get; init; } = [];
    }

    private sealed class ReconcileBankTransactionPaymentRequest
    {
        public ReconcileBankTransactionPaymentRequest()
        {
        }

        public ReconcileBankTransactionPaymentRequest(Guid paymentId, decimal allocatedAmount)
        {
            PaymentId = paymentId;
            AllocatedAmount = allocatedAmount;
        }

        public Guid PaymentId { get; init; }
        public decimal AllocatedAmount { get; init; }
    }

    private sealed class BankReconciliationAdjustmentRequest
    {
        public string Kind { get; init; } = string.Empty;
        public decimal DebitAmount { get; init; }
        public decimal CreditAmount { get; init; }
        public string Explanation { get; init; } = string.Empty;
    }

    private sealed class BankStatementImportResponse
    {
        public int ImportedCount { get; set; }
        public int DuplicateCount { get; set; }
        public int ConflictCount { get; set; }
        public bool IsIdempotentReplay { get; set; }
        public List<string> ConflictRowIdentities { get; set; } = [];
    }

    private sealed class BankReconciliationDetailResponse
    {
        public string State { get; set; } = string.Empty;
        public List<BankReconciliationJournalLinkResponse> Journals { get; set; } = [];
        public BankReconciliationFollowUpResponse? FollowUp { get; set; }
    }

    private sealed class BankReconciliationJournalLinkResponse
    {
        public Guid LedgerEntryId { get; set; }
        public bool IsOriginalSuspense { get; set; }
        public bool IsCorrection { get; set; }
    }

    private sealed class BankReconciliationFollowUpResponse
    {
        public string Status { get; set; } = string.Empty;
    }
}
