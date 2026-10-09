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
public sealed class StatutoryDocumentRequest
{
    public string DocumentType { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public Guid CounterpartyId { get; set; }
    public string CounterpartyLegalName { get; set; } = string.Empty;
    public string CounterpartyAddressLine1 { get; set; } = string.Empty;
    public string CounterpartyPostalCode { get; set; } = string.Empty;
    public string CounterpartyCity { get; set; } = string.Empty;
    public string CounterpartyCountryCode { get; set; } = string.Empty;
    public string? CounterpartyVatIdentifier { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly SupplyDate { get; set; }
    public DateOnly AccountingDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public string ExplanatoryText { get; set; } = string.Empty;
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public List<StatutoryDocumentLineRequest> Lines { get; set; } = [];
    public Guid? OriginalIssuedDocumentId { get; set; }
    public string? ProviderDocumentNumber { get; set; }
    public string? TaxFactsJson { get; set; }
    public List<Guid> ApprovalIds { get; set; } = [];
    public long SourceVersion { get; set; } = 1;
}

public sealed class AttachStatutoryDocumentEvidenceRequest
{
    public long ExpectedEvidenceVersion { get; set; }
    public string? RenderedEvidenceReference { get; set; }
    public string? DeliveryEvidenceReference { get; set; }
}

public sealed class RegisterImportedStatutoryDocumentRequest
{
    public Guid SourceRecordId { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public StatutoryDocumentRequest Document { get; set; } = new();
}

public sealed class IssueNativeStatutoryDocumentRequest
{
    public Guid SeriesId { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public StatutoryDocumentRequest Document { get; set; } = new();
}

public sealed class RecordStatutoryDocumentGapRequest
{
    public string BusinessKey { get; set; } = string.Empty;
    public long SourceVersion { get; set; } = 1;
    public string Reason { get; set; } = string.Empty;
}

public sealed class UpdateStatutoryDocumentSeriesRequest
{
    public long ExpectedVersion { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int NumberWidth { get; set; } = 6;
    public bool IsActive { get; set; } = true;
}

public sealed class CreateStatutoryDocumentSeriesRequest
{
    public string Code { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int NumberWidth { get; set; } = 6;
    public long FirstNumber { get; set; } = 1;
}

public sealed class StatutoryDocumentLineRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
}
