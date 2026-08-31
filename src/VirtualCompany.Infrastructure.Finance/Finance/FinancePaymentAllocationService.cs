using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class FinancePaymentAllocationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceCashSettlementPostingService? _cashSettlementPostingService;
    private readonly IExchangeRateService? _exchangeRateService;
    private readonly IAccountingPostingService? _accountingPostingService;
    private readonly ForeignCurrencySettlementTelemetry? _telemetry;
    private readonly TimeProvider _timeProvider;

    public FinancePaymentAllocationService(
        VirtualCompanyDbContext dbContext,
        IFinanceCashSettlementPostingService? cashSettlementPostingService = null,
        IExchangeRateService? exchangeRateService = null,
        IAccountingPostingService? accountingPostingService = null,
        ForeignCurrencySettlementTelemetry? telemetry = null,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _cashSettlementPostingService = cashSettlementPostingService;
        _exchangeRateService = exchangeRateService;
        _accountingPostingService = accountingPostingService;
        _telemetry = telemetry;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            ValidateAllocationDto(command.Allocation.PaymentId, command.Allocation.InvoiceId, command.Allocation.BillId,
                command.Allocation.AllocatedAmount, command.Allocation.Currency, command.Allocation.FeeAmount,
                command.Allocation.WriteOffAmount);

            var payment = await LoadPaymentAsync(command.CompanyId, command.Allocation.PaymentId, cancellationToken);
            var invoice = await LoadInvoiceAsync(command.CompanyId, command.Allocation.InvoiceId, cancellationToken);
            var bill = await LoadBillAsync(command.CompanyId, command.Allocation.BillId, cancellationToken);
            var amount = NormalizeMoney(command.Allocation.AllocatedAmount);
            var currency = NormalizeCurrency(command.Allocation.Currency);
            var idempotencyKey = NormalizeIdempotencyKey(command.Allocation.IdempotencyKey);
            var feeAmount = NormalizeNonNegativeMoney(command.Allocation.FeeAmount, "FeeAmount");
            var writeOffAmount = NormalizeNonNegativeMoney(command.Allocation.WriteOffAmount, "WriteOffAmount");

            if (idempotencyKey is not null)
            {
                var existing = await _dbContext.PaymentAllocations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == idempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    if (existing.PaymentId != payment.Id || existing.InvoiceId != invoice?.Id || existing.BillId != bill?.Id ||
                        existing.AllocatedAmount != amount || existing.FeeAmount != feeAmount ||
                        existing.WriteOffAmount != writeOffAmount ||
                        !string.Equals(existing.Currency, currency, StringComparison.Ordinal))
                    {
                        throw CreateValidationException("IdempotencyKey", "The idempotency key was already used for a different payment allocation.");
                    }

                    return Map(existing, true);
                }
            }

            await ValidateAsync(command.CompanyId, payment, invoice, bill, amount, feeAmount, writeOffAmount,
                currency, null, cancellationToken);

            var allocationId = Guid.NewGuid();
            var settlement = await PrepareSettlementAsync(command, allocationId, payment, invoice, bill,
                amount, feeAmount, writeOffAmount, cancellationToken);

            var allocation = new PaymentAllocation(
                allocationId,
                command.CompanyId,
                payment.Id,
                invoice?.Id,
                bill?.Id,
                amount,
                currency,
                sourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
                paymentSourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
                targetSourceSimulationEventRecordId: invoice?.SourceSimulationEventRecordId ?? bill?.SourceSimulationEventRecordId,
                idempotencyKey: idempotencyKey,
                feeAmount: feeAmount,
                writeOffAmount: writeOffAmount);

            _dbContext.PaymentAllocations.Add(allocation);
            await ApplyTargetSettlementStatusAsync(command.CompanyId, invoice, bill,
                amount + writeOffAmount, null, cancellationToken);
            if (settlement is not null)
            {
                var posted = await PostSettlementAsync(command.CompanyId, payment, allocation, settlement,
                    command.ActorUserId ?? Guid.Empty, command.CorrelationId, cancellationToken);
                allocation.RecordSettlement(settlement.Result.AllocatedPaymentAmount, payment.Currency,
                    settlement.Facts.FunctionalCurrency, settlement.Result.AllocatedFunctionalAmount,
                    settlement.Result.SettlementFunctionalAmount, settlement.Result.BankFunctionalAmount,
                    settlement.Result.FeeFunctionalAmount, settlement.Result.WriteOffFunctionalAmount,
                    settlement.Result.RealizedGainLossAmount, settlement.Result.RoundingFunctionalAmount,
                    settlement.Result.DocumentOutstandingAfter, settlement.Result.FunctionalOutstandingAfter,
                    settlement.Facts.SettlementRateDate, settlement.Facts.SettlementRate,
                    settlement.Facts.SettlementExchangeRateConversionId,
                    settlement.Facts.SettlementRateIdentity,
                    settlement.Facts.SettlementConversionRoundingResidual,
                    posted.LedgerEntryId, _timeProvider.GetUtcNow().UtcDateTime);
                _telemetry?.Settled(payment.PaymentType, payment.Currency,
                    settlement.Facts.FunctionalCurrency, settlement.Result.IsFinalSettlement,
                    settlement.Result.RealizedGainLossAmount);
            }
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

            ValidateAllocationDto(command.Allocation.PaymentId, command.Allocation.InvoiceId,
                command.Allocation.BillId, command.Allocation.AllocatedAmount,
                command.Allocation.Currency, 0m, 0m);

            var allocation = await _dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.AllocationId, cancellationToken);
            if (allocation is null)
            {
                throw new KeyNotFoundException("Finance payment allocation was not found.");
            }
            if (allocation.SettlementLedgerEntryId.HasValue || allocation.IsReversed)
                throw CreateValidationException("AllocationId",
                    "Posted settlement allocations are immutable. Reverse the settlement and create a corrected allocation.");
            if (await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == command.CompanyId, cancellationToken))
                throw CreateValidationException("AllocationId",
                    "Accounting-enabled allocations cannot be edited in place. Reverse or delete the unposted legacy allocation and create a governed settlement.");

            var previousInvoice = await LoadInvoiceAsync(command.CompanyId, allocation.InvoiceId, cancellationToken);
            var previousBill = await LoadBillAsync(command.CompanyId, allocation.BillId, cancellationToken);
            var payment = await LoadPaymentAsync(command.CompanyId, command.Allocation.PaymentId, cancellationToken);
            var invoice = await LoadInvoiceAsync(command.CompanyId, command.Allocation.InvoiceId, cancellationToken);
            var bill = await LoadBillAsync(command.CompanyId, command.Allocation.BillId, cancellationToken);
            var amount = NormalizeMoney(command.Allocation.AllocatedAmount);
            var currency = NormalizeCurrency(command.Allocation.Currency);

            await ValidateAsync(command.CompanyId, payment, invoice, bill, amount, 0m, 0m,
                currency, allocation.Id, cancellationToken);

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
            if (allocation.SettlementLedgerEntryId.HasValue || allocation.IsReversed)
                throw CreateValidationException("AllocationId",
                    "Posted settlement allocations cannot be deleted. Use the reversal endpoint so the original evidence remains available.");

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

    public Task<FinancePaymentAllocationDto> ReverseAsync(
        ReverseFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            if (command.AllocationId == Guid.Empty)
                throw CreateValidationException("AllocationId", "Allocation id is required.");
            if (command.PaymentId == Guid.Empty)
                throw CreateValidationException("PaymentId", "Payment id is required.");
            if (command.ActorUserId == Guid.Empty)
                throw new UnauthorizedAccessException("A resolved company user is required to reverse a settlement.");
            var idempotencyKey = NormalizeIdempotencyKey(command.IdempotencyKey)
                ?? throw CreateValidationException("IdempotencyKey", "A reversal idempotency key is required.");
            if (string.IsNullOrWhiteSpace(command.Reason))
                throw CreateValidationException("Reason", "A reversal reason is required.");

            var allocation = await _dbContext.PaymentAllocations.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.PaymentId == command.PaymentId &&
                    x.Id == command.AllocationId,
                    cancellationToken)
                ?? throw new KeyNotFoundException("Finance payment allocation was not found.");
            if (allocation.IsReversed)
            {
                if (!string.Equals(allocation.ReversalIdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                    throw CreateValidationException("IdempotencyKey",
                        "The settlement is already reversed under a different idempotency key.");
                return Map(allocation, true);
            }
            if (!allocation.SettlementLedgerEntryId.HasValue)
                throw CreateValidationException("AllocationId",
                    "Only a posted governed settlement can be reversed. Legacy unposted allocations may be deleted.");
            if (_accountingPostingService is null)
                throw CreateValidationException("AllocationId", "The accounting posting authority is unavailable.");

            var period = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
                    x.StartUtc <= command.PostingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) &&
                    x.EndUtc > command.PostingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken)
                ?? throw CreateValidationException("PostingDate", "No accounting period covers the reversal date.");
            var reversed = await _accountingPostingService.ReverseAsync(new ReverseAccountingEntryCommand(
                command.CompanyId, allocation.SettlementLedgerEntryId.Value, period.Id, "B",
                command.PostingDate, command.Reason, $"{allocation.Version}:reversal",
                idempotencyKey, command.ActorUserId, CorrelationId: command.CorrelationId), cancellationToken);
            allocation.Reverse(reversed.Journal.Id, command.ActorUserId, command.Reason,
                idempotencyKey, _timeProvider.GetUtcNow().UtcDateTime);

            var invoice = await LoadInvoiceAsync(command.CompanyId, allocation.InvoiceId, cancellationToken);
            var bill = await LoadBillAsync(command.CompanyId, allocation.BillId, cancellationToken);
            if (invoice is not null)
                await ApplyInvoiceSettlementStatusAsync(command.CompanyId, invoice, null, allocation.Id, cancellationToken);
            if (bill is not null)
                await ApplyBillSettlementStatusAsync(command.CompanyId, bill, null, allocation.Id, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _telemetry?.Reversed(allocation.Currency, allocation.FunctionalCurrency ?? allocation.Currency);
            return Map(allocation);
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
        var remainingAmount = Math.Abs(invoice.Amount);

        var paymentType = invoice.Amount < 0m ? PaymentTypes.Outgoing : PaymentTypes.Incoming;
        var payments = await FindExistingPaymentsAsync(companyId, paymentType, invoice.InvoiceNumber, invoice.Currency, cancellationToken);
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
                paymentType,
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
        var remainingAmount = Math.Abs(bill.Amount);

        var paymentType = bill.Amount < 0m ? PaymentTypes.Incoming : PaymentTypes.Outgoing;
        var payments = await FindExistingPaymentsAsync(companyId, paymentType, bill.BillNumber, bill.Currency, cancellationToken);
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
                paymentType,
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

        await ValidateAsync(companyId, payment, invoice, bill, allocationAmount, 0m, 0m,
            payment.Currency, null, cancellationToken);

        var allocationId = Guid.NewGuid();
        var command = new CreateFinancePaymentAllocationCommand(companyId,
            new CreateFinancePaymentAllocationDto(payment.Id, invoice?.Id, bill?.Id,
                allocationAmount, payment.Currency,
                $"payment-allocation-backfill:{companyId:N}:{payment.Id:N}:{(invoice?.Id ?? bill!.Id):N}"));
        var settlement = await PrepareSettlementAsync(command, allocationId, payment, invoice, bill,
            allocationAmount, 0m, 0m, cancellationToken);

        var allocation = new PaymentAllocation(
            allocationId,
            companyId,
            payment.Id,
            invoice?.Id,
            bill?.Id,
            allocationAmount,
            payment.Currency,
            sourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
            paymentSourceSimulationEventRecordId: payment.SourceSimulationEventRecordId,
            targetSourceSimulationEventRecordId: invoice?.SourceSimulationEventRecordId ?? bill?.SourceSimulationEventRecordId,
            idempotencyKey: command.Allocation.IdempotencyKey);

        _dbContext.PaymentAllocations.Add(allocation);
        await ApplyTargetSettlementStatusAsync(companyId, invoice, bill, allocationAmount, null, cancellationToken);
        if (settlement is not null)
        {
            var posted = await PostSettlementAsync(companyId, payment, allocation, settlement,
                Guid.Empty, null, cancellationToken);
            allocation.RecordSettlement(settlement.Result.AllocatedPaymentAmount, payment.Currency,
                settlement.Facts.FunctionalCurrency, settlement.Result.AllocatedFunctionalAmount,
                settlement.Result.SettlementFunctionalAmount, settlement.Result.BankFunctionalAmount,
                settlement.Result.FeeFunctionalAmount, settlement.Result.WriteOffFunctionalAmount,
                settlement.Result.RealizedGainLossAmount, settlement.Result.RoundingFunctionalAmount,
                settlement.Result.DocumentOutstandingAfter, settlement.Result.FunctionalOutstandingAfter,
                settlement.Facts.SettlementRateDate, settlement.Facts.SettlementRate,
                settlement.Facts.SettlementExchangeRateConversionId, settlement.Facts.SettlementRateIdentity,
                settlement.Facts.SettlementConversionRoundingResidual, posted.LedgerEntryId,
                _timeProvider.GetUtcNow().UtcDateTime);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new BackfillChunkResult(1, allocationAmount);
    }

    private async Task ValidateAsync(
        Guid companyId,
        Payment payment,
        FinanceInvoice? invoice,
        FinanceBill? bill,
        decimal amount,
        decimal feeAmount,
        decimal writeOffAmount,
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

        await EnsureSourceCompatibilityAsync(companyId, payment, invoice, bill, cancellationToken);

        if (!string.Equals(payment.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            throw CreateValidationException("Currency", $"Allocation currency '{currency}' must match payment currency '{payment.Currency}'.");
        }

        if (invoice is not null)
        {
            var expectedPaymentType = invoice.Amount < 0m ? PaymentTypes.Outgoing : PaymentTypes.Incoming;
            if (!string.Equals(payment.PaymentType, expectedPaymentType, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("PaymentId", invoice.Amount < 0m
                    ? "Customer credit refunds require an outgoing payment."
                    : "Incoming payments can only be allocated to customer invoices.");
            }

            if (!string.Equals(invoice.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("Currency", $"Allocation currency '{currency}' must match invoice currency '{invoice.Currency}'.");
            }
        }

        if (bill is not null)
        {
            var expectedPaymentType = bill.Amount < 0m ? PaymentTypes.Incoming : PaymentTypes.Outgoing;
            if (!string.Equals(payment.PaymentType, expectedPaymentType, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("PaymentId", bill.Amount < 0m
                    ? "Supplier credit refunds require an incoming payment."
                    : "Outgoing payments can only be allocated to supplier bills.");
            }

            if (!string.Equals(bill.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateValidationException("Currency", $"Allocation currency '{currency}' must match bill currency '{bill.Currency}'.");
            }
        }

        if ((invoice?.Amount ?? bill!.Amount) < 0m && writeOffAmount > 0m)
            throw CreateValidationException("WriteOffAmount", "Credit-note refunds cannot include a write-off.");
        if (string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase) && feeAmount >= amount)
            throw CreateValidationException("FeeAmount", "An incoming settlement fee must be smaller than the allocated amount.");

        var paymentAmountRequired = NormalizeMoney(string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
            ? amount - feeAmount
            : amount + feeAmount);
        var allocatedToPayment = await GetAllocatedToPaymentAsync(companyId, payment.Id, allocationIdToExclude, cancellationToken);
        var remainingOnPayment = NormalizeMoney(Math.Max(0m, payment.Amount - allocatedToPayment));
        if (paymentAmountRequired > remainingOnPayment)
        {
            throw CreateValidationException("AllocatedAmount", $"Payment allocations cannot exceed the remaining unallocated payment amount of {remainingOnPayment:0.00}.");
        }

        if (invoice is not null)
        {
            var allocatedToInvoice = await GetAllocatedToInvoiceAsync(companyId, invoice.Id, allocationIdToExclude, cancellationToken);
            var remainingOpenAmount = NormalizeMoney(Math.Max(0m, Math.Abs(invoice.Amount) - allocatedToInvoice));
            if (amount + writeOffAmount > remainingOpenAmount)
            {
                throw CreateValidationException("AllocatedAmount", $"Invoice allocations cannot exceed the remaining open amount of {remainingOpenAmount:0.00}.");
            }
        }

        if (bill is not null)
        {
            var allocatedToBill = await GetAllocatedToBillAsync(companyId, bill.Id, allocationIdToExclude, cancellationToken);
            var remainingOpenAmount = NormalizeMoney(Math.Max(0m, Math.Abs(bill.Amount) - allocatedToBill));
            if (amount + writeOffAmount > remainingOpenAmount)
            {
                throw CreateValidationException("AllocatedAmount", $"Bill allocations cannot exceed the remaining open amount of {remainingOpenAmount:0.00}.");
            }
        }
    }

    private async Task EnsureSourceCompatibilityAsync(
        Guid companyId,
        Payment payment,
        FinanceInvoice? invoice,
        FinanceBill? bill,
        CancellationToken cancellationToken)
    {
        var paymentSource = await ResolveRecordSourceAsync(
            companyId,
            payment.Id,
            payment.SourceSimulationEventRecordId,
            ["payment"],
            cancellationToken);
        var targetId = invoice?.Id ?? bill!.Id;
        var targetSimulationEventId = invoice?.SourceSimulationEventRecordId ?? bill?.SourceSimulationEventRecordId;
        IReadOnlyCollection<string> targetEntityTypes = invoice is not null
            ? ["invoice"]
            : ["supplier_invoice", "bill"];
        var targetSource = await ResolveRecordSourceAsync(
            companyId,
            targetId,
            targetSimulationEventId,
            targetEntityTypes,
            cancellationToken);

        if (!string.Equals(paymentSource, targetSource, StringComparison.OrdinalIgnoreCase))
        {
            throw CreateValidationException(
                "PaymentId",
                "Payments and the documents they settle must use the same accounting source. Create or import the payment in the selected source before allocating it.");
        }
    }

    private async Task<string> ResolveRecordSourceAsync(
        Guid companyId,
        Guid recordId,
        Guid? sourceSimulationEventRecordId,
        IReadOnlyCollection<string> entityTypes,
        CancellationToken cancellationToken)
    {
        var isFortnoxBacked = await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(reference =>
                reference.CompanyId == companyId &&
                reference.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                entityTypes.Contains(reference.EntityType) &&
                reference.InternalRecordId == recordId,
                cancellationToken);

        return isFortnoxBacked
            ? FinanceDataSources.Fortnox
            : sourceSimulationEventRecordId.HasValue
                ? FinanceDataSources.Simulation
                : FinanceDataSources.Manual;
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
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                (!allocationIdToExclude.HasValue || x.Id != allocationIdToExclude.Value))
            .SumAsync(x => (decimal?)x.AllocatedPaymentAmount, cancellationToken) ?? 0m;

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
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                (!allocationIdToExclude.HasValue || x.Id != allocationIdToExclude.Value))
            .SumAsync(x => (decimal?)(x.AllocatedAmount + x.WriteOffAmount), cancellationToken) ?? 0m;

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
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                (!allocationIdToExclude.HasValue || x.Id != allocationIdToExclude.Value))
            .SumAsync(x => (decimal?)(x.AllocatedAmount + x.WriteOffAmount), cancellationToken) ?? 0m;

    private static string ResolveSettlementStatus(decimal totalAmount, decimal allocatedAmount)
    {
        var roundedTotal = NormalizeMoney(Math.Abs(totalAmount));
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
        string currency,
        decimal feeAmount,
        decimal writeOffAmount)
    {
        if (paymentId == Guid.Empty)
        {
            throw CreateValidationException("PaymentId", "Payment id is required.");
        }

        if (allocatedAmount <= 0m)
        {
            throw CreateValidationException("AllocatedAmount", "Allocated amount must be greater than zero.");
        }

        _ = NormalizeNonNegativeMoney(feeAmount, "FeeAmount");
        _ = NormalizeNonNegativeMoney(writeOffAmount, "WriteOffAmount");

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

    private async Task<PreparedSettlement?> PrepareSettlementAsync(
        CreateFinancePaymentAllocationCommand command,
        Guid allocationId,
        Payment payment,
        FinanceInvoice? invoice,
        FinanceBill? bill,
        decimal amount,
        decimal feeAmount,
        decimal writeOffAmount,
        CancellationToken cancellationToken)
    {
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        if (configuration is null)
        {
            if (feeAmount != 0m || writeOffAmount != 0m)
                throw CreateValidationException("AllocatedAmount",
                    "Fees and write-offs require native accounting so their governed accounts can be resolved.");
            return null;
        }

        if (NormalizeIdempotencyKey(command.Allocation.IdempotencyKey) is null)
            throw CreateValidationException("IdempotencyKey",
                "Accounting-enabled settlement requires a stable idempotency key.");

        _ = NormalizeNonNegativeMoney(feeAmount, "FeeAmount");
        _ = NormalizeNonNegativeMoney(writeOffAmount, "WriteOffAmount");
        if (_cashSettlementPostingService is null)
            throw CreateValidationException("PaymentId", "The cash settlement posting authority is unavailable.");
        if (!string.Equals(payment.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            throw CreateValidationException("PaymentId",
                "Accounting-enabled settlements require a completed payment with authoritative evidence.");
        await EnsureAuthoritativePaymentEvidenceAsync(command.CompanyId, payment.Id, cancellationToken);

        var documentCurrency = invoice?.Currency ?? bill!.Currency;
        var documentTotal = Math.Abs(invoice?.Amount ?? bill!.Amount);
        var profileFacts = await LoadDocumentCarryingFactsAsync(command.CompanyId, invoice, bill,
            configuration.BaseCurrency, documentTotal, cancellationToken);
        var priorQuery = _dbContext.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId &&
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                (invoice != null ? x.InvoiceId == invoice.Id : x.BillId == bill!.Id));
        var previouslyAppliedDocument = await priorQuery
            .SumAsync(x => (decimal?)(x.AllocatedAmount + x.WriteOffAmount), cancellationToken) ?? 0m;
        var previouslyAppliedFunctional = await priorQuery
            .SumAsync(x => (decimal?)(x.AllocatedFunctionalAmount ??
                (x.AllocatedAmount + x.WriteOffAmount) * profileFacts.OriginalRate), cancellationToken) ?? 0m;
        previouslyAppliedFunctional = NormalizeMoney(previouslyAppliedFunctional);

        var incoming = string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase);
        var conversionInput = NormalizeMoney(amount + writeOffAmount + (incoming ? 0m : feeAmount));
        ExchangeRateConversionResult? conversion = null;
        decimal settlementRate;
        string rateIdentity;
        decimal conversionResidual;
        var rateDate = DateOnly.FromDateTime(payment.PaymentDate);
        if (string.Equals(documentCurrency, configuration.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            settlementRate = 1m;
            rateIdentity = DocumentCurrencyFacts.BaseIdentity(documentCurrency, rateDate);
            conversionResidual = 0m;
        }
        else
        {
            if (_exchangeRateService is null)
                throw CreateValidationException("Currency", "The authoritative exchange-rate service is unavailable.");
            if (!command.ActorUserId.HasValue || command.ActorUserId == Guid.Empty)
                throw CreateValidationException("PaymentId",
                    "A resolved company user is required to retain foreign-currency settlement evidence.");
            conversion = await _exchangeRateService.ConvertAsync(new ConvertCurrencyCommand(
                command.CompanyId, command.ActorUserId.Value, conversionInput, documentCurrency,
                configuration.BaseCurrency, rateDate, ExchangeRateLookupPurposes.SettlementDate,
                $"payment-allocation:{command.CompanyId:N}:{allocationId:N}:settlement",
                command.CorrelationId), cancellationToken);
            settlementRate = conversion.EffectiveRate;
            rateIdentity = DocumentCurrencyFacts.RateIdentity(conversion);
            conversionResidual = conversion.RoundingResidual;
        }

        ForeignCurrencySettlementResult result;
        try
        {
            result = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
                payment.PaymentType, documentTotal, profileFacts.FunctionalTotal,
                previouslyAppliedDocument, previouslyAppliedFunctional, amount, feeAmount,
                writeOffAmount, settlementRate, configuration.RoundingPrecision,
                configuration.RoundingMode));
        }
        catch (InvalidOperationException exception)
        {
            _telemetry?.Blocked("settlement_policy_blocked", documentCurrency, configuration.BaseCurrency);
            throw CreateValidationException("AllocatedAmount", exception.Message);
        }

        if (conversion is not null && conversion.InputAmount != result.JournalDocumentTotal)
            throw CreateValidationException("AllocatedAmount",
                "The retained settlement conversion does not reconcile to the journal document total.");
        var controlRole = invoice is not null
            ? AccountingAccountRoleKeys.AccountsReceivable
            : AccountingAccountRoleKeys.AccountsPayable;
        var facts = new CashSettlementAccountingFacts(
            allocationId, controlRole, documentCurrency, configuration.BaseCurrency,
            amount, writeOffAmount, feeAmount, result.AllocatedPaymentAmount,
            result.AllocatedFunctionalAmount, result.SettlementFunctionalAmount,
            result.BankFunctionalAmount, result.FeeFunctionalAmount,
            result.WriteOffFunctionalAmount, result.RealizedGainLossAmount,
            result.RoundingFunctionalAmount, rateDate, settlementRate, conversion?.Id,
            rateIdentity, conversionResidual, result.JournalDocumentTotal,
            result.IsFinalSettlement);
        return new PreparedSettlement(result, facts);
    }

    private async Task<CashSettlementPostingResultDto> PostSettlementAsync(
        Guid companyId,
        Payment payment,
        PaymentAllocation allocation,
        PreparedSettlement settlement,
        Guid actorUserId,
        string? correlationId,
        CancellationToken cancellationToken) =>
        await _cashSettlementPostingService!.PostCashSettlementAsync(
            new PostCashSettlementCommand(
                companyId,
                FinanceCashPostingSourceTypes.PaymentAllocation,
                allocation.Id.ToString("D"),
                payment.Id,
                settlement.Result.AllocatedPaymentAmount,
                payment.PaymentDate,
                settlement.Facts,
                actorUserId,
                correlationId),
            cancellationToken);

    private async Task<DocumentCarryingFacts> LoadDocumentCarryingFactsAsync(
        Guid companyId,
        FinanceInvoice? invoice,
        FinanceBill? bill,
        string functionalCurrency,
        decimal documentTotal,
        CancellationToken cancellationToken)
    {
        var documentCurrency = invoice?.Currency ?? bill!.Currency;
        if (string.Equals(documentCurrency, functionalCurrency, StringComparison.OrdinalIgnoreCase))
            return new DocumentCarryingFacts(documentTotal, 1m);

        if (invoice is not null)
        {
            var profile = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.InvoiceId == invoice.Id &&
                    x.Status == CustomerInvoiceAccountingStatuses.Posted, cancellationToken);
            if (profile is null || !profile.HasAuthoritativeCurrencyFacts)
                throw CreateValidationException("InvoiceId",
                    "The foreign-currency invoice must be posted with authoritative transaction-date rate facts before settlement.");
            if (Math.Abs(Math.Abs(profile.GrossAmount) - documentTotal) > 0.01m)
                throw CreateValidationException("InvoiceId", "The invoice accounting snapshot no longer matches the open-item amount.");
            return new DocumentCarryingFacts(Math.Abs(profile.GrossBaseAmount), profile.ExchangeRate);
        }

        var billProfile = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BillId == bill!.Id &&
                x.Status == SupplierBillAccountingStatuses.Posted, cancellationToken);
        if (billProfile is null || !billProfile.HasAuthoritativeCurrencyFacts)
            throw CreateValidationException("BillId",
                "The foreign-currency supplier bill must be posted with authoritative transaction-date rate facts before settlement.");
        if (Math.Abs(Math.Abs(billProfile.GrossAmount) - documentTotal) > 0.01m)
            throw CreateValidationException("BillId", "The supplier-bill accounting snapshot no longer matches the open-item amount.");
        return new DocumentCarryingFacts(Math.Abs(billProfile.GrossBaseAmount), billProfile.ExchangeRate);
    }

    private async Task EnsureAuthoritativePaymentEvidenceAsync(
        Guid companyId, Guid paymentId, CancellationToken cancellationToken)
    {
        var providerReported = await _dbContext.FinanceExternalReferences.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.InternalRecordId == paymentId &&
                x.EntityType == "payment", cancellationToken);
        if (!providerReported) return;
        var bankMatched = await _dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.PaymentId == paymentId, cancellationToken);
        if (!bankMatched)
        {
            _telemetry?.Blocked("bank_evidence_required", null, null);
            throw CreateValidationException("PaymentId",
                "Provider-reported settlement requires authoritative bank evidence and remains reconciliation-required until matched.");
        }
    }

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal NormalizeNonNegativeMoney(decimal value, string field)
    {
        if (value < 0m) throw CreateValidationException(field, $"{field} cannot be negative.");
        return NormalizeMoney(value);
    }

    private static string? NormalizeIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 200)
            throw CreateValidationException("IdempotencyKey", "Idempotency key cannot exceed 200 characters.");
        return normalized;
    }

    private static bool ShouldBackfillPaidDocument(string status, string settlementStatus) =>
        string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal) ||
        string.Equals(status?.Trim(), "paid", StringComparison.OrdinalIgnoreCase);

    private static FinancePaymentAllocationDto Map(PaymentAllocation allocation, bool isIdempotentReplay = false) =>
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
            allocation.TargetSourceSimulationEventRecordId,
            allocation.IdempotencyKey,
            isIdempotentReplay,
            allocation.FeeAmount,
            allocation.WriteOffAmount,
            allocation.AllocatedPaymentAmount,
            allocation.PaymentCurrency,
            allocation.FunctionalCurrency,
            allocation.AllocatedFunctionalAmount,
            allocation.SettlementFunctionalAmount,
            allocation.BankFunctionalAmount,
            allocation.FeeFunctionalAmount,
            allocation.WriteOffFunctionalAmount,
            allocation.RealizedGainLossAmount,
            allocation.RoundingFunctionalAmount,
            allocation.DocumentOutstandingAfter,
            allocation.FunctionalOutstandingAfter,
            allocation.SettlementRateDate,
            allocation.SettlementRate,
            allocation.SettlementExchangeRateConversionId,
            allocation.SettlementRateIdentity,
            allocation.SettlementConversionRoundingResidual,
            allocation.SettlementLedgerEntryId,
            allocation.ReversalLedgerEntryId,
            allocation.SettlementStatus,
            allocation.ReversedUtc,
            allocation.ReversedByUserId,
            allocation.ReversalReason,
            allocation.Version);

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
        {
            return await action();
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task ExecuteInTransactionAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
        {
            await action();
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
            await action();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private sealed record BackfillDocumentResult(int CreatedAllocationCount, int CreatedPaymentCount);
    private sealed record BackfillChunkResult(int CreatedAllocationCount, decimal AllocatedAmount);
    private sealed record DocumentCarryingFacts(decimal FunctionalTotal, decimal OriginalRate);
    private sealed record PreparedSettlement(ForeignCurrencySettlementResult Result, CashSettlementAccountingFacts Facts);

    private static FinanceValidationException CreateValidationException(string field, string message) =>
        new(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [field] = [message]
            },
            message);
}
