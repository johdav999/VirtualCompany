using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
[Route("api/accountant/portfolio")]
public sealed class AccountantPortfolioController(IAccountantCollaborationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AccountantPortfolioDto>> GetAsync(CancellationToken cancellationToken) =>
        Ok(await service.GetPortfolioAsync(cancellationToken));
}

[ApiController]
[Authorize(Policy = CompanyPolicies.AccountantCollaboration)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/accountant-collaboration")]
public sealed class AccountantCollaborationController(IAccountantCollaborationService service, ICompanyContextAccessor context) : ControllerBase
{
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpGet("grants")]
    public async Task<ActionResult<IReadOnlyList<AccountantGrantDto>>> ListGrantsAsync(Guid companyId, CancellationToken ct) =>
        await ExecuteAsync(() => service.ListGrantsAsync(companyId, ct));

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("grants")]
    public async Task<ActionResult<AccountantGrantDto>> CreateGrantAsync(Guid companyId, CreateAccountantGrantRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.CreateGrantAsync(new(companyId, request.MembershipId, request.ScopeKey,
            request.CanViewDocuments, request.CanRequestEvidence, request.CanSignOff, request.EffectiveFromUtc,
            request.EffectiveUntilUtc, UserId()), ct));

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("grants/{grantId:guid}/approve")]
    public async Task<ActionResult<AccountantGrantDto>> ApproveGrantAsync(Guid companyId, Guid grantId, VersionedAccountantRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.ApproveGrantAsync(new(companyId, grantId, UserId(), request.ExpectedVersion), ct));

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("grants/{grantId:guid}/revoke")]
    public async Task<ActionResult<AccountantGrantDto>> RevokeGrantAsync(Guid companyId, Guid grantId, RevokeAccountantGrantRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.RevokeGrantAsync(new(companyId, grantId, UserId(), request.Reason, request.ExpectedVersion), ct));

    [HttpGet("engagements")]
    public async Task<ActionResult<IReadOnlyList<AccountantEngagementDto>>> ListEngagementsAsync(Guid companyId, CancellationToken ct) =>
        await ExecuteAsync(() => service.ListEngagementsAsync(companyId, ct));

    [HttpGet("engagements/{engagementId:guid}")]
    public async Task<ActionResult<AccountantEngagementDto>> GetEngagementAsync(Guid companyId, Guid engagementId, CancellationToken ct) =>
        await ExecuteAsync(() => service.GetEngagementAsync(companyId, engagementId, ct));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("engagements")]
    public async Task<ActionResult<AccountantEngagementDto>> CreateEngagementAsync(Guid companyId, CreateAccountantEngagementRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.CreateEngagementAsync(new(companyId, request.GrantId, request.FiscalPeriodId,
            request.Title, request.EngagementType, UserId(), request.DueUtc), ct));

    [HttpPost("engagements/{engagementId:guid}/review-items")]
    public async Task<ActionResult<AccountantEngagementDto>> AddReviewItemAsync(Guid companyId, Guid engagementId, AddAccountantReviewItemRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.AddReviewItemAsync(new(companyId, engagementId, request.IsFinding,
            request.Severity, request.Content, request.TargetType, request.TargetId, UserId()), ct));

    [HttpPost("engagements/{engagementId:guid}/review-items/{itemId:guid}/resolve")]
    public async Task<ActionResult<AccountantEngagementDto>> ResolveReviewItemAsync(Guid companyId, Guid engagementId, Guid itemId, ResolveAccountantItemRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.ResolveReviewItemAsync(new(companyId, engagementId, itemId, request.ResolutionSummary, UserId()), ct));

    [HttpPost("engagements/{engagementId:guid}/evidence-requests")]
    public async Task<ActionResult<AccountantEngagementDto>> CreateEvidenceRequestAsync(Guid companyId, Guid engagementId, CreateAccountantEvidenceRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.CreateEvidenceRequestAsync(new(companyId, engagementId, request.RequestText,
            request.TargetType, request.TargetId, request.AssignedToUserId, request.DueUtc, UserId()), ct));

    [HttpPost("engagements/{engagementId:guid}/evidence-requests/{requestId:guid}/responses")]
    public async Task<ActionResult<AccountantEngagementDto>> RespondAsync(Guid companyId, Guid engagementId, Guid requestId, RespondToAccountantEvidenceRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.RespondToEvidenceRequestAsync(new(companyId, engagementId, requestId,
            request.ResponseText, request.DocumentId, UserId()), ct));

    [HttpPost("engagements/{engagementId:guid}/evidence-requests/{requestId:guid}/resolve")]
    public async Task<ActionResult<AccountantEngagementDto>> ResolveEvidenceAsync(Guid companyId, Guid engagementId, Guid requestId, ResolveAccountantItemRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.ResolveEvidenceRequestAsync(new(companyId, engagementId, requestId, request.ResolutionSummary, UserId()), ct));

    [HttpPost("engagements/{engagementId:guid}/sign-off")]
    public async Task<ActionResult<AccountantEngagementDto>> SignOffAsync(Guid companyId, Guid engagementId, SignOffAccountantEngagementRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.SignOffAsync(new(companyId, engagementId, request.Conclusion, UserId(), request.ExpectedVersion), ct));

    private Guid UserId() => context.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException();

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (AccountantCollaborationException exception)
        {
            var status = exception.ReasonCode.EndsWith("_not_found", StringComparison.Ordinal) ? StatusCodes.Status404NotFound
                : exception.ReasonCode.Contains("denied", StringComparison.Ordinal) || exception.ReasonCode.Contains("required", StringComparison.Ordinal) || exception.ReasonCode.Contains("forbidden", StringComparison.Ordinal) || exception.ReasonCode.Contains("inactive", StringComparison.Ordinal)
                    ? StatusCodes.Status403Forbidden : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            var problem = new ProblemDetails { Title = "Accountant collaboration request could not be completed", Detail = exception.Message,
                Status = status, Instance = Request.Path };
            problem.Extensions["reasonCode"] = exception.ReasonCode;
            return StatusCode(status, problem);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = "Invalid accountant collaboration request", Detail = exception.Message, Status = 400, Instance = Request.Path }); }
    }
}

public sealed record CreateAccountantGrantRequest(Guid MembershipId, string ScopeKey, bool CanViewDocuments,
    bool CanRequestEvidence, bool CanSignOff, DateTime EffectiveFromUtc, DateTime? EffectiveUntilUtc);
public sealed record VersionedAccountantRequest(long ExpectedVersion);
public sealed record RevokeAccountantGrantRequest(long ExpectedVersion, string Reason);
public sealed record CreateAccountantEngagementRequest(Guid GrantId, Guid? FiscalPeriodId, string Title, string EngagementType, DateTime DueUtc);
public sealed record AddAccountantReviewItemRequest(bool IsFinding, string Severity, string Content, string TargetType, Guid? TargetId);
public sealed record ResolveAccountantItemRequest(string ResolutionSummary);
public sealed record CreateAccountantEvidenceRequest(string RequestText, string TargetType, Guid? TargetId, Guid? AssignedToUserId, DateTime DueUtc);
public sealed record RespondToAccountantEvidenceRequest(string ResponseText, Guid? DocumentId);
public sealed record SignOffAccountantEngagementRequest(string Conclusion, long ExpectedVersion);
