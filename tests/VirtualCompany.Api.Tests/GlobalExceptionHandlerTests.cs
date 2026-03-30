using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Observability;
using Xunit;
using Xunit.Sdk;

namespace VirtualCompany.Api.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_returns_safe_problem_details_with_trace_and_correlation_ids()
    {
        var logger = new ScopeCapturingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(
            logger,
            Options.Create(new ObservabilityOptions()));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test/failure";
        context.TraceIdentifier = "trace-123";
        context.Items[CorrelationIdMiddleware.HttpContextItemKey] = "corr-123";
        var companyContextAccessor = new RequestCompanyContextAccessor();
        var companyId = Guid.NewGuid();
        companyContextAccessor.SetCompanyId(companyId);
        context.RequestServices = new ServiceCollection()
            .AddSingleton<VirtualCompany.Application.Auth.ICompanyContextAccessor>(companyContextAccessor)
            .AddSingleton<VirtualCompany.Application.Auth.ICurrentUserAccessor>(new TestCurrentUserAccessor())
            .BuildServiceProvider();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("sensitive database failure"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("corr-123", context.Response.Headers["X-Correlation-ID"].ToString());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();

        using var json = JsonDocument.Parse(payload);
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("/api/test/failure", json.RootElement.GetProperty("instance").GetString());
        Assert.Equal("corr-123", json.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("trace-123", json.RootElement.GetProperty("traceId").GetString());
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive database failure", payload);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Contains(nameof(InvalidOperationException), entry.Message);
        Assert.Equal("corr-123", AssertScopeValue(entry.Scope, "CorrelationId"));
        Assert.Equal("trace-123", AssertScopeValue(entry.Scope, "TraceId"));
        Assert.Equal(companyId, AssertScopeValue(entry.Scope, "CompanyId"));
        Assert.Equal("POST", AssertScopeValue(entry.Scope, "RequestMethod"));
        Assert.Equal("/api/test/failure", AssertScopeValue(entry.Scope, "RequestPath"));
    }

    [Fact]
    public async Task TryHandleAsync_maps_key_not_found_to_safe_not_found_problem_details()
    {
        var logger = new ScopeCapturingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(
            logger,
            Options.Create(new ObservabilityOptions()));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/test/missing";
        context.TraceIdentifier = "trace-404";
        context.Items[CorrelationIdMiddleware.HttpContextItemKey] = "corr-404";

        var handled = await handler.TryHandleAsync(
            context,
            new KeyNotFoundException("sensitive missing record"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();

        using var json = JsonDocument.Parse(payload);
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("corr-404", json.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("trace-404", json.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("sensitive missing record", payload);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
    }

    [Fact]
    public async Task TryHandleAsync_maps_validation_exceptions_to_validation_problem_details()
    {
        var logger = new ScopeCapturingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(
            logger,
            Options.Create(new ObservabilityOptions()));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "trace-validation";
        context.Items[CorrelationIdMiddleware.HttpContextItemKey] = "corr-validation";

        var handled = await handler.TryHandleAsync(
            context,
            new VirtualCompany.Application.Companies.CompanyMembershipAdministrationValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is required."]
            }),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var json = JsonDocument.Parse(await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync());
        Assert.Equal("Validation failed", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Email is required.", json.RootElement.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    private static object? AssertScopeValue(IReadOnlyDictionary<string, object?> scope, string key) =>
        scope.TryGetValue(key, out var value) ? value : throw new XunitException($"Expected scope value '{key}'.");
}