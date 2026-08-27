using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceDraftReadinessPolicy : ICustomerInvoiceDraftReadinessPolicy
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly ICustomerInvoiceDraftCalculationPolicy _calculationPolicy;

    public CustomerInvoiceDraftReadinessPolicy(VirtualCompanyDbContext dbContext,
        IAccountingPolicyPackResolver packResolver,
        ICustomerInvoiceDraftCalculationPolicy calculationPolicy)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _calculationPolicy = calculationPolicy;
    }

    public async Task<CustomerInvoiceDraftReadinessDto> EvaluateAsync(Guid companyId,
        CustomerInvoiceDraft draft, CancellationToken cancellationToken)
    {
        var blockers = ParseIssues(draft.BlockersJson).ToList();
        var warnings = ParseIssues(draft.WarningsJson).ToList();
        var customer = await _dbContext.FinanceCounterparties.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draft.CustomerId && x.CounterpartyType == "customer", cancellationToken);
        var profile = await _dbContext.CustomerBillingProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CounterpartyId == draft.CustomerId, cancellationToken);
        var statutory = await _dbContext.CompanyStatutoryProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var configuration = await _dbContext.AccountingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var financePolicy = await _dbContext.FinancePolicyConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (customer is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerNotFound,
                "The selected customer is not available.", draft.CustomerId));
        else if (customer.MergedIntoCounterpartyId.HasValue)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerMerged,
                "This customer was merged. Select the current customer record.", draft.CustomerId));
        if (profile is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerProfileMissing,
                "Complete the customer billing profile before requesting approval.", draft.CustomerId));
        else
        {
            if (draft.DocumentType != CustomerInvoiceDraftDocumentTypes.CreditNote &&
                profile.CreditStatus != CustomerBillingCreditStatuses.Active)
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerCreditHold,
                    "Customer credit control currently prevents invoice issue.", draft.CustomerId));
            if (profile.ConflictState != "clear")
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerConflict,
                    "Resolve the customer billing data conflict before invoice issue.", draft.CustomerId));
            if (profile.CurrencyCode != draft.Currency)
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.UnsupportedCurrency,
                    "The draft currency does not match the customer's supported billing currency.", draft.CustomerId));
            if (draft.DocumentType != CustomerInvoiceDraftDocumentTypes.CreditNote && profile.CreditLimit > 0m)
            {
                var outstanding = await _dbContext.FinanceInvoices.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.CounterpartyId == draft.CustomerId &&
                        x.Currency == draft.Currency && x.SettlementStatus != FinanceSettlementStatuses.Paid &&
                        x.SettlementStatus != FinanceSettlementStatuses.Credited)
                    .SumAsync(x => x.Amount - x.PaidAmount, cancellationToken);
                if (outstanding + draft.GrossTotal > profile.CreditLimit)
                    blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerCreditLimit,
                        $"This invoice would exceed the customer's {profile.CreditLimit:0.##} {draft.Currency} credit limit.", draft.CustomerId));
            }
        }

        if (draft.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote)
        {
            var originalExists = draft.OriginalInvoiceId.HasValue && await _dbContext.FinanceInvoices.AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == draft.OriginalInvoiceId &&
                    x.CounterpartyId == draft.CustomerId && x.DocumentKind == FinanceDocumentKinds.Invoice &&
                    x.Authority == StatutoryDocumentAuthorities.Native, cancellationToken);
            if (!originalExists)
                blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerNotFound,
                    "The original native customer invoice is unavailable for this credit note.", draft.OriginalInvoiceId));
        }

        if (statutory is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.StatutoryProfileMissing,
                "Complete the company statutory profile before invoice issue."));
        else if (!statutory.IsFormatComplete || !statutory.IsUserAttested ||
                 statutory.OrganisationRegistrationEffectiveFrom > draft.IssueDate ||
                 statutory.VatRegistrationEffectiveFrom > draft.IssueDate ||
                 statutory.VatRegistrationEffectiveTo < draft.IssueDate)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.StatutoryProfileIncomplete,
                "The company statutory profile is incomplete, not attested, or not effective on the invoice date."));

        if (configuration is null || !_packResolver.TryResolve(configuration.PolicyPackKey,
                configuration.PolicyPackVersion, out var currentPack) || currentPack is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.AccountingConfigurationMissing,
                "Accounting configuration is unavailable."));
        else if (draft.PolicyPackKey != currentPack.Definition.PackKey ||
                 draft.PolicyPackVersion != currentPack.Definition.Version ||
                 draft.PolicyDefinitionHash != currentPack.DefinitionHash)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CalculationStale,
                "The accounting policy changed after this draft was calculated. Save the draft again to refresh its tax preview."));

        var currentCalculation = await _calculationPolicy.CalculateAsync(companyId, ToInput(draft), cancellationToken);
        blockers.AddRange(currentCalculation.Blockers);
        var evidenceIdentity = string.Join('|', draft.EvidenceLinks.OrderBy(x => x.DocumentId)
            .Select(x => $"{x.DocumentId:N}:{x.ContentHash}"));
        var currentResultHash = HashText($"{currentCalculation.ResultHash}|{evidenceIdentity}");
        if (!string.Equals(currentResultHash, draft.ResultHash, StringComparison.OrdinalIgnoreCase))
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CalculationStale,
                "Customer, statutory, evidence, or accounting facts changed after this draft was calculated. Save the draft again to refresh its tax preview."));

        if (draft.Status == CustomerInvoiceDraftStatusValues.Discarded)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.NotEditable, "A discarded draft cannot be issued."));

        var approvalCurrent = draft.ApprovalRequest is not null && draft.ApprovalDraftVersion == draft.Version &&
            string.Equals(draft.ApprovalResultHash, draft.ResultHash, StringComparison.OrdinalIgnoreCase) &&
            ReadApprovalVersion(draft.ApprovalRequest) == draft.Version &&
            string.Equals(ReadApprovalHash(draft.ApprovalRequest), draft.ResultHash, StringComparison.OrdinalIgnoreCase);
        var approvalReason = CustomerInvoiceDraftReasonCodes.ApprovalRequired;
        var approvalExplanation = "Approval is required for the current saved invoice draft.";
        if (draft.ApprovalRequest is not null)
        {
            if (!approvalCurrent)
            {
                approvalReason = CustomerInvoiceDraftReasonCodes.ApprovalStale;
                approvalExplanation = "The approval does not match the current draft version and tax result.";
                blockers.Add(new(approvalReason, approvalExplanation));
            }
            else if (draft.ApprovalRequest.Status == ApprovalRequestStatus.Pending)
            {
                approvalReason = CustomerInvoiceDraftReasonCodes.ApprovalPending;
                approvalExplanation = "The invoice draft is waiting for approval.";
                blockers.Add(new(approvalReason, approvalExplanation, draft.ApprovalRequest.Id));
            }
            else if (draft.ApprovalRequest.Status != ApprovalRequestStatus.Approved)
            {
                approvalReason = CustomerInvoiceDraftReasonCodes.ApprovalRejected;
                approvalExplanation = "The invoice draft does not have a current approval.";
                blockers.Add(new(approvalReason, approvalExplanation, draft.ApprovalRequest.Id));
            }
        }
        else
        {
            blockers.Add(new(approvalReason, approvalExplanation));
        }

        blockers = blockers.GroupBy(x => new { x.ReasonCode, x.Explanation, x.RelatedEntityId })
            .Select(x => x.First()).ToList();
        var threshold = financePolicy?.InvoiceApprovalThreshold ?? 0m;
        var approvalCurrency = financePolicy?.ApprovalCurrency ?? configuration?.BaseCurrency ?? draft.Currency;
        var allowed = blockers.Count == 0 && approvalCurrent && draft.ApprovalRequest?.Status == ApprovalRequestStatus.Approved;
        var reason = allowed ? CustomerInvoiceDraftReasonCodes.Ready : blockers[0].ReasonCode;
        var explanation = allowed ? "The current draft version is approved and ready for the separate issue command." : blockers[0].Explanation;
        var evidence = new Dictionary<string, string>
        {
            ["draftVersion"] = draft.Version.ToString(CultureInfo.InvariantCulture),
            ["resultHash"] = draft.ResultHash,
            ["policyPack"] = $"{draft.PolicyPackKey}@{draft.PolicyPackVersion}",
            ["customerId"] = draft.CustomerId.ToString("N"),
            ["approvalRequestId"] = draft.ApprovalRequestId?.ToString("N") ?? string.Empty
        };
        return new(allowed, reason, explanation, true, threshold, approvalCurrency,
            blockers, warnings, evidence);
    }

    internal static IReadOnlyList<CustomerInvoiceDraftIssue> ParseIssues(string json) =>
        JsonSerializer.Deserialize<List<CustomerInvoiceDraftIssue>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    private static CustomerInvoiceDraftInput ToInput(CustomerInvoiceDraft draft) => new(draft.CustomerId,
        draft.DocumentType, draft.IssueDate, draft.SupplyDate, draft.DueDate, draft.Currency,
        draft.PaymentTermKind, draft.PaymentTermDays, draft.BuyerReference, draft.SellerReference,
        draft.Notes, draft.DeliveryIntent, draft.SourceKind, draft.SourceReference,
        draft.Lines.OrderBy(x => x.Sequence).Select(x => new CustomerInvoiceDraftLineInput(x.Sequence,
            x.Description, x.Quantity, x.Unit, x.UnitPrice, x.DiscountPercent, x.TaxRuleKey,
            x.TaxClassification,
            JsonSerializer.Deserialize<List<CustomerInvoiceDraftTaxEvidenceInput>>(x.TaxEvidenceJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [],
            JsonSerializer.Deserialize<Dictionary<string, string>>(x.DimensionFactsJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), x.RevenueAccountRoleKey,
            x.SourceReference, x.OrderReference)).ToArray(), draft.EvidenceLinks.Select(x => x.DocumentId).ToArray(),
        draft.OriginalInvoiceId);
    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static long ReadApprovalVersion(ApprovalRequest approval) =>
        approval.ThresholdContext.TryGetValue("sourceVersion", out var node) &&
        long.TryParse(node?.ToString().Trim('"'), CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static string? ReadApprovalHash(ApprovalRequest approval) =>
        approval.ThresholdContext.TryGetValue("resultHash", out var node) ? node?.ToString().Trim('"') : null;
}
