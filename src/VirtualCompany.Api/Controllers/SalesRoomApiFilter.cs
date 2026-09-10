using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Api.Controllers;
public sealed class SalesRoomApiFilter : IActionFilter, IExceptionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    { context.HttpContext.Response.Headers.CacheControl = "no-store"; context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer"; context.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff"; }
    public void OnActionExecuted(ActionExecutedContext context) { }
    public void OnException(ExceptionContext context)
    {
        var (status, code) = context.Exception switch
        {
            SalesRoomAccessException e => (e.Status, e.Code),
            SalesRoomAgentException e => (e.StatusCode, e.Code),
            SalesPresentationRuntimeConflictException e => (409, e.Code),
            SalesRoomMediaException e => (503, e.Code),
            CalendarProviderException e => (503,e.Code),
            KeyNotFoundException => (404,"invitation_not_found"),
            DbUpdateConcurrencyException => (409, "version_conflict"),
            ArgumentException => (400, "invalid_room_request"),
            _ => (0, "")
        };
        if (status == 0) return;
        context.Result = new ObjectResult(StableProblemDetails.Create(context.HttpContext, status, code, "Browser meeting request unavailable", "The request could not be completed. Refresh the room status or request a new invitation.")) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
