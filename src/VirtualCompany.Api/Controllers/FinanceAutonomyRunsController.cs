using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/autonomy/runs")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAutonomyRunsController(IFinanceAutonomyRunService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceAutonomyRunListResult>> List(
        Guid companyId, [FromQuery] Guid? agentId, [FromQuery] Guid? grantId, [FromQuery] string? status,
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int skip = 0,
        [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        await Execute(() => service.ListAsync(companyId,
            new FinanceAutonomyRunFilter(agentId, grantId, status, fromUtc, toUtc, skip, take), cancellationToken));

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Get(
        Guid companyId, Guid runId, CancellationToken cancellationToken) =>
        await Execute(() => service.GetAsync(companyId, runId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Create(
        Guid companyId, [FromBody] CreateOrCoalesceFinanceAutonomyRunCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.CreateOrCoalesceAsync(companyId, command, cancellationToken));

    [HttpPost("{runId:guid}/transition")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Transition(
        Guid companyId, Guid runId, [FromBody] TransitionFinanceAutonomyRunCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.TransitionAsync(companyId, runId, command, cancellationToken));

    [HttpPost("{runId:guid}/steps/{stepId:guid}/approval")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> BindApproval(
        Guid companyId, Guid runId, Guid stepId, [FromBody] BindFinanceAutonomyStepApprovalCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.BindApprovalAsync(companyId, runId, stepId, command, cancellationToken));

    [HttpPost("{runId:guid}/steps/{stepId:guid}/reconcile")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Reconcile(
        Guid companyId, Guid runId, Guid stepId, [FromBody] ReconcileFinanceAutonomyStepCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.ReconcileStepAsync(companyId, runId, stepId, command, cancellationToken));

    [HttpPost("{runId:guid}/cancel")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Cancel(
        Guid companyId, Guid runId, [FromBody] CancelFinanceAutonomyRunCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.CancelAsync(companyId, runId, command, cancellationToken));

    [HttpPost("{runId:guid}/supersede")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Supersede(
        Guid companyId, Guid runId, [FromBody] SupersedeFinanceAutonomyRunCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.SupersedeAsync(companyId, runId, command, cancellationToken));

    [HttpPost("{runId:guid}/redact")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Redact(
        Guid companyId, Guid runId, [FromBody] RedactFinanceAutonomyRunCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.RedactAsync(companyId, runId, command, cancellationToken));

    [HttpPost("{runId:guid}/replay")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Replay(
        Guid companyId, Guid runId, [FromBody] ReplayFinanceAutonomyRunCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.ReplayAsync(companyId, runId, command, cancellationToken));

    [HttpPost("{runId:guid}/narrow")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyRunDto>> Narrow(
        Guid companyId, Guid runId, [FromBody] NarrowFinanceAutonomyRunCommand command,
        CancellationToken cancellationToken) =>
        await Execute(() => service.NarrowAsync(companyId, runId, command, cancellationToken));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (FinanceAutonomyRunValidationException ex) { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))); }
        catch (FinanceAutonomyRunConcurrencyException ex) { return Conflict(new ProblemDetails { Title = "Finance autonomy run changed", Detail = ex.Message, Status = StatusCodes.Status409Conflict }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
