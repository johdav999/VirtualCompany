using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Observability;
using Xunit;
using Xunit.Sdk;

namespace VirtualCompany.Api.Tests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_generates_correlation_id_when_request_header_is_missing()
    {
        var accessor = new RequestCorrelationContextAccessor();
        var middleware = new CorrelationIdMiddleware(
            Options.Create(new ObservabilityOptions()),
            accessor,
            new RequestCompanyContextAccessor(),
            new TestCurrentUserAccessor(),
            new ScopeCapturingLogger<CorrelationIdMiddleware>());
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.False(string.IsNullOrWhiteSpace(accessor.CorrelationId));
        Assert.Equal(accessor.CorrelationId, context.Response.Headers["X-Correlation-ID"].ToString());
        Assert.Equal(accessor.CorrelationId, CorrelationIdMiddleware.GetCorrelationId(context));
    }

    [Fact]
    public async Task InvokeAsync_uses_incoming_correlation_id_when_present()
    {
        const string correlationId = "incoming-correlation-id";
        var accessor = new RequestCorrelationContextAccessor();
        var middleware = new CorrelationIdMiddleware(
            Options.Create(new ObservabilityOptions()),
            accessor,
            new RequestCompanyContextAccessor(),
            new TestCurrentUserAccessor(),
            new ScopeCapturingLogger<CorrelationIdMiddleware>());
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = correlationId;

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(correlationId, accessor.CorrelationId);
        Assert.Equal(correlationId, context.Response.Headers["X-Correlation-ID"].ToString());
        Assert.Equal(correlationId, context.TraceIdentifier);
    }

    [Fact]
    public async Task InvokeAsync_enriches_logs_with_company_context_when_it_becomes_available()
    {
        var correlationAccessor = new RequestCorrelationContextAccessor();
        var companyContextAccessor = new RequestCompanyContextAccessor();
        var currentUserAccessor = new TestCurrentUserAccessor { UserId = Guid.NewGuid() };
        var logger = new ScopeCapturingLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(
            Options.Create(new ObservabilityOptions()),
            correlationAccessor,
            companyContextAccessor,
            currentUserAccessor,
            logger);
        var context = new DefaultHttpContext();
        var companyId = Guid.NewGuid();

        await middleware.InvokeAsync(context, _ =>
        {
            companyContextAccessor.SetCompanyId(companyId);
            logger.LogInformation("Tenant-aware log entry.");
            return Task.CompletedTask;
        });

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(correlationAccessor.CorrelationId, AssertScopeValue(entry.Scope, "CorrelationId"));
        Assert.Equal(companyId, AssertScopeValue(entry.Scope, "CompanyId"));
        Assert.Equal(currentUserAccessor.UserId, AssertScopeValue(entry.Scope, "UserId"));
    }

    [Fact]
    public async Task InvokeAsync_omits_company_context_when_execution_scope_is_not_tenant_aware()
    {
        var logger = new ScopeCapturingLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(
            Options.Create(new ObservabilityOptions()),
            new RequestCorrelationContextAccessor(),
            new RequestCompanyContextAccessor(),
            new TestCurrentUserAccessor(),
            logger);

        await middleware.InvokeAsync(new DefaultHttpContext(), _ =>
        {
            logger.LogInformation("Non-tenant log entry.");
            return Task.CompletedTask;
        });

        var entry = Assert.Single(logger.Entries);
        Assert.True(entry.Scope.ContainsKey("CorrelationId"));
        Assert.False(entry.Scope.ContainsKey("CompanyId"));
    }

    private static object? AssertScopeValue(IReadOnlyDictionary<string, object?> scope, string key) =>
        scope.TryGetValue(key, out var value) ? value : throw new XunitException($"Expected scope value '{key}'.");
}
