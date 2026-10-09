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
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.ScheduleAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.StartAccountingProviderSwitchFreezeAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.RequestAccountingProviderSwitchActivationApprovalAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.ActivateAccountingProviderSwitchAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.CancelAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.ResumeAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.RecoverAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingAdmin);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.GetLatestAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingView);
        AssertPolicy(nameof(InternalFinanceAccountingProviderSwitchCutoverController.GetAccountingProviderSwitchCutoverAsync), CompanyPolicies.AccountingView);
    }

    [Fact]
    public void Cutover_routes_are_company_switch_and_execution_scoped()
    {
        var methodNames = new[]
        {
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.ScheduleAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.StartAccountingProviderSwitchFreezeAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.RequestAccountingProviderSwitchActivationApprovalAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.ActivateAccountingProviderSwitchAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.CancelAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.ResumeAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.RecoverAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.GetLatestAccountingProviderSwitchCutoverAsync),
            nameof(InternalFinanceAccountingProviderSwitchCutoverController.GetAccountingProviderSwitchCutoverAsync)
        };
        var methods = methodNames.Select(name => typeof(InternalFinanceAccountingProviderSwitchCutoverController).GetMethod(name)
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
        var method = typeof(InternalFinanceAccountingProviderSwitchCutoverController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        Assert.Equal(expectedPolicy, method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }
}
