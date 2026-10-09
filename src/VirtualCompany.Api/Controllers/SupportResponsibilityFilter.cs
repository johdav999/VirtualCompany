using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

public sealed class SupportResponsibilityFilter(ITodayWorkspaceLensResolver access, ICompanyContextAccessor context) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
    {
        if (context.CompanyId is not Guid companyId) { actionContext.Result = new ForbidResult(); return; }
        var scope = await access.ResolveAsync(companyId, TodayWorkspaceLenses.Customers, actionContext.HttpContext.RequestAborted);
        if (!scope.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Customers)) { actionContext.Result = new ForbidResult(); return; }
        var result=await next();
        if(result.Exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            result.ExceptionHandled=true;
            result.Result=new ObjectResult(new ProblemDetails{Status=409,Title="This Support case changed.",Detail="Reload the case before saving another change."}){StatusCode=409};
        }
    }
}
