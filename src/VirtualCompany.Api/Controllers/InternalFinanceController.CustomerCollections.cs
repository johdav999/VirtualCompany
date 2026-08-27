using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/receivables/aging")]
    public Task<ActionResult<CustomerAgingResultDto>> GetCustomerAgingAsync(Guid companyId,
        [FromServices] ICustomerCollectionsService collections, [FromQuery] DateOnly cutoffDate,
        [FromQuery] string timeZoneId = "UTC", [FromQuery] Guid? customerId = null,
        [FromQuery] string? currency = null, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) => ExecuteReadAsync(() => collections.GetAgingAsync(
            new(companyId, cutoffDate, timeZoneId, customerId, currency, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-statements")]
    public Task<ActionResult<CustomerStatementDto>> GenerateCustomerStatementAsync(Guid companyId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] GenerateCustomerStatementRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.GenerateStatementAsync(new(companyId,
            request.CustomerId, request.FromDate, request.CutoffDate, request.TimeZoneId, request.Locale, request.Currency,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-statements")]
    public Task<ActionResult<CustomerStatementListResult>> ListCustomerStatementsAsync(Guid companyId,
        [FromServices] ICustomerCollectionsService collections, [FromQuery] Guid? customerId = null,
        [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        ExecuteReadAsync(() => collections.ListStatementsAsync(new(companyId, customerId, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-statements/{statementId:guid}")]
    public Task<ActionResult<CustomerStatementDto>> GetCustomerStatementAsync(Guid companyId, Guid statementId,
        [FromServices] ICustomerCollectionsService collections, CancellationToken cancellationToken) =>
        ExecuteReadAsync(() => collections.GetStatementAsync(new(companyId, statementId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-statements/{statementId:guid}/download")]
    public async Task<IActionResult> DownloadCustomerStatementAsync(Guid companyId, Guid statementId,
        [FromServices] ICustomerCollectionsService collections, CancellationToken cancellationToken)
    {
        try
        {
            var artifact = await collections.OpenStatementAsync(companyId, statementId, cancellationToken);
            return File(artifact.Content, artifact.MediaType, artifact.FileName);
        }
        catch (CustomerCollectionException ex) { return CreateCustomerCollectionErrorResult<object>(ex).Result!; }
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-collections/policy")]
    public async Task<ActionResult<CustomerCollectionPolicyDto>> GetCustomerCollectionPolicyAsync(Guid companyId,
        [FromServices] ICustomerCollectionsService collections, CancellationToken cancellationToken)
    {
        var policy = await collections.GetPolicyAsync(companyId, cancellationToken); return policy is null ? NotFound() : Ok(policy);
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/customer-collections/policy")]
    public Task<ActionResult<CustomerCollectionPolicyDto>> UpsertCustomerCollectionPolicyAsync(Guid companyId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] UpsertCustomerCollectionPolicyRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.UpsertPolicyAsync(new(companyId,
            request.ExpectedVersion, request.GracePeriodDays, request.MaterialityThreshold, request.DefaultLocale,
            request.RequireApproval, request.FeesEnabled, request.InterestEnabled,
            (request.Stages ?? []).Select(x => new CustomerCollectionPolicyStageInput(x.Stage, x.DaysAfterDue, x.Channel, x.TemplateKey, x.RequiresApproval)).ToArray(),
            RequiredActor(), ResolveCorrelationId(),
            (request.CustomerExceptions ?? []).Select(x => new CustomerCollectionPolicyExceptionInput(x.CustomerId, x.Reason, x.ExcludedUntilDate)).ToArray()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-collections/cases")]
    public Task<ActionResult<CustomerCollectionCaseListResult>> ListCustomerCollectionCasesAsync(Guid companyId,
        [FromServices] ICustomerCollectionsService collections, [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? invoiceId = null, [FromQuery] string? status = null, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, CancellationToken cancellationToken = default) => ExecuteReadAsync(() =>
        collections.ListCasesAsync(new(companyId, customerId, invoiceId, status, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoices/{invoiceId:guid}/collection-disputes")]
    public Task<ActionResult<CustomerCollectionCaseDto>> RecordCustomerDisputeAsync(Guid companyId, Guid invoiceId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] RecordCustomerDisputeRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.RecordDisputeAsync(new(companyId,
            invoiceId, request.Amount, request.Reason, request.OwnerUserId, request.FollowUpDueUtc, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-collections/cases/{caseId:guid}/resolve-dispute")]
    public Task<ActionResult<CustomerCollectionCaseDto>> ResolveCustomerDisputeAsync(Guid companyId, Guid caseId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] ResolveCustomerCollectionIssueRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.ResolveDisputeAsync(new(companyId,
            caseId, request.ExpectedVersion, request.Resolution, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoices/{invoiceId:guid}/promises-to-pay")]
    public Task<ActionResult<CustomerCollectionCaseDto>> RecordPromiseToPayAsync(Guid companyId, Guid invoiceId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] RecordPromiseToPayRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.RecordPromiseAsync(new(companyId,
            invoiceId, request.Amount, request.DueDate, request.OwnerUserId, request.FollowUpDueUtc, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-collections/cases/{caseId:guid}/resolve-promise")]
    public Task<ActionResult<CustomerCollectionCaseDto>> ResolvePromiseToPayAsync(Guid companyId, Guid caseId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] ResolvePromiseToPayRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.ResolvePromiseAsync(new(companyId,
            caseId, request.ExpectedVersion, request.Kept, request.Resolution, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-collections/cases/{caseId:guid}/responses")]
    public Task<ActionResult<CustomerCollectionCaseDto>> RecordCustomerCollectionResponseAsync(Guid companyId, Guid caseId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] RecordCustomerCollectionResponseRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.RecordResponseAsync(new(companyId,
            caseId, request.ExpectedVersion, request.ResponseType, request.Summary, request.OwnerUserId,
            request.FollowUpDueUtc, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoices/{invoiceId:guid}/reminders")]
    public Task<ActionResult<CustomerReminderDraftDto>> PrepareCustomerReminderAsync(Guid companyId, Guid invoiceId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] PrepareCustomerReminderRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.PrepareReminderAsync(new(companyId,
            invoiceId, request.RequestedStage, request.StatementId, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-reminders/{reminderDraftId:guid}/send")]
    public Task<ActionResult<CustomerReminderDeliveryDto>> SendCustomerReminderAsync(Guid companyId, Guid reminderDraftId,
        [FromServices] ICustomerCollectionsService collections, [FromBody] SendCustomerReminderRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => collections.SendReminderAsync(new(companyId,
            reminderDraftId, request.ExpectedDraftVersion, request.ExpectedSourceHash, request.IdempotencyKey,
            RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-collections/metrics")]
    public Task<ActionResult<CustomerCollectionMetricsDto>> GetCustomerCollectionMetricsAsync(Guid companyId,
        [FromServices] ICustomerCollectionsService collections, [FromQuery] DateOnly asOfDate,
        [FromQuery] int lookbackDays = 90, [FromQuery] string? currency = null,
        CancellationToken cancellationToken = default) => ExecuteReadAsync(() =>
        collections.GetMetricsAsync(new(companyId, asOfDate, lookbackDays, currency), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-collections/worker/run")]
    public Task<ActionResult<CustomerCollectionWorkerResult>> RunCustomerCollectionWorkerAsync(Guid companyId,
        [FromServices] ICustomerCollectionWorkerRunner runner, [FromBody] RunCustomerCollectionWorkerRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => runner.RunAsync(new(request.AsOfUtc ?? DateTime.UtcNow,
            request.BatchSize, companyId, request.ResetBlockedLease), cancellationToken));
}

public sealed class GenerateCustomerStatementRequest { public Guid CustomerId { get; set; } public DateOnly FromDate { get; set; } public DateOnly CutoffDate { get; set; } public string TimeZoneId { get; set; } = "UTC"; public string Locale { get; set; } = "en-US"; public string Currency { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class UpsertCustomerCollectionPolicyRequest { public long? ExpectedVersion { get; set; } public int GracePeriodDays { get; set; } public decimal MaterialityThreshold { get; set; } public string DefaultLocale { get; set; } = "en-US"; public bool RequireApproval { get; set; } = true; public bool FeesEnabled { get; set; } public bool InterestEnabled { get; set; } public List<CustomerCollectionPolicyStageRequest>? Stages { get; set; } = []; public List<CustomerCollectionPolicyExceptionRequest>? CustomerExceptions { get; set; } = []; }
public sealed class CustomerCollectionPolicyStageRequest { public int Stage { get; set; } public int DaysAfterDue { get; set; } public string Channel { get; set; } = "email"; public string TemplateKey { get; set; } = string.Empty; public bool RequiresApproval { get; set; } = true; }
public sealed class CustomerCollectionPolicyExceptionRequest { public Guid CustomerId { get; set; } public string Reason { get; set; } = string.Empty; public DateOnly? ExcludedUntilDate { get; set; } }
public sealed class RecordCustomerDisputeRequest { public decimal Amount { get; set; } public string Reason { get; set; } = string.Empty; public Guid? OwnerUserId { get; set; } public DateTime? FollowUpDueUtc { get; set; } public long? ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class ResolveCustomerCollectionIssueRequest { public long ExpectedVersion { get; set; } public string Resolution { get; set; } = string.Empty; }
public sealed class RecordPromiseToPayRequest { public decimal Amount { get; set; } public DateOnly DueDate { get; set; } public Guid? OwnerUserId { get; set; } public DateTime? FollowUpDueUtc { get; set; } public long? ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class ResolvePromiseToPayRequest { public long ExpectedVersion { get; set; } public bool Kept { get; set; } public string Resolution { get; set; } = string.Empty; }
public sealed class RecordCustomerCollectionResponseRequest { public long ExpectedVersion { get; set; } public string ResponseType { get; set; } = string.Empty; public string Summary { get; set; } = string.Empty; public Guid? OwnerUserId { get; set; } public DateTime? FollowUpDueUtc { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class PrepareCustomerReminderRequest { public int? RequestedStage { get; set; } public Guid? StatementId { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class SendCustomerReminderRequest { public long ExpectedDraftVersion { get; set; } public string ExpectedSourceHash { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class RunCustomerCollectionWorkerRequest { public DateTime? AsOfUtc { get; set; } public int BatchSize { get; set; } = 100; public bool ResetBlockedLease { get; set; } }
