using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
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

public sealed class GenerateComplianceRequest { public Guid OwnerUserId {get;set;} public string IdempotencyKey {get;set;}=string.Empty; }
public sealed class ComplianceTransitionRequest { public string Action {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} public string? Reason {get;set;} }
public sealed class ComplianceDecisionRequest { public bool Approved {get;set;} public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} public string? Reason {get;set;} }
public sealed class ComplianceEvidenceRequest { public string Reference {get;set;}=string.Empty; public string EvidenceHash {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }
public sealed class ComplianceAcknowledgementRequest { public string Kind {get;set;}=string.Empty; public string Reference {get;set;}=string.Empty; public string EvidenceHash {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }
public sealed class ComplianceEvidenceReviewRequest { public bool Accepted {get;set;} public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }
public sealed class ComplianceCorrectionRequest { public string Reason {get;set;}=string.Empty; public string IdempotencyKey {get;set;}=string.Empty; public long ExpectedVersion {get;set;} }
