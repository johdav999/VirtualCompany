using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

public static class FinanceSeedDatasetConsistencyValidator
{
    public static IReadOnlyList<FinanceSeedValidationError> Validate(FinanceSeedDatasetValidationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new List<FinanceSeedValidationError>();
        var accountIds = input.Accounts.Select(x => x.Id).ToHashSet();
        var counterpartyIds = input.Counterparties.Select(x => x.Id).ToHashSet();
        var supplierIds = input.Counterparties
            .Where(x => x.CounterpartyType is "supplier" or "vendor")
            .Select(x => x.Id)
            .ToHashSet();
        var documentIds = input.Documents.Select(x => x.Id).ToHashSet();
        var invoiceIds = input.Invoices.Select(x => x.Id).ToHashSet();
        var billIds = input.Bills.Select(x => x.Id).ToHashSet();
        var categoryIds = input.CategoryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        ValidateMinimumDatasetShape(input, errors);
        ValidateCompanyOwnership(input, errors);
        ValidateReferences(input, errors, accountIds, counterpartyIds, supplierIds, documentIds, invoiceIds, billIds, categoryIds);
        ValidateDates(input, errors);
        ValidateDocumentTotals(input, errors);
        ValidateLedgerBalances(input, errors);
        ValidateRecurringExpansion(input, errors);

        return errors;
    }

    private static void ValidateMinimumDatasetShape(FinanceSeedDatasetValidationInput input, List<FinanceSeedValidationError> errors)
    {
        var historyDays = (input.WindowEndUtc.Date - input.WindowStartUtc.Date).Days + 1;
        AddIf(errors, historyDays is < 60 or > 90, "history.window", "Seed history must cover between 60 and 90 days.");
        AddIf(errors, input.RequireTransactions && input.Transactions.Count == 0, "transactions.required", "Seed history must include transactions.");
        AddIf(errors, input.Invoices.Count == 0, "invoices.required", "Seed history must include invoices.");
        AddIf(errors, input.Bills.Count == 0, "bills.required", "Seed history must include bills.");
        AddIf(errors, input.Counterparties.All(x => x.CounterpartyType is not "supplier" and not "vendor"), "suppliers.required", "Seed history must include suppliers.");
        AddIf(errors, input.RecurringExpenses.Count == 0, "recurring.required", "Seed history must include recurring expenses.");
    }

    private static void ValidateCompanyOwnership(FinanceSeedDatasetValidationInput input, List<FinanceSeedValidationError> errors)
    {
        AddIf(errors, input.Accounts.Any(x => x.CompanyId != input.CompanyId), "accounts.company", "Account company ids must match the seed company.");
        AddIf(errors, input.Counterparties.Any(x => x.CompanyId != input.CompanyId), "counterparties.company", "Counterparty company ids must match the seed company.");
        AddIf(errors, input.Documents.Any(x => x.CompanyId != input.CompanyId), "documents.company", "Document company ids must match the seed company.");
        AddIf(errors, input.Invoices.Any(x => x.CompanyId != input.CompanyId), "invoices.company", "Invoice company ids must match the seed company.");
        AddIf(errors, input.Bills.Any(x => x.CompanyId != input.CompanyId), "bills.company", "Bill company ids must match the seed company.");
        AddIf(errors, input.RecurringExpenses.Any(x => x.CompanyId != input.CompanyId), "recurring.company", "Recurring expense company ids must match the seed company.");
        AddIf(errors, input.Transactions.Any(x => x.CompanyId != input.CompanyId), "transactions.company", "Transaction company ids must match the seed company.");
        AddIf(errors, input.Balances.Any(x => x.CompanyId != input.CompanyId), "balances.company", "Balance company ids must match the seed company.");
    }

