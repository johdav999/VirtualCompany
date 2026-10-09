using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

internal static class CustomerInvoiceRequestMapping
{

    internal static CustomerInvoiceDraftInput MapInvoiceDraft(SaveCustomerInvoiceDraftRequest request) => new(
        request.CustomerId, request.DocumentType, request.IssueDate, request.SupplyDate, request.DueDate,
        request.Currency, request.PaymentTermKind, request.PaymentTermDays, request.BuyerReference,
        request.SellerReference, request.Notes, request.DeliveryIntent, request.SourceKind,
        request.SourceReference, (request.Lines ?? []).Select(line => new CustomerInvoiceDraftLineInput(
            line.Sequence, line.Description, line.Quantity, line.Unit, line.UnitPrice, line.DiscountPercent,
            line.TaxRuleKey, line.TaxClassification, (line.TaxEvidence ?? []).Select(evidence =>
                new CustomerInvoiceDraftTaxEvidenceInput(evidence.Classification, evidence.SourceReference)).ToArray(),
            line.DimensionFacts, line.RevenueAccountRoleKey, line.SourceReference, line.OrderReference)).ToArray(),
        request.EvidenceDocumentIds ?? [], request.OriginalInvoiceId);

}
