using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpPost("accounting/statutory-documents/preview")]
    public async Task<ActionResult<StatutoryDocumentPolicyDecisionDto>> PreviewStatutoryDocumentAsync(
        Guid companyId, [FromBody] StatutoryDocumentRequest request, [FromServices] IStatutoryDocumentService service,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.PreviewAsync(new(companyId, request.ToInput()), cancellationToken));

    [HttpGet("accounting/statutory-document-series")]
    public async Task<ActionResult<IReadOnlyList<StatutoryDocumentSeriesDto>>> ListStatutoryDocumentSeriesAsync(
        Guid companyId, [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.ListSeriesAsync(companyId, cancellationToken));

    [HttpPost("accounting/statutory-document-series")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryDocumentSeriesDto>> CreateStatutoryDocumentSeriesAsync(
        Guid companyId, [FromBody] CreateStatutoryDocumentSeriesRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.CreateSeriesAsync(new(companyId, request.Code, request.DocumentType,
            request.FiscalYearStart, request.FiscalYearEnd, request.Prefix, request.NumberWidth,
            request.FirstNumber, ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPut("accounting/statutory-document-series/{seriesId:guid}")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryDocumentSeriesDto>> UpdateStatutoryDocumentSeriesAsync(
        Guid companyId, Guid seriesId, [FromBody] UpdateStatutoryDocumentSeriesRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.UpdateSeriesAsync(new(companyId, seriesId, request.ExpectedVersion,
            request.Prefix, request.NumberWidth, request.IsActive, ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpGet("accounting/statutory-document-allocations")]
    public async Task<ActionResult<IReadOnlyList<StatutoryDocumentAllocationDto>>> ListStatutoryDocumentAllocationsAsync(
        Guid companyId, [FromQuery] Guid? seriesId, [FromServices] IStatutoryDocumentService service,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.ListAllocationsAsync(companyId, seriesId, cancellationToken));

    [HttpPost("accounting/statutory-document-series/{seriesId:guid}/gaps")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryDocumentAllocationDto>> RecordStatutoryDocumentGapAsync(
        Guid companyId, Guid seriesId, [FromBody] RecordStatutoryDocumentGapRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.RecordGapAsync(new(companyId, seriesId, request.BusinessKey,
            request.SourceVersion, request.Reason, ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPost("accounting/statutory-documents/issue-native")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> IssueNativeStatutoryDocumentAsync(
        Guid companyId, [FromBody] IssueNativeStatutoryDocumentRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.IssueNativeCustomerAsync(new(companyId, request.SeriesId,
            request.BusinessKey, request.Document.ToInput(), ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPost("accounting/statutory-documents/register-imported")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> RegisterImportedStatutoryDocumentAsync(
        Guid companyId, [FromBody] RegisterImportedStatutoryDocumentRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.RegisterImportedAsync(new(companyId, request.SourceRecordId,
            request.BusinessKey, request.Document.ToInput(), ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpGet("accounting/statutory-documents/{issuedDocumentId:guid}")]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> GetIssuedStatutoryDocumentAsync(
        Guid companyId, Guid issuedDocumentId, [FromServices] IStatutoryDocumentService service,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.GetIssuedAsync(companyId, issuedDocumentId, cancellationToken));

    [HttpPost("accounting/statutory-documents/{issuedDocumentId:guid}/evidence")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> AttachStatutoryDocumentEvidenceAsync(
        Guid companyId, Guid issuedDocumentId, [FromBody] AttachStatutoryDocumentEvidenceRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.AttachEvidenceAsync(new(companyId, issuedDocumentId,
            request.ExpectedEvidenceVersion, request.RenderedEvidenceReference, request.DeliveryEvidenceReference,
            ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));
}

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
    public StatutoryDocumentInput ToInput() => new(DocumentType, Authority, CounterpartyId, CounterpartyLegalName,
        CounterpartyAddressLine1, CounterpartyPostalCode, CounterpartyCity, CounterpartyCountryCode, CounterpartyVatIdentifier, IssueDate,
        SupplyDate, AccountingDate, DueDate, Currency, PaymentTerms, ExplanatoryText, NetTotal, VatTotal,
        GrossTotal, Lines.Select(x => new StatutoryDocumentLineInput(x.Description, x.Quantity, x.UnitPrice,
            x.NetAmount, x.VatRate, x.VatAmount)).ToArray(), OriginalIssuedDocumentId,
        ProviderDocumentNumber, TaxFactsJson, ApprovalIds, SourceVersion);
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
public sealed class UpdateStatutoryDocumentSeriesRequest
{
    public long ExpectedVersion { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int NumberWidth { get; set; } = 6;
    public bool IsActive { get; set; } = true;
}
public sealed class RecordStatutoryDocumentGapRequest
{
    public string BusinessKey { get; set; } = string.Empty;
    public long SourceVersion { get; set; } = 1;
    public string Reason { get; set; } = string.Empty;
}
public sealed class IssueNativeStatutoryDocumentRequest
{
    public Guid SeriesId { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public StatutoryDocumentRequest Document { get; set; } = new();
}
public sealed class RegisterImportedStatutoryDocumentRequest
{
    public Guid SourceRecordId { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public StatutoryDocumentRequest Document { get; set; } = new();
}
public sealed class AttachStatutoryDocumentEvidenceRequest
{
    public long ExpectedEvidenceVersion { get; set; }
    public string? RenderedEvidenceReference { get; set; }
    public string? DeliveryEvidenceReference { get; set; }
}
