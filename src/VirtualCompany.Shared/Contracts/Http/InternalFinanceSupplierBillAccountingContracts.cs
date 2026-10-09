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
public class SupplierBillAccountingRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = "G";
    public decimal? ExchangeRate { get; set; }
    public List<SupplierBillAccountingLineRequest> Lines { get; set; } = [];
}

public sealed class CreateNativeSupplierCreditNoteRequest
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public SupplierBillAccountingRequest Accounting { get; set; } = new();
}

public sealed class PostSupplierBillAccountingRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SubmitSupplierBillAccountingRequest : SupplierBillAccountingRequest
{
    public long? ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SupplierBillAccountingLineRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CostAccountId { get; set; }
    public string TaxRuleKey { get; set; } = string.Empty;
    public string? LineClassification { get; set; }
    public string? CounterpartyJurisdiction { get; set; }
    public string? CounterpartyVatStatus { get; set; }
    public List<AccountingTaxEvidenceRequest> TaxEvidence { get; set; } = [];
}
