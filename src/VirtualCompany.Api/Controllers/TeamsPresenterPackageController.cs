using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/platform/teams-presenter/package")]
[Authorize(Policy = CompanyPolicies.PlatformAdministration)]
public sealed class TeamsPresenterPackageController(ITeamsPresenterPackageBuilder builder) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> DownloadAsync(CancellationToken ct)
    {
        var output = new MemoryStream();
        var result = await builder.BuildAsync(output, ct);
        output.Position = 0;
        return File(output, "application/zip", $"alex-teams-presenter-{result.PackageVersion}.zip");
    }
}
