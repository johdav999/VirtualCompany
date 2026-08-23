using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchCutoverApiSurfaceTests
{
    [Fact]
    public void Cutover_mutations_require_accounting_admin_and_reads_require_accounting_view()
    {
        AssertPolicy(nameof(InternalFinanceController.ScheduleAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.StartAccountingProviderSwitchFreezeAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.RequestAccountingProviderSwitchActivationApprovalAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.ActivateAccountingProviderSwitchAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.CancelAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.ResumeAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.RecoverAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceController.GetLatestAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingView);
        AssertPolicy(nameof(InternalFinanceController.GetAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingView);
    }

    [Fact]
    public void Cutover_routes_are_company_switch_and_execution_scoped()
    {
        var methodNames = new[]
        {
            nameof(InternalFinanceController.ScheduleAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceController.StartAccountingProviderSwitchFreezeAsync),
            nameof(InternalFinanceController.RequestAccountingProviderSwitchActivationApprovalAsync),
            nameof(InternalFinanceController.ActivateAccountingProviderSwitchAsync),
            nameof(InternalFinanceController.CancelAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceController.ResumeAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceController.RecoverAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceController.GetLatestAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceController.GetAccountingProviderSwitchCutoverAsync)
        };
        var methods = methodNames.Select(name => typeof(InternalFinanceController).GetMethod(name)
            ?? throw new InvalidOperationException($"{name} was not found.")).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var route = method.GetCustomAttributes<HttpMethodAttribute>().Single().Template;
            Assert.Contains("provider-switches/{switchId:guid}/cutovers", route, StringComparison.Ordinal);
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
