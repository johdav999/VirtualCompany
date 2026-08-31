using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class CloseComplianceReleaseReadinessApiSurfaceTests
{
    [Fact]
    public void Readiness_endpoint_is_company_scoped_and_requires_accounting_administrator()
    {
        var controller = typeof(CloseComplianceReleaseReadinessController);
        var route = Assert.Single(controller.GetCustomAttributes<RouteAttribute>());
        var authorization = Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("api/companies/{companyId:guid}/finance/close-compliance-release-readiness", route.Template);
        Assert.Equal(CompanyPolicies.AccountingAdmin, authorization.Policy);
        Assert.NotEmpty(controller.GetCustomAttributes<RequireCompanyContextAttribute>());
    }

    [Fact]
    public void Readiness_endpoint_exposes_only_a_get_operation()
    {
        var publicActions = typeof(CloseComplianceReleaseReadinessController).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpGetAttribute>().Any())
            .ToArray();

        var action = Assert.Single(publicActions);
        Assert.Equal(nameof(CloseComplianceReleaseReadinessController.GetAsync), action.Name);
        Assert.NotEmpty(action.GetCustomAttributes<HttpGetAttribute>());
    }
}
