using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;
[ApiController,Route("api/companies/{companyId:guid}/planning/scenarios"),Authorize(Policy=CompanyPolicies.CompanyManager),RequireCompanyContext]
public sealed class StrategicScenariosController(IStrategicScenarioService service):ControllerBase
{
    [HttpGet("options")]public Task<ActionResult<StrategicScenarioOptions>> Options(Guid companyId,int fiscalYear,CancellationToken ct)=>Run(()=>service.OptionsAsync(companyId,fiscalYear,ct));
    [HttpGet("versions")]public Task<ActionResult<StrategicScenarioHistoryPage>> History(Guid companyId,int skip,CancellationToken ct)=>Run(()=>service.HistoryAsync(companyId,skip,ct));
    [HttpPost("preview")]public Task<ActionResult<StrategicScenarioPreview>> Preview(Guid companyId,StrategicScenarioInput input,CancellationToken ct)=>Run(()=>service.PreviewAsync(companyId,input,ct));
    [HttpPost("versions")]public Task<ActionResult<StrategicScenarioDocument>> Save(Guid companyId,SaveStrategicScenario command,CancellationToken ct)=>Run(()=>service.SaveAsync(companyId,command,ct));
    [HttpPost("versions/{id:guid}/open")]public Task<ActionResult<StrategicScenarioDocument>> Open(Guid companyId,Guid id,CancellationToken ct)=>Run(()=>service.OpenAsync(companyId,id,ct));
    [HttpPost("versions/{id:guid}/duplicate")]public Task<ActionResult<StrategicScenarioDocument>> Duplicate(Guid companyId,Guid id,DuplicateStrategicScenario command,CancellationToken ct)=>Run(()=>service.DuplicateAsync(companyId,id,command,ct));
    [HttpPost("compare")]public Task<ActionResult<StrategicScenarioComparison>> Compare(Guid companyId,CompareScenarios command,CancellationToken ct)=>Run(()=>service.CompareAsync(companyId,command.Baseline,command.Alternative,ct));
    public sealed record CompareScenarios(Guid Baseline,Guid Alternative);
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action){Response.Headers.CacheControl="no-store";try{return Ok(await action());}catch(UnauthorizedAccessException){return Forbid();}catch(KeyNotFoundException){return NotFound();}catch(ArgumentException e){return Problem(statusCode:400,detail:e.Message);}catch(InvalidOperationException e){return Problem(statusCode:409,detail:e.Message);}catch(InvalidDataException e){return Problem(statusCode:422,detail:e.Message);}}
}
