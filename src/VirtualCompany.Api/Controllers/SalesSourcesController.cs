using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/sources")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesSourcesController(ICompanyContextAccessor context, ISalesSourceService sources) : ControllerBase
{
    [HttpGet("campaigns")] public Task<IReadOnlyList<SalesAcquisitionCampaignDto>> Campaigns(CancellationToken ct)=>sources.ListCampaignsAsync(context.CompanyId??Guid.Empty,ct);
    [HttpPost("campaigns")][Authorize(Policy=CompanyPolicies.CompanyManager)] public Task<SalesAcquisitionCampaignDto> CreateCampaign(SaveSalesAcquisitionCampaignRequest request,CancellationToken ct)=>sources.CreateCampaignAsync(context.CompanyId??Guid.Empty,request,ct);
    [HttpGet("{subjectType}/{subjectId:guid}")]
    public async Task<ActionResult<SalesAttributionDto>> Get(string subjectType, Guid subjectId, CancellationToken ct)
    { var companyId = context.CompanyId ?? Guid.Empty; var result = await sources.GetAsync(companyId, subjectType, subjectId, ct); return result is null ? NotFound() : Ok(result); }

    [HttpGet("{subjectType}/{subjectId:guid}/timeline")]
    public async Task<ActionResult<IReadOnlyList<SalesSourceTouchDto>>> Timeline(string subjectType, Guid subjectId, CancellationToken ct) => Ok(await sources.TimelineAsync(context.CompanyId ?? Guid.Empty, subjectType, subjectId, ct));

    [HttpPost]
    public async Task<ActionResult<SalesSourceTouchDto>> Record(RecordSalesSourceTouchRequest request, CancellationToken ct) => Ok(await sources.RecordAsync(context.CompanyId ?? Guid.Empty, request, ct));
}
