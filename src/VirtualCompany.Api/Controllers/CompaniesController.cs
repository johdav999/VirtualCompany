using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class CompaniesController : ControllerBase
{
    private readonly ICurrentUserCompanyService _companyService;
    private readonly ICompanyNoteService _companyNoteService;

    public CompaniesController(
        ICurrentUserCompanyService companyService,
        ICompanyNoteService companyNoteService)
    {
        _companyService = companyService;
        _companyNoteService = companyNoteService;
    }

    [HttpGet("access")]
    public async Task<ActionResult<CompanyAccessDto>> GetAccessAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var access = await _companyService.GetCompanyAccessAsync(companyId, cancellationToken);
        if (access is null)
        {
            return Forbid();
        }

        return Ok(access);
    }

    [HttpGet("access/admin")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    public async Task<ActionResult<CompanyAccessDto>> GetAdminAccessAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var access = await _companyService.GetCompanyAccessAsync(companyId, cancellationToken);
        if (access is null)
        {
            return Forbid();
        }

        return Ok(access);
    }

    [HttpGet("notes/{noteId:guid}")]
    public async Task<ActionResult<CompanyNoteDto>> GetNoteAsync(
        Guid companyId,
        Guid noteId,
        CancellationToken cancellationToken)
    {
        var note = await _companyNoteService.GetNoteAsync(companyId, noteId, cancellationToken);
        if (note is null)
        {
            return NotFound();
        }

        return Ok(note);
    }
}