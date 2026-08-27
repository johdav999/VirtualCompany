using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class CustomerBillingController(ICustomerBillingProfileService service) : ControllerBase
{
    [HttpGet("customers/{counterpartyId:guid}/billing-profile")]
    public async Task<ActionResult<CustomerBillingProfileDto>> GetProfileAsync(Guid companyId, Guid counterpartyId,
        CancellationToken cancellationToken)
    {
        var profile = await service.GetAsync(new GetCustomerBillingProfileQuery(companyId, counterpartyId), cancellationToken);
        return profile is null ? NotFound(Problem(StatusCodes.Status404NotFound, CustomerBillingReasonCodes.ProfileNotFound,
            "Customer billing profile was not found.")) : Ok(profile);
    }

    [HttpGet("customers/{counterpartyId:guid}/billing-profile/history")]
    public async Task<ActionResult<IReadOnlyList<CustomerBillingProfileVersionDto>>> GetHistoryAsync(Guid companyId,
        Guid counterpartyId, [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Ok(await service.GetHistoryAsync(new GetCustomerBillingProfileHistoryQuery(companyId, counterpartyId, limit), cancellationToken));

    [HttpPut("customers/{counterpartyId:guid}/billing-profile")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerBillingProfileDto>> UpsertProfileAsync(Guid companyId, Guid counterpartyId,
        [FromBody] UpsertCustomerBillingProfileRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.UpsertAsync(new UpsertCustomerBillingProfileCommand(companyId, counterpartyId, request.Profile,
            request.ExpectedVersion, ResolveActorUserId(), ResolveCorrelationId()), cancellationToken));

    [HttpPut("customer-billing/source-conflicts/{conflictId:guid}")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerBillingProfileDto>> ResolveConflictAsync(Guid companyId, Guid conflictId,
        [FromBody] ResolveCustomerBillingSourceConflictRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.ResolveConflictAsync(new ResolveCustomerBillingSourceConflictCommand(companyId, conflictId,
            request.ExpectedConflictVersion, request.ExpectedProfileVersion, request.UseIncomingValues,
            request.Reason, ResolveActorUserId(), ResolveCorrelationId()), cancellationToken));

    [HttpGet("customer-duplicates")]
    public async Task<ActionResult<IReadOnlyList<CustomerDuplicateCandidateDto>>> GetDuplicatesAsync(Guid companyId,
        [FromQuery] string? status = null, [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Ok(await service.GetDuplicateCandidatesAsync(new GetCustomerDuplicateCandidatesQuery(companyId, status, limit), cancellationToken));

    [HttpPost("customer-duplicates/{candidateId:guid}/decision")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerDuplicateCandidateDto>> DecideDuplicateAsync(Guid companyId, Guid candidateId,
        [FromBody] DecideCustomerDuplicateRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.DecideDuplicateAsync(new DecideCustomerDuplicateCommand(companyId, candidateId, request.ExpectedVersion,
            request.Decision, request.MergeSourceCounterpartyId, request.MergeTargetCounterpartyId, request.Reason,
            ResolveActorUserId(), ResolveCorrelationId()), cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (CustomerBillingException ex)
        {
            var status = ex.IsNotFound ? StatusCodes.Status404NotFound : ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return new ObjectResult(Problem(status, ex.ReasonCode, ex.Message)) { StatusCode = status };
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "customer_billing_validation_failed", ex.Message));
        }
    }

    private Guid ResolveActorUserId()
    {
        var value = User.FindFirstValue(CurrentUserClaimTypes.UserId) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id :
            throw new UnauthorizedAccessException("An authenticated user id is required for customer billing changes.");
    }

    private string ResolveCorrelationId() =>
        Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()[..Math.Min(value.ToString().Length, 200)] : HttpContext.TraceIdentifier;

    private ProblemDetails Problem(int status, string reasonCode, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = status == 404 ? "Customer billing record not found" :
            status == 409 ? "Customer billing conflict" : "Customer billing request rejected", Detail = detail, Instance = Request.Path };
        problem.Extensions["reasonCode"] = reasonCode; problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return problem;
    }
}

public sealed record UpsertCustomerBillingProfileRequest(CustomerBillingProfileInputDto Profile, long? ExpectedVersion);
public sealed record ResolveCustomerBillingSourceConflictRequest(long ExpectedConflictVersion, long ExpectedProfileVersion,
    bool UseIncomingValues, string Reason);
public sealed record DecideCustomerDuplicateRequest(long ExpectedVersion, string Decision, Guid? MergeSourceCounterpartyId,
    Guid? MergeTargetCounterpartyId, string Reason);
