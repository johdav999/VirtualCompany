using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;
[ApiController,Route("api/companies/{companyId:guid}/workspace/monthly/reviews")]
[Authorize(Policy=CompanyPolicies.CompanyMember),RequireCompanyContext]
public sealed class MonthlyReviewSnapshotsController(IMonthlyReviewSnapshotService reviews) : ControllerBase
{
    [HttpGet] public Task<ActionResult<MonthlyReviewHistoryDto>> List(Guid companyId,[FromQuery]string lens,[FromQuery]int year,[FromQuery]int month,[FromQuery]int skip=0,[FromQuery]int take=10,CancellationToken ct=default)
        => Respond(()=>reviews.ListAsync(companyId,lens,year,month,skip,take,ct));
    [HttpPost] public Task<ActionResult<MonthlyReviewSnapshotDto>> Save(Guid companyId,SaveMonthlyReviewCommand command,CancellationToken ct)=>Respond(()=>reviews.SaveAsync(companyId,command,ct));
    [HttpPost("{id:guid}/open")] public Task<ActionResult<MonthlyReviewSnapshotDto>> Open(Guid companyId,Guid id,CancellationToken ct)=>Respond(()=>reviews.OpenAsync(companyId,id,ct));
    [HttpPost("{id:guid}/refresh")] public Task<ActionResult<MonthlyReviewSnapshotDto>> Refresh(Guid companyId,Guid id,RefreshMonthlyReviewCommand command,CancellationToken ct)=>Respond(()=>reviews.RefreshAsync(companyId,id,command,ct));
    [HttpPost("{id:guid}/export")] public Task<ActionResult<MonthlyReviewExportDto>> Export(Guid companyId,Guid id,CancellationToken ct)=>Respond(()=>reviews.ExportAsync(companyId,id,ct));
    private async Task<ActionResult<T>> Respond<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl="no-store";
        try { return Ok(await action()); }
        catch(UnauthorizedAccessException) { return Forbid(); }
        catch(KeyNotFoundException) { return NotFound(); }
        catch(ArgumentException ex) { return Problem(statusCode:400,title:"Invalid review request",detail:ex.Message); }
        catch(MonthlyReviewConflictException ex) { return Problem(statusCode:409,title:"Review changed",detail:ex.Message); }
        catch(MonthlyReviewReproductionException ex) { return Problem(statusCode:422,title:"Review could not be reproduced",detail:ex.Message); }
    }
}
