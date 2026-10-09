using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record UpsertFinanceCounterpartyRequest(string Name, string? Email, string? PaymentTerms, string? TaxId, decimal? CreditLimit, string? PreferredPaymentMethod, string? DefaultAccountMapping)
{
    public string Name { get; set; } = Name;
    public string? Email { get; set; } = Email;
    public string? PaymentTerms { get; set; } = PaymentTerms;
    public string? TaxId { get; set; } = TaxId;
    public decimal? CreditLimit { get; set; } = CreditLimit;
    public string? PreferredPaymentMethod { get; set; } = PreferredPaymentMethod;
    public string? DefaultAccountMapping { get; set; } = DefaultAccountMapping;

    public UpsertFinanceCounterpartyRequest() : this(string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}
