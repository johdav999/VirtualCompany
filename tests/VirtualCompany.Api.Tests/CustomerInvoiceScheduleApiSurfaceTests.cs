using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceScheduleApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.ListCustomerInvoiceSchedulesAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.GetCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.PreviewCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.CreateCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.UpdateCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.SubmitCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.ActivateCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.PauseCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.ResumeCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceSchedulesController.EndCustomerInvoiceScheduleAsync), CompanyPolicies.AccountingAdmin)]
    public void Schedule_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceCustomerInvoiceSchedulesController).GetMethod(methodName)!;

        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }
}
