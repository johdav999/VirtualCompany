using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class AuditPackageAuthorizationTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceController.RequestAuditPackageAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.ApproveAuditPackageAsync), CompanyPolicies.FinanceApproval)]
    [InlineData(nameof(InternalFinanceController.AuthorizeAuditPackageDownloadAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.DownloadAuditPackageAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.VerifyAuditPackageAsync), CompanyPolicies.AccountingView)]
    public void Sensitive_audit_package_routes_require_explicit_server_policy(string methodName, string expectedPolicy)
    {
        var method = typeof(InternalFinanceController).GetMethods().Single(x => x.Name == methodName);
        var authorization = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(expectedPolicy, authorization.Policy);
    }
}
