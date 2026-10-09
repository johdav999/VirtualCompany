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
public sealed class InternalFinanceComplianceObligationsController : InternalFinanceControllerBase
{

    public InternalFinanceComplianceObligationsController(
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceComplianceObligationsController> logger)
        : base(initializationProblems, logger)
    {
    }
    private IComplianceObligationService ComplianceObligations => HttpContext.RequestServices.GetRequiredService<IComplianceObligationService>();

    [Authorize(Policy=CompanyPolicies.AccountingView)]
    [HttpGet("accounting/compliance-obligations")]
    public Task<ActionResult<ComplianceCalendarDto>> GetComplianceCalendarAsync(Guid companyId,[FromQuery] DateOnly? from,[FromQuery] DateOnly? to,CancellationToken ct)
    { var start=from??new DateOnly(DateTime.UtcNow.Year,1,1);var end=to??start.AddYears(1).AddDays(-1);return ExecuteReadAsync(()=>ComplianceObligations.GetCalendarAsync(new(companyId,start,end),ct)); }

    [Authorize(Policy=CompanyPolicies.AccountingView)]
    [HttpGet("accounting/compliance-obligations/{instanceId:guid}")]
    public Task<ActionResult<ComplianceObligationDto>> GetComplianceObligationAsync(Guid companyId,Guid instanceId,CancellationToken ct)=>ExecuteReadAsync(()=>ComplianceObligations.GetAsync(companyId,instanceId,ct));

    [Authorize(Policy=CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/compliance-obligations/generate")]
    public Task<ActionResult<IReadOnlyList<ComplianceObligationDto>>> GenerateComplianceObligationsAsync(Guid companyId,[FromBody] GenerateComplianceRequest request,CancellationToken ct)=>ExecuteWriteAsync(()=>{var actor=ResolveActorId()??throw new UnauthorizedAccessException();return ComplianceObligations.GenerateAsync(new(companyId,request.OwnerUserId==Guid.Empty?actor:request.OwnerUserId,actor,request.IdempotencyKey),ct);});

    [Authorize(Policy=CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/compliance-obligations/{instanceId:guid}/transition")]
    public Task<ActionResult<ComplianceObligationDto>> TransitionComplianceObligationAsync(Guid companyId,Guid instanceId,[FromBody] ComplianceTransitionRequest request,CancellationToken ct)=>ExecuteWriteAsync(()=>ComplianceObligations.TransitionAsync(new(companyId,instanceId,request.Action,ResolveActorId()??throw new UnauthorizedAccessException(),request.IdempotencyKey,request.ExpectedVersion,request.Reason),ct));

    [Authorize(Policy=CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/compliance-obligations/{instanceId:guid}/decision")]
    public Task<ActionResult<ComplianceObligationDto>> DecideComplianceObligationAsync(Guid companyId,Guid instanceId,[FromBody] ComplianceDecisionRequest request,CancellationToken ct)=>ExecuteWriteAsync(()=>ComplianceObligations.TransitionAsync(new(companyId,instanceId,request.Approved?ComplianceObligationActions.Approve:ComplianceObligationActions.Reject,ResolveActorId()??throw new UnauthorizedAccessException(),request.IdempotencyKey,request.ExpectedVersion,request.Reason),ct));

    [Authorize(Policy=CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/compliance-obligations/{instanceId:guid}/manual-submission")]
    public Task<ActionResult<ComplianceObligationDto>> RecordComplianceManualSubmissionAsync(Guid companyId,Guid instanceId,[FromBody] ComplianceEvidenceRequest request,CancellationToken ct)=>ExecuteWriteAsync(()=>ComplianceObligations.RecordManualSubmissionAsync(new(companyId,instanceId,request.Reference,request.EvidenceHash,ResolveActorId()??throw new UnauthorizedAccessException(),request.IdempotencyKey,request.ExpectedVersion),ct));

    [Authorize(Policy=CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/compliance-obligations/{instanceId:guid}/acknowledgements")]
    public Task<ActionResult<ComplianceObligationDto>> RecordComplianceAcknowledgementAsync(Guid companyId,Guid instanceId,[FromBody] ComplianceAcknowledgementRequest request,CancellationToken ct)=>ExecuteWriteAsync(()=>ComplianceObligations.RecordAcknowledgementAsync(new(companyId,instanceId,request.Kind,request.Reference,request.EvidenceHash,ResolveActorId()??throw new UnauthorizedAccessException(),request.IdempotencyKey,request.ExpectedVersion),ct));

    [Authorize(Policy=CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/compliance-obligations/{instanceId:guid}/submission-evidence/{evidenceId:guid}/review")]
    public Task<ActionResult<ComplianceObligationDto>> ReviewComplianceEvidenceAsync(Guid companyId,Guid instanceId,Guid evidenceId,[FromBody] ComplianceEvidenceReviewRequest request,CancellationToken ct)=>ExecuteWriteAsync(()=>ComplianceObligations.ReviewEvidenceAsync(new(companyId,instanceId,evidenceId,request.Accepted,ResolveActorId()??throw new UnauthorizedAccessException(),request.IdempotencyKey,request.ExpectedVersion),ct));

    [Authorize(Policy=CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/compliance-obligations/{instanceId:guid}/corrections")]
    public Task<ActionResult<ComplianceObligationDto>> CorrectComplianceObligationAsync(Guid companyId,Guid instanceId,[FromBody] ComplianceCorrectionRequest request,CancellationToken ct)=>ExecuteWriteAsync(()=>ComplianceObligations.CorrectAsync(new(companyId,instanceId,request.Reason,ResolveActorId()??throw new UnauthorizedAccessException(),request.IdempotencyKey,request.ExpectedVersion),ct));

    [Authorize(Policy=CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/compliance-obligations/reminders/generate")]
    public Task<ActionResult<int>> GenerateComplianceRemindersAsync(Guid companyId,CancellationToken ct)=>ExecuteWriteAsync(()=>ComplianceObligations.GenerateRemindersAsync(companyId,ResolveActorId()??throw new UnauthorizedAccessException(),ct));

}
