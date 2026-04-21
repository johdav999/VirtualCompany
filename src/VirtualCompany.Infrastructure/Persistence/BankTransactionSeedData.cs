using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal static class BankTransactionSeedData
{
    public static void AddMockBankingData(VirtualCompanyDbContext dbContext, Guid companyId, DateTime anchorUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        var normalizedAnchorUtc = anchorUtc.Kind == DateTimeKind.Utc
            ? anchorUtc
            : anchorUtc.ToUniversalTime();
        if (HasAnyBankTransactions(dbContext, companyId))
        {
            return;
        }

        var accounts = LoadFinanceAccounts(dbContext, companyId)
            .OrderBy(x => x.Code)
            .ToList();
        if (accounts.Count == 0)
        {
            return;
        }

        var bankAccounts = LoadBankAccounts(dbContext, companyId);
        var operatingFinanceAccount = accounts.FirstOrDefault(x => x.Code == "1000") ?? accounts[0];
        var reserveFinanceAccount = accounts
            .FirstOrDefault(x =>
                x.Id != operatingFinanceAccount.Id &&
                string.Equals(x.AccountType, "asset", StringComparison.OrdinalIgnoreCase))
            ?? operatingFinanceAccount;

        CompanyBankAccount operatingBankAccount;
        CompanyBankAccount reserveBankAccount;

        if (bankAccounts.Count > 0)
        {
            operatingBankAccount = bankAccounts
                .OrderByDescending(x => x.IsPrimary)
                .ThenByDescending(x => x.IsActive)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First();
            reserveBankAccount = bankAccounts
                .Where(x => x.Id != operatingBankAccount.Id)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
                ?? operatingBankAccount;
        }
        else
        {
            operatingBankAccount = new CompanyBankAccount(
                StableId(companyId, "company-bank-account:operating"),
                companyId,
                operatingFinanceAccount.Id,
                "Operating Account",
                "Northwind Bank",
                "**** 7781",
                operatingFinanceAccount.Currency,
                "operating",
                true,
                true,
                normalizedAnchorUtc.AddDays(-120),
                normalizedAnchorUtc.AddDays(-120));

            reserveBankAccount = new CompanyBankAccount(
                StableId(companyId, "company-bank-account:reserve"),
                companyId,
                reserveFinanceAccount.Id,
                "Reserve Account",
                "Northwind Bank",
                "**** 1198",
                reserveFinanceAccount.Currency,
                "reserve",
                false,
                true,
                normalizedAnchorUtc.AddDays(-120),
                normalizedAnchorUtc.AddDays(-120));

            dbContext.CompanyBankAccounts.AddRange(operatingBankAccount, reserveBankAccount);
            bankAccounts.Add(operatingBankAccount);
            if (reserveBankAccount.Id != operatingBankAccount.Id)
            {
                bankAccounts.Add(reserveBankAccount);
            }
        }

        var invoicesByReference = LoadInvoices(dbContext, companyId)
            .ToDictionary(x => x.InvoiceNumber, x => x, StringComparer.OrdinalIgnoreCase);
        var billsByReference = LoadBills(dbContext, companyId)
            .ToDictionary(x => x.BillNumber, x => x, StringComparer.OrdinalIgnoreCase);
        var payments = LoadPayments(dbContext, companyId)
            .OrderBy(x => x.PaymentDate)
            .ThenBy(x => x.CounterpartyReference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var payment in payments)
        {
            var amount = string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
                ? payment.Amount
                : -payment.Amount;
            var counterparty = ResolveCounterparty(payment, invoicesByReference, billsByReference);
            var transactionBankAccount = bankAccounts
                .FirstOrDefault(x => string.Equals(x.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
                ?? operatingBankAccount;
            var reconciledAmount = string.Equals(payment.Status, "completed", StringComparison.OrdinalIgnoreCase)
                ? payment.Amount
                : 0m;

            var transaction = new BankTransaction(
                StableId(companyId, $"bank-transaction:payment:{payment.Id:N}"),
                companyId,
                transactionBankAccount.Id,
                payment.PaymentDate,
                payment.PaymentDate,
                amount,
                payment.Currency,
                payment.CounterpartyReference,
                counterparty,
                $"bank-payment:{payment.Id:N}",
                "mock_import",
                reconciledAmount,
                payment.PaymentDate,
                payment.PaymentDate);

            dbContext.BankTransactions.Add(transaction);

            if (reconciledAmount > 0m)
            {
                if (!dbContext.BankTransactionPaymentLinks.Local.Any(x => x.CompanyId == companyId && x.BankTransactionId == transaction.Id && x.PaymentId == payment.Id) &&
                    !dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters().Any(x => x.CompanyId == companyId && x.BankTransactionId == transaction.Id && x.PaymentId == payment.Id))
                {
                dbContext.BankTransactionPaymentLinks.Add(new BankTransactionPaymentLink(
                    StableId(companyId, $"bank-transaction-link:{payment.Id:N}"),
                    companyId,
                    transaction.Id,
                        payment.Id,
                    payment.Amount,
                    payment.Currency,
                    payment.PaymentDate));
                }
            }
        }

        var manualTransactions = new[]
        {
            CreateManual(
                companyId,
                operatingBankAccount.Id,
                normalizedAnchorUtc.AddDays(-8),
                -8420.00m,
                "Payroll April",
                operatingBankAccount.Currency,
                "Nordic Payroll Services",
                "bank-manual:payroll"),
            CreateManual(
                companyId,
                operatingBankAccount.Id,
                normalizedAnchorUtc.AddDays(-6),
                -399.00m,
                "SaaS subscription",
                operatingBankAccount.Currency,
                "Fabrikam Cloud",
                "bank-manual:subscription"),
            CreateManual(
                companyId,
                operatingBankAccount.Id,
                normalizedAnchorUtc.AddDays(-4),
                -24.00m,
                "Monthly bank fee",
                operatingBankAccount.Currency,
                "Northwind Bank",
                "bank-manual:bank-fee")
        }.ToList();

        if (reserveBankAccount.Id != operatingBankAccount.Id)
        {
            manualTransactions.Add(
                CreateManual(
                    companyId,
                    operatingBankAccount.Id,
                    normalizedAnchorUtc.AddDays(-3),
                    -2500.00m,
                    "Transfer to reserve",
                    operatingBankAccount.Currency,
                    "Reserve Account",
                    "bank-manual:transfer-out"));
            manualTransactions.Add(
                CreateManual(
                    companyId,
                    reserveBankAccount.Id,
                    normalizedAnchorUtc.AddDays(-3),
                    2500.00m,
                    "Transfer from operating",
                    reserveBankAccount.Currency,
                    "Operating Account",
                    "bank-manual:transfer-in"));
        }

        manualTransactions.Add(
            CreateManual(
                companyId,
                reserveBankAccount.Id,
                normalizedAnchorUtc.AddDays(-2),
                210.25m,
                "Software refund",
                reserveBankAccount.Currency,
                "Fabrikam Cloud",
                "bank-manual:refund"));

        dbContext.BankTransactions.AddRange(manualTransactions);
    }

    private static bool HasAnyBankTransactions(VirtualCompanyDbContext dbContext, Guid companyId) =>
        dbContext.BankTransactions.Local.Any(x => x.CompanyId == companyId) ||
        dbContext.BankTransactions.IgnoreQueryFilters().Any(x => x.CompanyId == companyId);

    private static List<FinanceAccount> LoadFinanceAccounts(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var local = dbContext.FinanceAccounts.Local
            .Where(x => x.CompanyId == companyId)
            .ToList();
        var localIds = local.Select(x => x.Id).ToHashSet();
        var persisted = dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .AsEnumerable()
            .Where(x => localIds.Add(x.Id))
            .ToList();

        local.AddRange(persisted);
        return local;
    }

    private static List<CompanyBankAccount> LoadBankAccounts(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var local = dbContext.CompanyBankAccounts.Local
            .Where(x => x.CompanyId == companyId)
            .ToList();
        var localIds = local.Select(x => x.Id).ToHashSet();
        var persisted = dbContext.CompanyBankAccounts
            .IgnoreQueryFilters()
            .Include(x => x.FinanceAccount)
            .Where(x => x.CompanyId == companyId)
            .AsEnumerable()
            .Where(x => localIds.Add(x.Id))
            .ToList();

        local.AddRange(persisted);
        return local;
    }

    private static List<FinanceInvoice> LoadInvoices(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var local = dbContext.FinanceInvoices.Local
            .Where(x => x.CompanyId == companyId)
            .ToList();
        var localIds = local.Select(x => x.Id).ToHashSet();
        var persisted = dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .Where(x => x.CompanyId == companyId)
            .AsEnumerable()
            .Where(x => localIds.Add(x.Id))
            .ToList();

        local.AddRange(persisted);
        return local;
    }

    private static List<FinanceBill> LoadBills(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var local = dbContext.FinanceBills.Local
            .Where(x => x.CompanyId == companyId)
            .ToList();
        var localIds = local.Select(x => x.Id).ToHashSet();
        var persisted = dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .Where(x => x.CompanyId == companyId)
            .AsEnumerable()
            .Where(x => localIds.Add(x.Id))
            .ToList();

        local.AddRange(persisted);
        return local;
    }

    private static List<Payment> LoadPayments(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var local = dbContext.Payments.Local
            .Where(x => x.CompanyId == companyId)
            .ToList();
        var localIds = local.Select(x => x.Id).ToHashSet();
        var persisted = dbContext.Payments
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .AsEnumerable()
            .Where(x => localIds.Add(x.Id))
            .ToList();

        local.AddRange(persisted);
        return local;
    }

    private static BankTransaction CreateManual(
        Guid companyId,
        Guid bankAccountId,
        DateTime occurredUtc,
        decimal amount,
        string referenceText,
        string currency,
        string counterparty,
        string externalReference) =>
        new(
            StableId(companyId, externalReference),
            companyId,
            bankAccountId,
            occurredUtc,
            occurredUtc,
            amount,
            currency,
            referenceText,
            counterparty,
            externalReference,
            "mock_import",
            0m,
            occurredUtc,
            occurredUtc);

    private static string ResolveCounterparty(
        Payment payment,
        IReadOnlyDictionary<string, FinanceInvoice> invoicesByReference,
        IReadOnlyDictionary<string, FinanceBill> billsByReference)
    {
        if (string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase) &&
            invoicesByReference.TryGetValue(payment.CounterpartyReference, out var invoice))
        {
            return invoice.Counterparty.Name;
        }

        if (string.Equals(payment.PaymentType, PaymentTypes.Outgoing, StringComparison.OrdinalIgnoreCase) &&
            billsByReference.TryGetValue(payment.CounterpartyReference, out var bill))
        {
            return bill.Counterparty.Name;
        }

        return payment.CounterpartyReference;
    }

    private static Guid StableId(Guid companyId, string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"{companyId:N}:{name}"));

        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash);
    }
}