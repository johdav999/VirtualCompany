using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/agent-supervision")]
[Authorize(Policy=CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class AgentSupervisionReportsController(IAgentSupervisionReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(Guid companyId,DateOnly? from,DateOnly? to,string? responsibility,string? taskType,
        Guid? agentId,string view="work",string? metric=null,CancellationToken cancellationToken=default)=>
        Read(()=>reports.GetAsync(new(companyId,from,to,responsibility,taskType,agentId,view,metric),cancellationToken));
    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid companyId,DateOnly? from,DateOnly? to,string? responsibility,string? taskType,
        Guid? agentId,string view="work",string? metric=null,CancellationToken cancellationToken=default)
    {
        try
        {
            Response.Headers.CacheControl="no-store";
            var csv=await reports.ExportAsync(new(companyId,from,to,responsibility,taskType,agentId,view,metric),cancellationToken);
            return Ok(csv);
        }
        catch(UnauthorizedAccessException){return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(TimeZoneNotFoundException){return BadRequest(new{message="Configure a supported company timezone before running reports."});}
        catch(InvalidTimeZoneException){return BadRequest(new{message="The configured company timezone is unavailable."});}
    }
    private async Task<IActionResult> Read(Func<Task<AgentSupervisionReport>> get)
    {
        try {Response.Headers.CacheControl="no-store";return Ok(await get());}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(TimeZoneNotFoundException){return BadRequest(new{message="Configure a supported company timezone before running reports."});}
        catch(InvalidTimeZoneException){return BadRequest(new{message="The configured company timezone is unavailable."});}
    }
}
