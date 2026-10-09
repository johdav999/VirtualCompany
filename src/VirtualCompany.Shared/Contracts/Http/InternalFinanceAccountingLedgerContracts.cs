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




public sealed class ReverseAccountingEntryRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
}




public sealed class ProposedAccountingLineRequest
{
    public Guid FinanceAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CostCenterId { get; set; }
    public Dictionary<string, string>? TaxFacts { get; set; }
    public Dictionary<string, string>? DimensionFacts { get; set; }
    public decimal? DocumentDebitAmount { get; set; }
    public decimal? DocumentCreditAmount { get; set; }
    public string? DocumentCurrency { get; set; }
    public decimal? ExchangeRate { get; set; }
    public DateOnly? ExchangeRateDate { get; set; }
    public Guid? ExchangeRateConversionId { get; set; }
    public string? ExchangeRateIdentity { get; set; }
    public decimal? ConversionRoundingResidual { get; set; }
    public List<Guid>? DimensionMemberIds { get; set; }
}


public sealed class ProposedAccountingEntryRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public string PostingType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<ProposedAccountingLineRequest> Lines { get; set; } = [];
    public Guid? ApprovalRequestId { get; set; }
    public bool RequiresApproval { get; set; }
    public Dictionary<string, string>? PolicyFacts { get; set; }
    public string Action { get; set; } = "post";
}
