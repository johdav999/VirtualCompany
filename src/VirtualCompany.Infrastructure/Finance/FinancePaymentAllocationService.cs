using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class FinancePaymentAllocationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceCashSettlementPostingService? _cashSettlementPostingService;

    public FinancePaymentAllocationService(
        VirtualCompanyDbContext dbContext,
        IFinanceCashSettlementPostingService? cashSettlementPostingService = null)
    {
        _dbContext = dbContext;
        _cashSettlementPostingService = cashSettlementPostingService;
    }
    public Task<FinancePaymentAllocationDto> CreateAsync(
        CreateFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(
            () => CreateWithinAmbientTransactionAsync(command, cancellationToken),
            cancellationToken);

    internal async Task<FinancePaymentAllocationDto> CreateWithinAmbientTransactionAsync(
        CreateFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken)
    {
            ValidateAllocationDto(command.Allocation.PaymentId, command.Allocation.InvoiceId, command.Allocation.BillId, command.Allocation.AllocatedAmount, command.Allocation.Currency);

            var payment = await LoadPaymentAsync(command.CompanyId, command.Allocation.PaymentId, cancellationToken);
            var invoice = await LoadInvoiceAsync(command.CompanyId, command.Allocation.InvoiceId, cancellationToken);
            var bill = await LoadBillAsync(command.CompanyId, command.Allocation.BillId, cancellationToken);
            var amount = NormalizeMoney(command.Allocation.AllocatedAmount);
            var currency = NormalizeCurrency(command.Allocation.Currency);

            await ValidateAsync(command.CompanyId, payment, invoice, bill, amount, currency, null, cancellationToken);

            var allocation = new PaymentAllocation(
                Guid.NewGuid(),
                command.CompanyId,
                payment.Id,
                invoice?.Id,
                bill?.Id,
                amount,
                currency,
                sourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
                paymentSourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
                targetSourceSimulationEventRecordId: invoice?.SourceSimulationEventRecordId ?? bill?.SourceSimulationEventRecordId);

            _dbContext.PaymentAllocations.Add(allocation);
            await ApplyTargetSettlementStatusAsync(command.CompanyId, invoice, bill, amount, null, cancellationToken);
            await TryPostSettlementAsync(command.CompanyId, payment, allocation, amount, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Map(allocation);
    }

    public Task<FinancePaymentAllocationDto> UpdateAsync(
        UpdateFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            if (command.AllocationId == Guid.Empty)
            {
                throw new ArgumentException("Allocation id is required.", nameof(command));
            }

            ValidateAllocationDto(command.Allocation.PaymentId, command.Allocation.InvoiceId, command.Allocation.BillId, command.Allocation.AllocatedAmount, command.Allocation.Currency);

            var allocation = await _dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.AllocationId, cancellationToken);
            if (allocation is null)
            {
                throw new KeyNotFoundException("Finance payment allocation was not found.");
            }

            var previousInvoice = await LoadInvoiceAsync(command.CompanyId, allocation.InvoiceId, cancellationToken);
            var previousBill = await LoadBillAsync(command.CompanyId, allocation.BillId, cancellationToken);
            var payment = await LoadPaymentAsync(command.CompanyId, command.Allocation.PaymentId, cancellationToken);
            var invoice = await LoadInvoiceAsync(command.CompanyId, command.Allocation.InvoiceId, cancellationToken);
            var bill = await LoadBillAsync(command.CompanyId, command.Allocation.BillId, cancellationToken);
            var amount = NormalizeMoney(command.Allocation.AllocatedAmount);
            var currency = NormalizeCurrency(command.Allocation.Currency);

            await ValidateAsync(command.CompanyId, payment, invoice, bill, amount, currency, allocation.Id, cancellationToken);

            allocation.Update(payment.Id, invoice?.Id, bill?.Id, amount, currency);
            allocation.Update(
                payment.Id,
                invoice?.Id,
                bill?.Id,
                amount,
                currency,
                sourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
                paymentSourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
                targetSourceSimulationEventRecordId: invoice?.SourceSimulationEventRecordId ?? bill?.SourceSimulationEventRecordId);

            var movedAwayFromInvoice = previousInvoice is not null && previousInvoice.Id != invoice?.Id;
            var movedAwayFromBill = previousBill is not null && previousBill.Id != bill?.Id;

            if (movedAwayFromInvoice)
            {
                await ApplyInvoiceSettlementStatusAsync(command.CompanyId, previousInvoice!, null, allocation.Id, cancellationToken);
            }

            if (movedAwayFromBill)
            {
                await ApplyBillSettlementStatusAsync(command.CompanyId, previousBill!, null, allocation.Id, cancellationToken);
            }

            await ApplyTargetSettlementStatusAsync(command.CompanyId, invoice, bill, amount, allocation.Id, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Map(allocation);
        }, cancellationToken);

    public Task DeleteAsync(
        DeleteFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            if (command.AllocationId == Guid.Empty)
            {
                throw new ArgumentException("Allocation id is required.", nameof(command));
            }

            var allocation = await _dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.AllocationId, cancellationToken);
            if (allocation is null)
            {
                throw new KeyNotFoundException("Finance payment allocation was not found.");
            }

            var invoice = await LoadInvoiceAsync(command.CompanyId, allocation.InvoiceId, cancellationToken);
            var bill = await LoadBillAsync(command.CompanyId, allocation.BillId, cancellationToken);

            _dbContext.PaymentAllocations.Remove(allocation);

            if (invoice is not null)
            {
                await ApplyInvoiceSettlementStatusAsync(command.CompanyId, invoice, null, allocation.Id, cancellationToken);
            }

            if (bill is not null)
            {
                await ApplyBillSettlementStatusAsync(command.CompanyId, bill, null, allocation.Id, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

    public Task<FinancePaymentAllocationBackfillResultDto> BackfillAsync(
        BackfillFinancePaymentAllocationsCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            var createdAllocationCount = 0;
            var createdPaymentCount = 0;
            var recalculatedInvoiceCount = 0;
            var recalculatedBillCount = 0;

            var invoices = await _dbContext.FinanceInvoices
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId)
                .OrderBy(x => x.IssuedUtc)
                .ThenBy(x => x.InvoiceNumber)
                .ToListAsync(cancellationToken);

            foreach (var invoice in invoices)
            {
                var existingAllocationCount = await _dbContext.PaymentAllocations
                    .IgnoreQueryFilters()
                    .Where(x => x.CompanyId == command.CompanyId && x.InvoiceId == invoice.Id)
                    .CountAsync(cancellationToken);

                if (existingAllocationCount == 0 && ShouldBackfillPaidDocument(invoice.Status, invoice.SettlementStatus))
                {
                    var result = await BackfillInvoiceAsync(command.CompanyId, invoice, command.SynthesizeMissingPayments, cancellationToken);
                    createdAllocationCount += result.CreatedAllocationCount;
                    createdPaymentCount += result.CreatedPaymentCount;
                }

                await ApplyInvoiceSettlementStatusAsync(command.CompanyId, invoice, null, null, cancellationToken);
                recalculatedInvoiceCount++;
            }

            var bills = await _dbContext.FinanceBills
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId)
                .OrderBy(x => x.ReceivedUtc)
                .ThenBy(x => x.BillNumber)
                .ToListAsync(cancellationToken);

            foreach (var bill in bills)
            {
                var existingAllocationCount = await _dbContext.PaymentAllocations
                    .IgnoreQueryFilters()
                    .Where(x => x.CompanyId == command.CompanyId && x.BillId == bill.Id)
                    .CountAsync(cancellationToken);

                if (existingAllocationCount == 0 && ShouldBackfillPaidDocument(bill.Status, bill.SettlementStatus))
                {
                    var result = await BackfillBillAsync(command.CompanyId, bill, command.SynthesizeMissingPayments, cancellationToken);
                    createdAllocationCount += result.CreatedAllocationCount;
                    createdPaymentCount += result.CreatedPaymentCount;
                }

                await ApplyBillSettlementStatusAsync(command.CompanyId, bill, null, null, cancellationToken);
                recalculatedBillCount++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new FinancePaymentAllocationBackfillResultDto(
                command.CompanyId,
                createdAllocationCount,
                createdPaymentCount,
                recalculatedInvoiceCount,
                recalculatedBillCount);
        }, cancellationToken);

    private async Task<BackfillDocumentResult> BackfillInvoiceAsync(
        Guid companyId,
        FinanceInvoice invoice,
        bool synthesizeMissingPayments,
        CancellationToken cancellationToken)
    {
        var createdAllocationCount = 0;
        var createdPaymentCount = 0;
        var remainingAmount = invoice.Amount;

        var payments = await FindExistingPaymentsAsync(companyId, PaymentTypes.Incoming, invoice.InvoiceNumber, invoice.Currency, cancellationToken);
        foreach (var payment in payments)
        {
            if (remainingAmount <= 0m)
            {
                break;
            }

            var created = await CreateBackfillAllocationChunkAsync(companyId, payment, invoice, null, remainingAmount, cancellationToken);
            createdAllocationCount += created.CreatedAllocationCount;
            remainingAmount -= created.AllocatedAmount;
        }

        if (remainingAmount > 0m && synthesizeMissingPayments)
        {
            var syntheticPayment = CreateSyntheticPayment(
                companyId,
                PaymentTypes.Incoming,
                remainingAmount,
                invoice.Currency,
                invoice.DueUtc,
                invoice.InvoiceNumber);

            _dbContext.Payments.Add(syntheticPayment);
            await _dbContext.SaveChangesAsync(cancellationToken);
            createdPaymentCount++;

            var created = await CreateBackfillAllocationChunkAsync(companyId, syntheticPayment, invoice, null, remainingAmount, cancellationToken);
            createdAllocationCount += created.CreatedAllocationCount;
        }

        return new BackfillDocumentResult(createdAllocationCount, createdPaymentCount);
    }

    private async Task<BackfillDocumentResult> BackfillBillAsync(
        Guid companyId,
        FinanceBill bill,
        bool synthesizeMissingPayments,
        CancellationToken cancellationToken)
    {
        var createdAllocationCount = 0;
        var createdPaymentCount = 0;
        var remainingAmount = bill.Amount;

        var payments = await FindExistingPaymentsAsync(companyId, PaymentTypes.Outgoing, bill.BillNumber, bill.Currency, cancellationToken);
        foreach (var payment in payments)
        {
            if (remainingAmount <= 0m)
            {
                break;
            }

            var created = await CreateBackfillAllocationChunkAsync(companyId, payment, null, bill, remainingAmount, cancellationToken);
            createdAllocationCount += created.CreatedAllocationCount;
            remainingAmount -= created.AllocatedAmount;
        }

        if (remainingAmount > 0m && synthesizeMissingPayments)
        {
            var syntheticPayment = CreateSyntheticPayment(
                companyId,
                PaymentTypes.Outgoing,
                remainingAmount,
                bill.Currency,
                bill.DueUtc,
                bill.BillNumber);

            _dbContext.Payments.Add(syntheticPayment);
            await _dbContext.SaveChangesAsync(cancellationToken);
            createdPaymentCount++;

            var created = await CreateBackfillAllocationChunkAsync(companyId, syntheticPayment, null, bill, remainingAmount, cancellationToken);
            createdAllocationCount += created.CreatedAllocationCount;
        }

        return new BackfillDocumentResult(createdAllocationCount, createdPaymentCount);
    }

    private async Task<BackfillChunkResult> CreateBackfillAllocationChunkAsync(
        Guid companyId,
        Payment payment,
        FinanceInvoice? invoice,
        FinanceBill? bill,
        decimal targetRemainingAmount,
        CancellationToken cancellationToken)
    {
        var allocatedToPayment = await GetAllocatedToPaymentAsync(companyId, payment.Id, null, cancellationToken);
        var availableOnPayment = Math.Max(0m, payment.Amount - allocatedToPayment);
        var allocationAmount = Math.Min(availableOnPayment, targetRemainingAmount);
        if (allocationAmount <= 0m)
        {
            return new BackfillChunkResult(0, 0m);
        }

        await ValidateAsync(companyId, payment, invoice, bill, allocationAmount, payment.Currency, null, cancellationToken);

        var allocation = new PaymentAllocation(
            Guid.NewGuid(),
            companyId,
            payment.Id,
            invoice?.Id,
            bill?.Id,
            allocationAmount,
            payment.Currency,
            sourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
            paymentSourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
            targetSourceSimulationEventRecordId: invoice?.SourceSimulationEventRecordId ?? bill?.SourceSimulationEventRecordId);

        _dbContext.PaymentAllocations.Add(allocation);
        await ApplyTargetSettlementStatusAsync(companyId, invoice, bill, allocationAmount, null, cancellationToken);
        await TryPostSettlementAsync(companyId, payment, allocation, allocationAmount, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new BackfillChunkResult(1, allocationAmount);
    }

    private async Task ValidateAsync(
        Guid companyId,
        Payment payment,
        FinanceInvoice? invoice,
        FinanceBill? bill,
        decimal amount,
        string currency,
        Guid? allocationIdToExclude,
        CancellationToken cancellationToken)
    {
        if (invoice is null && bill is null)
        {
            throw CreateValidationException("InvoiceId", "Allocation must reference either an invoice or a bill.");
        }

        if (invoice is not null && bill is not null)
        {
            throw CreateValidationException("InvoiceId", "Allocation cannot reference both an invoice and a bill.");
        }

        if (!string.Equals(payment.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            throw CreateValidationException("Currency", $"Allocation currency '{currency}' must match payment currency '{payment.Currency}'.");
        }

        if (invoice is not null)
        {
            if (!string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("PaymentId", "Incoming payments can only be allocated to invoices.");
            }

            if (!string.Equals(invoice.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("Currency", $"Allocation currency '{currency}' must match invoice currency '{invoice.Currency}'.");
            }
        }

        if (bill is not null)
        {
            if (!string.Equals(payment.PaymentType, PaymentTypes.Outgoing, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("PaymentId", "Outgoing payments can only be allocated to bills.");
            }

            if (!string.Equals(bill.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("Currency", $"Allocation currency '{currency}' must match bill currency '{bill.Currency}'.");
            }
        }

        var allocatedToPayment = await GetAllocatedToPaymentAsync(companyId, payment.Id, allocationIdToExclude, cancellationToken);
        var remainingOnPayment = NormalizeMoney(Math.Max(0m, payment.Amount - allocatedToPayment));
        if (amount > remainingOnPayment)
        {
            throw CreateValidationException("AllocatedAmount", $"Payment allocations cannot exceed the remaining unallocated payment amount of {remainingOnPayment:0.00}.");
        }

        if (invoice is not null)
        {
            var allocatedToInvoice = await GetAllocatedToInvoiceAsync(companyId, invoice.Id, allocationIdToExclude, cancellationToken);
            var remainingOpenAmount = NormalizeMoney(Math.Max(0m, invoice.Amount - allocatedToInvoice));
            if (amount > remainingOpenAmount)
            {
                throw CreateValidationException("AllocatedAmount", $"Invoice allocations cannot exceed the remaining open amount of {remainingOpenAmount:0.00}.");
            }
        }

        if (bill is not null)
        {
            var allocatedToBill = await GetAllocatedToBillAsync(companyId, bill.Id, allocationIdToExclude, cancellationToken);
            var remainingOpenAmount = NormalizeMoney(Math.Max(0m, bill.Amount - allocatedToBill));
            if (amount > remainingOpenAmount)
            {
                throw CreateValidationException("AllocatedAmount", $"Bill allocations cannot exceed the remaining open amount of {remainingOpenAmount:0.00}.");
            }
        }
    }

    private async Task ApplyTargetSettlementStatusAsync(
        Guid companyId,
        FinanceInvoice? invoice,
        FinanceBill? bill,
        decimal allocationAmount,
        Guid? allocationIdToExclude,
        CancellationToken cancellationToken)
    {
        if (invoice is not null)
        {
            await ApplyInvoiceSettlementStatusAsync(companyId, invoice, allocationAmount, allocationIdToExclude, cancellationToken);
        }

        if (bill is not null)
        {
            await ApplyBillSettlementStatusAsync(companyId, bill, allocationAmount, allocationIdToExclude, cancellationToken);
        }
    }

    private async Task ApplyInvoiceSettlementStatusAsync(
        Guid companyId,
        FinanceInvoice invoice,
        decimal? pendingAmount,
        Guid? allocationIdToExclude,
        CancellationToken cancellationToken)
    {
        var allocated = await GetAllocatedToInvoiceAsync(companyId, invoice.Id, allocationIdToExclude, cancellationToken) + (pendingAmount ?? 0m);
        invoice.ApplySettlementStatus(ResolveSettlementStatus(invoice.Amount, allocated));
    }

    private async Task ApplyBillSettlementStatusAsync(
        Guid companyId,
        FinanceBill bill,
        decimal? pendingAmount,
        Guid? allocationIdToExclude,
        CancellationToken cancellationToken)
    {
        var allocated = await GetAllocatedToBillAsync(companyId, bill.Id, allocationIdToExclude, cancellationToken) + (pendingAmount ?? 0m);
        bill.ApplySettlementStatus(ResolveSettlementStatus(bill.Amount, allocated));
    }

    private async Task<Payment> LoadPaymentAsync(Guid companyId, Guid paymentId, CancellationToken cancellationToken)
    {
        if (paymentId == Guid.Empty)
        {
            throw CreateValidationException("PaymentId", "Payment id is required.");
        }

        var payment = await _dbContext.Payments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == paymentId, cancellationToken);

        return payment ?? throw new KeyNotFoundException("Finance payment was not found.");
    }

    private async Task<FinanceInvoice?> LoadInvoiceAsync(Guid companyId, Guid? invoiceId, CancellationToken cancellationToken)
    {
        if (!invoiceId.HasValue)
        {
            return null;
        }

        var invoice = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId.Value, cancellationToken);

        return invoice ?? throw new KeyNotFoundException("Finance invoice was not found.");
    }

    private async Task<FinanceBill?> LoadBillAsync(Guid companyId, Guid? billId, CancellationToken cancellationToken)
    {
        if (!billId.HasValue)
        {
            return null;
        }

        var bill = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId.Value, cancellationToken);

        return bill ?? throw new KeyNotFoundException("Finance bill was not found.");
    }

    private async Task<List<Payment>> FindExistingPaymentsAsync(
        Guid companyId,
        string paymentType,
        string reference,
        string currency,
        CancellationToken cancellationToken) =>
        await _dbContext.Payments
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.PaymentType == paymentType &&
                x.CounterpartyReference == reference &&
                x.Currency == currency)
            .OrderByDescending(x => x.Status == "completed")
            .ThenBy(x => x.PaymentDate)
            .ThenBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

    private static Payment CreateSyntheticPayment(
        Guid companyId,
        string paymentType,
        decimal amount,
        string currency,
        DateTime paymentDate,
        string reference) =>
        new(
            Guid.NewGuid(),
            companyId,
            paymentType,
            amount,
            currency,
            paymentDate == default ? DateTime.UtcNow : paymentDate,
            "bank_transfer",
            "completed",
            reference);

    private async Task<decimal> GetAllocatedToPaymentAsync(
        Guid companyId,
        Guid paymentId,
        Guid? allocationIdToExclude,
        CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.PaymentId == paymentId &&
                (!allocationIdToExclude.HasValue || x.Id != allocationIdToExclude.Value))
            .SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;

    private async Task<decimal> GetAllocatedToInvoiceAsync(
        Guid companyId,
        Guid invoiceId,
        Guid? allocationIdToExclude,
        CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.InvoiceId == invoiceId &&
                (!allocationIdToExclude.HasValue || x.Id != allocationIdToExclude.Value))
            .SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;

    private async Task<decimal> GetAllocatedToBillAsync(
        Guid companyId,
        Guid billId,
        Guid? allocationIdToExclude,
        CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.BillId == billId &&
                (!allocationIdToExclude.HasValue || x.Id != allocationIdToExclude.Value))
            .SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;

    private static string ResolveSettlementStatus(decimal totalAmount, decimal allocatedAmount)
    {
        var roundedTotal = NormalizeMoney(totalAmount);
        var roundedAllocated = NormalizeMoney(Math.Max(0m, allocatedAmount));

        if (roundedAllocated <= 0m)
        {
            return FinanceSettlementStatuses.Unpaid;
        }

        if (roundedAllocated >= roundedTotal)
        {
            return FinanceSettlementStatuses.Paid;
        }

        return FinanceSettlementStatuses.PartiallyPaid;
    }

    private static void ValidateAllocationDto(
        Guid paymentId,
        Guid? invoiceId,
        Guid? billId,
        decimal allocatedAmount,
        string currency)
    {
        if (paymentId == Guid.Empty)
        {
            throw CreateValidationException("PaymentId", "Payment id is required.");
        }

        if (allocatedAmount <= 0m)
        {
            throw CreateValidationException("AllocatedAmount", "Allocated amount must be greater than zero.");
        }

        if ((invoiceId.HasValue && billId.HasValue) || (!invoiceId.HasValue && !billId.HasValue))
        {
            throw CreateValidationException("InvoiceId", "Specify either InvoiceId or BillId.");
        }

        if (invoiceId == Guid.Empty)
        {
            throw CreateValidationException("InvoiceId", "Invoice id cannot be empty.");
        }

        if (billId == Guid.Empty)
        {
            throw CreateValidationException("BillId", "Bill id cannot be empty.");
        }

        _ = NormalizeCurrency(currency);
    }

    private static string NormalizeCurrency(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw CreateValidationException("Currency", "Currency is required.");
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
        {
            throw CreateValidationException("Currency", "Currency must be a three-letter ISO code.");
        }

        return normalized;
    }

    private async Task TryPostSettlementAsync(
        Guid companyId,
        Payment payment,
        PaymentAllocation allocation,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (_cashSettlementPostingService is null ||
            !string.Equals(payment.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _cashSettlementPostingService.PostCashSettlementAsync(
            new PostCashSettlementCommand(
                companyId,
                FinanceCashPostingSourceTypes.PaymentAllocation,
                allocation.Id.ToString("D"),
                payment.Id,
                amount,
                allocation.CreatedUtc),
            cancellationToken);
    }

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool ShouldBackfillPaidDocument(string status, string settlementStatus) =>
        string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal) ||
        string.Equals(status?.Trim(), "paid", StringComparison.OrdinalIgnoreCase);

    private static FinancePaymentAllocationDto Map(PaymentAllocation allocation) =>
        new(
            allocation.Id,
            allocation.CompanyId,
            allocation.PaymentId,
            allocation.InvoiceId,
            allocation.BillId,
            allocation.AllocatedAmount,
            allocation.Currency,
            allocation.CreatedUtc,
            allocation.UpdatedUtc,
            allocation.SourceSimulationEventRecordId,
            allocation.PaymentSourceSimulationEventRecordId,
            allocation.TargetSourceSimulationEventRecordId);

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await action();
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task ExecuteInTransactionAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            await action();
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await action();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private sealed record BackfillDocumentResult(int CreatedAllocationCount, int CreatedPaymentCount);
    private sealed record BackfillChunkResult(int CreatedAllocationCount, decimal AllocatedAmount);

    private static FinanceValidationException CreateValidationException(string field, string message) =>
        new(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [field] = [message]
            },
            message);
}