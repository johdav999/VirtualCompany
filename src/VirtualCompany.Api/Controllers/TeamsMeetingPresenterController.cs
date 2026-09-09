using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;
[ApiController]
[TeamsDeploymentExceptionFilter]
[Route("api/sales/meeting-sessions/{sessionId:guid}/presenter")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class TeamsMeetingPresenterController(ITeamsMeetingPresenterService service, ICompanyContextAccessor company) : ControllerBase
{
    [HttpGet]
    public Task<TeamsMeetingPresenterDto> Get(Guid sessionId, CancellationToken ct) => service.GetAsync(CompanyId(), UserId(), sessionId, ct);
    [HttpPut]
    public Task<TeamsMeetingPresenterDto> Select(Guid sessionId, SelectTeamsMeetingPresenter request, CancellationToken ct) =>
        service.SelectAsync(CompanyId(), UserId(), sessionId, request, ct);
    private Guid CompanyId() => company.CompanyId ?? throw new UnauthorizedAccessException();
    private Guid UserId() => company.UserId ?? throw new UnauthorizedAccessException();
}
[ApiController]
[TeamsDeploymentExceptionFilter]
[Route("api/platform/teams-presenter/companies/{companyId:guid}/first-uat")]
[Authorize(Policy = CompanyPolicies.PlatformAdministration)]
public sealed class FirstTeamsUatController(IFirstTeamsUatService service, ICurrentUserAccessor user) : ControllerBase
{
    [HttpGet]
    public Task<FirstTeamsUatDto> Get(Guid companyId, CancellationToken ct) => service.GetAsync(companyId, ct);
    [HttpPost]
    public Task<FirstTeamsUatDto> AuthorizeTest(Guid companyId, FirstTeamsUatRequest request, CancellationToken ct) =>
        service.AuthorizeAsync(companyId, UserId(), request, ct);
    [HttpPost("revoke")]
    public Task<FirstTeamsUatDto> Revoke(Guid companyId, RevokeFirstTeamsUat request, CancellationToken ct) =>
        service.RevokeAsync(companyId, UserId(), request.ExpectedVersion, ct);
    private Guid UserId() => user.UserId ?? throw new UnauthorizedAccessException();
}
public sealed record RevokeFirstTeamsUat(long ExpectedVersion);
