using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceScheduleOccurrencePolicy : ICustomerInvoiceScheduleOccurrencePolicy
{
    private readonly VirtualCompanyDbContext _db;
    private readonly ICustomerInvoiceDraftCalculationPolicy _calculation;

    public CustomerInvoiceScheduleOccurrencePolicy(VirtualCompanyDbContext db,
        ICustomerInvoiceDraftCalculationPolicy calculation)
    {
        _db = db;
        _calculation = calculation;
    }

    public async Task<CustomerInvoiceScheduleOccurrenceDecision> EvaluateAsync(Guid companyId,
        CustomerInvoiceDraftInput input, CancellationToken cancellationToken)
    {
        var calculation = await _calculation.CalculateAsync(companyId, input, cancellationToken);
        var blockers = calculation.Blockers.ToList();
        var customer = await _db.FinanceCounterparties.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == input.CustomerId &&
                x.CounterpartyType == "customer", cancellationToken);
        var profile = await _db.CustomerBillingProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CounterpartyId == input.CustomerId,
                cancellationToken);
        var statutory = await _db.CompanyStatutoryProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var configuration = await _db.AccountingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (customer is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerNotFound,
                "The selected customer is no longer available.", input.CustomerId));
        else if (customer.MergedIntoCounterpartyId.HasValue)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerMerged,
                "The selected customer was merged. Update the schedule to use the current customer.", input.CustomerId));

        if (profile is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerProfileMissing,
                "Complete the customer billing profile before generating this occurrence.", input.CustomerId));
        else
        {
            if (profile.EffectiveFrom > input.IssueDate || profile.EffectiveTo < input.IssueDate)
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerProfileMissing,
                    "The customer billing profile is not effective on the occurrence date.", input.CustomerId));
            if (profile.CreditStatus != CustomerBillingCreditStatuses.Active)
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerCreditHold,
                    "Customer credit control currently prevents invoice generation.", input.CustomerId));
            if (profile.ConflictState != "clear")
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerConflict,
                    "Resolve the customer billing data conflict before generating this occurrence.", input.CustomerId));
            if (!string.Equals(profile.CurrencyCode, input.Currency, StringComparison.OrdinalIgnoreCase))
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.UnsupportedCurrency,
                    "The schedule currency no longer matches the customer's billing currency.", input.CustomerId));
            ValidateDelivery(profile, input, blockers);

            if (profile.CreditLimit > 0m)
            {
                var outstanding = await _db.FinanceInvoices.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.CounterpartyId == input.CustomerId &&
                        x.Currency == input.Currency && x.SettlementStatus != FinanceSettlementStatuses.Paid &&
                        x.SettlementStatus != FinanceSettlementStatuses.Credited)
                    .SumAsync(x => x.Amount - x.PaidAmount, cancellationToken);
                if (outstanding + calculation.GrossTotal > profile.CreditLimit)
                    blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerCreditLimit,
                        "This occurrence would exceed the customer's current credit limit.", input.CustomerId));
            }
        }

        if (statutory is null || !statutory.IsFormatComplete || !statutory.IsUserAttested ||
            statutory.OrganisationRegistrationEffectiveFrom > input.IssueDate ||
            statutory.VatRegistrationEffectiveFrom > input.IssueDate ||
            statutory.VatRegistrationEffectiveTo < input.IssueDate)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.StatutoryProfileIncomplete,
                "The company statutory profile is incomplete or not effective on the occurrence date."));

        if (configuration is null || configuration.SetupState != AccountingSetupStateValues.Ready ||
            configuration.PolicyPackEffectiveFrom > input.IssueDate)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.AccountingConfigurationMissing,
                "Accounting and tax policy are not ready for the occurrence date."));

        if (input.DueDate < input.IssueDate || input.SupplyDate > input.IssueDate)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.InvalidDates,
                "The schedule produced invalid invoice, supply, or due dates."));

        blockers = blockers.GroupBy(x => new { x.ReasonCode, x.Explanation, x.RelatedEntityId })
            .Select(x => x.First()).ToList();
        var allowed = blockers.Count == 0;
        return new(allowed, allowed ? CustomerInvoiceDraftReasonCodes.Ready : blockers[0].ReasonCode,
            allowed ? "The occurrence can be generated as a reviewable native invoice draft." : blockers[0].Explanation,
            calculation.NetTotal, calculation.TaxTotal, calculation.GrossTotal, input.Currency,
            calculation.Warnings, blockers);
    }

    private static void ValidateDelivery(CustomerBillingProfile profile, CustomerInvoiceDraftInput input,
        ICollection<CustomerInvoiceDraftIssue> blockers)
    {
        if (input.DeliveryIntent == CustomerBillingDeliveryChannels.Email &&
            string.IsNullOrWhiteSpace(profile.InvoiceDeliveryEmail))
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.InvalidEvidence,
                "Add a valid customer billing email before generating an email-delivery draft.", input.CustomerId));
        else if (input.DeliveryIntent == CustomerBillingDeliveryChannels.EInvoice &&
            (string.IsNullOrWhiteSpace(profile.EInvoiceIdentifier) || string.IsNullOrWhiteSpace(profile.EInvoiceIdentifierType)))
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.InvalidEvidence,
                "Complete the customer's e-invoice identity before generating this occurrence.", input.CustomerId));
        else if (input.DeliveryIntent == CustomerBillingDeliveryChannels.Postal &&
            (string.IsNullOrWhiteSpace(profile.BillingAddressLine1) ||
             string.IsNullOrWhiteSpace(profile.BillingPostalCode) ||
             string.IsNullOrWhiteSpace(profile.BillingCity) ||
             string.IsNullOrWhiteSpace(profile.BillingCountryCode)))
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.InvalidEvidence,
                "Complete the customer's billing address before generating a postal-delivery draft.", input.CustomerId));
    }
}
