using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceCorrectionApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceController.EvaluateCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.ListCustomerInvoiceCorrectionsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GetCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.ProposeCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.ExecuteCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.ReconcileCustomerInvoiceRefundAsync), CompanyPolicies.AccountingAdmin)]
    public void Correction_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName)!;
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }
}
