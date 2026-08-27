using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceScheduleApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceController.ListCustomerInvoiceSchedulesAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GetCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.PreviewCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.CreateCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.UpdateCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.SubmitCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.ActivateCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.PauseCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.ResumeCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.EndCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    public void Schedule_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName)!;

        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }
}
