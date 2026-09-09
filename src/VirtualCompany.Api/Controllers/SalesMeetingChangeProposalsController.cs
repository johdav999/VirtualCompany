using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}/change-proposals")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesMeetingChangeProposalsController(ISalesMeetingChangeProposalService service, ICompanyContextAccessor context) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<SalesMeetingChangeProposalDto>> ListAsync(Guid sessionId, CancellationToken ct) => service.ListAsync(CompanyId(), UserId(), sessionId, ct);
    [HttpGet("{proposalId:guid}")] public Task<ActionResult<SalesMeetingChangeProposalDto>> GetAsync(Guid sessionId, Guid proposalId, CancellationToken ct) => ExecuteAsync(() => service.GetAsync(CompanyId(), UserId(), sessionId, proposalId, ct));
    [HttpPost("generate")] public Task<ActionResult<IReadOnlyList<SalesMeetingChangeProposalDto>>> GenerateAsync(Guid sessionId, [FromBody] GenerateSalesMeetingChangeProposalsRequest request, CancellationToken ct) => ExecuteListAsync(() => service.GenerateAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct));
    [HttpPut("{proposalId:guid}")] public Task<ActionResult<SalesMeetingChangeProposalDto>> EditAsync(Guid sessionId, Guid proposalId, [FromBody] EditSalesMeetingChangeProposalRequest request, CancellationToken ct) => ExecuteAsync(() => service.EditAsync(CompanyId(), UserId(), sessionId, proposalId, request, HttpContext.TraceIdentifier, ct));
    [HttpPost("{proposalId:guid}/approve")] public Task<ActionResult<SalesMeetingChangeProposalDto>> ApproveAsync(Guid sessionId, Guid proposalId, [FromBody] ReviewSalesMeetingChangeProposalRequest request, CancellationToken ct) => ExecuteAsync(() => service.ApproveAsync(CompanyId(), UserId(), sessionId, proposalId, request, HttpContext.TraceIdentifier, ct));
    [HttpPost("{proposalId:guid}/reject")] public Task<ActionResult<SalesMeetingChangeProposalDto>> RejectAsync(Guid sessionId, Guid proposalId, [FromBody] ReviewSalesMeetingChangeProposalRequest request, CancellationToken ct) => ExecuteAsync(() => service.RejectAsync(CompanyId(), UserId(), sessionId, proposalId, request, HttpContext.TraceIdentifier, ct));
    [HttpPost("approve-all-safe")] public Task<ActionResult<BulkApproveSalesMeetingChangeProposalsResult>> BulkAsync(Guid sessionId, [FromBody] BulkApproveSalesMeetingChangeProposalsRequest request, CancellationToken ct) => ExecuteValueAsync(() => service.BulkApproveSafeAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct));
    [HttpPost("{proposalId:guid}/execute")] public Task<ActionResult<SalesMeetingChangeProposalDto>> ExecuteProposalAsync(Guid sessionId, Guid proposalId, [FromBody] ExecuteSalesMeetingChangeProposalRequest request, CancellationToken ct) => ExecuteAsync(() => service.ExecuteAsync(CompanyId(), UserId(), sessionId, proposalId, request, HttpContext.TraceIdentifier, ct));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T?>> action) where T : class { try { var result = await action(); return result is null ? NotFound() : Ok(result); } catch (SalesMeetingChangeProposalValidationException e) { return BadRequest(StableProblemDetails.CreateValidation(HttpContext, e.Errors, SalesMeetingChangeProposalProblemCodes.InvalidRequest)); } catch (SalesMeetingChangeProposalConflictException e) { return Conflict(StableProblemDetails.Create(HttpContext, 409, e.Code, "Sales meeting change conflict", e.Message)); } catch (KeyNotFoundException) { return NotFound(); } }
    private async Task<ActionResult<IReadOnlyList<T>>> ExecuteListAsync<T>(Func<Task<IReadOnlyList<T>>> action) { try { return Ok(await action()); } catch (SalesMeetingChangeProposalValidationException e) { return BadRequest(StableProblemDetails.CreateValidation(HttpContext, e.Errors, SalesMeetingChangeProposalProblemCodes.InvalidRequest)); } catch (SalesMeetingChangeProposalConflictException e) { return Conflict(StableProblemDetails.Create(HttpContext, 409, e.Code, "Sales meeting change conflict", e.Message)); } }
    private async Task<ActionResult<T>> ExecuteValueAsync<T>(Func<Task<T>> action) { try { return Ok(await action()); } catch (SalesMeetingChangeProposalValidationException e) { return BadRequest(StableProblemDetails.CreateValidation(HttpContext, e.Errors, SalesMeetingChangeProposalProblemCodes.InvalidRequest)); } catch (SalesMeetingChangeProposalConflictException e) { return Conflict(StableProblemDetails.Create(HttpContext, 409, e.Code, "Sales meeting change conflict", e.Message)); } }
    private Guid CompanyId() => context.CompanyId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => context.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}
