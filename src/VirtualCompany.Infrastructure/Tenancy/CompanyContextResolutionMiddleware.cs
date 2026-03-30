using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Tenancy;

public sealed class CompanyContextResolutionMiddleware : IMiddleware
{
    public const string CompanyHeaderName = "X-Company-Id";

    private readonly ICompanyContextAccessor _companyContextAccessor;

    public CompanyContextResolutionMiddleware(ICompanyContextAccessor companyContextAccessor)
    {
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var routeCompanyIdValue = context.Request.RouteValues.TryGetValue("companyId", out var routeCompanyId)
            ? routeCompanyId?.ToString()
            : null;

        var headerCompanyIdValue = context.Request.Headers[CompanyHeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(routeCompanyIdValue) && string.IsNullOrWhiteSpace(headerCompanyIdValue))
        {
            await next(context);
            return;
        }

        if (!TryParseCompanyId(routeCompanyIdValue, out var parsedRouteCompanyId) ||
            !TryParseCompanyId(headerCompanyIdValue, out var parsedHeaderCompanyId))
        {
            await WriteBadRequestAsync(context, "Company context must be a valid GUID.");
            return;
        }

        if (parsedRouteCompanyId.HasValue &&
            parsedHeaderCompanyId.HasValue &&
            parsedRouteCompanyId.Value != parsedHeaderCompanyId.Value)
        {
            await WriteBadRequestAsync(context, "Route companyId and X-Company-Id must match when both are supplied.");
            return;
        }

        _companyContextAccessor.SetCompanyId(parsedRouteCompanyId ?? parsedHeaderCompanyId);
        await next(context);
    }

    private static bool TryParseCompanyId(string? rawValue, out Guid? companyId)
    {
        companyId = null;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!Guid.TryParse(rawValue, out var parsed))
        {
            return false;
        }

        companyId = parsed;
        return true;
    }

    private static async Task WriteBadRequestAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid company context",
            Detail = detail
        });
    }
}