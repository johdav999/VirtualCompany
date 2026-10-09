using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class AuditPackageAuthorizationTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceAuditPackagesController.RequestAuditPackageAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceAuditPackagesController.ApproveAuditPackageAsync), CompanyPolicies.FinanceApproval)]
    [InlineData(nameof(InternalFinanceAuditPackagesController.AuthorizeAuditPackageDownloadAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceAuditPackagesController.DownloadAuditPackageAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceAuditPackagesController.VerifyAuditPackageAsync), CompanyPolicies.AccountingView)]
    public void Sensitive_audit_package_routes_require_explicit_server_policy(string methodName, string expectedPolicy)
    {
        var method = typeof(InternalFinanceAuditPackagesController).GetMethods().Single(x => x.Name == methodName);
        var authorization = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(expectedPolicy, authorization.Policy);
    }
}