    private static void ValidateReferences(
        FinanceSeedDatasetValidationInput input,
        List<FinanceSeedValidationError> errors,
        HashSet<Guid> accountIds,
        HashSet<Guid> counterpartyIds,
        HashSet<Guid> supplierIds,
        HashSet<Guid> documentIds,
        HashSet<Guid> invoiceIds,
        HashSet<Guid> billIds,
        HashSet<string> categoryIds)
    {
        AddIf(errors, input.Invoices.Any(x => !counterpartyIds.Contains(x.CounterpartyId)), "invoices.counterparty", "Invoices must reference valid counterparties.");
        AddIf(errors, input.Bills.Any(x => !counterpartyIds.Contains(x.CounterpartyId)), "bills.counterparty", "Bills must reference valid counterparties.");
        AddIf(errors, input.Invoices.Any(x => x.DocumentId.HasValue && !documentIds.Contains(x.DocumentId.Value)), "invoices.document", "Invoices must reference valid documents.");
        AddIf(errors, input.Bills.Any(x => x.DocumentId.HasValue && !documentIds.Contains(x.DocumentId.Value)), "bills.document", "Bills must reference valid documents.");
        AddIf(errors, input.RecurringExpenses.Any(x => !supplierIds.Contains(x.SupplierId)), "recurring.supplier", "Recurring expenses must reference valid suppliers.");
        AddIf(errors, input.RecurringExpenses.Any(x => !categoryIds.Contains(x.CategoryId)), "recurring.category", "Recurring expenses must reference valid categories.");
        AddIf(errors, input.Transactions.Any(x => !accountIds.Contains(x.AccountId)), "transactions.account", "Transactions must reference valid accounts.");
        AddIf(errors, input.Transactions.Any(x => x.CounterpartyId.HasValue && !counterpartyIds.Contains(x.CounterpartyId.Value)), "transactions.counterparty", "Transactions must reference valid counterparties.");
        AddIf(errors, input.Transactions.Any(x => x.InvoiceId.HasValue && !invoiceIds.Contains(x.InvoiceId.Value)), "transactions.invoice", "Transactions must reference valid invoices.");
        AddIf(errors, input.Transactions.Any(x => x.BillId.HasValue && !billIds.Contains(x.BillId.Value)), "transactions.bill", "Transactions must reference valid bills.");
        AddIf(errors, input.Transactions.Any(x => x.DocumentId.HasValue && !documentIds.Contains(x.DocumentId.Value)), "transactions.document", "Transactions must reference valid documents.");
        AddIf(errors, input.Transactions.Any(x => !categoryIds.Contains(x.TransactionType)), "transactions.category", "Transactions must reference valid categories.");
        AddIf(errors, input.Balances.Any(x => !accountIds.Contains(x.AccountId)), "balances.account", "Balances must reference valid accounts.");
    }

    private static void ValidateDates(FinanceSeedDatasetValidationInput input, List<FinanceSeedValidationError> errors)
    {
        AddIf(errors, input.WindowEndUtc < input.WindowStartUtc, "history.window-order", "Seed history window end must be on or after its start.");
        AddIf(errors, input.Invoices.Any(x => x.DueUtc < x.IssuedUtc), "invoices.dates", "Invoice due dates must be on or after issued dates.");
        AddIf(errors, input.Bills.Any(x => x.DueUtc < x.ReceivedUtc), "bills.dates", "Bill due dates must be on or after received dates.");
        AddIf(errors, input.Invoices.Any(x => x.IssuedUtc < input.WindowStartUtc || x.IssuedUtc > input.WindowEndUtc), "invoices.window", "Invoices must be issued inside the seeded history window.");
        AddIf(errors, input.Bills.Any(x => x.ReceivedUtc < input.WindowStartUtc || x.ReceivedUtc > input.WindowEndUtc), "bills.window", "Bills must be received inside the seeded history window.");
        var outOfWindowTransactions = input.Transactions
            .Where(x => x.TransactionUtc < input.WindowStartUtc || x.TransactionUtc > input.WindowEndUtc)
            .Select(x => $"{x.ExternalReference}@{x.TransactionUtc:O}")
            .Take(3)
            .ToArray();
        AddIf(
            errors,
            outOfWindowTransactions.Length > 0,
            "transactions.window",
            outOfWindowTransactions.Length == 0
                ? "Transactions must be inside the seeded history window."
                : $"Transactions must be inside the seeded history window. Offenders: {string.Join(", ", outOfWindowTransactions)}");

        foreach (var invoice in input.Invoices)
        {
            var linkedTransactions = input.Transactions.Where(x => x.InvoiceId == invoice.Id).ToArray();
            AddIf(errors, linkedTransactions.Any(x => x.TransactionUtc < invoice.IssuedUtc), $"invoice.{invoice.InvoiceNumber}.payment-date", "Invoice payments cannot predate invoice issue dates.");
        }

        foreach (var bill in input.Bills)
        {
            var linkedTransactions = input.Transactions.Where(x => x.BillId == bill.Id).ToArray();
            AddIf(errors, linkedTransactions.Any(x => x.TransactionUtc < bill.ReceivedUtc), $"bill.{bill.BillNumber}.payment-date", "Bill payments cannot predate bill received dates.");
        }
    }

