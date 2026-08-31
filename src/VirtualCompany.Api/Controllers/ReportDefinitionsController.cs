using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/accounting/report-definitions")]
public sealed class ReportDefinitionsController(IReportDefinitionService definitions, ICurrentUserAccessor currentUser)
    : ControllerBase
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<ReportSystemTemplateDto>>> TemplatesAsync(Guid companyId,
        CancellationToken cancellationToken) => Ok(await definitions.ListSystemTemplatesAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportDefinitionSummaryDto>>> ListAsync(Guid companyId,
        CancellationToken cancellationToken) => Ok(await definitions.ListAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("versions/{versionId:guid}")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> GetAsync(Guid companyId, Guid versionId,
        CancellationToken cancellationToken) => await ExecuteAsync(() => definitions.GetAsync(companyId, versionId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("copy-template")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> CopyAsync(Guid companyId,
        CopyReportTemplateRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.CopySystemTemplateAsync(new(companyId, request.TemplateKey,
            request.Code, request.Name, actor, request.IdempotencyKey), cancellationToken), created: true);
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("{definitionId:guid}/versions")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> CreateVersionAsync(Guid companyId, Guid definitionId,
        CreateReportDefinitionVersionRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.CreateVersionAsync(new(companyId, definitionId,
            request.SourceVersionId, actor, request.IdempotencyKey), cancellationToken), created: true);
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("versions/{versionId:guid}")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> UpdateAsync(Guid companyId, Guid versionId,
        UpdateReportDefinitionRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.UpdateAsync(new(companyId, versionId, request.Name,
            request.ExpectedRevision, actor, request.IdempotencyKey, request.Sections, request.Comparison), cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("versions/{versionId:guid}/validate")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> ValidateAsync(Guid companyId, Guid versionId,
        ReportDefinitionRevisionRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.ValidateAsync(new(companyId, versionId, request.ExpectedRevision,
            actor, request.IdempotencyKey), cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("versions/{versionId:guid}/preview")]
    public async Task<ActionResult<CompleteFinancialReportDto>> PreviewAsync(Guid companyId, Guid versionId,
        [FromQuery] Guid fiscalPeriodId, [FromQuery] Guid? comparisonFiscalPeriodId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 200,
        CancellationToken cancellationToken = default) => await ExecuteAsync(() => definitions.PreviewAsync(
            new(companyId, versionId, fiscalPeriodId, comparisonFiscalPeriodId, page, pageSize), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("versions/{versionId:guid}/submit")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> SubmitAsync(Guid companyId, Guid versionId,
        ReportDefinitionRevisionRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.SubmitAsync(new(companyId, versionId, request.ExpectedRevision,
            actor, request.IdempotencyKey), cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("versions/{versionId:guid}/decision")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> DecideAsync(Guid companyId, Guid versionId,
        DecideReportDefinitionRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.DecideAsync(new(companyId, versionId, request.ExpectedRevision,
            actor, request.Approve, request.DecisionNote, request.IdempotencyKey), cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("versions/{versionId:guid}/activate")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> ActivateAsync(Guid companyId, Guid versionId,
        ActivateReportDefinitionRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.ActivateAsync(new(companyId, versionId, request.ExpectedRevision,
            actor, request.EffectiveFrom, request.IdempotencyKey), cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("versions/{versionId:guid}/retire")]
    public async Task<ActionResult<ReportDefinitionVersionDto>> RetireAsync(Guid companyId, Guid versionId,
        RetireReportDefinitionRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await ExecuteAsync(() => definitions.RetireAsync(new(companyId, versionId, request.ExpectedRevision,
            actor, request.EffectiveTo, request.IdempotencyKey), cancellationToken));
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action, bool created = false)
    {
        try
        {
            var result = await action();
            return created ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ReportDefinitionException ex)
        {
            var status = ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return new ObjectResult(StableProblemDetails.Create(HttpContext, status, ex.ReasonCode,
                ex.IsConflict ? "Report definition conflict" : "Report definition rejected", ex.Message)) { StatusCode = status };
        }
        catch (ArgumentException ex)
        {
            return new ObjectResult(StableProblemDetails.Create(HttpContext, 400, "report_definition_invalid",
                "Report definition rejected", ex.Message)) { StatusCode = 400 };
        }
        catch (InvalidOperationException ex)
        {
            return new ObjectResult(StableProblemDetails.Create(HttpContext, 409, "report_definition_state_conflict",
                "Report definition conflict", ex.Message)) { StatusCode = 409 };
        }
    }
}

public sealed class CopyReportTemplateRequest
{
    public string TemplateKey { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}
public sealed class CreateReportDefinitionVersionRequest { public Guid SourceVersionId { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public class ReportDefinitionRevisionRequest { public int ExpectedRevision { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class DecideReportDefinitionRequest : ReportDefinitionRevisionRequest { public bool Approve { get; set; } public string? DecisionNote { get; set; } }
public sealed class ActivateReportDefinitionRequest : ReportDefinitionRevisionRequest { public DateOnly EffectiveFrom { get; set; } }
public sealed class RetireReportDefinitionRequest : ReportDefinitionRevisionRequest { public DateOnly EffectiveTo { get; set; } }
public sealed class UpdateReportDefinitionRequest : ReportDefinitionRevisionRequest
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<ReportDefinitionSectionInput> Sections { get; set; } = [];
    public ReportDefinitionComparisonDto Comparison { get; set; } = new("none", 1, false, false);
}
