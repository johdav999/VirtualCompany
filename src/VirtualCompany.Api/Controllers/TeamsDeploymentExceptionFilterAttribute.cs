using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Api.Controllers;
public sealed class TeamsDeploymentExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not TeamsCallControlException exception) return;
        var problem = new ProblemDetails { Status = 409, Title = "Teams operation needs attention", Detail = exception.Message };
        problem.Extensions["code"] = exception.Code;
        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        context.Result = new ConflictObjectResult(problem);
        context.ExceptionHandled = true;
    }
}
