using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BankTransactionsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BankTransactionsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

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
        Assert.NotNull(firstResult.CashLedgerEntryId);

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
        Assert.NotNull(retriedResult.CashLedgerEntryId);
        Assert.Equal(firstResult.CashLedgerEntryId, retriedResult.CashLedgerEntryId);

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
        Assert.Equal(firstResult.CashLedgerEntryId, secondResult.CashLedgerEntryId);

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
                .Where(x => x.CompanyId == seed.CompanyId && x.LedgerEntryId == firstResult.CashLedgerEntryId)
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

            return new
            {
                paymentLinks,
                cashLedgerLinks,
                ledgerEntry = ledgerEntries.Single(),
                ledgerEntryCount = ledgerEntries.Count,
                ledgerLines,
                sourceMappings = sourceMappings.Count,
                sourceMapping = sourceMappings.Single()
            };
        });

        Assert.Equal(2, counts.paymentLinks);
        Assert.Equal(1, counts.cashLedgerLinks);
        Assert.Equal(1, counts.ledgerEntryCount);
        Assert.Equal(1, counts.sourceMappings);
        Assert.Equal(seed.CompanyId, counts.ledgerEntry.CompanyId);
        Assert.Equal(LedgerEntryStatuses.Posted, counts.ledgerEntry.Status);
        Assert.Equal(FinanceCashPostingSourceTypes.BankTransaction, counts.ledgerEntry.SourceType);
        Assert.Equal(seed.BankTransactionId.ToString("D"), counts.ledgerEntry.SourceId);
        Assert.Equal(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), counts.ledgerEntry.PostedAtUtc);
        Assert.Equal(firstResult.CashLedgerEntryId, counts.ledgerEntry.Id);
        Assert.Equal(2, counts.ledgerLines.Count);
        Assert.Equal(seed.CashAccountId, counts.ledgerLines[0].FinanceAccountId);
        Assert.Equal(150m, counts.ledgerLines[0].DebitAmount);
        Assert.Equal(0m, counts.ledgerLines[0].CreditAmount);
        Assert.Equal(seed.ReceivablesAccountId, counts.ledgerLines[1].FinanceAccountId);
        Assert.Equal(0m, counts.ledgerLines[1].DebitAmount);
        Assert.Equal(150m, counts.ledgerLines[1].CreditAmount);
        Assert.Equal(counts.ledgerLines.Sum(x => x.DebitAmount), counts.ledgerLines.Sum(x => x.CreditAmount));
        Assert.Equal(firstResult.CashLedgerEntryId, counts.sourceMapping.LedgerEntryId);
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

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(ownerUserId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Bank Reconcile Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            var cashAccount = new FinanceAccount(Guid.NewGuid(), companyId, "1000", "Operating Cash", "asset", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var receivablesAccount = new FinanceAccount(Guid.NewGuid(), companyId, "1100", "Receivables", "asset", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            dbContext.FinanceAccounts.AddRange(cashAccount, receivablesAccount);

            var fiscalPeriod = new FiscalPeriod(
                Guid.NewGuid(),
                companyId,
                "FY 2026",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            dbContext.FiscalPeriods.Add(fiscalPeriod);

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
            dbContext.Payments.AddRange(firstPayment, secondPayment, pendingPayment);

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
            return Task.CompletedTask;
        });

        return new BankTransactionReconcileSeed(
            companyId,
            subject,
            email,
            displayName,
            bankTransactionId,
            cashAccount.Id,
            receivablesAccount.Id,
            firstPaymentId, secondPaymentId, pendingPaymentId);
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
        Guid FirstPaymentId,
        Guid SecondPaymentId,
        Guid PendingPaymentId);

    private sealed class BankTransactionResponse
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

    private sealed record ReconcileBankTransactionRequest(
        List<ReconcileBankTransactionPaymentRequest> Payments);

    private sealed record ReconcileBankTransactionPaymentRequest(Guid PaymentId, decimal AllocatedAmount);
}