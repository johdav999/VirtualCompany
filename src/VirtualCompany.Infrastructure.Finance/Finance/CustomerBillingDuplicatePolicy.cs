using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

internal static class CustomerBillingDuplicatePolicy
{
    public const int CandidateThreshold = 70;

    public static (int Score, IReadOnlyList<CustomerDuplicateEvidenceDto> Evidence) Evaluate(
        CustomerBillingProfile left,
        CustomerBillingProfile right)
    {
        var evidence = new List<CustomerDuplicateEvidenceDto>();
        AddExact(evidence, left.NormalizedTaxIdentifier, right.NormalizedTaxIdentifier, "tax_identity", "The normalized tax identifiers match.", 100);
        AddExact(evidence, left.NormalizedVatIdentifier, right.NormalizedVatIdentifier, "vat_identity", "The normalized VAT identifiers match.", 100);
        AddExact(evidence, left.NormalizedEInvoiceIdentifier, right.NormalizedEInvoiceIdentifier, "e_invoice_identity", "The normalized e-invoice identifiers match.", 90);
        AddExact(evidence, left.NormalizedInvoiceDeliveryEmail, right.NormalizedInvoiceDeliveryEmail, "invoice_email", "The normalized invoice delivery emails match.", 45);
        AddExact(evidence, left.NormalizedLegalName, right.NormalizedLegalName, "legal_name", "The normalized legal names match.", 30);
        AddExact(evidence, left.NormalizedBillingAddress, right.NormalizedBillingAddress, "billing_address", "The normalized billing addresses match.", 35);
        return (Math.Min(100, evidence.Sum(item => item.Weight)), evidence);
    }

    public static string SerializeEvidence(IReadOnlyList<CustomerDuplicateEvidenceDto> evidence) =>
        JsonSerializer.Serialize(evidence, CustomerBillingProfileService.SerializerOptions);

    private static void AddExact(List<CustomerDuplicateEvidenceDto> evidence, string? left, string? right,
        string fact, string explanation, int weight)
    {
        if (!string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.Ordinal))
            evidence.Add(new CustomerDuplicateEvidenceDto(fact, explanation, weight));
    }
}
