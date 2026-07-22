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
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("anomalies")]
    public async Task<ActionResult<IReadOnlyList<FinanceSeedAnomalyDto>>> GetSeedAnomaliesAsync(
        Guid companyId,
        [FromQuery(Name = "type")] string? anomalyType,
        [FromQuery] Guid? affectedRecordId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetSeedAnomaliesAsync(
                new GetFinanceSeedAnomaliesQuery(
                    companyId, anomalyType, affectedRecordId, limit),
                cancellationToken));

    [HttpGet("anomalies/{anomalyId:guid}")]
    public async Task<ActionResult<FinanceSeedAnomalyDto>> GetSeedAnomalyAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken)
    {
        var anomaly = await _financeReadService.GetSeedAnomalyByIdAsync(new GetFinanceSeedAnomalyByIdQuery(companyId, anomalyId), cancellationToken);
        return anomaly is null
            ? NotFound(CreateProblemDetails("Finance seed anomaly was not found.", "Finance record was not found.", StatusCodes.Status404NotFound))
            : Ok(anomaly);
    }

    [HttpGet("anomalies/workbench")]
    public async Task<ActionResult<FinanceAnomalyWorkbenchResultDto>> GetAnomalyWorkbenchAsync(
        Guid companyId,
        [FromQuery(Name = "type")] string? anomalyType,
        [FromQuery] string? status,
        [FromQuery] decimal? confidenceMin,
        [FromQuery] decimal? confidenceMax,
        [FromQuery] string? supplier,
        [FromQuery] DateTime? dateFromUtc,
        [FromQuery] DateTime? dateToUtc,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetAnomalyWorkbenchAsync(
                new GetFinanceAnomalyWorkbenchQuery(
                    companyId,
                    anomalyType,
                    status,
                    confidenceMin,
                    confidenceMax,
                    supplier,
                    dateFromUtc,
                    dateToUtc,
                    page,
                    pageSize),
                cancellationToken));

    [HttpGet("anomalies/workbench/{anomalyId:guid}")]
    public async Task<ActionResult<FinanceAnomalyDetailDto>> GetAnomalyDetailAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetAnomalyDetailAsync(new GetFinanceAnomalyDetailQuery(companyId, anomalyId), cancellationToken),
            "Finance anomaly was not found.");

}

