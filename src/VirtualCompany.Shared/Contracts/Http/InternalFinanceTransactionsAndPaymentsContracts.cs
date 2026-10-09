using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record UpdateFinanceTransactionCategoryRequest(string Category)
{
    public string Category { get; set; } = Category;

    public UpdateFinanceTransactionCategoryRequest() : this(string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CreateFinancePaymentRequest(string PaymentType, decimal Amount, string Currency, DateTime PaymentDate, string Method, string Status, string CounterpartyReference)
{
    public string PaymentType { get; set; } = PaymentType;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public DateTime PaymentDate { get; set; } = PaymentDate;
    public string Method { get; set; } = Method;
    public string Status { get; set; } = Status;
    public string CounterpartyReference { get; set; } = CounterpartyReference;

    public CreateFinancePaymentRequest() : this(string.Empty, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty)
    {
    }
}
