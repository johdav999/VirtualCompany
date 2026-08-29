using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class TreasuryWorkspaceAuthorizationTests
{
    [Fact]
    public void Daily_treasury_read_is_company_scoped_and_requires_finance_view()
    {
        var type = typeof(TreasuryWorkspaceController);

        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => attribute.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());
        Assert.Equal("api/companies/{companyId:guid}/finance/treasury-workspace",
            type.GetCustomAttribute<RouteAttribute>()?.Template);
        var method = Assert.Single(type.GetMethods(), candidate =>
            candidate.Name == nameof(TreasuryWorkspaceController.GetAsync));
        Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
    }
}
