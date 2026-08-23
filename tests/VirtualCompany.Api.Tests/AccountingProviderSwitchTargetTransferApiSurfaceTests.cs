using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchTargetTransferApiSurfaceTests
{
    [Fact]
    public void Target_transfer_mutations_require_accounting_admin_and_reads_require_accounting_view()
    {
        AssertPolicy(nameof(InternalFinanceController.StartAccountingProviderSwitchTargetTransferAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.ReplayAccountingProviderSwitchTargetTransferAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.ReconcileAccountingProviderSwitchTargetTransferItemAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.GetLatestAccountingProviderSwitchTargetTransferAsync), CompanyPolicies.AccountingView);
        AssertPolicy(nameof(InternalFinanceController.GetAccountingProviderSwitchTargetTransferAsync), CompanyPolicies.AccountingView);
    }

    [Fact]
    public void Target_transfer_routes_are_company_and_switch_scoped()
    {
        var methods = typeof(InternalFinanceController).GetMethods()
            .Where(x => x.Name.Contains("AccountingProviderSwitchTargetTransfer", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var route = method.GetCustomAttributes<HttpMethodAttribute>().Single().Template;
            Assert.Contains("provider-switches/{switchId:guid}/target-transfer-batches", route, StringComparison.Ordinal);
            Assert.Contains(method.GetParameters(), parameter => parameter.Name == "companyId");
            Assert.Contains(method.GetParameters(), parameter => parameter.Name == "switchId");
        });
    }

    private static void AssertPolicy(string methodName, string expectedPolicy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        Assert.Equal(expectedPolicy, method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }
}
