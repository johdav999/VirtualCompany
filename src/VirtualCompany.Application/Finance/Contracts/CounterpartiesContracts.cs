using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceCounterpartyQuery(
    Guid CompanyId,
    Guid CounterpartyId,
    string CounterpartyType);

public sealed record CreateFinanceCounterpartyCommand(
    Guid CompanyId,
    string CounterpartyType,
    FinanceCounterpartyUpsertDto Counterparty);

public sealed record UpdateFinanceCounterpartyCommand(
    Guid CompanyId,
    Guid CounterpartyId,
    string CounterpartyType,
    FinanceCounterpartyUpsertDto Counterparty);

public sealed record FinanceOverdueCustomerRiskInsightDto(
    int OverdueInvoiceCount,
    int OverdueCustomerCount,
    decimal TotalOverdueAmount,
    int MaxDaysOverdue,
    decimal LargestCustomerConcentrationRatio,
    string RiskLabel,
    string Summary,
    IReadOnlyList<FinanceOverdueCustomerRiskItemDto> Customers);

public sealed record FinanceOverdueCustomerRiskItemDto(
    Guid CounterpartyId,
    string CustomerName,
    decimal OverdueAmount,
    int OverdueInvoiceCount,
    int MaxDaysOverdue,
    decimal ConcentrationRatio,
    string RiskLabel,
    string Summary);

public sealed record FinanceCounterpartyDto(
    Guid Id,
    Guid CompanyId,
    string CounterpartyType,
    string Name,
    string? Email,
    string? PaymentTerms,
    string? TaxId,
    decimal? CreditLimit,
    string? PreferredPaymentMethod,
    string? DefaultAccountMapping,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record FinanceCounterpartyUpsertDto(
    string Name,
    string? Email,
    string? PaymentTerms,
    string? TaxId,
    decimal? CreditLimit,
    string? PreferredPaymentMethod,
    string? DefaultAccountMapping);

