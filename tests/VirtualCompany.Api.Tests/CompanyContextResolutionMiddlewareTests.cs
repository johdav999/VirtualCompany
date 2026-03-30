using System.Text;
using Microsoft.AspNetCore.Http;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyContextResolutionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_rejects_missing_company_context_for_marked_endpoint()
    {
        var accessor = new RequestCompanyContextAccessor();
        var middleware = new CompanyContextResolutionMiddleware(accessor);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequireCompanyContextAttribute()),
            "company-scoped"));

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(accessor.CompanyId);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("Company context is required for this endpoint.", body);
    }

    [Fact]
    public async Task InvokeAsync_resolves_header_company_context_for_marked_endpoint()
    {
        var accessor = new RequestCompanyContextAccessor();
        var middleware = new CompanyContextResolutionMiddleware(accessor);
        var context = new DefaultHttpContext();
        var companyId = Guid.NewGuid();
        context.Request.Headers[CompanyContextResolutionMiddleware.CompanyHeaderName] = companyId.ToString();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequireCompanyContextAttribute()),
            "company-scoped"));

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Equal(companyId, accessor.CompanyId);
        Assert.False(accessor.IsResolved);
        Assert.Null(accessor.Membership);
    }
}