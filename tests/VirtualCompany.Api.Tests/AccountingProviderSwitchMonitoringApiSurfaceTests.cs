using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchMonitoringApiSurfaceTests
{
    [Fact]
    public void Monitoring_reads_are_view_scoped_and_every_mutation_requires_accounting_admin()
    {
        AssertPolicy(nameof(InternalFinanceController.GetAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingView);
        AssertPolicy(nameof(InternalFinanceController.GetAccountingProviderSwitchOperationsAsync), CompanyPolicies.AccountingView);
        AssertPolicy(nameof(InternalFinanceController.RunAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.RetryAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.AcceptAccountingProviderSwitchMonitoringExceptionAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.RequestAccountingProviderSwitchMonitoringClosureAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.CloseAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.CreateCorrectiveAccountingProviderSwitchAsync), CompanyPolicies.AccountingAdmin);
    }

    [Fact]
    public void Monitoring_routes_and_commands_are_explicitly_company_and_switch_scoped()
    {
        var methods = typeof(InternalFinanceController).GetMethods().Where(x =>
            x.Name.Contains("AccountingProviderSwitchMonitoring", StringComparison.Ordinal) ||
            x.Name == nameof(InternalFinanceController.CreateCorrectiveAccountingProviderSwitchAsync)).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods.Where(x => x.Name != nameof(InternalFinanceController.GetAccountingProviderSwitchOperationsAsync)), method =>
        {
            Assert.Contains("provider-switches/{switchId:guid}/monitoring",
                method.GetCustomAttributes<HttpMethodAttribute>().Single().Template, StringComparison.Ordinal);
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
