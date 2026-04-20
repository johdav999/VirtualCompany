using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Tenancy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireCompanyContextAttribute : Attribute
{
}

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
        var requiresCompanyContext = context.GetEndpoint()?.Metadata.GetMetadata<RequireCompanyContextAttribute>() is not null;
        var routeCompanyIdValue = context.Request.RouteValues.TryGetValue("companyId", out var routeCompanyId)
            ? routeCompanyId?.ToString()
            : null;
        routeCompanyIdValue ??= context.Request.RouteValues.TryGetValue("tenantId", out var routeTenantId)
            ? routeTenantId?.ToString()
            : null;
        var queryCompanyIdValue = context.Request.Query.TryGetValue("companyId", out var queryCompanyId)
            ? queryCompanyId.FirstOrDefault()
            : null;

        var headerCompanyIdValue = context.Request.Headers[CompanyHeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(routeCompanyIdValue) &&
            string.IsNullOrWhiteSpace(queryCompanyIdValue) &&
            string.IsNullOrWhiteSpace(headerCompanyIdValue))
        {
            if (requiresCompanyContext)
            {
                await WriteBadRequestAsync(context, "Company context is required for this endpoint.");
                return;
            }
            await next(context);
            return;
        }

        if (!TryParseCompanyId(routeCompanyIdValue, out var parsedRouteCompanyId) ||
            !TryParseCompanyId(queryCompanyIdValue, out var parsedQueryCompanyId) ||
            !TryParseCompanyId(headerCompanyIdValue, out var parsedHeaderCompanyId))
        {
            await WriteBadRequestAsync(context, "Company context must be a valid GUID.");
            return;
        }

        if (parsedRouteCompanyId.HasValue &&
            parsedQueryCompanyId.HasValue &&
            parsedRouteCompanyId.Value != parsedQueryCompanyId.Value)
        {
            await WriteBadRequestAsync(context, "Route companyId and query companyId must match when both are supplied.");
            return;
        }

        if ((parsedRouteCompanyId ?? parsedQueryCompanyId).HasValue &&
            parsedHeaderCompanyId.HasValue &&
            (parsedRouteCompanyId ?? parsedQueryCompanyId)!.Value != parsedHeaderCompanyId.Value)
        {
            await WriteBadRequestAsync(context, "Route companyId and X-Company-Id must match when both are supplied.");
            return;
        }

        _companyContextAccessor.SetCompanyId(parsedRouteCompanyId ?? parsedQueryCompanyId ?? parsedHeaderCompanyId);
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