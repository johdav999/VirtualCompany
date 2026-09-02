using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/autonomy/workflow-templates")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAutonomyWorkflowTemplatesController(
    IFinanceAutonomyWorkflowTemplateService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FinanceAutonomyWorkflowTemplate>>> List(
        Guid companyId, [FromQuery] string? locale = null,
        CancellationToken cancellationToken = default) =>
        await Execute(() => service.ListAsync(companyId, locale, cancellationToken));

    [HttpPost("preview")]
    public async Task<ActionResult<FinanceAutonomyWorkflowActivationPreview>> Preview(
        Guid companyId, [FromBody] PreviewFinanceAutonomyWorkflowTemplateCommand command,
        CancellationToken cancellationToken = default) =>
        await Execute(() => service.PreviewAsync(companyId, command, cancellationToken));

    [HttpPost("draft")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyWorkflowDraftResult>> CreateDraft(
        Guid companyId, [FromBody] CreateFinanceAutonomyWorkflowTemplateDraftCommand command,
        CancellationToken cancellationToken = default) =>
        await Execute(() => service.CreateDraftAsync(companyId, command, cancellationToken));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (FinanceAutonomyWorkflowTemplateValidationException ex)
        { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))); }
        catch (FinanceAutonomyValidationException ex)
        { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))); }
        catch (FinanceAutonomyConcurrencyException ex)
        { return Conflict(new ProblemDetails { Title = "Finance autonomy grant changed", Detail = ex.Message, Status = StatusCodes.Status409Conflict }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