    private static void ValidateDocumentTotals(FinanceSeedDatasetValidationInput input, List<FinanceSeedValidationError> errors)
    {
        foreach (var invoice in input.Invoices)
        {
            var paidAmount = input.Transactions.Where(x => x.InvoiceId == invoice.Id).Sum(x => x.Amount);
            AddIf(errors, paidAmount != invoice.Amount, $"invoice.{invoice.InvoiceNumber}", "Invoice linked transactions must equal the invoice amount.");
        }

        foreach (var bill in input.Bills)
        {
            var paidAmount = input.Transactions.Where(x => x.BillId == bill.Id).Sum(x => Math.Abs(x.Amount));
            AddIf(errors, paidAmount != bill.Amount, $"bill.{bill.BillNumber}", "Bill linked transactions must equal the bill amount.");
        }
    }

    private static void ValidateLedgerBalances(FinanceSeedDatasetValidationInput input, List<FinanceSeedValidationError> errors)
    {
        foreach (var balance in input.Balances)
        {
            var account = input.Accounts.SingleOrDefault(x => x.Id == balance.AccountId);
            if (account is null)
            {
                errors.Add(new FinanceSeedValidationError("balances.account", "Balances must reference valid accounts."));
                continue;
            }

            var expected = account.OpeningBalance + input.Transactions.Where(x => x.AccountId == account.Id).Sum(x => x.Amount);
            AddIf(errors, balance.Amount != expected, $"balance.{account.Code}", "Ledger balance must equal opening balance plus posted transactions.");
            AddIf(errors, balance.Currency != account.Currency, $"balance.{account.Code}.currency", "Balance currency must match its account currency.");
        }
    }

    private static void ValidateRecurringExpansion(FinanceSeedDatasetValidationInput input, List<FinanceSeedValidationError> errors)
    {
        foreach (var recurringExpense in input.RecurringExpenses)
        {
            var expectedOccurrences = recurringExpense.GetOccurrences(input.WindowStartUtc, input.WindowEndUtc).ToArray();
            var actualOccurrences = input.Transactions
                .Where(x => x.CounterpartyId == recurringExpense.SupplierId)
                .Where(x => x.TransactionType == recurringExpense.CategoryId)
                .Where(x => x.Amount == -recurringExpense.Amount)
                .Where(x => x.Currency == recurringExpense.Currency)
                .Select(x => x.TransactionUtc)
                .OrderBy(x => x)
                .ToArray();

            AddIf(errors, !expectedOccurrences.SequenceEqual(actualOccurrences), $"recurring.{recurringExpense.Name}", "Recurring expense transactions must align to their cadence.");
        }
    }

    private static void AddIf(List<FinanceSeedValidationError> errors, bool condition, string code, string message)
    {
        if (condition)
        {
            errors.Add(new FinanceSeedValidationError(code, message));
        }
    }
}

public sealed record FinanceSeedDatasetValidationInput(
    Guid CompanyId,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    bool RequireTransactions,
    IReadOnlyList<string> CategoryIds,
    IReadOnlyList<FinanceAccount> Accounts,
    IReadOnlyList<FinanceCounterparty> Counterparties,
    IReadOnlyList<CompanyKnowledgeDocument> Documents,
    IReadOnlyList<FinanceInvoice> Invoices,
    IReadOnlyList<FinanceBill> Bills,
    IReadOnlyList<FinanceRecurringExpenseSeed> RecurringExpenses,
    IReadOnlyList<FinanceTransaction> Transactions,
    IReadOnlyList<FinanceBalance> Balances);
