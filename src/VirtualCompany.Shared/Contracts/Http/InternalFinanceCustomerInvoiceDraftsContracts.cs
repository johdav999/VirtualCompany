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
public sealed class CustomerInvoiceDraftTaxEvidenceRequest
{
    public string Classification { get; set; } = string.Empty;
    public string? SourceReference { get; set; }

    public CustomerInvoiceDraftTaxEvidenceRequest()
    {
    }

    public CustomerInvoiceDraftTaxEvidenceRequest(string Classification, string? SourceReference = null)
    {
        this.Classification = Classification;
        this.SourceReference = SourceReference;
    }
}

public sealed class IssueCustomerInvoiceDraftRequest : CustomerInvoiceDraftVersionedActionRequest
{
    public string ExpectedResultHash { get; set; } = string.Empty;
    public Guid SeriesId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public DateOnly AccountingDate { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;

    public IssueCustomerInvoiceDraftRequest()
    {
    }

    public IssueCustomerInvoiceDraftRequest(long ExpectedVersion, string IdempotencyKey, string ExpectedResultHash, Guid SeriesId, Guid FiscalPeriodId, DateOnly AccountingDate, string VoucherSeriesCode)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.IdempotencyKey = IdempotencyKey;
        this.ExpectedResultHash = ExpectedResultHash;
        this.SeriesId = SeriesId;
        this.FiscalPeriodId = FiscalPeriodId;
        this.AccountingDate = AccountingDate;
        this.VoucherSeriesCode = VoucherSeriesCode;
    }
}

public class CustomerInvoiceDraftVersionedActionRequest : CustomerInvoiceDraftVersionRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;

    public CustomerInvoiceDraftVersionedActionRequest()
    {
    }

    public CustomerInvoiceDraftVersionedActionRequest(long ExpectedVersion, string IdempotencyKey)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.IdempotencyKey = IdempotencyKey;
    }
}

public class CustomerInvoiceDraftVersionRequest
{
    public long ExpectedVersion { get; set; }
}

public sealed class CopyCustomerInvoiceDraftRequest : CustomerInvoiceDraftVersionedActionRequest
{
    public DateOnly IssueDate { get; set; }

    public CopyCustomerInvoiceDraftRequest()
    {
    }

    public CopyCustomerInvoiceDraftRequest(long ExpectedVersion, string IdempotencyKey, DateOnly IssueDate)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.IdempotencyKey = IdempotencyKey;
        this.IssueDate = IssueDate;
    }
}

public sealed class CustomerInvoiceDraftLineRequest
{
    public int Sequence { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public string TaxRuleKey { get; set; } = string.Empty;
    public string TaxClassification { get; set; } = string.Empty;
    public List<CustomerInvoiceDraftTaxEvidenceRequest>? TaxEvidence { get; set; } = [];
    public Dictionary<string, string>? DimensionFacts { get; set; }
    public string? RevenueAccountRoleKey { get; set; }
    public string? SourceReference { get; set; }
    public string? OrderReference { get; set; }

    public CustomerInvoiceDraftLineRequest()
    {
    }

    public CustomerInvoiceDraftLineRequest(int Sequence, string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountPercent, string TaxRuleKey, string TaxClassification, IReadOnlyList<VirtualCompany.Api.Controllers.CustomerInvoiceDraftTaxEvidenceRequest> TaxEvidence, IReadOnlyDictionary<string, string>? DimensionFacts = null, string? RevenueAccountRoleKey = null, string? SourceReference = null, string? OrderReference = null)
    {
        this.Sequence = Sequence;
        this.Description = Description;
        this.Quantity = Quantity;
        this.Unit = Unit;
        this.UnitPrice = UnitPrice;
        this.DiscountPercent = DiscountPercent;
        this.TaxRuleKey = TaxRuleKey;
        this.TaxClassification = TaxClassification;
        this.TaxEvidence = TaxEvidence.ToList();
        this.DimensionFacts = DimensionFacts is null ? null : new Dictionary<string, string>(DimensionFacts);
        this.RevenueAccountRoleKey = RevenueAccountRoleKey;
        this.SourceReference = SourceReference;
        this.OrderReference = OrderReference;
    }
}

public sealed class SaveCustomerInvoiceDraftRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public DateOnly SupplyDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentTermKind { get; set; } = string.Empty;
    public int PaymentTermDays { get; set; }
    public string? BuyerReference { get; set; }
    public string? SellerReference { get; set; }
    public string? Notes { get; set; }
    public string DeliveryIntent { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    public List<CustomerInvoiceDraftLineRequest>? Lines { get; set; } = [];
    public List<Guid>? EvidenceDocumentIds { get; set; } = [];

    public SaveCustomerInvoiceDraftRequest()
    {
    }

    public SaveCustomerInvoiceDraftRequest(long ExpectedVersion, string IdempotencyKey, Guid CustomerId, string DocumentType, DateOnly IssueDate, DateOnly SupplyDate, DateOnly DueDate, string Currency, string PaymentTermKind, int PaymentTermDays, string? BuyerReference, string? SellerReference, string? Notes, string DeliveryIntent, string SourceKind, string? SourceReference, IReadOnlyList<VirtualCompany.Api.Controllers.CustomerInvoiceDraftLineRequest> Lines, IReadOnlyList<Guid> EvidenceDocumentIds, Guid? OriginalInvoiceId = null)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.IdempotencyKey = IdempotencyKey;
        this.CustomerId = CustomerId;
        this.DocumentType = DocumentType;
        this.IssueDate = IssueDate;
        this.SupplyDate = SupplyDate;
        this.DueDate = DueDate;
        this.Currency = Currency;
        this.PaymentTermKind = PaymentTermKind;
        this.PaymentTermDays = PaymentTermDays;
        this.BuyerReference = BuyerReference;
        this.SellerReference = SellerReference;
        this.Notes = Notes;
        this.DeliveryIntent = DeliveryIntent;
        this.SourceKind = SourceKind;
        this.SourceReference = SourceReference;
        this.Lines = Lines.ToList();
        this.EvidenceDocumentIds = EvidenceDocumentIds.ToList();
        this.OriginalInvoiceId = OriginalInvoiceId;
    }
}
