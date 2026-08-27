using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceCorrectionPolicy : ICustomerInvoiceCorrectionPolicy
{
    private const decimal SmallBalanceThreshold = 100m;
    private readonly VirtualCompanyDbContext _db;

    public CustomerInvoiceCorrectionPolicy(VirtualCompanyDbContext db) => _db = db;

    public async Task<CustomerInvoiceCorrectionPolicyDecisionDto> EvaluateAsync(
        EvaluateCustomerInvoiceCorrectionQuery query, CancellationToken cancellationToken)
    {
        var type = CustomerInvoiceCorrectionTypes.Normalize(query.CorrectionType);
        var invoice = await _db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId, cancellationToken);
        if (invoice is null)
            return Block(CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound,
                "The customer invoice could not be found.", 0m, 0m, 0m, 0m, 0m, 0m, 0m,
                query.Currency, "missing", Hash("missing"), false, false, null, []);

        var currency = invoice.Currency;
        var gross = decimal.Round(Math.Abs(invoice.Amount), 2, MidpointRounding.AwayFromZero);
        var profile = await _db.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.InvoiceId == invoice.Id, cancellationToken);
        var allocatedGross = await _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.InvoiceId == invoice.Id &&
                x.Payment.Status == PaymentStatuses.Completed && x.Payment.PaymentType == PaymentTypes.Incoming)
            .SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;
        var released = await _db.CustomerInvoiceCorrectionAllocationAdjustments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.PaymentAllocation.InvoiceId == invoice.Id)
            .SumAsync(x => (decimal?)x.ReleasedAmount, cancellationToken) ?? 0m;
        var allocated = Math.Max(0m, allocatedGross - released);
        var reservations = await _db.CustomerInvoiceCorrections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.InvoiceId == invoice.Id &&
                x.Status != CustomerInvoiceCorrectionStatuses.Failed &&
                x.Status != CustomerInvoiceCorrectionStatuses.Cancelled &&
                (!query.ExistingCorrectionId.HasValue || x.Id != query.ExistingCorrectionId.Value))
            .Select(x => new { x.CorrectionType, x.Amount, x.Status }).ToListAsync(cancellationToken);
        var credits = reservations.Where(x => CustomerInvoiceCorrectionTypes.CreditTypes.Contains(x.CorrectionType)).Sum(x => x.Amount);
        var refunds = reservations.Where(x => x.CorrectionType == CustomerInvoiceCorrectionTypes.Refund).Sum(x => x.Amount);
        var writeOffs = reservations.Where(x => x.CorrectionType is CustomerInvoiceCorrectionTypes.SmallBalanceWriteOff or CustomerInvoiceCorrectionTypes.BadDebt).Sum(x => x.Amount);
        var recoveries = reservations.Where(x => x.CorrectionType == CustomerInvoiceCorrectionTypes.BadDebtRecovery).Sum(x => x.Amount);
        var remaining = Math.Max(0m, gross - credits - writeOffs + recoveries);
        var outstanding = Math.Max(0m, remaining - Math.Max(0m, allocated - refunds));
        var maximum = type switch
        {
            CustomerInvoiceCorrectionTypes.Refund => Math.Max(0m, allocated - refunds),
            CustomerInvoiceCorrectionTypes.SmallBalanceWriteOff or CustomerInvoiceCorrectionTypes.BadDebt => outstanding,
            CustomerInvoiceCorrectionTypes.BadDebtRecovery => Math.Max(0m,
                reservations.Where(x => x.CorrectionType == CustomerInvoiceCorrectionTypes.BadDebt &&
                    x.Status == CustomerInvoiceCorrectionStatuses.Executed).Sum(x => x.Amount) - recoveries),
            CustomerInvoiceCorrectionTypes.Cancellation => gross,
            _ => remaining
        };
        var sourceVersion = $"{invoice.UpdatedUtc:O}|{profile?.Version ?? 0}|{allocated.ToString("G29", CultureInfo.InvariantCulture)}|{reservations.Count}";
        var sourceHash = Hash(string.Join('|', invoice.Id, invoice.Authority, invoice.DocumentKind,
            invoice.PostingStatus, invoice.SettlementStatus, invoice.ProcessingStatus,
            gross.ToString("G29", CultureInfo.InvariantCulture), currency, sourceVersion,
            credits.ToString("G29", CultureInfo.InvariantCulture), refunds.ToString("G29", CultureInfo.InvariantCulture),
            writeOffs.ToString("G29", CultureInfo.InvariantCulture), recoveries.ToString("G29", CultureInfo.InvariantCulture)));
        var lockedVatReturn = profile?.LedgerEntryId is Guid ledgerId
            ? await _db.VatReturnSourceContributions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.LedgerEntryId == ledgerId &&
                    x.VatReturn.Status == VatReturnStatuses.Locked)
                .Select(x => (Guid?)x.VatReturnId).FirstOrDefaultAsync(cancellationToken)
            : null;
        var originalPeriodClosed = profile is not null && await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId && x.Id == profile.FiscalPeriodId &&
                (x.IsClosed || x.IsReportingLocked), cancellationToken);
        var evidence = new List<CustomerInvoiceCorrectionEvidenceDto>
        {
            new("invoiceAuthority", invoice.Authority), new("documentKind", invoice.DocumentKind),
            new("postingStatus", invoice.PostingStatus), new("settlementStatus", invoice.SettlementStatus),
            new("allocatedPaidAmount", allocated.ToString("0.00", CultureInfo.InvariantCulture)),
            new("releasedAllocationAmount", released.ToString("0.00", CultureInfo.InvariantCulture)),
            new("remainingEconomicBalance", remaining.ToString("0.00", CultureInfo.InvariantCulture)),
            new("maximumAllowedAmount", maximum.ToString("0.00", CultureInfo.InvariantCulture)),
            new("sourceHash", sourceHash)
        };

        CustomerInvoiceCorrectionPolicyDecisionDto Deny(string code, string explanation) =>
            Block(code, explanation, gross, allocated, credits, refunds, writeOffs, remaining, maximum,
                currency, sourceVersion, sourceHash, originalPeriodClosed, lockedVatReturn.HasValue,
                lockedVatReturn, evidence);

        if (invoice.DocumentKind != FinanceDocumentKinds.Invoice || invoice.Amount <= 0m)
            return Deny(CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound,
                "Only an original customer invoice can receive this correction.");
        if (!string.Equals(currency, query.Currency?.Trim(), StringComparison.OrdinalIgnoreCase))
            return Deny(CustomerInvoiceCorrectionReasonCodes.AmountExceedsBalance,
                "The correction currency must match the original invoice currency.");
        if (query.Amount <= 0m || decimal.Round(query.Amount, 2, MidpointRounding.AwayFromZero) > maximum)
        {
            var code = type switch
            {
                CustomerInvoiceCorrectionTypes.Refund => CustomerInvoiceCorrectionReasonCodes.RefundExceedsPaid,
                CustomerInvoiceCorrectionTypes.SmallBalanceWriteOff or CustomerInvoiceCorrectionTypes.BadDebt => CustomerInvoiceCorrectionReasonCodes.WriteOffExceedsOutstanding,
                CustomerInvoiceCorrectionTypes.BadDebtRecovery => CustomerInvoiceCorrectionReasonCodes.RecoveryExceedsBadDebt,
                _ => CustomerInvoiceCorrectionReasonCodes.AmountExceedsBalance
            };
            return Deny(code, $"The requested amount exceeds the current maximum of {maximum:0.00} {currency}.");
        }
        if (type == CustomerInvoiceCorrectionTypes.SmallBalanceWriteOff && query.Amount > SmallBalanceThreshold)
            return Deny(CustomerInvoiceCorrectionReasonCodes.SmallBalanceThresholdExceeded,
                $"Small-balance write-off is limited to {SmallBalanceThreshold:0.00} {currency}; use bad debt for a larger supported amount.");
        if (type == CustomerInvoiceCorrectionTypes.Cancellation)
        {
            if (invoice.Authority != StatutoryDocumentAuthorities.Native)
                return Deny(CustomerInvoiceCorrectionReasonCodes.ProviderActionRequired,
                    "This provider-authoritative invoice must be cancelled through its accounting-provider action workflow.");
            var delivered = await _db.CustomerInvoiceEmailDeliveries.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == query.CompanyId && x.InvoiceId == invoice.Id &&
                    x.Status == CustomerInvoiceDeliveryStatuses.Accepted, cancellationToken);
            if (profile?.LedgerEntryId is not null || allocated > 0m || delivered || invoice.PostingStatus == FinanceDocumentPostingStatuses.Cancelled)
                return Deny(CustomerInvoiceCorrectionReasonCodes.CancellationNotAllowed,
                    "An issued, posted, delivered, or paid invoice cannot be cancelled. Create a linked credit note instead.");
        }
        if (type != CustomerInvoiceCorrectionTypes.Cancellation && profile?.LedgerEntryId is null)
            return Deny(CustomerInvoiceCorrectionReasonCodes.OriginalNotPosted,
                "The original invoice must have a posted journal before this correction can be proposed.");
        if (CustomerInvoiceCorrectionTypes.CreditTypes.Contains(type) && invoice.Authority != StatutoryDocumentAuthorities.Native)
            return Deny(CustomerInvoiceCorrectionReasonCodes.ProviderActionRequired,
                "This provider-authoritative invoice must be credited through its provider correction workflow.");

        return new(true, CustomerInvoiceCorrectionReasonCodes.Ready,
            "The correction is within the current economic balance and can be proposed for approval.",
            true, gross, allocated, credits, refunds, writeOffs, remaining, maximum, currency,
            sourceVersion, sourceHash, originalPeriodClosed, lockedVatReturn.HasValue, lockedVatReturn, evidence);
    }

    private static CustomerInvoiceCorrectionPolicyDecisionDto Block(string code, string explanation,
        decimal invoiceAmount, decimal allocated, decimal credits, decimal refunds, decimal writeOffs,
        decimal remaining, decimal maximum, string currency, string version, string hash,
        bool currentPeriod, bool vatCorrection, Guid? vatReturn,
        IReadOnlyList<CustomerInvoiceCorrectionEvidenceDto> evidence) =>
        new(false, code, explanation, true, invoiceAmount, allocated, credits, refunds, writeOffs,
            remaining, maximum, currency, version, hash, currentPeriod, vatCorrection, vatReturn, evidence);

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
