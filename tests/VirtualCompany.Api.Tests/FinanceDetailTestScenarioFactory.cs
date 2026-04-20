using System.Net.Http;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

internal static class FinanceDetailTestScenarioFactory
{
    internal static async Task<FinanceDetailTestScenario> CreateAsync(TestWebApplicationFactory factory)
    {
        var ownerUserId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var ownerSubject = $"finance-detail-owner-{Guid.NewGuid():N}";
        var ownerEmail = $"{ownerSubject}@example.com";
        const string ownerDisplayName = "Finance Detail Owner";
        var approverSubject = $"finance-detail-approver-{Guid.NewGuid():N}";
        var approverEmail = $"{approverSubject}@example.com";
        const string approverDisplayName = "Finance Detail Approver";
        var anchorUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        FinanceSeedResult? primarySeed = null;
        FinanceSeedResult? otherSeed = null;
        var accessibleTransactionId = Guid.Empty;
        var restrictedTransactionId = Guid.Empty;
        var missingTransactionId = Guid.Empty;
        var reviewInvoiceId = Guid.Empty;
        var missingInvoiceId = Guid.Empty;
        var restrictedInvoiceId = Guid.Empty;

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, ownerEmail, ownerDisplayName, "dev-header", ownerSubject),
                new User(approverUserId, approverEmail, approverDisplayName, "dev-header", approverSubject));

            dbContext.Companies.AddRange(
                new Company(companyId, "Finance Detail Company"),
                new Company(otherCompanyId, "Other Finance Detail Company"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, approverUserId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));

            primarySeed = FinanceSeedData.AddMockFinanceData(dbContext, companyId, anchorUtc);
            otherSeed = FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId, anchorUtc);

            accessibleTransactionId = primarySeed.TransactionIds[0];
            restrictedTransactionId = primarySeed.TransactionIds[4];
            reviewInvoiceId = primarySeed.InvoiceIds[0];
            restrictedInvoiceId = primarySeed.InvoiceIds[4];

            var templateTransaction = dbContext.FinanceTransactions.Local.First(x => x.CompanyId == companyId);
            var missingTransaction = new FinanceTransaction(
                Guid.NewGuid(),
                companyId,
                templateTransaction.AccountId,
                templateTransaction.CounterpartyId,
                null,
                null,
                templateTransaction.TransactionUtc.AddMinutes(15),
                "software",
                -89.42m,
                templateTransaction.Currency,
                "Missing linked document transaction",
                $"missing-tx-{Guid.NewGuid():N}".Substring(0, 22),
                Guid.NewGuid());
            dbContext.FinanceTransactions.Add(missingTransaction);
            missingTransactionId = missingTransaction.Id;

            var templateInvoice = dbContext.FinanceInvoices.Local.First(x => x.CompanyId == companyId);
            var missingInvoice = new FinanceInvoice(
                Guid.NewGuid(),
                companyId,
                templateInvoice.CounterpartyId,
                $"{templateInvoice.InvoiceNumber}-MISSING",
                templateInvoice.IssuedUtc.AddDays(3),
                templateInvoice.DueUtc.AddDays(3),
                templateInvoice.Amount + 42m,
                templateInvoice.Currency,
                "pending_approval",
                Guid.NewGuid());
            dbContext.FinanceInvoices.Add(missingInvoice);
            missingInvoiceId = missingInvoice.Id;

            dbContext.FinanceSeedAnomalies.Add(new FinanceSeedAnomaly(
                Guid.NewGuid(),
                companyId,
                "missing_receipt",
                "detail_mapping",
                [accessibleTransactionId],
                """{"expectedDetector":"receipt_completeness","expectedSignal":"missing_supporting_document"}"""));

            return Task.CompletedTask;
        });

        return new FinanceDetailTestScenario(
            companyId,
            otherCompanyId,
            ownerSubject,
            ownerEmail,
            ownerDisplayName,
            approverSubject,
            approverEmail,
            approverDisplayName,
            anchorUtc,
            accessibleTransactionId,
            restrictedTransactionId,
            missingTransactionId,
            otherSeed!.TransactionIds[0],
            reviewInvoiceId,
            missingInvoiceId,
            restrictedInvoiceId,
            otherSeed.InvoiceIds[0]);
    }

    internal static HttpClient CreateAuthenticatedClient(
        TestWebApplicationFactory factory,
        string subject,
        string email,
        string displayName)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }
}

internal sealed record FinanceDetailTestScenario(
    Guid CompanyId,
    Guid OtherCompanyId,
    string OwnerSubject,
    string OwnerEmail,
    string OwnerDisplayName,
    string ApproverSubject,
    string ApproverEmail,
    string ApproverDisplayName,
    DateTime AnchorUtc,
    Guid AccessibleTransactionId,
    Guid RestrictedTransactionId,
    Guid MissingTransactionId,
    Guid OtherCompanyTransactionId,
    Guid ReviewInvoiceId,
    Guid MissingInvoiceId,
    Guid RestrictedInvoiceId,
    Guid OtherCompanyInvoiceId);
