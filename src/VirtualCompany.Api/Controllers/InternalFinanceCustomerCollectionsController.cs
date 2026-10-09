using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceCustomerCollectionsController : InternalFinanceControllerBase
{

    public InternalFinanceCustomerCollectionsController(
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceCustomerCollectionsController> logger)
        : base(initializationProblems, logger)
    {
    }
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
