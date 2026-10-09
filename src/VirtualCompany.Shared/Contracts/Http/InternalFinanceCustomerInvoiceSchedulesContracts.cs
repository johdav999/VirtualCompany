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
public sealed class CustomerInvoiceScheduleActionRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool AllowBackdatedGeneration { get; set; }
    public bool RetryBlockedOccurrence { get; set; }

    public CustomerInvoiceScheduleActionRequest()
    {
    }

    public CustomerInvoiceScheduleActionRequest(long ExpectedVersion, string IdempotencyKey, bool AllowBackdatedGeneration = false, bool RetryBlockedOccurrence = false)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.IdempotencyKey = IdempotencyKey;
        this.AllowBackdatedGeneration = AllowBackdatedGeneration;
        this.RetryBlockedOccurrence = RetryBlockedOccurrence;
    }
}

public sealed class CustomerInvoiceScheduleLineRequest
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

    public CustomerInvoiceScheduleLineRequest()
    {
    }

    public CustomerInvoiceScheduleLineRequest(int Sequence, string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountPercent, string TaxRuleKey, string TaxClassification, IReadOnlyList<VirtualCompany.Api.Controllers.CustomerInvoiceDraftTaxEvidenceRequest> TaxEvidence, IReadOnlyDictionary<string, string>? DimensionFacts = null, string? RevenueAccountRoleKey = null, string? SourceReference = null, string? OrderReference = null)
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

public sealed class SaveCustomerInvoiceScheduleRequest
{
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Cadence { get; set; } = "monthly";
    public int BillingDay { get; set; }
    public string TimeZoneId { get; set; } = "Europe/Stockholm";
    public string BusinessDayConvention { get; set; } = "calendar";
    public string ProrationRule { get; set; } = "none";
    public int DueDateOffsetDays { get; set; }
    public string DocumentType { get; set; } = "invoice";
    public string Currency { get; set; } = string.Empty;
    public string PaymentTermKind { get; set; } = "net";
    public int PaymentTermDays { get; set; }
    public string? BuyerReference { get; set; }
    public string? SellerReference { get; set; }
    public string? Notes { get; set; }
    public string DeliveryIntent { get; set; } = "email";
    public bool AutoIssueEnabled { get; set; }
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<CustomerInvoiceScheduleLineRequest>? Lines { get; set; } = [];
    public List<Guid>? EvidenceDocumentIds { get; set; } = [];

    public SaveCustomerInvoiceScheduleRequest()
    {
    }

    public SaveCustomerInvoiceScheduleRequest(long ExpectedVersion, string IdempotencyKey, Guid CustomerId, string Name, DateOnly StartDate, DateOnly? EndDate, string Cadence, int BillingDay, string TimeZoneId, string BusinessDayConvention, string ProrationRule, int DueDateOffsetDays, string DocumentType, string Currency, string PaymentTermKind, int PaymentTermDays, string? BuyerReference, string? SellerReference, string? Notes, string DeliveryIntent, bool AutoIssueEnabled, IReadOnlyList<VirtualCompany.Api.Controllers.CustomerInvoiceScheduleLineRequest> Lines, IReadOnlyList<Guid> EvidenceDocumentIds)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.IdempotencyKey = IdempotencyKey;
        this.CustomerId = CustomerId;
        this.Name = Name;
        this.StartDate = StartDate;
        this.EndDate = EndDate;
        this.Cadence = Cadence;
        this.BillingDay = BillingDay;
        this.TimeZoneId = TimeZoneId;
        this.BusinessDayConvention = BusinessDayConvention;
        this.ProrationRule = ProrationRule;
        this.DueDateOffsetDays = DueDateOffsetDays;
        this.DocumentType = DocumentType;
        this.Currency = Currency;
        this.PaymentTermKind = PaymentTermKind;
        this.PaymentTermDays = PaymentTermDays;
        this.BuyerReference = BuyerReference;
        this.SellerReference = SellerReference;
        this.Notes = Notes;
        this.DeliveryIntent = DeliveryIntent;
        this.AutoIssueEnabled = AutoIssueEnabled;
        this.Lines = Lines.ToList();
        this.EvidenceDocumentIds = EvidenceDocumentIds.ToList();
    }
}
