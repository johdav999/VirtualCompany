using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class GlobalExceptionHandlingIntegrationTests
{
    [Fact]
    public async Task Unhandled_exception_returns_safe_problem_details_response()
    {
        using var factory = new ThrowingCurrentUserCompanyFactory(
            () => new InvalidOperationException("sensitive database failure"));
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString(), StringComparison.OrdinalIgnoreCase);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);

        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("/api/auth/me", json.RootElement.GetProperty("instance").GetString());
        Assert.DoesNotContain("sensitive database failure", payload);

        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.Equal(correlationId, json.RootElement.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Empty(await dbContext.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Known_exception_returns_mapped_problem_details_response()
    {
        using var factory = new ThrowingCurrentUserCompanyFactory(
            () => new KeyNotFoundException("sensitive missing record"));
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString(), StringComparison.OrdinalIgnoreCase);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);

        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("/api/auth/me", json.RootElement.GetProperty("instance").GetString());
        Assert.DoesNotContain("sensitive missing record", payload);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    private static HttpClient CreateAuthenticatedClient(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "exception-user");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "exception.user@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Exception User");
        return client;
    }

    private sealed class ThrowingCurrentUserCompanyFactory : TestWebApplicationFactory
    {
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingCurrentUserCompanyFactory(Func<Exception> exceptionFactory)
        {
            _exceptionFactory = exceptionFactory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICurrentUserCompanyService>();
                services.AddScoped<ICurrentUserCompanyService>(_ =>
                    new ThrowingCurrentUserCompanyService(_exceptionFactory));
            });
        }
    }

    private sealed class ThrowingCurrentUserCompanyService : ICurrentUserCompanyService
    {
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingCurrentUserCompanyService(Func<Exception> exceptionFactory)
        {
            _exceptionFactory = exceptionFactory;
        }

        public Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken) =>
            Task.FromException<CurrentUserDto?>(_exceptionFactory());

        public Task<CurrentUserContextDto?> GetCurrentUserContextAsync(CancellationToken cancellationToken) =>
            Task.FromException<CurrentUserContextDto?>(_exceptionFactory());

        public Task<IReadOnlyList<CompanyMembershipDto>> GetMembershipsAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<CompanyMembershipDto>>(_exceptionFactory());

        public Task<ResolvedCompanyContextDto?> GetResolvedActiveCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromException<ResolvedCompanyContextDto?>(_exceptionFactory());

        public Task<CompanyAccessDto?> GetCompanyAccessAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromException<CompanyAccessDto?>(_exceptionFactory());

        public Task<CompanyDashboardEntryDto?> GetDashboardEntryAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromException<CompanyDashboardEntryDto?>(_exceptionFactory());

        public Task<bool> CanAccessCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromException<bool>(_exceptionFactory());
    }
}