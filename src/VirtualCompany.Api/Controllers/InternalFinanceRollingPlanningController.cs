using System.Globalization;
using FinanceForecastRevision = VirtualCompany.Application.Finance.FinanceForecastRevision;
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
public sealed class InternalFinanceRollingPlanningController : InternalFinanceControllerBase
{

    public InternalFinanceRollingPlanningController(
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceRollingPlanningController> logger)
        : base(initializationProblems, logger)
    {
    }
    [HttpGet("planning/analysis")]
    public Task<ActionResult<FinancePlanningReport>> PlanningAnalysis(Guid companyId, [FromQuery] FinancePlanningQuery query,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct) => PlanningRun(() => service.ReportAsync(companyId, query, ct));
    [HttpGet("planning/export")]
    public Task<ActionResult<FinancePlanningExport>> PlanningExport(Guid companyId, [FromQuery] FinancePlanningQuery query,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct) => PlanningRun(() => service.ExportAsync(companyId, query, ct));
    [HttpPost("planning/explanations")]
    public Task<ActionResult<FinanceVarianceExplanationDto>> ExplainVariance(Guid companyId, ExplainFinanceVariance command,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct) => PlanningRun(() => service.ExplainAsync(companyId, command, ct));
    [HttpPost("planning/preview")]
    public Task<ActionResult<FinanceForecastPreview>> PreviewForecast(Guid companyId, PreviewFinanceForecast command,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct) => PlanningRun(() => service.PreviewAsync(companyId, command, ct));
    [HttpPost("planning/versions")]
    public Task<ActionResult<FinanceForecastRevision>> SaveForecastVersion(Guid companyId, SaveFinanceForecast command,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct) => PlanningRun(() => service.SaveAsync(companyId, command, ct));
    [HttpGet("planning/versions")]
    public Task<ActionResult<IReadOnlyList<FinanceForecastRevisionSummary>>> ForecastVersionHistory(Guid companyId,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct, int skip = 0) => PlanningRun(() => service.HistoryAsync(companyId, skip, ct));
    [HttpGet("planning/versions/{id:guid}")]
    public Task<ActionResult<FinanceForecastRevision>> OpenForecastVersion(Guid companyId, Guid id,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct) => PlanningRun(() => service.OpenAsync(companyId, id, ct));
    [HttpGet("planning/compare")]
    public Task<ActionResult<FinanceForecastComparison>> CompareForecastVersions(Guid companyId, Guid earlier, Guid later,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct, string? currency = null, Guid? costCenterId = null)
        => PlanningRun(() => service.CompareAsync(companyId, earlier, later, currency, costCenterId, ct));
    private async Task<ActionResult<T>> PlanningRun<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return Problem(statusCode: 400, title: "Check planning periods, versions and assumptions.", detail: ex.Message); }
        catch (InvalidDataException) { return Problem(statusCode: 422, title: "Retained forecast evidence cannot be reproduced."); }
        catch (InvalidOperationException ex) { return Problem(statusCode: 409, title: "Reload changed planning evidence.", detail: ex.Message); }
    }
    [HttpGet("planning/compare/export")]
    public Task<ActionResult<FinancePlanningExport>> ComparisonExport(Guid companyId, Guid earlier, Guid later,
        [FromServices] IFinanceRollingPlanningService service, CancellationToken ct, string? currency = null, Guid? costCenterId = null)
        => PlanningRun(() => service.ComparisonExportAsync(companyId, earlier, later, currency, costCenterId, ct));

}
