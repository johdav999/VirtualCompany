using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal static class CustomerInvoiceSnapshotWriter
{
    public static async Task CaptureBeforeLegacyMasterChangeAsync(VirtualCompanyDbContext db, Guid companyId,
        FinanceCounterparty counterparty, CancellationToken cancellationToken)
    {
        if (counterparty.CounterpartyType != "customer") return;
        var invoiceIds = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CounterpartyId == counterparty.Id).Select(x => x.Id).ToListAsync(cancellationToken);
        if (invoiceIds.Count == 0) return;
        var captured = await db.CustomerInvoiceCustomerSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && invoiceIds.Contains(x.InvoiceId)).Select(x => x.InvoiceId).ToListAsync(cancellationToken);
        var capturedSet = captured.ToHashSet();
        var profile = await db.CustomerBillingProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CounterpartyId == counterparty.Id, cancellationToken);
        var snapshot = profile is null
            ? JsonSerializer.Serialize(new { counterparty.Name, counterparty.Email, counterparty.TaxId, counterparty.PaymentTerms,
                counterparty.PreferredPaymentMethod, counterparty.DefaultAccountMapping, source = "legacy_counterparty" })
            : JsonSerializer.Serialize(new { profile.LegalName, profile.DisplayName, profile.PartyKind, profile.TaxIdentifier,
                profile.VatIdentifier, profile.BillingAddressLine1, profile.BillingAddressLine2, profile.BillingPostalCode,
                profile.BillingCity, profile.BillingCountryCode, profile.LanguageCode, profile.CurrencyCode,
                profile.PaymentTermKind, profile.PaymentTermDays, profile.PaymentMethod, profile.InvoiceDeliveryChannel,
                profile.InvoiceDeliveryEmail, profile.BuyerReference, profile.EInvoiceIdentifier, profile.EInvoiceIdentifierType,
                profile.SourceKind, profile.SourceReference, profile.Version });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant();
        foreach (var invoiceId in invoiceIds.Where(id => !capturedSet.Contains(id)))
            db.CustomerInvoiceCustomerSnapshots.Add(new CustomerInvoiceCustomerSnapshot(Guid.NewGuid(), companyId,
                invoiceId, counterparty.Id, profile?.Version, profile?.SourceKind ?? CustomerBillingSourceKinds.Migration,
                snapshot, hash, DateTime.UtcNow));
    }
}
