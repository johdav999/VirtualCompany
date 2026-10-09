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




public sealed class ManualJournalPreviewRequest
{
    public long ExpectedVersion { get; set; }
}




public sealed class ManualJournalVersionedActionRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}




public sealed class ManualJournalLineRequest
{
    public Guid FinanceAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
    public Guid? CostCenterId { get; set; }
    public Dictionary<string, string>? TaxFacts { get; set; }
    public Dictionary<string, string>? DimensionFacts { get; set; }
}




public sealed class ManualJournalSourceReferenceRequest
{
    public string SourceType { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public string SourceVersion { get; set; } = string.Empty;
}


public sealed class SaveManualJournalDraftRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public List<ManualJournalLineRequest>? Lines { get; set; } = [];
    public List<Guid>? EvidenceDocumentIds { get; set; } = [];
    public Guid? OriginalLedgerEntryId { get; set; }
    public string? CorrectionReason { get; set; }
    public List<ManualJournalSourceReferenceRequest>? SourceRecords { get; set; }
}
