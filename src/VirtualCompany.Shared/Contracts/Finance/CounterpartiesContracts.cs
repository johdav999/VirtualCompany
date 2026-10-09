using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record FinanceCounterpartyDto(Guid Id, Guid CompanyId, string CounterpartyType, string Name, string? Email, string? PaymentTerms, string? TaxId, decimal? CreditLimit, string? PreferredPaymentMethod, string? DefaultAccountMapping, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string CounterpartyType { get; set; } = CounterpartyType;
    public string Name { get; set; } = Name;
    public string? Email { get; set; } = Email;
    public string? PaymentTerms { get; set; } = PaymentTerms;
    public string? TaxId { get; set; } = TaxId;
    public decimal? CreditLimit { get; set; } = CreditLimit;
    public string? PreferredPaymentMethod { get; set; } = PreferredPaymentMethod;
    public string? DefaultAccountMapping { get; set; } = DefaultAccountMapping;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public FinanceCounterpartyDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}
