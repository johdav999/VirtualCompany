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
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.GetAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingView);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.GetAccountingProviderSwitchOperationsAsync), CompanyPolicies.AccountingView);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.RunAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.RetryAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.AcceptAccountingProviderSwitchMonitoringExceptionAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.RequestAccountingProviderSwitchMonitoringClosureAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.CloseAccountingProviderSwitchMonitoringAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchMonitoringController.CreateCorrectiveAccountingProviderSwitchAsync), CompanyPolicies.AccountingAdmin);
    }

    [Fact]
    public void Monitoring_routes_and_commands_are_explicitly_company_and_switch_scoped()
    {
        var methods = typeof(InternalFinanceAccountingProviderSwitchMonitoringController).GetMethods().Where(x =>
            x.Name.Contains("AccountingProviderSwitchMonitoring", StringComparison.Ordinal) ||
            x.Name == nameof(InternalFinanceAccountingProviderSwitchMonitoringController.CreateCorrectiveAccountingProviderSwitchAsync)).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods.Where(x => x.Name != nameof(InternalFinanceAccountingProviderSwitchMonitoringController.GetAccountingProviderSwitchOperationsAsync)), method =>
        {
            Assert.Contains("provider-switches/{switchId:guid}/monitoring",
                method.GetCustomAttributes<HttpMethodAttribute>().Single().Template, StringComparison.Ordinal);
            Assert.Contains(method.GetParameters(), parameter => parameter.Name == "companyId");
            Assert.Contains(method.GetParameters(), parameter => parameter.Name == "switchId");
        });
    }

    private static void AssertPolicy(string methodName, string expectedPolicy)
    {
        var method = typeof(InternalFinanceAccountingProviderSwitchMonitoringController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        Assert.Equal(expectedPolicy, method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }
}
